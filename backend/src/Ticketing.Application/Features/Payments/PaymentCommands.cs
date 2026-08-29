using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Abstractions.Payments;
using Ticketing.Application.Abstractions.Persistence;
using Ticketing.Application.Abstractions.RealTime;
using Ticketing.Application.Abstractions.Security;
using Ticketing.Application.Abstractions.Time;
using Microsoft.Extensions.Logging;
using Ticketing.Application.Common.Logging;
using Ticketing.Application.Common.Results;
using Ticketing.Application.Features.Outbox;
using Ticketing.Domain.Entities;
using Ticketing.Domain.Enums;
using Ticketing.Domain.ValueObjects;

namespace Ticketing.Application.Features.Payments;

internal static class PaymentErrors
{
    public static readonly Error NotFound = Error.NotFound(
        "payment.not_found", "Ödeme bulunamadı.");

    public static readonly Error ReservationNotFound = Error.NotFound(
        "payment.reservation_not_found", "Rezervasyon bulunamadı.");

    public static readonly Error ReservationNotPayable = Error.Conflict(
        "payment.reservation_not_payable",
        "Bu rezervasyon için ödeme baslatilamaz. Süresi dolmuş veya iptal edilmiş olabilir.");

    public static readonly Error AlreadyPaid = Error.Conflict(
        "payment.already_paid",
        "Bu rezervasyon için zaten başarılı bir ödeme yapilmis.");

    public static readonly Error ProviderRejected = Error.Conflict(
        "payment.provider_rejected", "Ödeme sağlayıcısı islemi reddetti.");

    public static readonly Error VerificationFailed = Error.Conflict(
        "payment.verification_failed",
        "Ödeme sağlayıcı tarafında dogrulanamadi.");

    public static readonly Error NotRefundable = Error.Conflict(
        "payment.not_refundable", "Bu ödeme iade edilemez.");
}

// ===================================================================
// ÖDEME BASLATMA -- PDF: POST /api/v1/payments
// ===================================================================

/// <summary>
/// DIKKAT: Tutar alanı YOK -- rezervasyondan okunuyor.
///
/// PDF Sprint 6: "Frontend tarafından gonderilen toplam tutara
/// güvenilmemelidir." Alan hiç olmadığı için istemci 500 TL'lik
/// bileti 1 TL'ye ödemeyi DENEYEMEZ bile.
/// </summary>
public sealed record CreatePaymentCommand(Guid ReservationId, string? IdempotencyKey)
    : IRequest<Result<PaymentDto>>;

public sealed class CreatePaymentCommandValidator : AbstractValidator<CreatePaymentCommand>
{
    public CreatePaymentCommandValidator()
        => RuleFor(x => x.IdempotencyKey)
            .MaximumLength(100)
            .When(x => x.IdempotencyKey is not null);
}

