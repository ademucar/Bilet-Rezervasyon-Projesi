namespace Ticketing.Application.Abstractions.Caching;

/// <summary>
/// Dagitik onbellek. PDF Sprint 11.
///
/// ==================================================================
/// NEDEN ARAYUZ? Neden dogrudan Redis?
/// ==================================================================
/// Application katmanina StackExchange.Redis enjekte etseydik:
///
///   - Is mantigi bir ONBELLEK URUNUNE baglanirdi
///   - Mimari testimiz kirmizi yanardi (ve hakli olarak)
///   - Birim testlerinde Redis sunucusu ayaga kaldirmak gerekirdi
///
/// Bu arayuz sayesinde handler'lar yalnizca "bu veriyi onbellekten
/// ver, yoksa uret ve sakla" diyor.
/// ==================================================================
/// </summary>
public interface ICacheService
{
    /// <summary>
    /// Onbellekte varsa dondurur; yoksa <paramref name="factory"/> ile
    /// uretip saklar.
    /// </summary>
    /// <remarks>
    /// ==============================================================
    /// NEDEN "GET" VE "SET" AYRI DEGIL DE TEK METOT?
    /// ==============================================================
    /// Ayri olsaydi her cagiran su kaliba mecbur kalirdi:
    ///
    ///     var cached = await cache.GetAsync&lt;T&gt;(key);
    ///     if (cached is not null) return cached;
    ///     var data = await SorguyuCalistir();
    ///     await cache.SetAsync(key, data, sure);
    ///     return data;
    ///
    /// Bes satir, ve her cagirim yerinde TEKRAR. Birinde `SetAsync`
    /// unutulursa o sorgu hicbir zaman onbelleklenmez ve kimse fark
    /// etmez -- sistem calisir, sadece yavastir.
    ///
    /// Tek metot bu hatayi imkansiz kiliyor.
    /// ==============================================================
    ///
    /// PDF kurali: "Cache kapali oldugunda sistem calismaya devam
    /// edebilmelidir." Uygulamalar bu sozu tutmak zorunda: Redis'e
    /// ulasilamazsa istisna FIRLATMAZ, dogrudan factory'yi calistirir.
    /// </remarks>
    /// <param name="key">CacheKeys sinifindan uretilmis anahtar.</param>
    /// <param name="factory">Onbellekte yoksa veriyi ureten fonksiyon.</param>
    /// <param name="expiration">Yasam suresi. Bkz. CacheDurations.</param>
    Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan expiration,
        CancellationToken cancellationToken = default);

    /// <summary>Tek bir anahtari siler.</summary>
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Belirli bir onekle baslayan TUM anahtarlari siler.
    /// </summary>
    /// <remarks>
    /// PDF kurali: "Veri guncellendiginde ilgili cache temizlenmelidir."
    ///
    /// Neden onek gerekli? Cunku bir etkinlik guncellendiginde yalnizca
    /// tek bir anahtar bayatlamiyor:
    ///
    ///     event:detail:{id}
    ///     event:popular:10
    ///     event:popular:20
    ///
    /// Hepsini tek tek bilmek ve silmek mumkun degil -- ozellikle
    /// "popular:{n}" gibi degiskenli anahtarlarda. Onek ile silmek
    /// tek dogru yol.
    /// </remarks>
    Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default);
}
