using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Ticketing.Domain.Entities;
using Ticketing.Persistence;

namespace Ticketing.IntegrationTests;

/// <summary>
/// Entegrasyon testlerinin ortak temeli. PDF Sprint 17.
/// </summary>
/// <remarks>
/// Her test temiz bir veritabaniyla basliyor
///
/// InitializeAsync her testten ONCE calisiyor ve tablolari
/// bosaltiyor.
///
/// Neden sart? Testler paylasilan bir veritabanini kullaniyor.
/// Temizlemeseydim bir testin olusturdugu kullanici, digerinin
/// "kullanici sayisi 1 olmali" beklentisini bozardi -- ve bu
/// yalnizca testler belirli bir sirayla calistiginda olurdu.
///
/// Boyle bir hatayi ayiklamak saatler alir: test tek basina geciyor,
/// paket halinde kiriliyor, sebep gorunmuyor.
/// </remarks>
[Collection(TicketingTestSuite.Name)]
public abstract class IntegrationTestBase : IAsyncLifetime
{
    protected TicketingTestFactory Factory { get; }

    protected HttpClient Client { get; private set; } = null!;

    protected IntegrationTestBase(TicketingTestFactory factory)
    {
        Factory = factory;
    }

    public async Task InitializeAsync()
    {
        await Factory.ResetDatabaseAsync().ConfigureAwait(false);

        // Her test icin yeni HttpClient
        //
        // Ayni istemciyi paylassaydik, bir testte eklenen
        // Authorization başlığı sonraki teste sizardi ve
        // "yetkisiz erisim" testi yanlislikla GECERDI.
        //
        // Yani en kritik guvenlik testim, bir yan etki yuzunden
        // hicbir sey dogrulamayan bir teste donusurdu.
        Client = Factory.CreateClient();

        // Referans verisi (roller, kategoriler) her testte gerekli:
        // Respawn onlari da siliyor.
        await SeedReferansVerisiAsync().ConfigureAwait(false);
    }

    public Task DisposeAsync()
    {
        Client?.Dispose();

        return Task.CompletedTask;
    }

    // Yardimcilar

    /// <summary>Roller olmadan kayit calismaz; her testten once ekliyorum.</summary>
    private async Task SeedReferansVerisiAsync()
    {
        using var db = Factory.CreateDbContext();

        if (await db.Roles.AnyAsync().ConfigureAwait(false))
        {
            return;
        }

        // Rol ID'leri SABIT (Role.Ids). Rastgele uretseydik uygulama
        // kodu bu ID'lerle rol ararken bulamazdi.
        db.Roles.AddRange(
            Role.Create(Role.Ids.User, Role.Names.User, "Musteri"),
            Role.Create(Role.Ids.Organizer, Role.Names.Organizer, "Organizator"),
            Role.Create(Role.Ids.Admin, Role.Names.Admin, "Yonetici"));

        await db.SaveChangesAsync().ConfigureAwait(false);
    }

    /// <summary>Kullanici kaydeder ve access token doner.</summary>
    protected async Task<string> KayitOlVeGirisYapAsync(
        string email,
        string sifre = "Test1234!")
    {
        var kayit = await Client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email,
            password = sifre,
            firstName = "Test",
            lastName = "Kullanici",
        }).ConfigureAwait(false);

        kayit.EnsureSuccessStatusCode();

        return await TokenAlAsync(kayit).ConfigureAwait(false);
    }

    /// <summary>Var olan kullaniciyla giris yapar.</summary>
    protected async Task<string> GirisYapAsync(string email, string sifre = "Test1234!")
    {
        var yanit = await Client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email,
            password = sifre,
        }).ConfigureAwait(false);

        yanit.EnsureSuccessStatusCode();

        return await TokenAlAsync(yanit).ConfigureAwait(false);
    }

    private static async Task<string> TokenAlAsync(HttpResponseMessage yanit)
    {
        using var belge = JsonDocument.Parse(
            await yanit.Content.ReadAsStringAsync().ConfigureAwait(false));

        return belge.RootElement.GetProperty("accessToken").GetString()!;
    }

    /// <summary>Istemciye token yerlestirir.</summary>
    protected void TokenKullan(string? token)
        => Client.DefaultRequestHeaders.Authorization = token is null
            ? null
            : new AuthenticationHeaderValue("Bearer", token);

    /// <summary>
    /// Kullaniciya rol verir ve YENI token uretir.
    /// </summary>
    /// <remarks>
    /// Rol ekledikten sonra neden tekrar giris?
    ///
    /// Roller JWT'nin ICINE yaziliyor (Sprint 3). Var olan token,
    /// rol eklenmeden once uretildigi icin yeni rolu ICERMIYOR.
    ///
    /// Bunu ilk yazdigimda unutmustum ve "organizator etkinlik
    /// olusturabilmeli" testi 403 aliyordu. Kodda hata yoktu --
    /// testte vardi.
    ///
    /// Uretimde de ayni kural gecerli: rol degisikligi ancak
    /// kullanici yeniden giris yapinca (veya token yenilenince)
    /// etkili oluyor. Bu, JWT'nin durumsuz olmasinin dogal bedeli.
    /// </remarks>
    protected async Task<string> RolVerVeYenidenGirisAsync(string email, string rolAdi)
    {
        using (var db = Factory.CreateDbContext())
        {
            var normalize = email.ToLowerInvariant();

            // Include SART: AssignRole, mevcut rolleri kontrol ediyor.
            // Yuklemeseydik koleksiyon bos gorunur ve ayni rol ikinci
            // kez eklenmeye calisilirdi.
            var kullanici = await db.Users
                .Include(u => u.UserRoles)
                .FirstAsync(u => u.Email == normalize)
                .ConfigureAwait(false);

            var rol = await db.Roles.FirstAsync(r => r.Name == rolAdi).ConfigureAwait(false);

            // Rol, domain metoduyla ataniyor -- tabloya degil
            //
            // db.UserRoles.Add(new UserRole(...)) yazmayi denedim:
            // DERLENMEDI, cunku UserRole'un kurucusu internal.
            //
            // Bu bir engel degil, tasarimin CALISTIGININ kaniti:
            // Sprint 2'de ara tabloyu kasten kapsullemistik ki
            // kimse rol atamasini kurallari atlayarak yapmasin.
            // Test kodu bile bu kurala uymak zorunda.
            //
            // AssignRole ayrica "ayni rol iki kez atanmasin"
            // kontrolunu de yapiyor -- tabloya dogrudan yazsaydik
            // o kontrolu atlamis olurdum ve test, uretimde
            // olmayan bir durumu dogrulardi.
            kullanici.AssignRole(rol);

            await db.SaveChangesAsync().ConfigureAwait(false);
        }

        return await GirisYapAsync(email).ConfigureAwait(false);
    }

    /// <summary>Veritabanina dogrudan bakmak icin.</summary>
    protected TicketingDbContext Db() => Factory.CreateDbContext();
}
