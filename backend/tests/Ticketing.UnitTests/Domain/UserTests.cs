using FluentAssertions;
using Ticketing.Domain.Common;
using Ticketing.Domain.Entities;

namespace Ticketing.UnitTests.Domain;

public class UserTests
{
    private static User GecerliKullanici()
        => User.Create("ahmet@ornek.com", "hash", "Ahmet", "Yilmaz");

    // Olusturma ve normalizasyon

    [Fact]
    public void Create_BuyukHarfliEposta_KucukHarfeCevrilmeli()
    {
        var user = User.Create("Ahmet@Ornek.COM", "hash", "Ahmet", "Yilmaz");

        // Normalizasyon olmasaydi "Ahmet@Ornek.COM" ve "ahmet@ornek.com"
        // iki AYRI hesap olurdu ve unique index bunu engellemezdi.
        user.Email.Should().Be("ahmet@ornek.com");
    }

    [Fact]
    public void Create_TurkceIHarfiIceren_TurkishIProblemineDusmemeli()
    {
        // "Turkish I problem": Turkce kulturde ToLower() 'I' harfini
        // 'i' degil 'ı' (noktasiz) yapar. ToLowerInvariant kullandigim
        // icin bu tuzaga dusmuyoruz.
        var user = User.Create("ILKER@ORNEK.COM", "hash", "Ilker", "Demir");

        user.Email.Should().Be("ilker@ornek.com");
        user.Email.Should().NotContain("ı");
    }

    [Fact]
    public void Create_BaslangictaAktifOlmali()
    {
        GecerliKullanici().IsActive.Should().BeTrue();
    }

    [Fact]
    public void Create_BaslangictaEpostaOnaysizOlmali()
    {
        GecerliKullanici().IsEmailConfirmed.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_BosEposta_DomainExceptionFirlatmali(string email)
    {
        var eylem = () => User.Create(email, "hash", "Ahmet", "Yilmaz");

        eylem.Should().Throw<DomainException>()
             .Which.ErrorCode.Should().Be("user.email_required");
    }

    [Fact]
    public void Create_BosSoyad_DomainExceptionFirlatmali()
    {
        var eylem = () => User.Create("a@b.com", "hash", "Ahmet", "  ");

        eylem.Should().Throw<DomainException>()
             .Which.ErrorCode.Should().Be("user.name_required");
    }

    // Rol atama

    [Fact]
    public void AssignRole_AyniRolIkiKez_TekKayitOlusturmali()
    {
        // Idempotency: "bu kullaniciya Admin rolu ver" istegi iki kez
        // gelirse sonuc ayni olmali. Hata firlatmak yerine yok sayiyorum.
        var user = GecerliKullanici();
        var role = Role.Create(Role.Ids.Admin, Role.Names.Admin);

        user.AssignRole(role);
        user.AssignRole(role);

        user.UserRoles.Should().HaveCount(1);
    }

    [Fact]
    public void UserRoles_DisaridanDegistirilememeli()
    {
        // Bu test kapsullemeyi koruyor: rol ekleme yetkisi yalnizca
        // User.AssignRole metodunda olmali.
        //
        // Onemli ayrinti: AsReadOnly() bana ReadOnlyCollection<T> dondurur.
        // Bu tip ICollection<T>'yi ACIKCA (explicitly) implemente eder --
        // yani arayuz uzerinden Add cagrilabilir ama calisma zamaninda
        // NotSupportedException firlatir.
        //
        // Bu yuzden "ICollection degildir" diye test etmek YANLIS olurdu
        // (ilk yazdigimda oyle yapip testi kirdim). Dogru test, ekleme
        // girisiminin gercekten engellendigini dogrulamaktir.
        var user = GecerliKullanici();
        var role = Role.Create(Role.Ids.Admin, Role.Names.Admin);
        user.AssignRole(role);

        // Not: UserRole'un yapicisi internal oldugu icin test projesinden
        // yeni bir UserRole uretemiyorum -- bu da kasitli bir tasarim.
        // Bu yuzden silme uzerinden dogruluyorum; ayni korumayi kanitliyor.
        var eylem = () => ((ICollection<UserRole>)user.UserRoles).Clear();

        eylem.Should().Throw<NotSupportedException>(
            "koleksiyon disaridan degistirilememeli");

        user.UserRoles.Should().HaveCount(1);
    }

    // Brute force korumasi (PDF Sprint 15)

    [Fact]
    public void RegisterFailedLogin_LimitAltinda_HesapKilitlenmemeli()
    {
        var user = GecerliKullanici();

        user.RegisterFailedLogin(maxAttempts: 5, lockoutDuration: TimeSpan.FromMinutes(15));
        user.RegisterFailedLogin(maxAttempts: 5, lockoutDuration: TimeSpan.FromMinutes(15));

        user.FailedLoginAttempts.Should().Be(2);
        user.IsLockedOut().Should().BeFalse();
    }

    [Fact]
    public void RegisterFailedLogin_LimiteUlasinca_HesapKilitlenmeli()
    {
        var user = GecerliKullanici();

        for (var i = 0; i < 5; i++)
        {
            user.RegisterFailedLogin(maxAttempts: 5, lockoutDuration: TimeSpan.FromMinutes(15));
        }

        user.IsLockedOut().Should().BeTrue();
        user.LockoutEndAt.Should().NotBeNull();
    }

    [Fact]
    public void ResetFailedLoginAttempts_KilidiVeSayaciTemizlemeli()
    {
        var user = GecerliKullanici();
        for (var i = 0; i < 5; i++)
        {
            user.RegisterFailedLogin(5, TimeSpan.FromMinutes(15));
        }

        user.ResetFailedLoginAttempts();

        user.FailedLoginAttempts.Should().Be(0);
        user.LockoutEndAt.Should().BeNull();
        user.IsLockedOut().Should().BeFalse();
    }

    [Fact]
    public void ChangePasswordHash_BasarisizDenemeSayaciniSifirlamali()
    {
        // Mantik: kullanici sifresini degistirebildiyse kimligini kanitladi,
        // cezayi kaldirayim.
        var user = GecerliKullanici();
        user.RegisterFailedLogin(5, TimeSpan.FromMinutes(15));

        user.ChangePasswordHash("yeniHash");

        user.FailedLoginAttempts.Should().Be(0);
        user.PasswordHash.Should().Be("yeniHash");
    }

    [Fact]
    public void IsLockedOut_KilitSuresiGectiyse_FalseDonmeli()
    {
        // IsLockedOut'un metot olmasinin sebebi bu: sonuc zamana baglidir.
        // Ayni nesneye 15 dakika sonra sordugunda farkli cevap alirsin.
        // Property olsaydi cagiran kisi bunun kararli bir deger oldugunu sanirdi.
        var user = GecerliKullanici();
        user.RegisterFailedLogin(maxAttempts: 1, lockoutDuration: TimeSpan.FromMilliseconds(1));

        Thread.Sleep(20);

        user.IsLockedOut().Should().BeFalse();
    }
}
