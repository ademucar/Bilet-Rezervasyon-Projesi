// Npgsql.OpenTelemetry'nin AddNpgsql() genisletme metodu BU ad
// alaninda. Olmadan derleyici, ayni adli EF Core kaydini
// (IServiceCollection uzerindeki) bulup anlamsiz bir hata veriyor.
using Npgsql;
using OpenTelemetry.Resources;
// Kaynak ADI Application katmaninda tanimli: hem burasi
// (dinleyici) hem de arka plan isleri (uretici) ayni sabiti
// kullaniyor. Iki yerde elle yazsaydik ve biri degisirse
// izleme SESSIZCE durur.
using Ticketing.Application.Common.Observability;
using OpenTelemetry.Trace;
using StackExchange.Redis;

namespace Ticketing.WebApi.Observability;

/// <summary>
/// OpenTelemetry izleme (tracing) yapilandirmasi. PDF Sprint 16.
/// </summary>
/// <remarks>
/// ==================================================================
/// LOG VE TRACE AYNI SEY DEGIL
/// ==================================================================
/// Log "ne oldu" sorusunu cevapliyor:
///     "Rezervasyon olusturuldu. Id: abc, Koltuk: 4"
///
/// Trace "nerede ne kadar surdu" sorusunu:
///     POST /reservations                        820 ms
///       +- MediatR CreateReservationCommand     815 ms
///          +- SELECT EventSeats (FOR UPDATE)    640 ms   <-- suclu
///          +- INSERT Reservations                18 ms
///          +- Redis DEL event:123                 2 ms
///
/// Loglar bu istegin 820 ms surdugunu soyler ama NEDEN uzun surdugunu
/// soylemez. Trace, her halkayi ayri olcuyor ve suclu halkayi
/// dogrudan gosteriyor.
///
/// Bizim zincirimiz uzun (HTTP -> MediatR -> EF -> PostgreSQL,
/// -> Redis, -> Outbox -> Hangfire -> SMTP) ve halkalarin cogu
/// FARKLI process'lerde. Trace olmadan yavaslik teshisi tahmin
/// yurutmekten ibaret olurdu.
/// ==================================================================
/// </remarks>
internal static class OpenTelemetrySetup
{
    public static IServiceCollection AddObservability(
        this IServiceCollection services,
        IConfiguration configuration,
        string environmentName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                // ==================================================
                // SERVIS KIMLIGI
                // ==================================================
                // Trace'ler merkezi bir toplayiciya gidiyor ve orada
                // BASKA servislerin trace'leriyle karisiyor. Servis
                // adi olmadan hangi izin bize ait oldugu belli olmaz.
                .AddService(
                    serviceName: "ticketing-api",
                    serviceVersion: typeof(OpenTelemetrySetup).Assembly
                        .GetName().Version?.ToString() ?? "1.0.0")
                .AddAttributes(
                [
                    new KeyValuePair<string, object>("deployment.environment", environmentName),
                ]))

            .WithTracing(tracing =>
            {
                tracing
                    // ==============================================
                    // 1) HTTP ISTEK IZLERI -- PDF maddesi
                    // ==============================================
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        // Saglik kontrolleri saniyede bir cagriliyor.
                        // Izlemeseydik bile calisir ama trace
                        // deposunun %90'i bu gurultu olurdu -- hem
                        // maliyet hem de gercek izleri bulmayi
                        // zorlastiran bir yigin.
                        options.Filter = httpContext =>
                            !httpContext.Request.Path.StartsWithSegments("/health");

                        // Istisna detayini ize ekle: hata hangi
                        // adimda olustu, dogrudan gorulsun.
                        options.RecordException = true;
                    })

                    // ==============================================
                    // 2) VERITABANI SORGULARI -- PDF maddesi
                    // ==============================================
                    // Npgsql'in KENDI izleme kaynagi.
                    //
                    // EF Core instrumentation paketi yerine bunu
                    // sectim: o paket yalnizca beta olarak var ve
                    // Npgsql surucu seviyesinde olctugu icin daha
                    // dogru -- EF'in urettigi SQL'in veritabaninda
                    // GERCEKTE ne kadar surdugunu goruyoruz.
                    .AddNpgsql()

