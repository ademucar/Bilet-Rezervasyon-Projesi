using Ticketing.Domain.Common;

namespace Ticketing.Domain.Events;

/// <summary>
/// Rezervasyon süresi doldugunda firlatilir (background job tarafından).
/// Dinleyenler: SignalR SeatReleased yayini, kullanıcıya bildirim.
/// </summary>
public sealed record ReservationExpiredDomainEvent(
    Guid ReservationId,
    Guid UserId,
    Guid EventSessionId,
    IReadOnlyList<Guid> EventSeatIds,
    DateTimeOffset OccurredOn) : IDomainEvent;
