using System.Text.Json;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Ticketing.Application.Abstractions.Caching;

namespace Ticketing.Infrastructure.Caching;

/// <summary>
/// ICacheService'in Redis uygulamasi. PDF Sprint 11.
/// </summary>
internal sealed partial class RedisCacheService : ICacheService
{
    /// <summary>
    /// Tum anahtarlarin onune eklenen uygulama oneki.
    /// </summary>
    /// <remarks>
    /// Redis sunucusu baska uygulamalarla PAYLASILABILIR. Onek olmasaydi
    /// baska bir uygulamanin "ref:cities" anahtari bizimkiyle carpisirdi
    /// ve ikisi de yanlis veri okurdu.
    ///
    /// Ayrica "ticketing:*" ile bizim tum anahtarlarimizi tek seferde
    /// gormek/temizlemek mumkun oluyor.
    /// </remarks>
    private const string AppPrefix = "ticketing:";

    /// <summary>
    /// JSON ayarlari BIR KEZ olusturuluyor.
    ///
    /// Her cagrida yeni JsonSerializerOptions uretmek yaygin ve pahali
    /// bir hatadir: .NET her yeni ornek icin serilestirme meta verisini
    /// bastan hesaplar ve onbellege alamaz.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisCacheService> _logger;

    public RedisCacheService(IConnectionMultiplexer redis, ILogger<RedisCacheService> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan expiration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);

        var fullKey = AppPrefix + key;

        // ==============================================================
        // PDF KURALI: "Cache kapali oldugunda sistem calismaya devam
        // edebilmelidir."
        // ==============================================================
        // Bu kural bu dosyanin en onemli tasarim kisitidir ve iki yerde
        // uygulaniyor: OKUMA ve YAZMA.
        //
        // Onbellek bir HIZLANDIRICIDIR, veri kaynagi degil. Redis
        // coktugunde site YAVASLAMALI ama COKMEMELI.
        //
        // Istisnayi yukari biraksaydik, Redis'in bir dakikalik kesintisi
        // TUM SITEYI 500 hatasina bogardi -- oysa veritabani gayet
        // saglikli calisiyor olurdu. Onbellek eklemek, sistemi daha
        // KIRILGAN yapmis olurdu ki bu tam tersi bir sonuc.
        // ==============================================================
        try
        {
            var db = _redis.GetDatabase();
            var cached = await db.StringGetAsync(fullKey).ConfigureAwait(false);

            if (cached.HasValue)
            {
                var value = JsonSerializer.Deserialize<T>(cached!, JsonOptions);

                if (value is not null)
                {
                    LogHit(_logger, key);

                    return value;
                }
            }
        }
#pragma warning disable CA1031 // Genel istisna yakalama
        // CA1031 bilincli olarak susturuldu.
        //
        // Burada beklenen istisnalari saymak mumkun degil:
        // RedisConnectionException, RedisTimeoutException,
        // JsonException, SocketException, ObjectDisposedException...
        // Ve sayamadigimiz bir tanesi, onbellek yuzunden calisan bir
        // sorguyu hataya cevirirdi.
        //
        // Hatayi YUTMUYORUZ, logluyoruz -- ama kullaniciya
        // yansitmiyoruz. Yukaridaki PDF kuralinin geregi budur.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogReadFailed(_logger, key, ex);
        }

        // Onbellekte yok (veya Redis erisilemedi): asil kaynaktan uret.
        var fresh = await factory(cancellationToken).ConfigureAwait(false);

        // null'i onbelleklemiyoruz.
        //
        // Sebep: "bulunamadi" sonucunu saklamak, kayit sonradan
        // olusturulsa bile sure dolana kadar "yok" demeye devam etmek
        // demektir. Ornegin admin bir etkinlik yayinlar ama kullanicilar
        // 5 dakika boyunca 404 gormeye devam eder.
        if (fresh is null)
        {
            return fresh;
        }

        try
        {
            var db = _redis.GetDatabase();
            var json = JsonSerializer.Serialize(fresh, JsonOptions);

            // Fire-and-forget DEGIL, bekliyoruz.
            //
            // Beklemeseydik yazma hatasini hic goremezdik ve onbellek
            // sessizce hic dolmayabilirdi -- sistem calisir, sadece
            // hicbir zaman hizlanmaz.
            await db.StringSetAsync(fullKey, json, expiration).ConfigureAwait(false);

            LogMiss(_logger, key);
        }