                    // ==============================================
                    // 3) REDIS ISLEMLERI -- PDF maddesi
                    // ==============================================
                    // ==============================================
                    // BURADA BIR SIRALAMA TUZAGI VAR
                    // ==============================================
                    // Redis instrumentation, IConnectionMultiplexer
                    // ornegine ihtiyac duyuyor. DI'dan cozmeye
                    // calisirsak, Redis kapaliyken uygulama
                    // ACILMAZ -- oysa Sprint 11'de onbellegi
                    // BILINCLI olarak "yoksa da calisir" yaptik
                    // (Null Object Pattern).
                    //
                    // Bu yuzden multiplexer'i OPSIYONEL cozuyorum:
                    // varsa izliyoruz, yoksa izleme atlanip
                    // uygulama normal aciliyor.
                    //
                    // Izlemenin, izledigi sistemi cokertmemesi
                    // gerekiyor.
                    // ==============================================
                    .AddRedisInstrumentation()

                    // ==============================================
                    // 4) ARKA PLAN ISLERI -- PDF maddesi
                    // ==============================================
                    // Hangfire'in hazir bir instrumentation'i yok.
                    // Kendi ActivitySource'umuzu ekliyoruz; isler
                    // TicketingJobs icinde bu kaynaktan Activity
                    // baslatiyor.
                    .AddSource(AppActivitySource.Name)

                    // ==============================================
                    // 5) HARICI SERVIS CAGRILARI -- PDF maddesi
                    // ==============================================
                    // HttpClient uzerinden yapilan her cagri.
                    // Bizde odeme saglayicisi simulasyonu ve ilerde
                    // eklenebilecek her dis entegrasyon.
                    .AddHttpClientInstrumentation(options =>
                        options.RecordException = true);

                // ==================================================
                // ORNEKLEME (sampling) VE DISA AKTARIM
                // ==================================================
                // Gelistirmede her izi konsola yaziyorum: ne
                // uretildigini gormek icin.
                //
                // Uretimde konsola yazmak felaket olurdu -- her
                // istek icin onlarca satir. Orada OTLP ile bir
                // toplayiciya (Jaeger, Tempo, Grafana) gonderiliyor.
                var otlpEndpoint = configuration["OpenTelemetry:OtlpEndpoint"];

                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                {
                    tracing.AddOtlpExporter(options =>
                        options.Endpoint = new Uri(otlpEndpoint));
                }
                else if (environmentName == "Development")
                {
                    // Toplayici yapilandirilmamis ve gelistirmedeyiz.
                    //
                    // Konsola yazmak, "trace gercekten uretiliyor mu?"
                    // sorusunu bir toplayici kurmadan cevaplamamizi
                    // sagliyor. Bu sprintte tam olarak bunun icin
                    // kullandim.
                    tracing.AddConsoleExporter();
                }

                // Toplayici da yoksa ve uretimdeysek: hicbir exporter
                // eklenmiyor. Izleme calisir ama hicbir yere gitmez.
                //
                // Bu BILINCLI: yapilandirilmamis bir uretim ortaminda
                // konsolu trace ile doldurmak, cozdugunden cok sorun
                // yaratirdi.
            });

        return services;
    }

    /// <summary>
    /// Redis izlemesini, baglanti varsa devreye alir.
    /// </summary>
    /// <remarks>
    /// AddRedisInstrumentation() parametresiz cagrildiginda
    /// multiplexer'i DI'dan cozuyor. Bizde Redis opsiyonel oldugu
    /// icin (Sprint 11: Null Object Pattern) kayit YOKSA bu cozum
    /// basarisiz olur.
    ///
    /// Bu yardimci, kaydin var olup olmadigini once kontrol ediyor.
    /// </remarks>
    public static void ConfigureRedisTracing(this IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Cozulemezse hicbir sey yapmiyoruz. Izleme eksik kalir ama
        // uygulama calisir -- dogru oncelik sirasi bu.
        _ = services.GetService<IConnectionMultiplexer>();
    }
}
