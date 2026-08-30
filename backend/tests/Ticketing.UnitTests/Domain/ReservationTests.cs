using FluentAssertions;
using Ticketing.Domain.Common;
using Ticketing.Domain.Entities;
using Ticketing.Domain.Enums;
using Ticketing.Domain.Events;

namespace Ticketing.UnitTests.Domain;

/// <summary>
/// PDF Sprint 7'deki rezervasyon is kurallarinin testleri.
/// </summary>
public class ReservationTests
{
    private static readonly DateTimeOffset Simdi = TestVeriKurucu.Simdi;
    private static readonly TimeSpan KilitSuresi = TimeSpan.FromMinutes(10);

    private static Reservation Rezervasyon(int koltukSayisi = 2, decimal birimFiyat = 250m)
    {
        var (_, koltuklar) = TestVeriKurucu.OturumVeKoltuklar(koltukSayisi, birimFiyat);

        return Reservation.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            koltuklar.Take(koltukSayisi).ToList(),
            KilitSuresi,
            Simdi);
    }

    // Olusturma

    [Fact]
    public void Create_YeniRezervasyon_LockedDurumundaBaslamali()
    {
        Rezervasyon().Status.Should().Be(ReservationStatus.Locked);
    }

    [Fact]
    public void Create_KoltuklariDaKilitlemeli()
    {
        var (_, koltuklar) = TestVeriKurucu.OturumVeKoltuklar(3);

        var rezervasyon = Reservation.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), koltuklar.ToList(), KilitSuresi, Simdi);

        koltuklar.Should().AllSatisfy(k =>
        {
            k.Status.Should().Be(EventSeatStatus.Locked);
            k.LockedByReservationId.Should().Be(rezervasyon.Id);
        });
    }

    [Fact]
    public void Create_ToplamTutariBackendHesaplamali()
    {
        // PDF Sprint 6: "Frontend tarafindan gonderilen toplam tutara
        // guvenilmemelidir."
        //
        // Reservation.Create metodunun imzasinda "toplam tutar" diye bir
        // parametre YOK. Bu kasitli bir tasarim: cagiran taraf tutar
        // gonderemez bile. Guvenligi kural ile degil, tip sistemi ile
        // sagliyorum -- unutulmasi imkansiz.
        var rezervasyon = Rezervasyon(koltukSayisi: 3, birimFiyat: 150m);

        rezervasyon.TotalAmount.Amount.Should().Be(450m);
        rezervasyon.TotalAmount.Currency.Should().Be("TRY");
    }

    [Fact]
    public void Create_KoltukSecilmemisse_HataFirlatmali()
    {
        var eylem = () => Reservation.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), [], KilitSuresi, Simdi);

        eylem.Should().Throw<DomainException>()
             .Which.ErrorCode.Should().Be("reservation.no_seats");
    }

    [Fact]
    public void Create_AyniKoltukIkiKez_HataFirlatmali()
    {
        var (_, koltuklar) = TestVeriKurucu.OturumVeKoltuklar(2);

        var eylem = () => Reservation.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(),
            [koltuklar[0], koltuklar[0]],   // ayni koltuk iki kez
            KilitSuresi, Simdi);

        eylem.Should().Throw<DomainException>()
             .Which.ErrorCode.Should().Be("reservation.duplicate_seats");
    }

    [Fact]
    public void Create_KoltuklardanBiriDoluysa_RezervasyonHicOlusmamali()
    {
        // "Ya hep ya hic": 3 koltuktan biri kapilmissa rezervasyon
        // KISMEN olusmamali. Aksi halde kullanici 2 koltuk icin odeme
        // yapar ama 3 koltuk bekler.
        var (_, koltuklar) = TestVeriKurucu.OturumVeKoltuklar(3);

        // Ikinci koltugu baskasi kapti
        koltuklar[1].Lock(Guid.CreateVersion7(), Simdi.AddMinutes(10), Simdi);

        var eylem = () => Reservation.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), koltuklar.ToList(), KilitSuresi, Simdi);

        eylem.Should().Throw<DomainException>()
             .Which.ErrorCode.Should().Be("seat.already_locked");
    }

    [Fact]
    public void Create_RezervasyonOlustuOlayiFirlatmali()
    {
        var rezervasyon = Rezervasyon();

        rezervasyon.DomainEvents.Should().ContainSingle()
                   .Which.Should().BeOfType<ReservationCreatedDomainEvent>();
    }

    [Fact]
    public void Create_OkunabilirKodUretmeli()
    {
        var rezervasyon = Rezervasyon();

        rezervasyon.ReservationCode.Should().StartWith("RSV-").And.HaveLength(10);

        // Karisabilecek karakterler (0/O, 1/I/L) bilerek disarida birakildi.
        // Kullanici kodu telefonda okuyacak.
        rezervasyon.ReservationCode[4..].Should().NotContainAny("0", "O", "1", "I", "L");
    }

    // Sure kontrolu -- PDF Sprint 7'nin kalbi

    [Fact]
    public void Create_SureyiDogruHesaplamali()
    {
        Rezervasyon().ExpiresAt.Should().Be(Simdi.AddMinutes(10));
    }

    [Fact]
    public void StartPayment_SuresiDolmusRezervasyon_HataFirlatmali()
    {
        // PDF: "Suresi dolmus rezervasyon uzerinden odeme baslatilamaz."
        //
        // Bu kural iki katmanda korunuyor:
        //   1. Burada acik sure kontrolu -> "reservation.expired" hatasi
        //   2. Durum makinesinde: Expired -> PaymentPending yolu YOK
        //
        // Ikisi de gerekli: kullanici HENUZ Expired isaretlenmemis ama
        // suresi gecmis bir rezervasyonda odeme baslatmaya calisabilir
        // (temizlik job'i henuz gelmemistir). Durum makinesi bunu
        // yakalayamaz cunku durum hala Locked'dir.
        var rezervasyon = Rezervasyon();
        var sureSonrasi = Simdi.AddMinutes(11);

        var eylem = () => rezervasyon.StartPayment(sureSonrasi);

        eylem.Should().Throw<DomainException>()
             .Which.ErrorCode.Should().Be("reservation.expired");
    }

    [Fact]
    public void StartPayment_SureIcinde_PaymentPendingeGecmeli()
    {
        var rezervasyon = Rezervasyon();

        rezervasyon.StartPayment(Simdi.AddMinutes(5));

        rezervasyon.Status.Should().Be(ReservationStatus.PaymentPending);
    }

    [Fact]
    public void StartPayment_ExpiredDurumdan_DurumMakinesiEngellemeli()
    {
        // Bu test, sure kontrolunden BAGIMSIZ olarak durum makinesinin
        // calistigini dogruluyor. Bu yuzden "simdi" degerini bilerek
        // sure DOLMADAN once seciyorum -- yoksa sure kontrolu devreye girer
        // ve durum makinesini hic test etmemis olurdum.
        //
        // (Ilk yazisimda burada "reservation.invalid_transition" bekleyip
        // "simdi"yi sure sonrasi vermistim; test kirmizi yandi cunku
        // StartPayment sureyi ONCE kontrol ediyor. Kod dogruydu, testin
        // varsayimi yanlisti.)
        var rezervasyon = Rezervasyon();
        rezervasyon.Expire(Simdi.AddMinutes(11));

        var eylem = () => rezervasyon.StartPayment(Simdi.AddMinutes(5));   // sure DOLMAMIS

        eylem.Should().Throw<DomainException>()
             .Which.ErrorCode.Should().Be("reservation.invalid_transition");
    }

    [Fact]
    public void StartPayment_SureKontroluDurumKontrolundenONCEYapilmali()
    {
        // Hata kodunun hangisi oldugu onemli: kullaniciya "sureniz doldu"
        // demek, "gecersiz durum gecisi" demekten cok daha anlamli.
        // Frontend de bu iki koda farkli tepki verecek.
        var rezervasyon = Rezervasyon();

        var eylem = () => rezervasyon.StartPayment(Simdi.AddMinutes(11));

        eylem.Should().Throw<DomainException>()
             .Which.ErrorCode.Should().Be("reservation.expired");
    }

    [Fact]
    public void GetRemainingTime_SureGectiyse_SifirDonmeli()
    {
        // Negatif sure donseydim frontend geri sayimda "-00:03" gosterirdi.
        var rezervasyon = Rezervasyon();

        rezervasyon.GetRemainingTime(Simdi.AddMinutes(15)).Should().Be(TimeSpan.Zero);
    }

    // Sure uzatma

    [Fact]
    public void Extend_LimitDahilinde_SureyiUzatmali()
    {
        var rezervasyon = Rezervasyon();

        rezervasyon.Extend(TimeSpan.FromMinutes(5), maxExtensions: 1, Simdi.AddMinutes(2));

        rezervasyon.ExpiresAt.Should().Be(Simdi.AddMinutes(15));
        rezervasyon.ExtensionCount.Should().Be(1);
    }

    [Fact]
    public void Extend_LimitiAsinca_HataFirlatmali()
    {
        // Sinirsiz uzatma olsaydi bir kullanici populer bir etkinlikte
        // koltuklari suresiz bloke edip satisi sabote edebilirdi.
        var rezervasyon = Rezervasyon();
        rezervasyon.Extend(TimeSpan.FromMinutes(5), 1, Simdi.AddMinutes(2));

        var eylem = () => rezervasyon.Extend(TimeSpan.FromMinutes(5), 1, Simdi.AddMinutes(3));

        eylem.Should().Throw<DomainException>()
             .Which.ErrorCode.Should().Be("reservation.extension_limit_reached");
    }

    [Fact]
    public void Extend_SuresiDolmusRezervasyon_HataFirlatmali()
    {
        // Suresi dolmus rezervasyonu "uzatmak" onu diriltmek olurdu.
        // Koltuklar bu arada baskasina satılmış olabilir.
        var rezervasyon = Rezervasyon();

        var eylem = () => rezervasyon.Extend(TimeSpan.FromMinutes(5), 3, Simdi.AddMinutes(11));

        eylem.Should().Throw<DomainException>()
             .Which.ErrorCode.Should().Be("reservation.expired");
    }

    // Odeme basarisiz akisi (docs/01-is-analizi.md soru 8)

    [Fact]
    public void RevertToLocked_OdemeBasarisiz_SureyiUZATMAMALI()
    {
        // Bu testin adi buyuk harfle: kolay atlanacak ama onemli bir kural.
        //
        // Odeme basarisiz olunca kullaniciya ikinci sans veriyorum ama
        // sure uzatmiyoruz. Uzatsaydik, surekli basarisiz odeme deneyerek
        // koltugu suresiz bloke etmek mumkun olurdu.
        var rezervasyon = Rezervasyon();
        var ilkSure = rezervasyon.ExpiresAt;

        rezervasyon.StartPayment(Simdi.AddMinutes(2));
        rezervasyon.RevertToLocked();

        rezervasyon.Status.Should().Be(ReservationStatus.Locked);
        rezervasyon.ExpiresAt.Should().Be(ilkSure, "odeme basarisizliginda sure uzatilmaz");
    }

    // Onaylama ve son durumlar

    [Fact]
    public void Confirm_OdemeBasarili_ConfirmedOlmaliVeOlayFirlatmali()
    {
        var rezervasyon = Rezervasyon();
        rezervasyon.StartPayment(Simdi.AddMinutes(2));

        rezervasyon.Confirm(Guid.CreateVersion7(), Simdi.AddMinutes(3));

        rezervasyon.Status.Should().Be(ReservationStatus.Confirmed);
        rezervasyon.DomainEvents.Should().ContainItemsAssignableTo<ReservationConfirmedDomainEvent>();
    }

    [Fact]
    public void Confirm_LockedDurumundanDogrudan_HataFirlatmali()
    {
        // Odeme baslatilmadan onaylama girisimi.
        // Bu kontrol olmasaydi odeme yapmadan bilet almak mumkun olurdu.
        var rezervasyon = Rezervasyon();

        var eylem = () => rezervasyon.Confirm(Guid.CreateVersion7(), Simdi);

        eylem.Should().Throw<DomainException>()
             .Which.ErrorCode.Should().Be("reservation.invalid_transition");
    }

    [Fact]
    public void Expire_SureDoldu_OlayFirlatmaliVeKoltukIdleriniIcermeli()
    {
        var rezervasyon = Rezervasyon(koltukSayisi: 3);

        rezervasyon.Expire(Simdi.AddMinutes(11));

        rezervasyon.Status.Should().Be(ReservationStatus.Expired);

        var olay = rezervasyon.DomainEvents
            .OfType<ReservationExpiredDomainEvent>()
            .Should().ContainSingle().Subject;

        // Koltuk Id'leri olayin icinde olmali ki SignalR handler'i
        // hangi koltuklarin serbest kaldigini yayinlayabilsin.
        olay.EventSeatIds.Should().HaveCount(3);
    }

    [Fact]
    public void Cancel_IptalEdilmisRezervasyonTekrarIptal_HataFirlatmali()
    {
        var rezervasyon = Rezervasyon();
        rezervasyon.Cancel("vazgectim");

        var eylem = () => rezervasyon.Cancel("tekrar");

        eylem.Should().Throw<DomainException>()
             .Which.ErrorCode.Should().Be("reservation.invalid_transition");
    }
}
