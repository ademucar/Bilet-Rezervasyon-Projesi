using Asp.Versioning;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ticketing.Application.Common.Pagination;
using Ticketing.Application.Features.Events;
using Ticketing.Domain.Entities;
using Ticketing.WebApi.Security;

namespace Ticketing.WebApi.Controllers;

/// <summary>
/// Etkinlik ve oturum yonetimi. PDF Sprint 5.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/events")]
public sealed class EventsController : ApiControllerBase
{
    /// <summary>
    /// Etkinlikleri sayfali listeler. Sehir, kategori, mekan ve tarih
    /// araligina gore filtrelenebilir.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    // PDF Sprint 15: "Search endpointi" hiz siniri.
    //
    // Bu uc ANONIM erisime acik ve pahali (LIKE sorgusu + JOIN'ler).
    // Kimlik dogrulamasi olmadigi icin kota IP bazli calisiyor.
    [EnableRateLimiting(RateLimitingSetup.Policies.Search)]
    [ProducesResponseType<PagedResult<EventListItem>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetEvents(
        [FromQuery] GetEventsQuery query,
        CancellationToken cancellationToken)
    {
        // ==============================================================
        // GORUNURLUK KARARI SUNUCUDA VERILIYOR
        // ==============================================================
        // IncludeUnpublished, GetEventsQuery uzerinde bir alan ve
        // [FromQuery] ile baglaniyor. Yani istemci
        //     GET /api/v1/events?includeUnpublished=true
        // yazip yayinlanmamis etkinlikleri istemeyi DENEYEBILIR.
        //
        // Bu satir o denemeyi etkisiz kiliyor: gelen deger ne olursa
        // olsun UZERINE YAZILIYOR ve yalnizca gercekten admin olanlar
        // true aliyor.
        //
        // Bu, "istemciden gelen hicbir yetki bilgisine guvenme"
        // ilkesinin somut bir ornegi.
        // ==============================================================
        var effectiveQuery = query with
        {
            IncludeUnpublished = User.IsInRole(Role.Names.Admin)
        };

        return HandleResult(await Sender.Send(effectiveQuery, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Etkinlik kategorileri. PDF Sprint 11.
    /// </summary>
    /// <remarks>
    /// Redis te 24 saat onbellekleniyor. Filtre acilir listesi icin
    /// her sayfa acilisinda cagriliyor; onbellek olmadan gereksiz
    /// veritabani yuku olurdu.
    /// </remarks>
    [HttpGet("categories")]
    [AllowAnonymous]
    [ProducesResponseType<IReadOnlyList<CategoryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCategories(CancellationToken cancellationToken)
        => HandleResult(await Sender
            .Send(new GetCategoriesQuery(), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>
    /// En populer etkinlikler. PDF Sprint 11.
    /// </summary>
    /// <remarks>
    /// Redis te 10 dakika onbellekleniyor.
    ///
    /// Neden ayri bir uc? Ana sayfada gosterilecek ve listeleme
    /// ucundan farkli bir siralama mantigi var (bilet satisi).
    /// Listeye "sortBy=popular" olarak eklemek de mumkundu ama o
    /// zaman filtrelerle birlesince onbellek anahtari patlardi:
    /// sehir + kategori + tarih + populer = binlerce kombinasyon.
    ///
    /// Ayri uc, tek ve sabit bir anahtar demek.
    /// </remarks>
    [HttpGet("popular")]
    [AllowAnonymous]
    [ProducesResponseType<IReadOnlyList<EventListItem>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPopularEvents(
        [FromQuery] int count = 10,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender
            .Send(new GetPopularEventsQuery(count), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>Etkinlik detayini oturumlariyla birlikte dondurur.</summary>
    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    [ProducesResponseType<EventDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetEvent(Guid id, CancellationToken cancellationToken)
    {
        var query = new GetEventByIdQuery(id, User.IsInRole(Role.Names.Admin));

        return HandleResult(await Sender.Send(query, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Yeni etkinlik olusturur. Etkinlik Draft durumunda baslar.
    /// Organizator profili gerektirir.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = AuthenticationSetup.Policies.OrganizerOnly)]
    [ProducesResponseType<Guid>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreateEvent(
        [FromBody] CreateEventCommand command,
        CancellationToken cancellationToken)
    {
        var result = await Sender.Send(command, cancellationToken).ConfigureAwait(false);

        return HandleCreated(result, $"/api/v1/events/{(result.IsSuccess ? result.Value : Guid.Empty)}");
    }

    /// <summary>
    /// Etkinligin duzenlenebilir alanlarini gunceller.
    /// </summary>
    /// <remarks>
    /// PDF is kurali: "Yayina alinmis etkinligin kritik alanlari
    /// KONTROLSUZ degistirilemez."
    ///
    /// Kural iki seviyede isliyor:
    ///
    /// - **Baslik, aciklama, yas siniri**: yayindayken de
    ///   degistirilebilir. Yazim hatasi duzeltmek yasak olmamali.
    ///   Yalnizca iptal edilmis veya tamamlanmis etkinlikte kapali.
    ///
    /// - **Tarihler**: satis BASLADIYSA degistirilemez. Bilet almis
    ///   kullanicilarin altindan tarihi cekmek kabul edilemez;
    ///   o durumda dogru islem etkinligi iptal etmektir.
    ///
    /// Tarih alanlarinin ucu birlikte gonderilmeli veya hicbiri
    /// gonderilmemelidir.
    /// </remarks>
    /// <response code="204">Guncellendi.</response>
    /// <response code="404">Etkinlik bulunamadi.</response>
    /// <response code="422">Satis baslamis; tarihler degistirilemez.</response>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = AuthenticationSetup.Policies.EventOwner)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateEvent(
        Guid id,
        [FromBody] UpdateEventRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // ==========================================================
        // KIMLIK ADRESTEN, GOVDEDEN DEGIL
        // ==========================================================
        // Govdede de bir EventId tasisaydik, ikisi FARKLI olabilirdi:
        // adreste kendi etkinligini, govdede baskasininkini gonderen
        // bir istek EventOwner kontrolunu atlatabilirdi.
        //
        // Yetkilendirme adresteki kimlige bakiyor; komutu da ondan
        // kuruyoruz. Boylece iki kaynak arasinda fark olusamiyor.
        // ==========================================================
        return HandleResult(await Sender
            .Send(
                new UpdateEventCommand(
                    id,
                    request.Title,
                    request.Description,
                    request.MinimumAge,
                    request.EventDate,
                    request.SalesStartDate,
                    request.SalesEndDate),
                cancellationToken)
            .ConfigureAwait(false));
    }

    /// <summary>
    /// Etkinligi siler (soft delete).
    /// </summary>
    /// <remarks>
    /// Yalnizca HIC BILET SATILMAMIS ve aktif rezervasyonu OLMAYAN
    /// etkinlikler silinebilir.
    ///
    /// Bileti olan bir etkinlik icin dogru islem SILMEK degil IPTAL
    /// etmektir (`POST /events/{id}/cancel`): iptal, iade zincirini
    /// ve kullanici bildirimlerini baslatir. Silmek ise o biletleri
    /// sessizce gecersiz kilardi.
    ///
    /// Kayit fiziksel olarak silinmiyor: IsDeleted isaretleniyor ve
    /// global sorgu filtresi gizliyor. Bilet, odeme ve denetim
    /// kayitlari korunuyor.
    /// </remarks>
    /// <response code="204">Silindi.</response>
    /// <response code="404">Etkinlik bulunamadi.</response>
    /// <response code="409">Bileti satilmis veya aktif rezervasyonu var.</response>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AuthenticationSetup.Policies.EventOwner)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteEvent(Guid id, CancellationToken cancellationToken)
        => HandleResult(await Sender
            .Send(new DeleteEventCommand(id), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>
    /// Etkinlige oturum ekler. PDF: POST /api/v1/events/{id}/sessions
    ///
    /// EventOwner policy'si: yalnizca etkinligin sahibi organizator
    /// (veya admin) oturum ekleyebilir.
    /// </summary>
    [HttpPost("{id:guid}/sessions")]
    [Authorize(Policy = AuthenticationSetup.Policies.EventOwner)]
    [ProducesResponseType<Guid>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> AddSession(
        Guid id,
        [FromBody] AddSessionRequest request,
        CancellationToken cancellationToken)
    {
        var command = new AddEventSessionCommand(
            id, request.StartDate, request.EndDate, request.HallId, request.SeatLayoutId);

        var result = await Sender.Send(command, cancellationToken).ConfigureAwait(false);

        return HandleCreated(result, $"/api/v1/events/{id}");
    }

    /// <summary>
    /// Etkinligi admin onayina gonderir.
    /// En az bir oturum ve bir bilet turu gerektirir.
    /// </summary>
    [HttpPost("{id:guid}/submit")]
    [Authorize(Policy = AuthenticationSetup.Policies.EventOwner)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Submit(Guid id, CancellationToken cancellationToken)
        => HandleResult(await Sender
            .Send(new SubmitEventForApprovalCommand(id), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>
    /// Etkinligi yayina alir. PDF: POST /api/v1/events/{id}/publish
    ///
    /// YALNIZCA ADMIN. Organizatorun kendi etkinligini onaylamasi,
    /// onay surecini anlamsiz kilardi.
    /// </summary>
    [HttpPost("{id:guid}/publish")]
    [Authorize(Policy = AuthenticationSetup.Policies.AdminOnly)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Publish(Guid id, CancellationToken cancellationToken)
        => HandleResult(await Sender
            .Send(new PublishEventCommand(id), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>
    /// Etkinligi iptal eder. PDF: POST /api/v1/events/{id}/cancel
    /// Sahibi organizator veya admin yapabilir.
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = AuthenticationSetup.Policies.EventOwner)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Cancel(
        Guid id,
        [FromBody] CancelEventRequest request,
        CancellationToken cancellationToken)
        => HandleResult(await Sender
            .Send(new CancelEventCommand(id, request.Reason), cancellationToken)
            .ConfigureAwait(false));
}

public sealed record AddSessionRequest(
    DateTimeOffset StartDate,
    DateTimeOffset EndDate,
    Guid HallId,
    Guid SeatLayoutId);

public sealed record CancelEventRequest(string? Reason);

/// <summary>Etkinlik guncelleme istegi.</summary>
/// <param name="Title">Etkinlik basligi.</param>
/// <param name="Description">Etkinlik aciklamasi.</param>
/// <param name="MinimumAge">Yas siniri. null = sinir yok.</param>
/// <param name="EventDate">Etkinlik tarihi. Tarihler ucu birlikte gonderilmeli.</param>
/// <param name="SalesStartDate">Satis baslangici.</param>
/// <param name="SalesEndDate">Satis bitisi.</param>
public sealed record UpdateEventRequest(
    string Title,
    string Description,
    int? MinimumAge,
    DateTimeOffset? EventDate,
    DateTimeOffset? SalesStartDate,
    DateTimeOffset? SalesEndDate);
