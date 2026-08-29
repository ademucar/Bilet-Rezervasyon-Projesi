namespace Ticketing.Application.Abstractions.Security;

/// <summary>
/// Uretilen access token ve onun gecerlilik süresi.
/// </summary>
public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);

/// <summary>
/// Uretilen refresh token.
///
/// Iki alan var çünkü iki farklı yere gidiyorlar:
///   Value     -> KULLANICIYA döner (tarayıcıda saklanir)
///   HashValue -> VERITABANINA yazilir
///
/// Bu ayrim kritik: veritabanina token'in kendisi değil hash'i gider.
/// Veritabani sizarsa saldirgan eline yalnızca geri cevrilemez ozetler
/// gecer, onlarla giriş yapamaz.
/// </summary>
public sealed record RefreshTokenResult(string Value, string HashValue, DateTimeOffset ExpiresAt);

public interface ITokenService
{
    AccessToken CreateAccessToken(Guid userId, string email, IReadOnlyCollection<string> roles);

    RefreshTokenResult CreateRefreshToken();

    /// <summary>
    /// Kullanicidan gelen ham token'i, veritabanindaki hash ile
    /// karsilastirabilmek için hash'ler.
    /// </summary>
    string HashRefreshToken(string refreshToken);
}
