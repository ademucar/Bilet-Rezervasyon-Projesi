using MediatR;
using Ticketing.Application.Abstractions.Persistence;
using Ticketing.Application.Abstractions.Storage;
using Ticketing.Application.Common.Results;
using Ticketing.Application.Common.Security;
using Ticketing.Domain.Entities;

namespace Ticketing.Application.Features.Files;

/// <summary>Yuklenen dosyanin istemciye donen bilgisi.</summary>
/// <remarks>
/// StoragePath BILINCLI OLARAK YOK. Sunucudaki gercek dosya yolunu
/// istemciye vermek, saldirgana dizin yapisini acik eder. Istemcinin
/// ihtiyaci olan tek sey Id ve indirme adresi.
/// </remarks>
public sealed record UploadedFileDto(
    Guid Id,
    string FileName,
    string ContentType,
    long SizeInBytes,
    string DownloadUrl);

/// <summary>
/// Dosya yukleme. PDF Sprint 15: file type / MIME type / guvenli dosya adi.
/// </summary>
/// <remarks>
/// ==================================================================
/// NEDEN IFormFile DEGIL, STREAM?
/// ==================================================================
/// IFormFile, Microsoft.AspNetCore.Http icinde tanimli. Application
/// katmanina almak, is mantigini WEB e bagimli yapardi -- mimari
/// testimiz bunu zaten reddediyor.
///
/// Stream ise System.IO icinde. Ayni komut yarin bir arka plan
/// isinden veya konsol aracindan da cagrilabilir.
/// ==================================================================
/// </remarks>
public sealed record UploadFileCommand(
    string FileName,
    string ContentType,
    long SizeInBytes,
    Stream Content) : IRequest<Result<UploadedFileDto>>;

internal sealed class UploadFileCommandHandler
    : IRequestHandler<UploadFileCommand, Result<UploadedFileDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IFileStorage _storage;

    public UploadFileCommandHandler(IApplicationDbContext context, IFileStorage storage)
    {
        _context = context;
        _storage = storage;
    }

    public async Task<Result<UploadedFileDto>> Handle(
        UploadFileCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // ==============================================================
        // 1) IMZA ICIN BASTAN BIRKAC BAYT OKU
        // ==============================================================
        // Tum dosyayi belege almiyorum. 5 MB tek basina sorun degil ama
        // es zamanli 100 yukleme 500 MB eder ve sunucuyu dusurur.
        //
        // Yalnizca imza icin gereken kadar okuyup akisi BASA SARIYORUM;
        // sonra ayni akis dogrudan diske kopyalaniyor.
        // ==============================================================
        var basBaytlari = new byte[FileUploadValidator.ImzaIcinGerekenBayt];
        var okunan = await request.Content
            .ReadAtLeastAsync(basBaytlari, basBaytlari.Length, throwOnEndOfStream: false, cancellationToken)
            .ConfigureAwait(false);

        // ==============================================================
        // 2) DOGRULA -- diske YAZMADAN ONCE
        // ==============================================================
        // Sira onemli: once yazip sonra dogrulasaydik, zararli dosya
        // gecersiz bulunana kadar diskte durmus olurdu. Kisa bir an
        // gibi gorunuyor ama bu sure icinde baska bir istek o dosyayi
        // isteyebilir.
        //
        // Hicbir zaman diske dusmemesi, sonra silmekten guvenlidir.
        // ==============================================================
        var dogrulama = FileUploadValidator.Dogrula(
            request.FileName,
            request.ContentType,
            request.SizeInBytes,
            basBaytlari.AsSpan(0, okunan));

        if (dogrulama.IsFailure)
        {
            return Result.Failure<UploadedFileDto>(dogrulama.Error);
        }

        var guvenliAd = dogrulama.Value;

        // Akisi basa sar: imza icin okudugumuz baytlar da diske yazilmali.
        if (request.Content.CanSeek)
        {
            request.Content.Position = 0;
        }

        var yol = await _storage
            .SaveAsync(guvenliAd, request.Content, cancellationToken)
            .ConfigureAwait(false);

        // ==============================================================
        // 3) VERITABANI KAYDI
        // ==============================================================
        // Dosya diskte, kayit veritabaninda -- iki ayri sistem. Kayit
        // basarisiz olursa dosya SAHIPSIZ kalir.
        //
        // Bunu dagitik islem (2PC) ile cozmuyorum: karmasik ve pahali.
        // Bunun yerine sahipsiz dosyalar KABUL EDILEBILIR sayiliyor ve
        // UploadedFile.IsOrphan() ile bulunup temizlenebiliyor.
        //
        // Ters yon (kayit var, dosya yok) COK daha kotu olurdu: kullanici
        // kirik bir baglanti gorurdu. Bu yuzden once dosya, sonra kayit.
        // ==============================================================
        var kayit = UploadedFile.Create(
            // Orijinal ad SAKLANIYOR ama diske yazilmiyor -- yalnizca
            // kullaniciya gosterim icin. Path.GetFileName ile dizin
            // kismi atiliyor ki veritabaninda da yol parcasi durmasin.
            fileName: Path.GetFileName(request.FileName),
            storedFileName: guvenliAd,
            contentType: request.ContentType,
            sizeInBytes: request.SizeInBytes,
            storagePath: yol);

        _context.UploadedFiles.Add(kayit);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(new UploadedFileDto(
            kayit.Id,
            kayit.FileName,
            kayit.ContentType,
            kayit.SizeInBytes,
            $"/api/v1/files/{kayit.Id}"));
    }
}
