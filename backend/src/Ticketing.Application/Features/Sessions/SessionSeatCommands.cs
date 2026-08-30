using MediatR;
using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Abstractions.Persistence;
using Ticketing.Application.Abstractions.Time;
using Ticketing.Application.Common.Results;
using Ticketing.Domain.Enums;

namespace Ticketing.Application.Features.Sessions;

internal static class SessionErrors
{
    public static readonly Error NotFound = Error.NotFound(
        "session.not_found", "Etkinlik oturumu bulunamadı.");

    public static readonly Error SeatsAlreadyGenerated = Error.Conflict(
        "session.seats_already_generated", "Bu oturum için koltuklar zaten üretilmiş.");

    public static readonly Error NoTicketTypeForSection = Error.Conflict(
        "session.no_ticket_type_for_section",
        "Oturma planindaki bazi bolumlere bilet türü atanmamis. " +
        "Koltuk uretmeden önce tüm bolumleri bir bilet turune atayin.");
}

// Oturum koltuklarini uret

/// <summary>
/// Bir oturum için EventSeat kayitlarini üretir.
///
/// Bu, rezervasyonun ON KOSULUDUR: EventSeat olmadan koltuk
/// kilitlenemez, satilamaz.
/// </summary>
public sealed record GenerateSessionSeatsCommand(Guid SessionId) : IRequest<Result<int>>;

