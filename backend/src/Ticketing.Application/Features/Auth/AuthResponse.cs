namespace Ticketing.Application.Features.Auth;

/// <summary>
/// Başarılı giriş/kayıt/yenileme sonucu.
///
/// ==================================================================
/// NEDEN ENTITY DEĞİL DE DTO DONUYORUZ?
/// ==================================================================
/// PDF zorunlu kural: "Endpointler doğrudan Entity dondurmemelidir."
///
/// User entity'sini donseydik JSON'a PasswordHash, FailedLoginAttempts,
/// LockoutEndAt ve tüm RefreshTokens koleksiyonu dahil olurdu.
/// Yani şifre hash'lerini ve token'lari tarayiciya gondermis olurduk.
///
/// Bu, [JsonIgnore] ile tek tek gizlenerek de "cozulebilir" ama o
/// yaklasim kirilgandir: yarin entity'ye yeni bir hassas alan
/// eklendiginde önü gizlemeyi unutmak yeterlidir.
///
/// DTO ile varsayılan davranis GUVENLIDIR: acikca yazmadigin hiçbir
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
/// Kullanıcının disari acilabilir bilgileri.
/// Hassas alanlar (PasswordHash, LockoutEndAt vb.) BILEREK yok.
/// </summary>
public sealed record UserSummary(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    bool IsEmailConfirmed,
    IReadOnlyCollection<string> Roles);
