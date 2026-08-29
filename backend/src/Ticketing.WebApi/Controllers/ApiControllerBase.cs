using MediatR;
using Microsoft.AspNetCore.Mvc;
using Ticketing.Application.Common.Results;

namespace Ticketing.WebApi.Controllers;

/// <summary>
/// Tüm controller'larin ortak atasi.
///
/// Iki is yapiyor:
///   1. MediatR'i tek bir yerden saglar
///   2. Result nesnesini HTTP yanitina cevirir
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/[controller]")]
public abstract class ApiControllerBase : ControllerBase
{
    private ISender? _sender;

    /// <summary>
    /// MediatR gonderici.
    ///
    /// Yapicidan enjekte etmek yerine tembel (lazy) cozumleme
    /// kullanıyorum. Neden? Yapicidan alsaydim TÜM turetilmis
    /// controller'lar ISender'i yapicilarinda tasiyip base'e
    /// gecirmek zorunda kalırdı:
    ///
    ///     public EventsController(ISender sender) : base(sender) { }
    ///
    /// 20 controller'da bu 20 gereksiz satır demek. Boyle daha temiz.
    /// </summary>
    protected ISender Sender
        => _sender ??= HttpContext.RequestServices.GetRequiredService<ISender>();

    /// <summary>
    /// Result'i HTTP yanitina cevirir.
    ///
    /// ==================================================================
    /// HTTP BILGISI NEDEN BURADA?
    /// ==================================================================
    /// Application katmani ErrorType.NotFound diyor, "404" demiyor.
    /// HTTP'ye cevirme isi Presentation katmaninin sorumlulugunda ve
    /// tam olarak burada yapiliyor.
    ///
    /// Bu ayrim sayesinde aynı handler'lari yarin bir gRPC servisinden
    /// veya bir konsol uygulamasindan cagirabiliriz; onlar da kendi
    /// hata kodlarina cevirir.
    ///
    /// Tek yerde toplamanin ikinci faydasi: 100 endpoint'te
    /// "if (result.IsFailure) return BadRequest(...)" yazmiyoruz.
    /// Bir gün 422 yerine 409 donmeye karar verirsek tek satır
    /// degistiriyoruz.
    /// ==================================================================
    /// </summary>
    protected IActionResult HandleResult(Result result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.IsSuccess ? NoContent() : Problem(result.Error);
    }

    protected IActionResult HandleResult<T>(Result<T> result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>
    /// Olusturma islemlerinde 201 Created döndürür.
    ///
    /// 200 yerine 201 donmek REST'in gerektirdigi davranistir ve
    /// "Location" header'i istemciye yeni kaynagin adresini söyler.
    /// </summary>
    protected IActionResult HandleCreated<T>(Result<T> result, string locationUri)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.IsSuccess
            ? Created(locationUri, result.Value)
            : Problem(result.Error);
    }

    private ObjectResult Problem(Error error)
    {
        var statusCode = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Concurrency => StatusCodes.Status409Conflict,

            // 422 Unprocessable Entity: istek BICIMSEL olarak doğru ama
            // IS KURALI geregi islenemiyor.
            //
            // 400 ile karisir; fark su:
            //   400 -> "gonderdigin veri hatalı" (eksik alan, yanlış tip)
            //   422 -> "verin doğru ama bu işlem su an yapılamaz"
            //          (örneğin: rezervasyon süresi dolmuş)
            //
            // Frontend için bu ayrim önemli: 400'de formu duzelt,
            // 422'de kullanıcıya durum acikla.
            ErrorType.Conflict => StatusCodes.Status422UnprocessableEntity,

            _ => StatusCodes.Status500InternalServerError
        };

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = error.Type.ToString(),
            Detail = error.Message,
            Instance = $"{Request.Method} {Request.Path}"
        };

        // Frontend "detail" METNINE bakarak karar vermemeli -- metni
        // degistirdigimiz gün frontend bozulur. Bu kod sabit kalır.
        problem.Extensions["errorCode"] = error.Code;

        return StatusCode(statusCode, problem);
    }
}
