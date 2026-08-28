using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Ticketing.WebApi.Security;

/// <summary>
/// Istek govdesi cok buyukse, govde OKUNMADAN once 413 doner.
/// PDF Sprint 15: "Request size limit".
/// </summary>
/// <remarks>
/// ==================================================================
/// BU SINIF, [RequestSizeLimit] YETMEDIGI ICIN VAR
/// ==================================================================
/// Once yalnizca [RequestSizeLimit(5 MB)] kullandim. Sinir DOGRU
/// calisiyordu ama YANITI test edince iki sorun cikti:
///
///   1) Durum kodu 413 degil 400 donuyordu. Kestrel dogru istisnayi
///      (BadHttpRequestException, StatusCode = 413) firlatiyor ama
///      MVC bunu MODEL BAGLAMA sirasinda yakalayip siradan bir
///      dogrulama hatasina ceviriyor. Bizim GlobalExceptionHandler'a
///      hic ulasmiyor.
///
///   2) Yanit, yapilandirdigimiz siniri AYNEN yaziyordu:
///      "The max request body size is 5242880 bytes."
///      Bu, ic yapilandirmamizi disariya acan gereksiz bir bilgi ve
///      uygulamanin geri kalaniyla tutarsiz bir hata bicimi.
///
/// ------------------------------------------------------------------
/// NEDEN RESOURCE FILTER, ACTION FILTER DEGIL?
/// ------------------------------------------------------------------
/// Action filter, MODEL BAGLAMADAN SONRA calisiyor -- yani govde
/// coktan okunmus, hata coktan olusmus oluyor. Cok gec.
///
/// Resource filter, model baglamadan ONCE calisan ilk noktadir.
/// Content-Length basligi o an zaten elimizde; govdeyi hic
/// okumadan karar verebiliyoruz.
///
/// Yan fayda: 6 MB'lik bir istegi tel uzerinden okumak zorunda
/// kalmiyoruz. Reddedecegimiz veriyi almak icin bant genisligi ve
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
/// davranis geciyor. Ikisini birlikte kullaniyorum: bu filtre
/// dogru yaniti verir, [RequestSizeLimit] ise gercek sinirlayici
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

        // Problem Details bicimi: uygulamanin geri kalaniyla ayni.
        // Istemci tek bir hata ayristiricisi kullanabiliyor.
        var problem = new ProblemDetails
        {
            Title = "Istek cok buyuk",

            // Siniri MB cinsinden, YUVARLANMIS olarak soyluyorum.
            // Kullanicinin bilmesi gereken sey "5 MB"; tam bayt
            // degeri onun isine yaramaz, saldirganin ise isine yarar.
            Detail = $"Dosya boyutu en fazla {_limit / (1024 * 1024)} MB olabilir.",
            Status = StatusCodes.Status413PayloadTooLarge,
            Instance = $"{context.HttpContext.Request.Method} {context.HttpContext.Request.Path}",
        };

        problem.Extensions["errorCode"] = "request.too_large";

        // Result atamak, islem hattini KISA DEVRE yapiyor: eylem
        // metodu hic calismiyor ve govde hic okunmuyor.
        context.Result = new ObjectResult(problem)
        {
            StatusCode = StatusCodes.Status413PayloadTooLarge,
            ContentTypes = { "application/problem+json" },
        };
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
        // Istek tamamlandiktan sonra yapacak bir isimiz yok.
    }
}