internal sealed partial class CreatePaymentCommandHandler
    : IRequestHandler<CreatePaymentCommand, Result<PaymentDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IPaymentService _paymentService;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<CreatePaymentCommandHandler> _logger;

    public CreatePaymentCommandHandler(
        IApplicationDbContext context,
        IPaymentService paymentService,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        ILogger<CreatePaymentCommandHandler> logger)
    {
        _context = context;
        _paymentService = paymentService;
        _currentUser = currentUser;
        _clock = clock;
        _logger = logger;
    }

    // ==============================================================
    // PDF Sprint 16: "Ödeme" loglanmalidir.
    // ==============================================================
    // TUTARI logluyorum ama KART BILGISI YOK -- zaten hiçbir yerde
    // saklamiyoruz (simülasyon sağlayıcı kullanıyoruz).
    //
    // Tutar hassas veri değil ama is acisindan kritik: uretimde
    // "bugun ne kadar ödeme alındı?" sorusunun ilk cevabi loglardan
    // geliyor, rapor sisteminden değil -- çünkü rapor sistemi de
    // bozulmus olabilir.
    // ==============================================================
    [LoggerMessage(
        EventId = LogEvents.OdemeBaslatildi,
        Level = LogLevel.Information,
        Message = "Ödeme baslatildi. Id: {PaymentId}, Rezervasyon: {ReservationId}, Tutar: {Amount} {Currency}")]
    private static partial void LogPaymentStarted(
        ILogger logger, Guid paymentId, Guid reservationId, decimal amount, string currency);

    [LoggerMessage(
        EventId = LogEvents.OdemeBasarisiz,
        Level = LogLevel.Warning,
        Message = "Ödeme sağlayıcı tarafından REDDEDILDI. Id: {PaymentId}, Rezervasyon: {ReservationId}")]
    private static partial void LogPaymentRejected(
        ILogger logger, Guid paymentId, Guid reservationId);

    public async Task<Result<PaymentDto>> Handle(
        CreatePaymentCommand request,
        CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not Guid userId)
        {
            return Result.Failure<PaymentDto>(
                Error.Unauthorized("auth.required", "Giriş yapmalisiniz."));
        }

        var now = _clock.UtcNow;

        // Idempotency: aynı anahtarla ikinci istek AYNI ödemeyi döner.
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var existingId = await _context.Payments
                .AsNoTracking()
                .Where(p => p.IdempotencyKey == request.IdempotencyKey)
                .Select(p => p.Id)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (existingId != Guid.Empty)
            {
                return await LoadAsync(existingId, cancellationToken).ConfigureAwait(false);
            }
        }

        var reservation = await _context.Reservations
            .FirstOrDefaultAsync(r => r.Id == request.ReservationId, cancellationToken)
            .ConfigureAwait(false);

        if (reservation is null || reservation.UserId != userId)
        {
            // 404: baskasinin rezervasyonunun varligini dogrulamamak için.
            return Result.Failure<PaymentDto>(PaymentErrors.ReservationNotFound);
        }

        // ==============================================================
        // PDF: "Aynı rezervasyon için birden fazla başarılı ödeme
        //       olusamaz."
        // ==============================================================
        // Bu kontrol olmasaydı kullanıcı iki kez ödeme yapip iki kez
        // ucret odeyebilirdi -- ve ikinci ödemeyi iade etmek manuel
        // mudahale gerektirirdi.
        var alreadyPaid = await _context.Payments
            .AsNoTracking()
            .AnyAsync(
                p => p.ReservationId == reservation.Id && p.Status == PaymentStatus.Successful,
                cancellationToken)
            .ConfigureAwait(false);

        if (alreadyPaid)
        {
            return Result.Failure<PaymentDto>(PaymentErrors.AlreadyPaid);
        }

        // ==============================================================
        // PDF: "Ödeme yalnızca AKTIF rezervasyon için baslatilabilir."
        // ==============================================================
        // Reservation.StartPayment iki seyi kontrol ediyor:
        //   1. Süre dolmuş mu?  -> "reservation.expired"
        //   2. Durum gecisi geçerli mi? (Locked -> PaymentPending)
        //
        // Süre kontrolü KRITIK: süresi dolmuş bir rezervasyonda ödeme
        // alsaydik, kullanıcı para oderdi ama koltuklar baskasina
        // satılmış olabilirdi.
        try
        {
            reservation.StartPayment(now);
        }
        catch (Domain.Common.DomainException)
        {
            return Result.Failure<PaymentDto>(PaymentErrors.ReservationNotPayable);
        }

        // Tutari REZERVASYONDAN alıyorum -- istemciden değil.
        var payment = Payment.Create(
            reservation.Id,
            reservation.TotalAmount,
            _paymentService.ProviderName,
            request.IdempotencyKey);

        _context.Payments.Add(payment);

        // ==============================================================
        // SAGLAYICIYA GITMEDEN ONCE KAYDET
        // ==============================================================
        // Neden? Sağlayıcı cagrisi sırasında uygulama cokerse, elimizde
        // "Pending" durumda bir kayıt kalır ve ne olduğunu arastirabilir,
        // mutabakat yapabiliriz.
        //
        // Önce cagirip sonra kaydetseydik: para cekilmis ama bizde
        // hiçbir iz yok. Bu, gerçek sistemlerde en korkulan durumdur --
        // müşteri "param gitti" der, bizde kayıt yoktur.
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        payment.StartProcessing();

        var providerResult = await _paymentService
            .CreatePaymentAsync(
                new PaymentRequest(
                    payment.Id,
                    payment.Amount.Amount,
                    payment.Amount.Currency,
                    $"Rezervasyon {reservation.ReservationCode}"),
                cancellationToken)
            .ConfigureAwait(false);

        if (!providerResult.IsSuccess)
        {
            payment.Fail(providerResult.ErrorMessage, now, userId);

            // ==========================================================
            // BURADA KOLTUKLARI SERBEST BIRAKMIYORUZ -- BILINCLI AYRIM
            // ==========================================================
            // PDF Sprint 8: "Ödeme başarısız olduğunda koltuklar serbest
            // birakilmalidir." Bu kuralı FailPaymentCommandHandler'da
            // uyguluyoruz. Peki neden burada değil?
            //
            // Iki durum FARKLIDIR:
            //
            //   BURASI: Ödeme HİÇ BASLAMADI. Sağlayıcı isteği daha
            //           basinda reddetti (geçici hata, ag sorunu,
            //           sağlayıcı bakimda). Para hareket etmedi.
            //           Kullanıcı saniyeler içinde tekrar deneyebilir.
            //
            //   FailPayment: Ödeme BASLADI ve BASARISIZ SONUCLANDI
            //           (kart reddedildi, 3D doğrulama başarısız).
            //           Bu kesin bir sonuctur; koltukları tutmanin
            //           anlami yok.
            //
            // Ilkinde koltuğu serbest biraksaydik, sağlayıcının bir
            // saniyelik kesintisi yuzunden kullanıcı koltuğunu
            // kaybederdi -- ve popüler bir etkinlikte bir daha
            // bulamazdi.
            //
            // Kilit zaten 10 dakikada kendiliginden dolacak; sonsuza
            // kadar bloke kalmiyor.
            reservation.RevertToLocked();

            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            LogPaymentRejected(_logger, payment.Id, reservation.Id);

            return Result.Failure<PaymentDto>(PaymentErrors.ProviderRejected);
        }

        payment.SetProviderReference(providerResult.ProviderReference);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // PDF Sprint 16: "Ödeme" loglanmalidir.
        LogPaymentStarted(
            _logger,
            payment.Id,
            reservation.Id,
            payment.Amount.Amount,
            payment.Amount.Currency);

        return await LoadAsync(payment.Id, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<PaymentDto>> LoadAsync(Guid id, CancellationToken cancellationToken)
    {
        var dto = await _context.Payments
            .Where(p => p.Id == id)
            .ToDto()
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return dto is null
            ? Result.Failure<PaymentDto>(PaymentErrors.NotFound)
            : Result.Success(dto);
    }
}

