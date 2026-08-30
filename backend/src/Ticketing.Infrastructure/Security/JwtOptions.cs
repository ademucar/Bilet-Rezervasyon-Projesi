using System.ComponentModel.DataAnnotations;

namespace Ticketing.Infrastructure.Security;

/// <summary>
/// JWT yapilandirmasi. appsettings veya environment variable'dan okunur.
///
/// PDF Sprint 2: "JWT secret, Access token süresi, Refresh token süresi
/// environment variable olarak yonetilmelidir."
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>
    /// Token imzalama anahtari.
    ///
    /// MinimumLength = 32 ZORUNLU.
    /// HMAC-SHA256 için 256 bit (32 byte) anahtar gerekir. Daha kisa bir
    /// anahtarla kutuphane zaten hata verir -- ama o hata calisma zamaninda,
    /// ilk giriş denemesinde ortaya çıkar.
    ///
    /// Burada dogrulayarak uygulamanin BASLANGICTA patlamasini sagliyorum.
    /// Yanlis yapilandirmayla ayaga kalkip trafik almaya baslamasindansa
    /// hiç kalkmamasi iyidir.
    /// </summary>
    [Required]
    [MinLength(32, ErrorMessage = "JWT anahtari en az 32 karakter olmalıdır (HMAC-SHA256 için 256 bit).")]
    public string Secret { get; set; } = string.Empty;

    /// <summary>Token'i kim uretti. Dogrulamada kontrol edilir.</summary>
    [Required]
    public string Issuer { get; set; } = string.Empty;

    /// <summary>Token kimin için üretildi. Dogrulamada kontrol edilir.</summary>
    [Required]
    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// Access token omru (dakika). Varsayılan 15.
    ///
    /// Neden bu kadar kisa?
    /// Access token iptal edilemez -- imzasi geçerli olduğu surece kabul
    /// edilir; veritabanina bakilmaz (zaten amaci budur, her istekte
    /// veritabani sorgusu yapmamak).
    ///
    /// Dolayisiyla calinan bir access token, süresi dolana kadar
    /// kullanilabilir. 15 dakika, saldirganin elindeki zamani sinirlar.
    /// Kullanıcı deneyimi bozulmaz çünkü refresh token sessizce
    /// yenileme yapar.
    ///
    /// 24 saat verseydim, calinan bir token bir gün boyunca geçerli olurdu.
    /// </summary>
    [Range(1, 1440)]
    public int AccessTokenMinutes { get; set; } = 15;

    /// <summary>
    /// Refresh token omru (gün). Varsayılan 7.
    /// Bu token iptal edilebilir (veritabaninda kaydı var), o yüzden
    /// daha uzun olmasını kabul edilebilir.
    /// </summary>
    [Range(1, 365)]
    public int RefreshTokenDays { get; set; } = 7;
}
