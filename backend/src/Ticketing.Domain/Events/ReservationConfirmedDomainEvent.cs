using Ticketing.Domain.Common;

namespace Ticketing.Domain.Events;

/// <summary>
/// Ödeme başarılı olup rezervasyon onaylandiginda firlatilir.
/// Dinleyenler: bilet üretimi, QR üretimi, e-posta, SignalR SeatSold yayini.
/// </summary>
public sealed record ReservationConfirmedDomainEvent(
    Guid ReservationId,
    Guid UserId,
    Guid PaymentId,
    DateTimeOffset OccurredOn) : IDomainEvent;
