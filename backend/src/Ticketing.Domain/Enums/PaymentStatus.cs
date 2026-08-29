namespace Ticketing.Domain.Enums;

/// <summary>Ödeme durumu. PDF sayfa 6.</summary>
public enum PaymentStatus
{
    /// <summary>Ödeme kaydı oluşturuldu, saglayiciya henüz gidilmedi.</summary>
    Pending = 1,

    /// <summary>Saglayiciya istek gönderildi, cevap bekleniyor.</summary>
    Processing = 2,

    /// <summary>Ödeme başarılı.</summary>
    Successful = 3,

    /// <summary>Ödeme başarısız. Kayıt SILINMEZ -- denetim izi gerekir.</summary>
    Failed = 4,

    /// <summary>Kullanıcı odemeden vazgeçti.</summary>
    Cancelled = 5,

    /// <summary>İade yapıldı (tam veya kismi).</summary>
    Refunded = 6,
}
