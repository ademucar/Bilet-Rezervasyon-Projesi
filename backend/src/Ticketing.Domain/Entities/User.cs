using Ticketing.Domain.Common;

namespace Ticketing.Domain.Entities;

/// <summary>
/// Sistem kullanicisi. Rolleri UserRoles uzerinden tasir.
/// </summary>
public class User : AuditableEntity
{
    /// <summary>
    /// EF Core icin private parametresiz yapici.
    ///
    /// EF veritabanindan satir okurken nesneyi olusturmak zorunda ama
    /// bizim kurallarimizi (e-posta bos olamaz vb.) tekrar calistirmasina
    /// gerek yok -- o veriler zaten dogrulanmis halde kaydedilmisti.
    ///
    /// private yaptim ki bizim kodumuz yanlislikla bos bir User uretemesin.
    /// EF reflection kullandigi icin private yapiciyi gorebiliyor.
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
    /// Kullanicinin e-postasi. Her zaman kucuk harfe cevrilerek saklanir.
    ///
    /// Neden? "Ahmet@Gmail.com" ile "ahmet@gmail.com" ayni kisidir. Ham
    /// haliyle saklarsak iki ayri hesap acilabilir ve unique index bunu
    /// engellemez. Normalizasyonu TEK yerde (asagidaki Create metodunda)
    /// yapiyorum; 20 ayri yerde ToLower() yazmak yerine.
    /// </summary>
    public string Email { get; private set; }

    /// <summary>
    /// Sifrenin HASH'i. Sifrenin kendisi hicbir yerde saklanmaz.
    ///
    /// Hash'leme islemi Domain'de DEGIL, Infrastructure'da yapilacak
    /// (Sprint 3). Cunku hash algoritmasi (BCrypt, Argon2) bir altyapi
    /// tercihidir ve Domain'in framework bagimsiz kalmasi gerekiyor.
    /// Domain sadece "burada bir hash var" bilgisini tasir.
    /// </summary>
    public string PasswordHash { get; private set; }

    public string FirstName { get; private set; }

    public string LastName { get; private set; }

    public string? PhoneNumber { get; private set; }

    public bool IsEmailConfirmed { get; private set; }

    /// <summary>
    /// Hesap aktif mi? Admin bir kullaniciyi pasife alabilir.
    /// Silmek yerine pasife almak, gecmis biletlerin ve raporlarin
    /// bozulmamasini saglar.
    /// </summary>
    public bool IsActive { get; private set; }

    // ---------------------------------------------------------------
    // Brute force korumasi (PDF Sprint 15: "Brute force korumasi")
    // ---------------------------------------------------------------

    /// <summary>Ust uste basarisiz giris denemesi sayisi.</summary>
    public int FailedLoginAttempts { get; private set; }

    /// <summary>
    /// Hesabin kilitli kalacagi zamanin sonu. null ise kilitli degil.
    /// </summary>
    public DateTimeOffset? LockoutEndAt { get; private set; }

    private readonly List<UserRole> _userRoles = [];

    /// <summary>
    /// IReadOnlyCollection donuyorum, List degil.
    ///
    /// List donseydim disaridan user.UserRoles.Add(...) yazilabilirdi ve
    /// rol atama kurallarini (ornegin "Organizator rolu ancak basvuru
    /// onaylanirsa verilir") atlamak mumkun olurdu. Rol ekleme yetkisi
    /// sadece asagidaki AssignRole metodundadir.
    /// </summary>
    public IReadOnlyCollection<UserRole> UserRoles => _userRoles.AsReadOnly();

    private readonly List<RefreshToken> _refreshTokens = [];

    public IReadOnlyCollection<RefreshToken> RefreshTokens => _refreshTokens.AsReadOnly();

    // ---------------------------------------------------------------
    // Davranislar
    // ---------------------------------------------------------------

