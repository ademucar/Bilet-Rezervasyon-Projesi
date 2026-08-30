namespace Ticketing.Application.Abstractions.Caching;

/// <summary>
/// Dagitik önbellek. PDF Sprint 11.
///
/// Application katmanina dogrudan StackExchange.Redis enjekte
/// edebilirdim, ilk aklima gelen oydu. Uc sey engelledi: is mantigi
/// belirli bir onbellek urunune baglanirdi, mimari testim kirmizi
/// yanardi (hakli olarak) ve her birim testi icin Redis ayaga
/// kaldirmam gerekirdi.
///
/// Bu arayuzle handler'lar yalnizca "bu veriyi onbellekten ver,
/// yoksa uret ve sakla" diyor.
/// </summary>
public interface ICacheService
{
    /// <summary>
    /// Onbellekte varsa döndürür; yoksa <paramref name="factory"/> ile
    /// uretip saklar.
    /// </summary>
    /// <remarks>
    /// Get ve Set'i ayri iki metot yapmadim. Ayri olsalardi her
    /// cagiran su kaliba mecbur kalirdi:
    ///
    ///     var cached = await cache.GetAsync&lt;T&gt;(key);
    ///     if (cached is not null) return cached;
    ///     var data = await SorguyuCalistir();
    ///     await cache.SetAsync(key, data, süre);
    ///     return data;
    ///
    /// Bes satir, her cagirim yerinde tekrar. Birinde SetAsync
    /// unutulursa o sorgu hic onbelleklenmez ve kimse fark etmez;
    /// sistem calisir, sadece yavas. Tek metotta bu hatayi yapmak
    /// mumkun degil.
    ///
    /// PDF kuralı: "Cache kapalı olduğunda sistem calismaya devam
    /// edebilmelidir." Uygulamalar bu sozu tutmak zorunda: Redis'e
    /// ulasilamazsa istisna FIRLATMAZ, doğrudan factory'yi calistirir.
    /// </remarks>
    /// <param name="key">CacheKeys sinifindan üretilmiş anahtar.</param>
    /// <param name="factory">Onbellekte yoksa veriyi ureten fonksiyon.</param>
    /// <param name="expiration">Yasam süresi. Bkz. CacheDurations.</param>
    Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan expiration,
        CancellationToken cancellationToken = default);

    /// <summary>Tek bir anahtari siler.</summary>
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Belirli bir onekle baslayan TÜM anahtarlari siler.
    /// </summary>
    /// <remarks>
    /// PDF kuralı: "Veri guncellendiginde ilgili cache temizlenmelidir."
    ///
    /// Neden onek gerekli? Çünkü bir etkinlik guncellendiginde yalnızca
    /// tek bir anahtar bayatlamiyor:
    ///
    ///     event:detail:{id}
    ///     event:popular:10
    ///     event:popular:20
    ///
    /// Hepsini tek tek bilmek ve silmek mumkun değil -- ozellikle
    /// "popular:{n}" gibi degiskenli anahtarlarda. Onek ile silmek
    /// tek doğru yol.
    /// </remarks>
    Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default);
}
