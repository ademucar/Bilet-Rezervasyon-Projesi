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
        "reservation.not_found", "Rezervasyon bulunamadı.");

    public static readonly Error SessionNotFound = Error.NotFound(
        "reservation.session_not_found", "Etkinlik oturumu bulunamadı.");

    public static readonly Error SalesNotOpen = Error.Conflict(
        "reservation.sales_not_open",
        "Bu etkinlik için bilet satışı su anda açık değil.");

    public static readonly Error SeatsNotFound = Error.Validation(
        "reservation.seats_not_found",
        "Secilen koltuklardan bazilari bu oturuma ait değil.");

    public static readonly Error TicketLimitExceeded = Error.Conflict(
        "reservation.ticket_limit_exceeded",
        "Bu etkinlik için alabileceginiz maksimum bilet sayisini asiyorsunuz.");

    public static readonly Error NotOwner = Error.Forbidden(
        "reservation.not_owner", "Bu rezervasyon size ait değil.");

    /// <summary>
    /// Projenin en kritik hatasi.
    /// Iki kullanıcı aynı koltuğu aynı anda aldiginda kaybedene döner.
    /// </summary>
    public static readonly Error SeatConflict = Error.Concurrency(
        "reservation.seat_conflict",
        "Seçtiğiniz koltuklardan bazilari az önce başkası tarafından alındı. " +
        "Lütfen koltuk planini yenileyip tekrar deneyin.");
}

// Rezervasyon olusturma -- PDF: POST /api/v1/reservations

