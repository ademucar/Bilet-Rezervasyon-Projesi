using Ticketing.Domain.Common;

namespace Ticketing.Domain.Events;

/// <summary>
/// Odeme basarisiz oldugunda firlatilir.
/// Dinleyenler: kullaniciya bildirim ve e-posta.
///
/// DIKKAT: Koltuklari serbest BIRAKMIYORUZ (bkz. docs/01-is-analizi.md soru 8).
/// Kullanici kalan sure icinde tekrar deneyebilmeli.
/// </summary>
public sealed record PaymentFailedDomainEvent(
    Guid PaymentId,
    Guid ReservationId,
    Guid UserId,
    string? FailureReason,
    DateTimeOffset OccurredOn) : IDomainEvent;
