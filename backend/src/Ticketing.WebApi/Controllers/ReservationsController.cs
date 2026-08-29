using Asp.Versioning;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ticketing.Application.Features.Reservations;
using Ticketing.Application.Features.Sessions;
using Ticketing.Domain.Enums;
using Ticketing.WebApi.Security;

namespace Ticketing.WebApi.Controllers;

/// <summary>
/// Rezervasyon islemleri. PDF Sprint 7.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/reservations")]
public sealed class ReservationsController : ApiControllerBase
{
    /// <summary>
    /// Koltuk seçip rezervasyon oluşturur. Koltuklar 10 dakika kilitlenir.
    /// </summary>
    /// <remarks>
    /// IDEMPOTENCY: Istegi "Idempotency-Key" header'i ile gonderin.
    /// Aynı anahtarla ikinci kez gonderilirse YENI rezervasyon
    /// olusmaz, ILK rezervasyon döner.
    ///
    /// Bu, kullanıcının butona iki kez basmasi veya agin isteği
    /// tekrarlamasi durumunda cift rezervasyonu engeller.
    /// </remarks>
    /// <response code="201">Rezervasyon oluşturuldu, koltuklar kilitlendi.</response>
    /// <response code="409">Koltuklardan biri az önce başkası tarafından alındı.</response>
    /// <response code="422">Satış kapalı veya bilet limiti aşıldı.</response>
    [HttpPost]
    [Authorize]
    // PDF Sprint 15: "Rezervasyon oluşturma endpointi" hiz sınırı.
    // Bot ile koltuk kapatmayi (scalping) zorlastiriyor.
    [EnableRateLimiting(RateLimitingSetup.Policies.Transaction)]
    [ProducesResponseType<ReservationDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create(
        [FromBody] CreateReservationRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        // Idempotency anahtarini HEADER'dan alıyorum, govdeden değil.
        //
        // Sebep: bu bir PROTOKOL detayidir, is verisi değil. Stripe,
        // AWS gibi saglayicilar da aynı yaklasimi kullaniyor.
        // Govdeye koysaydık her istek modelinde tekrarlamamiz gerekirdi.
        var command = new CreateReservationCommand(
            request.EventSessionId, request.EventSeatIds, idempotencyKey);

        var result = await Sender.Send(command, cancellationToken).ConfigureAwait(false);

        return HandleCreated(
            result,
            $"/api/v1/reservations/{(result.IsSuccess ? result.Value.Id : Guid.Empty)}");
    }

    /// <summary>Rezervasyon detayı. Yalnızca sahibi görebilir.</summary>
    [HttpGet("{id:guid}")]
    // ==============================================================
    // ReservationOwner -- SPRINT 19'DA BAGLANDI
    // ==============================================================
    // Handler zaten sahiplik filtreliyordu (ve 404 donuyordu).
    // Politika IKINCI bir katman: birinin unutulmasi digerini
    // geçersiz kilmiyor.
    //
    // Politika reddi de 404 döner (ResourceOwnerResultHandler):
    // 403 "bu rezervasyon VAR ama senin değil" bilgisini
    // sizdirirdi.
    // ==============================================================
    [Authorize(Policy = AuthenticationSetup.Policies.ReservationOwner)]
    [ProducesResponseType<ReservationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetReservationQuery(id), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>Rezervasyonu iptal eder ve koltukları serbest birakir.</summary>
    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = AuthenticationSetup.Policies.ReservationOwner)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Cancel(
        Guid id,
        [FromBody] CancelReservationRequest? request,
        CancellationToken cancellationToken)
        => HandleResult(await Sender
            .Send(new CancelReservationCommand(id, request?.Reason), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>
    /// Rezervasyon suresini uzatir. Bir kez ve en fazla 5 dakika.
    /// </summary>
    [HttpPost("{id:guid}/extend")]
    [Authorize(Policy = AuthenticationSetup.Policies.ReservationOwner)]
    [ProducesResponseType<ReservationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Extend(Guid id, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new ExtendReservationCommand(id), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>
    /// Süresi dolmuş rezervasyonları temizler.
    /// Sprint 9'da Hangfire ile dakikada bir otomatik calisacak;
    /// şimdilik admin elle tetikleyebiliyor.
    /// </summary>
    [HttpPost("expire-overdue")]
    [Authorize(Policy = AuthenticationSetup.Policies.AdminOnly)]
    [ProducesResponseType<int>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ExpireOverdue(CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new ExpireReservationsCommand(), cancellationToken)
            .ConfigureAwait(false));
}

/// <summary>Kullanıcının kendi rezervasyonları. PDF: GET /api/v1/users/me/reservations</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/users/me")]
public sealed class MyReservationsController : ApiControllerBase
{
    /// <summary>
    /// Kullanıcının kendi rezervasyonları.
    /// </summary>
    /// <remarks>
    /// Yalnızca isteği yapan kullanıcının rezervasyonları döner;
    /// kullanıcı kimliği TOKEN'DAN okunuyor, istekten değil.
    ///
    /// Adreste bir kullanıcı kimliği tasisaydik, birinin baskasinin
    /// kimligini yazip onun rezervasyonlarini gormesini engellemek
    /// için ayrıca kontrol yazmak gerekirdi.
    /// </remarks>
    /// <response code="200">Rezervasyon listesi (en yeniden eskiye).</response>
    [HttpGet("reservations")]
    [Authorize]
    [ProducesResponseType<IReadOnlyList<ReservationDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyReservations(
        [FromQuery] ReservationStatus? status,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetMyReservationsQuery(status), cancellationToken)
            .ConfigureAwait(false));
}

/// <summary>Oturum koltuk islemleri. PDF Sprint 7.</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/event-sessions")]
public sealed class EventSessionsController : ApiControllerBase
{
    /// <summary>
    /// Oturumun koltuk uygunlugunu döndürür.
    /// PDF: GET /api/v1/event-sessions/{id}/seat-availability
    ///
    /// ANONIM erisime açık: kullanıcı bilet almadan önce hangi
    /// koltuklarin boş olduğunu gorebilmeli.
    ///
    /// Kimin kilitledigi bilgisi DONMUYOR -- yalnızca durum.
    /// Aksi halde kullanıcı gizliligi ihlal edilirdi.
    /// </summary>
    [HttpGet("{id:guid}/seat-availability")]
    [AllowAnonymous]
    [ProducesResponseType<SeatAvailability>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSeatAvailability(Guid id, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetSeatAvailabilityQuery(id), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>
    /// Oturum için koltuk kayitlarini üretir.
    /// Rezervasyonun ON KOSULUDUR.
    /// </summary>
    [HttpPost("{id:guid}/generate-seats")]
    [Authorize(Policy = AuthenticationSetup.Policies.OrganizerOnly)]
    [ProducesResponseType<int>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> GenerateSeats(Guid id, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GenerateSessionSeatsCommand(id), cancellationToken)
            .ConfigureAwait(false));
}

public sealed record CreateReservationRequest(
    Guid EventSessionId,
    IReadOnlyList<Guid> EventSeatIds);

public sealed record CancelReservationRequest(string? Reason);
