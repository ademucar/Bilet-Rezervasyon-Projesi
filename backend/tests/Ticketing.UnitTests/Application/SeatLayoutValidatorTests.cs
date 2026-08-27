using FluentAssertions;
using Ticketing.Application.Features.Halls;
using Ticketing.Application.Features.SeatLayouts;
using Ticketing.Application.Features.Venues;

namespace Ticketing.UnitTests.Application;

/// <summary>
/// Validator testleri.
///
/// Bu testler veritabani GEREKTIRMIYOR -- validator'lar saf fonksiyon
/// gibi calisiyor. Bu yuzden milisaniyeler icinde kosuyorlar ve her
/// derlemede calistirilabiliyorlar.
///
/// Handler testleri ise gercek veritabani gerektiriyor ve Sprint 17'de
/// Testcontainers ile integration test olarak yazilacak (PDF gereği).
/// Mock'lanmis DbContext ile handler testi yazmak, gercek sorgulari
/// ve kisitlari dogrulamadigi icin yanlis guven verir.
/// </summary>
public class GenerateSeatsValidatorTests
{
    private readonly GenerateSeatsCommandValidator _validator = new();

    private static GenerateSeatsCommand Komut(int rowCount, int seatsPerRow, IReadOnlyList<string>? labels = null)
        => new(Guid.CreateVersion7(), Guid.CreateVersion7(), rowCount, seatsPerRow, labels);

    [Fact]
    public void GecerliGirdi_KabulEdilmeli()
    {
        _validator.Validate(Komut(10, 20)).IsValid.Should().BeTrue();
    }

    // ---------------------------------------------------------------
    // DoS KORUMASI -- bu testin konusu bir guvenlik siniri
    // ---------------------------------------------------------------

    [Fact]
    public void CokBuyukKoltukSayisi_ReddedilmeliDoSKorumasi()
    {
        // ===============================================================
        // Sinir olmasaydi bu istek 10 MILYAR koltuk uretmeye calisirdi.
        // Sunucu bellegi tukenir, veritabani kilitlenir, sistem coker.
        //
        // Kod yazmayi bilen herkesin gonderebilecegi tek bir JSON
        // istegiyle servisi disari birakma saldirisi.
        // ===============================================================
        var sonuc = _validator.Validate(Komut(100_000, 100_000));

        sonuc.IsValid.Should().BeFalse();
    }

    [Fact]
    public void ToplamSinirinTamUstunde_Reddedilmeli()
    {
        // 500 x 41 = 20.500 > 20.000
        // Tek tek bakildiginda her iki deger de gecerli (500 ve 41);
        // ihlal ancak CARPIMLARINDA ortaya cikiyor.
        //
        // Bu yuzden RuleFor(x => x) ile komutun TAMAMINI dogruluyorum.
        // Alan bazli kurallar bu tur "capraz alan" kisitlarini yakalayamaz.
        var sonuc = _validator.Validate(Komut(500, 41));

        sonuc.IsValid.Should().BeFalse();
        sonuc.Errors.Should().Contain(e => e.PropertyName == "SeatCount");
    }

    [Fact]
    public void ToplamSinirinTamAltinda_KabulEdilmeli()
    {
        // 500 x 40 = 20.000 -> tam sinirda, GECERLI olmali.
        //
        // Sinir degeri testi: kodda "<" mi "<=" mi yazdigimizi kontrol
        // ediyor. Off-by-one hatalari en sik burada olusur ve
        // "neden 20.000 koltuk uretemiyorum?" diye kullanici sikayeti
        // olarak geri doner.
        _validator.Validate(Komut(500, 40)).IsValid.Should().BeTrue();
    }

    // ---------------------------------------------------------------
    // Sira etiketleri
    // ---------------------------------------------------------------

    [Fact]
    public void EtiketSayisiSiraSayisiylaUyusmuyorsa_Reddedilmeli()
    {
        // Bu kontrol olmasaydi entity katmaninda IndexOutOfRangeException
        // alirdik -- kullaniciya hicbir sey anlatmayan bir 500 hatasi.
        var sonuc = _validator.Validate(Komut(3, 10, ["A", "B"]));

        sonuc.IsValid.Should().BeFalse();
    }

    [Fact]
    public void EtiketSayisiEslesiyorsa_KabulEdilmeli()
    {
        _validator.Validate(Komut(3, 10, ["A", "B", "C"])).IsValid.Should().BeTrue();
    }

