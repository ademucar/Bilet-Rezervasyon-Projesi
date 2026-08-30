using FluentAssertions;
using Ticketing.Domain.Entities;
using Ticketing.Domain.Common;

namespace Ticketing.UnitTests.Domain;

/// <summary>
/// PDF Sprint 17 birim testi maddesi: "Review Create".
/// </summary>
public class ReviewTests
{
    private static Review Yorum(int puan = 4, string metin = "Guzel etkinlikti.")
        => Review.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), puan, metin);

    [Fact]
    public void Gecerli_yorum_olusturulabilmeli()
    {
        var yorum = Yorum(5, "Harikaydi");

        yorum.Rating.Should().Be(5);
        yorum.Comment.Should().Be("Harikaydi");
        yorum.IsHidden.Should().BeFalse();
    }

    /// <remarks>
    /// Sinir degerler ayrica test ediliyor
    ///
    /// 1 ve 5 GECERLI, 0 ve 6 GECERSIZ.
    ///
    /// Sinirlari ayrica yazmamin sebebi: bu tur kontrollerdeki en
    /// yaygin hata "bir eksik/bir fazla" (off-by-one). Kod
    /// "rating &gt; 1 &amp;&amp; rating &lt; 5" diye yazilsaydi, 1 ve 5 puanlarin
    /// ikisi de reddedilirdi -- ve yalnizca ortadaki degerlerle test
    /// edilseydi bu hata gorunmezdi.
    /// </remarks>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public void Bir_ile_bes_arasi_puanlar_kabul_edilmeli(int puan)
    {
        var yorum = Yorum(puan);

        yorum.Rating.Should().Be(puan);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    [InlineData(100)]
    public void Aralik_disi_puan_reddedilmeli(int puan)
    {
        var eylem = () => Yorum(puan);

        eylem.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be("review.invalid_rating");
    }

    [Fact]
    public void Yorum_guncellenebilmeli()
    {
        var yorum = Yorum(2, "Begenmedim");

        yorum.Update(4, "Fikrimi degistirdim");

        yorum.Rating.Should().Be(4);
        yorum.Comment.Should().Be("Fikrimi degistirdim");
    }

    /// <remarks>
    /// Guncellemede de ayni puan kurali gecerli olmali.
    ///
    /// Dogrulamayi yalnizca Create'e koymak yaygin bir hata: kullanici
    /// once gecerli bir yorum yazip sonra guncelleme ile 99 puan
    /// verebilirdi ve ortalama puan bozulurdu.
    /// </remarks>
    [Fact]
    public void Guncellemede_de_puan_araligi_kontrol_edilmeli()
    {
        var yorum = Yorum();

        var eylem = () => yorum.Update(9, "Cok iyi");

        eylem.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be("review.invalid_rating");
    }

    // Moderasyon

    /// <remarks>
    /// Gizleme, silme degil
    ///
    /// Uygunsuz bir yorum gizleniyor ama KAYIT duruyor.
    ///
    /// Silseydim: kullanici "yorumum nerede?" diye sorunca elimde
    /// hicbir sey olmazdi ve moderasyon karari denetlenemezdi.
    /// Ayrica ayni kullanici tekrar yorum yazabilir hale gelirdi --
    /// oysa "her kullanici bir etkinlige bir yorum" kurali var.
    /// </remarks>
    [Fact]
    public void Gizlenen_yorum_kayitta_kalmali()
    {
        var yorum = Yorum();

        yorum.Hide("Uygunsuz icerik");

        yorum.IsHidden.Should().BeTrue();
        yorum.Comment.Should().NotBeNullOrEmpty("yorum metni silinmemeli");
    }

    [Fact]
    public void Gizlenen_yorum_tekrar_gorunur_yapilabilmeli()
    {
        var yorum = Yorum();
        yorum.Hide("Yanlislikla gizlendi");

        yorum.Unhide();

        yorum.IsHidden.Should().BeFalse();
    }
}
