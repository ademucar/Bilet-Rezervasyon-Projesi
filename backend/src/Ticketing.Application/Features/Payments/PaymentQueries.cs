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
using Ticketing.Domain.Entities;
using Ticketing.Domain.Enums;

namespace Ticketing.Application.Features.Payments;

// DTO'lar

/// <param name="ProviderReference">Sağlayıcı işlem referansı. Bu alanı kullanıcıya DONUYORUM çünkü destek talebinde "işlem numaram su" diyebilmeli. Hassas bir bilgi değil -- tek başına hiçbir işlem yapılamaz.</param>
public sealed record PaymentDto(
    Guid Id,
    Guid ReservationId,
    string ReservationCode,
    PaymentStatus Status,
    string ProviderName,
    string? ProviderReference,
    decimal Amount,
    decimal RefundedAmount,
    string Currency,
    string? FailureReason,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<PaymentTransactionDto> Transactions);

public sealed record PaymentTransactionDto(
    PaymentTransactionType Type,
    PaymentStatus Status,
    string? Message,
    DateTimeOffset CreatedAt);

internal static class PaymentProjections
{
    /// <summary>
    /// Ödeme sorgusunu DTO'ya projelendirir.
    ///
    /// Filtre PROJEKSIYONDAN ONCE uygulanmali -- Sprint 7'de bu
    /// tuzaga dustuk: EF, olusturdugu DTO uzerinde WHERE calistiramiyor.
    /// Bu yüzden metot IQueryable&lt;Payment&gt; aliyor.
    /// </summary>
    public static IQueryable<PaymentDto> ToDto(this IQueryable<Payment> query)
        => query
            .AsNoTracking()
            .Select(p => new PaymentDto(
                p.Id,
                p.ReservationId,
                p.Reservation.ReservationCode,
                p.Status,
                p.ProviderName,
                p.ProviderReference,
                p.Amount.Amount,
                p.RefundedAmount.Amount,
                p.Amount.Currency,
                p.FailureReason,
                p.CompletedAt,
                p.Transactions
                    .OrderBy(t => t.CreatedAt)
                    .Select(t => new PaymentTransactionDto(
                        t.Type, t.Status, t.Message, t.CreatedAt))
                    .ToList()));
}

// DETAY -- PDF: GET /api/v1/payments/{id}

public sealed record GetPaymentQuery(Guid Id) : IRequest<Result<PaymentDto>>;

internal sealed class GetPaymentQueryHandler : IRequestHandler<GetPaymentQuery, Result<PaymentDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;

    public GetPaymentQueryHandler(IApplicationDbContext context, ICurrentUser currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<Result<PaymentDto>> Handle(
        GetPaymentQuery request,
        CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not Guid userId)
        {
            return Result.Failure<PaymentDto>(
                Error.Unauthorized("auth.required", "Giriş yapmalisiniz."));
        }

        var isAdmin = _currentUser.Roles.Contains(Role.Names.Admin);

        // Sahiplik kontrolü SORGUYA dahil.
        //
        // Admin destek islerini yapabilmek için her ödemeyi gorebilmeli;
        // normal kullanıcı yalnızca kendisininkini.
        var dto = await _context.Payments
            .Where(p => p.Id == request.Id && (isAdmin || p.Reservation.UserId == userId))
            .ToDto()
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return dto is null
            ? Result.Failure<PaymentDto>(PaymentErrors.NotFound)
            : Result.Success(dto);
    }
}

// IADE -- PDF: POST /api/v1/payments/{id}/refund

/// <summary>
/// İade islemi.
/// </summary>
/// <param name="Amount">
/// İade tutarı. null ise KALAN TÜM tutar iade edilir.
///
/// Kismi iade destegi var çünkü bir rezervasyondaki 4 biletten
/// yalnızca 2'si iade edilebilir.
/// </param>
/// <param name="IdempotencyKey">
/// PDF Sprint 15: "İade baslatma" idempotency listesinde.
///
/// Aynı anahtarla gelen ikinci istek YENI iade yapmaz, mevcut
/// ödemenin durumunu döner.
/// </param>
public sealed record RefundPaymentCommand(
    Guid PaymentId,
    decimal? Amount,
    string? Reason,
    string? IdempotencyKey = null) : IRequest<Result<PaymentDto>>;

