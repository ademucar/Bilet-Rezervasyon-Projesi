using FluentAssertions;
using Ticketing.Domain.Common;
using Ticketing.Domain.ValueObjects;

namespace Ticketing.UnitTests.Domain;

/// <summary>
/// İade politikasi -- PDF Sprint 1, soru 10:
///   7 gunden fazla  -> %100
///   48 saat - 7 gun -> %50
///   48 saatten az   -> iade yok
/// </summary>
/// <remarks>
/// Bu testleri bilet iptalini yazarken ekledim ve o sirada sunu fark
/// ettim: CancellationPolicy.CalculateRefundPercentage HICBIR YERDEN
/// cagrilmiyordu ve tek bir testi bile yoktu. Yani projenin "iade
/// politikasi" diye anlattigi sey, 19 sprint boyunca hic calismamis.
///
/// Var olan iade ucu (POST /payments/{id}/refund) tutari CAGIRANDAN
/// aliyordu -- yani politika degil, adminin yazdigi sayi gecerliydi.
/// </remarks>
public class CancellationPolicyTests
{
    private static readonly DateTimeOffset Etkinlik = new(2026, 6, 1, 20, 0, 0, TimeSpan.Zero);

    private static readonly CancellationPolicy Varsayilan = CancellationPolicy.Default;

    // ---- Esiklerin ic tarafi ----

    [Fact]
    public void YediGundenFazlaKala_TamIade()
    {
        var iptal = Etkinlik.AddDays(-8);

        Varsayilan.CalculateRefundPercentage(Etkinlik, iptal).Should().Be(100);
    }

    [Fact]
    public void UcGunKala_YarimIade()
    {
        // 72 saat: 48'in ustunde, 168'in altinda.
        var iptal = Etkinlik.AddHours(-72);

        Varsayilan.CalculateRefundPercentage(Etkinlik, iptal).Should().Be(50);
    }

    [Fact]
    public void BirGunKala_IadeYok()
    {
        var iptal = Etkinlik.AddHours(-24);

        Varsayilan.CalculateRefundPercentage(Etkinlik, iptal).Should().Be(0);
    }

    // ---- Esiklerin TAM uzeri ----
    //
    // Bu ikisi asil onemli testler. Kod "buyuktur" diyor, "buyuk
    // esittir" demiyor; yani esigin tam uzerinde alt dilime dusuyor.
    // Bunu yazili hale getirmezsem, ileride biri ">" yerine ">="
    // yazdiginda hicbir test kirilmaz ve musteri 168. saatte tam iade
    // beklerken yarim alir.

    [Fact]
    public void TamYediGunKala_YarimIade_CunkuEsikDahilDegil()
    {
        var iptal = Etkinlik.AddHours(-168);

        Varsayilan.CalculateRefundPercentage(Etkinlik, iptal).Should().Be(50);
    }

    [Fact]
    public void TamKirkSekizSaatKala_IadeYok_CunkuEsikDahilDegil()
    {
        var iptal = Etkinlik.AddHours(-48);

        Varsayilan.CalculateRefundPercentage(Etkinlik, iptal).Should().Be(0);
    }

    [Fact]
    public void EtkinlikBasladiktanSonra_IadeYok()
    {
        // Negatif kalan sure. Uygulama katmani bu durumu zaten
        // engelliyor (etkinlik basladiysa iptal kabul edilmiyor) ama
        // politikanin kendisi de dogru cevap vermeli.
        var iptal = Etkinlik.AddHours(1);

        Varsayilan.CalculateRefundPercentage(Etkinlik, iptal).Should().Be(0);
    }

    // ---- Ozel politika ----

    [Fact]
    public void OrganizatorunPolitikasi_KendiOranlariniUygulamali()
    {
        // 14 gun / 24 saat / %25
        var politika = CancellationPolicy.Create(336, 24, 25);

        politika.CalculateRefundPercentage(Etkinlik, Etkinlik.AddDays(-20)).Should().Be(100);
        politika.CalculateRefundPercentage(Etkinlik, Etkinlik.AddDays(-5)).Should().Be(25);
        politika.CalculateRefundPercentage(Etkinlik, Etkinlik.AddHours(-12)).Should().Be(0);
    }

    [Fact]
    public void TersEsikler_HataFirlatmali()
    {
        // "48 saatten fazlaysa tam iade, 168 saatten fazlaysa yarim"
        // anlamsiz olurdu: kullanici ERKEN iptal ettigi icin
        // cezalandirilirdi.
        var eylem = () => CancellationPolicy.Create(48, 168, 50);

        eylem.Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void GecersizYuzde_HataFirlatmali(int yuzde)
    {
        var eylem = () => CancellationPolicy.Create(168, 48, yuzde);

        eylem.Should().Throw<DomainException>();
    }
}
