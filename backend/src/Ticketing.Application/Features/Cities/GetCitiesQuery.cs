using MediatR;
using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Abstractions.Caching;
using Ticketing.Application.Abstractions.Persistence;
using Ticketing.Application.Common.Results;

namespace Ticketing.Application.Features.Cities;

public sealed record CityDto(Guid Id, string Name, int PlateCode);

/// <summary>
/// Şehir listesi. Etkinlik filtrelemede ve mekan olustururken kullanilir.
///
/// Sayfalama YOK -- bilerek. Turkiye'de 81 şehir var ve bu sayi
/// degismiyor. Sayfalama eklemek, frontend'i gereksiz yere "sonraki
/// sayfa" mantığı yazmaya zorlardi.
///
/// Sprint 11'de yapıldı: Redis'te 24 saat onbellekleniyor
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
        // PDF Sprint 11: "Şehir listesi" cache edilebilir.
        //
        // Onbelleklemek için en ideal veri: 81 satır, yillardir
        // degismiyor ve neredeyse her sayfada isteniyor (filtre
        // açılır listesi).
        //
        // Sorgu tamamen ANONIM -- kullanıcıya göre degismiyor. Bu
        // yüzden ortak onbellekte tutulmasi güvenli.
        // PDF kuralı: "Kullanıcıya ozel hassas veriler ortak cache
        // içinde tutulmamalidir." Burada kullanıcıya ozel hiçbir sey yok.
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
            // Projeksiyon: yalnızca 3 sutun çekiliyor.
            // Entity'nin tamamini yukleyip donusturseydik CreatedAt,
            // UpdatedBy, IsDeleted gibi alanlari da boşuna tasirdim.
            .Select(c => new CityDto(c.Id, c.Name, c.PlateCode))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return cities;
    }
}
