using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ticketing.WebApi.Security;

namespace Ticketing.WebApi.Middleware;

/// <summary>
/// Sahiplik politikalari reddettiginde 403 yerine 404 dondurur.
/// </summary>
/// <remarks>
/// ==================================================================
/// NEDEN 403 DEGIL DE 404?
/// ==================================================================
/// Sprint 19'da TicketOwner ve ReservationOwner politikalarini
/// tamamladiktan sonra bir celiski fark ettim.
///
/// Politikayi uca eklemek savunmayi iki katmanli yapiyor (iyi), ama
/// varsayilan reddetme 403 Forbidden. Ve 403 bir BILGI sizdiriyor:
///
///   403 -> "bu rezervasyon VAR ama senin degil"
///   404 -> "boyle bir rezervasyon yok"   (hicbir sey soylemiyor)
///
/// Ilginc olan: handler'larimiz ZATEN 404 donuyordu. Bunu Sprint 19
/// guvenlik testinde olctum -- baska bir kullanicinin rezervasyonuna
/// erisim denemesi 404 aliyordu.
///
/// Yani politikayi kosulsuz eklemek, daha guvenli olan bu davranisi
/// BOZACAKTI: bir iyilestirme yaparken bir gerileme uretecektim.
///
/// ------------------------------------------------------------------
/// NEDEN MIDDLEWARE, IAuthorizationMiddlewareResultHandler DEGIL?
/// ------------------------------------------------------------------
/// Once o arayuzu uygulamayi denedim -- bu isin "resmi" yolu. Ama
/// derleyici tipi cozemedi (CS0246): arayuz bu cerceve surumunde
/// projeden erisilebilir degil.
///
/// Middleware ayni sonucu veriyor ve daha az cerceve ic ayrintisina
/// bagli. Bedeli: yaniti DEGIL, yalnizca durum kodunu ve govdeyi
/// yeniden yaziyoruz -- ki zaten istedigimiz tam olarak bu.
///
/// ------------------------------------------------------------------
/// KAPSAM: YALNIZCA SAHIPLIK POLITIKALARI
/// ------------------------------------------------------------------
/// Rol bazli reddetmelerde (AdminOnly, OrganizerOnly) 403 KALIYOR.
/// Orada sizinti yok: "admin degilsin" bilgisi kullanicinin kendisi
/// hakkinda. Onu 404 ile karsilamak yaniltici olurdu -- kullanici
/// "sayfa silinmis mi?" diye dusunurdu.
/// ==================================================================
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

        // ==============================================================
        // YALNIZCA 403 -- 401 DEGIL
        // ==============================================================
        // 401 "kim oldugunu bilmiyorum" demek; kullanici giris
        // yapmamis. Ona "yok" demek yanlis olurdu: giris yapmasi
        // gerektigini soylemeliyiz.
        // ==============================================================
        if (context.Response.StatusCode != StatusCodes.Status403Forbidden)
        {
            return;
        }

        if (!SahiplikPolitikasiMi(context))
        {
            return;
        }

        // ==============================================================
        // YANIT BASLAMISSA DOKUNAMAYIZ
        // ==============================================================
        // Yetkilendirme reddi govde YAZMADAN durum kodu ayarliyor, bu
        // yuzden pratikte buraya her zaman "baslamamis" olarak
        // geliyoruz.
        //
        // Yine de kontrol ediyorum: aksi halde ilerde araya giren
        // baska bir middleware govde yazarsa burasi
        // InvalidOperationException firlatirdi.
        // ==============================================================
        if (context.Response.HasStarted)
        {
            return;
        }

        var problem = new ProblemDetails
        {
            Title = "NotFound",
            Status = StatusCodes.Status404NotFound,
            Detail = "Kayit bulunamadi.",
            Instance = $"{context.Request.Method} {context.Request.Path}",
        };

        problem.Extensions["errorCode"] = "resource.not_found";

        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        context.Response.ContentType = "application/problem+json";

        await context.Response.WriteAsJsonAsync(problem).ConfigureAwait(false);
    }

    /// <summary>
    /// Bu ucun yetkilendirmesi bir SAHIPLIK politikasina mi dayaniyor?
    /// </summary>
    /// <remarks>
    /// Endpoint metadata'sindan okuyoruz. Yol desenine gore karar
    /// verseydik ("/reservations/ ile basliyorsa") yeni bir uc
    /// eklendiginde kural sessizce yanlis yere uygulanabilirdi.
    ///
    /// Metadata, ucun GERCEKTEN hangi politikayi kullandigini
    /// soyluyor.
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
