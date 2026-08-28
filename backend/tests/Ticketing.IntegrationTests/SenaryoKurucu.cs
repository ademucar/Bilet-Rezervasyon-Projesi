using Microsoft.EntityFrameworkCore;
using Ticketing.Domain.Entities;
using Ticketing.Domain.ValueObjects;
using Ticketing.Persistence;

namespace Ticketing.IntegrationTests;

/// <summary>
/// Entegrasyon testleri icin satisa acik bir etkinlik kurar.
/// </summary>
/// <remarks>
/// ==================================================================
/// NEDEN HTTP DEGIL, DOGRUDAN VERITABANI?
/// ==================================================================
/// Satisa acik bir etkinlik icin su zincir gerekiyor:
///
///   Kategori -> Sehir -> Mekan -> Salon -> Koltuk plani -> Bolum
///   -> Koltuklar -> Organizator profili -> Etkinlik -> Bilet turu
///   -> Oturum -> Koltuk uretimi -> Onaya gonder -> Yayinla
///   -> Satisi ac
///
/// Bunu HTTP uzerinden kursaydik her rezervasyon testi ~15 istek
/// atardi ve test suresi katlanirdi. Daha kotusu: koltuk yarisi
/// testi bir yerde kirildiginda, HANGI adimin bozuldugunu anlamak
/// icin 15 istegi tek tek incelemek gerekirdi.
///
/// Kurulum dogrudan domain metotlariyla yapiliyor; TEST EDILEN
/// davranis (rezervasyon, odeme, iade) ise HER ZAMAN HTTP uzerinden.
/// Yani kisayol yalnizca hazirlikta, olculen seyde degil.
///
/// Kurulumda da domain metotlari kullaniliyor (SQL degil): boylece
/// kurdugumuz veri, uretimde olusabilecek bir veriyle ayni
/// kurallardan geciyor.
/// ==================================================================
/// </remarks>
internal static class SenaryoKurucu
{
    /// <summary>Kurulan senaryonun testlere lazim olan kimlikleri.</summary>
    internal sealed record Senaryo(
        Guid EventId,
        Guid SessionId,
        Guid TicketTypeId,
        IReadOnlyList<Guid> SeatIds,
        decimal KoltukFiyati);

    /// <summary>
    /// Satisa acik, koltuklari uretilmis bir etkinlik olusturur.
    /// </summary>
    public static async Task<Senaryo> SatisaAcikEtkinlikAsync(
        TicketingDbContext db,
        Guid organizatorKullaniciId,
        int koltukSayisi = 4,
        decimal fiyat = 100m)
    {
        ArgumentNullException.ThrowIfNull(db);

        var simdi = DateTimeOffset.UtcNow;

        // ---- Referans veriler ----
        var kategori = EventCategory.Create("Konser", "konser");
        var sehir = City.Create("Istanbul", 34);
        db.EventCategories.Add(kategori);
        db.Cities.Add(sehir);
        await db.SaveChangesAsync().ConfigureAwait(false);

        var mekan = Venue.Create(sehir.Id, "Test Arena", "Test Adres 1");
        db.Venues.Add(mekan);
        await db.SaveChangesAsync().ConfigureAwait(false);

        var salon = Hall.Create(mekan.Id, "Buyuk Salon", koltukSayisi);
        db.Halls.Add(salon);
        await db.SaveChangesAsync().ConfigureAwait(false);

        // ---- Fiziksel koltuk plani ----
        var plan = SeatLayout.Create(salon.Id, "Test Duzeni");
        var bolum = plan.AddSection("Orta Blok", 1);
        bolum.GenerateSeats(rowCount: 1, seatsPerRow: koltukSayisi, rowLabels: ["A"]);

        db.SeatLayouts.Add(plan);
        await db.SaveChangesAsync().ConfigureAwait(false);

        // ---- Organizator profili ----
        //
        // Etkinligin sahibi kullanici DEGIL, organizator profilidir
        // (Sprint 4 karari). Profil olmadan etkinlik olusturulamaz.
        var profil = OrganizerProfile.Create(
            organizatorKullaniciId, "Test Organizasyon", "test@organizator.local");

        db.OrganizerProfiles.Add(profil);
        await db.SaveChangesAsync().ConfigureAwait(false);

        // ---- Etkinlik ----
        var etkinlikTarihi = simdi.AddDays(30);

        var etkinlik = Event.Create(
            title: "Entegrasyon Test Konseri",
            description: "Testler icin olusturuldu",
            categoryId: kategori.Id,
            organizerId: profil.Id,
            cityId: sehir.Id,
            venueId: mekan.Id,
            hallId: salon.Id,
            eventDate: etkinlikTarihi,

            // Satis BASLAMIS olmali: dune ayarliyorum.
            // Bugune ayarlasaydik saat farki yuzunden bazen
            // "satis henuz baslamadi" hatasi alirdik -- ve test
            // ARADA BIR kirilirdi. En kotu test turu budur.
            salesStartDate: simdi.AddDays(-1),
            salesEndDate: etkinlikTarihi.AddHours(-1),
            durationMinutes: 120,
            maxTicketsPerUser: 4);

        var biletTuru = etkinlik.AddTicketType("Standart", new Money(fiyat, "TRY"));

        var oturum = etkinlik.AddSession(
            etkinlikTarihi,
            etkinlikTarihi.AddHours(2),
            salon.Id,
            plan.Id);

        db.Events.Add(etkinlik);
        await db.SaveChangesAsync().ConfigureAwait(false);

        // ---- Koltuk uretimi ----
        var koltuklar = oturum.GenerateSeats(
            bolum.Seats.ToList(),
            _ => (biletTuru.Id, new Money(fiyat, "TRY")));

        db.EventSeats.AddRange(koltuklar);
        await db.SaveChangesAsync().ConfigureAwait(false);

        // ---- Durum gecisleri ----
        //
        // ==========================================================
        // SIRA ZORUNLU: Draft -> PendingApproval -> Published -> SalesOpen
        // ==========================================================
        // Dogrudan SalesOpen'a gecmeyi denedim, durum makinesi
        // reddetti. Bu bir engel degil, tasarimin calistiginin
        // kaniti: onaydan gecmemis bir etkinlik satisa cikamiyor.
        //
        // Sprint 12'de de ayni makine beni bir hatadan korumustu
        // (SalesOpen -> Completed dogrudan gecilemiyordu).
        // ==========================================================
        etkinlik.SubmitForApproval();
        etkinlik.Publish();
        etkinlik.OpenSales();

        await db.SaveChangesAsync().ConfigureAwait(false);

        return new Senaryo(
            etkinlik.Id,
            oturum.Id,
            biletTuru.Id,
            koltuklar.Select(k => k.Id).ToList(),
            fiyat);
    }

