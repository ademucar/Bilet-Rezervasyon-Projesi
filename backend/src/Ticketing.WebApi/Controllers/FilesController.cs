using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Ticketing.Application.Common.Security;
using Ticketing.Application.Features.Files;
using Ticketing.WebApi.Security;

namespace Ticketing.WebApi.Controllers;

/// <summary>
/// Dosya yukleme ve indirme. PDF Sprint 15.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/files")]
public sealed class FilesController : ApiControllerBase
{
    /// <summary>
    /// Dosya yukler (afis, gorsel, belge).
    /// </summary>
    /// <remarks>
    /// Izin verilen turler: .jpg, .jpeg, .png, .webp, .pdf
    /// En buyuk boyut: 5 MB
    ///
    /// Uzanti, MIME turu ve dosya icerigi BIRLIKTE dogrulanir;
    /// ucu de ayni turu gostermelidir.
    /// </remarks>
    /// <response code="201">Dosya yuklendi.</response>
    /// <response code="400">Dosya turu, icerigi veya boyutu gecersiz.</response>
    /// <response code="413">Dosya izin verilen boyutu asiyor.</response>
    [HttpPost]

    // ==============================================================
    // KIMLIK DOGRULAMA SART -- ANONIM YUKLEMEYE ASLA IZIN YOK
    // ==============================================================
    // Anonim dosya yukleme, sunucumuzu herkese acik bir depolama
    // alanina cevirir. Saldirgan diski doldurabilir veya bizim alan
    // adimizi kullanarak zararli dosya dagitabilir -- ve iz surecek
    // bir kimlik olmaz.
    //
    // Kimlik zorunlu olunca her dosyanin bir sahibi oluyor
    // (AuditFieldsInterceptor CreatedBy alanini dolduruyor) ve
    // kotuye kullanim geriye dogru izlenebiliyor.
    // ==============================================================
    [Authorize]

    // Islem politikasi: dakikada 20. Yukleme pahali bir islem
    // (disk yazma + dogrulama) ve kotuye kullanimi kolay.
    [EnableRateLimiting(RateLimitingSetup.Policies.Transaction)]

    // ==============================================================
    // UC BAZLI BOYUT SINIRI
    // ==============================================================
    // Program.cs'te genel sinir 1 MB. Dosya yukleme icin bu yetersiz
    // oldugundan burada 5 MB'a yukseltiyorum.
    //
    // Genel siniri 5 MB yapip herkese acmak YANLIS olurdu: JSON
    // isteyen uclarin 5 MB'lik govde kabul etmesi icin hicbir sebep
    // yok ve bu, gereksiz bir saldiri yuzeyi olurdu.
    //
    // Ilke: sinirlar ihtiyaci olan yerde GENISLETILIR, her yerde
    // birden degil.
    // ==============================================================
    [RequestSizeLimit(FileUploadValidator.MaksimumBoyut)]

    // ==============================================================
    // IKI SINIR ATTRIBUTE'U -- IKISI DE GEREKLI
    // ==============================================================
    // [RequestSizeLimit] GERCEK sinirlayici: govdeyi Kestrel
    // seviyesinde kesiyor ve chunked isteklerde bile calisiyor.
    // Ama tetiklendiginde MVC yanlis yanit uretiyor (400 + ic
    // yapilandirmamiz), cunku hata model baglama sirasinda olusup
    // dogrulama hatasina cevriliyor.
    //
    // [RequestSizeGuard] DOGRU YANITI veriyor: model baglamadan once
    // Content-Length'e bakip 413 donuyor.
    //
    // Biri korumayi, digeri iletisimi ustleniyor. Bunu ancak siniri
    // GERCEKTEN asan bir istek gonderip yaniti okuyunca fark ettim --
    // ayar dogruydu, davranis yanlisti.
    // ==============================================================
    [RequestSizeGuard(FileUploadValidator.MaksimumBoyut)]
    [ProducesResponseType<UploadedFileDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    public async Task<IActionResult> Upload(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Dosya gerekli",
                Detail = "Yuklenecek dosya bulunamadi.",
                Status = StatusCodes.Status400BadRequest,
            });
        }

        // IFormFile burada BIRAKILIYOR: Application katmanina yalnizca
        // Stream ve birkac string geciyor. Boylece is mantigi
        // ASP.NET Core'a bagimli olmuyor (mimari testimizin sarti).
        await using var akis = file.OpenReadStream();

        var sonuc = await Sender.Send(
            new UploadFileCommand(
                file.FileName,
                file.ContentType,
                file.Length,
                akis),
            cancellationToken).ConfigureAwait(false);

        // Basarida 201 + Location: istemci yeni kaynagin adresini
        // yanittan degil, standart bir header'dan da alabiliyor.
        return sonuc.IsSuccess
            ? HandleCreated(sonuc, sonuc.Value.DownloadUrl)
            : HandleResult(sonuc);
    }

    /// <summary>
    /// Yuklenmis bir dosyayi indirir.
    /// </summary>
    /// <response code="200">Dosya donduruldu.</response>
    /// <response code="404">Dosya bulunamadi.</response>
    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingSetup.Policies.Search)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken)
    {
        var sonuc = await Sender
            .Send(new GetFileQuery(id), cancellationToken)
            .ConfigureAwait(false);

        if (sonuc.IsFailure)
        {
            return HandleResult(sonuc);
        }

        var dosya = sonuc.Value;

        // ==============================================================
        // NEDEN HER ZAMAN "attachment"?
        // ==============================================================
        // Content-Disposition: attachment, tarayiciya "bu dosyayi
        // GOSTERME, INDIR" diyor.
        //
        // "inline" olsaydi tarayici dosyayi bizim alan adimizda
        // acardi. Dogrulamayi gecmis ama icinde script barindiran bir
        // dosya (ornegin polyglot bir PDF) o zaman BIZIM alan
        // adimizda calisir ve kullanicilarin oturum cerezlerine
        // erisebilirdi.
        //
        // Indirme olarak sunmak bu riski ortadan kaldiriyor.
        // X-Content-Type-Options: nosniff basligi (Sprint 15
        // SecurityHeadersMiddleware) ikinci katman olarak tarayicinin
        // turu tahmin etmesini de engelliyor.
        //
        // NOT: Gercek bir uretim sisteminde yuklenen dosyalar AYRI bir
        // alan adindan sunulur (ornegin cdn-ornek.com). Boylece dosya
        // bir sekilde calissa bile ana alan adimizin cerezlerine
        // erisemez. Bunu simdi yapmiyorum cunku tek alan adiyla
        // calisiyoruz -- ama olceklenirken ilk yapilacak sey bu.
        // ==============================================================
        return File(dosya.Content, dosya.ContentType, dosya.FileName);
    }
}