/// <summary>
/// PDF Sprint 7'nin ana komutu.
///
/// Dikkat: Bu komutta toplam tutar alani yok -- bilerek.
///
/// PDF Sprint 6: "Frontend tarafından gonderilen toplam tutara
/// güvenilmemelidir." Alan hiç olmadığı için istemci tutar
/// gonderemiyor. Guvenligi kural ile değil tip sistemi ile
/// sagliyorum; unutulmasi imkansiz.
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
            // 10.000 koltuk iceren bir istegin veritabanina hiç
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

    // PDF Sprint 16: "Koltuk kilitleme"
    //
    // PDF bunu rezervasyondan AYRI bir madde olarak istiyor ve haklı:
    // koltuk kilitleme, projedeki en yogun yaris kosulunun yasandigi
    // nokta.
    [LoggerMessage(
        EventId = LogEvents.KoltuklarKilitlendi,
        Level = LogLevel.Information,
        Message = "Koltuklar kilitlendi. Oturum: {SessionId}, Koltuk: {SeatCount}, Süre: {LockMinutes} dk")]
    private static partial void LogSeatsLocked(
        ILogger logger, Guid sessionId, int seatCount, int lockMinutes);

    /// <remarks>
    /// Cakisma neden ayri ve neden warning?
    ///
    /// Bu satır olmadan "koltuğu secmistim ama alamadim" sikayetini
    /// arastirmak imkansiz: kullanıcının ekraninda koltuk BOSTU,
    /// veritabaninda ise baskasina ait. Log olmadan hangi iki istegin
    /// carpistigini goremem.
    ///
    /// Warning çünkü bu bir hata değil -- sistem tam olarak doğru
    /// calisti ve veri butunlugunu korudu. Ama SIKLIGI önemli:
    /// çakışma oranı aniden artiyorsa ya bot trafigi var ya da bir
    /// etkinlik beklenenden popüler. Ikisi de mudahale gerektirir.
    ///
    /// Error yapsaydim izleme panosu surekli alarm calardi ve gerçek
    /// hatalar bu gurultude kaybolurdu (Sprint 15'te konustugum
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
        Message = "Rezervasyon oluşturuldu. Id: {ReservationId}, Kod: {Code}, Koltuk: {SeatCount}")]
    private static partial void LogReservationCreated(
        ILogger logger, Guid reservationId, string code, int seatCount);

    public async Task<Result<ReservationDto>> Handle(
        CreateReservationCommand request,
        CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not Guid userId)
        {
            return Result.Failure<ReservationDto>(
                Error.Unauthorized("auth.required", "Giriş yapmalisiniz."));
        }

        var now = _clock.UtcNow;

        // 1. İdempotency -- once kontrol
        //
        // PDF Sprint 15: "Aynı istegin tekrar gonderilmesine karsi
        // idempotency uygulanmalıdır."
        //
        // Kullanıcı butona iki kez bastiysa AYNI rezervasyonu donuyorum,
        // yenisini olusturmuyoruz.
        //
        // Bu kontrol yarisa açık (iki istek aynı anda gelirse ikisi de
        // "yok" görebilir) -- ama sorun değil: veritabanindaki partial
        // unique index ikincisini reddedecek ve aşağıda yakalayacagim.
        // Buradaki kontrol YAYGIN durumu (kullanıcı 2 saniye sonra
        // tekrar bastı) ucuz şekilde cozuyor.
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

        // 2. Satis acik mi?
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
        // Bu kontrol olmasaydı kullanıcı, henüz yayinlanmamis veya
        // satışı kapanmis bir etkinlige rezervasyon yapabilirdi --
        // Id'yi bir yerden bulmasi yeterdi.
        if (sessionInfo.EventStatus != EventStatus.SalesOpen ||
            sessionInfo.Status != EventSessionStatus.Scheduled)
        {
            return Result.Failure<ReservationDto>(ReservationErrors.SalesNotOpen);
        }

        // 3. Kullanici bilet limiti
        //
        // PDF: "Bir kullanıcı aynı oturum için belirlenen maksimum
        // bilet sayisini aşamaz."
        //
        // Karaborsaciligi engellemek için. Mevcut AKTIF rezervasyonları
        // da sayiyorum -- yoksa kullanıcı 4'er 4'er 10 rezervasyon
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

        // 4. Koltuklari yukle -- takip edilerek (AsNoTracking yok!)
        //
        // Bu koltukları DEGISTIRECEGIZ (kilitleyecegiz). AsNoTracking
        // kullansaydım EF değişiklikleri fark etmez ve SaveChanges
        // hiçbir sey yazmazdi -- kilit sessizce uygulanmazdi.
        //
        // ORDER BY Id: bu satır DEADLOCK'U ENGELLIYOR.
        //
        // Iki kullanıcı {A, B} ve {B, A} koltuklarini isterse ve biz
        // istedikleri sırada kilitlersek, birinci A'yi ikinci B'yi
        // kilitler ve ikisi de digerini bekler -> deadlock.
        //
        // Her zaman AYNI sırada (Id'ye göre) işlem yaparak bu
        // ihtimali tamamen ortadan kaldiriyorum. Bu, kilit
        // siralamasinin klasik cozumudur.
        var seats = await _context.EventSeats
            .Where(es => request.EventSeatIds.Contains(es.Id)
                      && es.EventSessionId == request.EventSessionId)
            .OrderBy(es => es.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Istenen koltuklarin HEPSI bulundu mu?
        //
        // Bulunamayan varsa ya başka oturuma ait ya da hiç yok.
        // Kismi rezervasyon YAPMIYORUM: kullanıcı 4 koltuk istedi,
        // 3'unu alip "al bakayim" demek kötü bir deneyim olurdu.
        if (seats.Count != request.EventSeatIds.Distinct().Count())
        {
            return Result.Failure<ReservationDto>(ReservationErrors.SeatsNotFound);
        }

        // 5. Rezervasyonu olustur
        //
        // Reservation.Create koltukları KILITLIYOR ve toplam tutarı
        // koltuklarin KENDİ fiyatlarindan hesapliyor.
        //
        // Koltuklardan biri musait degilse DomainException firlar ve
        // hiçbir sey kaydedilmez -- "ya hep ya hiç".
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
            // DomainException'i disari birakmiyorum çünkü global handler
            // önü 422 yapardi. Burada 409 Conflict daha doğru: bu bir
            // CAKISMA, is kuralı ihlali değil. Frontend 409'da koltuk
            // haritasini yeniliyor.
            return Result.Failure<ReservationDto>(ReservationErrors.SeatConflict);
        }

        _context.Reservations.Add(reservation);

        // 6. Kaydet -- asil yaris burada cozuluyor
        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            // PDF Sprint 16: "Koltuk kilitleme".
            //
            // SaveChanges'ten SONRA: kilit ancak veritabani onayladiysa
            // gerçek. Önce loglasaydim, aşağıdaki catch'e dusen her
            // cakismada logda "kilitlendi" satiri kalırdı -- ve o satır
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

            // Optimistic concurrency devrede
            //
            // Biz koltuğu okuduktan SONRA başka bir istek önü kilitledi.
            //
            // EF'in urettigi UPDATE:
            //     UPDATE "EventSeats" SET "Status" = 2, ...
            //     WHERE "Id" = @id AND xmin = @okudugumDeger
            //
            // xmin degistigi için 0 satır etkilendi -> exception.
            //
            // Kritik nokta: benim istegim kaybetti ama hiçbir veriyi
            // bozmadi. Digerinin kilidinin uzerine yazmadik.
            //
            // Bu kontrol olmasaydı "son yazan kazanir" davranisi
            // olusur ve aynı koltuk iki kisiye satilirdi.
            return Result.Failure<ReservationDto>(ReservationErrors.SeatConflict);
        }
        catch (DbUpdateException)
        {
            // Unique index ihlali: ya idempotency key çakışması ya da
            // başka bir kisit.
            //
            // Idempotency key ise, ILK istegin sonucunu donmeliyim --
            // kullanıcının iki kez basmasi hata değil.
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

        // BILDIRIM -- PDF Sprint 14: "Rezervasyon olusturuldugunda"
        //
        // Uygulama ici bildirim BURADA, aynı transaction'da yaziliyor.
        //
        // Neden Outbox değil? Çünkü bu bildirim DIS bir sisteme
        // gitmiyor -- kendi veritabanimiza yaziliyor. Outbox'in varlik
        // sebebi "iki sistem arasında atomiklik saglamak"; burada tek
        // sistem var.
        //
        // E-POSTA ise Outbox'a gidiyor (aşağıda): o gerçekten dis bir
        // servise cikiyor ve yavas olabilir.
        _context.Notifications.Add(Notification.Create(
            userId,
            Domain.Enums.NotificationType.ReservationCreated,
            "Rezervasyonunuz oluşturuldu",
            $"{reservation.ReservationCode} numarali rezervasyonunuz için " +
            $"{seats.Count} koltuk ayrildi. Ödemeyi tamamlamak için " +
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

        // Gercek zamanli bildirim -- PDF Sprint 10: "SeatLocked"
        //
        // SaveChangesAsync'ten sonra cagriliyor. Bu sıra zorunlu.
        //
        // Önce bildirseydik ve kayıt DbUpdateConcurrencyException ile
        // başarısız olsaydı, oturumu izleyen herkes koltuğu KILITLI
        // gorurdu -- oysa koltuk bosta. Kimse alamazdi ve kimse
        // nedenini anlayamazdi.
        //
        // Commit sonrası bildirmek, "gordugunu soyle" ilkesi:
        // yalnızca GERCEKLESMIS bir seyi duyuruyoruz.
        //
        // PDF is kuralı: "Bir koltuk başka kullanıcı tarafından
        // secildiginde ekran guncellenmelidir."
        await _seatNotifier.SeatsLockedAsync(
            request.EventSessionId,
            seats.ConvertAll(s => s.Id),
            cancellationToken).ConfigureAwait(false);

        // PDF Sprint 16: "Rezervasyon oluşturma".
        //
        // Rezervasyon KODUNU logluyorum çünkü destek talebinde
        // kullanıcının elindeki tek tanimlayici o ("ABC-123 numarali
        // rezervasyonum"). Guid'i kullanıcı bilmiyor.
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
