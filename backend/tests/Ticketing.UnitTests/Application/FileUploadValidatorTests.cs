using FluentAssertions;
using Ticketing.Application.Common.Security;

namespace Ticketing.UnitTests.Application;

/// <summary>
/// PDF Sprint 15 dosya guvenligi kontrollerinin testleri.
/// </summary>
/// <remarks>
/// BU TESTLERIN COGU "OLUMSUZ" TEST -- BILINCLI
///
/// Guvenlik kodunda "dogru girdi kabul ediliyor mu?" sorusu kolay
/// olandir. Asil deger "YANLIS girdi REDDEDILIYOR mu?" sorusunda.
///
/// Bir dogrulayici, hicbir seyi reddetmezse de "gecerli dosyayi kabul
/// et" testini gecer. Yani yalnizca olumlu test yazmak, bozuk bir
/// dogrulayiciyi fark etmeden gecirebilir.
///
/// Her saldiri senaryosu icin ayri bir test yaziyorum ki ilerde biri
/// bir kontrolu kaldirdiginda HANGI korumanin kayboldugu test adindan
/// dogrudan okunabilsin.
/// </remarks>
public class FileUploadValidatorTests
{
    // Gercek dosya imzalari (magic number).
    private static readonly byte[] JpegBaslik = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0, 0, 0, 0, 0];
    private static readonly byte[] PngBaslik = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];
    private static readonly byte[] PdfBaslik = [0x25, 0x50, 0x44, 0x46, 0x2D, 0, 0, 0, 0, 0, 0, 0];

    // "MZ" ile baslar: Windows calistirilabilir dosyasinin imzasi.
    private static readonly byte[] ExeBaslik = [0x4D, 0x5A, 0x90, 0x00, 0, 0, 0, 0, 0, 0, 0, 0];

    [Fact]
    public void Gecerli_jpeg_kabul_edilir()
    {
        var sonuc = FileUploadValidator.Dogrula("afis.jpg", "image/jpeg", 1024, JpegBaslik);

        sonuc.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Gecerli_png_kabul_edilir()
    {
        var sonuc = FileUploadValidator.Dogrula("afis.png", "image/png", 1024, PngBaslik);

        sonuc.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Gecerli_pdf_kabul_edilir()
    {
        var sonuc = FileUploadValidator.Dogrula("bilet.pdf", "application/pdf", 1024, PdfBaslik);

        sonuc.IsSuccess.Should().BeTrue();
    }

    // SALDIRI 1: UZANTIYI DEGISTIRME
    //
    // Saldirgan "zararli.exe" dosyasini "afis.jpg" yapip yukluyor ve
    // Content-Type basligini da image/jpeg olarak elle yaziyor.
    //
    // Uzanti kontrolu GECER. MIME kontrolu de GECER. Yalnizca içerik
    // imzasi bu saldiriyi yakaliyor.
    //
    // Bu test, imza kontrolunun neden vazgecilmez oldugunun kanitidir:
    // ilk iki kontrolu de kullanici sagliyor.
    [Fact]
    public void Uzantisi_degistirilmis_exe_reddedilir()
    {
        var sonuc = FileUploadValidator.Dogrula("afis.jpg", "image/jpeg", 1024, ExeBaslik);

        sonuc.IsFailure.Should().BeTrue();
        sonuc.Error.Code.Should().Be("file.content_mismatch");
    }

    // SALDIRI 2: CIFT UZANTI
    //
    // "afis.jpg.exe" -- bazi sistemler ilk uzantiya bakar, isletim
    // sistemi ise SON uzantiyi calistirir.
    //
    // Path.GetExtension son uzantiyi doner (".exe"), o da beyaz
    // listede olmadigi icin reddediliyor.
    [Fact]
    public void Cift_uzantili_dosya_reddedilir()
    {
        var sonuc = FileUploadValidator.Dogrula("afis.jpg.exe", "image/jpeg", 1024, JpegBaslik);

        sonuc.IsFailure.Should().BeTrue();
        sonuc.Error.Code.Should().Be("file.type_not_allowed");
    }

    // SALDIRI 3: DIZIN GECISI (path traversal)
    //
    // "../../appsettings.json" gibi bir ad ile uygulama disina yazma
    // girisimi.
    //
    // Bu test dogrulamanin GECMESINI bekliyor -- cunku saldiri
    // reddedilerek degil, ETKISIZLESTIRILEREK cozuluyor: uretilen ad
    // Guid oldugu icin kullanicinin gonderdigi yol parcasi hicbir
    // yere yazilmiyor.
    //
    // Testin dogruladigi sey: donen adin icinde dizin ayirici YOK.
    [Theory]
    [InlineData("../../appsettings.json.jpg")]
    [InlineData("..\\..\\appsettings.json.jpg")]
    [InlineData("/etc/passwd.jpg")]
    public void Dizin_gecisi_denemesi_guvenli_ada_donusur(string kotuAd)
    {
        var sonuc = FileUploadValidator.Dogrula(kotuAd, "image/jpeg", 1024, JpegBaslik);

        sonuc.IsSuccess.Should().BeTrue();
        sonuc.Value.Should().NotContain("..");
        sonuc.Value.Should().NotContain("/");
        sonuc.Value.Should().NotContain("\\");
        sonuc.Value.Should().EndWith(".jpg");
    }

    // SALDIRI 4: SVG ILE SAKLANMIS XSS
    //
    // SVG bir XML belgesidir ve icine script gomulebilir. "Resim"
    // oldugu icin zararsiz sanilir; beyaz listemizde BILINCLI olarak
    // yok.
    //
    // Bu test, ilerde biri "SVG de resim, ekleyelim" derse kirilir ve
    // karari yeniden dusunmeye zorlar.
    [Fact]
    public void Svg_reddedilir()
    {
        var svg = "<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>"u8.ToArray();

        var sonuc = FileUploadValidator.Dogrula("resim.svg", "image/svg+xml", 1024, svg);

        sonuc.IsFailure.Should().BeTrue();
        sonuc.Error.Code.Should().Be("file.type_not_allowed");
    }

    // SALDIRI 5: MIME TURU ILE UZANTI UYUSMUYOR
    [Fact]
    public void Uyusmayan_mime_turu_reddedilir()
    {
        var sonuc = FileUploadValidator.Dogrula(
            "afis.jpg", "application/x-msdownload", 1024, JpegBaslik);

        sonuc.IsFailure.Should().BeTrue();
        sonuc.Error.Code.Should().Be("file.mime_mismatch");
    }

    // SALDIRI 6: PNG ADIYLA JPEG ICERIGI
    //
    // Bu bir saldiri olmayabilir -- kullanici dosyayi elle yeniden
    // adlandirmis da olabilir. Yine de reddediyoruz cunku dosyayi
    // sundugumuzda Content-Type yanlis olacak ve tarayici davranisi
    // ongorulemez hale gelecek.
    [Fact]
    public void Icerigi_uzantisiyla_uyusmayan_dosya_reddedilir()
    {
        var sonuc = FileUploadValidator.Dogrula("afis.png", "image/png", 1024, JpegBaslik);

        sonuc.IsFailure.Should().BeTrue();
        sonuc.Error.Code.Should().Be("file.content_mismatch");
    }

    [Fact]
    public void Bos_dosya_reddedilir()
    {
        var sonuc = FileUploadValidator.Dogrula("afis.jpg", "image/jpeg", 0, JpegBaslik);

        sonuc.IsFailure.Should().BeTrue();
        sonuc.Error.Code.Should().Be("file.empty");
    }

    [Fact]
    public void Cok_buyuk_dosya_reddedilir()
    {
        var sonuc = FileUploadValidator.Dogrula(
            "afis.jpg", "image/jpeg", FileUploadValidator.MaksimumBoyut + 1, JpegBaslik);

        sonuc.IsFailure.Should().BeTrue();
        sonuc.Error.Code.Should().Be("file.too_large");
    }

    [Fact]
    public void Uzantisiz_dosya_reddedilir()
    {
        var sonuc = FileUploadValidator.Dogrula("afis", "image/jpeg", 1024, JpegBaslik);

        sonuc.IsFailure.Should().BeTrue();
        sonuc.Error.Code.Should().Be("file.type_not_allowed");
    }

    // KIRPILMIS DOSYA
    //
    // Imzayi tamamlayacak kadar bayt yoksa dogrulayamayiz.
    // "Dogrulayamiyorum" durumunda GECIRMEK degil REDDETMEK dogru
    // olan -- guvenlik kontrollerinde belirsizlik, ret demektir.
    [Fact]
    public void Imza_icin_yetersiz_bayt_reddedilir()
    {
        var sonuc = FileUploadValidator.Dogrula(
            "afis.png", "image/png", 1024, new byte[] { 0x89, 0x50 });

        sonuc.IsFailure.Should().BeTrue();
        sonuc.Error.Code.Should().Be("file.content_mismatch");
    }

    // WEBP: "RIFF" TEK BASINA YETMEZ
    //
    // WAV ve AVI de "RIFF" ile basliyor. Yalnizca ilk 4 bayta
    // bakan bir kontrol, .webp adiyla yuklenen bir WAV dosyasini
    // kabul ederdi.
    [Fact]
    public void Riff_ile_baslayan_ama_webp_olmayan_dosya_reddedilir()
    {
        // "RIFF....WAVE" -- gercek bir WAV başlığı.
        byte[] wav = [0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x41, 0x56, 0x45];

        var sonuc = FileUploadValidator.Dogrula("ses.webp", "image/webp", 1024, wav);

        sonuc.IsFailure.Should().BeTrue();
        sonuc.Error.Code.Should().Be("file.content_mismatch");
    }

    [Fact]
    public void Gercek_webp_kabul_edilir()
    {
        // "RIFF....WEBP"
        byte[] webp = [0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50];

        var sonuc = FileUploadValidator.Dogrula("afis.webp", "image/webp", 1024, webp);

        sonuc.IsSuccess.Should().BeTrue();
    }

    // URETILEN AD HER SEFERINDE FARKLI OLMALI
    //
    // Ayni ad uretilseydi ikinci yukleme birincinin uzerine yazardi
    // (veya LocalFileStorage'daki FileMode.CreateNew yuzunden
    // patlardi). Ikisi de kabul edilemez.
    [Fact]
    public void Uretilen_ad_benzersizdir()
    {
        var a = FileUploadValidator.Dogrula("afis.jpg", "image/jpeg", 1024, JpegBaslik);
        var b = FileUploadValidator.Dogrula("afis.jpg", "image/jpeg", 1024, JpegBaslik);

        a.Value.Should().NotBe(b.Value);
    }
}
