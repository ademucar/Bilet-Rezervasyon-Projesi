using System.ComponentModel.DataAnnotations;

namespace Ticketing.Infrastructure.Security;

/// <summary>
/// JWT yapilandirmasi. appsettings veya environment variable'dan okunur.
///
/// PDF Sprint 2: "JWT secret, Access token suresi, Refresh token suresi
/// environment variable olarak yonetilmelidir."
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>
    /// Token imzalama anahtari.
    ///
    /// MinimumLength = 32 ZORUNLU.
    /// HMAC-SHA256 icin 256 bit (32 byte) anahtar gerekir. Daha kisa bir
    /// anahtarla kutuphane zaten hata verir -- ama o hata calisma zamaninda,
    /// ilk giris denemesinde ortaya cikar.
    ///
    /// Burada dogrulayarak uygulamanin BASLANGICTA patlamasini sagliyoruz.
    /// Yanlis yapilandirmayla ayaga kalkip trafik almaya baslamasindansa
    /// hic kalkmamasi iyidir.
    /// </summary>
    [Required]
    [MinLength(32, ErrorMessage = "JWT anahtari en az 32 karakter olmalidir (HMAC-SHA256 icin 256 bit).")]
    public string Secret { get; set; } = string.Empty;

    /// <summary>Token'i kim uretti. Dogrulamada kontrol edilir.</summary>
    [Required]
    public string Issuer { get; set; } = string.Empty;

    /// <summary>Token kimin icin uretildi. Dogrulamada kontrol edilir.</summary>
    [Required]
    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// Access token omru (dakika). Varsayilan 15.
    ///
    /// NEDEN BU KADAR KISA?
    /// Access token IPTAL EDILEMEZ -- imzasi gecerli oldugu surece kabul
    /// edilir; veritabanina bakilmaz (zaten amaci budur, her istekte
    /// veritabani sorgusu yapmamak).
    ///
    /// Dolayisiyla calinan bir access token, suresi dolana kadar
    /// kullanilabilir. 15 dakika, saldirganin elindeki zamani sinirlar.
    /// Kullanici deneyimi bozulmaz cunku refresh token sessizce
    /// yenileme yapar.
    ///
    /// 24 saat verseydik, calinan bir token bir gun boyunca gecerli olurdu.
    /// </summary>
    [Range(1, 1440)]
    public int AccessTokenMinutes { get; set; } = 15;

    /// <summary>
    /// Refresh token omru (gun). Varsayilan 7.
    /// Bu token IPTAL EDILEBILIR (veritabaninda kaydi var), o yuzden
    /// daha uzun olmasi kabul edilebilir.
    /// </summary>
    [Range(1, 365)]
    public int RefreshTokenDays { get; set; } = 7;
}