#pragma warning disable CA1031 // Bkz. yukaridaki gerekce
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogWriteFailed(_logger, key, ex);
        }

        return fresh;
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            await _redis.GetDatabase().KeyDeleteAsync(AppPrefix + key).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Bkz. GetOrCreateAsync icindeki gerekce
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogRemoveFailed(_logger, key, ex);
        }
    }

    /// <summary>
    /// Onekle eslesen tum anahtarlari siler.
    /// </summary>
    /// <remarks>
    /// ==============================================================
    /// NEDEN KEYS DEGIL SCAN?
    /// ==============================================================
    /// Redis'in KEYS komutu, eslesen anahtarlari bulmak icin TUM
    /// anahtar alanini tek seferde tarar ve bu sirada SUNUCUYU
    /// TAMAMEN BLOKE EDER. Redis tek is parcacikli oldugu icin, o
    /// sirada gelen HER istek bekler.
    ///
    /// Milyonlarca anahtarli bir Redis'te KEYS saniyelerce surebilir --
    /// yani tek bir etkinlik guncellemesi tum siteyi saniyelerce
    /// durdururdu.
    ///
    /// SCAN ise imlecli (cursor) calisir: kucuk parcalar halinde tarar
    /// ve aralarda diger isteklere sira verir. Biraz daha yavas ama
    /// sunucuyu bloke etmiyor.
    ///
    /// StackExchange.Redis'in Keys() metodu, sunucu destekliyorsa
    /// otomatik olarak SCAN kullaniyor (pageSize ile).
    /// ==============================================================
    /// </remarks>
    public async Task RemoveByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var pattern = $"{AppPrefix}{prefix}*";
            var silinen = 0;

            // Coklu sunucu (cluster) durumunda her sunucuyu ayri
            // taramak gerekiyor; tek sunucuda da dogru calisiyor.
            foreach (var endpoint in _redis.GetEndPoints())
            {
                var server = _redis.GetServer(endpoint);

                // Replika sunucularda anahtar silmek hatali olur;
                // yalnizca ana (primary) sunucularda calisiyoruz.
                if (server.IsReplica || !server.IsConnected)
                {
                    continue;
                }

                foreach (var key in server.Keys(db.Database, pattern, pageSize: 250))
                {
                    await db.KeyDeleteAsync(key).ConfigureAwait(false);
                    silinen++;
                }
            }

            if (silinen > 0)
            {
                LogPrefixRemoved(_logger, prefix, silinen);
            }
        }
#pragma warning disable CA1031 // Bkz. GetOrCreateAsync icindeki gerekce
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogRemoveFailed(_logger, prefix, ex);
        }
    }

    // ==================================================================
    // LOGLAMA -- kaynak ureteci ile (CA1848)
    // ==================================================================

    [LoggerMessage(
        EventId = 9301,
        Level = LogLevel.Debug,
        Message = "Onbellek ISABET: {Key}")]
    private static partial void LogHit(ILogger logger, string key);

    [LoggerMessage(
        EventId = 9302,
        Level = LogLevel.Debug,
        Message = "Onbellek ISKA, veri saklandi: {Key}")]
    private static partial void LogMiss(ILogger logger, string key);

    [LoggerMessage(
        EventId = 9303,
        Level = LogLevel.Warning,
        Message = "Onbellek OKUNAMADI: {Key}. Sorgu veritabanindan karsilaniyor.")]
    private static partial void LogReadFailed(ILogger logger, string key, Exception exception);

    [LoggerMessage(
        EventId = 9304,
        Level = LogLevel.Warning,
        Message = "Onbellege YAZILAMADI: {Key}. Sonuc yine de donduruldu.")]
    private static partial void LogWriteFailed(ILogger logger, string key, Exception exception);

    [LoggerMessage(
        EventId = 9305,
        Level = LogLevel.Warning,
        Message = "Onbellek SILINEMEDI: {Key}. Veri sure dolana kadar bayat kalabilir.")]
    private static partial void LogRemoveFailed(ILogger logger, string key, Exception exception);

    [LoggerMessage(
        EventId = 9306,
        Level = LogLevel.Information,
        Message = "Onbellek temizlendi. Onek: {Prefix}, silinen anahtar: {Count}")]
    private static partial void LogPrefixRemoved(ILogger logger, string prefix, int count);
}