internal sealed partial class RefundPaymentCommandHandler
    : IRequestHandler<RefundPaymentCommand, Result<PaymentDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IPaymentService _paymentService;
    private readonly IDateTimeProvider _clock;
    private readonly ISeatNotifier _seatNotifier;
    private readonly ILogger<RefundPaymentCommandHandler> _logger;

    public RefundPaymentCommandHandler(
        IApplicationDbContext context,
        IPaymentService paymentService,
        IDateTimeProvider clock,
        ISeatNotifier seatNotifier,
        ILogger<RefundPaymentCommandHandler> logger)
    {
        _context = context;
        _paymentService = paymentService;
        _clock = clock;
        _seatNotifier = seatNotifier;
        _logger = logger;
    }

    // PDF Sprint 16: "İade" loglanmalidir.
    //
    // WARNING seviyesi -- hata olduğu için değil, GORULMESI
    // gerektigi için.
    //
    // İade, sistemdeki tek PARA CIKISI. Hacminde ani bir artis ya
    // bir yazilim hatasinin ya da bir kotuye kullanimin isaretidir;
    // ikisi de hizli mudahale gerektirir.
    //
    // Information yapsaydim üretim filtrelerinde kaybolurdu ve
    // "günlük iade tutarı su esigi asti" alarmini besleyecek veri
    // hiç gelmezdi.
    //
    // Tam/kismi ayrimini AYRI bir alan olarak veriyorum: tam iade
    // koltukları serbest birakiyor (satış kaybi), kismi iade
    // birakmiyor. Aynı satirda toplanirsa bu ayrim sorgulanamaz.
    [LoggerMessage(
        EventId = LogEvents.IadeYapildi,
        Level = LogLevel.Warning,
        Message = "IADE yapıldı. Ödeme: {PaymentId}, Tutar: {Amount} {Currency}, Tam iade: {IsFull}, Sebep: {Reason}")]
    private static partial void LogRefunded(
        ILogger logger, Guid paymentId, decimal amount, string currency, bool isFull, string? reason);

    public async Task<Result<PaymentDto>> Handle(
        RefundPaymentCommand request,
        CancellationToken cancellationToken)
    {
        var payment = await _context.Payments
            .Include(p => p.Reservation)
                .ThenInclude(r => r.Items)
                    .ThenInclude(i => i.EventSeat)

            // BU Include SPRINT 17'DE, ENTEGRASYON TESTIYLE EKLENDI
            //
            // Aşağıdaki idempotency kontrolü payment.Transactions
            // uzerinde çalışıyor. Ama bu koleksiyon YUKLENMIYORDU:
            // lazy loading kapalı olduğu için her zaman BOŞ geliyordu.
            //
            // Sonuç: kontrol her seferinde "daha önce islenmemis"
            // diyordu ve idempotency HİÇ calismiyordu. Sprint 15'te
            // yazdim, doğru gorunuyordu, tek satiri bile calismiyordu.
            //
            // Bunu ancak entegrasyon testi yakaladi: aynı
            // Idempotency-Key ile iki kismi iade gonderdim ve
            // veritabaninda IKI iade kaydı buldum -- yani aynı para
            // iki kez geri gonderilmisti.
            //
            // NOT: tam iadede domain korumasi (toplam iade odenen
            // tutarı aşamaz) ikinci isteği zaten reddediyordu. Hata
            // yalnızca KISMI iadede görünür oluyordu -- bu yüzden
            // fark edilmesi bu kadar zordu.
            //
            // Sprint 12 (denetim alanlari), Sprint 15 (baglanmamis
            // maskeleyici), Sprint 16 (correlation ID) ile aynı
            // desen: yazilmis ama beslenmemis kod.
            .Include(p => p.Transactions)
            .FirstOrDefaultAsync(p => p.Id == request.PaymentId, cancellationToken)
            .ConfigureAwait(false);

        if (payment is null)
        {
            return Result.Failure<PaymentDto>(PaymentErrors.NotFound);
        }

        if (payment.Status is not (PaymentStatus.Successful or PaymentStatus.Refunded))
        {
            return Result.Failure<PaymentDto>(PaymentErrors.NotRefundable);
        }

        // IDEMPOTENCY -- PDF Sprint 15
        //
        // İade, cift calistirilmasi EN TEHLIKELI işlem: aynı parayi
        // iki kez geri gondermek doğrudan mali kayip.
        //
        // Anahtari işlem kayitlarinda (PaymentTransaction) ariyorum.
        // Ayrı bir tablo acmadim: bilgi zaten orada durmali, çünkü
        // "bu iade yapıldı mi?" sorusunun dogal yeri işlem gecmisi.
        //
        // Bu kontrol yarisa açık (iki istek aynı anda gelirse ikisi de
        // "yok" görebilir). Kabul edilebilir çünkü ASIL koruma
        // Payment.Refund() içinde: toplam iade odenen tutarı aşamaz.
        // Buradaki kontrol YAYGIN durumu (ag kopmasi sonrası tekrar)
        // temiz bir şekilde cozuyor.
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var zatenIslendi = payment.Transactions.Any(
                t => t.Type == PaymentTransactionType.Refund
                  && t.ProviderReference == request.IdempotencyKey);

            if (zatenIslendi)
            {
                // Aynı istek daha önce islenmis: HATA DEĞİL, mevcut
                // durumu donuyorum. Cagiran taraf için sonuç aynı.
                return await LoadDtoAsync(payment.Id, cancellationToken).ConfigureAwait(false);
            }
        }

        var refundable = payment.GetRefundableAmount();
        var amount = request.Amount ?? refundable.Amount;

        if (amount <= 0 || amount > refundable.Amount)
        {
            return Result.Failure<PaymentDto>(Error.Validation(
                "payment.invalid_refund_amount",
                $"İade tutari 0 ile {refundable.Amount} arasında olmalıdır."));
        }

        // Once saglayici, sonra veritabani
        //
        // Ödeme BASLATIRKEN önce kaydediyorduk (para gidip izimizin
        // kalmamasini onlemek için). IADE'de sıra TERS:
        //
        // Önce veritabanina "iade edildi" yazip sonra sağlayıcı
        // reddetseydi, kullanıcı parasini almadan sistemde "iade
        // edildi" görünürdü. Bu daha kötü bir durum: müşteri parasini
        // bekler, biz "iade ettim" deriz.
        //
        // Önce saglayiciya gidip onay alirsak, en kötü ihtimalle para
        // gider ama bende kayıt olmaz -- mutabakatta yakalanabilir
        // ve duzeltilebilir bir durumdur.
        var providerResult = await _paymentService
            .RefundPaymentAsync(payment.ProviderReference ?? string.Empty, amount, cancellationToken)
            .ConfigureAwait(false);

        if (!providerResult.IsSuccess)
        {
            return Result.Failure<PaymentDto>(Error.Conflict(
                "payment.refund_rejected",
                providerResult.ErrorMessage ?? "İade sağlayıcı tarafından reddedildi."));
        }

        // Idempotency anahtarini işlem kaydina yazıyorum.
        //
        // Anahtar verilmediyse sağlayıcının referansı kullanılıyor --
        // yani davranis eskisi gibi kaliyor ve mevcut cagiranlar
        // etkilenmiyor.
        payment.Refund(
            new Domain.ValueObjects.Money(amount, payment.Amount.Currency),
            request.IdempotencyKey ?? providerResult.ProviderReference);

        var now = _clock.UtcNow;
        var reservation = payment.Reservation;

        // TAM iade ise rezervasyonu ve biletleri de iade et.
        //
        // Kismi iadede rezervasyon Confirmed kaliyor: kullanıcının
        // hâlâ geçerli biletleri var.
        if (payment.GetRefundableAmount().Amount == 0)
        {
            reservation.MarkAsRefunded();

            var tickets = await _context.Tickets
                .Where(t => t.ReservationItem.ReservationId == reservation.Id
                         && t.Status == TicketStatus.Active)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var ticket in tickets)
            {
                ticket.Cancel(withRefund: true, now);
            }

            // Koltukları tekrar satışa ac.
            //
            // Bu adim atlanirsa koltuk kalici olarak "satılmış"
            // kalır ve bir daha kimseye satilamaz -- gelir kaybi.
            foreach (var item in reservation.Items)
            {
                if (item.EventSeat.Status == EventSeatStatus.Sold)
                {
                    item.EventSeat.Refund();
                }
            }

            _context.Notifications.Add(Notification.Create(
                reservation.UserId,
                NotificationType.RefundCompleted,
                "Iadeniz tamamlandı",
                $"{reservation.ReservationCode} numarali rezervasyonunuz için " +
                $"{amount} {payment.Amount.Currency} iade edildi.",
                reservation.Id));
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // PDF Sprint 16: "İade" loglanmalidir.
        //
        // SaveChanges'ten SONRA: para hareketi ancak kaydedildiyse
        // gerçek. Bu, para iceren bir islemde ozellikle önemli --
        // logda gorunup veritabaninda olmayan bir iade, mutabakat
        // sırasında saatler kaybettirir.
        LogRefunded(
            _logger,
            payment.Id,
            amount,
            payment.Amount.Currency,
            payment.GetRefundableAmount().Amount == 0,
            request.Reason);

        // PDF Sprint 10: "SeatReleased".
        //
        // YALNIZCA TAM IADEDE gonderiyorum. Kismi iadede koltuklar
        // satılmış kaliyor -- kullanıcının hâlâ geçerli biletleri var.
        // Kosulsuz gonderseydim, kismi iade sonrası herkesin
        // ekraninda koltuklar bosalmis görünür ama sunucu
        // rezervasyonu reddederdi. Ekran ile gerçek arasindaki bu
        // ayrilik, kullanıcının sisteme guvenini bitirir.
        if (payment.GetRefundableAmount().Amount == 0)
        {
            await _seatNotifier.SeatsReleasedAsync(
                reservation.EventSessionId,
                reservation.Items.Select(i => i.EventSeatId).ToList(),
                cancellationToken).ConfigureAwait(false);
        }

        return await LoadDtoAsync(payment.Id, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<PaymentDto>> LoadDtoAsync(
        Guid paymentId,
        CancellationToken cancellationToken)
    {
        var dto = await _context.Payments
            .Where(p => p.Id == paymentId)
            .ToDto()
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return dto is null
            ? Result.Failure<PaymentDto>(PaymentErrors.NotFound)
            : Result.Success(dto);
    }
}

