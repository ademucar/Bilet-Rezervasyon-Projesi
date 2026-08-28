using Microsoft.Extensions.Diagnostics.HealthChecks;
using Ticketing.Application.Common.Security;

namespace Ticketing.WebApi.Observability;

/// <summary>
/// Saglik kontrolleri. PDF Sprint 16.
/// </summary>
/// <remarks>
/// ==================================================================
/// UC UC, UC FARKLI SORU
/// ==================================================================
/// PDF ucunu de istiyor ve ucu de farkli bir soruya cevap veriyor.
/// Aralarindaki farki bilmemek, uretimde en can sikici hatalardan
/// birine yol aciyor.
///
///   GET /health/live   "Process ayakta mi?"
///        -> HICBIR bagimliligi kontrol ETMEZ.
///        -> Kubernetes bunu kullanir ve BASARISIZ olursa
///           kapsayiciyi OLDURUP yeniden baslatir.
///
///   GET /health/ready  "Trafik alabilir miyim?"
///        -> Veritabani, Redis, Hangfire, disk kontrol edilir.
///        -> Kubernetes bunu kullanir ve basarisiz olursa
///           kapsayiciyi OLDURMEZ, sadece yuk dengeleyiciden
///           CIKARIR.
///
///   GET /health        Insan icin: her seyin ozeti.
///
/// ------------------------------------------------------------------
/// BU AYRIM NEDEN HAYATI? -- SOMUT FELAKET SENARYOSU
/// ------------------------------------------------------------------
/// Diyelim /health/live de veritabanini kontrol ettik.
///
/// PostgreSQL 30 saniye icin yanit vermez oldu (bakim, ag dalgalanmasi).
/// TUM kapsayicilarin live probe'u basarisiz olur. Kubernetes hepsini
/// birden oldurur. Yeniden baslarlar, veritabani hala yok, yine
/// olurler...
///
/// Sonuc: gecici bir veritabani sorunu, KALICI bir uygulama cokusune
/// donusur. Uygulama kendi kendini yeniden baslatarak duzeltemeyecegi
/// bir sey icin surekli yeniden baslatilir.
///
/// Dogru davranis: live gecer (process saglikli), ready kalir
/// (trafik alma). Veritabani donunce ready kendiliginden gecer ve
/// trafik geri gelir. Hicbir kapsayici oldurulmez.
/// ==================================================================
/// </remarks>
internal static class HealthChecksSetup
{
    /// <summary>
    /// Bagimlilik kontrollerinin etiketi.
    /// </summary>
    /// <remarks>
    /// Etiket kullanmamin sebebi: /health/ready yalnizca bu etikete
    /// sahip kontrolleri calistiriyor, /health/live ise HICBIRINI.
    ///
    /// Etiketsiz yapsaydik, her yeni saglik kontrolu otomatik olarak
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

        // ==============================================================
        // 1) VERITABANI -- PDF maddesi
        // ==============================================================
        var postgres = configuration.GetConnectionString("Postgres");

        if (!string.IsNullOrWhiteSpace(postgres))
        {
            builder.AddNpgSql(
                connectionString: postgres,

                // Varsayilan sorgu "SELECT 1". Onu birakiyorum:
                // amac veriyi dogrulamak degil, BAGLANTININ kurulup
                // sorgu calistirilabildigini gormek.
                //
                // Agir bir sorgu (ornegin COUNT(*)) yazmak cazip ama
                // yanlis olurdu: saglik kontrolu saniyede bir
                // calisiyor ve veritabanina yuk BINDIRMEMELI.
                name: "postgresql",
                failureStatus: HealthStatus.Unhealthy,
                tags: [ReadyTag]);
        }

        // ==============================================================
        // 2) REDIS -- PDF maddesi
        // ==============================================================
        var redis = configuration.GetConnectionString("Redis");