    /// <summary>
    /// Onaya gonderilmeye HAZIR bir taslak etkinlik olusturur.
    /// </summary>
    /// <remarks>
    /// Oturum ve bilet turu ICEREN bir taslak. Ikisi de olmadan
    /// SubmitForApproval() reddediyor (domain kurali).
    ///
    /// Kurulum domain metotlariyla; TEST EDILEN gecisler (submit ve
    /// publish) HTTP uzerinden yapiliyor.
    /// </remarks>
    public static async Task<Guid> TaslakEtkinlikAsync(
        TicketingDbContext db,
        Guid organizatorProfilId,
        Guid kategoriId,
        Guid sehirId,
        Guid mekanId,
        Guid salonId,
        Guid planId,
        string baslik = "Taslak Etkinlik")
    {
        ArgumentNullException.ThrowIfNull(db);

        var simdi = DateTimeOffset.UtcNow;
        var etkinlikTarihi = simdi.AddDays(45);

        var etkinlik = Event.Create(
            title: baslik,
            description: "Test",
            categoryId: kategoriId,
            organizerId: organizatorProfilId,
            cityId: sehirId,
            venueId: mekanId,
            hallId: salonId,
            eventDate: etkinlikTarihi,
            salesStartDate: simdi.AddDays(-1),
            salesEndDate: etkinlikTarihi.AddHours(-1),
            durationMinutes: 90);

        etkinlik.AddTicketType("Standart", new Money(100m, "TRY"));
        etkinlik.AddSession(etkinlikTarihi, etkinlikTarihi.AddHours(2), salonId, planId);

        db.Events.Add(etkinlik);
        await db.SaveChangesAsync().ConfigureAwait(false);

        return etkinlik.Id;
    }

    /// <summary>
    /// Bir rezervasyonun suresini GECMISE ceker.
    /// </summary>
    /// <remarks>
    /// ==============================================================
    /// NEDEN BEKLEMIYORUZ?
    /// ==============================================================
    /// Rezervasyon kilidi 10 dakika. "Suresi dolmus rezervasyonda
    /// odeme" senaryosunu gercekten beklemeyle test etseydik tek bir
    /// test 10 dakika surerdi -- ve kimse o paketi calistirmazdi.
    ///
    /// Bunun yerine ZAMANI degistiriyoruz: son kullanma tarihini
    /// gecmise cekiyoruz. Sistem acisindan bu, gercekten suresi
    /// dolmus bir rezervasyonla AYNI durum.
    ///
    /// SQL ile yaziyorum cunku ExpiresAt private set ve domain'de
    /// "suresini geriye al" diye bir metot YOK -- olmamali da.
    /// Uretim kodunun izin vermedigi bir seyi test kurulumu icin
    /// domain'e eklemek, korumayi delmek olurdu.
    /// ==============================================================
    /// </remarks>
    public static async Task RezervasyonSuresiniDoldurAsync(
        TicketingDbContext db,
        Guid rezervasyonId)
    {
        ArgumentNullException.ThrowIfNull(db);

        await db.Database.ExecuteSqlAsync(
            $"""UPDATE "Reservations" SET "ExpiresAt" = now() - interval '1 minute' WHERE "Id" = {rezervasyonId}""")
            .ConfigureAwait(false);
    }
}
