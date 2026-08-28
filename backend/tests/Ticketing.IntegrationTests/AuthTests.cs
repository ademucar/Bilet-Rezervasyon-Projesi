using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Ticketing.IntegrationTests;

/// <summary>
/// PDF Sprint 17 entegrasyon senaryolari:
/// "Register ve Login", "Yetkisiz erisim".
/// </summary>
public sealed class AuthTests : IntegrationTestBase
{
    public AuthTests(TicketingTestFactory factory) : base(factory)
    {
    }

    // ==============================================================
    // PDF: "Register ve Login"
    // ==============================================================

    [Fact]
    public async Task Kayit_sonrasi_giris_yapilabilmeli()
    {
        const string Eposta = "yeni@ornek.com";

        var kayit = await Client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email = Eposta,
            password = "Test1234!",
            firstName = "Adem",
            lastName = "Test",
        });

        kayit.StatusCode.Should().Be(HttpStatusCode.OK);

        // ==========================================================
        // VERITABANINA DA BAKIYORUZ, YALNIZCA YANITA DEGIL
        // ==========================================================
        // Yanit 200 dondugu halde kaydin yazilmamis olmasi
        // mumkun (ornegin SaveChanges unutulmus olsaydi).
        //
        // Sprint 16'da tam olarak bu turden bir hata bulmustum:
        // her sey dogru gorunuyordu ama sutun bostu. O gunden beri
        // "yanit basarili" ile "veri yazildi" ayri iki sey olarak
        // dogruluyorum.
        // ==========================================================
        using (var db = Db())
        {
            var kullanici = await db.Users
                .Include(u => u.UserRoles)
                .SingleAsync(u => u.Email == Eposta);

            // E-posta KUCUK HARFE cevrilerek saklanmali.
            kullanici.Email.Should().Be(Eposta);

            // Sifre ACIK METIN olarak saklanmamali.
            kullanici.PasswordHash.Should().NotBe("Test1234!");
            kullanici.PasswordHash.Should().StartWith("$2");

            // Kayit olan herkes varsayilan rolu almali; almazsa
            // hicbir yetkilendirme calismaz.
            kullanici.UserRoles.Should().HaveCount(1);
        }

        var giris = await Client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = Eposta,
            password = "Test1234!",
        });

        giris.StatusCode.Should().Be(HttpStatusCode.OK);

        using var belge = JsonDocument.Parse(await giris.Content.ReadAsStringAsync());

        belge.RootElement.GetProperty("accessToken").GetString().Should().NotBeNullOrEmpty();
        belge.RootElement.GetProperty("refreshToken").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Ayni_eposta_ikinci_kez_kaydedilememeli()
    {
        await KayitOlVeGirisYapAsync("tekrar@ornek.com");

        var ikinci = await Client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email = "tekrar@ornek.com",
            password = "Baska1234!",
            firstName = "Baska",
            lastName = "Kisi",
        });

        ikinci.IsSuccessStatusCode.Should().BeFalse();
    }

    [Fact]
    public async Task Yanlis_sifreyle_giris_yapilamamali()
    {
        await KayitOlVeGirisYapAsync("sifre@ornek.com");

        var yanit = await Client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = "sifre@ornek.com",
            password = "YanlisSifre1!",
        });

        yanit.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <remarks>
    /// ==============================================================
    /// HESAP SAYIMI (user enumeration) KORUMASI
    /// ==============================================================
    /// Var olmayan kullanici ile yanlis sifre AYNI yaniti vermeli.
    ///
    /// Farkli verseydi saldirgan hangi e-postalarin kayitli oldugunu
    /// tek tek olcebilirdi -- ve bu liste, hedefli bir saldirinin
    /// ilk adimi olurdu.
    ///
    /// Sprint 16'da bu ayrimi YALNIZCA loga koymustuk; bu test,
    /// ayrimin yaniti etkilemedigini dogruluyor.
    /// </remarks>
    [Fact]
    public async Task Olmayan_kullanici_ve_yanlis_sifre_ayni_yaniti_vermeli()
    {
        await KayitOlVeGirisYapAsync("varolan@ornek.com");

        var yanlisSifre = await Client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = "varolan@ornek.com",
            password = "Yanlis1234!",
        });

        var olmayanKullanici = await Client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = "hicyok@ornek.com",
            password = "Yanlis1234!",
        });

        yanlisSifre.StatusCode.Should().Be(olmayanKullanici.StatusCode);

        var a = await yanlisSifre.Content.ReadAsStringAsync();
        var b = await olmayanKullanici.Content.ReadAsStringAsync();

        // "instance" alani istek yolunu iceriyor ve ikisinde de ayni;
        // govdelerin tamamini karsilastirmak guvenli.
        a.Should().Be(b);
    }

    // ==============================================================
    // PDF: "Refresh Token"
    // ==============================================================

    [Fact]
    public async Task Refresh_token_ile_yeni_access_token_alinabilmeli()
    {
        var kayit = await Client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email = "yenile@ornek.com",
            password = "Test1234!",
            firstName = "Yenile",
            lastName = "Test",
        });

        using var ilk = JsonDocument.Parse(await kayit.Content.ReadAsStringAsync());
        var refreshToken = ilk.RootElement.GetProperty("refreshToken").GetString();

        var yanit = await Client.PostAsJsonAsync("/api/v1/auth/refresh-token", new
        {
            refreshToken,
        });

        yanit.StatusCode.Should().Be(HttpStatusCode.OK);

        using var yeni = JsonDocument.Parse(await yanit.Content.ReadAsStringAsync());

        yeni.RootElement.GetProperty("accessToken").GetString()
            .Should().NotBeNullOrEmpty();

        // ==========================================================
        // DONME (rotation): eski token artik gecersiz olmali
        // ==========================================================
        // Yenilemede yeni bir refresh token uretiliyor ve eskisi
        // iptal ediliyor.
        //
        // Olmasaydi calinan bir refresh token SONSUZA KADAR
        // kullanilabilirdi -- sifre degistirmek bile onu
        // durdurmazdi.
        // ==========================================================
        var tekrar = await Client.PostAsJsonAsync("/api/v1/auth/refresh-token", new
        {
            refreshToken,
        });

        tekrar.IsSuccessStatusCode.Should().BeFalse(
            "kullanilmis refresh token ikinci kez kabul edilmemeli");
    }

    // ==============================================================
    // PDF: "Yetkisiz erisim"
    // ==============================================================

    [Fact]
    public async Task Tokensiz_istek_401_donmeli()
    {
        var yanit = await Client.GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative));

        yanit.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Gecersiz_token_401_donmeli()
    {
        TokenKullan("bu.gecerli.bir.token.degil");

        var yanit = await Client.GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative));

        yanit.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <remarks>
    /// 401 ile 403 farki onemli:
    ///   401 = "kim oldugunu bilmiyorum"
    ///   403 = "kim oldugunu biliyorum ama yetkin yok"
    ///
    /// Istemci ikisine farkli tepki vermeli: 401'de giris sayfasina
    /// yonlendirmeli, 403'te "yetkiniz yok" gostermeli. Ayni
    /// yapsaydik, yetkisiz bir kullanici surekli giris sayfasina
    /// atilir ve zaten girisli oldugu icin donup dolasip ayni yere
    /// gelirdi.
    /// </remarks>
    [Fact]
    public async Task Yetkisiz_rol_403_donmeli()
    {
        // Normal kullanici (yalnizca User rolu).
        var token = await KayitOlVeGirisYapAsync("normal@ornek.com");
        TokenKullan(token);

        // Organizator ucu.
        var yanit = await Client.PostAsJsonAsync("/api/v1/events", new
        {
            title = "Izinsiz Etkinlik",
            description = "Bu olusturulamamali",
        });

        yanit.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
