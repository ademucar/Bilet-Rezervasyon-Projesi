namespace Ticketing.Domain.Common;

/// <summary>
/// Bir is kurali ihlal edildiginde firlatilir.
///
/// Bu tipi ayrı bir sinif yapmamin sebebi, hatalari iki gruba ayirabilmek:
///
///   1. DomainException  -> "Süresi dolmuş rezervasyonda ödeme baslatilamaz."
///      Bu bir hata değil, beklenen bir durumdur. Kullanıcıya anlamlı bir
///      mesaj gostermeliyim ve HTTP 400/409 donmeliyim. Alarm calmamali.
///
///   2. Diger exception'lar -> NullReferenceException, veritabani bağlantı
///      hatası vb. Bunlar GERCEK hatalardir. HTTP 500 donmeli, tam stack
///      trace loglanmali ve gelistiriciye bildirilmelidir.
///
/// Sprint 2'de yazacagim global exception middleware bu ayrimi kullanacak.
/// Ikisini ayirmasaydim ya kullanıcıya "Sunucu hatası" der ya da gerçek
/// hatalari 400 olarak gizleyip fark etmezdik.
/// </summary>
public class DomainException : Exception
{
    /// <summary>
    /// Makine tarafından okunabilir hata kodu. Ornek: "reservation.expired".
    ///
    /// Neden sadece mesaj yetmiyor? Frontend'in hataya göre farklı davranmasi
    /// gerekiyor: süre dolduysa koltuk haritasini yenile, koltuk kapildiysa
    /// başka bir sey yap. Mesaj metnine bakarak karar vermek kirilgan olur --
    /// metni degistirdigim gün frontend bozulur. Kod sabit kalır.
    /// </summary>
    public string? ErrorCode { get; }

    public DomainException()
    {
    }

    public DomainException(string message)
        : base(message)
    {
    }

    public DomainException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public DomainException(string message, string errorCode)
        : base(message)
    {
        ErrorCode = errorCode;
    }
}
