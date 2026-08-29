using Serilog;
using Serilog.Events;
using Ticketing.WebApi.Middleware;

namespace Ticketing.WebApi.Observability;

/// <summary>
/// Serilog yapilandirmasi. PDF Sprint 16.
/// </summary>
/// <remarks>
/// SERILOG NEYI DEGISTIRIYOR, NEYI DEGISTIRMIYOR?
///
/// DEGISTIRMEDIGI: kodumuzdaki tek bir log satiri bile. Her yerde
/// ILogger ve [LoggerMessage] kullanmaya devam ediyorum. Serilog,
/// Microsoft.Extensions.Logging'in ARKASINA geciyor.
///
/// DEGISTIRDIGI: o loglarin nereye ve hangi bicimde yazildigi.
///
/// Kazandigim sey YAPILANDIRILMIS (structured) log. Fark su:
///
///   Duz metin:
///     "Rezervasyon oluşturuldu. Id: abc-123, Koltuk: 4"
///     -> tek bir metin. Aramak için grep, ayristirmak için regex.
///
///   Yapilandirilmis (JSON):
///     { "Message": "...", "ReservationId": "abc-123", "SeatCount": 4,
///       "CorrelationId": "9f2c...", "Level": "Information" }
///     -> ALANLARI olan bir kayıt. "SeatCount > 3 olan rezervasyonlar"
///        diye SORGU yazilabiliyor.
///
/// Mesaj sablonundaki {ReservationId} gibi yer tutucular otomatik
/// olarak alan adina donusuyor. Yani bu bicimi kazanmak için ekstra
/// hiçbir sey yazmiyorum -- zaten doğru şekilde logluyorduk.
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
                // SEVIYELER
                //
                // Varsayılan Information; framework gurultusu bastirilmis.
                //
                // Microsoft.AspNetCore Information seviyesinde her istek
                // için 2-3 satır uretiyor ("Request starting",
                // "Executing endpoint", "Request finished"). Bunlari
                // zaten kendi istek logumuzla (aşağıda) tek satirda
                // topluyorum.
                //
                // Bastirmasaydim: günde milyonlarca gereksiz satır,
                // hem maliyet hem de GERCEK loglarin gorunmez olmasını.
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
                .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)

                // Hangfire her is için birden fazla Information satiri
                // uretiyor. Bizim kendi is loglarim (9101-9106) zaten
                // anlamlı olani söylüyor.
                .MinimumLevel.Override("Hangfire", LogEventLevel.Warning)

                // ZENGINLESTIRICILER (enrichers)
                //
                // Her log satirina otomatik olarak eklenen alanlar.
                //
                // Neden gerekli? Uretimde birden fazla sunucu (instance)
                // çalışıyor ve loglar TEK bir yerde toplaniyor. "Bu hata
                // hangi makinede oldu?" sorusunu cevaplayamazsak, tek
                // bir bozuk sunucuyu bulmak imkansiz olur.
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Application", "Ticketing.Api")
                .Enrich.WithProperty("Environment", context.HostingEnvironment.EnvironmentName)

                // KONSOL
                //
                // Gelistirmede insan tarafından okunuyor, o yüzden duz
                // metin. JSON yazsaydım gelistirme deneyimi berbat
                // olurdu -- her satır 400 karakterlik bir JSON blogu.
                .WriteTo.Console(
                    // CA1305: bicimlendirme kullanıcının yerel ayarina
                    // göre degismemeli. Log zaman damgalari ve sayilar
                    // MAKINE tarafından okunuyor; Turkce yerel ayarda
                    // ondalik ayirici virgul olur ve ayristirma bozulur.
                    formatProvider: System.Globalization.CultureInfo.InvariantCulture,
                    outputTemplate:
                        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} " +
                        "{Properties:j}{NewLine}{Exception}")

                // DOSYA -- JSON, günlük donen
                //
                // Burada JSON kullanıyorum çünkü bu dosyalar MAKINE
                // tarafından okunuyor: merkezi log sistemine (Seq, ELK)
                // aktarilacak veya jq ile sorgulanacak.
                //
                // rollingInterval: günlük yeni dosya. Tek bir dev dosya
                // olsaydı acmak bile zor olurdu.
                //
                // retainedFileCountLimit: 14 gün. Sinirsiz birakmak
                // diski doldurur -- Sprint 15'te dosya yuklemede
                // konustugum sorunun aynisi, ama bu kez KENDİ
                // urettigim veriyle.
                .WriteTo.File(
                    formatter: new Serilog.Formatting.Compact.CompactJsonFormatter(),
                    path: Path.Combine(AppContext.BaseDirectory, "logs", "ticketing-.json"),
                    rollingInterval: Serilog.RollingInterval.Day,
                    retainedFileCountLimit: 14,

                    // Tek günde bir dosyanin buyuyebilecegi ust sinir.
                    // Asilirsa aynı gün içinde yeni dosya aciliyor.
                    fileSizeLimitBytes: 100 * 1024 * 1024,
                    rollOnFileSizeLimit: true);
        });
    }

    /// <summary>
    /// Her HTTP isteği için TEK satirlik özet log ekler.
    /// </summary>
    /// <remarks>
    /// NEDEN KENDİ OZETIMIZ?
    ///
    /// ASP.NET Core'un yerlesik istek loglamasi aynı istek için
    /// birden fazla satır uretiyor ve hicbiri süreyi net vermiyor.
    /// Serilog'un UseSerilogRequestLogging'i ise tek satirda
    /// yol + durum kodu + süre veriyor.
    ///
    /// Ustune kendi alanlarimi ekliyorum: CorrelationId ve
    /// kullanıcı kimliği. Boylece tek bir satirdan "kim, neyi, ne
    /// kadar surede" sorularinin hepsi cevaplaniyor.
    /// </remarks>
    public static void UseRequestLogging(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseSerilogRequestLogging(options =>
        {
            options.MessageTemplate =
                "{RequestMethod} {RequestPath} -> {StatusCode} ({Elapsed:0.0} ms)";

            // SEVIYE, DURUM KODUNA GORE
            //
            // Hepsini Information yapsaydim 500'ler normal isteklerin
            // arasında kaybolurdu. Sprint 15'te "alarm yorgunlugu"
            // baglaminda konustugum ayrimin aynı si.
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
                // probe'lari). Information yapsaydim loglarin büyük
                // kismi bu gurultu olurdu.
                //
                // Debug'a dusuruyorum: sorun olduğunda acilabiliyor,
                // normalde gorunmuyor.
                if (httpContext.Request.Path.StartsWithSegments("/health"))
                {
                    return LogEventLevel.Debug;
                }

                return LogEventLevel.Information;
            };

            options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
            {
                // PDF: correlation ID "Application log" içinde olmalı.
                if (httpContext.Response.Headers.TryGetValue(
                        CorrelationIdMiddleware.HeaderName, out var correlationId))
                {
                    diagnosticContext.Set("CorrelationId", correlationId.ToString());
                }

                // Kullanıcı KIMLIGI (Guid), e-postası DEĞİL.
                //
                // Sprint 15'te konustugum gerekce: e-posta kisisel
                // veri. Guid ise anlamsiz bir tanimlayici -- destek
                // gerektiginde veritabanindan kullanıcıya cevrilebilir
                // ama log dosyasi tek başına bir kullanıcı listesi
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
