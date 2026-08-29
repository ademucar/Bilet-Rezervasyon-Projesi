using Ticketing.Domain.Common;

namespace Ticketing.Domain.Entities;

/// <summary>
/// Sistem kullanicisi. Rolleri UserRoles üzerinden tasir.
/// </summary>
public class User : AuditableEntity
{
    /// <summary>
    /// EF Core için private parametresiz yapici.
    ///
    /// EF veritabanindan satır okurken nesneyi olusturmak zorunda ama
    /// bizim kurallarimizi (e-posta boş olamaz vb.) tekrar calistirmasina
    /// gerek yok -- o veriler zaten dogrulanmis halde kaydedilmisti.
    ///
    /// private yaptım ki bizim kodumuz yanlislikla boş bir User uretemesin.
    /// EF reflection kullandigi için private yapiciyi gorebiliyor.
    /// </summary>
    private User()
    {
        Email = string.Empty;
        PasswordHash = string.Empty;
        FirstName = string.Empty;
        LastName = string.Empty;
    }

    private User(string email, string passwordHash, string firstName, string lastName)
    {
        Email = email;
        PasswordHash = passwordHash;
        FirstName = firstName;
        LastName = lastName;
        IsActive = true;
    }

    /// <summary>
    /// Kullanıcının e-postası. Her zaman küçük harfe cevrilerek saklanir.
    ///
    /// Neden? "Ahmet@Gmail.com" ile "ahmet@gmail.com" aynı kisidir. Ham
    /// haliyle saklarsak iki ayrı hesap acilabilir ve unique index bunu
    /// engellemez. Normalizasyonu TEK yerde (aşağıdaki Create metodunda)
    /// yapıyorum; 20 ayrı yerde ToLower() yazmak yerine.
    /// </summary>
    public string Email { get; private set; }

    /// <summary>
    /// Sifrenin HASH'i. Sifrenin kendisi hiçbir yerde saklanmaz.
    ///
    /// Hash'leme islemi Domain'de DEĞİL, Infrastructure'da yapilacak
    /// (Sprint 3). Çünkü hash algoritmasi (BCrypt, Argon2) bir altyapi
    /// tercihidir ve Domain'in framework bağımsız kalmasi gerekiyor.
    /// Domain sadece "burada bir hash var" bilgisini tasir.
    /// </summary>
    public string PasswordHash { get; private set; }

    public string FirstName { get; private set; }

    public string LastName { get; private set; }

    public string? PhoneNumber { get; private set; }

    public bool IsEmailConfirmed { get; private set; }

    /// <summary>
    /// Hesap aktif mi? Admin bir kullanıcıyı pasife alabilir.
    /// Silmek yerine pasife almak, gecmis biletlerin ve raporlarin
    /// bozulmamasini saglar.
    /// </summary>
    public bool IsActive { get; private set; }

    // ---------------------------------------------------------------
    // Brute force korumasi (PDF Sprint 15: "Brute force korumasi")
    // ---------------------------------------------------------------

    /// <summary>Ust uste başarısız giriş denemesi sayısı.</summary>
    public int FailedLoginAttempts { get; private set; }

    /// <summary>
    /// Hesabin kilitli kalacagi zamanin sonu. null ise kilitli değil.
    /// </summary>
    public DateTimeOffset? LockoutEndAt { get; private set; }

    // ---------------------------------------------------------------
    // Şifre sıfırlama (PDF Sprint 3)
    // ---------------------------------------------------------------

    /// <summary>
    /// Şifre sıfırlama tokeninin HASH'i.
    ///
    /// Refresh token'da olduğu gibi burada da token'in KENDISI değil
    /// hash'i saklaniyor. Sebep aynı: veritabani sizarsa saldirgan
    /// bu token'larla herkesin sifresini sifirlayabilirdi.
    ///
    /// ------------------------------------------------------------------
    /// NEDEN AYRI TABLO DEĞİL DE User UZERINDE IKI ALAN?
    /// ------------------------------------------------------------------
    /// Bir kullanıcının aynı anda en fazla BIR aktif şifre sıfırlama
    /// talebi olmalı. Ayrı tablo olsaydı birden fazla kayıt olusabilir
    /// ve "hangisi geçerli?" sorusu ortaya çıkardı -- ayrıca eskilerini
    /// temizlemek için bir job yazmak gerekirdi.
    ///
    /// Tek alan olduğu için yeni talep otomatik olarak eskisinin
    /// USTUNE YAZIYOR; eski link anında geçersiz oluyor. Bu davranis
    /// hem daha basit hem de daha güvenli.
    ///
    /// PDF'in ER diyagramina yeni tablo eklememis olmamin sebebi de bu.
    /// </summary>
    public string? PasswordResetTokenHash { get; private set; }

