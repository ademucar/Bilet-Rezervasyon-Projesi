using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Ticketing.WebApi.Security;

/// <summary>
/// Istek hizi sinirlama. PDF Sprint 15.
/// </summary>
/// <remarks>
/// ==================================================================
/// NEDEN AYRI KUTUPHANE YOK?
/// ==================================================================
/// AspNetCoreRateLimit gibi paketler var ama .NET 7'den beri
/// Microsoft.AspNetCore.RateLimiting FRAMEWORK ICINDE geliyor.
///
/// Ucuncu bir bagimlilik eklemek; guvenlik taramasi, surum takibi ve
/// gecisli bagimlilik maliyeti getirir. Yerlesik olan ihtiyacimizi
/// karsiliyor.
/// ==================================================================
/// </remarks>
public static class RateLimitingSetup
{
    /// <summary>Politika adlari. Controller'larda [EnableRateLimiting] ile kullaniliyor.</summary>
    public static class Policies
    {
        /// <summary>Giris, kayit, sifre sifirlama. En siki.</summary>
        public const string Authentication = "auth";

        /// <summary>Rezervasyon ve odeme. Para ile ilgili.</summary>
        public const string Transaction = "transaction";

        /// <summary>Arama ve listeleme. Gevsek ama sinirsiz degil.</summary>
        public const string Search = "search";
    }

