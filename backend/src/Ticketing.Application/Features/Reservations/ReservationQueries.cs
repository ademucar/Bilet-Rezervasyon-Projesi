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

/// <param name="RemainingSeconds">Kalan süre (saniye). Neden SUNUCUDA hesapliyorum? Çünkü istemcinin saati YANLIS olabilir. Frontend ExpiresAt - Date.now() hesaplasaydi, saati 5 dakika geri olan bir kullanıcı süreyi 15 dakika sanirdi ve ödemeye geçtiğinde "süreniz doldu" hatası alırdı. Saniye cinsinden gonderip frontend'in kendi içinde geri saymasi, saat farkindan bağımsız çalışır.</param>
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
    /// Ayrı bir metotta topluyorum çünkü UC yerde kullanılıyor:
    /// oluşturma sonucu, detay, kullanıcı listesi. Uc kez yazsaydim
    /// birinde bir alanı eklemeyi unutmam kacinilmazdi ve o ekranda
    /// veri eksik görünürdü.
    /// </summary>
    /// <summary>
    /// Materyalize edilmiş DTO'ya kalan süreyi ekler.
    ///
    /// ==================================================================
    /// NEDEN SORGUDA HESAPLAMIYORUZ? -- CALISTIRINCA OGRENDIK
    /// ==================================================================
    /// İlk yazisimda kalan süreyi SQL içinde hesapliyordum:
    ///
    ///     (int)(r.ExpiresAt > now ? (r.ExpiresAt - now).TotalSeconds : 0)
    ///
    /// Derlendi. Ama CALISMA ZAMANINDA patladi:
    ///     InvalidOperationException: The LINQ expression ...
    ///     could not be translated
    ///
    /// Sebep: DateTimeOffset cikarmasi + TimeSpan.TotalSeconds +
    /// int'e donusum zinciri Npgsql tarafından SQL'e cevrilemiyor.
    ///
    /// Bu hata ES ZAMANLILIK TESTINDE ortaya cikti ve ogretici oldu:
    /// 10 es zamanlı istekten 9'u doğru şekilde 409 aldi, 1'i
    /// rezervasyonu OLUSTURDU ama yaniti hazirlarken 500 dondu.
    /// Yani cekirdek mantik dogruydu, sunum katmani hatalıydı.
    ///
    /// Bellekte hesaplamak hem cevrilebilirlik sorununu cozuyor hem de
    /// doğru: kalan süre bir GORUNUM bilgisi, veritabaninin isi değil.
    /// </summary>
    public static ReservationDto WithRemainingSeconds(ReservationDto dto, DateTimeOffset now)
    {
        var remaining = dto.ExpiresAt - now;

        // Negatif süre dondurmuyorum: frontend geri sayimda "-00:03"
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
    /// İlk yazisimda bu metot doğrudan context.Reservations üzerinden
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
    /// Çünkü EF, DTO nesnesini olusturup sonra uzerinde filtre
    /// uygulayamiyor -- filtrenin WHERE cumlesine donusebilmesi için
    /// ENTITY uzerinde olmasını gerekiyor.
    ///
    /// Cozum: filtrelenmis IQueryable&lt;Reservation&gt; ALMAK. Boylece
    /// cagiran önce filtreliyor, sonra projelendiriyoruz:
    ///
    ///     context.Reservations.Where(r =&gt; r.Id == id).ToDto(context)
    ///
    /// Bu hata ES ZAMANLILIK TESTINDE ortaya cikti: 9 istek doğru
    /// şekilde 409 aldi, kazanan istek rezervasyonu OLUSTURDU ama
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

                // Kalan süre burada 0 olarak birakiliyor; SQL'e
                // cevrilemedigi için BELLEKTE, WithRemainingSeconds
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
                Error.Unauthorized("auth.required", "Giriş yapmalisiniz."));
        }

        // ==============================================================
        // SAHIPLIK KONTROLU -- SORGUNUN ICINDE
        // ==============================================================
        // "Önce cek, sonra sahibi mi diye bak" da yapabilirdik. Ama o
        // zaman baskasinin rezervasyonu bir an için bellege gelirdi ve
        // bir loglama veya hata mesajinda sizabilirdi.
        //
        // Sorguya dahil etmek daha güvenli: veri hiç gelmiyor.
        //
        // Sonuç bulunamazsa 404 donuyoruz (403 değil) -- var olan bir
        // rezervasyonun varligini dogrulamamak için.
        // Filtreyi ENTITY uzerinde uyguluyorum, DTO uzerinde değil.
        //
        // Sahiplik kontrolü de burada: baskasinin rezervasyonu hiç
        // bellege gelmiyor. Sonuç bulunamazsa 404 -- 403 deseydik
        // rezervasyonun VAR olduğunu dogrulamis olurduk.
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
                Error.Unauthorized("auth.required", "Giriş yapmalisiniz."));
        }

        var query = _context.Reservations.AsNoTracking().Where(r => r.UserId == userId);

        if (request.Status.HasValue)
        {
            query = query.Where(r => r.Status == request.Status.Value);
        }

        var ids = await query
            // En yeni rezervasyon en ustte: kullanıcı genelde en son
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
        // Contains ile filtreleme, ids listesinin sırasını KORUMAZ --
        // PostgreSQL sonuclari istedigi sırada dondurebilir.
        // Bu, kolayca gozden kacan ve "bazen sıralı bazen değil"
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