        if (!string.IsNullOrWhiteSpace(redis))
        {
            builder.AddRedis(
                redisConnectionString: redis,
                name: "redis",

                // ==========================================================
                // DEGRADED, UNHEALTHY DEGIL -- BILINCLI KARAR
                // ==========================================================
                // Sprint 11'de onbellegi BILINCLI olarak opsiyonel
                // yaptik: Redis yoksa sorgular veritabanindan
                // karsilaniyor (Null Object Pattern). Yani Redis
                // olmadan sistem YAVAS calisir, BOZUK calismaz.
                //
                // Unhealthy deseydik: Redis dustugunde /health/ready
                // basarisiz olur, Kubernetes tum kapsayicilari yuk
                // dengeleyiciden cikarir ve site TAMAMEN erisilemez
                // hale gelirdi.
                //
                // Yani calisabilecek bir sistemi, calismayan bir
                // onbellek yuzunden kapatmis olurduk. Degraded dogru
                // seviye: alarm uretir, trafigi kesmez.
                // ==========================================================
                failureStatus: HealthStatus.Degraded,
                tags: [ReadyTag]);
        }

        // ==============================================================
        // 3) ARKA PLAN ISLERI -- PDF maddesi
        // ==============================================================
        // Hangfire kontrolu iki sey soyluyor: depolama erisilebilir mi
        // ve CALISAN (worker) var mi.
        //
        // Ikincisi onemli: Hangfire kayitlari alir, kuyruga koyar ve
        // hicbir hata vermez -- ama isleyen kimse yoksa isler sonsuza
        // kadar bekler. Bu, SESSIZ bir arizadir; kullanici e-postasini
        // beklerken sistem "her sey yolunda" der.
        builder.AddHangfire(
            options =>
            {
                // En az bir sunucu calisiyor olmali.
                options.MinimumAvailableServers = 1;
            },
            name: "hangfire",

            // Degraded: arka plan isleri durdugunda web trafigi
            // etkilenmemeli. Kullanici hala bilet alabilir; yalnizca
            // e-posta gecikir. Trafigi kesmek durumu kotulestirirdi.
            failureStatus: HealthStatus.Degraded,
            tags: [ReadyTag]);

        // ==============================================================
        // 4) DEPOLAMA -- PDF maddesi
        // ==============================================================
        // Sprint 15'te dosya yukleme ekledik; Sprint 13'te rapor
        // disa aktarimi. Ikisi de DISKE yaziyor.
        //
        // Disk dolarsa: yukleme basarisiz olur, rapor uretilemez ve
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
/// Yukleme klasorunun yazilabilir ve diskin dolu olmadigini kontrol eder.
/// PDF Sprint 16: "Storage health check".
/// </summary>
internal sealed class StorageHealthCheck : IHealthCheck
{
    /// <summary>Bu esigin altinda disk alani kaldiysa uyariyoruz.</summary>
    /// <remarks>
    /// 500 MB, "hemen mudahale et" esigi. Sifir beklemek anlamsiz:
    /// disk gercekten dolduktan sonra uyarmak cok gec olur -- o anda
    /// log bile yazilamiyor olur.
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
                    $"Depolama klasoru yok: {_yol}"));
            }

            // ==========================================================
            // GERCEKTEN YAZMAYI DENIYORUZ
            // ==========================================================
            // Directory.Exists yeterli DEGIL: klasor var olabilir ama
            // salt okunur baglanmis olabilir (Docker volume ayari),
            // izinler degismis olabilir veya disk dolmus olabilir.
            //
            // Bunlarin hicbirini "klasor var mi" sorusu yakalamaz.
            // Tek guvenilir yol, gercek bir yazma denemesi.
            //
            // Gecici dosya adi Guid: es zamanli kontroller birbirinin
            // dosyasini silmesin.
            // ==========================================================
            var denemeDosyasi = Path.Combine(_yol, $".health-{Guid.NewGuid():N}");

            File.WriteAllBytes(denemeDosyasi, [0]);
            File.Delete(denemeDosyasi);

            // Bos alan kontrolu.
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
                "Depolama yazilabilir.",
                data: veri));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // ==========================================================
            // ISTISNA MESAJI MASKELENIYOR
            // ==========================================================
            // Saglik ucu genellikle disaridan erisilebilir olur
            // (yuk dengeleyici cagiriyor). IO istisnalari TAM DOSYA
            // YOLUNU iceriyor:
            //   "Access to the path 'C:\...\uploads\...' is denied."
            //
            // Sunucu dizin yapisini disariya acmak, Sprint 15'te
            // stack trace icin verdigimiz kararin aynisi -- burada da
            // maskeliyoruz.
            // ==========================================================
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "Depolamaya yazilamiyor.",
                exception: null,
                data: new Dictionary<string, object>
                {
                    ["error"] = SensitiveDataMasker.Mask(ex.GetType().Name),
                }));
        }
    }
}
