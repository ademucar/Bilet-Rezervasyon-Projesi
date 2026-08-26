using FluentAssertions;
using Ticketing.Domain.Common;
using Ticketing.Domain.Entities;
using Ticketing.Domain.Enums;
using Ticketing.Domain.Events;
using Ticketing.Domain.ValueObjects;

namespace Ticketing.UnitTests.Domain;

/// <summary>
/// PDF Sprint 8'deki odeme is kurallarinin testleri.
/// </summary>
public class PaymentTests
{
    private static readonly DateTimeOffset Simdi = TestVeriKurucu.Simdi;

    private static Payment Odeme(decimal tutar = 500m)
        => Payment.Create(Guid.CreateVersion7(), new Money(tutar, "TRY"), "MockPaymentProvider");

    private static Payment BasariliOdeme(decimal tutar = 500m)
    {
        var odeme = Odeme(tutar);
        odeme.StartProcessing("REF-123");
        odeme.Complete("REF-123", Simdi);

        return odeme;
    }

    // ---------------------------------------------------------------
    // Olusturma
    // ---------------------------------------------------------------

    [Fact]
    public void Create_YeniOdeme_PendingDurumundaBaslamali()
    {
        Odeme().Status.Should().Be(PaymentStatus.Pending);
    }

    [Fact]
    public void Create_SifirTutar_HataFirlatmali()
    {
        var eylem = () => Payment.Create(Guid.CreateVersion7(), Money.Zero("TRY"), "Mock");

        eylem.Should().Throw<DomainException>()
             .Which.ErrorCode.Should().Be("payment.invalid_amount");
    }

    // ---------------------------------------------------------------
    // IDEMPOTENCY -- PDF: "Callback islemleri idempotent olmalidir."
    // ---------------------------------------------------------------

    [Fact]
    public void Complete_IkinciKezCagrilirsa_FalseDonmeliVeHataFirlatMAMALI()
    {
        // ===============================================================
        // Odeme saglayicilari callback'i BIRDEN FAZLA KEZ gonderir.
        // Bu bir hata degil, normal davranistir: saglayici cevap
        // alamadigini dusunurse tekrar dener.
        //
        // Hata firlatsaydik saglayici "callback basarisiz" deyip tekrar
        // tekrar denerdi -- sonsuz dongu.
        //
        // false donuyoruz ki cagiran taraf "yeni bir sey olmadi,
        // TEKRAR BILET URETME" diye anlasin. Bu donus degeri, ayni
        // rezervasyon icin iki kez bilet uretilmesini engelliyor.
        // ===============================================================
        var odeme = Odeme();
        odeme.StartProcessing("REF-1");

        var ilkSonuc = odeme.Complete("REF-1", Simdi);
        var ikinciSonuc = odeme.Complete("REF-1", Simdi.AddSeconds(5));

        ilkSonuc.Should().BeTrue("ilk callback gercek islemi yapar");
        ikinciSonuc.Should().BeFalse("ikinci callback yok sayilmali");
        odeme.Status.Should().Be(PaymentStatus.Successful);
    }

    [Fact]
    public void Fail_IkinciKezCagrilirsa_HataFirlatmamali()
    {
        var odeme = Odeme();
        odeme.StartProcessing();
        odeme.Fail("kart limiti yetersiz", Simdi, Guid.CreateVersion7());

        var eylem = () => odeme.Fail("tekrar", Simdi, Guid.CreateVersion7());

        eylem.Should().NotThrow();
        odeme.Status.Should().Be(PaymentStatus.Failed);
    }

    // ---------------------------------------------------------------
    // Durum makinesi
    // ---------------------------------------------------------------

    [Fact]
    public void Complete_ProcessingOlmadanDogrudan_HataFirlatmali()
    {
        // Pending -> Successful diye bir yol YOK.
        // Saglayiciya hic gitmeden odeme basarili olamaz.
        var odeme = Odeme();

        var eylem = () => odeme.Complete("REF", Simdi);

        eylem.Should().Throw<DomainException>()
             .Which.ErrorCode.Should().Be("payment.invalid_transition");
    }

    [Fact]
    public void Fail_BasariliOdemedenSonra_HataFirlatmali()
    {
        // Successful -> Failed yolu YOK. Basarili bir odeme sonradan
        // "basarisiz" olamaz; olsa olsa IADE edilir.
        var odeme = BasariliOdeme();

        var eylem = () => odeme.Fail("gec gelen hata", Simdi, Guid.CreateVersion7());

        eylem.Should().Throw<DomainException>()
             .Which.ErrorCode.Should().Be("payment.invalid_transition");
    }

