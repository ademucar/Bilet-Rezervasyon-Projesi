using Ticketing.Domain.Common;

namespace Ticketing.Domain.Entities;

/// <summary>
/// Refresh token kaydı. PDF Sprint 3'un su maddelerini karsilar:
///   - "Refresh Token rotation uygulanmalıdır."
///   - "Eski Refresh Token tekrar kullanilamamalidir."
///   - "Logout isleminde token iptal edilmelidir."
/// </summary>
public class RefreshToken : Entity
{
    private RefreshToken() => TokenHash = string.Empty;

    private RefreshToken(Guid userId, string tokenHash, DateTimeOffset expiresAt, string? createdByIp)
    {
        UserId = userId;
        TokenHash = tokenHash;
        ExpiresAt = expiresAt;
        CreatedByIp = createdByIp;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid UserId { get; private set; }

    /// <summary>
    /// Token'in KENDISI DEĞİL, HASH'i saklanir.
    ///
    /// Bu çok önemli bir güvenlik karari. Neden?
    ///
    /// Refresh token, kullanıcının kimligini kanitlayan bir anahtardir --
    /// pratikte sifreye esdegerdir. Veritabani bir şekilde sizarsa
    /// (SQL injection, yedek dosyasinin calinmasi, ic tehdit), saldirgan
    /// tüm token'lari ele gecirip herkesin hesabina girebilir.
    ///
    /// Hash sakladigimizda saldirgan eline sadece geri cevrilemez ozetler
    /// gecer; onlarla giriş yapamaz.
    ///
    /// Sifreleri neden hash'liyorsak, refresh token'i da aynı sebeple
    /// hash'lemeliyiz. Çok sik atlanan bir noktadir.
    ///
    /// Not: Burada BCrypt gibi yavas bir algoritma DEĞİL, SHA-256 gibi
    /// hizli bir algoritma kullanacagim. Sebep: refresh token zaten
    /// yüksek entropili rastgele bir degerdir, sozluk saldirisina açık
    /// degildir. Şifreler tahmin edilebilir olduğu için yavas algoritma
    /// gerektirir; token'lar gerektirmez.
    /// </summary>
    public string TokenHash { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public string? CreatedByIp { get; private set; }

    /// <summary>İptal edildiyse ne zaman. null ise iptal edilmemis.</summary>
    public DateTimeOffset? RevokedAt { get; private set; }

    public string? RevokedByIp { get; private set; }

    /// <summary>
    /// ROTATION'IN KALBI.
    ///
    /// Kullanıcı token'ini yeniledignde eski token iptal edilir ve bu alana
    /// yeni token'in hash'i yazilir. Boylece bir zincir olusur:
    ///     token1 -> token2 -> token3 -> ...
    ///
    /// Bu zincir neden lazim? Su saldiriyi tespit etmek için:
    ///
    ///   1. Saldirgan token2'yi caldi (örneğin XSS ile).
    ///   2. Gerçek kullanıcı token2 ile yenileme yapti -> token3 aldi.
    ///      token2 artık iptal.
    ///   3. Saldirgan da token2 ile yenileme denedi.
    ///   4. Sistem "iptal edilmiş bir token kullanıldı" der.
    ///
    /// Bu ancak IKI ihtimalle olur: ya token calindi ya da bir hata var.
    /// Ikisi de ciddi. Bu durumda o kullanıcının TÜM aktif token'larini
    /// iptal ederiz ve saldirgan da gerçek kullanıcı da disari atilir.
    /// Kullanıcı tekrar giriş yapar, saldirgan yapamaz.
    ///
    /// Bu alan olmasaydı calinmayi hiç fark edemezdik.
    /// </summary>
    public string? ReplacedByTokenHash { get; private set; }

    public User User { get; private set; } = null!;

    // Hesaplanan durumlar

    public bool IsExpired() => DateTimeOffset.UtcNow >= ExpiresAt;

    public bool IsRevoked() => RevokedAt.HasValue;

    /// <summary>
    /// Token su an kullanilabilir mi?
    /// Hem süresi dolmamis hem de iptal edilmemis olmalı.
    /// </summary>
    public bool IsActive() => !IsRevoked() && !IsExpired();

    // Davranislar

    public static RefreshToken Create(
        Guid userId,
        string tokenHash,
        DateTimeOffset expiresAt,
        string? createdByIp = null)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            throw new DomainException("Token hash'i boş olamaz.", "refresh_token.hash_required");
        }

        if (expiresAt <= DateTimeOffset.UtcNow)
        {
            throw new DomainException(
                "Token gecerlilik süresi gelecekte olmalıdır.",
                "refresh_token.invalid_expiry");
        }

        return new RefreshToken(userId, tokenHash, expiresAt, createdByIp);
    }

    /// <summary>
    /// Token'i iptal eder. Zaten iptal edilmisse hiçbir sey yapmaz (idempotent).
    /// </summary>
    /// <param name="replacedByTokenHash">
    /// Rotation sonucu yeni uretilen token'in hash'i. Logout'ta null gecilir,
    /// çünkü yerine yeni bir token uretilmiyor.
    /// </param>
    public void Revoke(string? revokedByIp = null, string? replacedByTokenHash = null)
    {
        if (IsRevoked())
        {
            // Idempotent: iki kez iptal etmek hata değil.
            // İlk iptalin zaman damgasini KORUYORUM -- ustune yazsaydim
            // denetim izini bozardim, gerçek iptal anini kaybederdim.
            return;
        }

        RevokedAt = DateTimeOffset.UtcNow;
        RevokedByIp = revokedByIp;
        ReplacedByTokenHash = replacedByTokenHash;
    }
}
