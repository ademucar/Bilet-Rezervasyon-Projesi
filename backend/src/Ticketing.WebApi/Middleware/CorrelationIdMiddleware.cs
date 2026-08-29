namespace Ticketing.WebApi.Middleware;

/// <summary>
/// Her HTTP istegine benzersiz bir Correlation ID atar.
///
/// PDF Sprint 16: "Her HTTP isteği için Correlation ID uretilmelidir.
/// Bu deger: Response header, Application log, Exception log,
/// Background job log, Outbox kaydı icerisinde kullanılmalıdır."
///
/// NEDEN GEREKLI?
///
/// Uretimde bir kullanıcı arayip "biletim gelmedi" diyor. Loglara
/// bakiyorsun: saniyede yuzlerce satır akiyor, hangileri BU kullanıcıya
/// ait?
///
/// Correlation ID olmadan bunu bulmak neredeyse imkansiz. Ozellikle
/// benim sistemimizde zincir uzun:
///
///   HTTP isteği -> Handler -> Outbox kaydı -> Background job -> E-posta
///
/// Bu adimlar FARKLI ZAMANLARDA ve farklı process'lerde çalışıyor.
/// Correlation ID hepsini tek bir ipe diziyor. Kullanicidan ID'yi
/// alip tek bir sorguyla tüm hikayeyi gorebiliyorsun.
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

        // DEGERI ONCE HttpContext.Items'A KOY -- SPRINT 16'DA BULUNAN HATA
        //
        // Bu satır olmadan sistemin yarisi correlation ID'yi GOREMIYORDU.
        //
        // Sebep: ICurrentUser.CorrelationId, değeri RESPONSE HEADER'INDAN
        // okuyordu. Ama aşağıdaki OnStarting geri cagrimi, yanitin ilk
        // bayti yazilmadan hemen önce -- yani HANDLER CALISTIKTAN SONRA
        // -- çalışıyor.
        //
        // Yani istek islenirken response header HENUZ BOSTU ve
        // ICurrentUser.CorrelationId her zaman null donuyordu:
        //
        //     Middleware  -> OnStarting KAYDEDILDI (henüz calismadi)
        //     Handler     -> _currentUser.CorrelationId => null   <-- burada
        //     SaveChanges -> Outbox.CorrelationId = null
        //     OnStarting  -> header nihayet yaziliyor (çok geç)
        //
        // Sonucu veritabaninda olctum: 22 Outbox kaydinin 22'sinde,
        // butun AuditLog kayitlarinda correlation ID BOSTU. Alan vardi,
        // indeks vardi, hatta doğru cagri yerleri vardi -- deger yoktu.
        //
        // HttpContext.Items, istek boyunca yasayan ve HEMEN yazilabilen
        // bir sozluk. Deger artık handler calismadan önce hazır.
        context.Items[HeaderName] = correlationId;

        // Response'a ekliyorum ki istemci de gorebilsin.
        //
        // OnStarting kullanmamin sebebi: response yazilmaya BASLADIKTAN
        // sonra header eklenemez. Bu geri cagirim, ilk byte gonderilmeden
        // hemen önce çalışır -- yani header eklemek için son güvenli an.
        //
        // Dogrudan context.Response.Headers.Add(...) yazsaydim, alt
        // katmanlardan biri response'a erken yazmaya baslarsa
        // InvalidOperationException alirdim.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;

            return Task.CompletedTask;
        });

        // BeginScope: bu blok içinde atilan TÜM loglara CorrelationId
        // otomatik olarak eklenir. Her log satirinda elle yazmamiza
        // gerek kalmaz -- ki yazsaydım yarisini unuturdum.
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
        // Istemci kendi ID'sini gonderdiyse ONU kullanıyorum.
        //
        // Neden? Frontend bir kullanıcı islemini birden fazla API cagrisiyla
        // yapabilir (önce rezervasyon, sonra ödeme). Aynı ID'yi gondererek
        // bu cagrilari birbirine baglayabilir.
        //
        // GÜVENLİK NOTU: Istemciden gelen değeri OLDUGU GIBI kullanmiyorum.
        // Uzunlugu sinirliyorum, çünkü bu deger loglara ve response
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
