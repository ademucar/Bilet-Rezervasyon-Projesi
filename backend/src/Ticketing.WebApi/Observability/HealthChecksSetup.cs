using Microsoft.Extensions.Diagnostics.HealthChecks;
using Ticketing.Application.Common.Security;

namespace Ticketing.WebApi.Observability;

/// <summary>
/// Saglik kontrolleri. PDF Sprint 16.
/// </summary>
/// <remarks>
/// UC UC, UC FARKLI SORU
///
/// PDF ucunu de istiyor ve ucu de farklı bir soruya cevap veriyor.
/// Aralarindaki farki bilmemek, uretimde en can sıkıcı hatalardan
/// birine yol aciyor.
///
///   GET /health/live   "Process ayakta mi?"
///        -> HICBIR bagimliligi kontrol ETMEZ.
///        -> Kubernetes bunu kullanir ve BASARISIZ olursa
///           kapsayiciyi OLDURUP yeniden baslatir.
///
///   GET /health/ready  "Trafik alabilir miyim?"
///        -> Veritabani, Redis, Hangfire, disk kontrol edilir.
///        -> Kubernetes bunu kullanir ve başarısız olursa
///           kapsayiciyi OLDURMEZ, sadece yuk dengeleyiciden
///           CIKARIR.
///
///   GET /health        Insan için: her seyin özeti.
///
/// BU AYRIM NEDEN HAYATI? -- SOMUT FELAKET SENARYOSU
///
/// Diyelim /health/live de veritabanini kontrol ettim.
///
/// PostgreSQL 30 saniye için yanit vermez oldu (bakim, ag dalgalanmasi).
/// TÜM kapsayicilarin live probe'u başarısız olur. Kubernetes hepsini
/// birden oldurur. Yeniden baslarlar, veritabani hâlâ yok, yine
/// olurler...
///
/// Sonuç: geçici bir veritabani sorunu, KALICI bir uygulama cokusune
/// donusur. Uygulama kendi kendini yeniden baslatarak duzeltemeyecegi
/// bir sey için surekli yeniden baslatilir.
///
/// Dogru davranis: live gecer (process sağlıklı), ready kalır
/// (trafik alma). Veritabani donunce ready kendiliginden gecer ve
/// trafik geri gelir. Hicbir kapsayici oldurulmez.
/// </remarks>
internal static class HealthChecksSetup
{
    /// <summary>
    /// Bagimlilik kontrollerinin etiketi.
    /// </summary>
    /// <remarks>
    /// Etiket kullanmamin sebebi: /health/ready yalnızca bu etikete
    /// sahip kontrolleri calistiriyor, /health/live ise HICBIRINI.
    ///
    /// Etiketsiz yapsaydim, her yeni saglik kontrolü otomatik olarak
    /// live probe'a da girerdi -- yukaridaki felaket senaryosunu
    /// farkinda olmadan geri getirirdik.
    /// </remarks>
    public const string ReadyTag = "ready";

    public static IServiceCollection AddApplicationHealthChecks(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var builder = services.AddHealthChecks();

        // 1) VERITABANI -- PDF maddesi
        var postgres = configuration.GetConnectionString("Postgres");

        if (!string.IsNullOrWhiteSpace(postgres))
        {
            builder.AddNpgSql(
                connectionString: postgres,

                // Varsayılan sorgu "SELECT 1". Onu birakiyorum:
                // amac veriyi dogrulamak değil, BAGLANTININ kurulup
                // sorgu calistirilabildigini gormek.
                //
                // Agir bir sorgu (örneğin COUNT(*)) yazmak cazip ama
                // yanlış olurdu: saglik kontrolü saniyede bir
                // çalışıyor ve veritabanina yuk BINDIRMEMELI.
                name: "postgresql",
                failureStatus: HealthStatus.Unhealthy,
                tags: [ReadyTag]);
        }

        // 2) REDIS -- PDF maddesi
        var redis = configuration.GetConnectionString("Redis");

        if (!string.IsNullOrWhiteSpace(redis))
        {
            builder.AddRedis(
                redisConnectionString: redis,
                name: "redis",

                // DEGRADED, UNHEALTHY DEĞİL -- BILINCLI KARAR
                //
                // Sprint 11'de önbelleği BILINCLI olarak opsiyonel
                // yaptim: Redis yoksa sorgular veritabanindan
                // karsilaniyor (Null Object Pattern). Yani Redis
                // olmadan sistem YAVAS çalışır, BOZUK calismaz.
                //
                // Unhealthy deseydim: Redis dustugunde /health/ready
                // başarısız olur, Kubernetes tüm kapsayicilari yuk
                // dengeleyiciden cikarir ve site TAMAMEN erişilemez
                // hale gelirdi.
                //
                // Yani calisabilecek bir sistemi, çalışmayan bir
                // önbellek yuzunden kapatmis olurdum. Degraded doğru
                // seviye: alarm üretir, trafigi kesmez.
                failureStatus: HealthStatus.Degraded,
                tags: [ReadyTag]);
        }

        // 3) ARKA PLAN ISLERI -- PDF maddesi
        //
        // Hangfire kontrolü iki sey söylüyor: depolama erişilebilir mi
        // ve CALISAN (worker) var mi.
        //
        // Ikincisi önemli: Hangfire kayitlari alır, kuyruga koyar ve
        // hiçbir hata vermez -- ama isleyen kimse yoksa isler sonsuza
        // kadar bekler. Bu, SESSIZ bir arizadir; kullanıcı e-postasini
        // beklerken sistem "her sey yolunda" der.
        builder.AddHangfire(
            options =>
            {
                // En az bir sunucu çalışıyor olmalı.
                options.MinimumAvailableServers = 1;
            },
            name: "hangfire",

            // Degraded: arka plan isleri durdugunda web trafigi
            // etkilenmemeli. Kullanıcı hâlâ bilet alabilir; yalnızca
            // e-posta gecikir. Trafigi kesmek durumu kotulestirirdi.
            failureStatus: HealthStatus.Degraded,
            tags: [ReadyTag]);

        // 4) DEPOLAMA -- PDF maddesi
        //
        // Sprint 15'te dosya yukleme ekledim; Sprint 13'te rapor
        // disa aktarimi. Ikisi de DISKE yazıyor.
        //
        // Disk dolarsa: yukleme başarısız olur, rapor uretilemez ve
        // Serilog log YAZAMAZ -- yani sorunu anlatacak olan mekanizma
        // da susar. Bu kontrol o sessizligi engelliyor.
        builder.AddCheck<StorageHealthCheck>(
            "storage",
            failureStatus: HealthStatus.Degraded,
            tags: [ReadyTag]);

        return services;
    }
}

