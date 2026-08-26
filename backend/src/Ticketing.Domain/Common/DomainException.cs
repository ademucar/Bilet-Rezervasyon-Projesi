namespace Ticketing.Domain.Common;

/// <summary>
/// Bir IS KURALI ihlal edildiginde firlatilir.
///
/// Bu tipi ayri bir sinif yapmamin sebebi, hatalari iki gruba ayirabilmek:
///
///   1. DomainException  -> "Suresi dolmus rezervasyonda odeme baslatilamaz."
///      Bu bir HATA DEGIL, beklenen bir durumdur. Kullaniciya anlamli bir
///      mesaj gostermeliyiz ve HTTP 400/409 donmeliyiz. Alarm calmamali.
///
///   2. Diger exception'lar -> NullReferenceException, veritabani baglanti
///      hatasi vb. Bunlar GERCEK hatalardir. HTTP 500 donmeli, tam stack
///      trace loglanmali ve gelistiriciye bildirilmelidir.
///
/// Sprint 2'de yazacagimiz global exception middleware bu ayrimi kullanacak.
/// Ikisini ayirmasaydik ya kullaniciya "Sunucu hatasi" der ya da gercek
/// hatalari 400 olarak gizleyip fark etmezdik.
/// </summary>
public class DomainException : Exception
{
    /// <summary>
    /// Makine tarafindan okunabilir hata kodu. Ornek: "reservation.expired".
    ///
    /// Neden sadece mesaj yetmiyor? Frontend'in hataya gore farkli davranmasi
    /// gerekiyor: sure dolduysa koltuk haritasini yenile, koltuk kapildiysa
    /// baska bir sey yap. Mesaj metnine bakarak karar vermek kirilgan olur --
    /// metni degistirdigimiz gun frontend bozulur. Kod sabit kalir.
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
