using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Ticketing.Domain.Entities;
using Ticketing.Domain.Enums;

namespace Ticketing.IntegrationTests;

/// <summary>
/// PDF Sprint 17 entegrasyon senaryolari:
/// "Etkinlik olusturma", "Etkinlik yayinlama".
/// </summary>
public sealed class EventLifecycleTests : IntegrationTestBase
{
    public EventLifecycleTests(TicketingTestFactory factory) : base(factory)
    {
    }

    /// <summary>Organizator kullanici + referans veriler kurar.</summary>
    private async Task<(string Token, Guid CategoryId, Guid CityId, Guid VenueId,
        Guid HallId, Guid ProfileId, Guid LayoutId)> OrganizatorHazirlaAsync()
    {
        const string Eposta = "org@ornek.com";

        await KayitOlVeGirisYapAsync(Eposta);
        var token = await RolVerVeYenidenGirisAsync(Eposta, Role.Names.Organizer);

        using var db = Db();

        var kullaniciId = await db.Users
            .Where(u => u.Email == Eposta)
            .Select(u => u.Id)
            .FirstAsync();

        var kategori = EventCategory.Create("Tiyatro", "tiyatro");
        var sehir = City.Create("Ankara", 6);
        db.EventCategories.Add(kategori);
        db.Cities.Add(sehir);
        await db.SaveChangesAsync();

        var mekan = Venue.Create(sehir.Id, "Sahne", "Adres");
        db.Venues.Add(mekan);
        await db.SaveChangesAsync();

        var salon = Hall.Create(mekan.Id, "Salon 1", 50);
        db.Halls.Add(salon);

        // Etkinlik olusturmak icin organizator PROFILI sart:
        // etkinligin sahibi kullanici degil, profil (Sprint 4).
        var profil = OrganizerProfile.Create(kullaniciId, "Test Org", "org@test.local");
        db.OrganizerProfiles.Add(profil);

        await db.SaveChangesAsync();

        var plan = SeatLayout.Create(salon.Id, "Duzen");
        plan.AddSection("Blok", 1).GenerateSeats(1, 10, ["A"]);
        db.SeatLayouts.Add(plan);

        await db.SaveChangesAsync();

        return (token, kategori.Id, sehir.Id, mekan.Id, salon.Id, profil.Id, plan.Id);
    }

    private static object EtkinlikGovdesi(
        Guid kategoriId, Guid sehirId, Guid mekanId, Guid salonId, string baslik = "Yeni Oyun")
    {
        var tarih = DateTimeOffset.UtcNow.AddDays(45);

        return new
        {
            title = baslik,
            description = "Entegrasyon testi icin olusturuldu",
            categoryId = kategoriId,
            cityId = sehirId,
            venueId = mekanId,
            hallId = salonId,
            eventDate = tarih,
            salesStartDate = DateTimeOffset.UtcNow.AddDays(-1),
            salesEndDate = tarih.AddHours(-1),
            durationMinutes = 90,
            maxTicketsPerUser = 4,
            minimumAge = 0,
        };
    }

    // ==============================================================
    // PDF: "Etkinlik olusturma"
    // ==============================================================

    [Fact]
    public async Task Organizator_etkinlik_olusturabilmeli()
    {
        var (token, kategori, sehir, mekan, salon, _, _) = await OrganizatorHazirlaAsync();
        TokenKullan(token);

        var yanit = await Client.PostAsJsonAsync(
            "/api/v1/events", EtkinlikGovdesi(kategori, sehir, mekan, salon));

        yanit.StatusCode.Should().Be(HttpStatusCode.Created);

        using var belge = JsonDocument.Parse(await yanit.Content.ReadAsStringAsync());
        // ==========================================================
        // YANIT GOVDESI NESNE DEGIL, DOGRUDAN GUID
        // ==========================================================
        // Once GetProperty("id") yazdim ve su hatayi aldim:
        //   "requires an element of type 'Object', but the target
        //    element has type 'String'"
        //
        // Sebep: etkinlik olusturma ucu Result<Guid> donuyor, yani
        // govde su sekilde:
        //     "01a04a1b-...."          ({ "id": "..." } DEGIL)
        //
        // Bu bir hata degil, bilincli bir tasarim: olusturma ucu
        // yeni kaynagin KIMLIGINI donuyor ve adresi Location
        // header'inda veriyor.
        // ==========================================================
        var etkinlikId = belge.RootElement.GetGuid();

        using var db = Db();

        var etkinlik = await db.Events.SingleAsync(e => e.Id == etkinlikId);

        // ==========================================================
        // YENI ETKINLIK TASLAK OLARAK BASLAMALI
        // ==========================================================
        // Dogrudan yayinda baslasaydi, yarim hazirlanmis bir etkinlik
        // (oturumu yok, bilet turu yok, koltugu yok) ana sayfada
        // gorunurdu ve kullanicilar bilet almaya calisirdi.
        //
        // Taslak durumu, organizatore hazirligi bitirme firsati
        // veriyor.
        // ==========================================================
        etkinlik.Status.Should().Be(EventStatus.Draft);

        // Denetim alanlari dolmali (Sprint 12'de bulunan hatanin
        // nobetcisi: o zaman CreatedAt "-infinity" kaliyordu).
        etkinlik.CreatedAt.Should().BeAfter(DateTimeOffset.UtcNow.AddMinutes(-5));
        etkinlik.CreatedBy.Should().NotBeNull();
    }

