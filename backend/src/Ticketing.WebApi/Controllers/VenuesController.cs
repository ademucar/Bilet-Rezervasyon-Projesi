using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ticketing.Application.Common.Pagination;
using Ticketing.Application.Features.Halls;
using Ticketing.Application.Features.SeatLayouts;
using Ticketing.Application.Features.Venues;
using Ticketing.WebApi.Security;

namespace Ticketing.WebApi.Controllers;

/// <summary>
/// Mekan ve salon yonetimi. PDF Sprint 4.
///
/// ==================================================================
/// YETKILENDIRME STRATEJISI
/// ==================================================================
/// OKUMA islemleri ANONIM: kullanici etkinlik ararken mekan bilgisini
/// gormeli ve bunun icin giris yapmak zorunda kalmamali.
///
/// YAZMA islemleri ADMIN: PDF sayfa 5'e gore mekan/salon yonetimi
/// admin sorumlulugunda. Organizator yalnizca var olan salonlari SECER.
///
/// Bu ayrim olmasaydi her organizator kendi "salon"unu tanimlardi ve
/// "ayni salon ayni saatte iki etkinlige atanamaz" kurali anlamsizlasirdi.
/// ==================================================================
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/venues")]
public sealed class VenuesController : ApiControllerBase
{
    // ---------------- Mekan ----------------

