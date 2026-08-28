using Serilog;
using Serilog.Events;
using Ticketing.WebApi.Middleware;

namespace Ticketing.WebApi.Observability;

/// <summary>
/// Serilog yapilandirmasi. PDF Sprint 16.
/// </summary>
/// <remarks>
/// ==================================================================
/// SERILOG NEYI DEGISTIRIYOR, NEYI DEGISTIRMIYOR?
/// ==================================================================
/// DEGISTIRMEDIGI: kodumuzdaki tek bir log satiri bile. Her yerde
/// ILogger ve [LoggerMessage] kullanmaya devam ediyoruz. Serilog,
/// Microsoft.Extensions.Logging'in ARKASINA geciyor.
///
/// DEGISTIRDIGI: o loglarin nereye ve hangi bicimde yazildigi.
///
/// Kazandigimiz sey YAPILANDIRILMIS (structured) log. Fark su:
///
///   Duz metin:
///     "Rezervasyon olusturuldu. Id: abc-123, Koltuk: 4"
///     -> tek bir metin. Aramak icin grep, ayristirmak icin regex.
///
///   Yapilandirilmis (JSON):
///     { "Message": "...", "ReservationId": "abc-123", "SeatCount": 4,
///       "CorrelationId": "9f2c...", "Level": "Information" }
///     -> ALANLARI olan bir kayit. "SeatCount > 3 olan rezervasyonlar"
///        diye SORGU yazilabiliyor.
///
/// Mesaj sablonundaki {ReservationId} gibi yer tutucular otomatik
/// olarak alan adina donusuyor. Yani bu bicimi kazanmak icin ekstra
/// hicbir sey yazmiyoruz -- zaten dogru sekilde logluyorduk.
/// ==================================================================
/// </remarks>
internal static class SerilogSetup
{
    /// <summary>
    /// Serilog'u yapilandirir ve konak (host) ile birlestirir.
    /// </summary>
    public static void AddSerilogLogging(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Host.UseSerilog((context, services, configuration) =>
        {
            configuration
                // ======================================================
                // SEVIYELER
                // ======================================================
                // Varsayilan Information; framework gurultusu bastirilmis.
                //
                // Microsoft.AspNetCore Information seviyesinde her istek
                // icin 2-3 satir uretiyor ("Request starting",
                // "Executing endpoint", "Request finished"). Bunlari
                // zaten kendi istek logumuzla (asagida) tek satirda
                // topluyoruz.
                //
                // Bastirmasaydik: gunde milyonlarca gereksiz satir,
                // hem maliyet hem de GERCEK loglarin gorunmez olmasi.
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
                .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)

                // Hangfire her is icin birden fazla Information satiri
                // uretiyor. Bizim kendi is loglarimiz (9101-9106) zaten
                // anlamli olani soyluyor.
                .MinimumLevel.Override("Hangfire", LogEventLevel.Warning)

                // ======================================================
                // ZENGINLESTIRICILER (enrichers)
                // ======================================================
                // Her log satirina otomatik olarak eklenen alanlar.
                //
                // Neden gerekli? Uretimde birden fazla sunucu (instance)
                // calisiyor ve loglar TEK bir yerde toplaniyor. "Bu hata
                // hangi makinede oldu?" sorusunu cevaplayamazsak, tek
                // bir bozuk sunucuyu bulmak imkansiz olur.
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Application", "Ticketing.Api")
                .Enrich.WithProperty("Environment", context.HostingEnvironment.EnvironmentName)

                // ======================================================
                // KONSOL
                // ======================================================
                // Gelistirmede insan tarafindan okunuyor, o yuzden duz
                // metin. JSON yazsaydik gelistirme deneyimi berbat
                // olurdu -- her satir 400 karakterlik bir JSON blogu.
                .WriteTo.Console(
                    // CA1305: bicimlendirme kullanicinin yerel ayarina
                    // gore degismemeli. Log zaman damgalari ve sayilar
                    // MAKINE tarafindan okunuyor; Turkce yerel ayarda
                    // ondalik ayirici virgul olur ve ayristirma bozulur.
                    formatProvider: System.Globalization.CultureInfo.InvariantCulture,
                    outputTemplate:
                        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} " +
                        "{Properties:j}{NewLine}{Exception}")

                // ======================================================
                // DOSYA -- JSON, gunluk donen
                // ======================================================
                // Burada JSON kullaniyorum cunku bu dosyalar MAKINE
                // tarafindan okunuyor: merkezi log sistemine (Seq, ELK)
                // aktarilacak veya jq ile sorgulanacak.
                //
                // rollingInterval: gunluk yeni dosya. Tek bir dev dosya
                // olsaydi acmak bile zor olurdu.
                //
                // retainedFileCountLimit: 14 gun. Sinirsiz birakmak
                // diski doldurur -- Sprint 15'te dosya yuklemede
                // konustugumuz sorunun aynisi, ama bu kez KENDI
                // urettigimiz veriyle.
                .WriteTo.File(
                    formatter: new Serilog.Formatting.Compact.CompactJsonFormatter(),
                    path: Path.Combine(AppContext.BaseDirectory, "logs", "ticketing-.json"),
                    rollingInterval: Serilog.RollingInterval.Day,
                    retainedFileCountLimit: 14,

                    // Tek gunde bir dosyanin buyuyebilecegi ust sinir.
                    // Asilirsa ayni gun icinde yeni dosya aciliyor.
                    fileSizeLimitBytes: 100 * 1024 * 1024,
                    rollOnFileSizeLimit: true);
        });
    }

    /// <summary>
    /// Her HTTP istegi icin TEK satirlik ozet log ekler.
    /// </summary>
    /// <remarks>
    /// ==============================================================
    /// NEDEN KENDI OZETIMIZ?
    /// ==============================================================
    /// ASP.NET Core'un yerlesik istek loglamasi ayni istek icin
    /// birden fazla satir uretiyor ve hicbiri sureyi net vermiyor.
    /// Serilog'un UseSerilogRequestLogging'i ise tek satirda
    /// yol + durum kodu + sure veriyor.
    ///
    /// Ustune kendi alanlarimizi ekliyorum: CorrelationId ve
    /// kullanici kimligi. Boylece tek bir satirdan "kim, neyi, ne
    /// kadar surede" sorularinin hepsi cevaplaniyor.
    /// ==============================================================
    /// </remarks>
    public static void UseRequestLogging(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseSerilogRequestLogging(options =>
        {
            options.MessageTemplate =
                "{RequestMethod} {RequestPath} -> {StatusCode} ({Elapsed:0.0} ms)";

            // ==========================================================
            // SEVIYE, DURUM KODUNA GORE
            // ==========================================================
            // Hepsini Information yapsaydik 500'ler normal isteklerin
            // arasinda kaybolurdu. Sprint 15'te "alarm yorgunlugu"
            // baglaminda konustugumuz ayrimin ayni si.
            options.GetLevel = (httpContext, elapsed, ex) =>
            {
                if (ex is not null || httpContext.Response.StatusCode >= 500)
                {
                    return LogEventLevel.Error;
                }

                if (httpContext.Response.StatusCode >= 400)
                {
                    return LogEventLevel.Warning;
                }

                // Saglik kontrolleri saniyede bir cagriliyor (Kubernetes
                // probe'lari). Information yapsaydik loglarin buyuk
                // kismi bu gurultu olurdu.
                //
                // Debug'a dusuruyorum: sorun oldugunda acilabiliyor,
                // normalde gorunmuyor.
                if (httpContext.Request.Path.StartsWithSegments("/health"))
                {
                    return LogEventLevel.Debug;
                }

                return LogEventLevel.Information;
            };

            options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
            {
                // PDF: correlation ID "Application log" icinde olmali.
                if (httpContext.Response.Headers.TryGetValue(
                        CorrelationIdMiddleware.HeaderName, out var correlationId))
                {
                    diagnosticContext.Set("CorrelationId", correlationId.ToString());
                }

                // Kullanici KIMLIGI (Guid), e-postasi DEGIL.
                //
                // Sprint 15'te konustugumuz gerekce: e-posta kisisel
                // veri. Guid ise anlamsiz bir tanimlayici -- destek
                // gerektiginde veritabanindan kullaniciya cevrilebilir
                // ama log dosyasi tek basina bir kullanici listesi
                // olmaz.
                var userId = httpContext.User?.FindFirst("sub")?.Value;

                if (!string.IsNullOrEmpty(userId))
                {
                    diagnosticContext.Set("UserId", userId);
                }
            };
        });
    }
}