    /// <remarks>
    /// PDF: "Yetkisiz erisim" senaryosunun etkinlik ayagi.
    /// Normal kullanici etkinlik olusturamamali.
    /// </remarks>
    [Fact]
    public async Task Normal_kullanici_etkinlik_olusturamamali()
    {
        var (_, kategori, sehir, mekan, salon, _, _) = await OrganizatorHazirlaAsync();

        var musteri = await KayitOlVeGirisYapAsync("musteri2@ornek.com");
        TokenKullan(musteri);

        var yanit = await Client.PostAsJsonAsync(
            "/api/v1/events", EtkinlikGovdesi(kategori, sehir, mekan, salon));

        yanit.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var db = Db();

        (await db.Events.CountAsync()).Should().Be(0);
    }

    // ==============================================================
    // PDF: "Etkinlik yayinlama"
    // ==============================================================

    /// <remarks>
    /// ==============================================================
    /// YAYINLAMA, DURUM MAKINESINDEN GECMEK ZORUNDA
    /// ==============================================================
    /// Draft -> PendingApproval -> Published
    ///
    /// Taslaktan dogrudan yayina gecmeyi de deniyoruz (asagidaki
    /// test) ve reddedilmesini bekliyoruz. Bu, Sprint 2'de kurulan
    /// durum makinesinin gercekten korudugunu dogruluyor.
    /// ==============================================================
    /// </remarks>
    [Fact]
    public async Task Onaya_gonderilen_etkinlik_yayinlanabilmeli()
    {
        var (token, kategori, sehir, mekan, salon, profil, plan) =
            await OrganizatorHazirlaAsync();

        // ==========================================================
        // ETKINLIK OTURUM VE BILET TURU ILE KURULUYOR
        // ==========================================================
        // Ilk denememde bos bir taslak olusturup dogrudan onaya
        // gonderdim ve reddedildim.
        //
        // Sebep Event.SubmitForApproval() icindeki iki kural:
        // en az bir OTURUM ve en az bir BILET TURU olmali.
        //
        // Mantikli: oturumsuz bir etkinlik satilamaz ve admin'in
        // onune bos bir kayit gitmesinin anlami yok. Kural, onay
        // sirasini gereksiz isten koruyor.
        //
        // (Bu kuralin kendisi asagidaki ayri testte dogrulaniyor.)
        // ==========================================================
        Guid etkinlikId;

        using (var db = Db())
        {
            etkinlikId = await SenaryoKurucu.TaslakEtkinlikAsync(
                db, profil, kategori, sehir, mekan, salon, plan);
        }

        TokenKullan(token);

        var onaya = await Client.PostAsync(
            new Uri($"/api/v1/events/{etkinlikId}/submit", UriKind.Relative),
            content: null);

        onaya.IsSuccessStatusCode.Should().BeTrue();

        using (var db = Db())
        {
            var bekleyen = await db.Events.SingleAsync(e => e.Id == etkinlikId);
            bekleyen.Status.Should().Be(EventStatus.PendingApproval);
        }

        // ==========================================================
        // YAYINLAMA YALNIZCA ADMIN
        // ==========================================================
        // Organizator kendi etkinligini onaylayabilseydi onay sureci
        // tamamen anlamsiz olurdu. Once organizator token'iyla
        // deneyip 403 bekliyoruz.
        // ==========================================================
        var organizatorYayin = await Client.PostAsync(
            new Uri($"/api/v1/events/{etkinlikId}/publish", UriKind.Relative), content: null);

        organizatorYayin.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "organizator kendi etkinligini yayinlayamamali");

