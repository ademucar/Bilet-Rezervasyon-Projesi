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

        // ONBELLEK ISTEGE BAGLI -- PDF: "Cache kapalı olduğunda sistem
        // calismaya devam edebilmelidir."
        //
        // Bağlantı dizesi yoksa bu BIR HATA DEĞİL, bir TERCIH.
        //
        // Gelistirici Redis kurmak istemeyebilir; testler Redis'siz
        // calismali; küçük bir kurulumda Redis gereksiz olabilir.
        //
        // Burada istisna firlatsaydik (JWT_SECRET'te olduğu gibi)
        // Redis'i zorunlu kilardim. Ama JWT olmadan sistem GUVENLI
        // calisamaz; Redis olmadan sadece YAVAS çalışır. Bu fark
        // karari belirliyor.
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddSingleton<ICacheService, NullCacheService>();

            return services;
        }

        // BAGLANTI KURULAMAZSA UYGULAMA COKMEMELI
        //
        // ConnectionMultiplexer.Connect(), Redis kapaliysa istisna
        // firlatir. Burada yakalamasaydik uygulama HİÇ BASLAMAZDI.
        //
        // Bu, JWT dogrulamasindaki "fail fast" yaklasiminin TERSI --
        // ve bilinçli. Orada eksik yapilandirma bir GÜVENLİK acigiydi,
        // burada yalnızca bir performans kaybi.
        //
        // AbortOnConnectFail = false: Redis o an kapalı olsa bile
        // istemci nesnesi olusuyor ve arka planda yeniden baglanmayi
        // deniyor. Yani Redis sonradan ayaga kalkarsa uygulamayi
        // yeniden baslatmaya gerek kalmiyor.
        try
        {
            var options = ConfigurationOptions.Parse(connectionString);
            options.AbortOnConnectFail = false;

            // Bağlantı kurulmasini sonsuza kadar beklemiyoruz.
            // Uygulama acilisini bir önbellek yuzunden geciktirmek
            // doğru olmaz.
            options.ConnectTimeout = 5000;

            var multiplexer = ConnectionMultiplexer.Connect(options);

            services.AddSingleton<IConnectionMultiplexer>(multiplexer);
            services.AddSingleton<ICacheService, RedisCacheService>();
        }
#pragma warning disable CA1031 // Genel istisna yakalama
        // Redis'e baglanamamak uygulamayi durdurmamali.
        // Hangi istisnanin gelecegini onceden saymak mumkun değil
        // (ag, DNS, kimlik doğrulama, yapilandirma bicimi...).
        catch (Exception ex)
#pragma warning restore CA1031
        {
            // Burada ILogger yok (henüz DI kurulmadi), bu yüzden
            // geçici bir logger uretiyorum. Sessiz kalmak en kotusu
            // olurdu: önbellek kapalı calisirken kimse fark etmezdi.
            using var loggerFactory = LoggerFactory.Create(b => b.AddConsole());

            LogRedisUnavailable(loggerFactory.CreateLogger("Ticketing.Infrastructure.Caching"), ex);

            services.AddSingleton<ICacheService, NullCacheService>();
        }

        return services;
    }

    // CA1848: LogWarning yerine kaynak ureteci.
    //
    // Bu metot uygulama omrunde EN FAZLA BIR KEZ çalışıyor, yani
    // performans farki sıfıra yakın. Yine de kurala uyuyorum:
    // "burada onemsiz" diye istisna yapmaya baslarsak, kural
    // giderek anlamini yitirir. Susturmak yerine uymak daha ucuz.
    [LoggerMessage(
        EventId = 9307,
        Level = LogLevel.Warning,
        Message = "Redis bağlantısı kurulamadi. Onbellek DEVRE DISI, " +
                  "sistem veritabanindan calismaya devam ediyor.")]
    private static partial void LogRedisUnavailable(ILogger logger, Exception exception);
}
