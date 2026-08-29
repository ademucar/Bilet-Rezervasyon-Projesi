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
/// "Rezervasyon olusturma", "Ayni koltugu iki kullanicinin almaya
/// calismasi", "Suresi dolmus rezervasyonda odeme", "Basarili odeme
/// sonrasi bilet olusturma", "Basarisiz odeme sonrasi koltuk serbest
/// birakma", "Iade islemi".
/// </summary>
public sealed class ReservationFlowTests : IntegrationTestBase
{
    public ReservationFlowTests(TicketingTestFactory factory) : base(factory)
    {
    }

    /// <summary>Organizator kullanici + satisa acik etkinlik kurar.</summary>
    private async Task<SenaryoKurucu.Senaryo> SenaryoHazirlaAsync(int koltukSayisi = 4)
    {
        var token = await KayitOlVeGirisYapAsync("organizator@ornek.com");
        TokenKullan(token);

        Guid organizatorId;

        using (var db = Db())
        {
            organizatorId = await db.Users
                .Where(u => u.Email == "organizator@ornek.com")
                .Select(u => u.Id)
                .FirstAsync();
        }

        using var db2 = Db();

        return await SenaryoKurucu
            .SatisaAcikEtkinlikAsync(db2, organizatorId, koltukSayisi)
            .ConfigureAwait(false);
    }

    private async Task<(Guid Id, string Kod)> RezervasyonYapAsync(
        SenaryoKurucu.Senaryo senaryo,
        params Guid[] koltuklar)
    {
        var yanit = await Client.PostAsJsonAsync("/api/v1/reservations", new
        {
            eventSessionId = senaryo.SessionId,
            eventSeatIds = koltuklar,
        });

        yanit.StatusCode.Should().Be(HttpStatusCode.Created);

        using var belge = JsonDocument.Parse(await yanit.Content.ReadAsStringAsync());

        return (
            belge.RootElement.GetProperty("id").GetGuid(),
            belge.RootElement.GetProperty("reservationCode").GetString()!);
    }

    // ==============================================================
    // PDF: "Rezervasyon olusturma"
    // ==============================================================