    /// <summary>
    /// Yeni kullanici olusturur.
    ///
    /// Neden yapici (constructor) yerine static factory metot?
    ///
    /// 1) Isim verebiliyorum. "User.Create(...)" ile "new User(...)" arasinda
    ///    okunabilirlik farki var; ileride "User.CreateFromGoogleLogin(...)"
    ///    gibi ikinci bir yol eklersem ikisini isimle ayirt edebilirim.
    ///    Iki farkli yapici olsaydi imzalari karisirdi.
    ///
    /// 2) Dogrulama ve normalizasyon tek kapida toplaniyor. Bu metodu
    ///    kullanmadan gecerli bir User uretmek mumkun degil.
    /// </summary>
    public static User Create(string email, string passwordHash, string firstName, string lastName)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new DomainException("E-posta bos olamaz.", "user.email_required");
        }

        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new DomainException("Sifre hash'i bos olamaz.", "user.password_required");
        }

        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
        {
            throw new DomainException("Ad ve soyad bos olamaz.", "user.name_required");
        }

        // ToLowerInvariant, ToLower degil.
        //
        // Turkce kulturde ToLower() "I" harfini "i" degil "ı" yapar
        // (noktasiz i). "AHMET@X.COM" -> "ahmet@x.com" beklerken
        // "ahmet@x.com" yerine farkli bir metin uretebilir.
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

        // Ayni rolu iki kez eklemeyi sessizce yok sayiyorum.
        // Bunu hata yapmadim cunku "kullaniciya Admin rolu ver" istegi
        // idempotent olmali: iki kez cagrilirsa sonuc ayni olmali.
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
            throw new DomainException("Sifre hash'i bos olamaz.", "user.password_required");
        }

        PasswordHash = newPasswordHash;

        // Sifre degistiginde basarisiz deneme sayacini sifirliyorum.
        // Mantik: kullanici kimligini kanitlamis oldu, cezayi kaldiralim.
        ResetFailedLoginAttempts();
    }

    public void UpdateProfile(string firstName, string lastName, string? phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
        {
            throw new DomainException("Ad ve soyad bos olamaz.", "user.name_required");
        }

        FirstName = firstName.Trim();
        LastName = lastName.Trim();
        PhoneNumber = string.IsNullOrWhiteSpace(phoneNumber) ? null : phoneNumber.Trim();
    }

    /// <summary>
    /// Basarisiz giris denemesini kaydeder ve gerekirse hesabi kilitler.
    /// </summary>
    /// <param name="maxAttempts">Kilitlemeden once izin verilen deneme sayisi.</param>
    /// <param name="lockoutDuration">Kilit suresi.</param>
    public void RegisterFailedLogin(int maxAttempts, TimeSpan lockoutDuration)
    {
        FailedLoginAttempts++;

        if (FailedLoginAttempts >= maxAttempts)
        {
            LockoutEndAt = DateTimeOffset.UtcNow.Add(lockoutDuration);
        }
    }

    /// <summary>
    /// Basarili giristen sonra cagrilir.
    /// </summary>
    public void ResetFailedLoginAttempts()
    {
        FailedLoginAttempts = 0;
        LockoutEndAt = null;
    }

    /// <summary>
    /// Hesap su an kilitli mi?
    ///
    /// Not: Bu bir METOT, property degil. Cunku sonuc ZAMANA bagli olarak
    /// degisiyor -- ayni nesneye iki kez sordugunda farkli cevap alabilirsin.
    /// Property'ler yan etkisiz ve kararli olmali; zamana bagli hesaplamalar
    /// metot olarak yazilir ki cagiran kisi bunun bir hesaplama oldugunu bilsin.
    /// </summary>
    public bool IsLockedOut() => LockoutEndAt.HasValue && LockoutEndAt.Value > DateTimeOffset.UtcNow;

    internal void AddRefreshToken(RefreshToken token)
    {
        ArgumentNullException.ThrowIfNull(token);
        _refreshTokens.Add(token);
    }
}
