namespace Ticketing.Domain.Enums;

/// <summary>Odeme durumu. PDF sayfa 6.</summary>
public enum PaymentStatus
{
    /// <summary>Odeme kaydi olusturuldu, saglayiciya henuz gidilmedi.</summary>
    Pending = 1,

    /// <summary>Saglayiciya istek gonderildi, cevap bekleniyor.</summary>
    Processing = 2,

    /// <summary>Odeme basarili.</summary>
    Successful = 3,

    /// <summary>Odeme basarisiz. Kayit SILINMEZ -- denetim izi gerekir.</summary>
    Failed = 4,

    /// <summary>Kullanici odemeden vazgecti.</summary>
    Cancelled = 5,

    /// <summary>Iade yapildi (tam veya kismi).</summary>
    Refunded = 6
}