internal sealed class GenerateSessionSeatsCommandHandler
    : IRequestHandler<GenerateSessionSeatsCommand, Result<int>>
{
    private readonly IApplicationDbContext _context;

    public GenerateSessionSeatsCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<Result<int>> Handle(
        GenerateSessionSeatsCommand request,
        CancellationToken cancellationToken)
    {
        var session = await _context.EventSessions
            .Include(s => s.EventSeats)
            .FirstOrDefaultAsync(s => s.Id == request.SessionId, cancellationToken)
            .ConfigureAwait(false);

        if (session is null)
        {
            return Result.Failure<int>(SessionErrors.NotFound);
        }

        if (session.AreSeatsGenerated)
        {
            return Result.Failure<int>(SessionErrors.SeatsAlreadyGenerated);
        }

        // Oturma planindaki fiziksel koltukları, BOLUMLERIYLE birlikte cek.
        var seats = await _context.Seats
            .AsNoTracking()
            .Where(s => s.SeatSection.SeatLayoutId == session.SeatLayoutId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (seats.Count == 0)
        {
            return Result.Failure<int>(Error.Conflict(
                "session.layout_has_no_seats",
                "Secilen oturma planinda hiç koltuk yok."));
        }

        // Bolum -> bilet turu eslestirmesi
        //
        // Her koltuğun fiyati, ait olduğu BOLUMUN bilet turunden gelir.
        // Bu eslestirme olmadan koltuğun fiyati belirsiz kalır.
        //
        // Sozluge (Dictionary) donusturuyorum: aşağıdaki dongude her
        // koltuk için liste taramasi yapmak yerine O(1) arama.
        // 2000 koltuk x 5 bölüm = 10.000 karsilastirma yerine 2000 arama.
        var sectionToTicketType = await _context.TicketTypeSections
            .AsNoTracking()
            .Where(ts => ts.TicketType.EventId == session.EventId && ts.TicketType.IsActive)
            .Select(ts => new { ts.SeatSectionId, ts.TicketTypeId, ts.TicketType.Price })
            .ToDictionaryAsync(x => x.SeatSectionId, cancellationToken)
            .ConfigureAwait(false);

        // TÜM bolumlerin bir bilet turune atanmis olduğunu dogrula.
        //
        // Eksik atama varsa uretimi hiç baslatmiyorum. Yarim üretim
        // yapip "bu koltuklarin fiyati yok" durumuna dusmek, sonradan
        // temizlenmesi çok zor bir tutarsizlik olurdu.
        var sectionIds = seats.Select(s => s.SeatSectionId).Distinct().ToList();
        var unmapped = sectionIds.Where(id => !sectionToTicketType.ContainsKey(id)).ToList();

        if (unmapped.Count > 0)
        {
            return Result.Failure<int>(SessionErrors.NoTicketTypeForSection);
        }

        // Koltuk uretimi
        //
        // Fiyatlandirmayi bir FONKSIYON olarak geciyorum. Boylece her
        // koltuk, ait olduğu bölümün bilet türü ve fiyatiyla DOGUYOR --
        // önce uretip sonra duzeltmek gerekmiyor.
        //
        // Eslestirme eksikse entity DomainException firlatiyor ve
        // hiçbir sey kaydedilmiyor ("ya hep ya hiç").
        var generated = session.GenerateSeats(
            seats,
            seat => sectionToTicketType.TryGetValue(seat.SeatSectionId, out var m)
                ? (m.TicketTypeId, m.Price)
                : null);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(generated.Count);
    }
}

// Koltuk uygunlugu
// PDF: GET /api/v1/event-sessions/{id}/seat-availability

public sealed record SeatAvailabilityItem(
    Guid EventSeatId,
    Guid SeatId,
    string RowLabel,
    int SeatNumber,
    string DisplayLabel,
    Guid SectionId,
    string SectionName,
    string? SectionColor,
    Guid TicketTypeId,
    string TicketTypeName,
    decimal Price,
    string Currency,
    EventSeatStatus Status);

public sealed record SeatAvailability(
    Guid SessionId,
    DateTimeOffset StartDate,
    int TotalSeats,
    int AvailableSeats,
    IReadOnlyList<SeatAvailabilityItem> Seats);

public sealed record GetSeatAvailabilityQuery(Guid SessionId) : IRequest<Result<SeatAvailability>>;

internal sealed class GetSeatAvailabilityQueryHandler
    : IRequestHandler<GetSeatAvailabilityQuery, Result<SeatAvailability>>
{
    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _clock;

    public GetSeatAvailabilityQueryHandler(IApplicationDbContext context, IDateTimeProvider clock)
    {
        _context = context;
        _clock = clock;
    }

    public async Task<Result<SeatAvailability>> Handle(
        GetSeatAvailabilityQuery request,
        CancellationToken cancellationToken)
    {
        var session = await _context.EventSessions
            .AsNoTracking()
            .Where(s => s.Id == request.SessionId)
            .Select(s => new { s.Id, s.StartDate, s.AreSeatsGenerated })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (session is null)
        {
            return Result.Failure<SeatAvailability>(SessionErrors.NotFound);
        }

        var now = _clock.UtcNow;

        var seats = await _context.EventSeats
            .AsNoTracking()
            .Where(es => es.EventSessionId == request.SessionId)
            .OrderBy(es => es.Seat.SeatSection.DisplayOrder)
                .ThenBy(es => es.Seat.RowLabel)
                .ThenBy(es => es.Seat.SeatNumber)
            .Select(es => new
            {
                es.Id,
                es.SeatId,
                es.Seat.RowLabel,
                es.Seat.SeatNumber,
                SectionId = es.Seat.SeatSectionId,
                SectionName = es.Seat.SeatSection.Name,
                SectionColor = es.Seat.SeatSection.ColorHex,
                es.TicketTypeId,
                TicketTypeName = es.TicketType.Name,
                es.Price,
                es.Status,
                es.LockedUntil,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Suresi dolmus kilitleri "musait" goster
        //
        // Temizlik job'i dakikada bir çalışıyor. Kilidi 10:10'da dolan
        // bir koltuk, job 10:11'de gelene kadar veritabaninda hâlâ
        // "Locked" görünür.
        //
        // Bu bir dakikalik pencerede koltuğu dolu göstermek, popüler
        // bir konserde yuzlerce kullanıcının boş koltuğu gorememesi
        // demektir.
        //
        // Bu donusumu SUNUCUDA yapıyorum, frontend'de değil. Frontend'e
        // biraksaydim her istemcinin saati farklı olurdu ve bazi
        // kullanıcılar koltuğu musait, bazilari dolu gorurdu.
        var items = seats
            .Select(s => new SeatAvailabilityItem(
                s.Id,
                s.SeatId,
                s.RowLabel,
                s.SeatNumber,
                $"{s.RowLabel}-{s.SeatNumber}",
                s.SectionId,
                s.SectionName,
                s.SectionColor,
                s.TicketTypeId,
                s.TicketTypeName,
                s.Price.Amount,
                s.Price.Currency,
                IsEffectivelyAvailable(s.Status, s.LockedUntil, now)
                    ? EventSeatStatus.Available
                    : s.Status))
            .ToList();

        return Result.Success(new SeatAvailability(
            session.Id,
            session.StartDate,
            items.Count,
            items.Count(i => i.Status == EventSeatStatus.Available),
            items));
    }

    private static bool IsEffectivelyAvailable(
        EventSeatStatus status,
        DateTimeOffset? lockedUntil,
        DateTimeOffset now)
        => status == EventSeatStatus.Locked && lockedUntil.HasValue && lockedUntil.Value <= now;
}
