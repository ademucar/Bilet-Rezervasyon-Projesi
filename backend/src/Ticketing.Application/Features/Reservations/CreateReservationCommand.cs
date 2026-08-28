using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Ticketing.Application.Abstractions.Persistence;
using Ticketing.Application.Abstractions.RealTime;
using Ticketing.Application.Abstractions.Security;
using Ticketing.Application.Abstractions.Time;
using Ticketing.Application.Common.Options;
using Microsoft.Extensions.Logging;
using Ticketing.Application.Common.Logging;
using Ticketing.Application.Common.Results;
using Ticketing.Application.Features.Outbox;
using Ticketing.Domain.Entities;
using Ticketing.Domain.Enums;

namespace Ticketing.Application.Features.Reservations;

internal static class ReservationErrors
{
    public static readonly Error NotFound = Error.NotFound(
        "reservation.not_found", "Rezervasyon bulunamadi.");

    public static readonly Error SessionNotFound = Error.NotFound(
        "reservation.session_not_found", "Etkinlik oturumu bulunamadi.");

    public static readonly Error SalesNotOpen = Error.Conflict(
        "reservation.sales_not_open",
        "Bu etkinlik icin bilet satisi su anda acik degil.");

    public static readonly Error SeatsNotFound = Error.Validation(
        "reservation.seats_not_found",
        "Secilen koltuklardan bazilari bu oturuma ait degil.");

    public static readonly Error TicketLimitExceeded = Error.Conflict(
        "reservation.ticket_limit_exceeded",
        "Bu etkinlik icin alabileceginiz maksimum bilet sayisini asiyorsunuz.");

    public static readonly Error NotOwner = Error.Forbidden(
        "reservation.not_owner", "Bu rezervasyon size ait degil.");

    /// <summary>
    /// PROJENIN EN KRITIK HATASI.
    /// Iki kullanici ayni koltugu ayni anda aldiginda kaybedene doner.
    /// </summary>
    public static readonly Error SeatConflict = Error.Concurrency(
        "reservation.seat_conflict",
        "Sectiginiz koltuklardan bazilari az once baskasi tarafindan alindi. " +
        "Lutfen koltuk planini yenileyip tekrar deneyin.");
}

// ===================================================================
// REZERVASYON OLUSTURMA -- PDF: POST /api/v1/reservations
// ===================================================================

/// <summary>
/// PDF Sprint 7'nin ana komutu.
///
/// DIKKAT: Bu komutta TOPLAM TUTAR ALANI YOK -- bilerek.
///
/// PDF Sprint 6: "Frontend tarafindan gonderilen toplam tutara
/// guvenilmemelidir." Alan hic olmadigi icin istemci tutar
/// GONDEREMIYOR. Guvenligi kural ile degil TIP SISTEMI ile
/// sagliyoruz; unutulmasi imkansiz.
/// </summary>
public sealed record CreateReservationCommand(
    Guid EventSessionId,
    IReadOnlyList<Guid> EventSeatIds,
    string? IdempotencyKey) : IRequest<Result<ReservationDto>>;

public sealed class CreateReservationCommandValidator : AbstractValidator<CreateReservationCommand>
{
    public CreateReservationCommandValidator()
    {
        RuleFor(x => x.EventSeatIds)
            .NotEmpty().WithMessage("En az bir koltuk secmelisiniz.")
            // Ust sinir: MaxTicketsPerUser zaten kontrol ediliyor ama o
            // veritabanina gitmeyi gerektiriyor. Buradaki kaba sinir,
            // 10.000 koltuk iceren bir istegin veritabanina hic
            // ulasmamasini sagliyor.
            .Must(ids => ids is null || ids.Count <= 50)
            .WithMessage("Tek seferde en fazla 50 koltuk secilebilir.");

        RuleFor(x => x.IdempotencyKey)
            .MaximumLength(100)
            .When(x => x.IdempotencyKey is not null);
    }
}

