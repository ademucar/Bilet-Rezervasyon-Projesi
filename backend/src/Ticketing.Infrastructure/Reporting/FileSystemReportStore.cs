using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Ticketing.Application.Abstractions.Reporting;
using Ticketing.Application.Features.Reports;

namespace Ticketing.Infrastructure.Reporting;

/// <summary>
/// Uretilen rapor dosyalarini diskte saklar. PDF Sprint 13.
/// </summary>
/// <remarks>
/// Neden veritabani değil, disk?
///
/// Dosyalari veritabaninda bytea olarak da tutabilirdim. Tutmadim:
///
///   - Rapor dosyalari megabaytlarca olabilir. Veritabaninin her
///     yedegi bu dosyalari da tasir ve yedek boyutu hizla buyur.
///   - PostgreSQL büyük ikili veriyi TOAST tablolarina tasiyor;
///     sorgular yavaslar.
///   - Rapor dosyasi GECICI bir cikti. Kaybolsa yeniden uretilebilir.
///     Veritabani ise dogruluk kaynagim; oraya geçici veri koymak
///     iki farklı sorumlulugu karistirmak olurdu.
///
/// Uretimde ne degisir?
///
/// Birden fazla sunucuya olceklenirse disk PAYLASILMAZ: rapor
/// sunucu-1'de üretilir, kullanıcı sunucu-2'ye baglanir ve dosyayı
/// bulamaz.
///
/// O zaman bu sinifin yerine bir S3/Azure Blob uygulamasi gelir --
/// arayüz (IReportFileStore) aynı kaldigi için Application katmaninda
/// TEK SATIR degismez. Zaten arayuzun varlik sebebi bu.
/// </remarks>
internal sealed class FileSystemReportStore : IReportFileStore
{
    private readonly string _root;

    public FileSystemReportStore(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        // Yapilandirilabilir; verilmezse uygulama klasörü altinda.
        _root = configuration["Reports:StoragePath"]
            ?? Path.Combine(AppContext.BaseDirectory, "report-exports");

        Directory.CreateDirectory(_root);
    }

    /// <summary>
    /// Dosya yolunu üretir.
    /// </summary>
    /// <remarks>
    /// Dosya adi olarak GUID -- kullanici girdisi değil
    ///
    /// Rapor basligini dosya adı yapsaydim, ilerde ozellestirilebilir
    /// bir başlık "../../appsettings.json" olabilirdi ve dizin gecisi
    /// (path traversal) acigi olusurdu.
    ///
    /// Guid.ToString("N") yalnızca 32 onaltilik karakter uretiyor --
    /// tanim geregi güvenli. Gerçek dosya adı ve içerik türü ayrı bir
    /// meta dosyasinda duruyor.
    /// </remarks>
    private string DosyaYolu(Guid exportId, string uzanti)
        => Path.Combine(_root, $"{exportId:N}.{uzanti}");

    public async Task SaveAsync(
        Guid exportId,
        ExportedReport report,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);

        await File.WriteAllBytesAsync(DosyaYolu(exportId, "bin"), report.Content, cancellationToken)
            .ConfigureAwait(false);

        // Dosya adı ve içerik türü ayrı bir meta dosyasinda.
        //
        // Indirme sırasında tarayiciya doğru Content-Type ve dosya adı
        // vermek için gerekli. Bunlari dosya adina gomseydik
        // ayristirmak gerekirdi ve yukaridaki güvenlik faydasi
        // kaybolurdu.
        var meta = JsonSerializer.Serialize(new ReportFileMeta(
            report.FileName, report.ContentType, report.Content.Length));

        await File.WriteAllTextAsync(DosyaYolu(exportId, "json"), meta, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ExportedReport?> GetAsync(Guid exportId, CancellationToken cancellationToken)
    {
        var binPath = DosyaYolu(exportId, "bin");
        var metaPath = DosyaYolu(exportId, "json");

        if (!File.Exists(binPath) || !File.Exists(metaPath))
        {
            return null;
        }

        var metaJson = await File.ReadAllTextAsync(metaPath, cancellationToken)
            .ConfigureAwait(false);

        var meta = JsonSerializer.Deserialize<ReportFileMeta>(metaJson);

        if (meta is null)
        {
            return null;
        }

        var content = await File.ReadAllBytesAsync(binPath, cancellationToken)
            .ConfigureAwait(false);

        return new ExportedReport(meta.FileName, meta.ContentType, content);
    }

    public Task<bool> ExistsAsync(Guid exportId, CancellationToken cancellationToken)
        => Task.FromResult(File.Exists(DosyaYolu(exportId, "bin")));

    private sealed record ReportFileMeta(string FileName, string ContentType, int Size);
}
