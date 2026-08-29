using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Ticketing.WebApi.Security;

/// <summary>
/// İstek govdesi çok buyukse, govde OKUNMADAN önce 413 döner.
/// PDF Sprint 15: "Request size limit".
/// </summary>
/// <remarks>
/// ==================================================================
/// BU SINIF, [RequestSizeLimit] YETMEDIGI ICIN VAR
/// ==================================================================
/// Önce yalnızca [RequestSizeLimit(5 MB)] kullandim. Sinir DOGRU
/// calisiyordu ama YANITI test edince iki sorun cikti:
///
///   1) Durum kodu 413 değil 400 donuyordu. Kestrel doğru istisnayi
///      (BadHttpRequestException, StatusCode = 413) firlatiyor ama
///      MVC bunu MODEL BAGLAMA sırasında yakalayip siradan bir
///      doğrulama hatasina ceviriyor. Bizim GlobalExceptionHandler'a
///      hiç ulasmiyor.
///
///   2) Yanit, yapilandirdigimiz sınırı AYNEN yaziyordu:
///      "The max request body size is 5242880 bytes."
///      Bu, ic yapilandirmamizi disariya acan gereksiz bir bilgi ve
///      uygulamanin geri kalaniyla tutarsiz bir hata bicimi.
///
/// ------------------------------------------------------------------
/// NEDEN RESOURCE FILTER, ACTION FILTER DEĞİL?
/// ------------------------------------------------------------------
/// Action filter, MODEL BAGLAMADAN SONRA çalışıyor -- yani govde
/// coktan okunmus, hata coktan olusmus oluyor. Çok geç.
///
/// Resource filter, model baglamadan ONCE calisan ilk noktadir.
/// Content-Length başlığı o an zaten elimizde; govdeyi hiç
/// okumadan karar verebiliyoruz.
///
/// Yan fayda: 6 MB'lik bir isteği tel üzerinden okumak zorunda
/// kalmiyoruz. Reddedecegimiz veriyi almak için bant genisligi ve
/// bellek harcamak, tam olarak saldirganin istedigi seydir.
///
/// ------------------------------------------------------------------
/// SINIRLAMA -- DURUSTCE
/// ------------------------------------------------------------------
/// Content-Length OLMAYAN istekler (chunked transfer encoding) bu
/// kontrolden gecer. O durumda [RequestSizeLimit] yine devreye
/// giriyor ve istek durduruluyor -- ama yanit yine 400 oluyor.
///
/// Yani bu sinif, YAYGIN durumu duzeltiyor; nadir durumda eski
/// davranis geciyor. Ikisini birlikte kullanıyorum: bu filtre
/// doğru yaniti verir, [RequestSizeLimit] ise gerçek sinirlayici
/// olarak her kosulda korur.
/// ==================================================================
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
internal sealed class RequestSizeGuardAttribute : Attribute, IResourceFilter
{
    private readonly long _limit;

    public RequestSizeGuardAttribute(long limitInBytes) => _limit = limitInBytes;

    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var uzunluk = context.HttpContext.Request.ContentLength;

        if (uzunluk is null || uzunluk <= _limit)
        {
            return;
        }

        // Problem Details bicimi: uygulamanin geri kalaniyla aynı.
        // Istemci tek bir hata ayristiricisi kullanabiliyor.
        var problem = new ProblemDetails
        {
            Title = "İstek çok büyük",

            // Siniri MB cinsinden, YUVARLANMIS olarak soyluyorum.
            // Kullanıcının bilmesi gereken sey "5 MB"; tam bayt
            // değeri onun isine yaramaz, saldirganin ise isine yarar.
            Detail = $"Dosya boyutu en fazla {_limit / (1024 * 1024)} MB olabilir.",
            Status = StatusCodes.Status413PayloadTooLarge,
            Instance = $"{context.HttpContext.Request.Method} {context.HttpContext.Request.Path}",
        };

        problem.Extensions["errorCode"] = "request.too_large";

        // Result atamak, işlem hattini KISA DEVRE yapiyor: eylem
        // metodu hiç calismiyor ve govde hiç okunmuyor.
        context.Result = new ObjectResult(problem)
        {
            StatusCode = StatusCodes.Status413PayloadTooLarge,
            ContentTypes = { "application/problem+json" },
        };
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
        // İstek tamamlandıktan sonra yapacak bir isimiz yok.
    }
}
