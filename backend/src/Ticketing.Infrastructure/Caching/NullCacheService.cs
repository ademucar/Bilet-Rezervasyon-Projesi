using Ticketing.Application.Abstractions.Caching;

namespace Ticketing.Infrastructure.Caching;

/// <summary>
/// ==================================================================
/// ONBELLEKSIZ UYGULAMA -- PDF Sprint 11
/// ==================================================================
/// PDF kurali: "Cache kapali oldugunda sistem calismaya devam
/// edebilmelidir."
///
/// Bu sinif o kurali EN NET sekilde karsiliyor: yapilandirmada
/// Redis kapaliysa (veya hic adres verilmemisse) DI konteynerine
/// bu kaydediliyor ve her sorgu dogrudan veritabanina gidiyor.
///
/// ------------------------------------------------------------------
/// NEDEN "if (cache != null)" KONTROLU YERINE BOS BIR SINIF?
/// ------------------------------------------------------------------
/// Alternatif su olurdu: ICacheService'i nullable yapip her cagirim
/// yerinde kontrol etmek.
///
///     if (_cache is not null)
///         return await _cache.GetOrCreateAsync(...);
///
///     return await SorguyuCalistir();
///
/// Bu yaklasim her handler'da IKI KOD YOLU olusturur. Ve o iki yoldan
/// yalnizca biri test edilir -- digeri uretimde ilk kez calisir.
///
/// Bu desene "Null Object Pattern" deniyor. Cagiran taraf onbellegin
/// acik mi kapali mi oldugunu HIC BILMIYOR; kodu tek bir yol.
///
/// Yan fayda: testlerde de bunu kullanabiliyoruz -- Redis kurmadan
/// handler'lari calistirmak mumkun.
/// ==================================================================
/// </summary>
internal sealed class NullCacheService : ICacheService
{
    public Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan expiration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);

        // Onbellek yok: dogrudan asil kaynaga git.
        return factory(cancellationToken);
    }

    // Saklanmadi, silinecek bir sey de yok.
    //
    // Burada istisna FIRLATMAK bir secenek degil: cagiran taraf
    // temizleme cagrisini kosula baglamak zorunda kalirdi ve
    // yukaridaki "tek kod yolu" faydasi kaybolurdu.
    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
