using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Ticketing.WebApi.Security;

/// <summary>
/// İstek hizi sinirlama. PDF Sprint 15.
/// </summary>
/// <remarks>
/// Neden ayri kutuphane yok?
///
/// AspNetCoreRateLimit gibi paketler var ama .NET 7'den beri
/// Microsoft.AspNetCore.RateLimiting framework icinde geliyor.
///
/// Ucuncu bir bagimlilik eklemek; güvenlik taramasi, surum takibi ve
/// gecisli bagimlilik maliyeti getirir. Yerlesik olan ihtiyacimi
/// karsiliyor.
/// </remarks>
public static class RateLimitingSetup
{
    /// <summary>Politika adları. Controller'larda [EnableRateLimiting] ile kullanılıyor.</summary>
    public static class Policies
    {
        /// <summary>Giriş, kayıt, şifre sıfırlama. En siki.</summary>
        public const string Authentication = "auth";

        /// <summary>Rezervasyon ve ödeme. Para ile ilgili.</summary>
        public const string Transaction = "transaction";

        /// <summary>Arama ve listeleme. Gevsek ama sınırsız değil.</summary>
        public const string Search = "search";
    }

    /// <summary>
    /// Hiz sinirlamasini yapilandirir.
    /// </summary>
    /// <param name="enabled">
    ///
    /// Neden kapatilabilir olmali? (PDF Sprint 17)
    ///
    /// Entegrasyon testleri aynı istemciden (bellek ici sunucu)
    /// onlarca giriş yapiyor. Auth politikasi 5 dakikada 10 istek
    /// olduğu için 11. testten sonra HEPSI 429 alırdı.
    ///
    /// Yani hiz sınırı, kendi testlerimi engellerdi ve testler
    /// SIRALARINA göre gecip kalırdı -- hata ayiklamasi en zor
    /// test türü.
    ///
    /// Bayrak yapilandirmadan geliyor ve varsayilani açık. Kapali
    /// olabilmesi için birinin acikca "false" yazmasi gerekiyor;
    /// uretimde yanlislikla kapalı kalmasi mumkun değil.
    ///
    /// Hiz sinirinin GERCEKTEN calistigi ayrı bir testte
    /// (RateLimitingTests) acikca dogrulaniyor -- yani bu bayrak
    /// bir kapsam bosluguna yol acmiyor.
    ///
    /// </param>
    public static IServiceCollection AddRateLimiting(
        this IServiceCollection services,
        bool enabled = true)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddRateLimiter(options =>
        {
            if (!enabled)
            {
                // Politikalar YINE de kaydediliyor: [EnableRateLimiting]
                // ozniteligi taniyamadigi bir politika adı gorurse
                // uygulama ACILMAZ.
                //
                // Yalnızca sınırları pratikte sonsuz yapiyorum.
                options.AddPolicy(Policies.Authentication, SinirsizPartition());
                options.AddPolicy(Policies.Transaction, SinirsizPartition());
                options.AddPolicy(Policies.Search, SinirsizPartition());

                return;
            }

            // Sinira takilan istek: 429 + Retry-After
            //
            // Varsayılan 503 Service Unavailable dönüyor. 429 Too Many
            // Requests dogrusu: 503 "sunucu bozuk" der, 429 "yavasla"
            // der. Istemci ikisine farklı tepki vermeli.
            //
            // Retry-After başlığı ŞART: istemcinin ne kadar bekleyecegini
            // bilmesi gerekiyor. Olmasaydı istemci korlemesine tekrar
            // dener ve durumu kotulestirirdi.
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                // Problem Details bicimi: uygulamanin geri kalaniyla
                // tutarli hata yaniti. Istemci aynı ayristiriciyi
                // kullanabiliyor.
                context.HttpContext.Response.ContentType = "application/problem+json";

                await context.HttpContext.Response.WriteAsJsonAsync(
                    new
                    {
                        type = "https://tools.ietf.org/html/rfc6585#section-4",
                        title = "Çok fazla istek",
                        status = StatusCodes.Status429TooManyRequests,
                        detail = "Çok sik istek gonderdiniz. Lütfen biraz bekleyip tekrar deneyin.",
                        errorCode = "rate_limit.exceeded",
                    },
                    cancellationToken).ConfigureAwait(false);
            };

            // 1) Kimlik dogrulama -- PDF: login, register, şifre sıfırlama
            //
            // 5 dakikada 10 istek.
            //
            // Neden bu kadar siki? Çünkü bunlar brute force'un hedefi.
            // Sprint 3'te hesap bazlı kilitleme (5 yanlış deneme)
            // vardi ama o tek bir hesabi koruyor.
            //
            // Saldirgan 10.000 farklı e-posta ile "şifre123" deneyebilir
            // (credential stuffing). Hicbir hesap kilitlenmez çünkü her
            // hesaba yalnızca bir deneme yapiliyor.
            //
            // IP bazlı sinir bu saldiriyi durduruyor. Ikisi birlikte
            // çalışıyor: hesap kilidi tek hesabi, hiz sınırı tüm
            // saldiriyi.
            options.AddPolicy(Policies.Authentication, IpPartitionFactory(
                permitLimit: 10,
                window: TimeSpan.FromMinutes(5)));

            // 2) ISLEM -- PDF: rezervasyon oluşturma, ödeme
            //
            // 1 dakikada 20 istek.
            //
            // Normal bir kullanıcı dakikada 20 rezervasyon denemez.
            // Bu sinir bot ile koltuk kapatmayi (scalping) zorlastiriyor.
            //
            // Çok siki yapmadim: popüler bir konserde kullanıcı
            // gerçekten ust uste birkaç kez deneyebilir (koltuklar
            // kapiliyor). 20, mesru kullanıcıyı engellemeyecek kadar
            // genis.
            options.AddPolicy(Policies.Transaction, IpPartitionFactory(
                permitLimit: 20,
                window: TimeSpan.FromMinutes(1)));

            // 3) ARAMA -- PDF: search endpointi
            //
            // 1 dakikada 60 istek.
            //
            // Arama pahalidir (LIKE sorgusu, birden fazla JOIN) ve
            // kazinma (scraping) hedefidir. Ama mesru kullanıcı da
            // filtreleri hizli hizli değiştirir.
            //
            // Saniyede 1 istek ortalamasi, elle kullanim için fazlasiyla
            // yeterli; otomatik kazima için çok yavas.
            options.AddPolicy(Policies.Search, IpPartitionFactory(
                permitLimit: 60,
                window: TimeSpan.FromMinutes(1)));

            // 4) Genel sinir -- her istek için
            //
            // Politikasi olmayan uclar da korunmali. Aksi halde yeni
            // eklenen bir uc, politika atanana kadar TAMAMEN korumasiz
            // kalırdı -- ve bunu kimse fark etmezdi.
            //
            // "Varsayılan olarak güvenli" (secure by default) ilkesi.
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
    /// Sinir uygulamayan partition (yalnızca testlerde).
    /// </summary>
    private static Func<HttpContext, RateLimitPartition<string>> SinirsizPartition()
        => _ => RateLimitPartition.GetNoLimiter("test");

    /// <summary>
    /// Istemci başına sabit pencere sinirlayici üretir.
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

                // Kuyruk yok (QueueLimit = 0)
                //
                // Kuyruk acsaydim, sinira takilan istek beklerdi ve
                // sunucu kaynagini tutardi. Saldirgan bunu kullanip
                // binlerce isteği kuyrukta bekletebilir ve gerçek
                // kullanıcılar için kaynak birakmayabilirdi.
                //
                // Hemen reddetmek daha güvenli: istemci 429 alip
                // Retry-After'a göre bekliyor.
                QueueLimit = 0,
            });

    /// <summary>
    /// İstemci anahtari: giris yapmissa kullanici, yoksa IP
    ///
    /// Yalnızca IP kullansaydım, aynı sirket agindan (tek NAT IP)
    /// baglanan yuzlerce calisan TEK bir sinira takilirdi -- biri
    /// digerlerini engellerdi.
    ///
    /// Giriş yapmış kullanicilarda kimliği kullanmak bunu cozuyor:
    /// herkesin kendi kotasi oluyor.
    ///
    /// Giriş yapmamislarda IP tek seçenek.
    ///
    /// Uyari: IP guvenilir olmali
    ///
    /// RemoteIpAddress, ters vekil sunucu (nginx, load balancer)
    /// arkasinda VEKILIN adresini gosterir -- gerçek istemciyi değil.
    /// O durumda TÜM istekler tek bir IP'den gelmis gibi görünür ve
    /// sinir tüm kullanicilari birlikte etkiler.
    ///
    /// Cozum ForwardedHeaders middleware'i (Program.cs'te
    /// yapilandirildi). O olmadan bu sinirlayici uretimde YANLIS
    /// çalışır.
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
