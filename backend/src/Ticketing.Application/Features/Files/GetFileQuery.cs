using MediatR;
using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Abstractions.Persistence;
using Ticketing.Application.Common.Results;

namespace Ticketing.Application.Features.Files;

/// <summary>Indirilecek dosyanin icerigi ve meta bilgisi.</summary>
public sealed record FileContentDto(
    Stream Content,
    string ContentType,
    string FileName);

/// <summary>
/// Yuklenmis bir dosyayi indirir. PDF Sprint 15.
/// </summary>
public sealed record GetFileQuery(Guid Id) : IRequest<Result<FileContentDto>>;

internal sealed class GetFileQueryHandler
    : IRequestHandler<GetFileQuery, Result<FileContentDto>>
{
    private readonly IApplicationDbContext _context;

    public GetFileQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<Result<FileContentDto>> Handle(
        GetFileQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // ==============================================================
        // DOSYA VERITABANINDAN BULUNUYOR, YOLDAN DEGIL
        // ==============================================================
        // Istemci bize bir Guid veriyor; biz o Guid ile veritabanina
        // bakip GERCEK yolu oradan okuyoruz.
        //
        // Alternatif -- istemcinin gonderdigi dosya adiyla dogrudan
        // diske bakmak -- klasik bir dizin gecisi acigidir:
        //     GET /api/v1/files/../../appsettings.json
        //
        // Veritabani araya girince bu saldiri sinifi tamamen ortadan
        // kalkiyor: kullanicidan gelen deger bir YOL degil, bir
        // ANAHTAR. Yol bizim kendi kaydimizdan geliyor.
        // ==============================================================
        var kayit = await _context.UploadedFiles
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == request.Id, cancellationToken)
            .ConfigureAwait(false);

        if (kayit is null)
        {
            return Result.Failure<FileContentDto>(
                Error.NotFound("file.not_found", "Dosya bulunamadi."));
        }

        // Veritabaninda kayit var ama diskte dosya yok.
        //
        // Bu, yukleme sirasinda kayit basarili olup dosya yazmanin
        // basarisiz oldugu (veya dosyanin elle silindigi) durum.
        // Kullaniciya 404 donuyorum: onun acisindan dosya YOK.
        // Ic tutarsizligi kullaniciya anlatmanin bir faydasi olmaz.
        if (!File.Exists(kayit.StoragePath))
        {
            return Result.Failure<FileContentDto>(
                Error.NotFound("file.not_found", "Dosya bulunamadi."));
        }

        // Akis olarak donuyorum, byte[] olarak degil: dosyanin tamami
        // bellege alinmadan dogrudan yanit govdesine akiyor.
        var akis = new FileStream(
            kayit.StoragePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true);

        return Result.Success(new FileContentDto(
            akis,
            kayit.ContentType,
            kayit.FileName));
    }
}
