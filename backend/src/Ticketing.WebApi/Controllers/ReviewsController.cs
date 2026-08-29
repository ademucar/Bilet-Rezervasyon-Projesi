using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ticketing.Application.Common.Pagination;
using Ticketing.Application.Features.Events;
using Ticketing.Application.Features.Favorites;
using Ticketing.Application.Features.Reviews;

namespace Ticketing.WebApi.Controllers;

/// <summary>
/// Etkinlik yorumlari. PDF Sprint 12.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/events/{eventId:guid}/reviews")]
public sealed class EventReviewsController : ApiControllerBase
{
    /// <summary>
    /// Etkinlige yorum ve puan ekler.
    /// PDF: POST /api/v1/events/{eventId}/reviews
    /// </summary>
    /// <remarks>
    /// Uygulanan kurallar:
    ///   - Puan 1-5 arasında olmalıdır
    ///   - Etkinlik TAMAMLANMIS olmalıdır
    ///   - Kullanıcının GECERLI bileti olmalıdır (Active veya Used)
    ///   - Etkinlik başına TEK yorum
    /// </remarks>
    /// <response code="201">Yorum oluşturuldu.</response>
    /// <response code="403">Geçerli bilet yok.</response>
    /// <response code="409">Etkinlik tamamlanmadi veya zaten yorum var.</response>
    [HttpPost]
    [Authorize]
    [ProducesResponseType<Guid>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        Guid eventId,
        [FromBody] CreateReviewRequest request,
        CancellationToken cancellationToken)
    {
        // eventId ADRESTEN aliniyor, govdeden DEĞİL.
        //
        // Govdede de olsaydı ikisi CELISEBILIRDI: adres A etkinligini
        // gosterirken govde B'yi soyleyebilirdi. Hangisinin kazandigi
        // belirsiz kalır ve yetki kontrolü yanlış etkinlik uzerinde
        // calisabilirdi.
        var command = new CreateReviewCommand(eventId, request.Rating, request.Comment);

        var result = await Sender.Send(command, cancellationToken).ConfigureAwait(false);

        return HandleCreated(
            result,
            $"/api/v1/events/{eventId}/reviews");
    }

    /// <summary>
    /// Etkinligin yorumlarini ve puan ozetini döndürür.
    /// PDF: GET /api/v1/events/{eventId}/reviews
    /// </summary>
    /// <remarks>
    /// ANONIM erisime açık: yorumlar herkese görünür olmalı. Bilet
    /// almayi dusunen kullanıcı, giriş yapmadan önce baskalarinin ne
    /// dedigini gorebilmeli.
    ///
    /// Gizlenmis (admin tarafından kaldirilmis) yorumlar donmuyor.
    /// </remarks>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType<EventReviewsResult>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetReviews(
        Guid eventId,
        [FromQuery] GetEventReviewsQuery query,
        CancellationToken cancellationToken)
    {
        // Adresten gelen eventId, sorgu dizesinden geleni EZIYOR.
        //
        // Istemci ?eventId=... yazarak başka bir etkinliğin
        // yorumlarini isteyebilirdi. Zararsiz görünüyor (yorumlar
        // zaten açık) ama adres ile sonucun uyusmamasi her zaman bir
        // hata kaynagi.
        var effective = query with { EventId = eventId };

        return HandleResult(await Sender.Send(effective, cancellationToken).ConfigureAwait(false));
    }
}

/// <summary>
/// Tekil yorum islemleri. PDF Sprint 12.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/reviews")]
public sealed class ReviewsController : ApiControllerBase
{
    /// <summary>
    /// Yorumu günceller. PDF: PUT /api/v1/reviews/{id}
    /// </summary>
    /// <remarks>
    /// PDF: "Kullanıcı yalnızca kendi yorumunu düzenleyebilir."
    /// Sahiplik kontrolü handler içinde; baskasinin yorumu için 403 döner.
    /// </remarks>
    [HttpPut("{id:guid}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateReviewRequest request,
        CancellationToken cancellationToken)
        => HandleResult(await Sender
            .Send(new UpdateReviewCommand(id, request.Rating, request.Comment), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>
    /// Yorumu kaldirir. PDF: DELETE /api/v1/reviews/{id}
    /// </summary>
    /// <remarks>
    /// IKI FARKLI DAVRANIS:
    ///   Kullanıcı kendi yorumunu siler -> soft delete
    ///   Admin baskasinin yorumunu siler -> GIZLENIR (denetim izi kalır)
    ///
    /// Gerekce DeleteReviewCommandHandler içinde ayrintili yazili.
    /// </remarks>
    [HttpDelete("{id:guid}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Delete(
        Guid id,
        [FromQuery] string? reason,
        CancellationToken cancellationToken)
        => HandleResult(await Sender
            .Send(new DeleteReviewCommand(id, reason), cancellationToken)
            .ConfigureAwait(false));
}

/// <summary>
/// Favori islemleri. PDF Sprint 12.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/events/{eventId:guid}/favorite")]
public sealed class EventFavoriteController : ApiControllerBase
{
    /// <summary>PDF: POST /api/v1/events/{eventId}/favorite</summary>
    /// <remarks>
    /// IDEMPOTENT: zaten favorideyse de 204 döner. Kullanıcının
    /// istedigi sonuç (etkinlik favorilerimde olsun) gerceklesmis
    /// durumda; hata dondurmek anlamsiz olurdu.
    /// </remarks>
    [HttpPost]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Add(Guid eventId, CancellationToken cancellationToken)
        => HandleResult(await Sender
            .Send(new AddFavoriteCommand(eventId), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>PDF: DELETE /api/v1/events/{eventId}/favorite</summary>
    /// <remarks>IDEMPOTENT: favoride degilse de 204 döner.</remarks>
    [HttpDelete]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Remove(Guid eventId, CancellationToken cancellationToken)
        => HandleResult(await Sender
            .Send(new RemoveFavoriteCommand(eventId), cancellationToken)
            .ConfigureAwait(false));
}

/// <summary>Kullanıcının favorileri. PDF: GET /api/v1/users/me/favorites</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/users/me")]
public sealed class MyFavoritesController : ApiControllerBase
{
    /// <summary>
    /// Kullanıcının favori etkinlikleri.
    /// </summary>
    /// <response code="200">Favori etkinlik listesi.</response>
    [HttpGet("favorites")]
    [Authorize]
    [ProducesResponseType<IReadOnlyList<EventListItem>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyFavorites(CancellationToken cancellationToken)
        => HandleResult(await Sender
            .Send(new GetMyFavoritesQuery(), cancellationToken)
            .ConfigureAwait(false));
}

public sealed record CreateReviewRequest(int Rating, string Comment);

public sealed record UpdateReviewRequest(int Rating, string Comment);
