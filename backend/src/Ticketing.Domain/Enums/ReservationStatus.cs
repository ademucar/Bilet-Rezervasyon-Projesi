namespace Ticketing.Domain.Enums;

/// <summary>
/// Rezervasyonun yasam dongusu. PDF sayfa 6.
/// Gecis kurallari Reservation entity'sinin icinde tanimli.
/// </summary>
public enum ReservationStatus
{
    /// <summary>Kayit olusturuldu, koltuklar henuz kilitlenmedi. Gecici ara durum.</summary>
    Pending = 1,

    /// <summary>Koltuklar kilitli, geri sayim isliyor. Normal baslangic durumu.</summary>
    Locked = 2,

    /// <summary>Odeme baslatildi, saglayicidan sonuc bekleniyor.</summary>
    PaymentPending = 3,

    /// <summary>Odeme basarili, biletler uretildi.</summary>
    Confirmed = 4,

    /// <summary>Sure doldu, koltuklar serbest birakildi. Background job yazar.</summary>
    Expired = 5,

    /// <summary>Kullanici veya sistem iptal etti.</summary>
    Cancelled = 6,

    /// <summary>Iade edildi.</summary>
    Refunded = 7
}