// KULLANICININ BILETLERI -- PDF sayfa 4

public sealed record TicketDto(
    Guid Id,
    string TicketNumber,
    TicketStatus Status,
    string EventTitle,
    DateTimeOffset SessionStartDate,
    string VenueName,
    string SeatLabel,
    string SectionName,
    string TicketTypeName,
    decimal Price,
    string Currency,
    string? QrValue,
    DateTimeOffset? UsedAt);

public sealed record GetMyTicketsQuery(TicketStatus? Status)
    : IRequest<Result<IReadOnlyList<TicketDto>>>;

internal sealed class GetMyTicketsQueryHandler
    : IRequestHandler<GetMyTicketsQuery, Result<IReadOnlyList<TicketDto>>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;

    public GetMyTicketsQueryHandler(IApplicationDbContext context, ICurrentUser currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<TicketDto>>> Handle(
        GetMyTicketsQuery request,
        CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not Guid userId)
        {
            return Result.Failure<IReadOnlyList<TicketDto>>(
                Error.Unauthorized("auth.required", "Giriş yapmalisiniz."));
        }

        var query = _context.Tickets.AsNoTracking().Where(t => t.UserId == userId);

        if (request.Status.HasValue)
        {
            query = query.Where(t => t.Status == request.Status.Value);
        }

        var tickets = await query
            // Yaklasan etkinlikler önce.
            .OrderBy(t => t.EventSeat.EventSession.StartDate)
            .Select(t => new TicketDto(
                t.Id,
                t.TicketNumber,
                t.Status,
                t.EventSeat.EventSession.Event.Title,
                t.EventSeat.EventSession.StartDate,
                t.EventSeat.EventSession.Event.Venue.Name,
                t.EventSeat.Seat.RowLabel + "-" + t.EventSeat.Seat.SeatNumber,
                t.EventSeat.Seat.SeatSection.Name,
                t.EventSeat.TicketType.Name,
                t.Price.Amount,
                t.Price.Currency,

                // QR degerini YALNIZCA AKTIF biletlerde donuyorum.
                //
                // İptal edilmiş veya kullanılmış biletin QR'ini
                // gondermenin bir faydasi yok; hassas bir deger
                // olduğu için gereksiz yere yaymiyorum.
                t.Status == TicketStatus.Active && t.QrCode != null && !t.QrCode.IsRevoked
                    ? t.QrCode.QrValue
                    : null,

                t.UsedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Result.Success<IReadOnlyList<TicketDto>>(tickets);
    }
}
