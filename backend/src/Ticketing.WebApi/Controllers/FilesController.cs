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
    /// Dosya yukler (afis, görsel, belge).
    /// </summary>
    /// <remarks>
    /// Izin verilen turler: .jpg, .jpeg, .png, .webp, .pdf
    /// En büyük boyut: 5 MB
    ///
    /// Uzanti, MIME türü ve dosya içeriği BIRLIKTE dogrulanir;
    /// ucu de aynı türü gostermelidir.
    /// </remarks>
    /// <response code="201">Dosya yuklendi.</response>
    /// <response code="400">Dosya türü, içeriği veya boyutu geçersiz.</response>
    /// <response code="413">Dosya izin verilen boyutu asiyor.</response>
    [HttpPost]

    // KIMLIK DOGRULAMA ŞART -- ANONIM YUKLEMEYE ASLA IZIN YOK
    //
    // Anonim dosya yukleme, sunucumuzu herkese açık bir depolama
    // alanina cevirir. Saldirgan diski doldurabilir veya benim alan
    // adimizi kullanarak zararli dosya dagitabilir -- ve iz surecek
    // bir kimlik olmaz.
    //
    // Kimlik zorunlu olunca her dosyanin bir sahibi oluyor
    // (AuditFieldsInterceptor CreatedBy alanini dolduruyor) ve
    // kotuye kullanim geriye doğru izlenebiliyor.
    [Authorize]

    // İşlem politikasi: dakikada 20. Yukleme pahali bir işlem
    // (disk yazma + doğrulama) ve kotuye kullanimi kolay.
    [EnableRateLimiting(RateLimitingSetup.Policies.Transaction)]

    // UC BAZLI BOYUT SINIRI
    //
    // Program.cs'te genel sinir 1 MB. Dosya yukleme için bu yetersiz
    // oldugundan burada 5 MB'a yukseltiyorum.
    //
    // Genel sınırı 5 MB yapip herkese acmak YANLIS olurdu: JSON
    // isteyen uclarin 5 MB'lik govde kabul etmesi için hiçbir sebep
    // yok ve bu, gereksiz bir saldiri yuzeyi olurdu.
    //
    // Ilke: sinirlar ihtiyaci olan yerde GENISLETILIR, her yerde
    // birden değil.
    [RequestSizeLimit(FileUploadValidator.MaksimumBoyut)]

    // IKI SINIR ATTRIBUTE'U -- IKISI DE GEREKLI
    //
    // [RequestSizeLimit] GERCEK sinirlayici: govdeyi Kestrel
    // seviyesinde kesiyor ve chunked isteklerde bile çalışıyor.
    // Ama tetiklendiginde MVC yanlış yanit uretiyor (400 + ic
    // yapilandirmamiz), çünkü hata model baglama sırasında olusup
    // doğrulama hatasina çevriliyor.
    //
    // [RequestSizeGuard] DOGRU YANITI veriyor: model baglamadan önce
    // Content-Length'e bakip 413 dönüyor.
    //
    // Biri korumayi, digeri iletisimi ustleniyor. Bunu ancak sınırı
    // GERCEKTEN asan bir istek gonderip yaniti okuyunca fark ettim --
    // ayar dogruydu, davranis yanlisti.
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
                Detail = "Yuklenecek dosya bulunamadı.",
                Status = StatusCodes.Status400BadRequest,
            });
        }

        // IFormFile burada BIRAKILIYOR: Application katmanina yalnızca
        // Stream ve birkaç string geciyor. Boylece is mantığı
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
        // yanittan değil, standart bir header'dan da alabiliyor.
        return sonuc.IsSuccess
            ? HandleCreated(sonuc, sonuc.Value.DownloadUrl)
            : HandleResult(sonuc);
    }

    /// <summary>
    /// Yuklenmis bir dosyayı indirir.
    /// </summary>
    /// <response code="200">Dosya donduruldu.</response>
    /// <response code="404">Dosya bulunamadı.</response>
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

        // NEDEN HER ZAMAN "attachment"?
        //
        // Content-Disposition: attachment, tarayiciya "bu dosyayı
        // GOSTERME, INDIR" diyor.
        //
        // "inline" olsaydı tarayıcı dosyayı benim alan adimizda
        // acardi. Dogrulamayi gecmis ama içinde script barindiran bir
        // dosya (örneğin polyglot bir PDF) o zaman BENIM alan
        // adimizda çalışır ve kullanicilarin oturum cerezlerine
        // erisebilirdi.
        //
        // Indirme olarak sunmak bu riski ortadan kaldiriyor.
        // X-Content-Type-Options: nosniff başlığı (Sprint 15
        // SecurityHeadersMiddleware) ikinci katman olarak tarayıcının
        // türü tahmin etmesini de engelliyor.
        //
        // NOT: Gerçek bir üretim sisteminde yuklenen dosyalar AYRI bir
        // alan adindan sunulur (örneğin cdn-örnek.com). Boylece dosya
        // bir şekilde calissa bile ana alan adimizin cerezlerine
        // erisemez. Bunu simdi yapmiyorum çünkü tek alan adiyla
        // calisiyorum -- ama olceklenirken ilk yapilacak sey bu.
        return File(dosya.Content, dosya.ContentType, dosya.FileName);
    }
}
