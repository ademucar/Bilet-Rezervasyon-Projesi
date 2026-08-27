using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Abstractions.Payments;
using Ticketing.Application.Abstractions.Persistence;
using Ticketing.Application.Abstractions.RealTime;
using Ticketing.Application.Abstractions.Security;
using Ticketing.Application.Abstractions.Time;
using Ticketing.Application.Common.Results;
using Ticketing.Application.Features.Outbox;
using Ticketing.Domain.Entities;
using Ticketing.Domain.Enums;
using Ticketing.Domain.ValueObjects;

namespace Ticketing.Application.Features.Payments;

internal static class PaymentErrors
{
    public static readonly Error NotFound = Error.NotFound(
        "payment.not_found", "Odeme bulunamadi.");

    public static readonly Error ReservationNotFound = Error.NotFound(
        "payment.reservation_not_found", "Rezervasyon bulunamadi.");

    public static readonly Error ReservationNotPayable = Error.Conflict(
        "payment.reservation_not_payable",
        "Bu rezervasyon icin odeme baslatilamaz. Suresi dolmus veya iptal edilmis olabilir.");

    public static readonly Error AlreadyPaid = Error.Conflict(
        "payment.already_paid",
        "Bu rezervasyon icin zaten basarili bir odeme yapilmis.");

    public static readonly Error ProviderRejected = Error.Conflict(
        "payment.provider_rejected", "Odeme saglayicisi islemi reddetti.");

    public static readonly Error VerificationFailed = Error.Conflict(
        "payment.verification_failed",
        "Odeme saglayici tarafinda dogrulanamadi.");

    public static readonly Error NotRefundable = Error.Conflict(
        "payment.not_refundable", "Bu odeme iade edilemez.");
}

// ===================================================================
// ODEME BASLATMA -- PDF: POST /api/v1/payments
// ===================================================================

/// <summary>
/// DIKKAT: Tutar alani YOK -- rezervasyondan okunuyor.
///
/// PDF Sprint 6: "Frontend tarafindan gonderilen toplam tutara
/// guvenilmemelidir." Alan hic olmadigi icin istemci 500 TL'lik
/// bileti 1 TL'ye odemeyi DENEYEMEZ bile.
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

internal sealed class CreatePaymentCommandHandler
    : IRequestHandler<CreatePaymentCommand, Result<PaymentDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IPaymentService _paymentService;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public CreatePaymentCommandHandler(
        IApplicationDbContext context,
        IPaymentService paymentService,
        ICurrentUser currentUser,
        IDateTimeProvider clock)
    {
        _context = context;
        _paymentService = paymentService;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result<PaymentDto>> Handle(
        CreatePaymentCommand request,
        CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not Guid userId)
        {
            return Result.Failure<PaymentDto>(
                Error.Unauthorized("auth.required", "Giris yapmalisiniz."));
        }

        var now = _clock.UtcNow;

        // Idempotency: ayni anahtarla ikinci istek AYNI odemeyi doner.
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
            // 404: baskasinin rezervasyonunun varligini dogrulamamak icin.
            return Result.Failure<PaymentDto>(PaymentErrors.ReservationNotFound);
        }

        // ==============================================================
        // PDF: "Ayni rezervasyon icin birden fazla basarili odeme
        //       olusamaz."
        // ==============================================================
        // Bu kontrol olmasaydi kullanici iki kez odeme yapip iki kez
        // ucret odeyebilirdi -- ve ikinci odemeyi iade etmek manuel
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
        // PDF: "Odeme yalnizca AKTIF rezervasyon icin baslatilabilir."
        // ==============================================================
        // Reservation.StartPayment iki seyi kontrol ediyor:
        //   1. Sure dolmus mu?  -> "reservation.expired"
        //   2. Durum gecisi gecerli mi? (Locked -> PaymentPending)
        //
        // Sure kontrolu KRITIK: suresi dolmus bir rezervasyonda odeme
        // alsaydik, kullanici para oderdi ama koltuklar baskasina
        // satilmis olabilirdi.
        try
        {
            reservation.StartPayment(now);
        }
        catch (Domain.Common.DomainException)
        {
            return Result.Failure<PaymentDto>(PaymentErrors.ReservationNotPayable);
        }

        // Tutari REZERVASYONDAN aliyorum -- istemciden degil.
        var payment = Payment.Create(
            reservation.Id,
            reservation.TotalAmount,
            _paymentService.ProviderName,
            request.IdempotencyKey);

        _context.Payments.Add(payment);

        // ==============================================================
        // SAGLAYICIYA GITMEDEN ONCE KAYDET
        // ==============================================================
        // Neden? Saglayici cagrisi sirasinda uygulama cokerse, elimizde
        // "Pending" durumda bir kayit kalir ve ne oldugunu arastirabilir,
        // mutabakat yapabiliriz.
        //
        // Once cagirip sonra kaydetseydik: para cekilmis ama bizde
        // hicbir iz yok. Bu, gercek sistemlerde en korkulan durumdur --
        // musteri "param gitti" der, bizde kayit yoktur.
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
            // PDF Sprint 8: "Odeme basarisiz oldugunda koltuklar serbest
            // birakilmalidir." Bu kurali FailPaymentCommandHandler'da
            // uyguluyoruz. Peki neden burada degil?
            //
            // Iki durum FARKLIDIR:
            //
            //   BURASI: Odeme HIC BASLAMADI. Saglayici istegi daha
            //           basinda reddetti (gecici hata, ag sorunu,
            //           saglayici bakimda). Para hareket etmedi.
            //           Kullanici saniyeler icinde tekrar deneyebilir.
            //
            //   FailPayment: Odeme BASLADI ve BASARISIZ SONUCLANDI
            //           (kart reddedildi, 3D dogrulama basarisiz).
            //           Bu kesin bir sonuctur; koltuklari tutmanin
            //           anlami yok.
            //
            // Ilkinde koltugu serbest biraksaydik, saglayicinin bir
            // saniyelik kesintisi yuzunden kullanici koltugunu
            // kaybederdi -- ve populer bir etkinlikte bir daha
            // bulamazdi.
            //
            // Kilit zaten 10 dakikada kendiliginden dolacak; sonsuza
            // kadar bloke kalmiyor.
            reservation.RevertToLocked();

            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return Result.Failure<PaymentDto>(PaymentErrors.ProviderRejected);
        }

        payment.SetProviderReference(providerResult.ProviderReference);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

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
// ODEME TAMAMLAMA + BILET URETIMI
// PDF: POST /api/v1/payments/{id}/complete
// ===================================================================

