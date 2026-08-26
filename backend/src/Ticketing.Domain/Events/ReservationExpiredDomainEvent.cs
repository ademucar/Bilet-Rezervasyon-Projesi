using Ticketing.Domain.Common;

namespace Ticketing.Domain.Events;

/// <summary>
/// Rezervasyon suresi doldugunda firlatilir (background job tarafindan).
/// Dinleyenler: SignalR SeatReleased yayini, kullaniciya bildirim.
/// </summary>
public sealed record ReservationExpiredDomainEvent(
    Guid ReservationId,
    Guid UserId,
    Guid EventSessionId,
    IReadOnlyList<Guid> EventSeatIds,
    DateTimeOffset OccurredOn) : IDomainEvent;
