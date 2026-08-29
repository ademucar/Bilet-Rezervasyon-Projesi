using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ticketing.WebApi.Security;

namespace Ticketing.WebApi.Middleware;

/// <summary>
/// Sahiplik politikalari reddettiginde 403 yerine 404 döndürür.
/// </summary>
/// <remarks>
/// NEDEN 403 DEĞİL DE 404?
///
/// Sprint 19'da TicketOwner ve ReservationOwner politikalarini
/// tamamladiktan sonra bir celiski fark ettim.
///
/// Politikayi uca eklemek savunmayi iki katmanli yapiyor (iyi), ama
/// varsayılan reddetme 403 Forbidden. Ve 403 bir BILGI sizdiriyor:
///
///   403 -> "bu rezervasyon VAR ama senin değil"
///   404 -> "boyle bir rezervasyon yok"   (hiçbir sey soylemiyor)
///
/// Ilginc olan: handler'larimiz ZATEN 404 donuyordu. Bunu Sprint 19
/// güvenlik testinde olctum -- başka bir kullanıcının rezervasyonuna
/// erişim denemesi 404 aliyordu.
///
/// Yani politikayi kosulsuz eklemek, daha güvenli olan bu davranisi
/// BOZACAKTI: bir iyilestirme yaparken bir gerileme uretecektim.
///
/// NEDEN MIDDLEWARE, IAuthorizationMiddlewareResultHandler DEĞİL?
///
/// Önce o arayuzu uygulamayi denedim -- bu isin "resmi" yolu. Ama
/// derleyici tipi cozemedi (CS0246): arayüz bu cerceve surumunde
/// projeden erişilebilir değil.
///
/// Middleware aynı sonucu veriyor ve daha az cerceve ic ayrintisina
/// bağlı. Bedeli: yaniti DEĞİL, yalnızca durum kodunu ve govdeyi
/// yeniden yazıyorum -- ki zaten istedigimiz tam olarak bu.
///
/// KAPSAM: YALNIZCA SAHIPLIK POLITIKALARI
///
/// Rol bazlı reddetmelerde (AdminOnly, OrganizerOnly) 403 KALIYOR.
/// Orada sizinti yok: "admin degilsin" bilgisi kullanıcının kendisi
/// hakkında. Onu 404 ile karsilamak yanıltıcı olurdu -- kullanıcı
/// "sayfa silinmis mi?" diye dusunurdu.
/// </remarks>
internal sealed class OwnershipNotFoundMiddleware
{
    private static readonly string[] SahiplikPolitikalari =
    [
        AuthenticationSetup.Policies.TicketOwner,
        AuthenticationSetup.Policies.ReservationOwner,
        AuthenticationSetup.Policies.EventOwner,
    ];

    private readonly RequestDelegate _next;

    public OwnershipNotFoundMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        await _next(context).ConfigureAwait(false);

        // YALNIZCA 403 -- 401 DEĞİL
        //
        // 401 "kim olduğunu bilmiyorum" demek; kullanıcı giriş
        // yapmamis. Ona "yok" demek yanlış olurdu: giriş yapmasi
        // gerektigini soylemeliyiz.
        if (context.Response.StatusCode != StatusCodes.Status403Forbidden)
        {
            return;
        }

        if (!SahiplikPolitikasiMi(context))
        {
            return;
        }

        // YANIT BASLAMISSA DOKUNAMAYIZ
        //
        // Yetkilendirme reddi govde YAZMADAN durum kodu ayarliyor, bu
        // yüzden pratikte buraya her zaman "baslamamis" olarak
        // geliyorum.
        //
        // Yine de kontrol ediyorum: aksi halde ilerde araya giren
        // başka bir middleware govde yazarsa burasi
        // InvalidOperationException firlatirdi.
        if (context.Response.HasStarted)
        {
            return;
        }

        var problem = new ProblemDetails
        {
            Title = "NotFound",
            Status = StatusCodes.Status404NotFound,
            Detail = "Kayıt bulunamadı.",
            Instance = $"{context.Request.Method} {context.Request.Path}",
        };

        problem.Extensions["errorCode"] = "resource.not_found";

        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        context.Response.ContentType = "application/problem+json";

        // context.RequestAborted geciyorum. Sonar bunu BUG olarak
        // isaretledi ve haklıydı: istemci baglantiyi kapattiginda
        // (sekmeyi kapatti, agi gitti) yazma islemi bosuna devam
        // ediyordu. Yuk altinda bu, hicbir yere gitmeyen cevaplari
        // yazmakla ugrasan thread'ler demek.
        await context.Response
            .WriteAsJsonAsync(problem, context.RequestAborted)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Bu ucun yetkilendirmesi bir SAHIPLIK politikasina mi dayaniyor?
    /// </summary>
    /// <remarks>
    /// Endpoint metadata'sindan okuyorum. Yol desenine göre karar
    /// verseydim ("/reservations/ ile basliyorsa") yeni bir uc
    /// eklendiginde kural sessizce yanlış yere uygulanabilirdi.
    ///
    /// Metadata, ucun GERCEKTEN hangi politikayi kullandigini
    /// söylüyor.
    /// </remarks>
    private static bool SahiplikPolitikasiMi(HttpContext context)
    {
        var endpoint = context.GetEndpoint();

        if (endpoint is null)
        {
            return false;
        }

        foreach (var yetki in endpoint.Metadata.OfType<IAuthorizeData>())
        {
            if (yetki.Policy is { } politika
                && SahiplikPolitikalari.Contains(politika, StringComparer.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
