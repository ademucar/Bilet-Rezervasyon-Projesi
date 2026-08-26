using Ticketing.Domain.Common;

namespace Ticketing.Domain.Entities;

/// <summary>
/// Refresh token kaydi. PDF Sprint 3'un su maddelerini karsilar:
///   - "Refresh Token rotation uygulanmalidir."
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
    /// Token'in KENDISI DEGIL, HASH'i saklanir.
    ///
    /// Bu cok onemli bir guvenlik karari. Neden?
    ///
    /// Refresh token, kullanicinin kimligini kanitlayan bir anahtardir --
    /// pratikte sifreye esdegerdir. Veritabani bir sekilde sizarsa
    /// (SQL injection, yedek dosyasinin calinmasi, ic tehdit), saldirgan
    /// tum token'lari ele gecirip herkesin hesabina girebilir.
    ///
    /// Hash sakladigimizda saldirgan eline sadece geri cevrilemez ozetler
    /// gecer; onlarla giris yapamaz.
    ///
    /// Sifreleri neden hash'liyorsak, refresh token'i da ayni sebeple
    /// hash'lemeliyiz. Cok sik atlanan bir noktadir.
    ///
    /// Not: Burada BCrypt gibi yavas bir algoritma DEGIL, SHA-256 gibi
    /// hizli bir algoritma kullanacagiz. Sebep: refresh token zaten
    /// yuksek entropili rastgele bir degerdir, sozluk saldirisina acik
    /// degildir. Sifreler tahmin edilebilir oldugu icin yavas algoritma
    /// gerektirir; token'lar gerektirmez.
    /// </summary>
    public string TokenHash { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public string? CreatedByIp { get; private set; }

    /// <summary>Iptal edildiyse ne zaman. null ise iptal edilmemis.</summary>
    public DateTimeOffset? RevokedAt { get; private set; }

    public string? RevokedByIp { get; private set; }

    /// <summary>
    /// ROTATION'IN KALBI.
    ///
    /// Kullanici token'ini yeniledignde eski token iptal edilir ve bu alana
    /// yeni token'in hash'i yazilir. Boylece bir zincir olusur:
    ///     token1 -> token2 -> token3 -> ...
    ///
    /// Bu zincir neden lazim? Su saldiriyi tespit etmek icin:
    ///
    ///   1. Saldirgan token2'yi caldi (ornegin XSS ile).
    ///   2. Gercek kullanici token2 ile yenileme yapti -> token3 aldi.
    ///      token2 artik iptal.
    ///   3. Saldirgan da token2 ile yenileme denedi.
    ///   4. Sistem "iptal edilmis bir token kullanildi" der.
    ///
    /// Bu ancak IKI ihtimalle olur: ya token calindi ya da bir hata var.
    /// Ikisi de ciddi. Bu durumda o kullanicinin TUM aktif token'larini
    /// iptal ederiz ve saldirgan da gercek kullanici da disari atilir.
    /// Kullanici tekrar giris yapar, saldirgan yapamaz.
    ///
    /// Bu alan olmasaydi calinmayi hic fark edemezdik.
    /// </summary>
    public string? ReplacedByTokenHash { get; private set; }

    public User User { get; private set; } = null!;

    // ---------------------------------------------------------------
    // Hesaplanan durumlar
    // ---------------------------------------------------------------

    public bool IsExpired() => DateTimeOffset.UtcNow >= ExpiresAt;

    public bool IsRevoked() => RevokedAt.HasValue;

    /// <summary>
    /// Token su an kullanilabilir mi?
    /// Hem suresi dolmamis hem de iptal edilmemis olmali.
    /// </summary>
    public bool IsActive() => !IsRevoked() && !IsExpired();

    // ---------------------------------------------------------------
    // Davranislar
    // ---------------------------------------------------------------

    public static RefreshToken Create(
        Guid userId,
        string tokenHash,
        DateTimeOffset expiresAt,
        string? createdByIp = null)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            throw new DomainException("Token hash'i bos olamaz.", "refresh_token.hash_required");
        }

        if (expiresAt <= DateTimeOffset.UtcNow)
        {
            throw new DomainException(
                "Token gecerlilik suresi gelecekte olmalidir.",
                "refresh_token.invalid_expiry");
        }

        return new RefreshToken(userId, tokenHash, expiresAt, createdByIp);
    }

    /// <summary>
    /// Token'i iptal eder. Zaten iptal edilmisse hicbir sey yapmaz (idempotent).
    /// </summary>
    /// <param name="replacedByTokenHash">
    /// Rotation sonucu yeni uretilen token'in hash'i. Logout'ta null gecilir,
    /// cunku yerine yeni bir token uretilmiyor.
    /// </param>
    public void Revoke(string? revokedByIp = null, string? replacedByTokenHash = null)
    {
        if (IsRevoked())
        {
            // Idempotent: iki kez iptal etmek hata degil.
            // Ilk iptalin zaman damgasini KORUYORUM -- ustune yazsaydim
            // denetim izini bozardim, gercek iptal anini kaybederdim.
            return;
        }

        RevokedAt = DateTimeOffset.UtcNow;
        RevokedByIp = revokedByIp;
        ReplacedByTokenHash = replacedByTokenHash;
    }
}
