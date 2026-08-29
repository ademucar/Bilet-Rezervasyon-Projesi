namespace Ticketing.Domain.Enums;

/// <summary>
/// Rezervasyonun yasam dongusu. PDF sayfa 6.
/// Gecis kurallari Reservation entity'sinin içinde tanimli.
/// </summary>
public enum ReservationStatus
{
    /// <summary>Kayıt oluşturuldu, koltuklar henüz kilitlenmedi. Gecici ara durum.</summary>
    Pending = 1,

    /// <summary>Koltuklar kilitli, geri sayım isliyor. Normal başlangıç durumu.</summary>
    Locked = 2,

    /// <summary>Ödeme baslatildi, saglayicidan sonuç bekleniyor.</summary>
    PaymentPending = 3,

    /// <summary>Ödeme başarılı, biletler üretildi.</summary>
    Confirmed = 4,

    /// <summary>Süre doldu, koltuklar serbest birakildi. Background job yazar.</summary>
    Expired = 5,

    /// <summary>Kullanıcı veya sistem iptal etti.</summary>
    Cancelled = 6,

    /// <summary>İade edildi.</summary>
    Refunded = 7,
}
