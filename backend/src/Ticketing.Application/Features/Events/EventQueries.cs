using System.Diagnostics.CodeAnalysis;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Abstractions.Persistence;
using Ticketing.Application.Common.Pagination;
using Ticketing.Application.Common.Results;
using Ticketing.Domain.Enums;

namespace Ticketing.Application.Features.Events;

// ===================================================================
// DTO'lar
// ===================================================================

public sealed record EventListItem(
    Guid Id,
    string Title,
    string CategoryName,
    string CityName,
    string VenueName,
    string? PosterImagePath,
    DateTimeOffset EventDate,
    EventStatus Status,
    int MinimumAge,
    int SessionCount);

public sealed record EventDetail(
    Guid Id,
    string Title,
    string Description,
    Guid CategoryId,
    string CategoryName,
    Guid OrganizerId,
    string OrganizerName,
    Guid CityId,
    string CityName,
    Guid VenueId,
    string VenueName,
    string VenueAddress,
    Guid HallId,
    string HallName,
    string? PosterImagePath,
    int MinimumAge,
    int DurationMinutes,
    int MaxTicketsPerUser,
    DateTimeOffset SalesStartDate,
    DateTimeOffset SalesEndDate,
    DateTimeOffset EventDate,
    EventStatus Status,
    string? CancellationReason,
    IReadOnlyList<EventSessionDto> Sessions);

public sealed record EventSessionDto(
    Guid Id,
    DateTimeOffset StartDate,
    DateTimeOffset EndDate,
    Guid HallId,
    string HallName,
    Guid SeatLayoutId,
    string SeatLayoutName,
    EventSessionStatus Status,
    bool AreSeatsGenerated);

// ===================================================================
// LISTELEME -- PDF: GET /api/v1/events
// ===================================================================

public sealed record GetEventsQuery : PaginationRequest, IRequest<Result<PagedResult<EventListItem>>>
{
    public string? Search { get; init; }

    public Guid? CityId { get; init; }

    public Guid? CategoryId { get; init; }

    public Guid? VenueId { get; init; }

    public DateTimeOffset? DateFrom { get; init; }

    public DateTimeOffset? DateTo { get; init; }

    /// <summary>
    /// Taslak ve onay bekleyen etkinlikleri de getir.
    ///
    /// Controller bu alani, kullanicinin ROLUNE gore SUNUCUDA set ediyor --
    /// istemciden gelen degere GUVENMIYORUZ. Guvenseydik, herhangi bir
    /// kullanici sorguya includeUnpublished=true ekleyip yayinlanmamis
    /// etkinlikleri gorurdu.
    /// </summary>
    public bool IncludeUnpublished { get; init; }

    /// <summary>Yalnizca bu organizatorun etkinlikleri (organizator paneli).</summary>
    public Guid? OrganizerId { get; init; }
}

