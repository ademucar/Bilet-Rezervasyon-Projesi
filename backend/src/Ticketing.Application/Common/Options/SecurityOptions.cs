using System.ComponentModel.DataAnnotations;

namespace Ticketing.Application.Common.Options;

/// <summary>
/// Güvenlik ayarlari. appsettings "Security" bolumunden okunur.
///
/// Neden Application katmaninda? Çünkü bu değerleri KULLANAN kod burada
/// (LoginCommandHandler). Infrastructure'da olsaydı Application önü
/// referans almak zorunda kalırdı ve katman kuralı bozulurdu.
///
/// Degerleri OKUMA isi (configuration binding) ise WebApi'de yapiliyor;
/// Application yalnızca IOptions&lt;T&gt; üzerinden aliyor ve
/// yapilandirmanin nereden geldigini bilmiyor.
/// </summary>
public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    /// <summary>
    /// Hesap kilitlenmeden önce izin verilen başarısız giriş sayısı.
    ///
    /// 5 değeri bilinçli bir denge:
    ///   - Çok düşük olsaydı (2), sifresini yanlış yazan normal kullanıcı
    ///     surekli kilitlenir ve destek hattini kilitlerdi.
    ///   - Çok yüksek olsaydı (50), brute force korumasi anlamsizlasirdi.
    /// </summary>
    [Range(3, 20)]
    public int MaxFailedLoginAttempts { get; set; } = 5;

    /// <summary>
    /// Kilit süresi (dakika).
    ///
    /// Kalici kilit yapmiyorum. Kalici olsaydı saldirgan, hedefledigi
    /// kullanıcının hesabini kasten 5 kez yanlış şifre girerek KALICI
    /// olarak kilitleyebilirdi. Bu, kullanıcıyı kendi hesabindan eden
    /// bir servis dışı birakma saldirisi olurdu.
    ///
    /// Gecici kilit ise saldiriyi yavaslatir ama mesru kullanıcıyı
    /// kalici magdur etmez.
    /// </summary>
    [Range(1, 1440)]
    public int LockoutMinutes { get; set; } = 15;
}