/// <summary>
/// Yukleme klasorunun yazılabilir ve diskin dolu olmadigini kontrol eder.
/// PDF Sprint 16: "Storage health check".
/// </summary>
internal sealed class StorageHealthCheck : IHealthCheck
{
    /// <summary>Bu esigin altinda disk alanı kaldiysa uyariyorum.</summary>
    /// <remarks>
    /// 500 MB, "hemen mudahale et" esigi. Sifir beklemek anlamsiz:
    /// disk gerçekten dolduktan sonra uyarmak çok geç olur -- o anda
    /// log bile yazılamıyor olur.
    /// </remarks>
    private const long UyariEsigiBayt = 500L * 1024 * 1024;

    private readonly string _yol;

    public StorageHealthCheck(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        _yol = configuration["FileStorage:Path"]
            ?? Path.Combine(AppContext.BaseDirectory, "uploads");
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!Directory.Exists(_yol))
            {
                return Task.FromResult(HealthCheckResult.Degraded(
                    $"Depolama klasörü yok: {_yol}"));
            }

            // GERCEKTEN YAZMAYI DENIYORUZ
            //
            // Directory.Exists yeterli DEĞİL: klasor var olabilir ama
            // salt okunur baglanmis olabilir (Docker volume ayari),
            // izinler degismis olabilir veya disk dolmuş olabilir.
            //
            // Bunlarin hicbirini "klasor var mi" sorusu yakalamaz.
            // Tek guvenilir yol, gerçek bir yazma denemesi.
            //
            // Gecici dosya adı Guid: es zamanlı kontroller birbirinin
            // dosyasini silmesin.
            var denemeDosyasi = Path.Combine(_yol, $".health-{Guid.NewGuid():N}");

            File.WriteAllBytes(denemeDosyasi, [0]);
            File.Delete(denemeDosyasi);

            // Boş alan kontrolü.
            var surucu = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(_yol))!);
            var bosAlan = surucu.AvailableFreeSpace;

            var veri = new Dictionary<string, object>
            {
                ["path"] = _yol,
                ["freeSpaceMb"] = bosAlan / (1024 * 1024),
            };

            if (bosAlan < UyariEsigiBayt)
            {
                return Task.FromResult(HealthCheckResult.Degraded(
                    $"Disk alani azaliyor: {bosAlan / (1024 * 1024)} MB kaldi.",
                    data: veri));
            }

            return Task.FromResult(HealthCheckResult.Healthy(
                "Depolama yazılabilir.",
                data: veri));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // ISTISNA MESAJI MASKELENIYOR
            //
            // Saglik ucu genellikle disaridan erişilebilir olur
            // (yuk dengeleyici cagiriyor). IO istisnalari TAM DOSYA
            // YOLUNU iceriyor:
            //   "Access to the path 'C:\...\uploads\...' is denied."
            //
            // Sunucu dizin yapisini disariya acmak, Sprint 15'te
            // stack trace için verdigim kararin aynisi -- burada da
            // maskeliyorum.
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "Depolamaya yazılamıyor.",
                exception: null,
                data: new Dictionary<string, object>
                {
                    ["error"] = SensitiveDataMasker.Mask(ex.GetType().Name),
                }));
        }
    }
}
