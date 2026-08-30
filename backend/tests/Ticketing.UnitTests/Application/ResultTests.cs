using FluentAssertions;
using Ticketing.Application.Common.Pagination;
using Ticketing.Application.Common.Results;

namespace Ticketing.UnitTests.Application;

public class ResultTests
{
    [Fact]
    public void Success_HataIcermemeli()
    {
        var sonuc = Result.Success();

        sonuc.IsSuccess.Should().BeTrue();
        sonuc.IsFailure.Should().BeFalse();
        sonuc.Error.Should().Be(Error.None);
    }

    [Fact]
    public void Failure_HatayiTasimali()
    {
        var hata = Error.NotFound("event.not_found", "Etkinlik bulunamadi.");

        var sonuc = Result.Failure(hata);

        sonuc.IsFailure.Should().BeTrue();
        sonuc.Error.Code.Should().Be("event.not_found");
        sonuc.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public void BasarisizSonucunDegerineErisim_ExceptionFirlatmali()
    {
        // null donmuyoruz. Cunku null donseydim cagiran kisi onu gecerli
        // bir deger sanip devam eder ve hata cok ilerideki bir noktada,
        // hicbir sey anlatmayan bir NullReferenceException olarak patlardi.
        //
        // Burada patlarsa mesaj net: "sonucu kontrol etmeden degere eristin".
        var sonuc = Result.Failure<string>(Error.NotFound("x", "yok"));

        var eylem = () => sonuc.Value;

        eylem.Should().Throw<InvalidOperationException>()
             .WithMessage("*Başarısız bir sonucun değerine erişilemez*");
    }

    [Fact]
    public void TryGetValue_BasariliSonucta_DegeriVermeli()
    {
        var sonuc = Result.Success("bilet-123");

        sonuc.TryGetValue(out var deger).Should().BeTrue();
        deger.Should().Be("bilet-123");
    }

    [Fact]
    public void TryGetValue_BasarisizSonucta_FalseDonmeli()
    {
        var sonuc = Result.Failure<string>(Error.Conflict("seat.locked", "dolu"));

        sonuc.TryGetValue(out var deger).Should().BeFalse();
        deger.Should().BeNull();
    }

    [Fact]
    public void OrtukDonusum_DegerdenSonucaCevirmeli()
    {
        // Handler'larda "return Result.Success(user)" yerine
        // sadece "return user" yazabilmemizi saglayan kolaylik.
        Result<int> sonuc = 42;

        sonuc.IsSuccess.Should().BeTrue();
        sonuc.Value.Should().Be(42);
    }

    [Fact]
    public void TutarsizSonuc_BasariliAmaHatali_ExceptionFirlatmali()
    {
        // Bu bir PROGRAMLAMA hatasidir, kullanici hatasi degil.
        // Result donup sessizce devam etmek yerine dogrudan patlatiyoruz ki
        // hatali kullanim uretime cikmadan once, ilk testte ortaya ciksin.
        var eylem = () => Result.Failure(Error.None);

        eylem.Should().Throw<InvalidOperationException>();
    }
}

public class PagedResultTests
{
    [Theory]
    [InlineData(0, 10, 0)]
    [InlineData(1, 10, 1)]
    [InlineData(10, 10, 1)]
    [InlineData(11, 10, 2)]
    [InlineData(25, 10, 3)]
    [InlineData(100, 20, 5)]
    public void TotalPages_DogruHesaplanmali(int toplamKayit, int sayfaBoyutu, int beklenenSayfa)
    {
        // Tam sayi aritmetigi: (a + b - 1) / b
        // double'a cevirip Math.Ceiling kullanmak yerine bunu tercih ettim:
        // hem daha hizli hem de cok buyuk sayilarda hassasiyet kaybi yok.
        //
        // Sinir degerleri (10 ve 11 kayit, 10 sayfa boyutu) ozellikle test
        // ediliyor: off-by-one hatalarinin en sik ciktigi yer burasi.
        var sonuc = PagedResult<string>.Create([], 1, sayfaBoyutu, toplamKayit);

        sonuc.TotalPages.Should().Be(beklenenSayfa);
    }

    [Fact]
    public void IlkSayfa_OncekiSayfaOlmamali()
    {
        var sonuc = PagedResult<string>.Create([], pageNumber: 1, pageSize: 10, totalCount: 50);

        sonuc.HasPreviousPage.Should().BeFalse();
        sonuc.HasNextPage.Should().BeTrue();
    }

    [Fact]
    public void SonSayfa_SonrakiSayfaOlmamali()
    {
        var sonuc = PagedResult<string>.Create([], pageNumber: 5, pageSize: 10, totalCount: 50);

        sonuc.HasPreviousPage.Should().BeTrue();
        sonuc.HasNextPage.Should().BeFalse();
    }
}

public class PaginationRequestTests
{
    private sealed record TestIstegi : PaginationRequest;

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(1, 1)]
    [InlineData(7, 7)]
    public void PageNumber_GecersizDeger_BireDuzeltilmeli(int girdi, int beklenen)
    {
        new TestIstegi { PageNumber = girdi }.PageNumber.Should().Be(beklenen);
    }

    [Fact]
    public void PageSize_UstSiniriAsamaz()
    {
        // Bu test bir guvenlik kontrolu
        //
        // Ust sinir olmasaydi bir kullanici
        //     GET /api/v1/events?pageSize=999999999
        // isteyebilirdi. Sunucu tum tabloyu bellege yukler ve coker.
        //
        // Kod yazmayi bilen herkesin yapabilecegi en basit DoS saldirisi.
        // Siniri SUNUCU tarafinda zorlamak sart -- frontend'in dogru
        // deger gonderecegine guvenemem.
        var istek = new TestIstegi { PageSize = 999_999_999 };

        istek.PageSize.Should().Be(PaginationRequest.MaxPageSize);
    }

    [Fact]
    public void PageSize_SifirVeyaNegatif_VarsayilanaDonmeli()
    {
        new TestIstegi { PageSize = 0 }.PageSize.Should().Be(PaginationRequest.DefaultPageSize);
        new TestIstegi { PageSize = -10 }.PageSize.Should().Be(PaginationRequest.DefaultPageSize);
    }

    [Theory]
    [InlineData(1, 20, 0)]
    [InlineData(2, 20, 20)]
    [InlineData(3, 15, 30)]
    public void Skip_DogruHesaplanmali(int sayfa, int boyut, int beklenenSkip)
    {
        // (pageNumber - 1) * pageSize hesabini tek yerde tutuyorum.
        // Her sorguda elle yazsaydik birinde mutlaka -1'i unuturdum
        // ve ilk sayfa atlanirdi -- fark edilmesi zor bir hata.
        new TestIstegi { PageNumber = sayfa, PageSize = boyut }.Skip.Should().Be(beklenenSkip);
    }
}
