using Ticketing.Domain.Common;

namespace Ticketing.Domain.Events;

/// <summary>
/// Odeme basarili olup rezervasyon onaylandiginda firlatilir.
/// Dinleyenler: bilet uretimi, QR uretimi, e-posta, SignalR SeatSold yayini.
/// </summary>
public sealed record ReservationConfirmedDomainEvent(
    Guid ReservationId,
    Guid UserId,
    Guid PaymentId,
    DateTimeOffset OccurredOn) : IDomainEvent;
