using System.Diagnostics.CodeAnalysis;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Abstractions.Persistence;
using Ticketing.Application.Common.Pagination;
using Ticketing.Application.Common.Results;

namespace Ticketing.Application.Features.Venues;

// ===================================================================
// LISTELEME
// ===================================================================

/// <summary>
/// PDF Sprint 4: GET /api/v1/venues
///
/// PaginationRequest'ten turuyor: PageNumber, PageSize ve ust sinir
/// kontrolü bedava geliyor.
/// </summary>
public sealed record GetVenuesQuery : PaginationRequest, IRequest<Result<PagedResult<VenueListItem>>>
{
    /// <summary>İsme göre arama. null ise filtre uygulanmaz.</summary>
    public string? Search { get; init; }

    /// <summary>Sehre göre filtre.</summary>
    public Guid? CityId { get; init; }
}

internal sealed class GetVenuesQueryHandler
    : IRequestHandler<GetVenuesQuery, Result<PagedResult<VenueListItem>>>
{
    private readonly IApplicationDbContext _context;

    public GetVenuesQueryHandler(IApplicationDbContext context) => _context = context;

    [SuppressMessage(
        "Globalization",
        "CA1304:Specify CultureInfo",
        Justification =
            "Bu ToLower() cagrisi bir IFADE AGACI (expression tree) içinde ve " +
            ".NET'te HİÇ CALISMIYOR. EF Core önü SQL'deki LOWER() fonksiyonuna " +
            "ceviriyor; buyuk/kucuk harf donusumunu veritabani yapiyor. " +
            "Dolayisiyla .NET kultur ayarinin sonuca hiçbir etkisi yok. " +
            "Ayrıca ToLowerInvariant() burada KULLANILAMAZ -- EF Core önü " +
            "SQL'e ceviremez ve calisma zamaninda 'could not be translated' " +
            "hatasi verir.")]
    [SuppressMessage(
        "Globalization",
        "CA1311:Specify a culture or use an invariant version",
        Justification = "Bkz. CA1304 açıklaması: ifade ağacı, SQL'e çevriliyor.")]
    public async Task<Result<PagedResult<VenueListItem>>> Handle(
        GetVenuesQuery request,
        CancellationToken cancellationToken)
    {
        var query = _context.Venues.AsNoTracking();

        // ==============================================================
        // FILTRELERI KOSULLU EKLIYORUM
        // ==============================================================
        // IQueryable tembeldir (lazy): aşağıdaki Where cagrilarinin
        // hicbiri veritabanina gitmez. Yalnızca SQL agacini insa eder.
        // Sorgu, ToListAsync cagrildiginda TEK SEFERDE çalışır.
        //
        // Bu yüzden filtreleri if bloklariyla eklemek maliyetsiz.
        // "her ihtimale karsi hepsini ekleyip null kontrolü yapayim"
        // deseydik, SQL'e gereksiz "WHERE (@p IS NULL OR ...)" kosullari
        // girer ve PostgreSQL index kullanamaz hale gelirdi.
        // ==============================================================
        if (request.CityId.HasValue)
        {
            query = query.Where(v => v.CityId == request.CityId.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim();

            // ==========================================================
            // NEDEN EF.Functions.ILike KULLANMIYORUM?
            // ==========================================================
            // İlk yazisimda ILike kullanmistim -- PostgreSQL'in
            // büyük/küçük harf duyarsiz LIKE'i ve tam ihtiyacimiz olan sey.
            //
            // Ama derleme hatası verdi: ILike, Npgsql paketinde tanimli.
            // Kullanmak için Application katmanina Npgsql referansı
            // eklemem gerekirdi -- yani is mantığı katmanimiz
            // POSTGRESQL'E OZGU hale gelirdi.
            //
            // Bu, EF Core soyutlamasina bagimli olmaktan farklı bir sey.
            // DbSet ve IQueryable her saglayicida aynı çalışır; ILike
            // yalnızca PostgreSQL'de var. Veritabanini degistirdigimizde
            // (veya integration testlerde SQLite kullanmak istedigimizde)
            // bu satır derlenmezdi.
            //
            // Bunun yerine saglayicidan bağımsız EF.Functions.Like'i
            // ToLower ile birlikte kullanıyorum.
            //
            // PERFORMANS NOTU: ToLower(), sutun uzerinde fonksiyon
            // uygulandigi için normal bir btree index'i KULLANAMAZ.
            // Cozum, veritabaninda FONKSIYONEL index tanimlamak:
            //     CREATE INDEX ix_venues_name_lower ON "Venues" (LOWER("Name"));
            // Bunu Sprint 11'de (arama ve performans sprinti) ham SQL
            // migration'i olarak ekleyecegiz.
            var pattern = $"%{search.ToLowerInvariant()}%";
            query = query.Where(v => EF.Functions.Like(v.Name.ToLower(), pattern));
        }

        // Toplam sayiyi ONCE alıyorum, sayfalamadan önce.
        //
        // Skip/Take uyguladiktan sonra Count() cagirsaydik yalnızca o
        // sayfadaki kayitlari sayardik ve TotalPages hep 1 çıkardı.
        var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        var items = await query
            .OrderBy(v => v.Name)
            .Skip(request.Skip)
            .Take(request.PageSize)
            .Select(v => new VenueListItem(
                v.Id,
                v.Name,
                v.City.Name,
                // Alt sorgu: EF bunu tek SQL'e cevirir (correlated subquery).
                // Halls'u Include edip C#'ta saymak, TÜM salonlari
                // bellege cekmek demek olurdu.
                v.Halls.Count(h => !h.IsDeleted)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(
            PagedResult<VenueListItem>.Create(items, request.PageNumber, request.PageSize, totalCount));
    }
}

// ===================================================================
// DETAY
// ===================================================================

public sealed record GetVenueByIdQuery(Guid Id) : IRequest<Result<VenueDetail>>;

internal sealed class GetVenueByIdQueryHandler
    : IRequestHandler<GetVenueByIdQuery, Result<VenueDetail>>
{
    private readonly IApplicationDbContext _context;

    public GetVenueByIdQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<Result<VenueDetail>> Handle(
        GetVenueByIdQuery request,
        CancellationToken cancellationToken)
    {
        var venue = await _context.Venues
            .AsNoTracking()
            .Where(v => v.Id == request.Id)
            .Select(v => new VenueDetail(
                v.Id,
                v.Name,
                v.Address,
                v.CityId,
                v.City.Name,
                v.Latitude,
                v.Longitude,
                v.Halls
                    .Where(h => !h.IsDeleted)
                    .OrderBy(h => h.Name)
                    .Select(h => new HallSummary(
                        h.Id,
                        h.Name,
                        h.Capacity,
                        h.SeatLayouts.Count(sl => !sl.IsDeleted)))
                    .ToList()))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return venue is null
            ? Result.Failure<VenueDetail>(VenueErrors.NotFound)
            : Result.Success(venue);
    }
}