    [Fact]
    public async Task Rezervasyon_olusturuldugunda_koltuklar_kilitlenmeli()
    {
        var senaryo = await SenaryoHazirlaAsync();

        var musteri = await KayitOlVeGirisYapAsync("musteri@ornek.com");
        TokenKullan(musteri);

        var (rezervasyonId, kod) = await RezervasyonYapAsync(
            senaryo, senaryo.SeatIds[0], senaryo.SeatIds[1]);

        kod.Should().StartWith("RSV-");

        using var db = Db();

        // Koltuklar GERCEKTEN kilitlenmis olmali.
        var koltuklar = await db.EventSeats
            .Where(s => senaryo.SeatIds.Take(2).Contains(s.Id))
            .ToListAsync();

        koltuklar.Should().OnlyContain(s => s.Status == EventSeatStatus.Locked);

        // ==========================================================
        // KILIT SURESI KONTROLU
        // ==========================================================
        // Sadece "Locked" olmasi yetmez: kilidin bir SON KULLANMA
        // tarihi olmali. Olmasaydi koltuk sonsuza kadar kilitli
        // kalir ve odeme yapmayan bir kullanici koltugu kalici
        // olarak isgal ederdi.
        // ==========================================================
        koltuklar.Should().OnlyContain(s => s.LockedUntil != null);

        var rezervasyon = await db.Reservations.SingleAsync(r => r.Id == rezervasyonId);

        rezervasyon.Status.Should().Be(ReservationStatus.Locked);
        rezervasyon.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    // ==============================================================
    // PDF: "Ayni koltugu iki kullanicinin almaya calismasi"
    // ==============================================================

    /// <remarks>
    /// ==============================================================
    /// PROJENIN EN KRITIK TESTI
    /// ==============================================================
    /// Bu davranis, EF Core InMemory saglayicisiyla test EDILEMEZ:
    /// xmin tabanli iyimser eszamanlilik orada hic yok. Test yesil
    /// doner ve HICBIR SEY kanitlamaz.
    ///
    /// Testcontainers'in bu projede var olma sebebi tam olarak bu
    /// senaryo -- PDF de zaten gercek kapsayici istiyor.
    /// ==============================================================
    /// </remarks>
    [Fact]
    public async Task Ayni_koltugu_ikinci_kullanici_alamamali()
    {
        var senaryo = await SenaryoHazirlaAsync();
        var koltuk = senaryo.SeatIds[0];

        // Birinci kullanici koltugu aliyor.
        var birinci = await KayitOlVeGirisYapAsync("birinci@ornek.com");
        TokenKullan(birinci);
        await RezervasyonYapAsync(senaryo, koltuk);

        // Ikinci kullanici AYNI koltugu istiyor.
        var ikinci = await KayitOlVeGirisYapAsync("ikinci@ornek.com");
        TokenKullan(ikinci);

        var yanit = await Client.PostAsJsonAsync("/api/v1/reservations", new
        {
            eventSessionId = senaryo.SessionId,
            eventSeatIds = new[] { koltuk },
        });

        // ==========================================================
        // 409 Conflict -- 500 DEGIL
        // ==========================================================
        // Durum kodu onemli: 500 "sunucu bozuk" demek olurdu, oysa
        // sunucu tam olarak dogru calisti ve veri butunlugunu
        // korudu. Frontend 409'da koltuk haritasini yenileyip
        // "bu koltuk az once alindi" diyebiliyor.
        //
        // 422 de degil: bu bir is kurali ihlali degil, bir YARIS
        // sonucu. Kullanici tekrar denerse (baska koltukla)
        // basarili olabilir.
        // ==========================================================
        yanit.StatusCode.Should().Be(HttpStatusCode.Conflict);

        using var db = Db();

        // ==========================================================
        // EN ONEMLI DOGRULAMA: VERI BOZULMADI
        // ==========================================================
        // Ikinci istek reddedildi ama BIRINCININ kilidini de
        // bozmadi. "Son yazan kazanir" davranisi olsaydi koltuk
        // ikinci kullaniciya gecerdi ve birinci kullanici odeme
        // yapmaya calisirken koltugunu kaybettigini gorurdu.
        // ==========================================================
        var rezervasyonSayisi = await db.Reservations.CountAsync();
        rezervasyonSayisi.Should().Be(1, "yalnizca birinci rezervasyon olusmali");

        var kilitliKoltuk = await db.EventSeats.SingleAsync(s => s.Id == koltuk);
        kilitliKoltuk.Status.Should().Be(EventSeatStatus.Locked);
    }

    // ==============================================================
    // PDF: "Suresi dolmus rezervasyonda odeme"
    // ==============================================================

    [Fact]
    public async Task Suresi_dolmus_rezervasyonda_odeme_baslatilamamali()
    {
        var senaryo = await SenaryoHazirlaAsync();

        var musteri = await KayitOlVeGirisYapAsync("gec@ornek.com");
        TokenKullan(musteri);

        var (rezervasyonId, _) = await RezervasyonYapAsync(senaryo, senaryo.SeatIds[0]);

        // Zamani ileri almak yerine son kullanma tarihini geriye
        // cekiyoruz: 10 dakika beklemek yerine.
        using (var db = Db())
        {
            await SenaryoKurucu.RezervasyonSuresiniDoldurAsync(db, rezervasyonId);
        }

        var yanit = await Client.PostAsJsonAsync("/api/v1/payments", new
        {
            reservationId = rezervasyonId,
        });

        // 422: is kurali ihlali. İstek bicimsel olarak dogru ama
        // sistemin durumu bu islemi kabul etmiyor.
        yanit.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        using var db2 = Db();

        // Odeme kaydi HIC olusmamali -- yarim kalmis bir odeme
        // kaydi, mutabakatta "bu para nerede?" sorusuna yol acardi.
        (await db2.Payments.CountAsync()).Should().Be(0);
    }

    // ==============================================================
    // PDF: "Basarili odeme sonrasi bilet olusturma"
    // ==============================================================

    [Fact]
    public async Task Basarili_odeme_bilet_uretmeli_ve_koltugu_satmali()
    {
        var senaryo = await SenaryoHazirlaAsync();

        var musteri = await KayitOlVeGirisYapAsync("odeyen@ornek.com");
        TokenKullan(musteri);

        var (rezervasyonId, _) = await RezervasyonYapAsync(
            senaryo, senaryo.SeatIds[0], senaryo.SeatIds[1]);

        // ---- Odeme baslat ----
        var odemeYanit = await Client.PostAsJsonAsync("/api/v1/payments", new
        {
            reservationId = rezervasyonId,
        });

        odemeYanit.StatusCode.Should().Be(HttpStatusCode.Created);

        using var odemeBelge = JsonDocument.Parse(
            await odemeYanit.Content.ReadAsStringAsync());

        var odemeId = odemeBelge.RootElement.GetProperty("id").GetGuid();

        // ==========================================================
        // PROVIDER REFERENCE'I UYDURAMAYIZ -- ILK DENEMEM BUYDU
        // ==========================================================
        // Once "TEST-REF-1" diye kendi uydurdugum bir referans
        // gonderdim ve 422 aldim.
        //
        // Sebep Sprint 8'de yazdigimiz guvenlik kontrolu:
        // CompletePayment, referansi SAGLAYICIYA dogrulatiyor ve
        // MockPaymentProvider yalnizca KENDI urettigi referanslari
        // taniyor.
        //
        // Bu bir test engeli degil, korumanin CALISTIGININ kaniti:
        // o kontrol olmasaydi saldirgan dogrudan bu adrese istek
        // atip BEDAVA BILET alabilirdi.
        //
        // Bos gonderiyoruz; handler odemenin kendi kayitli
        // referansini kullaniyor.
        // ==========================================================
        var tamamla = await Client.PostAsJsonAsync(
            $"/api/v1/payments/{odemeId}/complete",
            new { providerReference = (string?)null });

        tamamla.StatusCode.Should().Be(HttpStatusCode.OK);

        using var db = Db();

        // ---- Biletler uretildi mi? ----
        var biletler = await db.Tickets
            .Where(t => t.ReservationItem.ReservationId == rezervasyonId)
            .ToListAsync();

        biletler.Should().HaveCount(2, "her koltuk icin bir bilet uretilmeli");

        // ---- Koltuklar SATILDI olmali ----
        //
        // Locked degil Sold: Locked 10 dakika sonra bosalabilir,
        // Sold bir daha asla bosalmaz. Ayrim istemci icin de kritik
        // (Sprint 10: SeatLocked vs SeatSold olaylari).
        var koltuklar = await db.EventSeats
            .Where(s => senaryo.SeatIds.Take(2).Contains(s.Id))
            .ToListAsync();

        koltuklar.Should().OnlyContain(s => s.Status == EventSeatStatus.Sold);

        var rezervasyon = await db.Reservations.SingleAsync(r => r.Id == rezervasyonId);
        rezervasyon.Status.Should().Be(ReservationStatus.Confirmed);
    }

    /// <remarks>
    /// PDF Sprint 15 idempotency maddesi: "Odeme callback".
    ///
    /// Odeme saglayicilari callback'i BIRDEN FAZLA KEZ gonderebilir
    /// (ag hatasi, yeniden deneme). Her cagride yeni bilet uretseydik
    /// kullanicinin elinde 3 bilet olurdu ve koltuk sayisi tutmazdi.
    /// </remarks>
    [Fact]
    public async Task Odeme_callback_iki_kez_gelse_de_ikinci_bilet_uretilmemeli()
    {
        var senaryo = await SenaryoHazirlaAsync();

        var musteri = await KayitOlVeGirisYapAsync("tekrar@ornek.com");
        TokenKullan(musteri);

        var (rezervasyonId, _) = await RezervasyonYapAsync(senaryo, senaryo.SeatIds[0]);

        var odemeYanit = await Client.PostAsJsonAsync("/api/v1/payments", new
        {
            reservationId = rezervasyonId,
        });

        using var belge = JsonDocument.Parse(await odemeYanit.Content.ReadAsStringAsync());
        var odemeId = belge.RootElement.GetProperty("id").GetGuid();

        await Client.PostAsJsonAsync(
            $"/api/v1/payments/{odemeId}/complete", new { providerReference = (string?)null });

        // AYNI callback ikinci kez.
        await Client.PostAsJsonAsync(
            $"/api/v1/payments/{odemeId}/complete", new { providerReference = (string?)null });

        using var db = Db();

        var biletSayisi = await db.Tickets
            .CountAsync(t => t.ReservationItem.ReservationId == rezervasyonId);

        biletSayisi.Should().Be(1, "ikinci callback yeni bilet uretmemeli");
    }

    // ==============================================================
    // PDF: "Basarisiz odeme sonrasi koltuk serbest birakma"
    // ==============================================================

    /// <remarks>
    /// ==============================================================
    /// BU TESTI ONCE YANLIS YAZDIM
    /// ==============================================================
    /// Ilk halinde "koltuk kilitli kalmali, kullanici tekrar
    /// denesin" bekliyordum ve gerekcesini de yazmistim: kart hatasi
    /// yaygin, kullanici baska kartla hemen tekrar dener.
    ///
    /// Test kirildi. Kod, rezervasyonu IPTAL edip koltuklari SERBEST
    /// birakiyordu.
    ///
    /// Kodu duzeltmedim -- cunku kod DOGRUYDU. PDF Sprint 8 acikca
    /// diyor ki: "Odeme basarisiz oldugunda koltuklar serbest
    /// birakilmalidir." Handler'in icindeki yorumda bu tartisma
    /// zaten yazili: is analizinde ben de tersini onermistim ama
    /// sartname benim tercihimin onune geciyor.
    ///
    /// Yani testim, kendi eski goruşumu dogruluyordu; sartnameyi
    /// degil. Beklentiyi PDF'e gore duzelttim.
    ///
    /// Ders: bir test kirildiginda ilk soru "kod mu yanlis, test mi?"
    /// olmali. Burada cevap testti.
    /// </remarks>
    [Fact]
    public async Task Basarisiz_odemede_koltuklar_serbest_birakilmali()
    {
        var senaryo = await SenaryoHazirlaAsync();

        var musteri = await KayitOlVeGirisYapAsync("basarisiz@ornek.com");
        TokenKullan(musteri);

        var (rezervasyonId, _) = await RezervasyonYapAsync(senaryo, senaryo.SeatIds[0]);

        var odemeYanit = await Client.PostAsJsonAsync("/api/v1/payments", new
        {
            reservationId = rezervasyonId,
        });

        using var belge = JsonDocument.Parse(await odemeYanit.Content.ReadAsStringAsync());
        var odemeId = belge.RootElement.GetProperty("id").GetGuid();

        var basarisiz = await Client.PostAsJsonAsync(
            $"/api/v1/payments/{odemeId}/fail", new { });

        basarisiz.IsSuccessStatusCode.Should().BeTrue();

        using var db = Db();

        var rezervasyon = await db.Reservations.SingleAsync(r => r.Id == rezervasyonId);
        rezervasyon.Status.Should().Be(ReservationStatus.Cancelled);

        // PDF Sprint 8: "Odeme basarisiz oldugunda koltuklar serbest
        // birakilmalidir." Koltuk YENIDEN SATILABILIR olmali.
        var koltuk = await db.EventSeats.SingleAsync(s => s.Id == senaryo.SeatIds[0]);
        koltuk.Status.Should().Be(EventSeatStatus.Available);

        var odeme = await db.Payments.SingleAsync(p => p.Id == odemeId);
        odeme.Status.Should().Be(PaymentStatus.Failed);

        // Koltuk gercekten yeniden alinabilmeli: durum alanina
        // bakmak yetmez, AKIS da calismali.
        //
        // Bu ayrimi Sprint 16'da ogrendim: bir alanin dogru degeri
        // tasimasi, sistemin o degeri kullandigi anlamina gelmiyor.
        var baskaMusteri = await KayitOlVeGirisYapAsync("ikinci-sans@ornek.com");
        TokenKullan(baskaMusteri);

        var yenidenAl = await Client.PostAsJsonAsync("/api/v1/reservations", new
        {
            eventSessionId = senaryo.SessionId,
            eventSeatIds = new[] { senaryo.SeatIds[0] },
        });

        yenidenAl.StatusCode.Should().Be(HttpStatusCode.Created,
            "serbest birakilan koltuk baska bir kullanici tarafindan alinabilmeli");
    }

    /// <remarks>
    /// PDF: "Reservation Expire" -- suresi dolan rezervasyonun
    /// koltuklari GERCEKTEN serbest kalmali.
    ///
    /// Yukaridaki testin tamamlayicisi: basarisiz odemede koltuk
    /// bekletiliyor ama SONSUZA KADAR degil.
    /// </remarks>
    [Fact]
    public async Task Suresi_dolan_rezervasyonun_koltuklari_serbest_kalmali()
    {
        var senaryo = await SenaryoHazirlaAsync();

        var musteri = await KayitOlVeGirisYapAsync("suredolan@ornek.com");
        TokenKullan(musteri);

        var (rezervasyonId, _) = await RezervasyonYapAsync(senaryo, senaryo.SeatIds[0]);

        using (var db = Db())
        {
            await SenaryoKurucu.RezervasyonSuresiniDoldurAsync(db, rezervasyonId);
        }

        // ==========================================================
        // BU UC ADMIN YETKISI ISTIYOR -- ILK DENEMEM 403 ALDI
        // ==========================================================
        // Müşteri token'iyla cagirdim ve reddedildi. Dogru davranis:
        // suresi dolan rezervasyonlari toplu temizlemek, siradan bir
        // kullanicinin yapabilecegi bir islem degil.
        //
        // Uretimde bunu Hangfire dakikada bir calistiriyor; uc
        // yalnizca elle tetikleme icin duruyor.
        //
        // Testte Hangfire'i beklemek yerine ucu dogrudan
        // cagiriyoruz: ayni komutu (ExpireReservationsCommand)
        // calistirdigi icin dogrulanan davranis ayni.
        // ==========================================================
        await KayitOlVeGirisYapAsync("temizlik-admin@ornek.com");
        var adminToken = await RolVerVeYenidenGirisAsync(
            "temizlik-admin@ornek.com", Role.Names.Admin);

        TokenKullan(adminToken);

        var yanit = await Client.PostAsync(
            new Uri("/api/v1/reservations/expire-overdue", UriKind.Relative), content: null);

        yanit.IsSuccessStatusCode.Should().BeTrue();

        using var db2 = Db();

        var rezervasyon = await db2.Reservations.SingleAsync(r => r.Id == rezervasyonId);
        rezervasyon.Status.Should().Be(ReservationStatus.Expired);

        var koltuk = await db2.EventSeats.SingleAsync(s => s.Id == senaryo.SeatIds[0]);

        koltuk.Status.Should().Be(EventSeatStatus.Available,
            "suresi dolan rezervasyonun koltugu yeniden satilabilir olmali");
    }

    // ==============================================================
    // PDF: "Iade islemi"
    // ==============================================================

    [Fact]
    public async Task Tam_iade_koltuklari_serbest_birakmali()
    {
        var senaryo = await SenaryoHazirlaAsync();

        var musteri = await KayitOlVeGirisYapAsync("iade@ornek.com");
        TokenKullan(musteri);

        var (rezervasyonId, _) = await RezervasyonYapAsync(senaryo, senaryo.SeatIds[0]);

        var odemeYanit = await Client.PostAsJsonAsync("/api/v1/payments", new
        {
            reservationId = rezervasyonId,
        });

        using var belge = JsonDocument.Parse(await odemeYanit.Content.ReadAsStringAsync());
        var odemeId = belge.RootElement.GetProperty("id").GetGuid();

        await Client.PostAsJsonAsync(
            $"/api/v1/payments/{odemeId}/complete", new { providerReference = (string?)null });

        // ---- Iade (admin yetkisi gerekiyor) ----
        var adminToken = await KayitOlVeGirisYapAsync("admin@ornek.com");
        adminToken = await RolVerVeYenidenGirisAsync("admin@ornek.com", Role.Names.Admin);
        TokenKullan(adminToken);

        var iade = await Client.PostAsJsonAsync(
            $"/api/v1/payments/{odemeId}/refund",
            new { amount = (decimal?)null, reason = "Test iadesi" });

        iade.StatusCode.Should().Be(HttpStatusCode.OK);

        using var db = Db();

        var odeme = await db.Payments.SingleAsync(p => p.Id == odemeId);
        odeme.Status.Should().Be(PaymentStatus.Refunded);

        // Tam iadede koltuklar YENIDEN SATILABILIR olmali.
        var koltuk = await db.EventSeats.SingleAsync(s => s.Id == senaryo.SeatIds[0]);
        koltuk.Status.Should().Be(EventSeatStatus.Available);

        // Biletler iptal edilmeli: iade edilmis bir biletle
        // etkinlige girilememeli.
        var biletler = await db.Tickets
            .Where(t => t.ReservationItem.ReservationId == rezervasyonId)
            .ToListAsync();

        biletler.Should().OnlyContain(t => t.Status != TicketStatus.Active);
    }

    /// <remarks>
    /// PDF Sprint 15 idempotency maddesi: "Iade baslatma".
    ///
    /// Iade, cift calistirilmasi EN TEHLIKELI islem: ayni parayi iki
    /// kez geri gondermek dogrudan mali kayip.
    /// </remarks>
    [Fact]
    public async Task Ayni_idempotency_anahtariyla_ikinci_iade_yeni_iade_acmamali()
    {
        var senaryo = await SenaryoHazirlaAsync();

        var musteri = await KayitOlVeGirisYapAsync("cifte@ornek.com");
        TokenKullan(musteri);

        var (rezervasyonId, _) = await RezervasyonYapAsync(senaryo, senaryo.SeatIds[0]);

        var odemeYanit = await Client.PostAsJsonAsync("/api/v1/payments", new
        {
            reservationId = rezervasyonId,
        });

        using var belge = JsonDocument.Parse(await odemeYanit.Content.ReadAsStringAsync());
        var odemeId = belge.RootElement.GetProperty("id").GetGuid();

        await Client.PostAsJsonAsync(
            $"/api/v1/payments/{odemeId}/complete", new { providerReference = (string?)null });

        await KayitOlVeGirisYapAsync("admin2@ornek.com");
        var adminToken = await RolVerVeYenidenGirisAsync("admin2@ornek.com", Role.Names.Admin);
        TokenKullan(adminToken);

        Client.DefaultRequestHeaders.Add("Idempotency-Key", "IADE-ANAHTARI-1");

        var ilk = await Client.PostAsJsonAsync(
            $"/api/v1/payments/{odemeId}/refund",
            new { amount = (decimal?)25m, reason = "Kismi iade" });

        ilk.StatusCode.Should().Be(HttpStatusCode.OK);

        // AYNI anahtarla ikinci istek.
        var ikinci = await Client.PostAsJsonAsync(
            $"/api/v1/payments/{odemeId}/refund",
            new { amount = (decimal?)25m, reason = "Kismi iade" });

        ikinci.StatusCode.Should().Be(HttpStatusCode.OK,
            "ayni anahtarla gelen istek HATA degil, ilk sonucu donmeli");

        using var db = Db();

        var iadeSayisi = await db.PaymentTransactions
            .CountAsync(t => t.PaymentId == odemeId && t.Type == PaymentTransactionType.Refund);

        iadeSayisi.Should().Be(1, "ayni anahtarla ikinci iade kaydi olusmamali");
    }
}
