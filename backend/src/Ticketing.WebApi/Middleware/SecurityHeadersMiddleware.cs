namespace Ticketing.WebApi.Middleware;

/// <summary>
/// Güvenlik basliklarini ekler. PDF Sprint 15: "Security headers".
/// </summary>
/// <remarks>
/// NEDEN MIDDLEWARE? Neden her yanitta elle eklemiyoruz?
///
/// Basliklari controller'larda eklemek, birini unutmak demektir --
/// ve unutulan uc tam olarak korumasiz olandir.
///
/// Middleware TÜM yanitlara ekliyor: controller, statik dosya,
/// hata sayfası, Swagger, Hangfire paneli. Yeni bir uc eklendiginde
/// hiçbir sey yapmaya gerek yok.
/// </remarks>
internal sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly bool _isDevelopment;

    public SecurityHeadersMiddleware(RequestDelegate next, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        _next = next;
        _isDevelopment = environment.IsDevelopment();
    }

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // BASLIKLARI YANIT BASLAMADAN ONCE EKLE
        //
        // OnStarting kullanıyorum, doğrudan atama değil.
        //
        // Sebep: _next(context) calistiktan SONRA eklemeye calissaydik,
        // yanit govdesi coktan yazilmaya baslamis olabilirdi ve
        // "headers are read-only" istisnasi alırdım.
        //
        // OnStarting, ilk bayt yazilmadan hemen önce çalışıyor --
        // basliklari degistirmek için son güvenli an.
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;

            // X-Content-Type-Options: nosniff
            //
            // Tarayicinin içerik turunu TAHMIN etmesini engelliyor.
            //
            // Olmasaydı: kullanıcının yukledigi bir .txt dosyasi HTML
            // gibi gorunuyorsa tarayıcı önü HTML olarak calistirabilir
            // ve icindeki script calisirdi. Buna "MIME sniffing
            // saldirisi" deniyor.
            headers["X-Content-Type-Options"] = "nosniff";

            // X-Frame-Options: DENY
            //
            // Sayfanin başka bir sitede iframe icine konmasini
            // engelliyor.
            //
            // Olmasaydı: saldirgan benim sitemizi seffaf bir iframe'e
            // koyup ustune kendi dugmelerini yerlestirebilirdi.
            // Kullanıcı "odulu al" sanip aslında "bileti iptal et"e
            // basardi. Buna "clickjacking" deniyor.
            //
            // CSP'nin frame-ancestors direktifi de aynı isi yapiyor ama
            // eski tarayicilar yalnızca bu başlığı anliyor.
            headers["X-Frame-Options"] = "DENY";

            // Referrer-Policy
            //
            // Baska siteye giderken ADRESIMIZIN ne kadarinin
            // gonderilecegini belirliyor.
            //
            // Varsayılan davranis TAM ADRESI gönderiyor. Bizim
            // adreslerimizde hassas bilgi olabilir:
            //   /rezervasyonlar/{guid}
            //   /api/v1/reports/exports/{guid}
            //
            // strict-origin-when-cross-origin: kendi sitemizde tam
            // adres, disariya yalnızca alan adı.
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

            // Permissions-Policy
            //
            // Tarayici ozelliklerini kapatiyor. Bizim uygulamamiz
            // kamera, mikrofon veya konum kullanmiyor.
            //
            // Kullanmadigimiz bir ozelligi kapatmak bedava güvenlik:
            // ilerde bir XSS acigi olussa bile saldirgan bunlara
            // erisemez.
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";

            // Content-Security-Policy
            //
            // XSS'e karsi en güçlü savunma: hangi kaynaklardan script,
            // stil ve resim yuklenebilecegini belirliyor.
            //
            // BU API ICIN CSP -- SAYFA SUNMUYORUZ AMA YINE DE GEREKLI
            //
            // Bu bir API; HTML dondurmuyor. O zaman CSP niye?
            //
            // Çünkü iki yerde HTML var:
            //   1) Hangfire izleme paneli (/hangfire)
            //   2) Hata sayfalari ve Swagger (gelistirmede)
            //
            // Ayrıca bir saldirgan API'den HTML dondurmeyi basarirsa
            // (yansitilmis XSS), CSP son savunma hatti oluyor.
            //
            // default-src 'none': hiçbir sey yuklenemez. En kisitlayici
            // başlangıç; ihtiyac oldukca aciliyor.
            headers["Content-Security-Policy"] = _isDevelopment

                // Gelistirmede Swagger ve Hangfire paneli satır ici
                // script/stil kullaniyor. 'unsafe-inline' vermezsek
                // paneller calismaz.
                //
                // Bu bir GÜVENLİK GEVSETMESI ve yalnızca gelistirmede
                // geçerli -- uretimde bu paneller zaten kapalı veya
                // ag seviyesinde korunuyor.
                ? "default-src 'self'; " +
                  "script-src 'self' 'unsafe-inline'; " +
                  "style-src 'self' 'unsafe-inline'; " +
                  "img-src 'self' data:; " +
                  "frame-ancestors 'none'"

                // Uretimde çok daha siki: satır ici script YOK.
                : "default-src 'none'; " +
                  "script-src 'self'; " +
                  "style-src 'self'; " +
                  "img-src 'self' data:; " +
                  "font-src 'self'; " +
                  "connect-src 'self'; " +
                  "frame-ancestors 'none'; " +
                  "base-uri 'none'; " +
                  "form-action 'self'";

            // Strict-Transport-Security (HSTS)
            //
            // Tarayiciya "bu siteye bir daha SADECE HTTPS ile gel" der.
            //
            // YALNIZCA URETIMDE. Gelistirmede localhost HTTP kullaniyor;
            // HSTS gonderirsek tarayıcı localhost'u kalici olarak
            // HTTPS'e zorlar ve gelistirme ortami bozulur.
            //
            // Bunu geri almak zor: tarayıcı ayarlarindan elle silmek
            // gerekiyor. Bu yüzden kosul ŞART.
            if (!_isDevelopment)
            {
                // 1 yil + alt alan adları.
                //
                // preload EKLEMEDIM: preload listesine girmek KALICI
                // bir karardir ve cikmak aylar surer. Önce gerçek bir
                // alan adiyla test edilmeli.
                headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
            }

            // Sunucu parmak izi
            //
            // "Server: Kestrel" başlığı BURADAN kaldirilamiyor: Kestrel
            // önü bu geri cagrimdan SONRA ekliyor. Denedim, calismadi.
            //
            // Dogru yer Program.cs'teki AddServerHeader = false ayari
            // (gerekçesi orada yazili).
            //
            // X-Powered-By ise bazi ters vekil sunucular tarafından
            // ekleniyor; önü burada kaldirabiliyoruz.
            headers.Remove("X-Powered-By");

            return Task.CompletedTask;
        });

        await _next(context).ConfigureAwait(false);
    }
}
