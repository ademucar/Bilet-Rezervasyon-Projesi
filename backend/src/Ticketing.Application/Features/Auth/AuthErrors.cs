using Ticketing.Application.Common.Results;

namespace Ticketing.Application.Features.Auth;

/// <summary>
/// Kimlik dogrulama hatalari tek yerde.
///
/// Neden her handler'da tek tek yazmiyorum?
/// Cunku ayni hata birden fazla yerde uretiliyor (ornegin
/// "gecersiz kimlik bilgileri" hem Login hem ChangePassword'de).
/// Iki yerde ayri ayri yazsaydim metinler zamanla birbirinden
/// ayrisirdi ve frontend iki farkli mesajla ugrasirdi.
/// </summary>
internal static class AuthErrors
{
    /// <summary>
    /// ==================================================================
    /// KRITIK GUVENLIK KARARI: TEK VE BELIRSIZ HATA MESAJI
    /// ==================================================================
    /// "E-posta bulunamadi" ve "Sifre yanlis" diye AYRI mesajlar
    /// dondurmuyoruz. Ikisi de bu tek hatayi doner.
    ///
    /// NEDEN? Ayirsaydik, saldirgan hangi e-postalarin sistemde KAYITLI
    /// oldugunu ogrenebilirdi:
    ///
    ///   POST /login {"email":"ahmet@x.com","password":"deneme"}
    ///   -> "Sifre yanlis"     => ahmet@x.com KAYITLI
    ///
    ///   POST /login {"email":"mehmet@x.com","password":"deneme"}
    ///   -> "Kullanici yok"    => mehmet@x.com kayitli DEGIL
    ///
    /// Buna "kullanici numaralandirma" (user enumeration) denir.
    /// Saldirgan gecerli e-posta listesi cikarip yalnizca onlara
    /// odaklanmis sifre saldirisi veya oltalama (phishing) yapar.
    ///
    /// Ayni sebeple "forgot-password" endpoint'i de e-posta kayitli
    /// olsun olmasin AYNI cevabi donecek.
    /// ==================================================================
    /// </summary>
    public static readonly Error InvalidCredentials = Error.Unauthorized(
        "auth.invalid_credentials",
        "E-posta veya sifre hatali.");

    public static readonly Error EmailAlreadyInUse = Error.Conflict(
        "auth.email_in_use",
        "Bu e-posta adresi zaten kullaniliyor.");

    public static readonly Error AccountLocked = Error.Forbidden(
        "auth.account_locked",
        "Cok fazla basarisiz giris denemesi yapildi. Hesabiniz gecici olarak kilitlendi.");

    public static readonly Error AccountInactive = Error.Forbidden(
        "auth.account_inactive",
        "Hesabiniz aktif degil. Lutfen destek ile iletisime gecin.");

    public static readonly Error InvalidRefreshToken = Error.Unauthorized(
        "auth.invalid_refresh_token",
        "Oturum bilgisi gecersiz. Lutfen tekrar giris yapin.");

    public static readonly Error RefreshTokenReused = Error.Unauthorized(
        "auth.refresh_token_reused",
        "Guvenlik nedeniyle tum oturumlariniz sonlandirildi. Lutfen tekrar giris yapin.");

    public static readonly Error UserNotFound = Error.NotFound(
        "auth.user_not_found",
        "Kullanici bulunamadi.");
}
