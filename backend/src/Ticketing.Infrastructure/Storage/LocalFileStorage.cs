using Microsoft.Extensions.Configuration;
using Ticketing.Application.Abstractions.Storage;

namespace Ticketing.Infrastructure.Storage;

/// <summary>
/// Yuklenen dosyalari yerel diske yazar. PDF Sprint 15.
/// </summary>
/// <remarks>
/// URETIMDE BU SINIF YETMEZ -- BILINCLI BIR SINIRLAMA
///
/// Birden fazla sunucuya olceklenince disk PAYLASILMAZ: kullanıcı
/// afisi sunucu-1 e yukler, sunucu-2 den istendiginde bulunamaz.
///
/// O gün IFileStorage in S3/Azure Blob uygulamasi yazilacak ve
/// Application katmaninda TEK SATIR degismeyecek. Arayuzun varlik
/// sebebi tam olarak bu.
///
/// Şimdilik yerel disk yeterli çünkü tek sunucuda calisiyorum --
/// ihtiyac duymadigimiz altyapiyi simdiden kurmuyorum.
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

        // FileMode.CreateNew -- Create DEĞİL
        //
        // Create, var olan dosyanin USTUNE yazar. CreateNew ise dosya
        // varsa HATA firlatir.
        //
        // Ad zaten Guid olduğu için çakışma pratikte imkansiz. Ama
        // "imkansiz" varsayimiyla ustune yazmak yerine PATLAMASINI
        // tercih ediyorum: bir gün ad üretimi bozulursa (örneğin biri
        // Guid yerine kullanıcı adını koyarsa) bu satır sessiz veri
        // kaybi yerine gurultulu bir hata verir.
        await using var dosya = new FileStream(
            yol,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);

        // CopyToAsync akışı PARCA PARCA kopyaliyor; dosyanin tamami
        // hiçbir zaman bellege alinmiyor. 5 MB tek başına sorun değil
        // ama es zamanlı yuzlerce yukleme olsaydı olurdu.
        await content.CopyToAsync(dosya, cancellationToken).ConfigureAwait(false);

        return yol;
    }

    public Task DeleteAsync(string storedFileName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storedFileName);

        var yol = TamYol(storedFileName);

        // File.Delete dosya yoksa zaten hata vermiyor.
        // Silme islemi IDEMPOTENT olmalı: temizlik job u aynı dosyayı
        // iki kez silmeye calisirsa patlamamali.
        File.Delete(yol);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Dosya adını kok klasorle birlestirir ve sonucun GERCEKTEN kok
    /// klasorun altinda kaldigini dogrular.
    /// </summary>
    /// <remarks>
    /// UCUNCU SIPER -- DERINLEMESINE SAVUNMA
    ///
    /// Bu noktaya gelen ad zaten Guid: FileUploadValidator uretti ve
    /// kullanıcı girdisi icermiyor. Yani bu kontrol BUGUN gereksiz.
    ///
    /// Yine de koyuyorum çünkü bu sinif, cagiranin ne gonderdigini
    /// bilmiyor. Ilerde biri IFileStorage i başka bir yerden, elle
    /// olusturulmus bir adla cagirirsa tek koruma bu satır olacak.
    ///
    /// Güvenlik kontrolü, ona GUVENEN katmanda değil, ihlal
    /// EDILEBILECEK katmanda durmali.
    /// </remarks>
    private string TamYol(string storedFileName)
    {
        var ad = Path.GetFileName(storedFileName);
        var yol = Path.GetFullPath(Path.Combine(_root, ad));

        if (!yol.StartsWith(Path.GetFullPath(_root), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Geçersiz dosya yolu.");
        }

        return yol;
    }
}
