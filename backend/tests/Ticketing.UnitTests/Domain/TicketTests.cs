using FluentAssertions;
using Ticketing.Domain.Entities;
using Ticketing.Domain.Enums;
using Ticketing.Domain.Common;

namespace Ticketing.UnitTests.Domain;

/// <summary>
/// PDF Sprint 17 birim testi maddesi: "Ticket Generate".
/// </summary>
public class TicketTests
{
    private static readonly DateTimeOffset Simdi = new(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

    private static Ticket BiletUret()
    {
        var (oturum, koltuklar) = TestVeriKurucu.OturumVeKoltuklar(koltukSayisi: 1);

        // Rezervasyon, koltuklari KURULURKEN aliyor ve kalemleri
        // kendisi olusturuyor -- disaridan AddItem cagrilamiyor.
        //
        // Bu bir kapsulleme karari: kalem eklemek koltugu kilitlemek
        // demek ve ikisinin ayri adimlar olmasi, yarim kalmis bir
        // rezervasyon (kalemi var ama koltugu kilitli degil) uretme
        // ihtimali dogururdu.
        var rezervasyon = Reservation.Create(
            Guid.CreateVersion7(),
            oturum.Id,
            [koltuklar[0]],
            TimeSpan.FromMinutes(10),
            Simdi);

        var kalem = rezervasyon.Items.First();

        return Ticket.Create(kalem, rezervasyon.UserId, oturum.Id, Simdi);
    }

    [Fact]
    public void Uretilen_bilet_aktif_baslamali()
    {
        var bilet = BiletUret();

        bilet.Status.Should().Be(TicketStatus.Active);
        bilet.TicketNumber.Should().NotBeNullOrWhiteSpace();
    }

    /// <remarks>
    /// Bilet numarasi benzersiz olmali
    ///
    /// Ayni numaraya sahip iki bilet uretilseydi, girişte QR
    /// dogrulamasi hangi bileti kastettigini bilemezdi: bir kisi
    /// digerinin biletiyle iceri girebilir, gercek sahibi ise
    /// "biletiniz kullanilmis" yaniti alirdi.
    ///
    /// Ayni ANDA uretilen biletlerle test ediyorum: zamana dayali bir
    /// numaralandirma en kolay burada kirilir.
    /// </remarks>
    [Fact]
    public void Ayni_anda_uretilen_biletlerin_numaralari_farkli_olmali()
    {
        var numaralar = Enumerable.Range(0, 20)
            .Select(_ => BiletUret().TicketNumber)
            .ToList();

        numaralar.Should().OnlyHaveUniqueItems();
    }

    // Durum gecisleri

    [Fact]
    public void Aktif_bilet_kullanilabilmeli()
    {
        var bilet = BiletUret();

        bilet.MarkAsUsed(Simdi);

        bilet.Status.Should().Be(TicketStatus.Used);
    }

    /// <remarks>
    /// Ayni biletin iki kez okutulmasi, kapida yasanabilecek en somut
    /// suistimal: bir kisi girer, ekran goruntusunu arkadasina
    /// gonderir.
    ///
    /// Ikinci okutmanin HATA vermesi sart; sessizce gecseydi kontrol
    /// tamamen anlamsiz olurdu.
    /// </remarks>
    [Fact]
    public void Kullanilmis_bilet_ikinci_kez_kullanilamamali()
    {
        var bilet = BiletUret();
        bilet.MarkAsUsed(Simdi);

        var eylem = () => bilet.MarkAsUsed(Simdi.AddMinutes(1));

        eylem.Should().Throw<DomainException>();
    }

    /// <remarks>
    /// Kullanilmis bir bileti iptal etmek YANLIS olurdu: kisi
    /// etkinlige zaten girdi. Iade edilirse hem hizmeti almis hem
    /// parasini geri almis olur.
    /// </remarks>
    [Fact]
    public void Kullanilmis_bilet_iptal_edilememeli()
    {
        var bilet = BiletUret();
        bilet.MarkAsUsed(Simdi);

        var eylem = () => bilet.Cancel(withRefund: true, Simdi.AddMinutes(1));

        eylem.Should().Throw<DomainException>();
    }

    /// <remarks>
    /// İadeli ve iadesiz iptal ayri durumlar
    ///
    /// Tek bir "Cancelled" durumu kullansaydik, mutabakat sirasinda
    /// "bu bilet icin para geri gonderildi mi?" sorusunu yalnizca
    /// odeme kayitlarina bakarak cevaplayabilirdik.
    ///
    /// Ayri durum, sorunun cevabini biletin KENDISINDE tutuyor.
    /// </remarks>
    [Fact]
    public void Iadeli_ve_iadesiz_iptal_farkli_durum_uretmeli()
    {
        var iadeli = BiletUret();
        iadeli.Cancel(withRefund: true, Simdi);

        var iadesiz = BiletUret();
        iadesiz.Cancel(withRefund: false, Simdi);

        iadeli.Status.Should().Be(TicketStatus.Refunded);
        iadesiz.Status.Should().Be(TicketStatus.Cancelled);
    }

    /// <remarks>
    /// Bu testi de once yanlis yazdim
    ///
    /// Ikinci iptalin HATA firlatmasini bekliyordum. Test kirildi:
    /// Cancel() sessizce geri donuyor.
    ///
    /// Kodu okudum, yaninda "// idempotent" yaziyordu. Ve bu DOGRU
    /// tasarim:
    ///
    /// Bileti iptal eden sey bir ARKA PLAN ISI (etkinlik iptali,
    /// iade akisi). Arka plan isleri basarisiz olunca yeniden
    /// deneniyor -- Outbox'ta en az bir kez teslim (at-least-once)
    /// garantisi var, yani AYNI is birden fazla kez calisabilir.
    ///
    /// Hata firlatsaydi: ikinci deneme patlar, is basarisiz sayilir,
    /// tekrar denenir, yine patlar... Sonunda dead letter'a duserdi
    /// -- oysa yapilmasi gereken is ZATEN YAPILMISTI.
    ///
    /// "Zaten istenen durumdaysa sessizce gec" ile "gecersiz bir
    /// gecis denendi, reddet" farkli seyler. Kullanilmis bir bileti
    /// iptal etmek gercekten YANLIS (yukaridaki test), ama iptal
    /// edilmis bir bileti tekrar iptal etmek yalnizca GEREKSIZ.
    /// </remarks>
    [Fact]
    public void Iptal_tekrar_cagrilirsa_durum_degismemeli()
    {
        var bilet = BiletUret();
        bilet.Cancel(withRefund: true, Simdi);

        // Ikinci cagri: hata YOK.
        bilet.Cancel(withRefund: true, Simdi.AddMinutes(1));

        bilet.Status.Should().Be(TicketStatus.Refunded);

        // ONEMLI: ikinci cagri withRefund degerini DEGISTIREMEMELI.
        // Aksi halde "iadesiz iptal" edilmis bir bilet, tekrarlanan
        // bir cagriyla iade edilmis gorunebilirdi.
        var iadesiz = BiletUret();
        iadesiz.Cancel(withRefund: false, Simdi);
        iadesiz.Cancel(withRefund: true, Simdi.AddMinutes(1));

        iadesiz.Status.Should().Be(TicketStatus.Cancelled);
    }

    /// <remarks>
    /// Etkinlik gectikten sonra kullanilmayan biletler Expired olmali.
    ///
    /// Active kalsaydi "kullanilmamis aktif bilet" sayisi surekli
    /// birikirdi ve raporlarda gecmis etkinliklerin biletleri hala
    /// gecerli gorunurdu.
    /// </remarks>
    [Fact]
    public void Kullanilmayan_bilet_suresi_dolabilmeli()
    {
        var bilet = BiletUret();

        bilet.MarkAsExpired();

        bilet.Status.Should().Be(TicketStatus.Expired);
    }

    /// <remarks>
    /// MarkAsExpired de idempotent: yalnizca Active bileti
    /// etkiliyor, digerlerinde sessizce geciyor.
    ///
    /// Gerekce aynisi: bu metodu "gecmis etkinliklerin biletlerini
    /// kapat" arka plan isi cagiriyor ve o is TOPLU calisiyor.
    /// Kullanilmis bir bilete rastladiginda patlasaydi, tek bir
    /// bilet yuzunden partinin tamami durur ve digerleri hic
    /// islenmezdi.
    ///
    /// Onemli olan sonuc: kullanilmis bilet Used KALIYOR. Expired
    /// olsaydi "kisi etkinlige girdi mi?" sorusunun cevabi
    /// kaybolurdu.
    /// </remarks>
    [Fact]
    public void Kullanilmis_bilet_suresi_dolmus_isaretlenmemeli()
    {
        var bilet = BiletUret();
        bilet.MarkAsUsed(Simdi);

        bilet.MarkAsExpired();

        bilet.Status.Should().Be(TicketStatus.Used, "kullanim bilgisi kaybolmamali");
    }
}