    /// <summary>Mekanlari sayfali listeler. Isim ve sehre gore filtrelenebilir.</summary>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType<PagedResult<VenueListItem>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetVenues(
        [FromQuery] GetVenuesQuery query,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(query, cancellationToken).ConfigureAwait(false));

    /// <summary>Mekan detayini salonlariyla birlikte dondurur.</summary>
    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    [ProducesResponseType<VenueDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetVenue(Guid id, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetVenueByIdQuery(id), cancellationToken).ConfigureAwait(false));

    /// <summary>Yeni mekan olusturur.</summary>
    [HttpPost]
    [Authorize(Policy = AuthenticationSetup.Policies.AdminOnly)]
    [ProducesResponseType<Guid>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateVenue(
        [FromBody] CreateVenueCommand command,
        CancellationToken cancellationToken)
    {
        var result = await Sender.Send(command, cancellationToken).ConfigureAwait(false);

        // 201 Created + Location header.
        //
        // 200 donmek de "calisir" ama REST'te olusturma islemi 201
        // dondurmeli ve yeni kaynagin adresini Location header'inda
        // bildirmeli. Istemciler bunu takip edip kaynagi cekebiliyor.
        return HandleCreated(result, $"/api/v1/venues/{(result.IsSuccess ? result.Value : Guid.Empty)}");
    }

    /// <summary>Mekan bilgilerini gunceller. Sehir DEGISTIRILEMEZ.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = AuthenticationSetup.Policies.AdminOnly)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateVenue(
        Guid id,
        [FromBody] UpdateVenueRequest request,
        CancellationToken cancellationToken)
    {
        // ==============================================================
        // ID GOVDEDEN DEGIL URL'DEN ALINIYOR
        // ==============================================================
        // Komut nesnesini dogrudan [FromBody] ile baglasaydim, istemci
        // URL'de bir Id, govdede BASKA bir Id gonderebilirdi:
        //     PUT /api/v1/venues/AAA   { "id": "BBB", ... }
        //
        // Hangisi gecerli? Belirsiz. Ve daha kotusu: yetkilendirme
        // URL'deki Id'ye bakip govdedeki Id guncellenirse GUVENLIK
        // ACIGI olusur -- kullanici erisebildigi bir kaynagin adresini
        // verip erisemedigi bir kaynagi degistirir.
        //
        // Ayri bir Request tipi kullanip Id'yi YALNIZCA URL'den almak
        // bu belirsizligi tamamen ortadan kaldiriyor.
        var command = new UpdateVenueCommand(
            id, request.Name, request.Address, request.Latitude, request.Longitude);

        return HandleResult(await Sender.Send(command, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Mekani pasife alir (soft delete). Aktif etkinlik varsa reddedilir.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AuthenticationSetup.Policies.AdminOnly)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> DeleteVenue(Guid id, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new DeleteVenueCommand(id), cancellationToken).ConfigureAwait(false));

    // ---------------- Salon ----------------

    /// <summary>Mekana yeni salon ekler. PDF: POST /api/v1/venues/{venueId}/halls</summary>
    [HttpPost("{venueId:guid}/halls")]
    [Authorize(Policy = AuthenticationSetup.Policies.AdminOnly)]
    [ProducesResponseType<Guid>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateHall(
        Guid venueId,
        [FromBody] CreateHallRequest request,
        CancellationToken cancellationToken)
    {
        var command = new CreateHallCommand(venueId, request.Name, request.Capacity);
        var result = await Sender.Send(command, cancellationToken).ConfigureAwait(false);

        return HandleCreated(result, $"/api/v1/halls/{(result.IsSuccess ? result.Value : Guid.Empty)}");
    }
}

/// <summary>
/// Guncelleme istek modeli. Id ICERMEZ -- o URL'den geliyor.
///
/// PDF zorunlu kural: "Request ve response modelleri ayrilmalidir."
/// </summary>
public sealed record UpdateVenueRequest(
    string Name,
    string Address,
    decimal? Latitude,
    decimal? Longitude);

public sealed record CreateHallRequest(string Name, int Capacity);

// ===================================================================
// SALON VE OTURMA PLANI
// ===================================================================

/// <summary>Salon ve oturma plani islemleri. PDF Sprint 4.</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/halls")]
public sealed class HallsController : ApiControllerBase
{
    [HttpPut("{id:guid}")]
    [Authorize(Policy = AuthenticationSetup.Policies.AdminOnly)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UpdateHall(
        Guid id,
        [FromBody] CreateHallRequest request,
        CancellationToken cancellationToken)
        => HandleResult(await Sender
            .Send(new UpdateHallCommand(id, request.Name, request.Capacity), cancellationToken)
            .ConfigureAwait(false));

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AuthenticationSetup.Policies.AdminOnly)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteHall(Guid id, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new DeleteHallCommand(id), cancellationToken).ConfigureAwait(false));

    /// <summary>Salonun oturma planlarini listeler.</summary>
    [HttpGet("{hallId:guid}/seat-layouts")]
    [AllowAnonymous]
    [ProducesResponseType<IReadOnlyList<SeatLayoutListItem>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSeatLayouts(Guid hallId, CancellationToken cancellationToken)
        => HandleResult(await Sender
            .Send(new GetSeatLayoutsByHallQuery(hallId), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>Salona yeni oturma plani ekler. PDF: POST /api/v1/halls/{hallId}/seat-layouts</summary>
    [HttpPost("{hallId:guid}/seat-layouts")]
    [Authorize(Policy = AuthenticationSetup.Policies.AdminOnly)]
    [ProducesResponseType<Guid>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateSeatLayout(
        Guid hallId,
        [FromBody] CreateSeatLayoutRequest request,
        CancellationToken cancellationToken)
    {
        var command = new CreateSeatLayoutCommand(hallId, request.Name, request.Description);
        var result = await Sender.Send(command, cancellationToken).ConfigureAwait(false);

        return HandleCreated(result, $"/api/v1/seat-layouts/{(result.IsSuccess ? result.Value : Guid.Empty)}");
    }
}

public sealed record CreateSeatLayoutRequest(string Name, string? Description);

/// <summary>Oturma plani detay ve yapilandirma islemleri. PDF Sprint 4.</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/seat-layouts")]
public sealed class SeatLayoutsController : ApiControllerBase
{
    /// <summary>
    /// Plan detayini TUM bolum ve koltuklariyla dondurur.
    /// Frontend gorsel koltuk haritasini bu veriyle ciziyor.
    /// </summary>
    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    [ProducesResponseType<SeatLayoutDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSeatLayout(Guid id, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetSeatLayoutQuery(id), cancellationToken).ConfigureAwait(false));

    /// <summary>Plana bolum ekler. PDF: POST /api/v1/seat-layouts/{id}/sections</summary>
    [HttpPost("{id:guid}/sections")]
    [Authorize(Policy = AuthenticationSetup.Policies.AdminOnly)]
    [ProducesResponseType<Guid>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> AddSection(
        Guid id,
        [FromBody] AddSectionRequest request,
        CancellationToken cancellationToken)
    {
        var command = new AddSectionCommand(id, request.Name, request.DisplayOrder, request.ColorHex);
        var result = await Sender.Send(command, cancellationToken).ConfigureAwait(false);

        return HandleCreated(result, $"/api/v1/seat-layouts/{id}");
    }

    /// <summary>
    /// Bir bolume toplu koltuk uretir.
    /// PDF: POST /api/v1/seat-layouts/{id}/generate-seats
    /// </summary>
    /// <returns>Uretilen koltuk sayisi.</returns>
    [HttpPost("{id:guid}/generate-seats")]
    [Authorize(Policy = AuthenticationSetup.Policies.AdminOnly)]
    [ProducesResponseType<int>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> GenerateSeats(
        Guid id,
        [FromBody] GenerateSeatsRequest request,
        CancellationToken cancellationToken)
    {
        var command = new GenerateSeatsCommand(
            id, request.SectionId, request.RowCount, request.SeatsPerRow, request.RowLabels);

        return HandleResult(await Sender.Send(command, cancellationToken).ConfigureAwait(false));
    }
}

public sealed record AddSectionRequest(string Name, int DisplayOrder, string? ColorHex);

public sealed record GenerateSeatsRequest(
    Guid SectionId,
    int RowCount,
    int SeatsPerRow,
    IReadOnlyList<string>? RowLabels);
