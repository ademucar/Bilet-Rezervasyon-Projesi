using FluentAssertions;
using Ticketing.Domain.Common;
using Ticketing.Domain.Enums;

namespace Ticketing.UnitTests.Domain;

/// <summary>
/// EventSeat kilitleme testleri.
///
/// Bu dosya PDF'in "es zamanli rezervasyon" probleminin UYGULAMA
/// katmanindaki savunmasini test ediyor. Veritabani katmanindaki savunma
/// (RowVersion + unique index) integration testlerde dogrulanacak --
/// unit testlerde gercek bir veritabani yok.
/// </summary>
public class EventSeatTests
{
    private static readonly DateTimeOffset Simdi = TestVeriKurucu.Simdi;

    // ---------------------------------------------------------------
    // Kilitleme
    // ---------------------------------------------------------------

    [Fact]
    public void Create_YeniKoltuk_AvailableDurumundaBaslamali()
    {
        var (_, koltuklar) = TestVeriKurucu.OturumVeKoltuklar(1);

        koltuklar[0].Status.Should().Be(EventSeatStatus.Available);
        koltuklar[0].IsAvailableAt(Simdi).Should().BeTrue();
    }

    [Fact]
    public void Lock_MusaitKoltuk_KilitlenmeliVeRezervasyonuKaydetmeli()
    {
        var (_, koltuklar) = TestVeriKurucu.OturumVeKoltuklar(1);
        var koltuk = koltuklar[0];
        var rezervasyonId = Guid.CreateVersion7();

        koltuk.Lock(rezervasyonId, Simdi.AddMinutes(10), Simdi);

        koltuk.Status.Should().Be(EventSeatStatus.Locked);
        koltuk.LockedByReservationId.Should().Be(rezervasyonId);
        koltuk.LockedUntil.Should().Be(Simdi.AddMinutes(10));
    }

    [Fact]
    public void Lock_ZatenKilitliKoltuk_HataFirlatmali()
    {
        // ===============================================================
        // PROJENIN EN ONEMLI TESTI
        // "Ayni koltugu iki kullanici ayni anda secerse ne olmali?"
        // Cevap: ilk kilitleyen kazanir, ikinci 409 alir.
        // ===============================================================
        var (_, koltuklar) = TestVeriKurucu.OturumVeKoltuklar(1);
        var koltuk = koltuklar[0];

        koltuk.Lock(Guid.CreateVersion7(), Simdi.AddMinutes(10), Simdi);

        var ikinciKullanici = () => koltuk.Lock(Guid.CreateVersion7(), Simdi.AddMinutes(10), Simdi);

        ikinciKullanici.Should().Throw<DomainException>()
             .Which.ErrorCode.Should().Be("seat.already_locked");
    }

    [Fact]
    public void Lock_SatilmisKoltuk_FarkliHataKoduVermeli()
    {
        // Hata kodunun farkli olmasi onemli: frontend "az once baskasi
        // secti, birazdan bosalabilir" ile "bu koltuk satildi, bir daha
        // bosalmayacak" durumlarina farkli tepki vermeli.
        var (_, koltuklar) = TestVeriKurucu.OturumVeKoltuklar(1);
        var koltuk = koltuklar[0];
        var rezervasyonId = Guid.CreateVersion7();

        koltuk.Lock(rezervasyonId, Simdi.AddMinutes(10), Simdi);
        koltuk.MarkAsSold(rezervasyonId);

        var eylem = () => koltuk.Lock(Guid.CreateVersion7(), Simdi.AddMinutes(10), Simdi);

        eylem.Should().Throw<DomainException>()
             .Which.ErrorCode.Should().Be("seat.already_sold");
    }

    [Fact]
    public void Lock_SuresiDolmusKilitliKoltuk_YenidenKilitlenebilmeli()
    {
        // ===============================================================
        // Bu davranis, "Status == Available" kontrolunun neden YETMEDIGINI
        // gosteriyor.
        //
        // Temizlik job'i dakikada bir calisiyor. Bir kullanicinin kilidi
        // 10:10'da doluyorsa, job 10:11'de gelip temizleyecek. Arada
        // gecen 1 dakikada koltuk bos OLMASINA RAGMEN dolu gorunurdu.
        //
        // Populer bir konserde bu 1 dakika, yuzlerce kullanicinin bos
        // koltugu alamamasi demek.
        // ===============================================================
        var (_, koltuklar) = TestVeriKurucu.OturumVeKoltuklar(1);
        var koltuk = koltuklar[0];

        koltuk.Lock(Guid.CreateVersion7(), Simdi.AddMinutes(10), Simdi);

        var kilitSonrasi = Simdi.AddMinutes(11);   // kilit doldu, job henuz gelmedi

        koltuk.Status.Should().Be(EventSeatStatus.Locked, "job henuz temizlemedi");
        koltuk.IsAvailableAt(kilitSonrasi).Should().BeTrue("kilit suresi gecmis");

        var yeniKullanici = () => koltuk.Lock(
            Guid.CreateVersion7(), kilitSonrasi.AddMinutes(10), kilitSonrasi);

        yeniKullanici.Should().NotThrow();
    }

