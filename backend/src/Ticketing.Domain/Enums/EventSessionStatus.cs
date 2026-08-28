namespace Ticketing.Domain.Enums;

/// <summary>
/// Etkinlik oturumunun durumu.
///
/// Neden Event'ten ayri bir durum? Cunku cok oturumlu etkinliklerde
/// (ornegin 3 gunluk festival) bir oturum iptal olabilir ama digerleri
/// devam edebilir. Tek durum olsaydi tum etkinligi iptal etmek zorunda kalirdik.
/// </summary>
public enum EventSessionStatus
{
    /// <summary>Planlandi, satisa hazir.</summary>
    Scheduled = 1,

    /// <summary>Su an gerceklesiyor.</summary>
    InProgress = 2,

    /// <summary>Tamamlandi.</summary>
    Completed = 3,

    /// <summary>Iptal edildi. Biletleri iade surecine girer.</summary>
    Cancelled = 4,

    /// <summary>Ertelendi. Yeni tarih belirlenene kadar satis durur.</summary>
    Postponed = 5,
}