    /// <summary>
    /// PDF: "Şifre sıfırlama tokeni SURELI olmalıdır."
    ///
    /// Suresiz olsaydı, e-posta kutusuna bir kez erisen biri (eski
    /// telefon, paylasilan bilgisayar, sizmis e-posta arsivi) aylar
    /// sonra bile hesabi ele gecirebilirdi.
    /// </summary>
    public DateTimeOffset? PasswordResetTokenExpiresAt { get; private set; }

    private readonly List<UserRole> _userRoles = [];

    /// <summary>
    /// IReadOnlyCollection donuyorum, List değil.
    ///
    /// List donseydim disaridan user.UserRoles.Add(...) yazilabilirdi ve
    /// rol atama kurallarini (örneğin "Organizatör rolü ancak basvuru
    /// onaylanirsa verilir") atlamak mumkun olurdu. Rol ekleme yetkisi
    /// sadece aşağıdaki AssignRole metodundadir.
    /// </summary>
    public IReadOnlyCollection<UserRole> UserRoles => _userRoles.AsReadOnly();

    private readonly List<RefreshToken> _refreshTokens = [];

    public IReadOnlyCollection<RefreshToken> RefreshTokens => _refreshTokens.AsReadOnly();

    // ---------------------------------------------------------------
    // Davranislar
    // ---------------------------------------------------------------