internal sealed class GetEventsQueryHandler
    : IRequestHandler<GetEventsQuery, Result<PagedResult<EventListItem>>>
{
    /// <summary>
    /// Anonim kullanicinin gorebilecegi durumlar.
    ///
    /// static readonly: her istekte yeni dizi ayirmiyoruz. Bu, sitenin
    /// en sik calisan sorgusu olacak.
    /// </summary>
    private static readonly EventStatus[] PublicStatuses =
    [
        EventStatus.Published,
        EventStatus.SalesOpen,
        EventStatus.SalesClosed,
        EventStatus.Completed
    ];

    private readonly IApplicationDbContext _context;

    public GetEventsQueryHandler(IApplicationDbContext context) => _context = context;

    [SuppressMessage(
        "Globalization",
        "CA1304:Specify CultureInfo",
        Justification =
            "ToLower() bir IFADE AGACI icinde ve .NET'te calismiyor; EF Core " +
            "onu SQL'deki LOWER() fonksiyonuna ceviriyor. Kultur ayarinin " +
            "sonuca etkisi yok. ToLowerInvariant() ise EF tarafindan SQL'e " +
            "cevrilemedigi icin kullanilamaz.")]
    [SuppressMessage(
        "Globalization",
        "CA1311:Specify a culture or use an invariant version",
        Justification = "Bkz. CA1304 aciklamasi.")]
    public async Task<Result<PagedResult<EventListItem>>> Handle(
        GetEventsQuery request,
        CancellationToken cancellationToken)
    {
        var query = _context.Events.AsNoTracking();

        // ==============================================================
        // GORUNURLUK FILTRESI -- BU DOSYANIN EN ONEMLI SATIRI
        // ==============================================================
        // Bu filtre olmasaydi herkes Draft ve PendingApproval durumundaki
        // etkinlikleri gorurdu: organizatorun henuz yayinlamadigi,
        // fiyatlari belirlenmemis, belki de vazgecilecek etkinlikler.
        //
        // Ayrica Suspended (admin tarafindan uygunsuz bulunup askiya
        // alinmis) etkinlikler de gorunurdu -- askiya almanin hicbir
        // anlami kalmazdi.
        // ==============================================================
        if (!request.IncludeUnpublished)
        {
            query = query.Where(e => PublicStatuses.Contains(e.Status));
        }

        if (request.OrganizerId.HasValue)
        {
            query = query.Where(e => e.OrganizerId == request.OrganizerId.Value);
        }

        if (request.CityId.HasValue)
        {
            query = query.Where(e => e.CityId == request.CityId.Value);
        }

        if (request.CategoryId.HasValue)
        {
            query = query.Where(e => e.CategoryId == request.CategoryId.Value);
        }

        if (request.VenueId.HasValue)
        {
            query = query.Where(e => e.VenueId == request.VenueId.Value);
        }

        if (request.DateFrom.HasValue)
        {
            query = query.Where(e => e.EventDate >= request.DateFrom.Value);
        }

        if (request.DateTo.HasValue)
        {
            query = query.Where(e => e.EventDate <= request.DateTo.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var pattern = $"%{request.Search.Trim().ToLowerInvariant()}%";

            // Baslik VE aciklamada ariyorum.
            //
            // Kullanici "rock" yazdiginda, baslikta gecmese bile
            // aciklamasinda gecen konserleri de bulmali. Yalnizca
            // baslikta arasaydik arama sonuclari fakir kalirdi.
            query = query.Where(e =>
                EF.Functions.Like(e.Title.ToLower(), pattern) ||
                EF.Functions.Like(e.Description.ToLower(), pattern));
        }

        var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        var items = await query
            // Yaklasan etkinlikler once.
            // Kullanici "bu hafta ne var" diye bakiyor; en uzak tarihli
            // konseri ilk sirada gormek istemez.
            .OrderBy(e => e.EventDate)
            .Skip(request.Skip)
            .Take(request.PageSize)
            .Select(e => new EventListItem(
                e.Id,
                e.Title,
                e.Category.Name,
                e.City.Name,
                e.Venue.Name,
                e.PosterImagePath,
                e.EventDate,
                e.Status,
                e.MinimumAge,
                e.Sessions.Count(s => s.Status != EventSessionStatus.Cancelled)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(
            PagedResult<EventListItem>.Create(items, request.PageNumber, request.PageSize, totalCount));
    }
}

// ===================================================================
// DETAY -- PDF: GET /api/v1/events/{id}
// ===================================================================

public sealed record GetEventByIdQuery(Guid Id, bool IncludeUnpublished)
    : IRequest<Result<EventDetail>>;

internal sealed class GetEventByIdQueryHandler
    : IRequestHandler<GetEventByIdQuery, Result<EventDetail>>
{
    private readonly IApplicationDbContext _context;

    public GetEventByIdQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<Result<EventDetail>> Handle(
        GetEventByIdQuery request,
        CancellationToken cancellationToken)
    {
        var detail = await _context.Events
            .AsNoTracking()
            .Where(e => e.Id == request.Id)
            .Select(e => new EventDetail(
                e.Id,
                e.Title,
                e.Description,
                e.CategoryId,
                e.Category.Name,
                e.OrganizerId,

                // Organizator adini ALT SORGU ile aliyorum.
                //
                // Sebep: Event ile OrganizerProfile arasinda navigation
                // ozelligi tanimlamadik. Tanimlayabilirdik ama Event
                // sinifi zaten kalabalik ve bu bilgi yalnizca detay
                // ekraninda gerekiyor.
                _context.OrganizerProfiles
                    .Where(p => p.Id == e.OrganizerId)
                    .Select(p => p.CompanyName)
                    .FirstOrDefault() ?? "-",

                e.CityId,
                e.City.Name,
                e.VenueId,
                e.Venue.Name,
                e.Venue.Address,
                e.HallId,
                e.Hall.Name,
                e.PosterImagePath,
                e.MinimumAge,
                e.DurationMinutes,
                e.MaxTicketsPerUser,
                e.SalesStartDate,
                e.SalesEndDate,
                e.EventDate,
                e.Status,
                e.CancellationReason,
                e.Sessions
                    .OrderBy(s => s.StartDate)
                    .Select(s => new EventSessionDto(
                        s.Id,
                        s.StartDate,
                        s.EndDate,
                        s.HallId,

                        // EventSession'da Hall navigation'i YOK -- yalnizca
                        // HallId var. Alt sorgu ile adini aliyorum.
                        //
                        // Navigation eklemek de mumkundu ama EventSession'i
                        // olabildigince yalin tutmak istiyorum: Sprint 7'de
                        // bu entity koltuk kilitleme akisinin merkezinde
                        // olacak ve her ekstra navigation, yanlislikla
                        // yuklenip performans sorunu cikarma riski tasiyor.
                        _context.Halls
                            .Where(h => h.Id == s.HallId)
                            .Select(h => h.Name)
                            .FirstOrDefault() ?? "-",
                        s.SeatLayoutId,
                        _context.SeatLayouts
                            .Where(sl => sl.Id == s.SeatLayoutId)
                            .Select(sl => sl.Name)
                            .FirstOrDefault() ?? "-",
                        s.Status,
                        s.AreSeatsGenerated))
                    .ToList()))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (detail is null)
        {
            return Result.Failure<EventDetail>(EventErrors.NotFound);
        }

        // ==============================================================
        // IDOR KORUMASI
        // ==============================================================
        // Listede gorunurluk filtresi uyguladik ama detay endpoint'i
        // Id ile DOGRUDAN cagrilabiliyor.
        //
        // Burada kontrol etmeseydik, birisi Id'yi bir yerden gorup
        // (veya tahmin edip) taslak etkinligi okuyabilirdi. Buna
        // "guvensiz dogrudan nesne referansi" (IDOR) denir ve web
        // guvenligindeki en yaygin aciklardan biridir.
        // ==============================================================
        var isPublic = detail.Status is EventStatus.Published
                                     or EventStatus.SalesOpen
                                     or EventStatus.SalesClosed
                                     or EventStatus.Completed;

        if (!isPublic && !request.IncludeUnpublished)
        {
            // 403 degil 404 donuyorum -- bilerek.
            //
            // 403 "bu kayit VAR ama goremezsin" der ve varligini
            // DOGRULAR. Saldirgan bu bilgiyle Id taramasi yapip hangi
            // etkinliklerin var oldugunu ogrenebilir.
            // 404 hicbir sey sizdirmaz.
            return Result.Failure<EventDetail>(EventErrors.NotFound);
        }

        return Result.Success(detail);
    }
}
