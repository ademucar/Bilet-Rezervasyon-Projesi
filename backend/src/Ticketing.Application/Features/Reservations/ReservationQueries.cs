using MediatR;
using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Abstractions.Persistence;
using Ticketing.Application.Abstractions.Security;
using Ticketing.Application.Abstractions.Time;
using Ticketing.Application.Common.Results;
using Ticketing.Domain.Enums;

namespace Ticketing.Application.Features.Reservations;

// ===================================================================
// DTO'lar
// ===================================================================

public sealed record ReservationItemDto(
    Guid Id,
    Guid EventSeatId,
    string SeatLabel,
    string SectionName,
    string TicketTypeName,
    decimal UnitPrice,
    string Currency);

public sealed record ReservationDto(
    Guid Id,
    string ReservationCode,
    ReservationStatus Status,
    Guid EventSessionId,
    string EventTitle,
    DateTimeOffset SessionStartDate,
    string VenueName,
    decimal TotalAmount,
    string Currency,
    DateTimeOffset ExpiresAt,
    /// <summary>
    /// Kalan sure (saniye).
    ///
    /// Neden SUNUCUDA hesapliyorum? Cunku istemcinin saati YANLIS
    /// olabilir. Frontend ExpiresAt - Date.now() hesaplasaydi, saati
    /// 5 dakika geri olan bir kullanici sureyi 15 dakika sanirdi ve
    /// odemeye gectiginde "sureniz doldu" hatasi alirdi.
    ///
    /// Saniye cinsinden gonderip frontend'in kendi icinde geri
    /// saymasi, saat farkindan bagimsiz calisir.
    /// </summary>
    int RemainingSeconds,
    int ExtensionCount,
    IReadOnlyList<ReservationItemDto> Items);

// ===================================================================
// ORTAK SORGU
// ===================================================================

internal static class ReservationQueries
{
    /// <summary>
    /// Rezervasyon DTO sorgusu.
    ///
    /// Ayri bir metotta topluyorum cunku UC yerde kullaniliyor:
    /// olusturma sonucu, detay, kullanici listesi. Uc kez yazsaydim
    /// birinde bir alani eklemeyi unutmam kacinilmazdi ve o ekranda
    /// veri eksik gorunurdu.
    /// </summary>
    /// <summary>
    /// Materyalize edilmis DTO'ya kalan sureyi ekler.
    ///
    /// ==================================================================
    /// NEDEN SORGUDA HESAPLAMIYORUZ? -- CALISTIRINCA OGRENDIK
    /// ==================================================================
    /// Ilk yazisimda kalan sureyi SQL icinde hesapliyordum:
    ///
    ///     (int)(r.ExpiresAt > now ? (r.ExpiresAt - now).TotalSeconds : 0)
    ///
    /// Derlendi. Ama CALISMA ZAMANINDA patladi:
    ///     InvalidOperationException: The LINQ expression ...
    ///     could not be translated
    ///
    /// Sebep: DateTimeOffset cikarmasi + TimeSpan.TotalSeconds +
    /// int'e donusum zinciri Npgsql tarafindan SQL'e cevrilemiyor.
    ///
    /// Bu hata ES ZAMANLILIK TESTINDE ortaya cikti ve ogretici oldu:
    /// 10 es zamanli istekten 9'u dogru sekilde 409 aldi, 1'i
    /// rezervasyonu OLUSTURDU ama yaniti hazirlarken 500 dondu.
    /// Yani cekirdek mantik dogruydu, sunum katmani hatalıydı.
    ///
    /// Bellekte hesaplamak hem cevrilebilirlik sorununu cozuyor hem de
    /// dogru: kalan sure bir GORUNUM bilgisi, veritabaninin isi degil.
    /// </summary>
    public static ReservationDto WithRemainingSeconds(ReservationDto dto, DateTimeOffset now)
    {
        var remaining = dto.ExpiresAt - now;

        // Negatif sure dondurmuyorum: frontend geri sayimda "-00:03"
        // gostermemeli.
        var seconds = remaining > TimeSpan.Zero ? (int)remaining.TotalSeconds : 0;

        return dto with { RemainingSeconds = seconds };
    }

