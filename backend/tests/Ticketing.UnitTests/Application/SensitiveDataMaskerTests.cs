using FluentAssertions;
using Ticketing.Application.Common.Security;

namespace Ticketing.UnitTests.Application;

/// <summary>
/// PDF Sprint 15: "Hassas veri maskeleme" testleri.
/// </summary>
public class SensitiveDataMaskerTests
{
    // JWT
    //
    // Loga dusen bir JWT, suresi dolana kadar o kullanicinin hesabina
    // giris yetkisidir. Maskeleme burada en kritik.
    [Fact]
    public void Jwt_maskelenir()
    {
        const string Girdi =
            "Authorization basarisiz: eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjMifQ.abc-def_123";

        var sonuc = SensitiveDataMasker.Mask(Girdi);

        sonuc.Should().NotContain("eyJhbGciOiJIUzI1NiJ9");
        sonuc.Should().Contain("MASKELENDI");

        // Cevredeki metin KORUNMALI: maskeleme, logu okunamaz hale
        // getirmemeli. Yoksa gelistiriciler maskelemeyi kapatir.
        sonuc.Should().StartWith("Authorization basarisiz:");
    }

    [Theory]
    [InlineData("{\"password\":\"Gizli123!\"}", "Gizli123!")]
    [InlineData("{\"currentPassword\":\"Eski123!\"}", "Eski123!")]
    [InlineData("{\"refreshToken\":\"abc123xyz\"}", "abc123xyz")]
    [InlineData("{\"apiKey\":\"sk-canli-anahtar\"}", "sk-canli-anahtar")]
    [InlineData("{\"secret\":\"cok-gizli\"}", "cok-gizli")]
    public void Json_icindeki_sifre_alanlari_maskelenir(string girdi, string gizliDeger)
    {
        var sonuc = SensitiveDataMasker.Mask(girdi);

        sonuc.Should().NotContain(gizliDeger);
    }

    // ALAN ADI KORUNUYOR, DEGERI GIDIYOR
    //
    // Alan adini da silseydim logdan "hangi alan vardi" bilgisi
    // kaybolur ve hata ayiklamak imkansizlasirdi. Amac logu
    // yok etmek degil, ZARARSIZ hale getirmek.
    [Fact]
    public void Maskelemede_alan_adi_korunur()
    {
        var sonuc = SensitiveDataMasker.Mask("{\"password\":\"Gizli123!\"}");

        sonuc.Should().Contain("password");
        sonuc.Should().NotContain("Gizli123!");
    }

    [Fact]
    public void Kart_numarasi_maskelenir()
    {
        var sonuc = SensitiveDataMasker.Mask("Odeme reddedildi: 4242424242424242");

        sonuc.Should().NotContain("4242424242424242");
    }

    [Fact]
    public void Zararsiz_metin_degismez()
    {
        const string Girdi = "Rezervasyon 10 dakika icinde suresi doldu.";

        SensitiveDataMasker.Mask(Girdi).Should().Be(Girdi);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Bos_girdi_patlatmaz(string? girdi)
    {
        SensitiveDataMasker.Mask(girdi).Should().BeEmpty();
    }

    // E-POSTA: KISMEN MASKELENIYOR
    [Theory]
    [InlineData("adem@ornek.com", "ade***@ornek.com")]
    [InlineData("a@ornek.com", "a***@ornek.com")]
    [InlineData("ab@ornek.com", "a***@ornek.com")]
    public void Eposta_kismen_maskelenir(string girdi, string beklenen)
    {
        SensitiveDataMasker.MaskEmail(girdi).Should().Be(beklenen);
    }

    // Alan adi KORUNUYOR: destek ekibi "kurumsal müşteri mi?"
    // sorusunu cevaplayabilmeli.
    [Fact]
    public void Eposta_alan_adi_korunur()
    {
        SensitiveDataMasker.MaskEmail("adem@sirket.com.tr").Should().EndWith("@sirket.com.tr");
    }

    // Gecerli bir e-posta degilse kismi maskeleme mantigi calismaz.
    // O durumda TAMAMEN maskeliyorum -- yanlislikla tamamini
    // loglamaktansa hicbir sey loglamak daha guvenli.
    [Fact]
    public void Gecersiz_eposta_tamamen_maskelenir()
    {
        SensitiveDataMasker.MaskEmail("bu-bir-eposta-degil").Should().Be("***MASKELENDI***");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Bos_eposta_tire_doner(string? girdi)
    {
        SensitiveDataMasker.MaskEmail(girdi).Should().Be("-");
    }
}
