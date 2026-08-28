namespace Ticketing.WebApi.Middleware;

/// <summary>
/// Guvenlik basliklarini ekler. PDF Sprint 15: "Security headers".
/// </summary>
/// <remarks>
/// ==================================================================
/// NEDEN MIDDLEWARE? Neden her yanitta elle eklemiyoruz?
/// ==================================================================
/// Basliklari controller'larda eklemek, birini unutmak demektir --
/// ve unutulan uc tam olarak korumasiz olandir.
///
/// Middleware TUM yanitlara ekliyor: controller, statik dosya,
/// hata sayfasi, Swagger, Hangfire paneli. Yeni bir uc eklendiginde
/// hicbir sey yapmaya gerek yok.
/// ==================================================================
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

        // ==============================================================
        // BASLIKLARI YANIT BASLAMADAN ONCE EKLE
        // ==============================================================
        // OnStarting kullaniyorum, dogrudan atama degil.
        //
        // Sebep: _next(context) calistiktan SONRA eklemeye calissaydik,
        // yanit govdesi coktan yazilmaya baslamis olabilirdi ve
        // "headers are read-only" istisnasi alirdik.
        //
        // OnStarting, ilk bayt yazilmadan hemen once calisiyor --
        // basliklari degistirmek icin son guvenli an.
        // ==============================================================
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;

            // ----------------------------------------------------------
            // X-Content-Type-Options: nosniff
            // ----------------------------------------------------------
            // Tarayicinin icerik turunu TAHMIN etmesini engelliyor.
            //
            // Olmasaydi: kullanicinin yukledigi bir .txt dosyasi HTML
            // gibi gorunuyorsa tarayici onu HTML olarak calistirabilir
            // ve icindeki script calisirdi. Buna "MIME sniffing
            // saldirisi" deniyor.
            headers["X-Content-Type-Options"] = "nosniff";

            // ----------------------------------------------------------
            // X-Frame-Options: DENY
            // ----------------------------------------------------------
            // Sayfanin baska bir sitede iframe icine konmasini
            // engelliyor.
            //
            // Olmasaydi: saldirgan bizim sitemizi seffaf bir iframe'e
            // koyup ustune kendi dugmelerini yerlestirebilirdi.
            // Kullanici "odulu al" sanip aslinda "bileti iptal et"e
            // basardi. Buna "clickjacking" deniyor.
            //
            // CSP'nin frame-ancestors direktifi de ayni isi yapiyor ama
            // eski tarayicilar yalnizca bu basligi anliyor.
            headers["X-Frame-Options"] = "DENY";

            // ----------------------------------------------------------
            // Referrer-Policy
            // ----------------------------------------------------------
            // Baska siteye giderken ADRESIMIZIN ne kadarinin
            // gonderilecegini belirliyor.
            //
            // Varsayilan davranis TAM ADRESI gonderiyor. Bizim
            // adreslerimizde hassas bilgi olabilir:
            //   /rezervasyonlar/{guid}
            //   /api/v1/reports/exports/{guid}
            //
            // strict-origin-when-cross-origin: kendi sitemizde tam
            // adres, disariya yalnizca alan adi.
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

            // ----------------------------------------------------------
            // Permissions-Policy
            // ----------------------------------------------------------
            // Tarayici ozelliklerini kapatiyor. Bizim uygulamamiz
            // kamera, mikrofon veya konum kullanmiyor.
            //
            // Kullanmadigimiz bir ozelligi kapatmak bedava guvenlik:
            // ilerde bir XSS acigi olussa bile saldirgan bunlara
            // erisemez.
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";

            // ----------------------------------------------------------
            // Content-Security-Policy
            // ----------------------------------------------------------
            // XSS'e karsi en guclu savunma: hangi kaynaklardan script,
            // stil ve resim yuklenebilecegini belirliyor.
            //
            // ==========================================================
            // BU API ICIN CSP -- SAYFA SUNMUYORUZ AMA YINE DE GEREKLI
            // ==========================================================
            // Bu bir API; HTML dondurmuyor. O zaman CSP niye?
            //
            // Cunku iki yerde HTML var:
            //   1) Hangfire izleme paneli (/hangfire)
            //   2) Hata sayfalari ve Swagger (gelistirmede)
            //
            // Ayrica bir saldirgan API'den HTML dondurmeyi basarirsa
            // (yansitilmis XSS), CSP son savunma hatti oluyor.
            //
            // default-src 'none': hicbir sey yuklenemez. En kisitlayici
            // baslangic; ihtiyac oldukca aciliyor.
            // ==========================================================
            headers["Content-Security-Policy"] = _isDevelopment

                // Gelistirmede Swagger ve Hangfire paneli satir ici
                // script/stil kullaniyor. 'unsafe-inline' vermezsek
                // paneller calismaz.
                //
                // Bu bir GUVENLIK GEVSETMESI ve yalnizca gelistirmede
                // gecerli -- uretimde bu paneller zaten kapali veya
                // ag seviyesinde korunuyor.
                ? "default-src 'self'; " +
                  "script-src 'self' 'unsafe-inline'; " +
                  "style-src 'self' 'unsafe-inline'; " +
                  "img-src 'self' data:; " +
                  "frame-ancestors 'none'"

                // Uretimde cok daha siki: satir ici script YOK.
                : "default-src 'none'; " +
                  "script-src 'self'; " +
                  "style-src 'self'; " +
                  "img-src 'self' data:; " +
                  "font-src 'self'; " +
                  "connect-src 'self'; " +
                  "frame-ancestors 'none'; " +
                  "base-uri 'none'; " +
                  "form-action 'self'";

            // ----------------------------------------------------------
            // Strict-Transport-Security (HSTS)
            // ----------------------------------------------------------
            // Tarayiciya "bu siteye bir daha SADECE HTTPS ile gel" der.
            //
            // YALNIZCA URETIMDE. Gelistirmede localhost HTTP kullaniyor;
            // HSTS gonderirsek tarayici localhost'u kalici olarak
            // HTTPS'e zorlar ve gelistirme ortami bozulur.
            //
            // Bunu geri almak zor: tarayici ayarlarindan elle silmek
            // gerekiyor. Bu yuzden kosul SART.
            if (!_isDevelopment)
            {
                // 1 yil + alt alan adlari.
                //
                // preload EKLEMEDIM: preload listesine girmek KALICI
                // bir karardir ve cikmak aylar surer. Once gercek bir
                // alan adiyla test edilmeli.
                headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
            }

            // ----------------------------------------------------------
            // Sunucu parmak izi
            // ----------------------------------------------------------
            // "Server: Kestrel" basligi BURADAN kaldirilamiyor: Kestrel
            // onu bu geri cagrimdan SONRA ekliyor. Denedim, calismadi.
            //
            // Dogru yer Program.cs'teki AddServerHeader = false ayari
            // (gerekcesi orada yazili).
            //
            // X-Powered-By ise bazi ters vekil sunucular tarafindan
            // ekleniyor; onu burada kaldirabiliyoruz.
            headers.Remove("X-Powered-By");

            return Task.CompletedTask;
        });

        await _next(context).ConfigureAwait(false);
    }
}
