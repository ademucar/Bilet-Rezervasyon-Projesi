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

    /// <summary>
    /// Kullanici kendi biletini iptal etti.
    /// </summary>
    /// <remarks>
    /// RefundCompleted'dan ayri tutuyorum: iade tutari SIFIR da
    /// olabiliyor (etkinlige 48 saatten az kala iptal edilirse).
    /// O durumda "Iadeniz tamamlandi" baslikli bir bildirim gondermek
    /// kullaniciyi yaniltirdi -- parasini bekler, gelmez.
    /// </remarks>
    TicketCancelled = 14,
}
