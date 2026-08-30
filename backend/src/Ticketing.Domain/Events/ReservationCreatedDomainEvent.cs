using Ticketing.Domain.Common;

namespace Ticketing.Domain.Events;

/// <summary>
/// Rezervasyon olusturuldugunda firlatilir.
/// Dinleyenler: bildirim oluşturma, e-posta gonderme, SignalR SeatLocked yayini.
/// </summary>
public sealed record ReservationCreatedDomainEvent(
    Guid ReservationId,
    Guid UserId,
    Guid EventSessionId,
    IReadOnlyList<Guid> EventSeatIds,
    DateTimeOffset ExpiresAt,
    DateTimeOffset OccurredOn) : IDomainEvent;
