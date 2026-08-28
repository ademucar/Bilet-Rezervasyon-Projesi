using MediatR;
using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Abstractions.Caching;
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
/// Sprint 11'de yapildi: Redis'te 24 saat onbellekleniyor
/// (bkz. docs/01-is-analizi.md soru 12 ve docs/08).
/// </summary>
public sealed record GetCitiesQuery : IRequest<Result<IReadOnlyList<CityDto>>>;

internal sealed class GetCitiesQueryHandler
    : IRequestHandler<GetCitiesQuery, Result<IReadOnlyList<CityDto>>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICacheService _cache;

    public GetCitiesQueryHandler(IApplicationDbContext context, ICacheService cache)
    {
        _context = context;
        _cache = cache;
    }

    public async Task<Result<IReadOnlyList<CityDto>>> Handle(
        GetCitiesQuery request,
        CancellationToken cancellationToken)
    {
        // ==============================================================
        // PDF Sprint 11: "Sehir listesi" cache edilebilir.
        // ==============================================================
        // Onbelleklemek icin en ideal veri: 81 satir, yillardir
        // degismiyor ve neredeyse her sayfada isteniyor (filtre
        // acilir listesi).
        //
        // Sorgu tamamen ANONIM -- kullaniciya gore degismiyor. Bu
        // yuzden ortak onbellekte tutulmasi guvenli.
        // PDF kurali: "Kullaniciya ozel hassas veriler ortak cache
        // icinde tutulmamalidir." Burada kullaniciya ozel hicbir sey yok.
        // ==============================================================
        var cities = await _cache.GetOrCreateAsync(
            CacheKeys.Cities,
            LoadAsync,
            CacheDurations.ReferenceData,
            cancellationToken).ConfigureAwait(false);

        return Result.Success(cities);
    }

    private async Task<IReadOnlyList<CityDto>> LoadAsync(CancellationToken cancellationToken)
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

        return cities;
    }
}
