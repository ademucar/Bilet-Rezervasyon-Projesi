using MediatR;
using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Abstractions.Persistence;
using Ticketing.Application.Common.Results;

namespace Ticketing.Application.Features.SeatLayouts;

// ===================================================================
// DTO'lar
// ===================================================================

public sealed record SeatLayoutDetail(
    Guid Id,
    Guid HallId,
    string HallName,
    int HallCapacity,
    string Name,
    string? Description,
    bool IsActive,
    bool IsInUse,
    int TotalSeatCount,
    IReadOnlyList<SectionDetail> Sections);

public sealed record SectionDetail(
    Guid Id,
    string Name,
    int DisplayOrder,
    string? ColorHex,
    int SeatCount,
    IReadOnlyList<SeatDto> Seats);

public sealed record SeatDto(
    Guid Id,
    string RowLabel,
    int SeatNumber,
    string DisplayLabel,
    bool IsActive,
    int? PositionX,
    int? PositionY);

public sealed record SeatLayoutListItem(
    Guid Id,
    string Name,
    bool IsActive,
    bool IsInUse,
    int SectionCount,
    int SeatCount);

// ===================================================================
// PLAN DETAYI -- PDF: GET /api/v1/seat-layouts/{id}
// ===================================================================

public sealed record GetSeatLayoutQuery(Guid Id) : IRequest<Result<SeatLayoutDetail>>;

internal sealed class GetSeatLayoutQueryHandler
    : IRequestHandler<GetSeatLayoutQuery, Result<SeatLayoutDetail>>
{
    private readonly IApplicationDbContext _context;

    public GetSeatLayoutQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<Result<SeatLayoutDetail>> Handle(
        GetSeatLayoutQuery request,
        CancellationToken cancellationToken)
    {
        // ==============================================================
        // AsSplitQuery -- BURADA KRITIK
        // ==============================================================
        // Bu sorgu uc seviyeli bir koleksiyon zinciri iceriyor:
        //     SeatLayout -> Sections -> Seats
        //
        // EF varsayilan olarak TEK sorgu uretir ve tablolari JOIN'ler.
        // Sonuc: 5 bolum x 500 koltuk = 2500 satir dondurur ve
        // SeatLayout'un tum sutunlari 2500 KEZ tekrarlanir. Buna
        // "kartezyen patlama" (cartesian explosion) denir.
        //
        // AsSplitQuery, EF'e "her koleksiyon icin AYRI sorgu calistir"
        // der. Uc kucuk sorgu, tek dev sorgudan cok daha hizli ve
        // cok daha az veri tasir.
        //
        // Ne zaman kullanilmaz? Tek koleksiyon varsa veya kayit sayisi
        // azsa; o zaman ekstra gidis-donus maliyeti kazanci gecer.
        // Burada koltuk sayisi binleri bulabilecegi icin acikca aciyorum.
        // ==============================================================
        var layout = await _context.SeatLayouts
            .AsNoTracking()
            .AsSplitQuery()
            .Include(sl => sl.Hall)
            .Include(sl => sl.Sections.OrderBy(s => s.DisplayOrder))
                .ThenInclude(s => s.Seats.OrderBy(seat => seat.RowLabel).ThenBy(seat => seat.SeatNumber))
            .FirstOrDefaultAsync(sl => sl.Id == request.Id, cancellationToken)
            .ConfigureAwait(false);

        if (layout is null)
        {
            return Result.Failure<SeatLayoutDetail>(SeatLayoutErrors.NotFound);
        }

        var isInUse = await _context.EventSessions
            .AsNoTracking()
            .AnyAsync(s => s.SeatLayoutId == request.Id, cancellationToken)
            .ConfigureAwait(false);

        var detail = new SeatLayoutDetail(
            layout.Id,
            layout.HallId,
            layout.Hall.Name,
            layout.Hall.Capacity,
            layout.Name,
            layout.Description,
            layout.IsActive,
            isInUse,
            layout.GetTotalSeatCount(),
            layout.Sections
                .Select(s => new SectionDetail(
                    s.Id,
                    s.Name,
                    s.DisplayOrder,
                    s.ColorHex,
                    s.Seats.Count,
                    s.Seats
                        .Select(seat => new SeatDto(
                            seat.Id,
                            seat.RowLabel,
                            seat.SeatNumber,
                            seat.GetDisplayLabel(),
                            seat.IsActive,
                            seat.PositionX,
                            seat.PositionY))
                        .ToList()))
                .ToList());

        return Result.Success(detail);
    }
}

// ===================================================================
// SALONA AIT PLANLARIN LISTESI
// ===================================================================

public sealed record GetSeatLayoutsByHallQuery(Guid HallId)
    : IRequest<Result<IReadOnlyList<SeatLayoutListItem>>>;

internal sealed class GetSeatLayoutsByHallQueryHandler
    : IRequestHandler<GetSeatLayoutsByHallQuery, Result<IReadOnlyList<SeatLayoutListItem>>>
{
    private readonly IApplicationDbContext _context;

    public GetSeatLayoutsByHallQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<Result<IReadOnlyList<SeatLayoutListItem>>> Handle(
        GetSeatLayoutsByHallQuery request,
        CancellationToken cancellationToken)
    {
        // Listede KOLTUKLARI cekmiyorum, yalnizca SAYILARINI.
        //
        // Include kullansaydim 5 plan x 2000 koltuk = 10.000 satir
        // bellege gelirdi; oysa ekranda yalnizca "2000 koltuk" yaziyor.
        // Projeksiyon icindeki Count(), SQL'de COUNT(*) olarak calisir.
        var layouts = await _context.SeatLayouts
            .AsNoTracking()
            .Where(sl => sl.HallId == request.HallId)
            .OrderBy(sl => sl.Name)
            .Select(sl => new SeatLayoutListItem(
                sl.Id,
                sl.Name,
                sl.IsActive,
                _context.EventSessions.Any(s => s.SeatLayoutId == sl.Id),
                sl.Sections.Count,
                sl.Sections.Sum(s => s.Seats.Count(seat => !seat.IsDeleted))))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Result.Success<IReadOnlyList<SeatLayoutListItem>>(layouts);
    }
}
