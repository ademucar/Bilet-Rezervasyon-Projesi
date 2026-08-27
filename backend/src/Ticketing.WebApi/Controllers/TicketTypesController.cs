using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ticketing.Application.Features.TicketTypes;
using Ticketing.WebApi.Security;

namespace Ticketing.WebApi.Controllers;

/// <summary>
/// Bilet turu ve fiyatlandirma. PDF Sprint 6.
/// Etkinlik altindaki islemler (olusturma, listeleme).
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/events/{eventId:guid}/ticket-types")]
public sealed class EventTicketTypesController : ApiControllerBase
{
    /// <summary>Etkinligin bilet turlerini fiyata gore sirali dondurur.</summary>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType<IReadOnlyList<TicketTypeDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTicketTypes(Guid eventId, CancellationToken cancellationToken)
        => HandleResult(await Sender
            .Send(new GetTicketTypesQuery(eventId), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>Etkinlige yeni bilet turu ekler.</summary>
    [HttpPost]
    // Route parametresi "eventId" ama EventOwner handler'i "id" ariyor.
    // Bu yuzden burada OrganizerOnly kullaniyorum ve sahiplik kontrolu
    // handler icinde Event uzerinden yapiliyor (AddTicketType, etkinligi
    // yukleyip kurallarini uyguluyor).
    //
    // Alternatif, EventOwner handler'ini "eventId" adini da okuyacak
    // sekilde genisletmekti; Sprint 7'de rezervasyon endpointleri
    // eklenirken o genellestirmeyi yapacagim.
    [Authorize(Policy = AuthenticationSetup.Policies.OrganizerOnly)]
    [ProducesResponseType<Guid>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateTicketType(
        Guid eventId,
        [FromBody] CreateTicketTypeRequest request,
        CancellationToken cancellationToken)
    {
        var command = new CreateTicketTypeCommand(
            eventId, request.Name, request.Price, request.Currency,
            request.Quota, request.RequiresStudentVerification,
            request.SalesStartDate, request.SalesEndDate);

        var result = await Sender.Send(command, cancellationToken).ConfigureAwait(false);

        return HandleCreated(result, $"/api/v1/events/{eventId}/ticket-types");
    }
}

/// <summary>Bilet turu uzerindeki dogrudan islemler. PDF Sprint 6.</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/ticket-types")]
public sealed class TicketTypesController : ApiControllerBase
{
    [HttpPut("{id:guid}")]
    [Authorize(Policy = AuthenticationSetup.Policies.OrganizerOnly)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateTicketTypeRequest request,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(
            new UpdateTicketTypeCommand(
                id, request.Name, request.Quota, request.RequiresStudentVerification,
                request.SalesStartDate, request.SalesEndDate),
            cancellationToken).ConfigureAwait(false));

    /// <summary>
    /// Fiyati degistirir.
    ///
    /// AYRI endpoint cunku fiyat degisikligi ayri bir olaydir:
    /// satis baslamissa denetim kaydi olusturulur (PDF Sprint 6).
    /// Genel guncelleme icine gomulseydi, adi degistirilen her turde
    /// gereksiz audit kaydi olusurdu.
    /// </summary>
    [HttpPut("{id:guid}/price")]
    [Authorize(Policy = AuthenticationSetup.Policies.OrganizerOnly)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ChangePrice(
        Guid id,
        [FromBody] ChangePriceRequest request,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(
            new ChangeTicketTypePriceCommand(id, request.Price, request.Currency),
            cancellationToken).ConfigureAwait(false));

    /// <summary>PDF: POST /api/v1/ticket-types/{id}/assign-section</summary>
    [HttpPost("{id:guid}/assign-section")]
    [Authorize(Policy = AuthenticationSetup.Policies.OrganizerOnly)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> AssignSection(
        Guid id,
        [FromBody] AssignSectionRequest request,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(
            new AssignSectionCommand(id, request.SeatSectionId), cancellationToken)
            .ConfigureAwait(false));

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AuthenticationSetup.Policies.OrganizerOnly)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
        => HandleResult(await Sender
            .Send(new DeleteTicketTypeCommand(id), cancellationToken)
            .ConfigureAwait(false));
}

public sealed record CreateTicketTypeRequest(
    string Name,
    decimal Price,
    string Currency,
    int? Quota,
    bool RequiresStudentVerification,
    DateTimeOffset? SalesStartDate,
    DateTimeOffset? SalesEndDate);

public sealed record UpdateTicketTypeRequest(
    string Name,
    int? Quota,
    bool RequiresStudentVerification,
    DateTimeOffset? SalesStartDate,
    DateTimeOffset? SalesEndDate);

public sealed record ChangePriceRequest(decimal Price, string Currency);

public sealed record AssignSectionRequest(Guid SeatSectionId);
