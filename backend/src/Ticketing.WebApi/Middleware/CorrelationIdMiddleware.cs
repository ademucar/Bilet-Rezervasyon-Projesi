namespace Ticketing.WebApi.Middleware;

/// <summary>
/// Her HTTP istegine benzersiz bir Correlation ID atar.
///
/// PDF Sprint 16: "Her HTTP istegi icin Correlation ID uretilmelidir.
/// Bu deger: Response header, Application log, Exception log,
/// Background job log, Outbox kaydi icerisinde kullanilmalidir."
///
/// ==================================================================
/// NEDEN GEREKLI?
/// ==================================================================
/// Uretimde bir kullanici arayip "biletim gelmedi" diyor. Loglara
/// bakiyorsun: saniyede yuzlerce satir akiyor, hangileri BU kullaniciya
/// ait?
///
/// Correlation ID olmadan bunu bulmak neredeyse imkansiz. Ozellikle
/// bizim sistemimizde zincir uzun:
///
///   HTTP istegi -> Handler -> Outbox kaydi -> Background job -> E-posta
///
/// Bu adimlar FARKLI ZAMANLARDA ve farkli process'lerde calisiyor.
/// Correlation ID hepsini tek bir ipe diziyor. Kullanicidan ID'yi
/// alip tek bir sorguyla tum hikayeyi gorebiliyorsun.
/// ==================================================================
/// </summary>
public sealed class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-Id";

    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, ILogger<CorrelationIdMiddleware> logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(logger);

        var correlationId = GetOrCreateCorrelationId(context);

        // ==============================================================
        // DEGERI ONCE HttpContext.Items'A KOY -- SPRINT 16'DA BULUNAN HATA
        // ==============================================================
        // Bu satir olmadan sistemin yarisi correlation ID'yi GOREMIYORDU.
        //
        // Sebep: ICurrentUser.CorrelationId, degeri RESPONSE HEADER'INDAN
        // okuyordu. Ama asagidaki OnStarting geri cagrimi, yanitin ilk
        // bayti yazilmadan hemen once -- yani HANDLER CALISTIKTAN SONRA
        // -- calisiyor.
        //
        // Yani istek islenirken response header HENUZ BOSTU ve
        // ICurrentUser.CorrelationId her zaman null donuyordu:
        //
        //     Middleware  -> OnStarting KAYDEDILDI (henuz calismadi)
        //     Handler     -> _currentUser.CorrelationId => null   <-- burada
        //     SaveChanges -> Outbox.CorrelationId = null
        //     OnStarting  -> header nihayet yaziliyor (cok gec)
        //
        // Sonucu veritabaninda olctum: 22 Outbox kaydinin 22'sinde,
        // butun AuditLog kayitlarinda correlation ID BOSTU. Alan vardi,
        // indeks vardi, hatta dogru cagri yerleri vardi -- deger yoktu.
        //
        // HttpContext.Items, istek boyunca yasayan ve HEMEN yazilabilen
        // bir sozluk. Deger artik handler calismadan once hazir.
        // ==============================================================
        context.Items[HeaderName] = correlationId;

        // Response'a ekliyorum ki istemci de gorebilsin.
        //
        // OnStarting kullanmamin sebebi: response yazilmaya BASLADIKTAN
        // sonra header eklenemez. Bu geri cagirim, ilk byte gonderilmeden
        // hemen once calisir -- yani header eklemek icin son guvenli an.
        //
        // Dogrudan context.Response.Headers.Add(...) yazsaydim, alt
        // katmanlardan biri response'a erken yazmaya baslarsa
        // InvalidOperationException alirdim.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;

            return Task.CompletedTask;
        });

        // BeginScope: bu blok icinde atilan TUM loglara CorrelationId
        // otomatik olarak eklenir. Her log satirinda elle yazmamiza
        // gerek kalmaz -- ki yazsaydik yarisini unuturduk.
        using (logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId
        }))
        {
            await _next(context);
        }
    }

    private static string GetOrCreateCorrelationId(HttpContext context)
    {
        // Istemci kendi ID'sini gonderdiyse ONU kullaniyorum.
        //
        // Neden? Frontend bir kullanici islemini birden fazla API cagrisiyla
        // yapabilir (once rezervasyon, sonra odeme). Ayni ID'yi gondererek
        // bu cagrilari birbirine baglayabilir.
        //
        // GUVENLIK NOTU: Istemciden gelen degeri OLDUGU GIBI kullanmiyoruz.
        // Uzunlugu sinirliyorum, cunku bu deger loglara ve response
        // header'ina yaziliyor. Sinirsiz uzunlukta bir deger log
        // dosyalarini sisirebilir veya header limitlerini asabilir.
        if (context.Request.Headers.TryGetValue(HeaderName, out var gelen))
        {
            var deger = gelen.ToString();

            if (!string.IsNullOrWhiteSpace(deger) && deger.Length <= 64)
            {
                return deger;
            }
        }

        return Guid.CreateVersion7().ToString("N");
    }
}
