using FluentAssertions;
using Ticketing.Domain.Common;
using Ticketing.Domain.ValueObjects;

namespace Ticketing.UnitTests.Domain;

/// <summary>
/// Money value object testleri.
///
/// Isimlendirme: Metot_Senaryo_BeklenenSonuc
/// Test kirmizi yandiginda ismin tek basina ne oldugunu anlatmasi gerekir;
/// kodu acmak zorunda kalmamalisin.
/// </summary>
public class MoneyTests
{
    private const string TRY = "TRY";

    // Olusturma kurallari

    [Fact]
    public void Ctor_GecerliDegerlerle_TutariVeParaBirimiSaklamali()
    {
        var money = new Money(150.50m, TRY);

        money.Amount.Should().Be(150.50m);
        money.Currency.Should().Be(TRY);
    }

    [Fact]
    public void Ctor_KucukHarfliParaBirimi_BuyukHarfeCevrilmeli()
    {
        // Neden onemli? Frontend "try" gonderirse, veritabaninda "try" ve
        // "TRY" diye iki farkli deger olusur. Karsilastirmalar bozulur,
        // raporlar iki ayri para birimi gorur. Normalizasyonu tek yerde
        // (yapicida) yapmak, 50 ayri yerde ToUpper() yazmaktan iyidir.
        var money = new Money(10m, "try");

        money.Currency.Should().Be("TRY");
    }

    [Fact]
    public void Ctor_NegatifTutar_DomainExceptionFirlatmali()
    {
        var eylem = () => new Money(-1m, TRY);

        eylem.Should().Throw<DomainException>()
             .Which.ErrorCode.Should().Be("money.negative");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("TL")]      // 2 harf -- ISO 4217 degil
    [InlineData("TURK")]    // 4 harf
    public void Ctor_GecersizParaBirimi_DomainExceptionFirlatmali(string currency)
    {
        var eylem = () => new Money(10m, currency);

        eylem.Should().Throw<DomainException>()
             .Which.ErrorCode.Should().Be("money.invalid_currency");
    }

    // Yuvarlama -- bu testler projenin para dogrulugunu koruyor

    [Theory]
    [InlineData(2.125, 2.12)]   // 2 cift -> asagi
    [InlineData(2.135, 2.14)]   // 4 cift -> yukari
    [InlineData(2.145, 2.14)]   // 4 cift -> asagi
    [InlineData(10.999, 11.00)]
    public void Ctor_IkiBasamaktanFazlaOndalik_BankersRoundingUygulanmali(
        decimal girdi, decimal beklenen)
    {
        var money = new Money(girdi, TRY);

        money.Amount.Should().Be(beklenen);
    }

    [Fact]
    public void Toplama_CokSayidaIslemde_FloatingPointHatasiOlusturmamali()
    {
        // Bu test, decimal kullanmamizin SEBEBINI kanitliyor.
        // Ayni donguyu double ile yapsaydin sonuc 0.30000000000000004
        // gibi bir sey cikardi ve bu test kirmizi yanardi.
        var toplam = Money.Zero(TRY);

        for (var i = 0; i < 10; i++)
        {
            toplam += new Money(0.10m, TRY);
        }

        toplam.Amount.Should().Be(1.00m);
    }

    // Aritmetik

    [Fact]
    public void Toplama_AyniParaBirimi_DogruSonucVermeli()
    {
        var sonuc = new Money(100m, TRY) + new Money(50.25m, TRY);

        sonuc.Amount.Should().Be(150.25m);
        sonuc.Currency.Should().Be(TRY);
    }

    [Fact]
    public void Toplama_FarkliParaBirimi_DomainExceptionFirlatmali()
    {
        var eylem = () => new Money(100m, TRY) + new Money(50m, "USD");

        eylem.Should().Throw<DomainException>()
             .Which.ErrorCode.Should().Be("money.currency_mismatch");
    }

    [Fact]
    public void Carpma_BiletAdediyle_ToplamTutariVermeli()
    {
        // Gercek senaryo: 4 adet 250 TL'lik bilet
        var birimFiyat = new Money(250m, TRY);

        var toplam = birimFiyat * 4;

        toplam.Amount.Should().Be(1000m);
    }

    [Fact]
    public void Cikarma_SonucNegatifOlacaksa_DomainExceptionFirlatmali()
    {
        // Bu kural kasitli: bir odemeden odenenden fazlasini iade edemezsin.
        // Kurali Money'nin icine koydugum icin, iade mantigini yazan kisi
        // bu kontrolu unutamaz.
        var eylem = () => new Money(50m, TRY) - new Money(100m, TRY);

        eylem.Should().Throw<DomainException>()
             .Which.ErrorCode.Should().Be("money.negative");
    }

    // Deger esitligi -- record'un bana bedavaya verdigi davranis

    [Fact]
    public void Esitlik_AyniTutarVeParaBirimi_EsitSayilmali()
    {
        var a = new Money(100m, TRY);
        var b = new Money(100m, TRY);

        // Iki ayri nesne ama ayni deger. Class olsaydi bu test kirmizi yanardi
        // cunku class'lar varsayilan olarak referans esitligi kullanir.
        a.Should().Be(b);
        (a == b).Should().BeTrue();
    }

    [Fact]
    public void Esitlik_AyniTutarFarkliParaBirimi_EsitOlmamali()
    {
        var lira = new Money(100m, TRY);
        var dolar = new Money(100m, "USD");

        lira.Should().NotBe(dolar);
    }

    [Fact]
    public void ToString_KulturdenBagimsizFormatVermeli()
    {
        // Turkce kulturde ondalik ayraci VIRGULDUR (150,50).
        // Log'larin sunucunun bolge ayarina gore degismesini istemiyorum;
        // log analiz araclari bunu ayirt edemez.
        var money = new Money(150.5m, TRY);

        money.ToString().Should().Be("150.50 TRY");
    }
}
