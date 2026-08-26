using System.ComponentModel.DataAnnotations;

namespace Ticketing.Application.Common.Options;

/// <summary>
/// Guvenlik ayarlari. appsettings "Security" bolumunden okunur.
///
/// Neden Application katmaninda? Cunku bu degerleri KULLANAN kod burada
/// (LoginCommandHandler). Infrastructure'da olsaydi Application onu
/// referans almak zorunda kalirdi ve katman kurali bozulurdu.
///
/// Degerleri OKUMA isi (configuration binding) ise WebApi'de yapiliyor;
/// Application yalnizca IOptions&lt;T&gt; uzerinden aliyor ve
/// yapilandirmanin nereden geldigini bilmiyor.
/// </summary>
public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    /// <summary>
    /// Hesap kilitlenmeden once izin verilen basarisiz giris sayisi.
    ///
    /// 5 degeri bilincli bir denge:
    ///   - Cok dusuk olsaydi (2), sifresini yanlis yazan normal kullanici
    ///     surekli kilitlenir ve destek hattini kilitlerdi.
    ///   - Cok yuksek olsaydi (50), brute force korumasi anlamsizlasirdi.
    /// </summary>
    [Range(3, 20)]
    public int MaxFailedLoginAttempts { get; set; } = 5;

    /// <summary>
    /// Kilit suresi (dakika).
    ///
    /// KALICI kilit YAPMIYORUZ. Kalici olsaydi saldirgan, hedefledigi
    /// kullanicinin hesabini kasten 5 kez yanlis sifre girerek KALICI
    /// olarak kilitleyebilirdi. Bu, kullaniciyi kendi hesabindan eden
    /// bir servis disi birakma saldirisi olurdu.
    ///
    /// Gecici kilit ise saldiriyi yavaslatir ama mesru kullaniciyi
    /// kalici magdur etmez.
    /// </summary>
    [Range(1, 1440)]
    public int LockoutMinutes { get; set; } = 15;
}
