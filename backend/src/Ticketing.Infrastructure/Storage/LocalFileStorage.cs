using Microsoft.Extensions.Configuration;
using Ticketing.Application.Abstractions.Storage;

namespace Ticketing.Infrastructure.Storage;

/// <summary>
/// Yuklenen dosyalari yerel diske yazar. PDF Sprint 15.
/// </summary>
/// <remarks>
/// ==================================================================
/// URETIMDE BU SINIF YETMEZ -- BILINCLI BIR SINIRLAMA
/// ==================================================================
/// Birden fazla sunucuya olceklenince disk PAYLASILMAZ: kullanici
/// afisi sunucu-1 e yukler, sunucu-2 den istendiginde bulunamaz.
///
/// O gun IFileStorage in S3/Azure Blob uygulamasi yazilacak ve
/// Application katmaninda TEK SATIR degismeyecek. Arayuzun varlik
/// sebebi tam olarak bu.
///
/// Simdilik yerel disk yeterli cunku tek sunucuda calisiyoruz --
/// ihtiyac duymadigimiz altyapiyi simdiden kurmuyorum.
/// ==================================================================
/// </remarks>
internal sealed class LocalFileStorage : IFileStorage
{
    private readonly string _root;

    public LocalFileStorage(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        _root = configuration["FileStorage:Path"]
            ?? Path.Combine(AppContext.BaseDirectory, "uploads");

        Directory.CreateDirectory(_root);
    }

    public async Task<string> SaveAsync(
        string storedFileName,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storedFileName);
        ArgumentNullException.ThrowIfNull(content);

        var yol = TamYol(storedFileName);

        // ==============================================================
        // FileMode.CreateNew -- Create DEGIL
        // ==============================================================
        // Create, var olan dosyanin USTUNE yazar. CreateNew ise dosya
        // varsa HATA firlatir.
        //
        // Ad zaten Guid oldugu icin cakisma pratikte imkansiz. Ama
        // "imkansiz" varsayimiyla ustune yazmak yerine PATLAMASINI
        // tercih ediyorum: bir gun ad uretimi bozulursa (ornegin biri
        // Guid yerine kullanici adini koyarsa) bu satir sessiz veri
        // kaybi yerine gurultulu bir hata verir.
        // ==============================================================
        await using var dosya = new FileStream(
            yol,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);

        // CopyToAsync akisi PARCA PARCA kopyaliyor; dosyanin tamami
        // hicbir zaman bellege alinmiyor. 5 MB tek basina sorun degil
        // ama es zamanli yuzlerce yukleme olsaydi olurdu.
        await content.CopyToAsync(dosya, cancellationToken).ConfigureAwait(false);

        return yol;
    }

    public Task DeleteAsync(string storedFileName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storedFileName);

        var yol = TamYol(storedFileName);

        // File.Delete dosya yoksa zaten hata vermiyor.
        // Silme islemi IDEMPOTENT olmali: temizlik job u ayni dosyayi
        // iki kez silmeye calisirsa patlamamali.
        File.Delete(yol);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Dosya adini kok klasorle birlestirir ve sonucun GERCEKTEN kok
    /// klasorun altinda kaldigini dogrular.
    /// </summary>
    /// <remarks>
    /// ==============================================================
    /// UCUNCU SIPER -- DERINLEMESINE SAVUNMA
    /// ==============================================================
    /// Bu noktaya gelen ad zaten Guid: FileUploadValidator uretti ve
    /// kullanici girdisi icermiyor. Yani bu kontrol BUGUN gereksiz.
    ///
    /// Yine de koyuyorum cunku bu sinif, cagiranin ne gonderdigini
    /// bilmiyor. Ilerde biri IFileStorage i baska bir yerden, elle
    /// olusturulmus bir adla cagirirsa tek koruma bu satir olacak.
    ///
    /// Guvenlik kontrolu, ona GUVENEN katmanda degil, ihlal
    /// EDILEBILECEK katmanda durmali.
    /// ==============================================================
    /// </remarks>
    private string TamYol(string storedFileName)
    {
        var ad = Path.GetFileName(storedFileName);
        var yol = Path.GetFullPath(Path.Combine(_root, ad));

        if (!yol.StartsWith(Path.GetFullPath(_root), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Gecersiz dosya yolu.");
        }

        return yol;
    }
}