    /// <summary>
    /// Rezervasyon sorgusunu DTO'ya projelendirir.
    ///
    /// ==================================================================
    /// FILTRE, PROJEKSIYONDAN ONCE UYGULANMALI
    /// ==================================================================
    /// Ilk yazisimda bu metot dogrudan context.Reservations uzerinden
    /// baslayip IQueryable&lt;ReservationDto&gt; donuyordu ve cagiranlar
    /// sonucu filtreliyordu:
    ///
    ///     BuildDtoQuery(context, now).FirstOrDefaultAsync(r =&gt; r.Id == id)
    ///
    /// Bu, PROJEKSIYONDAN SONRA filtreleme demek. EF Core bunu SQL'e
    /// ceviremedi:
    ///     "The LINQ expression ... .Where(r =&gt; new ReservationDto(...))
    ///      could not be translated"
    ///
    /// Cunku EF, DTO nesnesini olusturup sonra uzerinde filtre
    /// uygulayamiyor -- filtrenin WHERE cumlesine donusebilmesi icin
    /// ENTITY uzerinde olmasi gerekiyor.
    ///
    /// Cozum: filtrelenmis IQueryable&lt;Reservation&gt; ALMAK. Boylece
    /// cagiran once filtreliyor, sonra projelendiriyoruz:
    ///
    ///     context.Reservations.Where(r =&gt; r.Id == id).ToDto(context)
    ///
    /// Bu hata ES ZAMANLILIK TESTINDE ortaya cikti: 9 istek dogru
    /// sekilde 409 aldi, kazanan istek rezervasyonu OLUSTURDU ama
    /// yaniti hazirlarken 500 dondu. Cekirdek mantik dogruydu.
    /// ==================================================================
    /// </summary>
    public static IQueryable<ReservationDto> ToDto(
        this IQueryable<Ticketing.Domain.Entities.Reservation> query,
        IApplicationDbContext context)
        => query
            .AsNoTracking()
            .Select(r => new ReservationDto(
                r.Id,
                r.ReservationCode,
                r.Status,
                r.EventSessionId,
                r.EventSession.Event.Title,
                r.EventSession.StartDate,
                r.EventSession.Event.Venue.Name,
                r.TotalAmount.Amount,
                r.TotalAmount.Currency,
                r.ExpiresAt,

                // Kalan sure burada 0 olarak birakiliyor; SQL'e
                // cevrilemedigi icin BELLEKTE, WithRemainingSeconds
                // ile hesaplaniyor. Gerekcesi yukarida yazili.
                0,

                r.ExtensionCount,
                r.Items
                    .OrderBy(i => i.EventSeat.Seat.RowLabel)
                        .ThenBy(i => i.EventSeat.Seat.SeatNumber)
                    .Select(i => new ReservationItemDto(
                        i.Id,
                        i.EventSeatId,
                        i.EventSeat.Seat.RowLabel + "-" + i.EventSeat.Seat.SeatNumber,
                        i.EventSeat.Seat.SeatSection.Name,
                        i.EventSeat.TicketType.Name,
                        i.UnitPrice.Amount,
                        i.UnitPrice.Currency))
                    .ToList()));
}

// ===================================================================
// DETAY -- PDF: GET /api/v1/reservations/{id}
// ===================================================================

public sealed record GetReservationQuery(Guid Id) : IRequest<Result<ReservationDto>>;

internal sealed class GetReservationQueryHandler
    : IRequestHandler<GetReservationQuery, Result<ReservationDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public GetReservationQueryHandler(
        IApplicationDbContext context,
        ICurrentUser currentUser,
        IDateTimeProvider clock)
    {
        _context = context;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result<ReservationDto>> Handle(
        GetReservationQuery request,
        CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not Guid userId)
        {
            return Result.Failure<ReservationDto>(
                Error.Unauthorized("auth.required", "Giris yapmalisiniz."));
        }

        // ==============================================================
        // SAHIPLIK KONTROLU -- SORGUNUN ICINDE
        // ==============================================================
        // "Once cek, sonra sahibi mi diye bak" da yapabilirdik. Ama o
        // zaman baskasinin rezervasyonu bir an icin bellege gelirdi ve
        // bir loglama veya hata mesajinda sizabilirdi.
        //
        // Sorguya dahil etmek daha guvenli: veri hic gelmiyor.
        //
        // Sonuc bulunamazsa 404 donuyoruz (403 degil) -- var olan bir
        // rezervasyonun varligini dogrulamamak icin.
        // Filtreyi ENTITY uzerinde uyguluyorum, DTO uzerinde degil.
        //
        // Sahiplik kontrolu de burada: baskasinin rezervasyonu hic
        // bellege gelmiyor. Sonuc bulunamazsa 404 -- 403 deseydik
        // rezervasyonun VAR oldugunu dogrulamis olurduk.
        var dto = await _context.Reservations
            .Where(r => r.Id == request.Id && r.UserId == userId)
            .ToDto(_context)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return dto is null
            ? Result.Failure<ReservationDto>(ReservationErrors.NotFound)
            : Result.Success(ReservationQueries.WithRemainingSeconds(dto, _clock.UtcNow));
    }
}

// ===================================================================
// KULLANICININ REZERVASYONLARI
// PDF: GET /api/v1/users/me/reservations
// ===================================================================

public sealed record GetMyReservationsQuery(ReservationStatus? Status)
    : IRequest<Result<IReadOnlyList<ReservationDto>>>;

internal sealed class GetMyReservationsQueryHandler
    : IRequestHandler<GetMyReservationsQuery, Result<IReadOnlyList<ReservationDto>>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public GetMyReservationsQueryHandler(
        IApplicationDbContext context,
        ICurrentUser currentUser,
        IDateTimeProvider clock)
    {
        _context = context;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result<IReadOnlyList<ReservationDto>>> Handle(
        GetMyReservationsQuery request,
        CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not Guid userId)
        {
            return Result.Failure<IReadOnlyList<ReservationDto>>(
                Error.Unauthorized("auth.required", "Giris yapmalisiniz."));
        }

        var query = _context.Reservations.AsNoTracking().Where(r => r.UserId == userId);

        if (request.Status.HasValue)
        {
            query = query.Where(r => r.Status == request.Status.Value);
        }

        var ids = await query
            // En yeni rezervasyon en ustte: kullanici genelde en son
            // yaptigi islemi ariyor.
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var items = await _context.Reservations
            .Where(r => ids.Contains(r.Id))
            .ToDto(_context)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Siralamayi BELLEKTE koruyorum.
        //
        // Contains ile filtreleme, ids listesinin sirasini KORUMAZ --
        // PostgreSQL sonuclari istedigi sirada dondurebilir.
        // Bu, kolayca gozden kacan ve "bazen sirali bazen degil"
        // gibi kafa karistirici bir hataya yol acan bir ayrinti.
        var now = _clock.UtcNow;

        var ordered = ids
            .Select(id => items.FirstOrDefault(i => i.Id == id))
            .Where(i => i is not null)
            .Select(i => ReservationQueries.WithRemainingSeconds(i!, now))
            .ToList();

        return Result.Success<IReadOnlyList<ReservationDto>>(ordered);
    }
}
