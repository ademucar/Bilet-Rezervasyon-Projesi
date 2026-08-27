using Asp.Versioning;
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
    /// Koltuk secip rezervasyon olusturur. Koltuklar 10 dakika kilitlenir.
    /// </summary>
    /// <remarks>
    /// IDEMPOTENCY: Istegi "Idempotency-Key" header'i ile gonderin.
    /// Ayni anahtarla ikinci kez gonderilirse YENI rezervasyon
    /// olusmaz, ILK rezervasyon doner.
    ///
    /// Bu, kullanicinin butona iki kez basmasi veya agin istegi
    /// tekrarlamasi durumunda cift rezervasyonu engeller.
    /// </remarks>
    /// <response code="201">Rezervasyon olusturuldu, koltuklar kilitlendi.</response>
    /// <response code="409">Koltuklardan biri az once baskasi tarafindan alindi.</response>
    /// <response code="422">Satis kapali veya bilet limiti asildi.</response>
    [HttpPost]
    [Authorize]
    [ProducesResponseType<ReservationDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create(
        [FromBody] CreateReservationRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        // Idempotency anahtarini HEADER'dan aliyorum, govdeden degil.
        //
        // Sebep: bu bir PROTOKOL detayidir, is verisi degil. Stripe,
        // AWS gibi saglayicilar da ayni yaklasimi kullaniyor.
        // Govdeye koysaydik her istek modelinde tekrarlamamiz gerekirdi.
        var command = new CreateReservationCommand(
            request.EventSessionId, request.EventSeatIds, idempotencyKey);

        var result = await Sender.Send(command, cancellationToken).ConfigureAwait(false);

        return HandleCreated(
            result,
            $"/api/v1/reservations/{(result.IsSuccess ? result.Value.Id : Guid.Empty)}");
    }

    /// <summary>Rezervasyon detayi. Yalnizca sahibi gorebilir.</summary>
    [HttpGet("{id:guid}")]
    [Authorize]
    [ProducesResponseType<ReservationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetReservationQuery(id), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>Rezervasyonu iptal eder ve koltuklari serbest birakir.</summary>
    [HttpPost("{id:guid}/cancel")]
    [Authorize]
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
    [Authorize]
    [ProducesResponseType<ReservationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Extend(Guid id, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new ExtendReservationCommand(id), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>
    /// Suresi dolmus rezervasyonlari temizler.
    /// Sprint 9'da Hangfire ile dakikada bir otomatik calisacak;
    /// simdilik admin elle tetikleyebiliyor.
    /// </summary>
    [HttpPost("expire-overdue")]
    [Authorize(Policy = AuthenticationSetup.Policies.AdminOnly)]
    [ProducesResponseType<int>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ExpireOverdue(CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new ExpireReservationsCommand(), cancellationToken)
            .ConfigureAwait(false));
}

/// <summary>Kullanicinin kendi rezervasyonlari. PDF: GET /api/v1/users/me/reservations</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/users/me")]
public sealed class MyReservationsController : ApiControllerBase
{
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
    /// Oturumun koltuk uygunlugunu dondurur.
    /// PDF: GET /api/v1/event-sessions/{id}/seat-availability
    ///
    /// ANONIM erisime acik: kullanici bilet almadan once hangi
    /// koltuklarin bos oldugunu gorebilmeli.
    ///
    /// Kimin kilitledigi bilgisi DONMUYOR -- yalnizca durum.
    /// Aksi halde kullanici gizliligi ihlal edilirdi.
    /// </summary>
    [HttpGet("{id:guid}/seat-availability")]
    [AllowAnonymous]
    [ProducesResponseType<SeatAvailability>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSeatAvailability(Guid id, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetSeatAvailabilityQuery(id), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>
    /// Oturum icin koltuk kayitlarini uretir.
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
