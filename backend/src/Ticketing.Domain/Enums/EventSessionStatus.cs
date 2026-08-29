namespace Ticketing.Domain.Enums;

/// <summary>
/// Etkinlik oturumunun durumu.
///
/// Neden Event'ten ayrı bir durum? Çünkü çok oturumlu etkinliklerde
/// (örneğin 3 günlük festival) bir oturum iptal olabilir ama digerleri
/// devam edebilir. Tek durum olsaydı tüm etkinligi iptal etmek zorunda kalirdim.
/// </summary>
public enum EventSessionStatus
{
    /// <summary>Planlandi, satışa hazır.</summary>
    Scheduled = 1,

    /// <summary>Su an gerceklesiyor.</summary>
    InProgress = 2,

    /// <summary>Tamamlandı.</summary>
    Completed = 3,

    /// <summary>İptal edildi. Biletleri iade surecine girer.</summary>
    Cancelled = 4,

    /// <summary>Ertelendi. Yeni tarih belirlenene kadar satış durur.</summary>
    Postponed = 5,
}
