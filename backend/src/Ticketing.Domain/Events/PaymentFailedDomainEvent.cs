using Ticketing.Domain.Common;

namespace Ticketing.Domain.Events;

/// <summary>
/// Ödeme başarısız olduğunda firlatilir.
/// Dinleyenler: kullanıcıya bildirim ve e-posta.
///
/// DIKKAT: Koltukları serbest BIRAKMIYORUZ (bkz. docs/01-is-analizi.md soru 8).
/// Kullanıcı kalan süre içinde tekrar deneyebilmeli.
/// </summary>
public sealed record PaymentFailedDomainEvent(
    Guid PaymentId,
    Guid ReservationId,
    Guid UserId,
    string? FailureReason,
    DateTimeOffset OccurredOn) : IDomainEvent;