    public static IServiceCollection AddRateLimiting(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddRateLimiter(options =>
        {
            // ==========================================================
            // SINIRA TAKILAN ISTEK: 429 + Retry-After
            // ==========================================================
            // Varsayilan 503 Service Unavailable donuyor. 429 Too Many
            // Requests dogrusu: 503 "sunucu bozuk" der, 429 "yavasla"
            // der. Istemci ikisine farkli tepki vermeli.
            //
            // Retry-After basligi SART: istemcinin ne kadar bekleyecegini
            // bilmesi gerekiyor. Olmasaydi istemci korlemesine tekrar
            // dener ve durumu kotulestirirdi.
            // ==========================================================
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                // Problem Details bicimi: uygulamanin geri kalaniyla
                // tutarli hata yaniti. Istemci ayni ayristiriciyi
                // kullanabiliyor.
                context.HttpContext.Response.ContentType = "application/problem+json";

                await context.HttpContext.Response.WriteAsJsonAsync(
                    new
                    {
                        type = "https://tools.ietf.org/html/rfc6585#section-4",
                        title = "Cok fazla istek",
                        status = StatusCodes.Status429TooManyRequests,
                        detail = "Cok sik istek gonderdiniz. Lutfen biraz bekleyip tekrar deneyin.",
                        errorCode = "rate_limit.exceeded",
                    },
                    cancellationToken).ConfigureAwait(false);
            };

            // ==========================================================
            // 1) KIMLIK DOGRULAMA -- PDF: login, register, sifre sifirlama
            // ==========================================================
            // 5 dakikada 10 istek.
            //
            // Neden bu kadar siki? Cunku bunlar brute force'un hedefi.
            // Sprint 3'te hesap bazli kilitleme (5 yanlis deneme)
            // vardi ama o TEK BIR HESABI koruyor.
            //
            // Saldirgan 10.000 farkli e-posta ile "sifre123" deneyebilir
            // (credential stuffing). Hicbir hesap kilitlenmez cunku her
            // hesaba yalnizca bir deneme yapiliyor.
            //
            // IP bazli sinir bu saldiriyi durduruyor. Ikisi birlikte
            // calisiyor: hesap kilidi tek hesabi, hiz siniri tum
            // saldiriyi.
            // ==========================================================
            options.AddPolicy(Policies.Authentication, IpPartitionFactory(
                permitLimit: 10,
                window: TimeSpan.FromMinutes(5)));

            // ==========================================================
            // 2) ISLEM -- PDF: rezervasyon olusturma, odeme
            // ==========================================================
            // 1 dakikada 20 istek.
            //
            // Normal bir kullanici dakikada 20 rezervasyon denemez.
            // Bu sinir bot ile koltuk kapatmayi (scalping) zorlastiriyor.
            //
            // Cok siki yapmadim: populer bir konserde kullanici
            // gercekten ust uste birkac kez deneyebilir (koltuklar
            // kapiliyor). 20, mesru kullaniciyi engellemeyecek kadar
            // genis.
            // ==========================================================
            options.AddPolicy(Policies.Transaction, IpPartitionFactory(
                permitLimit: 20,
                window: TimeSpan.FromMinutes(1)));

            // ==========================================================
            // 3) ARAMA -- PDF: search endpointi
            // ==========================================================
            // 1 dakikada 60 istek.
            //
            // Arama pahalidir (LIKE sorgusu, birden fazla JOIN) ve
            // kazinma (scraping) hedefidir. Ama mesru kullanici da
            // filtreleri hizli hizli degistirir.
            //
            // Saniyede 1 istek ortalamasi, elle kullanim icin fazlasiyla
            // yeterli; otomatik kazima icin cok yavas.
            // ==========================================================
            options.AddPolicy(Policies.Search, IpPartitionFactory(
                permitLimit: 60,
                window: TimeSpan.FromMinutes(1)));

            // ==========================================================
            // 4) GENEL SINIR -- her istek icin
            // ==========================================================
            // Politikasi olmayan uclar da korunmali. Aksi halde yeni
            // eklenen bir uc, politika atanana kadar TAMAMEN korumasiz
            // kalirdi -- ve bunu kimse fark etmezdi.
            //
            // "Varsayilan olarak guvenli" (secure by default) ilkesi.
            // ==========================================================
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
                context => RateLimitPartition.GetFixedWindowLimiter(
                    ClientKey(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 300,
                        Window = TimeSpan.FromMinutes(1),
                    }));
        });

        return services;
    }

    /// <summary>
    /// Istemci basina sabit pencere sinirlayici uretir.
    /// </summary>
    private static Func<HttpContext, RateLimitPartition<string>> IpPartitionFactory(
        int permitLimit,
        TimeSpan window)
        => context => RateLimitPartition.GetFixedWindowLimiter(
            ClientKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = window,

                // ==================================================
                // KUYRUK YOK (QueueLimit = 0)
                // ==================================================
                // Kuyruk acsaydik, sinira takilan istek beklerdi ve
                // sunucu kaynagini tutardi. Saldirgan bunu kullanip
                // binlerce istegi kuyrukta bekletebilir ve gercek
                // kullanicilar icin kaynak birakmayabilirdi.
                //
                // Hemen reddetmek daha guvenli: istemci 429 alip
                // Retry-After'a gore bekliyor.
                // ==================================================
                QueueLimit = 0,
            });

    /// <summary>
    /// ==============================================================
    /// ISTEMCI ANAHTARI: GIRIS YAPMISSA KULLANICI, YOKSA IP
    /// ==============================================================
    /// Yalnizca IP kullansaydik, ayni sirket agindan (tek NAT IP)
    /// baglanan yuzlerce calisan TEK bir sinira takilirdi -- biri
    /// digerlerini engellerdi.
    ///
    /// Giris yapmis kullanicilarda kimligi kullanmak bunu cozuyor:
    /// herkesin kendi kotasi oluyor.
    ///
    /// Giris yapmamislarda IP tek secenek.
    /// ==============================================================
    ///
    /// ==============================================================
    /// UYARI: IP GUVENILIR OLMALI
    /// ==============================================================
    /// RemoteIpAddress, ters vekil sunucu (nginx, load balancer)
    /// arkasinda VEKILIN adresini gosterir -- gercek istemciyi degil.
    /// O durumda TUM istekler tek bir IP'den gelmis gibi gorunur ve
    /// sinir tum kullanicilari birlikte etkiler.
    ///
    /// Cozum ForwardedHeaders middleware'i (Program.cs'te
    /// yapilandirildi). O olmadan bu sinirlayici uretimde YANLIS
    /// calisir.
    /// ==============================================================
    /// </summary>
    private static string ClientKey(HttpContext context)
    {
        var userId = context.User?.FindFirst("sub")?.Value;

        if (!string.IsNullOrEmpty(userId))
        {
            return $"user:{userId}";
        }

        return $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
    }
}
