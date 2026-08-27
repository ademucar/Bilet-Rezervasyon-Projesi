using MediatR;
using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Abstractions.Persistence;
using Ticketing.Application.Common.Results;

namespace Ticketing.Application.Features.Cities;

public sealed record CityDto(Guid Id, string Name, int PlateCode);

/// <summary>
/// Sehir listesi. Etkinlik filtrelemede ve mekan olustururken kullanilir.
///
/// Sayfalama YOK -- bilerek. Turkiye'de 81 sehir var ve bu sayi
/// degismiyor. Sayfalama eklemek, frontend'i gereksiz yere "sonraki
/// sayfa" mantigi yazmaya zorlardi.
///
/// Sprint 11'de bu sorgu Redis'te 24 saat cache'lenecek
/// (bkz. docs/01-is-analizi.md soru 12).
/// </summary>
public sealed record GetCitiesQuery : IRequest<Result<IReadOnlyList<CityDto>>>;

internal sealed class GetCitiesQueryHandler
    : IRequestHandler<GetCitiesQuery, Result<IReadOnlyList<CityDto>>>
{
    private readonly IApplicationDbContext _context;

    public GetCitiesQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<Result<IReadOnlyList<CityDto>>> Handle(
        GetCitiesQuery request,
        CancellationToken cancellationToken)
    {
        var cities = await _context.Cities
            // AsNoTracking: salt okuma sorgusu. EF'in degisiklik takibi
            // yapmasina gerek yok; hem bellek hem CPU tasarrufu.
            .AsNoTracking()
            .OrderBy(c => c.Name)
            // Projeksiyon: yalnizca 3 sutun cekiliyor.
            // Entity'nin tamamini yukleyip donusturseydik CreatedAt,
            // UpdatedBy, IsDeleted gibi alanlari da bosuna tasirdik.
            .Select(c => new CityDto(c.Id, c.Name, c.PlateCode))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Result.Success<IReadOnlyList<CityDto>>(cities);
    }
}
