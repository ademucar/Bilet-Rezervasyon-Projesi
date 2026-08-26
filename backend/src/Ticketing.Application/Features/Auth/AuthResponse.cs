namespace Ticketing.Application.Features.Auth;

/// <summary>
/// Basarili giris/kayit/yenileme sonucu.
///
/// ==================================================================
/// NEDEN ENTITY DEGIL DE DTO DONUYORUZ?
/// ==================================================================
/// PDF zorunlu kural: "Endpointler dogrudan Entity dondurmemelidir."
///
/// User entity'sini donseydik JSON'a PasswordHash, FailedLoginAttempts,
/// LockoutEndAt ve tum RefreshTokens koleksiyonu dahil olurdu.
/// Yani sifre hash'lerini ve token'lari tarayiciya gondermis olurduk.
///
/// Bu, [JsonIgnore] ile tek tek gizlenerek de "cozulebilir" ama o
/// yaklasim kirilgandir: yarin entity'ye yeni bir hassas alan
/// eklendiginde onu gizlemeyi unutmak yeterlidir.
///
/// DTO ile varsayilan davranis GUVENLIDIR: acikca yazmadigin hicbir
/// alan disari cikmaz.
/// ==================================================================
/// </summary>
public sealed record AuthResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    UserSummary User);

/// <summary>
/// Kullanicinin disari acilabilir bilgileri.
/// Hassas alanlar (PasswordHash, LockoutEndAt vb.) BILEREK yok.
/// </summary>
public sealed record UserSummary(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    bool IsEmailConfirmed,
    IReadOnlyCollection<string> Roles);