// ===================================================================
// ÖDEME TAMAMLAMA + BİLET URETIMI
// PDF: POST /api/v1/payments/{id}/complete
// ===================================================================

public sealed record CompletePaymentCommand(Guid PaymentId, string? ProviderReference)
    : IRequest<Result<PaymentDto>>;

internal sealed partial class CompletePaymentCommandHandler
    : IRequestHandler<CompletePaymentCommand, Result<PaymentDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IPaymentService _paymentService;
    private readonly IDateTimeProvider _clock;
    private readonly ISeatNotifier _seatNotifier;
    private readonly ILogger<CompletePaymentCommandHandler> _logger;

    public CompletePaymentCommandHandler(
        IApplicationDbContext context,
        IPaymentService paymentService,
        IDateTimeProvider clock,
        ISeatNotifier seatNotifier,
        ILogger<CompletePaymentCommandHandler> logger)
    {
        _context = context;
        _paymentService = paymentService;
        _clock = clock;
        _seatNotifier = seatNotifier;
        _logger = logger;
    }

    // Odemenin BASARIYLA tamamlandigi an: para alındı, biletler
    // üretildi. Sistemdeki en degerli tekil olay.
    [LoggerMessage(
        EventId = LogEvents.OdemeBasarili,
        Level = LogLevel.Information,
        Message = "Ödeme BASARILI. Id: {PaymentId}, Tutar: {Amount} {Currency}, Uretilen bilet: {TicketCount}")]
    private static partial void LogPaymentSucceeded(
        ILogger logger, Guid paymentId, decimal amount, string currency, int ticketCount);

    public async Task<Result<PaymentDto>> Handle(
        CompletePaymentCommand request,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        // Rezervasyon, kalemleri ve koltuklariyla birlikte yükleniyor:
        // hepsini DEGISTIRECEGIZ (onayla, sat, bilet üret).
        var payment = await _context.Payments
            .Include(p => p.Reservation)
                .ThenInclude(r => r.Items)
                    .ThenInclude(i => i.EventSeat)
            .FirstOrDefaultAsync(p => p.Id == request.PaymentId, cancellationToken)
            .ConfigureAwait(false);

        if (payment is null)
        {
            return Result.Failure<PaymentDto>(PaymentErrors.NotFound);
        }

        // ==============================================================
        // 1. SAGLAYICIYA SORARAK DOGRULA
        // ==============================================================
        // Callback'e KORU KORUNE GUVENMIYORUZ.
        //
        // Bu endpoint disariya açık (ödeme sağlayıcısı cagiracak).
        // Doğrulama olmasaydı saldirgan doğrudan bu adrese istek
        // gonderip BEDAVA BİLET alabilirdi.
        //
        // Simulasyonda da bu adimi isletiyoruz: MockPaymentProvider
        // yalnızca KENDİ urettigi referanslari dogruluyor, uydurma
        // referans reddediliyor.
        var reference = request.ProviderReference ?? payment.ProviderReference;

        if (string.IsNullOrWhiteSpace(reference))
        {
            return Result.Failure<PaymentDto>(PaymentErrors.VerificationFailed);
        }

        var verification = await _paymentService
            .VerifyPaymentAsync(reference, cancellationToken)
            .ConfigureAwait(false);

        if (!verification.IsSuccess)
        {
            return Result.Failure<PaymentDto>(PaymentErrors.VerificationFailed);
        }

        // ==============================================================
        // 2. IDEMPOTENCY -- PDF: "Callback islemleri idempotent olmalıdır."
        // ==============================================================
        // Ödeme saglayicilari callback'i BIRDEN FAZLA KEZ gönderir.
        // Bu bir hata değil, normal davranistir: sağlayıcı cevap
        // alamadigini dusunurse tekrar dener.
        //
        // Complete() zaten Successful ise FALSE dönüyor. O durumda
        // bilet URETMIYORUZ -- aksi halde her callback'te yeni bilet
        // olusur ve kullanıcının 5 bileti olurdu.
        var isFirstCompletion = payment.Complete(reference, now);

        if (!isFirstCompletion)
        {
            // Zaten tamamlanmis. Saglayiciya 200 donuyoruz ki tekrar
            // denemeyi biraksin. Hata donseydik sonsuz dongu olusurdu.
            return await LoadAsync(payment.Id, cancellationToken).ConfigureAwait(false);
        }

        var reservation = payment.Reservation;

        // ==============================================================
        // 3. TEK TRANSACTION -- PDF Sprint 8'in acikca istedigi liste
        // ==============================================================
        // "Aşağıdaki islemler tek bir transaction içinde calismalidir:
        //    - Ödeme başarılı kaydı
        //    - Rezervasyon onayı
        //    - Bilet oluşturma
        //    - Koltuklarin satıldı olarak isaretlenmesi
        //    - Notification oluşturma
        //    - Outbox message oluşturma"
        //
        // Hepsi AYNI SaveChangesAsync cagrisinda kaydediliyor; EF bunu
        // tek transaction içinde calistirir.
        //
        // Ayrı ayrı kaydetseydik: para alındı ama bilet olusmadi
        // (bağlantı koptu) gibi durumlar olusurdu ve elle duzeltmek
        // gerekirdi.

        reservation.Confirm(payment.Id, now);

        var tickets = new List<Ticket>(reservation.Items.Count);

        foreach (var item in reservation.Items)
        {
            // PDF: "Her rezervasyon kalemi için bilet olusturulmalidir."
            //
            // Ticket.Create, ReservationItem.AttachTicket'i cagiriyor.
            // O metot aynı kalem için IKINCI bileti reddediyor --
            // "koltuk bir ama bilet iki" hatasinin (salona iki kişi
            // girer) onune geciyor.
            var ticket = Ticket.Create(item, reservation.UserId, reservation.EventSessionId, now);

            _context.Tickets.Add(ticket);
            _context.TicketQrCodes.Add(TicketQrCode.Create(ticket.Id, now));

            tickets.Add(ticket);

            // Koltugu SATILDI olarak işaretle.
            //
            // MarkAsSold, koltuğun BU rezervasyon tarafından
            // kilitlendigini dogruluyor -- başka bir rezervasyonun
            // koltuğunu satmamizi engelliyor.
            item.EventSeat.MarkAsSold(reservation.Id);
        }

        // Bildirim (PDF Sprint 14: "Ödeme başarılı olduğunda").
        _context.Notifications.Add(Notification.Create(
            reservation.UserId,
            NotificationType.PaymentSucceeded,
            "Ödemeniz alındı",
            $"{reservation.ReservationCode} numarali rezervasyonunuz onaylandı. " +
            $"{tickets.Count} adet biletiniz hazır.",
            reservation.Id,
            $"/biletlerim"));

        // ==============================================================
        // OUTBOX -- PDF Sprint 9
        // ==============================================================
        // E-postayi BURADA GONDERMIYORUZ. Sebep:
        //
        // E-posta gonderimi ile veritabani yazimi ayrı sistemler ve
        // aralarinda ortak transaction yok. Önce gonderip sonra
        // veritabani islemi geri alinirsa, kullanıcı "biletiniz hazır"
        // maili alır ama bilet YOKTUR.
        //
        // Bunun yerine "e-posta gonderilecek" NIYETINI aynı transaction
        // içinde yazıyoruz. Arka plandaki job bunu okuyup gonderecek.
        //
        // Job Sprint 9'da yazilacak; mesajlar o zamana kadar tabloda
        // birikecek ve islenecek.
        // PDF Sprint 14: "Bilet olusturuldugunda" bildirimi.
        //
        // Ödeme başarılı bildirimi ZATEN var (yukarida) ama bu FARKLI
        // bir sey: kullanıcı "param gitti mi?" ile "biletim hazır mi?"
        // sorularinin ikisini de soruyor.
        //
        // Tek bildirimde birlestirseydik, biletlerini gormek isteyen
        // kullanıcı ödeme bildirimini aramak zorunda kalırdı.
        _context.Notifications.Add(Notification.Create(
            reservation.UserId,
            NotificationType.TicketCreated,
            "Biletleriniz hazır",
            $"{tickets.Count} adet biletiniz oluşturuldu. Girise QR " +
            "kodunuzu okutmaniz yeterli.",
            reservation.Id,
            "/biletlerim"));

        // Sprint 9 notu: Bu iki mesaj AYRI çünkü ayrı şeyler yapiyorlar
        // ve BIRBIRINDEN BAGIMSIZ başarısız olabilmeliler.
        //
        // Tek mesaj olsaydı ve e-posta gonderimi başarısız olsaydı,
        // uygulama ici bildirim de yeniden denenirdi -- kullanıcı
        // bildirimi iki kez gorurdu. Ayirinca her biri kendi
        // RetryCount'unu tutuyor.
        _context.OutboxMessages.Add(OutboxMessage.Create(
            OutboxMessageTypes.TicketsIssued,
            JsonSerializer.Serialize(new TicketsIssuedPayload(
                reservation.Id,
                reservation.UserId,
                payment.Id,
                tickets.Select(t => t.Id).ToList()))));

        _context.OutboxMessages.Add(OutboxMessage.Create(
            OutboxMessageTypes.PaymentSucceeded,
            JsonSerializer.Serialize(new PaymentSucceededPayload(
                payment.Id,
                reservation.Id,
                reservation.UserId,
                payment.Amount.Amount,
                payment.Amount.Currency))));

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // ==============================================================
        // PDF Sprint 10: "SeatSold"
        // ==============================================================
        // PDF is kuralı: "Satılan koltuk yeniden secilememelidir."
        //
        // SeatLocked yerine AYRI bir olay gonderiyorum çünkü istemci
        // için anlamlari farklı:
        //
        //   Locked -> 10 dakika sonra bosalabilir, umut var
        //   Sold   -> bir daha asla bosalmayacak
        //
        // Istemci bu ayrimi bilmeden doğru rengi ve tiklanabilirligi
        // secemezdi. Tek olay gonderseydik, satılan koltuk sureli
        // kilit gibi görünür ve kullanıcı bosalmasini beklerdi.
        await _seatNotifier.SeatsSoldAsync(
            reservation.EventSessionId,
            reservation.Items.Select(i => i.EventSeatId).ToList(),
            cancellationToken).ConfigureAwait(false);

        // PDF Sprint 16: "Ödeme" -- basariyla tamamlanma ani.
        LogPaymentSucceeded(
            _logger,
            payment.Id,
            payment.Amount.Amount,
            payment.Amount.Currency,
            tickets.Count);

        return await LoadAsync(payment.Id, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<PaymentDto>> LoadAsync(Guid id, CancellationToken cancellationToken)
    {
        var dto = await _context.Payments
            .Where(p => p.Id == id)
            .ToDto()
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return dto is null
            ? Result.Failure<PaymentDto>(PaymentErrors.NotFound)
            : Result.Success(dto);
    }
}

// ===================================================================
// ÖDEME BASARISIZ -- PDF: POST /api/v1/payments/{id}/fail
// ===================================================================

public sealed record FailPaymentCommand(Guid PaymentId, string? Reason) : IRequest<Result>;

internal sealed class FailPaymentCommandHandler : IRequestHandler<FailPaymentCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _clock;
    private readonly ISeatNotifier _seatNotifier;

    public FailPaymentCommandHandler(
        IApplicationDbContext context,
        IDateTimeProvider clock,
        ISeatNotifier seatNotifier)
    {
        _context = context;
        _clock = clock;
        _seatNotifier = seatNotifier;
    }

    public async Task<Result> Handle(FailPaymentCommand request, CancellationToken cancellationToken)
    {
        var payment = await _context.Payments
            .Include(p => p.Reservation)
                .ThenInclude(r => r.Items)
                    .ThenInclude(i => i.EventSeat)
            .FirstOrDefaultAsync(p => p.Id == request.PaymentId, cancellationToken)
            .ConfigureAwait(false);

        if (payment is null)
        {
            return Result.Failure(PaymentErrors.NotFound);
        }

        var now = _clock.UtcNow;
        var reservation = payment.Reservation;

        payment.Fail(request.Reason, now, reservation.UserId);

        // ==============================================================
        // PDF Sprint 8 KURALI:
        // "Ödeme başarısız olduğunda koltuklar serbest birakilmalidir."
        // ==============================================================
        // ONEMLI NOT: docs/01-is-analizi.md soru 8'de ilk analizimde
        // TERSINI yazmistim -- koltukları kilitli tutup kullanıcıya
        // ikinci sans vermeyi onermistim (kart hatası siktir diye).
        //
        // Ama PDF Sprint 8 bu kuralı ACIKCA belirtiyor. Sartname
        // benim tercihimin onune gecer; kuralı PDF'e göre uyguluyorum
        // ve dokumani da guncelledim.
        //
        // Odun: kart hatası alan kullanıcı koltuklarini kaybediyor.
        // Kazanc: koltuklar hemen satışa dönüyor, popüler etkinliklerde
        // boş yere bloke kalmiyor.
        reservation.Cancel("Ödeme başarısız");

        foreach (var item in reservation.Items)
        {
            // Satılmış koltuğu atla: bu ödeme başarısız olsa bile
            // aynı rezervasyon için başka bir ödeme başarılı olmuş
            // olabilir (yaris durumu).
            if (item.EventSeat.Status != EventSeatStatus.Sold)
            {
                item.EventSeat.Release();
            }
        }

        _context.Notifications.Add(Notification.Create(
            reservation.UserId,
            NotificationType.PaymentFailed,
            "Ödemeniz alinamadi",
            $"{reservation.ReservationCode} numarali rezervasyonunuzun ödemesi başarısız oldu " +
            "ve koltuklariniz serbest birakildi.",
            reservation.Id));

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // PDF Sprint 10: "SeatReleased".
        // Başarısız odemede koltuklar hemen satışa dönüyor; bekleyen
        // kullanicilarin ekraninda anında yesile ceviriyoruz.
        await _seatNotifier.SeatsReleasedAsync(
            reservation.EventSessionId,
            reservation.Items.Select(i => i.EventSeatId).ToList(),
            cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