    [Fact]
    public void Lock_GecmisBitisZamani_HataFirlatmali()
    {
        var (_, koltuklar) = TestVeriKurucu.OturumVeKoltuklar(1);

        var eylem = () => koltuklar[0].Lock(Guid.CreateVersion7(), Simdi.AddMinutes(-1), Simdi);

        eylem.Should().Throw<DomainException>()
             .Which.ErrorCode.Should().Be("seat.invalid_lock_expiry");
    }

    // ---------------------------------------------------------------
    // Satis
    // ---------------------------------------------------------------

    [Fact]
    public void MarkAsSold_BaskaRezervasyonunKilidi_HataFirlatmali()
    {
        // ===============================================================
        // Bu kontrol, odeme akisindaki bir mantik hatasinin baskasinin
        // koltugunu satmasini engelliyor.
        //
        // Senaryo: A rezervasyonu koltugu kilitledi. Bir hata sonucu
        // B rezervasyonunun odemesi bu koltugu satmaya calisti.
        // Bu kontrol olmasaydi A'nin koltugu B'ye satilirdi.
        // ===============================================================
        var (_, koltuklar) = TestVeriKurucu.OturumVeKoltuklar(1);
        var koltuk = koltuklar[0];

        var rezervasyonA = Guid.CreateVersion7();
        var rezervasyonB = Guid.CreateVersion7();

        koltuk.Lock(rezervasyonA, Simdi.AddMinutes(10), Simdi);

        var eylem = () => koltuk.MarkAsSold(rezervasyonB);

        eylem.Should().Throw<DomainException>()
             .Which.ErrorCode.Should().Be("seat.locked_by_another_reservation");
    }

    [Fact]
    public void MarkAsSold_KilitliOlmayanKoltuk_HataFirlatmali()
    {
        // PDF: "Odeme basarili olmadan bilet olusturulamaz."
        // Bunun koltuk tarafindaki karsiligi: kilitlenmemis koltuk satilamaz.
        var (_, koltuklar) = TestVeriKurucu.OturumVeKoltuklar(1);

        var eylem = () => koltuklar[0].MarkAsSold(Guid.CreateVersion7());

        eylem.Should().Throw<DomainException>()
             .Which.ErrorCode.Should().Be("seat.not_locked");
    }

    [Fact]
    public void MarkAsSold_BasariliOdeme_KilitSuresiniTemizlemeli()
    {
        var (_, koltuklar) = TestVeriKurucu.OturumVeKoltuklar(1);
        var koltuk = koltuklar[0];
        var rezervasyonId = Guid.CreateVersion7();

        koltuk.Lock(rezervasyonId, Simdi.AddMinutes(10), Simdi);
        koltuk.MarkAsSold(rezervasyonId);

        koltuk.Status.Should().Be(EventSeatStatus.Sold);
        koltuk.LockedUntil.Should().BeNull("satilan koltugun sure kavrami yok");
        koltuk.IsAvailableAt(Simdi.AddYears(1)).Should().BeFalse("satilan koltuk asla musait olmaz");
    }

    // ---------------------------------------------------------------
    // Serbest birakma ve iade
    // ---------------------------------------------------------------

    [Fact]
    public void Release_SatilmisKoltuk_HataFirlatmali()
    {
        // ===============================================================
        // Bu, ciddi bir veri bozulmasini engelliyor: bileti olan
        // kullanicinin koltugunun baskasina satilmasi.
        //
        // Sure asimi job'i tum kilitli koltuklari serbest birakiyor.
        // Satilmis bir koltuk yanlislikla o listeye girerse, bu kontrol
        // job'i durdurur ve hata loglanir.
        // ===============================================================
        var (_, koltuklar) = TestVeriKurucu.OturumVeKoltuklar(1);
        var koltuk = koltuklar[0];
        var rezervasyonId = Guid.CreateVersion7();

        koltuk.Lock(rezervasyonId, Simdi.AddMinutes(10), Simdi);
        koltuk.MarkAsSold(rezervasyonId);

        var eylem = koltuk.Release;

        eylem.Should().Throw<DomainException>()
             .Which.ErrorCode.Should().Be("seat.already_sold");
    }

