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
/// Etkinlik ve oturum yönetimi. PDF Sprint 5.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/events")]
public sealed class EventsController : ApiControllerBase
{
    /// <summary>
    /// Etkinlikleri sayfali listeler. Şehir, kategori, mekan ve tarih
    /// araligina göre filtrelenebilir.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    // PDF Sprint 15: "Search endpointi" hiz sınırı.
    //
    // Bu uc anonim erisime açık ve pahali (like sorgusu + JOIN'ler).
    // Kimlik dogrulamasi olmadığı için kota IP bazlı çalışıyor.
    [EnableRateLimiting(RateLimitingSetup.Policies.Search)]
    [ProducesResponseType<PagedResult<EventListItem>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetEvents(
        [FromQuery] GetEventsQuery query,
        CancellationToken cancellationToken)
    {
        // Gorunurluk karari sunucuda veriliyor
        //
        // IncludeUnpublished, GetEventsQuery uzerinde bir alan ve
        // [FromQuery] ile bağlanıyor. Yani istemci
        //     GET /api/v1/events?includeUnpublished=true
        // yazip yayinlanmamis etkinlikleri istemeyi DENEYEBILIR.
        //
        // Bu satır o denemeyi etkisiz kiliyor: gelen deger ne olursa
        // olsun uzerine yaziliyor ve yalnızca gerçekten admin olanlar
        // true aliyor.
        //
        // Bu, "istemciden gelen hiçbir yetki bilgisine guvenme"
        // ilkesinin somut bir ornegi.
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
    /// Redis te 24 saat onbellekleniyor. Filtre açılır listesi için
    /// her sayfa acilisinda cagriliyor; önbellek olmadan gereksiz
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
    /// En popüler etkinlikler. PDF Sprint 11.
    /// </summary>
    /// <remarks>
    /// Redis te 10 dakika onbellekleniyor.
    ///
    /// Neden ayrı bir uc? Ana sayfada gösterilecek ve listeleme
    /// ucundan farklı bir sıralama mantığı var (bilet satışı).
    /// Listeye "sortBy=popular" olarak eklemek de mumkundu ama o
    /// zaman filtrelerle birlesince önbellek anahtari patlardi:
    /// şehir + kategori + tarih + popüler = binlerce kombinasyon.
    ///
    /// Ayrı uc, tek ve sabit bir anahtar demek.
    /// </remarks>
    /// <summary>
    /// Organizatorun KENDI etkinlikleri -- taslaklar dahil.
    /// PDF: GET /api/v1/events/mine
    /// </summary>
    /// <remarks>
    /// Yol sirasi onemli: bu satir "{id:guid}" kalibindan ONCE
    /// gelmeli. Sonra gelseydi "mine" bir GUID gibi ayristirilmaya
    /// calisilirdi. Rota kisitlamasi (:guid) yuzunden eslesme
    /// olmazdi ama yine de acik yazmak, ileride kisitlama
    /// kaldirilirsa sessizce bozulmasini engelliyor.
    /// </remarks>
    [HttpGet("mine")]
    [Authorize(Policy = AuthenticationSetup.Policies.OrganizerOnly)]
    [ProducesResponseType<PagedResult<EventListItem>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMyEvents(
        [FromQuery] GetMyEventsQuery query,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(query, cancellationToken).ConfigureAwait(false));

    [HttpGet("popular")]
    [AllowAnonymous]
    [ProducesResponseType<IReadOnlyList<EventListItem>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPopularEvents(
        [FromQuery] int count = 10,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender
            .Send(new GetPopularEventsQuery(count), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>Etkinlik detayını oturumlariyla birlikte döndürür.</summary>
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
    /// Yeni etkinlik oluşturur. Etkinlik Draft durumunda başlar.
    /// Organizatör profili gerektirir.
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
    /// Etkinligin duzenlenebilir alanlarini günceller.
    /// </summary>
    /// <remarks>
    /// PDF is kuralı: "Yayina alinmis etkinliğin kritik alanlari
    /// KONTROLSUZ degistirilemez."
    ///
    /// Kural iki seviyede isliyor:
    ///
    /// - **Başlık, açıklama, yaş sınırı**: yayindayken de
    ///   degistirilebilir. Yazim hatası duzeltmek yasak olmamali.
    ///   Yalnızca iptal edilmiş veya tamamlanmis etkinlikte kapalı.
    ///
    /// - **Tarihler**: satış BASLADIYSA degistirilemez. Bilet almis
    ///   kullanicilarin altindan tarihi cekmek kabul edilemez;
    ///   o durumda doğru işlem etkinligi iptal etmektir.
    ///
    /// Tarih alanlarinin ucu birlikte gonderilmeli veya hicbiri
    /// gonderilmemelidir.
    /// </remarks>
    /// <response code="204">Guncellendi.</response>
    /// <response code="404">Etkinlik bulunamadı.</response>
    /// <response code="422">Satış baslamis; tarihler degistirilemez.</response>
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

        // Kimlik adresten, govdeden değil
        //
        // Govdede de bir EventId tasisaydim, ikisi FARKLI olabilirdi:
        // adreste kendi etkinligini, govdede baskasininkini gonderen
        // bir istek EventOwner kontrolunu atlatabilirdi.
        //
        // Yetkilendirme adresteki kimlige bakiyor; komutu da ondan
        // kuruyorum. Boylece iki kaynak arasında fark olusamiyor.
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
    /// Yalnızca hiç bilet satilmamis ve aktif rezervasyonu olmayan
    /// etkinlikler silinebilir.
    ///
    /// Bileti olan bir etkinlik için doğru işlem silmek değil iptal
    /// etmektir (`POST /events/{id}/cancel`): iptal, iade zincirini
    /// ve kullanıcı bildirimlerini baslatir. Silmek ise o biletleri
    /// sessizce geçersiz kilardi.
    ///
    /// Kayıt fiziksel olarak silinmiyor: IsDeleted isaretleniyor ve
    /// global sorgu filtresi gizliyor. Bilet, ödeme ve denetim
    /// kayitlari korunuyor.
    /// </remarks>
    /// <response code="204">Silindi.</response>
    /// <response code="404">Etkinlik bulunamadı.</response>
    /// <response code="409">Bileti satılmış veya aktif rezervasyonu var.</response>
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
    /// EventOwner policy'si: yalnızca etkinliğin sahibi organizatör
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
    /// Etkinligi admin onayina gönderir.
    /// En az bir oturum ve bir bilet türü gerektirir.
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
    /// Etkinligi yayina alır. PDF: POST /api/v1/events/{id}/publish
    ///
    /// Yalnizca admin. Organizatorun kendi etkinligini onaylamasi,
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
    /// Sahibi organizatör veya admin yapabilir.
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

/// <summary>Etkinlik güncelleme isteği.</summary>
/// <param name="Title">Etkinlik başlığı.</param>
/// <param name="Description">Etkinlik açıklaması.</param>
/// <param name="MinimumAge">Yaş sınırı. null = sinir yok.</param>
/// <param name="EventDate">Etkinlik tarihi. Tarihler ucu birlikte gonderilmeli.</param>
/// <param name="SalesStartDate">Satış baslangici.</param>
/// <param name="SalesEndDate">Satış bitisi.</param>
public sealed record UpdateEventRequest(
    string Title,
    string Description,
    int? MinimumAge,
    DateTimeOffset? EventDate,
    DateTimeOffset? SalesStartDate,
    DateTimeOffset? SalesEndDate);
