// Npgsql.OpenTelemetry'nin AddNpgsql() genisletme metodu BU ad
// alaninda. Olmadan derleyici, aynı adli EF Core kaydini
// (IServiceCollection uzerindeki) bulup anlamsiz bir hata veriyor.
using Npgsql;
using OpenTelemetry.Resources;
// Kaynak ADI Application katmaninda tanimli: hem burasi
// (dinleyici) hem de arka plan isleri (uretici) aynı sabiti
// kullaniyor. Iki yerde elle yazsaydım ve biri degisirse
// izleme SESSIZCE durur.
using Ticketing.Application.Common.Observability;
using OpenTelemetry.Trace;
using StackExchange.Redis;

namespace Ticketing.WebApi.Observability;

/// <summary>
/// OpenTelemetry izleme (tracing) yapilandirmasi. PDF Sprint 16.
/// </summary>
/// <remarks>
/// LOG VE TRACE AYNI SEY DEĞİL
///
/// Log "ne oldu" sorusunu cevapliyor:
///     "Rezervasyon oluşturuldu. Id: abc, Koltuk: 4"
///
/// Trace "nerede ne kadar surdu" sorusunu:
///     POST /reservations                        820 ms
///       +- MediatR CreateReservationCommand     815 ms
///          +- SELECT EventSeats (FOR UPDATE)    640 ms   &lt;-- suclu
///          +- INSERT Reservations                18 ms
///          +- Redis DEL event:123                 2 ms
///
/// Loglar bu istegin 820 ms surdugunu söyler ama NEDEN uzun surdugunu
/// soylemez. Trace, her halkayi ayrı olcuyor ve suclu halkayi
/// doğrudan gosteriyor.
///
/// Bizim zincirimiz uzun (HTTP -> MediatR -> EF -> PostgreSQL,
/// -> Redis, -> Outbox -> Hangfire -> SMTP) ve halkalarin çoğu
/// FARKLI process'lerde. Trace olmadan yavaslik teshisi tahmin
/// yurutmekten ibaret olurdu.
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
                // SERVIS KIMLIGI
                //
                // Trace'ler merkezi bir toplayiciya gidiyor ve orada
                // BASKA servislerin trace'leriyle karisiyor. Servis
                // adı olmadan hangi izin bana ait olduğu belli olmaz.
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
                    // 1) HTTP ISTEK IZLERI -- PDF maddesi
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        // Saglik kontrolleri saniyede bir cagriliyor.
                        // Izlemeseydik bile çalışır ama trace
                        // deposunun %90'i bu gurultu olurdu -- hem
                        // maliyet hem de gerçek izleri bulmayi
                        // zorlastiran bir yigin.
                        options.Filter = httpContext =>
                            !httpContext.Request.Path.StartsWithSegments("/health");

                        // Istisna detayını ize ekle: hata hangi
                        // adımda oluştu, doğrudan gorulsun.
                        options.RecordException = true;
                    })

                    // 2) VERITABANI SORGULARI -- PDF maddesi
                    //
                    // Npgsql'in KENDİ izleme kaynagi.
                    //
                    // EF Core instrumentation paketi yerine bunu
                    // sectim: o paket yalnızca beta olarak var ve
                    // Npgsql surucu seviyesinde olctugu için daha
                    // doğru -- EF'in urettigi SQL'in veritabaninda
                    // GERCEKTE ne kadar surdugunu goruyoruz.
                    .AddNpgsql()

                    // 3) REDIS ISLEMLERI -- PDF maddesi
                    //
                    // BURADA BIR SIRALAMA TUZAGI VAR
                    //
                    // Redis instrumentation, IConnectionMultiplexer
                    // ornegine ihtiyac duyuyor. DI'dan cozmeye
                    // calisirsak, Redis kapaliyken uygulama
                    // ACILMAZ -- oysa Sprint 11'de önbelleği
                    // BILINCLI olarak "yoksa da çalışır" yaptim
                    // (Null Object Pattern).
                    //
                    // Bu yüzden multiplexer'i OPSIYONEL cozuyorum:
                    // varsa izliyoruz, yoksa izleme atlanip
                    // uygulama normal aciliyor.
                    //
                    // Izlemenin, izledigi sistemi cokertmemesi
                    // gerekiyor.
                    .AddRedisInstrumentation()

                    // 4) ARKA PLAN ISLERI -- PDF maddesi
                    //
                    // Hangfire'in hazır bir instrumentation'i yok.
                    // Kendi ActivitySource'umuzu ekliyorum; isler
                    // TicketingJobs içinde bu kaynaktan Activity
                    // baslatiyor.
                    .AddSource(AppActivitySource.Name)

                    // 5) HARICI SERVIS CAGRILARI -- PDF maddesi
                    //
                    // HttpClient üzerinden yapilan her cagri.
                    // Bizde ödeme sağlayıcısı simülasyonu ve ilerde
                    // eklenebilecek her dis entegrasyon.
                    .AddHttpClientInstrumentation(options =>
                        options.RecordException = true);

                // ORNEKLEME (sampling) VE DISA AKTARIM
                //
                // Gelistirmede her izi konsola yazıyorum: ne
                // uretildigini gormek için.
                //
                // Uretimde konsola yazmak felaket olurdu -- her
                // istek için onlarca satır. Orada OTLP ile bir
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
                    // Konsola yazmak, "trace gerçekten uretiliyor mu?"
                    // sorusunu bir toplayici kurmadan cevaplamamizi
                    // sagliyor. Bu sprintte tam olarak bunun için
                    // kullandim.
                    tracing.AddConsoleExporter();
                }

                // Toplayici da yoksa ve uretimdeysek: hiçbir exporter
                // eklenmiyor. Izleme çalışır ama hiçbir yere gitmez.
                //
                // Bu BILINCLI: yapilandirilmamis bir üretim ortaminda
                // konsolu trace ile doldurmak, cozdugunden çok sorun
                // yaratirdi.
            });

        return services;
    }

    /// <summary>
    /// Redis izlemesini, bağlantı varsa devreye alır.
    /// </summary>
    /// <remarks>
    /// AddRedisInstrumentation() parametresiz cagrildiginda
    /// multiplexer'i DI'dan cozuyor. Bizde Redis opsiyonel olduğu
    /// için (Sprint 11: Null Object Pattern) kayıt YOKSA bu çözüm
    /// başarısız olur.
    ///
    /// Bu yardimci, kaydin var olup olmadigini önce kontrol ediyor.
    /// </remarks>
    public static void ConfigureRedisTracing(this IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Cozulemezse hiçbir sey yapmiyorum. Izleme eksik kalır ama
        // uygulama çalışır -- doğru oncelik sırası bu.
        _ = services.GetService<IConnectionMultiplexer>();
    }
}
