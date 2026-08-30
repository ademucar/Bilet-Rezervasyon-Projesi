using Ticketing.Application.Common.Results;

namespace Ticketing.Application.Features.Auth;

/// <summary>
/// Kimlik doğrulama hatalari tek yerde.
///
/// Neden her handler'da tek tek yazmiyorum?
/// Çünkü aynı hata birden fazla yerde uretiliyor (örneğin
/// "geçersiz kimlik bilgileri" hem Login hem ChangePassword'de).
/// Iki yerde ayrı ayrı yazsaydim metinler zamanla birbirinden
/// ayrisirdi ve frontend iki farklı mesajla ugrasirdi.
/// </summary>
internal static class AuthErrors
{
    /// <summary>
    /// Kritik güvenlik karari: tek ve belirsiz hata mesaji
    ///
    /// "E-posta bulunamadı" ve "Şifre yanlış" diye AYRI mesajlar
    /// dondurmuyoruz. Ikisi de bu tek hatayi döner.
    ///
    /// Neden? Ayirsaydik, saldirgan hangi e-postalarin sistemde kayitli
    /// olduğunu ogrenebilirdi:
    ///
    ///   POST /login {"email":"ahmet@x.com","password":"deneme"}
    ///   -> "Şifre yanlış"     => ahmet@x.com KAYITLI
    ///
    ///   POST /login {"email":"mehmet@x.com","password":"deneme"}
    ///   -> "Kullanıcı yok"    => mehmet@x.com kayıtlı DEĞİL
    ///
    /// Buna "kullanıcı numaralandirma" (user enumeration) denir.
    /// Saldirgan geçerli e-posta listesi cikarip yalnızca onlara
    /// odaklanmis şifre saldirisi veya oltalama (phishing) yapar.
    ///
    /// Aynı sebeple "forgot-password" endpoint'i de e-posta kayıtlı
    /// olsun olmasın AYNI cevabi donecek.
    /// </summary>
    public static readonly Error InvalidCredentials = Error.Unauthorized(
        "auth.invalid_credentials",
        "E-posta veya şifre hatalı.");

    public static readonly Error EmailAlreadyInUse = Error.Conflict(
        "auth.email_in_use",
        "Bu e-posta adresi zaten kullanılıyor.");

    public static readonly Error AccountLocked = Error.Forbidden(
        "auth.account_locked",
        "Çok fazla başarısız giriş denemesi yapıldı. Hesabınız geçici olarak kilitlendi.");

    public static readonly Error AccountInactive = Error.Forbidden(
        "auth.account_inactive",
        "Hesabınız aktif değil. Lütfen destek ile iletisime gecin.");

    public static readonly Error InvalidRefreshToken = Error.Unauthorized(
        "auth.invalid_refresh_token",
        "Oturum bilgisi geçersiz. Lütfen tekrar giriş yapın.");

    public static readonly Error RefreshTokenReused = Error.Unauthorized(
        "auth.refresh_token_reused",
        "Güvenlik nedeniyle tüm oturumlarınız sonlandırıldı. Lütfen tekrar giriş yapın.");

    public static readonly Error UserNotFound = Error.NotFound(
        "auth.user_not_found",
        "Kullanıcı bulunamadı.");
}
