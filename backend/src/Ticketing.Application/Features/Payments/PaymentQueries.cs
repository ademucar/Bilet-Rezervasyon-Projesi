using MediatR;
using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Abstractions.Payments;
using Ticketing.Application.Abstractions.Persistence;
using Ticketing.Application.Abstractions.Security;
using Ticketing.Application.Abstractions.Time;
using Ticketing.Application.Common.Results;
using Ticketing.Domain.Entities;
using Ticketing.Domain.Enums;

namespace Ticketing.Application.Features.Payments;

// ===================================================================
// DTO'lar
// ===================================================================

public sealed record PaymentDto(
    Guid Id,
    Guid ReservationId,
    string ReservationCode,
    PaymentStatus Status,
    string ProviderName,
    /// <summary>
    /// Saglayici islem referansi.
    ///
    /// Bu alani kullaniciya DONUYORUZ cunku destek talebinde
    /// "islem numaram su" diyebilmeli. Hassas bir bilgi degil --
    /// tek basina hicbir islem yapilamaz.
    /// </summary>
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
    /// Odeme sorgusunu DTO'ya projelendirir.
    ///
    /// Filtre PROJEKSIYONDAN ONCE uygulanmali -- Sprint 7'de bu
    /// tuzaga dustuk: EF, olusturdugu DTO uzerinde WHERE calistiramiyor.
    /// Bu yuzden metot IQueryable&lt;Payment&gt; aliyor.
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

// ===================================================================
// DETAY -- PDF: GET /api/v1/payments/{id}
// ===================================================================

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
                Error.Unauthorized("auth.required", "Giris yapmalisiniz."));
        }

        var isAdmin = _currentUser.Roles.Contains(Role.Names.Admin);

        // Sahiplik kontrolu SORGUYA dahil.
        //
        // Admin destek islerini yapabilmek icin her odemeyi gorebilmeli;
        // normal kullanici yalnizca kendisininkini.
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

// ===================================================================
// IADE -- PDF: POST /api/v1/payments/{id}/refund
// ===================================================================

/// <summary>
/// Iade islemi.
/// </summary>
/// <param name="Amount">
/// Iade tutari. null ise KALAN TUM tutar iade edilir.
///
/// Kismi iade destegi var cunku bir rezervasyondaki 4 biletten
/// yalnizca 2'si iade edilebilir.
/// </param>
public sealed record RefundPaymentCommand(Guid PaymentId, decimal? Amount, string? Reason)
    : IRequest<Result<PaymentDto>>;

internal sealed class RefundPaymentCommandHandler
    : IRequestHandler<RefundPaymentCommand, Result<PaymentDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IPaymentService _paymentService;
    private readonly IDateTimeProvider _clock;

    public RefundPaymentCommandHandler(
        IApplicationDbContext context,
        IPaymentService paymentService,
        IDateTimeProvider clock)
    {
        _context = context;
        _paymentService = paymentService;
        _clock = clock;
    }

    public async Task<Result<PaymentDto>> Handle(
        RefundPaymentCommand request,
        CancellationToken cancellationToken)
    {
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

        if (payment.Status is not (PaymentStatus.Successful or PaymentStatus.Refunded))
        {
            return Result.Failure<PaymentDto>(PaymentErrors.NotRefundable);
        }

        var refundable = payment.GetRefundableAmount();
        var amount = request.Amount ?? refundable.Amount;

        if (amount <= 0 || amount > refundable.Amount)
        {
            return Result.Failure<PaymentDto>(Error.Validation(
                "payment.invalid_refund_amount",
                $"Iade tutari 0 ile {refundable.Amount} arasinda olmalidir."));
        }

        // ==============================================================
        // ONCE SAGLAYICI, SONRA VERITABANI
        // ==============================================================
        // Odeme BASLATIRKEN once kaydediyorduk (para gidip izimizin
        // kalmamasini onlemek icin). IADE'de sira TERS:
        //
        // Once veritabanina "iade edildi" yazip sonra saglayici
        // reddetseydi, kullanici parasini almadan sistemde "iade
        // edildi" gorunurdu. Bu daha kotu bir durum: musteri parasini
        // bekler, biz "iade ettik" deriz.
        //
        // Once saglayiciya gidip onay alirsak, en kotu ihtimalle para
        // gider ama bizde kayit olmaz -- mutabakatta yakalanabilir
        // ve duzeltilebilir bir durumdur.
        var providerResult = await _paymentService
            .RefundPaymentAsync(payment.ProviderReference ?? string.Empty, amount, cancellationToken)
            .ConfigureAwait(false);

        if (!providerResult.IsSuccess)
        {
            return Result.Failure<PaymentDto>(Error.Conflict(
                "payment.refund_rejected",
                providerResult.ErrorMessage ?? "Iade saglayici tarafindan reddedildi."));
        }

        payment.Refund(new Domain.ValueObjects.Money(amount, payment.Amount.Currency),
                       providerResult.ProviderReference);

        var now = _clock.UtcNow;
        var reservation = payment.Reservation;

        // TAM iade ise rezervasyonu ve biletleri de iade et.
        //
        // Kismi iadede rezervasyon Confirmed kaliyor: kullanicinin
        // hala gecerli biletleri var.
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

            // Koltuklari tekrar satisa ac.
            //
            // Bu adim atlanirsa koltuk kalici olarak "satilmis"
            // kalir ve bir daha kimseye satilamaz -- gelir kaybi.
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
                "Iadeniz tamamlandi",
                $"{reservation.ReservationCode} numarali rezervasyonunuz icin " +
                $"{amount} {payment.Amount.Currency} iade edildi.",
                reservation.Id));
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var dto = await _context.Payments
            .Where(p => p.Id == payment.Id)
            .ToDto()
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return dto is null
            ? Result.Failure<PaymentDto>(PaymentErrors.NotFound)
            : Result.Success(dto);
    }
}

// ===================================================================
// KULLANICININ BILETLERI -- PDF sayfa 4
// ===================================================================

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
                Error.Unauthorized("auth.required", "Giris yapmalisiniz."));
        }

        var query = _context.Tickets.AsNoTracking().Where(t => t.UserId == userId);

        if (request.Status.HasValue)
        {
            query = query.Where(t => t.Status == request.Status.Value);
        }

        var tickets = await query
            // Yaklasan etkinlikler once.
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
                // Iptal edilmis veya kullanilmis biletin QR'ini
                // gondermenin bir faydasi yok; hassas bir deger
                // oldugu icin gereksiz yere yaymiyoruz.
                t.Status == TicketStatus.Active && t.QrCode != null && !t.QrCode.IsRevoked
                    ? t.QrCode.QrValue
                    : null,

                t.UsedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Result.Success<IReadOnlyList<TicketDto>>(tickets);
    }
}