    /// <summary>
    /// Yeni kullanıcı oluşturur.
    ///
    /// Neden yapici (constructor) yerine static factory metot?
    ///
    /// 1) Isim verebiliyorum. "User.Create(...)" ile "new User(...)" arasında
    ///    okunabilirlik farki var; ileride "User.CreateFromGoogleLogin(...)"
    ///    gibi ikinci bir yol eklersem ikisini isimle ayırt edebilirim.
    ///    Iki farklı yapici olsaydı imzalari karisirdi.
    ///
    /// 2) Doğrulama ve normalizasyon tek kapida toplaniyor. Bu metodu
    ///    kullanmadan geçerli bir User uretmek mumkun değil.
    /// </summary>
    public static User Create(string email, string passwordHash, string firstName, string lastName)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new DomainException("E-posta boş olamaz.", "user.email_required");
        }

        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new DomainException("Şifre hash'i boş olamaz.", "user.password_required");
        }

        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
        {
            throw new DomainException("Ad ve soyad boş olamaz.", "user.name_required");
        }

        // ToLowerInvariant, ToLower değil.
        //
        // Turkce kulturde ToLower() "I" harfini "i" değil "ı" yapar
        // (noktasiz i). "AHMET@X.COM" -> "ahmet@x.com" beklerken
        // "ahmet@x.com" yerine farklı bir metin uretebilir.
        // Bu, meshur "Turkish I problem"idir ve e-posta eslesmesini bozar.
        // Invariant kultur bu tuzagi ortadan kaldirir.
        return new User(
            email.Trim().ToLowerInvariant(),
            passwordHash,
            firstName.Trim(),
            lastName.Trim());
    }

    public void AssignRole(Role role)
    {
        ArgumentNullException.ThrowIfNull(role);

        // Aynı rolü iki kez eklemeyi sessizce yok sayiyorum.
        // Bunu hata yapmadim çünkü "kullanıcıya Admin rolü ver" isteği
        // idempotent olmalı: iki kez cagrilirsa sonuç aynı olmalı.
        if (_userRoles.Exists(ur => ur.RoleId == role.Id))
        {
            return;
        }

        _userRoles.Add(new UserRole(Id, role.Id));
    }

    public void RemoveRole(Guid roleId) => _userRoles.RemoveAll(ur => ur.RoleId == roleId);

    public void ConfirmEmail() => IsEmailConfirmed = true;

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;

    public void ChangePasswordHash(string newPasswordHash)
    {
        if (string.IsNullOrWhiteSpace(newPasswordHash))
        {
            throw new DomainException("Şifre hash'i boş olamaz.", "user.password_required");
        }

        PasswordHash = newPasswordHash;

        // Şifre degistiginde başarısız deneme sayacini sifirliyorum.
        // Mantik: kullanıcı kimligini kanitlamis oldu, cezayi kaldiralim.
        ResetFailedLoginAttempts();

        // Kullanilmamis bir sıfırlama tokeni varsa GECERSIZ KIL.
        //
        // Senaryo: kullanıcı "sifremi unuttum" dedi, e-posta geldi ama
        // sonra sifresini hatirlayip normal yoldan degistirdi.
        // O eski link hâlâ çalışıyor olsaydı, e-postasina erisen biri
        // gunler sonra sifreyi tekrar degistirebilirdi.
        ClearPasswordResetToken();
    }

    // ---------------------------------------------------------------
    // Şifre sıfırlama akışı
    // ---------------------------------------------------------------

    public void SetPasswordResetToken(string tokenHash, DateTimeOffset expiresAt)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            throw new DomainException("Sıfırlama token'i boş olamaz.", "user.reset_token_required");
        }

        // Yeni talep eskisinin USTUNE yazar -> eski link anında geçersiz.
        PasswordResetTokenHash = tokenHash;
        PasswordResetTokenExpiresAt = expiresAt;
    }

    public void ClearPasswordResetToken()
    {
        PasswordResetTokenHash = null;
        PasswordResetTokenExpiresAt = null;
    }

    /// <summary>
    /// Verilen token hash'i geçerli mi?
    ///
    /// Uc kosulun HEPSI saglanmali:
    ///   1. Aktif bir token var mi?
    ///   2. Süresi dolmamis mi?
    ///   3. Hash'ler esitniyor mu?
    ///
    /// Karsilastirmayi StringComparison.Ordinal ile yapıyorum.
    /// Kulture duyarli karsilastirma (varsayılan) hem yavastir hem de
    /// bazi kulturlerde beklenmedik esitlikler uretebilir. Hash'ler
    /// metin değil, BAYT dizisinin metin gosterimidir; kultur kavrami
    /// burada anlamsizdir.
    /// </summary>
    public bool IsPasswordResetTokenValid(string tokenHash, DateTimeOffset now)
    {
        if (PasswordResetTokenHash is null || PasswordResetTokenExpiresAt is null)
        {
            return false;
        }

        if (now >= PasswordResetTokenExpiresAt.Value)
        {
            return false;
        }

        return string.Equals(PasswordResetTokenHash, tokenHash, StringComparison.Ordinal);
    }

    public void UpdateProfile(string firstName, string lastName, string? phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
        {
            throw new DomainException("Ad ve soyad boş olamaz.", "user.name_required");
        }

        FirstName = firstName.Trim();
        LastName = lastName.Trim();
        PhoneNumber = string.IsNullOrWhiteSpace(phoneNumber) ? null : phoneNumber.Trim();
    }

    /// <summary>
    /// Başarısız giriş denemesini kaydeder ve gerekirse hesabi kilitler.
    /// </summary>
    /// <param name="maxAttempts">Kilitlemeden önce izin verilen deneme sayısı.</param>
    /// <param name="lockoutDuration">Kilit süresi.</param>
    public void RegisterFailedLogin(int maxAttempts, TimeSpan lockoutDuration)
    {
        FailedLoginAttempts++;

        if (FailedLoginAttempts >= maxAttempts)
        {
            LockoutEndAt = DateTimeOffset.UtcNow.Add(lockoutDuration);
        }
    }

    /// <summary>
    /// Başarılı giristen sonra cagrilir.
    /// </summary>
    public void ResetFailedLoginAttempts()
    {
        FailedLoginAttempts = 0;
        LockoutEndAt = null;
    }

    /// <summary>
    /// Hesap su an kilitli mi?
    ///
    /// Not: Bu bir METOT, property değil. Çünkü sonuç ZAMANA bağlı olarak
    /// değişiyor -- aynı nesneye iki kez sordugunda farklı cevap alabilirsin.
    /// Property'ler yan etkisiz ve kararli olmalı; zamana bağlı hesaplamalar
    /// metot olarak yazilir ki cagiran kişi bunun bir hesaplama olduğunu bilsin.
    /// </summary>
    public bool IsLockedOut() => LockoutEndAt.HasValue && LockoutEndAt.Value > DateTimeOffset.UtcNow;

    internal void AddRefreshToken(RefreshToken token)
    {
        ArgumentNullException.ThrowIfNull(token);
        _refreshTokens.Add(token);
    }
}
