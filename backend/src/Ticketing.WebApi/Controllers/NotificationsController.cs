using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ticketing.Application.Common.Pagination;
using Ticketing.Application.Features.Notifications;

namespace Ticketing.WebApi.Controllers;

/// <summary>
/// Kullanici bildirimleri. PDF Sprint 14.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/notifications")]
[Authorize]
public sealed class NotificationsController : ApiControllerBase
{
    /// <summary>PDF: GET /api/v1/notifications</summary>
    /// <remarks>
    /// Sayfali. Kullanici yalnizca KENDI bildirimlerini goruyor --
    /// filtre sorgunun icinde, handler'da.
    /// </remarks>
    [HttpGet]
    [ProducesResponseType<PagedResult<NotificationDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetNotifications(
        [FromQuery] GetNotificationsQuery query,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(query, cancellationToken).ConfigureAwait(false));

    /// <summary>PDF: GET /api/v1/notifications/unread-count</summary>
    /// <remarks>
    /// Zil rozetinin veri kaynagi. Yalnizca bir SAYI donuyor --
    /// gerekcesi handler'da yazili.
    /// </remarks>
    [HttpGet("unread-count")]
    [ProducesResponseType<int>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUnreadCount(CancellationToken cancellationToken)
        => HandleResult(await Sender
            .Send(new GetUnreadNotificationCountQuery(), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>PDF: PATCH /api/v1/notifications/{id}/read</summary>
    /// <remarks>
    /// PATCH kullaniyorum, PUT degil.
    ///
    /// PUT "kaynagin TAMAMINI degistir" demek; biz yalnizca tek bir
    /// alani (IsRead) degistiriyoruz. PATCH kismi guncellemenin
    /// dogru fiili -- ve PDF de PATCH yazmis.
    /// </remarks>
    [HttpPatch("{id:guid}/read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken cancellationToken)
        => HandleResult(await Sender
            .Send(new MarkNotificationReadCommand(id), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>PDF: PATCH /api/v1/notifications/read-all</summary>
    /// <returns>Okundu isaretlenen bildirim sayisi.</returns>
    [HttpPatch("read-all")]
    [ProducesResponseType<int>(StatusCodes.Status200OK)]
    public async Task<IActionResult> MarkAllRead(CancellationToken cancellationToken)
        => HandleResult(await Sender
            .Send(new MarkAllNotificationsReadCommand(), cancellationToken)
            .ConfigureAwait(false));
}