    [Fact]
    public void Fail_OdemeBasarisizOlayiFirlatmali()
    {
        var odeme = Odeme();
        odeme.StartProcessing();

        odeme.Fail("kart limiti", Simdi, Guid.CreateVersion7());

        odeme.DomainEvents.Should().ContainItemsAssignableTo<PaymentFailedDomainEvent>();
    }

    // ---------------------------------------------------------------
    // IADE
    // ---------------------------------------------------------------

    [Fact]
    public void Refund_TamIade_DurumuRefundedYapmali()
    {
        var odeme = BasariliOdeme(500m);

        odeme.Refund(new Money(500m, "TRY"));

        odeme.Status.Should().Be(PaymentStatus.Refunded);
        odeme.RefundedAmount.Amount.Should().Be(500m);
        odeme.GetRefundableAmount().Amount.Should().Be(0m);
    }

    [Fact]
    public void Refund_KismiIade_DurumSuccessfulKalmali()
    {
        // Kismi iadede odeme HALA gecerli, sadece bir kismi geri donmus.
        // Durumu Refunded yapsaydik "bu odeme tamamen iade edildi" gibi
        // gorunurdu ve raporlar yanlis cikardi.
        var odeme = BasariliOdeme(500m);

        odeme.Refund(new Money(200m, "TRY"));

        odeme.Status.Should().Be(PaymentStatus.Successful);
        odeme.RefundedAmount.Amount.Should().Be(200m);
        odeme.GetRefundableAmount().Amount.Should().Be(300m);
    }

    [Fact]
    public void Refund_IkiKismiIadeToplaminaTamIade_RefundedYapmali()
    {
        var odeme = BasariliOdeme(500m);

        odeme.Refund(new Money(200m, "TRY"));
        odeme.Refund(new Money(300m, "TRY"));

        odeme.RefundedAmount.Amount.Should().Be(500m);
        odeme.Status.Should().Be(PaymentStatus.Refunded);
    }

    [Fact]
    public void Refund_OdenendenFazlasi_HataFirlatmali()
    {
        // ===============================================================
        // Bu kontrol gercek para kaybini engelliyor.
        //
        // Senaryo: iade callback'i iki kez gelirse ve kontrol olmasaydi
        // kullaniciya IKI KAT para gonderirdik. Bu, geri alinmasi cok
        // zor bir hatadir.
        // ===============================================================
        var odeme = BasariliOdeme(500m);
        odeme.Refund(new Money(400m, "TRY"));

        var eylem = () => odeme.Refund(new Money(200m, "TRY"));   // toplam 600 > 500

        eylem.Should().Throw<DomainException>()
             .Which.ErrorCode.Should().Be("payment.refund_exceeds_amount");
    }

    [Fact]
    public void Refund_BasarisizOdeme_HataFirlatmali()
    {
        // Hic tahsil edilmemis parayi iade edemezsin.
        var odeme = Odeme();
        odeme.StartProcessing();
        odeme.Fail("hata", Simdi, Guid.CreateVersion7());

        var eylem = () => odeme.Refund(new Money(100m, "TRY"));

        eylem.Should().Throw<DomainException>()
             .Which.ErrorCode.Should().Be("payment.not_refundable");
    }

    // ---------------------------------------------------------------
    // Denetim izi
    // ---------------------------------------------------------------

    [Fact]
    public void Transactions_HerAdimIcinKayitOlusturmali()
    {
        // PaymentTransactions bir DENETIM IZIDIR: "bu odemede ne oldu?"
        // sorusunun cevabi burada. Payment'ta tek Status alani var;
        // bu dort adimi orada tutamayiz.
        var odeme = Odeme(500m);

        odeme.StartProcessing("REF-1");          // 1: Processing
        odeme.Complete("REF-1", Simdi);          // 2: Successful
        odeme.Refund(new Money(200m, "TRY"));    // 3: Refund
        odeme.Refund(new Money(300m, "TRY"));    // 4: Refund

        odeme.Transactions.Should().HaveCount(4);
        odeme.Transactions.Count(t => t.Type == PaymentTransactionType.Refund).Should().Be(2);
    }
}