    [Fact]
    public void Release_KilitliKoltuk_TekrarMusaitOlmali()
    {
        var (_, koltuklar) = TestVeriKurucu.OturumVeKoltuklar(1);
        var koltuk = koltuklar[0];

        koltuk.Lock(Guid.CreateVersion7(), Simdi.AddMinutes(10), Simdi);
        koltuk.Release();

        koltuk.Status.Should().Be(EventSeatStatus.Available);
        koltuk.LockedByReservationId.Should().BeNull();
        koltuk.LockedUntil.Should().BeNull();
    }

    [Fact]
    public void Refund_SatilmisKoltuk_TekrarSatisaCikmali()
    {
        var (_, koltuklar) = TestVeriKurucu.OturumVeKoltuklar(1);
        var koltuk = koltuklar[0];
        var rezervasyonId = Guid.CreateVersion7();

        koltuk.Lock(rezervasyonId, Simdi.AddMinutes(10), Simdi);
        koltuk.MarkAsSold(rezervasyonId);
        koltuk.Refund();

        koltuk.Status.Should().Be(EventSeatStatus.Available);
        koltuk.IsAvailableAt(Simdi).Should().BeTrue();
    }

    [Fact]
    public void Block_SatilmisKoltuk_HataFirlatmali()
    {
        var (_, koltuklar) = TestVeriKurucu.OturumVeKoltuklar(1);
        var koltuk = koltuklar[0];
        var rezervasyonId = Guid.CreateVersion7();

        koltuk.Lock(rezervasyonId, Simdi.AddMinutes(10), Simdi);
        koltuk.MarkAsSold(rezervasyonId);

        var eylem = koltuk.Block;

        eylem.Should().Throw<DomainException>()
             .Which.ErrorCode.Should().Be("seat.already_sold");
    }

    // ---------------------------------------------------------------
    // Koltuk uretimi
    // ---------------------------------------------------------------

    [Fact]
    public void GenerateSeats_PasifKoltuklariAtlamali()
    {
        // Kirik veya satisa kapali fiziksel koltuklar icin EventSeat
        // uretmiyoruz -- koltuk haritasinda satilamaz ama gorunur bir
        // kayit olusturmanin anlami yok.
        var etkinlik = TestVeriKurucu.Etkinlik();
        var oturum = etkinlik.AddSession(
            TestVeriKurucu.EtkinlikTarihi,
            TestVeriKurucu.EtkinlikTarihi.AddHours(2),
            Guid.CreateVersion7(),
            Guid.CreateVersion7());
        var biletTuru = etkinlik.AddTicketType("Standard", TestVeriKurucu.Fiyat());

        var plan = Ticketing.Domain.Entities.SeatLayout.Create(Guid.CreateVersion7(), "Duzen");
        var bolum = plan.AddSection("Blok", 1);
        bolum.GenerateSeats(1, 5, ["A"]);

        // 5 koltuktan 2'sini devre disi birak
        var koltukListesi = bolum.Seats.ToList();
        koltukListesi[0].Deactivate();
        koltukListesi[1].Deactivate();

        var uretilenler = oturum.GenerateSeats(
            koltukListesi, _ => (biletTuru.Id, TestVeriKurucu.Fiyat()));

        uretilenler.Should().HaveCount(3);
    }

    [Fact]
    public void GenerateSeats_IkinciKez_HataFirlatmali()
    {
        var (oturum, _) = TestVeriKurucu.OturumVeKoltuklar(3);

        var plan = Ticketing.Domain.Entities.SeatLayout.Create(Guid.CreateVersion7(), "Duzen");
        var bolum = plan.AddSection("Blok", 1);
        bolum.GenerateSeats(1, 2, ["B"]);

        var eylem = () => oturum.GenerateSeats(
            bolum.Seats.ToList(), _ => (Guid.CreateVersion7(), TestVeriKurucu.Fiyat()));

        eylem.Should().Throw<DomainException>()
             .Which.ErrorCode.Should().Be("event_session.seats_already_generated");
    }
}
