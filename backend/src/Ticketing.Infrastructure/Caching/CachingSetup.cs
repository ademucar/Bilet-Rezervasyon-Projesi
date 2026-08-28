using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Ticketing.Application.Abstractions.Caching;

namespace Ticketing.Infrastructure.Caching;

/// <summary>
/// Onbellek kurulumu. PDF Sprint 11.
/// </summary>
public static partial class CachingSetup
{
    public static IServiceCollection AddCaching(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("Redis");

        // ==============================================================
        // ONBELLEK ISTEGE BAGLI -- PDF: "Cache kapali oldugunda sistem
        // calismaya devam edebilmelidir."
        // ==============================================================
        // Baglanti dizesi yoksa bu BIR HATA DEGIL, bir TERCIH.
        //
        // Gelistirici Redis kurmak istemeyebilir; testler Redis'siz
        // calismali; kucuk bir kurulumda Redis gereksiz olabilir.
        //
        // Burada istisna firlatsaydik (JWT_SECRET'te oldugu gibi)
        // Redis'i zorunlu kilardik. Ama JWT olmadan sistem GUVENLI
        // calisamaz; Redis olmadan sadece YAVAS calisir. Bu fark
        // karari belirliyor.
        // ==============================================================
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddSingleton<ICacheService, NullCacheService>();

            return services;
        }

        // ==============================================================
        // BAGLANTI KURULAMAZSA UYGULAMA COKMEMELI
        // ==============================================================
        // ConnectionMultiplexer.Connect(), Redis kapaliysa istisna
        // firlatir. Burada yakalamasaydik uygulama HIC BASLAMAZDI.
        //
        // Bu, JWT dogrulamasindaki "fail fast" yaklasiminin TERSI --
        // ve bilincli. Orada eksik yapilandirma bir GUVENLIK acigiydi,
        // burada yalnizca bir performans kaybi.
        //
        // AbortOnConnectFail = false: Redis o an kapali olsa bile
        // istemci nesnesi olusuyor ve arka planda yeniden baglanmayi
        // deniyor. Yani Redis sonradan ayaga kalkarsa uygulamayi
        // yeniden baslatmaya gerek kalmiyor.
        // ==============================================================
        try
        {
            var options = ConfigurationOptions.Parse(connectionString);
            options.AbortOnConnectFail = false;

            // Baglanti kurulmasini sonsuza kadar beklemiyoruz.
            // Uygulama acilisini bir onbellek yuzunden geciktirmek
            // dogru olmaz.
            options.ConnectTimeout = 5000;

            var multiplexer = ConnectionMultiplexer.Connect(options);

            services.AddSingleton<IConnectionMultiplexer>(multiplexer);
            services.AddSingleton<ICacheService, RedisCacheService>();
        }
#pragma warning disable CA1031 // Genel istisna yakalama
        // Redis'e baglanamamak uygulamayi durdurmamali.
        // Hangi istisnanin gelecegini onceden saymak mumkun degil
        // (ag, DNS, kimlik dogrulama, yapilandirma bicimi...).
        catch (Exception ex)
#pragma warning restore CA1031
        {
            // Burada ILogger yok (henuz DI kurulmadi), bu yuzden
            // gecici bir logger uretiyorum. Sessiz kalmak en kotusu
            // olurdu: onbellek kapali calisirken kimse fark etmezdi.
            using var loggerFactory = LoggerFactory.Create(b => b.AddConsole());

            LogRedisUnavailable(loggerFactory.CreateLogger("Ticketing.Infrastructure.Caching"), ex);

            services.AddSingleton<ICacheService, NullCacheService>();
        }

        return services;
    }

    // CA1848: LogWarning yerine kaynak ureteci.
    //
    // Bu metot uygulama omrunde EN FAZLA BIR KEZ calisiyor, yani
    // performans farki sifira yakin. Yine de kurala uyuyorum:
    // "burada onemsiz" diye istisna yapmaya baslarsak, kural
    // giderek anlamini yitirir. Susturmak yerine uymak daha ucuz.
    [LoggerMessage(
        EventId = 9307,
        Level = LogLevel.Warning,
        Message = "Redis baglantisi kurulamadi. Onbellek DEVRE DISI, " +
                  "sistem veritabanindan calismaya devam ediyor.")]
    private static partial void LogRedisUnavailable(ILogger logger, Exception exception);
}
