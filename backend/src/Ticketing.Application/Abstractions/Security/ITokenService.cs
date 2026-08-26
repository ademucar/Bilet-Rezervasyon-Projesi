namespace Ticketing.Application.Abstractions.Security;

/// <summary>
/// Uretilen access token ve onun gecerlilik suresi.
/// </summary>
public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);

/// <summary>
/// Uretilen refresh token.
///
/// Iki alan var cunku iki farkli yere gidiyorlar:
///   Value     -> KULLANICIYA doner (tarayicida saklanir)
///   HashValue -> VERITABANINA yazilir
///
/// Bu ayrim kritik: veritabanina token'in kendisi degil hash'i gider.
/// Veritabani sizarsa saldirgan eline yalnizca geri cevrilemez ozetler
/// gecer, onlarla giris yapamaz.
/// </summary>
public sealed record RefreshTokenResult(string Value, string HashValue, DateTimeOffset ExpiresAt);

public interface ITokenService
{
    AccessToken CreateAccessToken(Guid userId, string email, IReadOnlyCollection<string> roles);

    RefreshTokenResult CreateRefreshToken();

    /// <summary>
    /// Kullanicidan gelen ham token'i, veritabanindaki hash ile
    /// karsilastirabilmek icin hash'ler.
    /// </summary>
    string HashRefreshToken(string refreshToken);
}