internal sealed partial class CreateReservationCommandHandler
    : IRequestHandler<CreateReservationCommand, Result<ReservationDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly ReservationOptions _options;
    private readonly ISeatNotifier _seatNotifier;
    private readonly ILogger<CreateReservationCommandHandler> _logger;

    public CreateReservationCommandHandler(
        IApplicationDbContext context,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        IOptions<ReservationOptions> options,
        ISeatNotifier seatNotifier,
        ILogger<CreateReservationCommandHandler> logger)
    {
        _context = context;
        _currentUser = currentUser;
        _clock = clock;
        _options = options.Value;
        _seatNotifier = seatNotifier;
        _logger = logger;
    }

    // ==============================================================
    // PDF Sprint 16: "Koltuk kilitleme"
    // ==============================================================
    // PDF bunu rezervasyondan AYRI bir madde olarak istiyor ve hakli:
    // koltuk kilitleme, projedeki en yogun yaris kosulunun yasandigi
    // nokta.
    // ==============================================================
    [LoggerMessage(
        EventId = LogEvents.KoltuklarKilitlendi,
        Level = LogLevel.Information,
        Message = "Koltuklar kilitlendi. Oturum: {SessionId}, Koltuk: {SeatCount}, Sure: {LockMinutes} dk")]
    private static partial void LogSeatsLocked(
        ILogger logger, Guid sessionId, int seatCount, int lockMinutes);

    /// <remarks>
    /// ==============================================================
    /// CAKISMA NEDEN AYRI VE NEDEN WARNING?
    /// ==============================================================
    /// Bu satir olmadan "koltugu secmistim ama alamadim" sikayetini
    /// arastirmak imkansiz: kullanicinin ekraninda koltuk BOSTU,
    /// veritabaninda ise baskasina ait. Log olmadan hangi iki istegin
    /// carpistigini goremeyiz.
    ///
    /// Warning cunku bu bir HATA DEGIL -- sistem tam olarak dogru
    /// calisti ve veri butunlugunu korudu. Ama SIKLIGI onemli:
    /// cakisma orani aniden artiyorsa ya bot trafigi var ya da bir
    /// etkinlik beklenenden populer. Ikisi de mudahale gerektirir.
    ///
    /// Error yapsaydik izleme panosu surekli alarm calardi ve gercek
    /// hatalar bu gurultude kaybolurdu (Sprint 15'te konustugumuz
    /// alarm yorgunlugu).
    /// </remarks>
    [LoggerMessage(
        EventId = LogEvents.KoltukCakismasi,
        Level = LogLevel.Warning,
        Message = "Koltuk cakismasi. Oturum: {SessionId}, Istenen koltuk: {SeatCount}")]
    private static partial void LogSeatConflict(ILogger logger, Guid sessionId, int seatCount);

    [LoggerMessage(
        EventId = LogEvents.RezervasyonOlusturuldu,
        Level = LogLevel.Information,
        Message = "Rezervasyon olusturuldu. Id: {ReservationId}, Kod: {Code}, Koltuk: {SeatCount}")]
    private static partial void LogReservationCreated(
        ILogger logger, Guid reservationId, string code, int seatCount);

    public async Task<Result<ReservationDto>> Handle(
        CreateReservationCommand request,
        CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not Guid userId)
        {
            return Result.Failure<ReservationDto>(
                Error.Unauthorized("auth.required", "Giris yapmalisiniz."));
        }

        var now = _clock.UtcNow;

        // ==============================================================
        // 1. IDEMPOTENCY -- ONCE KONTROL
        // ==============================================================
        // PDF Sprint 15: "Ayni istegin tekrar gonderilmesine karsi
        // idempotency uygulanmalidir."
        //
        // Kullanici butona iki kez bastiysa AYNI rezervasyonu donuyoruz,
        // yenisini olusturmuyoruz.
        //
        // Bu kontrol yarisa acik (iki istek ayni anda gelirse ikisi de
        // "yok" gorebilir) -- ama sorun degil: veritabanindaki partial
        // unique index ikincisini reddedecek ve asagida yakalayacagiz.
        // Buradaki kontrol YAYGIN durumu (kullanici 2 saniye sonra
        // tekrar bastı) ucuz sekilde cozuyor.
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var existing = await _context.Reservations
                .AsNoTracking()
                .Where(r => r.IdempotencyKey == request.IdempotencyKey && r.UserId == userId)
                .Select(r => r.Id)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (existing != Guid.Empty)
            {
                return await LoadDtoAsync(existing, now, cancellationToken).ConfigureAwait(false);
            }
        }

        // ==============================================================
        // 2. SATIS ACIK MI?
        // ==============================================================
        var sessionInfo = await _context.EventSessions
            .AsNoTracking()
            .Where(s => s.Id == request.EventSessionId)
            .Select(s => new
            {
                s.Id,
                s.Status,
                s.EventId,
                EventStatus = s.Event.Status,
                s.Event.MaxTicketsPerUser,
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (sessionInfo is null)
        {
            return Result.Failure<ReservationDto>(ReservationErrors.SessionNotFound);
        }

        // Rezervasyon YALNIZCA SalesOpen durumunda yapilabilir.
        //
        // Bu kontrol olmasaydi kullanici, henuz yayinlanmamis veya
        // satisi kapanmis bir etkinlige rezervasyon yapabilirdi --
        // Id'yi bir yerden bulmasi yeterdi.
        if (sessionInfo.EventStatus != EventStatus.SalesOpen ||
            sessionInfo.Status != EventSessionStatus.Scheduled)
        {
            return Result.Failure<ReservationDto>(ReservationErrors.SalesNotOpen);
        }

        // ==============================================================
        // 3. KULLANICI BILET LIMITI
        // ==============================================================
        // PDF: "Bir kullanici ayni oturum icin belirlenen maksimum
        // bilet sayisini asamaz."
        //
        // Karaborsaciligi engellemek icin. Mevcut AKTIF rezervasyonlari
        // da sayiyorum -- yoksa kullanici 4'er 4'er 10 rezervasyon
        // yapip limiti atlatirdi.
        var activeTicketCount = await _context.ReservationItems
            .AsNoTracking()
            .CountAsync(
                ri => ri.Reservation.UserId == userId
                   && ri.Reservation.EventSessionId == request.EventSessionId
                   && (ri.Reservation.Status == ReservationStatus.Locked
                    || ri.Reservation.Status == ReservationStatus.PaymentPending
                    || ri.Reservation.Status == ReservationStatus.Confirmed),
                cancellationToken)
            .ConfigureAwait(false);

        if (activeTicketCount + request.EventSeatIds.Count > sessionInfo.MaxTicketsPerUser)
        {
            return Result.Failure<ReservationDto>(ReservationErrors.TicketLimitExceeded);
        }

        // ==============================================================
        // 4. KOLTUKLARI YUKLE -- TAKIP EDILEREK (AsNoTracking YOK!)
        // ==============================================================
        // Bu koltuklari DEGISTIRECEGIZ (kilitleyecegiz). AsNoTracking
        // kullansaydik EF degisiklikleri fark etmez ve SaveChanges
        // hicbir sey yazmazdi -- kilit sessizce uygulanmazdi.
        //
        // ORDER BY Id: bu satir DEADLOCK'U ENGELLIYOR.
        //
        // Iki kullanici {A, B} ve {B, A} koltuklarini isterse ve biz
        // istedikleri sirada kilitlersek, birinci A'yi ikinci B'yi
        // kilitler ve ikisi de digerini bekler -> deadlock.
        //
        // Her zaman AYNI sirada (Id'ye gore) islem yaparak bu
        // ihtimali tamamen ortadan kaldiriyoruz. Bu, kilit
        // siralamasinin klasik cozumudur.
        var seats = await _context.EventSeats
            .Where(es => request.EventSeatIds.Contains(es.Id)
                      && es.EventSessionId == request.EventSessionId)
            .OrderBy(es => es.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Istenen koltuklarin HEPSI bulundu mu?
        //
        // Bulunamayan varsa ya baska oturuma ait ya da hic yok.
        // Kismi rezervasyon YAPMIYORUZ: kullanici 4 koltuk istedi,
        // 3'unu alip "al bakalim" demek kotu bir deneyim olurdu.
        if (seats.Count != request.EventSeatIds.Distinct().Count())
        {
            return Result.Failure<ReservationDto>(ReservationErrors.SeatsNotFound);
        }

        // ==============================================================
        // 5. REZERVASYONU OLUSTUR
        // ==============================================================
        // Reservation.Create koltuklari KILITLIYOR ve toplam tutari
        // koltuklarin KENDI fiyatlarindan hesapliyor.
        //
        // Koltuklardan biri musait degilse DomainException firlar ve
        // hicbir sey kaydedilmez -- "ya hep ya hic".
        Reservation reservation;

        try
        {
            reservation = Reservation.Create(
                userId,
                request.EventSessionId,
                seats,
                TimeSpan.FromMinutes(_options.LockDurationMinutes),
                now,
                request.IdempotencyKey);
        }
        catch (Domain.Common.DomainException ex)
            when (ex.ErrorCode is "seat.already_locked" or "seat.already_sold")
        {
            // Koltuk BELLEKTE dolu gorundu (yaygin durum).
            //
            // DomainException'i disari birakmiyorum cunku global handler
            // onu 422 yapardi. Burada 409 Conflict daha dogru: bu bir
            // CAKISMA, is kurali ihlali degil. Frontend 409'da koltuk
            // haritasini yeniliyor.
            return Result.Failure<ReservationDto>(ReservationErrors.SeatConflict);
        }

        _context.Reservations.Add(reservation);

        // ==============================================================
        // 6. KAYDET -- ASIL YARIS BURADA COZULUYOR
        // ==============================================================
        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            // PDF Sprint 16: "Koltuk kilitleme".
            //
            // SaveChanges'ten SONRA: kilit ancak veritabani onayladiysa
            // gercek. Once loglasaydik, asagidaki catch'e dusen her
            // cakismada logda "kilitlendi" satiri kalirdi -- ve o satir
            // yalan olurdu.
            LogSeatsLocked(
                _logger,
                request.EventSessionId,
                seats.Count,
                _options.LockDurationMinutes);
        }
        catch (DbUpdateConcurrencyException)
        {
            LogSeatConflict(_logger, request.EventSessionId, seats.Count);

            // ==========================================================
            // OPTIMISTIC CONCURRENCY DEVREDE
            // ==========================================================
            // Biz koltugu okuduktan SONRA baska bir istek onu kilitledi.
            //
            // EF'in urettigi UPDATE:
            //     UPDATE "EventSeats" SET "Status" = 2, ...
            //     WHERE "Id" = @id AND xmin = @okudugumDeger
            //
            // xmin degistigi icin 0 satir etkilendi -> exception.
            //
            // KRITIK NOKTA: bizim istegimiz KAYBETTI ama hicbir veriyi
            // BOZMADI. Digerinin kilidinin uzerine YAZMADIK.
            //
            // Bu kontrol olmasaydi "son yazan kazanir" davranisi
            // olusur ve ayni koltuk iki kisiye satilirdi.
            return Result.Failure<ReservationDto>(ReservationErrors.SeatConflict);
        }
        catch (DbUpdateException)
        {
            // Unique index ihlali: ya idempotency key cakismasi ya da
            // baska bir kisit.
            //
            // Idempotency key ise, ILK istegin sonucunu donmeliyiz --
            // kullanicinin iki kez basmasi hata degil.
            if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
            {
                var existing = await _context.Reservations
                    .AsNoTracking()
                    .Where(r => r.IdempotencyKey == request.IdempotencyKey && r.UserId == userId)
                    .Select(r => r.Id)
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (existing != Guid.Empty)
                {
                    return await LoadDtoAsync(existing, now, cancellationToken).ConfigureAwait(false);
                }
            }

            return Result.Failure<ReservationDto>(ReservationErrors.SeatConflict);
        }

        // ==============================================================
        // BILDIRIM -- PDF Sprint 14: "Rezervasyon olusturuldugunda"
        // ==============================================================
        // Uygulama ici bildirim BURADA, ayni transaction'da yaziliyor.
        //
        // Neden Outbox degil? Cunku bu bildirim DIS bir sisteme
        // gitmiyor -- kendi veritabanimiza yaziliyor. Outbox'in varlik
        // sebebi "iki sistem arasinda atomiklik saglamak"; burada tek
        // sistem var.
        //
        // E-POSTA ise Outbox'a gidiyor (asagida): o gercekten dis bir
        // servise cikiyor ve yavas olabilir.
        // ==============================================================
        _context.Notifications.Add(Notification.Create(
            userId,
            Domain.Enums.NotificationType.ReservationCreated,
            "Rezervasyonunuz olusturuldu",
            $"{reservation.ReservationCode} numarali rezervasyonunuz icin " +
            $"{seats.Count} koltuk ayrildi. Odemeyi tamamlamak icin " +
            $"{_options.LockDurationMinutes} dakikaniz var.",
            reservation.Id,
            "/rezervasyonlarim"));

        _context.OutboxMessages.Add(OutboxMessage.Create(
            OutboxMessageTypes.ReservationCreated,
            System.Text.Json.JsonSerializer.Serialize(new ReservationCreatedPayload(
                reservation.Id,
                userId,
                reservation.ReservationCode,
                seats.Count,
                _options.LockDurationMinutes))));

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // ==============================================================
        // GERCEK ZAMANLI BILDIRIM -- PDF Sprint 10: "SeatLocked"
        // ==============================================================
        // SaveChangesAsync'ten SONRA cagriliyor. Bu sira ZORUNLU.
        //
        // Once bildirseydik ve kayit DbUpdateConcurrencyException ile
        // basarisiz olsaydi, oturumu izleyen herkes koltugu KILITLI
        // gorurdu -- oysa koltuk bosta. Kimse alamazdi ve kimse
        // nedenini anlayamazdi.
        //
        // Commit sonrasi bildirmek, "gordugunu soyle" ilkesi:
        // yalnizca GERCEKLESMIS bir seyi duyuruyoruz.
        //
        // PDF is kurali: "Bir koltuk baska kullanici tarafindan
        // secildiginde ekran guncellenmelidir."
        // ==============================================================
        await _seatNotifier.SeatsLockedAsync(
            request.EventSessionId,
            seats.ConvertAll(s => s.Id),
            cancellationToken).ConfigureAwait(false);

        // PDF Sprint 16: "Rezervasyon olusturma".
        //
        // Rezervasyon KODUNU logluyorum cunku destek talebinde
        // kullanicinin elindeki tek tanimlayici o ("ABC-123 numarali
        // rezervasyonum"). Guid'i kullanici bilmiyor.
        LogReservationCreated(
            _logger, reservation.Id, reservation.ReservationCode, seats.Count);

        return await LoadDtoAsync(reservation.Id, now, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<ReservationDto>> LoadDtoAsync(
        Guid reservationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var dto = await _context.Reservations
            .Where(r => r.Id == reservationId)
            .ToDto(_context)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return dto is null
            ? Result.Failure<ReservationDto>(ReservationErrors.NotFound)
            : Result.Success(ReservationQueries.WithRemainingSeconds(dto, now));
    }
}