public sealed record CompletePaymentCommand(Guid PaymentId, string? ProviderReference)
    : IRequest<Result<PaymentDto>>;

internal sealed class CompletePaymentCommandHandler
    : IRequestHandler<CompletePaymentCommand, Result<PaymentDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IPaymentService _paymentService;
    private readonly IDateTimeProvider _clock;
    private readonly ISeatNotifier _seatNotifier;

    public CompletePaymentCommandHandler(
        IApplicationDbContext context,
        IPaymentService paymentService,
        IDateTimeProvider clock,
        ISeatNotifier seatNotifier)
    {
        _context = context;
        _paymentService = paymentService;
        _clock = clock;
        _seatNotifier = seatNotifier;
    }

    public async Task<Result<PaymentDto>> Handle(
        CompletePaymentCommand request,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        // Rezervasyon, kalemleri ve koltuklariyla birlikte yukleniyor:
        // hepsini DEGISTIRECEGIZ (onayla, sat, bilet uret).
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
        // Bu endpoint disariya acik (odeme saglayicisi cagiracak).
        // Dogrulama olmasaydi saldirgan dogrudan bu adrese istek
        // gonderip BEDAVA BILET alabilirdi.
        //
        // Simulasyonda da bu adimi isletiyoruz: MockPaymentProvider
        // yalnizca KENDI urettigi referanslari dogruluyor, uydurma
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
        // 2. IDEMPOTENCY -- PDF: "Callback islemleri idempotent olmalidir."
        // ==============================================================
        // Odeme saglayicilari callback'i BIRDEN FAZLA KEZ gonderir.
        // Bu bir hata degil, normal davranistir: saglayici cevap
        // alamadigini dusunurse tekrar dener.
        //
        // Complete() zaten Successful ise FALSE donuyor. O durumda
        // bilet URETMIYORUZ -- aksi halde her callback'te yeni bilet
        // olusur ve kullanicinin 5 bileti olurdu.
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
        // "Asagidaki islemler tek bir transaction icinde calismalidir:
        //    - Odeme basarili kaydi
        //    - Rezervasyon onayi
        //    - Bilet olusturma
        //    - Koltuklarin satildi olarak isaretlenmesi
        //    - Notification olusturma
        //    - Outbox message olusturma"
        //
        // Hepsi AYNI SaveChangesAsync cagrisinda kaydediliyor; EF bunu
        // tek transaction icinde calistirir.
        //
        // Ayri ayri kaydetseydik: para alindi ama bilet olusmadi
        // (baglanti koptu) gibi durumlar olusurdu ve elle duzeltmek
        // gerekirdi.

        reservation.Confirm(payment.Id, now);

        var tickets = new List<Ticket>(reservation.Items.Count);

        foreach (var item in reservation.Items)
        {
            // PDF: "Her rezervasyon kalemi icin bilet olusturulmalidir."
            //
            // Ticket.Create, ReservationItem.AttachTicket'i cagiriyor.
            // O metot ayni kalem icin IKINCI bileti reddediyor --
            // "koltuk bir ama bilet iki" hatasinin (salona iki kisi
            // girer) onune geciyor.
            var ticket = Ticket.Create(item, reservation.UserId, reservation.EventSessionId, now);

            _context.Tickets.Add(ticket);
            _context.TicketQrCodes.Add(TicketQrCode.Create(ticket.Id, now));

            tickets.Add(ticket);

            // Koltugu SATILDI olarak isaretle.
            //
            // MarkAsSold, koltugun BU rezervasyon tarafindan
            // kilitlendigini dogruluyor -- baska bir rezervasyonun
            // koltugunu satmamizi engelliyor.
            item.EventSeat.MarkAsSold(reservation.Id);
        }

        // Bildirim (PDF Sprint 14: "Odeme basarili oldugunda").
        _context.Notifications.Add(Notification.Create(
            reservation.UserId,
            NotificationType.PaymentSucceeded,
            "Odemeniz alindi",
            $"{reservation.ReservationCode} numarali rezervasyonunuz onaylandi. " +
            $"{tickets.Count} adet biletiniz hazir.",
            reservation.Id,
            $"/biletlerim"));

        // ==============================================================
        // OUTBOX -- PDF Sprint 9
        // ==============================================================
        // E-postayi BURADA GONDERMIYORUZ. Sebep:
        //
        // E-posta gonderimi ile veritabani yazimi ayri sistemler ve
        // aralarinda ortak transaction yok. Once gonderip sonra
        // veritabani islemi geri alinirsa, kullanici "biletiniz hazir"
        // maili alir ama bilet YOKTUR.
        //
        // Bunun yerine "e-posta gonderilecek" NIYETINI ayni transaction
        // icinde yaziyoruz. Arka plandaki job bunu okuyup gonderecek.
        //
        // Job Sprint 9'da yazilacak; mesajlar o zamana kadar tabloda
        // birikecek ve islenecek.
        // Sprint 9 notu: Bu iki mesaj AYRI cunku ayri seyler yapiyorlar
        // ve BIRBIRINDEN BAGIMSIZ basarisiz olabilmeliler.
        //
        // Tek mesaj olsaydi ve e-posta gonderimi basarisiz olsaydi,
        // uygulama ici bildirim de yeniden denenirdi -- kullanici
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
        // PDF is kurali: "Satilan koltuk yeniden secilememelidir."
        //
        // SeatLocked yerine AYRI bir olay gonderiyorum cunku istemci
        // icin anlamlari farkli:
        //
        //   Locked -> 10 dakika sonra bosalabilir, umut var
        //   Sold   -> bir daha asla bosalmayacak
        //
        // Istemci bu ayrimi bilmeden dogru rengi ve tiklanabilirligi
        // secemezdi. Tek olay gonderseydik, satilan koltuk sureli
        // kilit gibi gorunur ve kullanici bosalmasini beklerdi.
        await _seatNotifier.SeatsSoldAsync(
            reservation.EventSessionId,
            reservation.Items.Select(i => i.EventSeatId).ToList(),
            cancellationToken).ConfigureAwait(false);

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
// ODEME BASARISIZ -- PDF: POST /api/v1/payments/{id}/fail
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
        // "Odeme basarisiz oldugunda koltuklar serbest birakilmalidir."
        // ==============================================================
        // ONEMLI NOT: docs/01-is-analizi.md soru 8'de ilk analizimde
        // TERSINI yazmistim -- koltuklari kilitli tutup kullaniciya
        // ikinci sans vermeyi onermistim (kart hatasi siktir diye).
        //
        // Ama PDF Sprint 8 bu kurali ACIKCA belirtiyor. Sartname
        // benim tercihimin onune gecer; kurali PDF'e gore uyguluyorum
        // ve dokumani da guncelledim.
        //
        // Odun: kart hatasi alan kullanici koltuklarini kaybediyor.
        // Kazanc: koltuklar hemen satisa donuyor, populer etkinliklerde
        // bos yere bloke kalmiyor.
        reservation.Cancel("Odeme basarisiz");

        foreach (var item in reservation.Items)
        {
            // Satilmis koltugu atla: bu odeme basarisiz olsa bile
            // ayni rezervasyon icin baska bir odeme basarili olmus
            // olabilir (yaris durumu).
            if (item.EventSeat.Status != EventSeatStatus.Sold)
            {
                item.EventSeat.Release();
            }
        }

        _context.Notifications.Add(Notification.Create(
            reservation.UserId,
            NotificationType.PaymentFailed,
            "Odemeniz alinamadi",
            $"{reservation.ReservationCode} numarali rezervasyonunuzun odemesi basarisiz oldu " +
            "ve koltuklariniz serbest birakildi.",
            reservation.Id));

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // PDF Sprint 10: "SeatReleased".
        // Basarisiz odemede koltuklar hemen satisa donuyor; bekleyen
        // kullanicilarin ekraninda aninda yesile ceviriyoruz.
        await _seatNotifier.SeatsReleasedAsync(
            reservation.EventSessionId,
            reservation.Items.Select(i => i.EventSeatId).ToList(),
            cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