        await KayitOlVeGirisYapAsync("yayin-admin@ornek.com");
        var adminToken = await RolVerVeYenidenGirisAsync(
            "yayin-admin@ornek.com", Role.Names.Admin);

        TokenKullan(adminToken);

        var yayinla = await Client.PostAsync(
            new Uri($"/api/v1/events/{etkinlikId}/publish", UriKind.Relative), content: null);

        yayinla.IsSuccessStatusCode.Should().BeTrue();

        using var son = Db();

        var etkinlik = await son.Events.SingleAsync(e => e.Id == etkinlikId);
        etkinlik.Status.Should().Be(EventStatus.Published);
    }

    /// <remarks>
    /// Onaya gonderme, EKSIK bir etkinligi reddetmeli.
    ///
    /// Bu kurali yukaridaki testi yazarken kesfettim: bos bir taslagi
    /// onaya gondermeye calistim ve reddedildim. Kuralin kendisini
    /// ayri bir testle koruyorum ki ilerde biri "kolaylik olsun" diye
    /// kaldirmak isterse test kirilsin.
    /// </remarks>
    [Fact]
    public async Task Oturumsuz_etkinlik_onaya_gonderilememeli()
    {
        var (token, kategori, sehir, mekan, salon, _, _) = await OrganizatorHazirlaAsync();
        TokenKullan(token);

        var olustur = await Client.PostAsJsonAsync(
            "/api/v1/events", EtkinlikGovdesi(kategori, sehir, mekan, salon));

        using var belge = JsonDocument.Parse(await olustur.Content.ReadAsStringAsync());
        var etkinlikId = belge.RootElement.GetGuid();

        var onaya = await Client.PostAsync(
            new Uri($"/api/v1/events/{etkinlikId}/submit", UriKind.Relative), content: null);

        onaya.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Taslak_etkinlik_dogrudan_yayinlanamamali()
    {
        var (token, kategori, sehir, mekan, salon, _, _) = await OrganizatorHazirlaAsync();
        TokenKullan(token);

        var olustur = await Client.PostAsJsonAsync(
            "/api/v1/events", EtkinlikGovdesi(kategori, sehir, mekan, salon));

        using var belge = JsonDocument.Parse(await olustur.Content.ReadAsStringAsync());
        var etkinlikId = belge.RootElement.GetGuid();

        await KayitOlVeGirisYapAsync("admin3@ornek.com");
        var adminToken = await RolVerVeYenidenGirisAsync("admin3@ornek.com", Role.Names.Admin);
        TokenKullan(adminToken);

        // Onaydan GECMEDEN yayinlamayi dene.
        var yayinla = await Client.PostAsync(
            new Uri($"/api/v1/events/{etkinlikId}/publish", UriKind.Relative), content: null);

        // 422: is kurali ihlali. Durum makinesi bu gecise izin vermiyor.
        yayinla.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        using var db = Db();

        var etkinlik = await db.Events.SingleAsync(e => e.Id == etkinlikId);

        etkinlik.Status.Should().Be(EventStatus.Draft,
            "reddedilen gecis etkinligin durumunu degistirmemeli");
    }

    /// <remarks>
    /// PDF Sprint 11: yayinlanmamis etkinlik listede GORUNMEMELI.
    ///
    /// Bu bir yetkilendirme testi kadar onemli: taslak etkinliklerin
    /// başlık ve fiyatlari, organizatorun henuz duyurmak istemedigi
    /// bilgiler.
    /// </remarks>
    [Fact]
    public async Task Taslak_etkinlik_herkese_acik_listede_gorunmemeli()
    {
        var (token, kategori, sehir, mekan, salon, _, _) = await OrganizatorHazirlaAsync();
        TokenKullan(token);

        await Client.PostAsJsonAsync(
            "/api/v1/events",
            EtkinlikGovdesi(kategori, sehir, mekan, salon, "Gizli Taslak"));

        // Anonim istek.
        TokenKullan(null);

        var liste = await Client.GetAsync(new Uri("/api/v1/events", UriKind.Relative));

        liste.StatusCode.Should().Be(HttpStatusCode.OK);

        var govde = await liste.Content.ReadAsStringAsync();

        govde.Should().NotContain("Gizli Taslak");
    }
}