    [Fact]
    public void EtiketVerilmemisse_KabulEdilmeli()
    {
        // null gecerli: o zaman "1, 2, 3..." kullanilacak.
        _validator.Validate(Komut(3, 10, null)).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(10, 0)]
    [InlineData(-1, 10)]
    [InlineData(10, -5)]
    public void SifirVeyaNegatifDegerler_Reddedilmeli(int rowCount, int seatsPerRow)
    {
        _validator.Validate(Komut(rowCount, seatsPerRow)).IsValid.Should().BeFalse();
    }
}

public class SectionValidatorTests
{
    private readonly AddSectionCommandValidator _validator = new();

    private static AddSectionCommand Komut(string name, int order = 1, string? color = null)
        => new(Guid.CreateVersion7(), name, order, color);

    [Theory]
    [InlineData("#E63946")]
    [InlineData("#000000")]
    [InlineData("#ffffff")]
    public void GecerliRenkKodu_KabulEdilmeli(string color)
    {
        _validator.Validate(Komut("Orta Blok", color: color)).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("E63946")]     // # yok
    [InlineData("#E639")]      // eksik basamak
    [InlineData("#GGGGGG")]    // gecersiz hex
    [InlineData("kirmizi")]    // renk adi
    public void GecersizRenkKodu_Reddedilmeli(string color)
    {
        // Renk dogrulamasi onemsiz gorunur ama degil: frontend bu degeri
        // dogrudan CSS'e yaziyor. Dogrulanmamis bir metin, koltuk
        // haritasinin gorunumunu bozar veya CSS enjeksiyonuna zemin
        // hazirlar.
        _validator.Validate(Komut("Orta Blok", color: color)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void RenkBelirtilmemisse_KabulEdilmeli()
    {
        _validator.Validate(Komut("Orta Blok")).IsValid.Should().BeTrue();
    }

    [Fact]
    public void BosBolumAdi_Reddedilmeli()
    {
        _validator.Validate(Komut("   ")).IsValid.Should().BeFalse();
    }

    [Fact]
    public void NegatifGosterimSirasi_Reddedilmeli()
    {
        _validator.Validate(Komut("Balkon", order: -1)).IsValid.Should().BeFalse();
    }
}

public class HallValidatorTests
{
    private readonly CreateHallCommandValidator _validator = new();

    private static CreateHallCommand Komut(int capacity, string name = "Salon A")
        => new(Guid.CreateVersion7(), name, capacity);

    [Fact]
    public void MakulKapasite_KabulEdilmeli()
    {
        _validator.Validate(Komut(1500)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void SifirKapasite_Reddedilmeli()
    {
        _validator.Validate(Komut(0)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void AsiriBuyukKapasite_Reddedilmeli()
    {
        // Dunyanin en buyuk stadyumu ~150.000 kisilik.
        // 2 milyar kapasiteli bir "salon" yazim hatasidir ve koltuk
        // uretiminde bellegi tuketir.
        _validator.Validate(Komut(int.MaxValue)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void TamSinirdaKapasite_KabulEdilmeli()
    {
        _validator.Validate(Komut(200_000)).IsValid.Should().BeTrue();
    }
}

public class VenueValidatorTests
{
    private readonly CreateVenueCommandValidator _validator = new();

    private static CreateVenueCommand Komut(decimal? lat = null, decimal? lng = null)
        => new("Zorlu PSM", "Levazim Mah.", Guid.CreateVersion7(), lat, lng);

    [Fact]
    public void KoordinatsizMekan_KabulEdilmeli()
    {
        _validator.Validate(Komut()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void GecerliKoordinat_KabulEdilmeli()
    {
        // Istanbul
        _validator.Validate(Komut(41.0082m, 28.9784m)).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(91, 0)]
    [InlineData(-91, 0)]
    [InlineData(0, 181)]
    [InlineData(0, -181)]
    public void AralikDisiKoordinat_Reddedilmeli(int lat, int lng)
    {
        // Bu kontrol olmasaydi mekan haritada okyanusun ortasinda
        // gorunurdu ve kimse sebebini anlamazdi. En sik sebep:
        // enlem ve boylamin yer degistirmis olmasi.
        _validator.Validate(Komut(lat, lng)).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(90, 180)]
    [InlineData(-90, -180)]
    public void TamSinirKoordinatlari_KabulEdilmeli(int lat, int lng)
    {
        _validator.Validate(Komut(lat, lng)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void SehirSecilmemisse_Reddedilmeli()
    {
        var komut = new CreateVenueCommand("Zorlu PSM", "Adres", Guid.Empty, null, null);

        _validator.Validate(komut).IsValid.Should().BeFalse();
    }
}
