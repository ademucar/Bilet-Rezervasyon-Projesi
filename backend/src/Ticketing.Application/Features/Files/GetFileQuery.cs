using MediatR;
using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Abstractions.Persistence;
using Ticketing.Application.Common.Results;

namespace Ticketing.Application.Features.Files;

/// <summary>Indirilecek dosyanin içeriği ve meta bilgisi.</summary>
public sealed record FileContentDto(
    Stream Content,
    string ContentType,
    string FileName);

/// <summary>
/// Yuklenmis bir dosyayı indirir. PDF Sprint 15.
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

        // Dosya veritabanindan bulunuyor, yoldan değil
        //
        // Istemci bana bir Guid veriyor; biz o Guid ile veritabanina
        // bakip GERCEK yolu oradan okuyorum.
        //
        // Alternatif -- istemcinin gonderdigi dosya adiyla doğrudan
        // diske bakmak -- klasik bir dizin gecisi acigidir:
        //     GET /api/v1/files/../../appsettings.json
        //
        // Veritabani araya girince bu saldiri sinifi tamamen ortadan
        // kalkiyor: kullanicidan gelen deger bir YOL değil, bir
        // ANAHTAR. Yol benim kendi kaydimizdan geliyor.
        var kayit = await _context.UploadedFiles
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == request.Id, cancellationToken)
            .ConfigureAwait(false);

        if (kayit is null)
        {
            return Result.Failure<FileContentDto>(
                Error.NotFound("file.not_found", "Dosya bulunamadı."));
        }

        // Veritabaninda kayıt var ama diskte dosya yok.
        //
        // Bu, yukleme sırasında kayıt başarılı olup dosya yazmanin
        // başarısız olduğu (veya dosyanin elle silindigi) durum.
        // Kullanıcıya 404 donuyorum: onun acisindan dosya YOK.
        // Ic tutarsizligi kullanıcıya anlatmanin bir faydasi olmaz.
        if (!File.Exists(kayit.StoragePath))
        {
            return Result.Failure<FileContentDto>(
                Error.NotFound("file.not_found", "Dosya bulunamadı."));
        }

        // Akis olarak donuyorum, byte[] olarak değil: dosyanin tamami
        // bellege alinmadan doğrudan yanit govdesine akiyor.
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
