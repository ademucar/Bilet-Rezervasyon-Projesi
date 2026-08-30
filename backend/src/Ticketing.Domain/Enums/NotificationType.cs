namespace Ticketing.Domain.Enums;

/// <summary>
/// Bildirim türü. PDF Sprint 14'teki "Bildirim Olusturulacak Islemler"
/// listesinin birebir karşılığı.
/// </summary>
public enum NotificationType
{
    Welcome = 1,
    ReservationCreated = 2,
    ReservationExpiring = 3,
    ReservationExpired = 4,
    PaymentSucceeded = 5,
    PaymentFailed = 6,
    TicketCreated = 7,
    EventReminder = 8,
    EventCancelled = 9,
    RefundCompleted = 10,
    ReportReady = 11,
    OrganizerApplicationApproved = 12,
    OrganizerApplicationRejected = 13,
}
