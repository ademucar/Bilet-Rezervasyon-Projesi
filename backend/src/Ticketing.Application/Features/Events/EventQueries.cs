using System.Diagnostics.CodeAnalysis;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Abstractions.Caching;
using Ticketing.Application.Abstractions.Persistence;
using Ticketing.Application.Common.Pagination;
using Ticketing.Application.Common.Results;
using Ticketing.Domain.Enums;

using EventEntity = Ticketing.Domain.Entities.Event;

namespace Ticketing.Application.Features.Events;

// ===================================================================
// DTO'lar
// ===================================================================

public sealed record EventListItem(
    Guid Id,
    string Title,
    string CategoryName,
    string CityName,
    string VenueName,
    string? PosterImagePath,
    DateTimeOffset EventDate,
    EventStatus Status,
    int MinimumAge,
    int SessionCount);

public sealed record EventDetail(
    Guid Id,
    string Title,
    string Description,
    Guid CategoryId,
    string CategoryName,
    Guid OrganizerId,
    string OrganizerName,
    Guid CityId,
    string CityName,
    Guid VenueId,
    string VenueName,
    string VenueAddress,
    Guid HallId,
    string HallName,
    string? PosterImagePath,
    int MinimumAge,
    int DurationMinutes,
    int MaxTicketsPerUser,
    DateTimeOffset SalesStartDate,
    DateTimeOffset SalesEndDate,
    DateTimeOffset EventDate,
    EventStatus Status,
    string? CancellationReason,
    IReadOnlyList<EventSessionDto> Sessions);

public sealed record EventSessionDto(
    Guid Id,
    DateTimeOffset StartDate,
    DateTimeOffset EndDate,
    Guid HallId,
    string HallName,
    Guid SeatLayoutId,
    string SeatLayoutName,
    EventSessionStatus Status,
    bool AreSeatsGenerated);

// ===================================================================
// LISTELEME -- PDF: GET /api/v1/events
// ===================================================================

public sealed record GetEventsQuery : PaginationRequest, IRequest<Result<PagedResult<EventListItem>>>
{
    public string? Search { get; init; }

    public Guid? CityId { get; init; }

    public Guid? CategoryId { get; init; }

    public Guid? VenueId { get; init; }

    public DateTimeOffset? DateFrom { get; init; }

    public DateTimeOffset? DateTo { get; init; }

    /// <summary>
    /// Taslak ve onay bekleyen etkinlikleri de getir.
    ///
    /// Controller bu alani, kullanicinin ROLUNE gore SUNUCUDA set ediyor --
    /// istemciden gelen degere GUVENMIYORUZ. Guvenseydik, herhangi bir
    /// kullanici sorguya includeUnpublished=true ekleyip yayinlanmamis
    /// etkinlikleri gorurdu.
    /// </summary>
    public bool IncludeUnpublished { get; init; }

    /// <summary>
    /// Yalnizca bu organizatorun etkinlikleri.
    /// PDF Sprint 11 filtresi: "Organizator".
    /// </summary>
    public Guid? OrganizerId { get; init; }

    // ==============================================================
    // PDF Sprint 11'in istedigi kalan filtreler
    // ==============================================================

    /// <summary>
    /// En dusuk bilet fiyati. PDF filtresi: "Fiyat araligi".
    /// </summary>
    /// <remarks>
    /// Etkinligin BIRDEN FAZLA bilet turu var (Tam, Ogrenci, VIP...)
    /// ve her birinin fiyati farkli.
    ///
    /// Kullanici "en fazla 300 TL" dediginde ne bekler? "300 TL'ye
    /// girebilecegim etkinlikler" -- yani EN UCUZ bileti 300'un
    /// altinda olanlar. VIP bileti 1000 TL olsa bile.
    ///
    /// Bu yuzden "herhangi bir bilet turu araliga giriyorsa" seklinde
    /// filtreliyoruz. "Tum bilet turleri araliga girmeli" deseydik
    /// kullanici pahali bir VIP secenegi yuzunden uygun fiyatli
    /// etkinligi hic goremezdi.
    /// </remarks>
    public decimal? MinPrice { get; init; }

    /// <summary>En yuksek bilet fiyati. Bkz. MinPrice.</summary>
    public decimal? MaxPrice { get; init; }

    /// <summary>
    /// Yas sinirinin ust degeri. PDF filtresi: "Yas siniri".
    /// </summary>
    /// <remarks>
    /// Adi neden "MaxMinimumAge"? Kulaga garip geliyor ama dogru olan
    /// bu: etkinligin MinimumAge alani var ve biz onun EN FAZLA kac
    /// olabilecegini soruyoruz.
    ///
    /// Kullanici acisindan anlami: "18 yasindayim, girebilecegim
    /// etkinlikleri goster" -> maxMinimumAge=18.
    ///
    /// Yalnizca "age" deseydik, "18 yas siniri OLAN etkinlikler" mi
    /// yoksa "18 yasindakinin girebilecegi etkinlikler" mi belirsiz
    /// kalirdi -- ve ikisi cok farkli sonuc verirdi.
    /// </remarks>
    public int? MaxMinimumAge { get; init; }

    /// <summary>
    /// Etkinlik durumu. PDF filtresi: "Satis durumu".
    /// </summary>
    /// <remarks>
    /// Istemci buraya Draft veya PendingApproval gonderebilir. Sorun
    /// degil: gorunurluk filtresi (PublicStatuses) DAHA SONRA
    /// uygulaniyor ve yetkisiz kullanici icin sonuc yine bos doner.
    /// Iki filtre birlikte calisiyor, biri digerini gecersiz kilmiyor.
    /// </remarks>
    public EventStatus? Status { get; init; }

    // ==============================================================
    // SIRALAMA -- PDF: GET /api/v1/events?sortBy=startDate&sortDirection=asc
    // ==============================================================

    /// <summary>
    /// Siralama alani. Gecerli degerler: date, title, created.
    /// Bos veya taninmayan bir deger verilirse tarihe gore siralanir.
    /// </summary>
    public string? SortBy { get; init; }

    /// <summary>Siralama yonu: "asc" veya "desc". Varsayilan: asc.</summary>
    public string? SortDirection { get; init; }
}

internal sealed class GetEventsQueryHandler
    : IRequestHandler<GetEventsQuery, Result<PagedResult<EventListItem>>>
{
    /// <summary>
    /// Anonim kullanicinin gorebilecegi durumlar.
    ///
    /// static readonly: her istekte yeni dizi ayirmiyoruz. Bu, sitenin
    /// en sik calisan sorgusu olacak.
    /// </summary>
    private static readonly EventStatus[] PublicStatuses = EventVisibility.PublicStatuses;

    private readonly IApplicationDbContext _context;

    public GetEventsQueryHandler(IApplicationDbContext context) => _context = context;

    [SuppressMessage(
        "Globalization",
        "CA1304:Specify CultureInfo",
        Justification =
            "ToLower() bir IFADE AGACI icinde ve .NET'te calismiyor; EF Core " +
            "onu SQL'deki LOWER() fonksiyonuna ceviriyor. Kultur ayarinin " +
            "sonuca etkisi yok. ToLowerInvariant() ise EF tarafindan SQL'e " +
            "cevrilemedigi icin kullanilamaz.")]
    [SuppressMessage(
        "Globalization",
        "CA1311:Specify a culture or use an invariant version",
        Justification = "Bkz. CA1304 aciklamasi.")]
    public async Task<Result<PagedResult<EventListItem>>> Handle(
        GetEventsQuery request,
        CancellationToken cancellationToken)
    {
        var query = _context.Events.AsNoTracking();

        // ==============================================================
        // GORUNURLUK FILTRESI -- BU DOSYANIN EN ONEMLI SATIRI
        // ==============================================================
        // Bu filtre olmasaydi herkes Draft ve PendingApproval durumundaki
        // etkinlikleri gorurdu: organizatorun henuz yayinlamadigi,
        // fiyatlari belirlenmemis, belki de vazgecilecek etkinlikler.
        //
        // Ayrica Suspended (admin tarafindan uygunsuz bulunup askiya
        // alinmis) etkinlikler de gorunurdu -- askiya almanin hicbir
        // anlami kalmazdi.
        // ==============================================================
        if (!request.IncludeUnpublished)
        {
            query = query.Where(e => PublicStatuses.Contains(e.Status));
        }

        if (request.OrganizerId.HasValue)
        {
            query = query.Where(e => e.OrganizerId == request.OrganizerId.Value);
        }

        if (request.CityId.HasValue)
        {
            query = query.Where(e => e.CityId == request.CityId.Value);
        }

        if (request.CategoryId.HasValue)
        {
            query = query.Where(e => e.CategoryId == request.CategoryId.Value);
        }

        if (request.VenueId.HasValue)
        {
            query = query.Where(e => e.VenueId == request.VenueId.Value);
        }

        if (request.DateFrom.HasValue)
        {
            query = query.Where(e => e.EventDate >= request.DateFrom.Value);
        }

        if (request.DateTo.HasValue)
        {
            query = query.Where(e => e.EventDate <= request.DateTo.Value);
        }

        // ---- PDF Sprint 11: Satis durumu ----
        if (request.Status.HasValue)
        {
            query = query.Where(e => e.Status == request.Status.Value);
        }

        // ---- PDF Sprint 11: Yas siniri ----
        if (request.MaxMinimumAge.HasValue)
        {
            query = query.Where(e => e.MinimumAge <= request.MaxMinimumAge.Value);
        }

        // ---- PDF Sprint 11: Fiyat araligi ----
        //
        // Any(...) ile "en az bir bilet turu araliga giriyorsa" diyoruz.
        // EF bunu SQL'de EXISTS alt sorgusuna ceviriyor -- yani tum
        // bilet turlerini bellege cekmiyoruz.
        //
        // Silinmis bilet turleri icin AYRICA filtre YAZMIYORUM: EF
        // global query filter (HasQueryFilter) zaten navigation
        // koleksiyonlarina da WHERE "IsDeleted" = false ekliyor.
        // Elle tekrar yazmak, ayni kurali iki yerde tutmak olurdu.
        if (request.MinPrice.HasValue)
        {
            query = query.Where(e => e.TicketTypes.Any(
                tt => tt.Price.Amount >= request.MinPrice.Value));
        }

        if (request.MaxPrice.HasValue)
        {
            query = query.Where(e => e.TicketTypes.Any(
                tt => tt.Price.Amount <= request.MaxPrice.Value));
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var pattern = $"%{request.Search.Trim().ToLowerInvariant()}%";

            // Baslik VE aciklamada ariyorum.
            //
            // Kullanici "rock" yazdiginda, baslikta gecmese bile
            // aciklamasinda gecen konserleri de bulmali. Yalnizca
            // baslikta arasaydik arama sonuclari fakir kalirdi.
            query = query.Where(e =>
                EF.Functions.Like(e.Title.ToLower(), pattern) ||
                EF.Functions.Like(e.Description.ToLower(), pattern));
        }

        var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        var items = await ApplySorting(query, request.SortBy, request.SortDirection)
            .Skip(request.Skip)
            .Take(request.PageSize)
            .Select(e => new EventListItem(
                e.Id,
                e.Title,
                e.Category.Name,
                e.City.Name,
                e.Venue.Name,
                e.PosterImagePath,
                e.EventDate,
                e.Status,
                e.MinimumAge,
                e.Sessions.Count(s => s.Status != EventSessionStatus.Cancelled)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(
            PagedResult<EventListItem>.Create(items, request.PageNumber, request.PageSize, totalCount));
    }

    /// <summary>
    /// Siralamayi uygular. PDF Sprint 11: "Sorting".
    /// </summary>
    /// <remarks>
    /// ==============================================================
    /// NEDEN switch? Neden alan adini dogrudan kullanmiyoruz?
    /// ==============================================================
    /// Bazi kutuphaneler `OrderBy("Title")` gibi METIN alarak siralama
    /// yapmayi mumkun kiliyor. Cazip ama TEHLIKELI.
    ///
    /// Iki sebeple:
    ///
    /// 1) GUVENLIK: Ham SQL uretilen bir yapida bu, siralama uzerinden
    ///    SQL enjeksiyonuna kapi acar. (EF Core'un LINQ'i buna karsi
    ///    korumali ama aliskanlik olarak dogrusunu yaziyorum.)
    ///
    /// 2) VERI SIZINTISI: Istemci sortBy=PasswordHash yazarsa, sonuclar
    ///    o alana gore SIRALANIR. Alan yanitta gorunmese bile,
    ///    siralamanin kendisi bilgi verir -- birden fazla sorguyla
    ///    degerler ikili aramayla cikarilabilir.
    ///
    /// Beyaz liste (whitelist) ile yalnizca IZIN VERDIGIM alanlar
    /// siralanabiliyor. Taninmayan deger sessizce varsayilana dusuyor:
    /// hata donmek yerine mantikli bir sonuc vermek, listeleme
    /// uclarinda daha iyi bir davranis.
    /// ==============================================================
    /// </remarks>
    private static IQueryable<EventEntity> ApplySorting(
        IQueryable<EventEntity> query,
        string? sortBy,
        string? sortDirection)
    {
        // OrdinalIgnoreCase: kultur bagimsiz karsilastirma.
        //
        // Turkce kulturde "TITLE".ToLower() "tıtle" verir (noktasiz i)
        // ve "title" ile ESLESMEZ. Sunucunun kultur ayarina gore
        // calisan/calismayan bir siralama, teshisi cok zor bir hata
        // olurdu.
        var descending = string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase);

        return sortBy?.ToUpperInvariant() switch
        {
            "TITLE" => descending
                ? query.OrderByDescending(e => e.Title)
                : query.OrderBy(e => e.Title),

            "CREATED" => descending
                ? query.OrderByDescending(e => e.CreatedAt)
                : query.OrderBy(e => e.CreatedAt),

            // Varsayilan: etkinlik tarihi, yaklasanlar once.
            //
            // Kullanici "bu hafta ne var" diye bakiyor; en uzak
            // tarihli konseri ilk sirada gormek istemez.
            _ => descending
                ? query.OrderByDescending(e => e.EventDate)
                : query.OrderBy(e => e.EventDate),
        };
    }
}

// ===================================================================
// DETAY -- PDF: GET /api/v1/events/{id}
// ===================================================================

public sealed record GetEventByIdQuery(Guid Id, bool IncludeUnpublished)
    : IRequest<Result<EventDetail>>;

/// <summary>
/// Herkese acik etkinlik durumlari.
/// </summary>
/// <remarks>
/// Iki handler da (liste ve detay) ayni listeyi kullaniyor. Ayri ayri
/// yazsaydik, ilerde yeni bir durum eklendiginde birini guncelleyip
/// digerini unutmak kacinilmazdi -- ve sonuc bir GUVENLIK acigi olurdu:
/// listede gorunmeyen bir etkinlik detayda gorunur (veya tersi).
/// </remarks>
internal static class EventVisibility
{
    public static readonly EventStatus[] PublicStatuses =
    [
        EventStatus.Published,
        EventStatus.SalesOpen,
        EventStatus.SalesClosed,
        EventStatus.Completed
    ];
}

internal sealed class GetEventByIdQueryHandler
    : IRequestHandler<GetEventByIdQuery, Result<EventDetail>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICacheService _cache;

    public GetEventByIdQueryHandler(IApplicationDbContext context, ICacheService cache)
    {
        _context = context;
        _cache = cache;
    }

    public async Task<Result<EventDetail>> Handle(
        GetEventByIdQuery request,
        CancellationToken cancellationToken)
    {
        // ==============================================================
        // PDF Sprint 11: "Etkinlik detaylari" cache edilebilir.
        // ==============================================================
        // PDF kurali: "Kullaniciya ozel hassas veriler ortak cache
        // icinde tutulmamalidir."
        //
        // Bu sorgu ILK BAKISTA kullanicidan bagimsiz gorunuyor -- ayni
        // etkinlik herkese ayni doner. Ama bir alan var:
        // IncludeUnpublished. Admin icin true, herkes icin false.
        // Yani AYNI Id, ROLE GORE FARKLI sonuc veriyor.
        //
        // ------------------------------------------------------------
        // ILK AKLIMA GELEN COZUM VE NEDEN VAZGECTIM
        // ------------------------------------------------------------
        // "Anahtara rolu de ekleyeyim" diye dusundum:
        //
        //     event:detail:{id}:admin
        //     event:detail:{id}:public
        //
        // Calisirdi. Ama YAYINLANMAMIS etkinligin tum icerigi Redis e
        // yazilmis olurdu. Redis e erisen herhangi biri (yanlis
        // yapilandirilmis bir port, baska bir uygulama, bir yedek
        // dosyasi) organizatorun henuz yayinlamadigi etkinlikleri
        // okuyabilirdi.
        //
        // ------------------------------------------------------------
        // SECTIGIM COZUM: YAYINLANMAMIS ICERIK HIC ONBELLEKLENMEZ
        // ------------------------------------------------------------
        // Admin gorunumu onbellegi TAMAMEN ATLIYOR ve dogrudan
        // veritabanina gidiyor. Onbellege giren sorgu ise YALNIZCA
        // yayinlanmis etkinlikleri donduren surum. Redis te hicbir
        // zaman taslak veri bulunmuyor.
        //
        // Maliyeti: admin istekleri onbellekten yararlanmiyor. Kabul
        // edilebilir -- admin trafigi toplam trafigin binde biri bile
        // degil ve onbellek zaten olcek icin var.
        //
        // Yan fayda: anahtar sayisi ikiye katlanmiyor.
        // ==============================================================
        var detail = request.IncludeUnpublished
            ? await LoadAsync(request.Id, includeUnpublished: true, cancellationToken)
                .ConfigureAwait(false)
            : await _cache.GetOrCreateAsync(
                CacheKeys.EventDetail(request.Id),
                ct => LoadAsync(request.Id, includeUnpublished: false, ct),
                CacheDurations.EventDetail,
                cancellationToken).ConfigureAwait(false);

        if (detail is null)
        {
            return Result.Failure<EventDetail>(EventErrors.NotFound);
        }

        return Result.Success(detail);
    }

    private async Task<EventDetail?> LoadAsync(
        Guid eventId,
        bool includeUnpublished,
        CancellationToken cancellationToken)
    {
        var query = _context.Events
            .AsNoTracking()
            .Where(e => e.Id == eventId);

        // ==============================================================
        // IDOR KORUMASI -- ARTIK SORGUNUN ICINDE
        // ==============================================================
        // Onceden bu kontrol veriyi CEKTIKTEN SONRA yapiliyordu.
        // Onbellek eklerken sorgunun icine tasidim ve daha da guvenli
        // oldu.
        //
        // Detay endpoint i Id ile dogrudan cagrilabiliyor. Kontrol
        // olmasaydi birisi Id yi gorup (veya tahmin edip) taslak
        // etkinligi okuyabilirdi. Buna "guvensiz dogrudan nesne
        // referansi" (IDOR) denir.
        //
        // Sorguya tasimanin ek faydasi: yetkisiz kullanici icin
        // veritabanindan HIC veri gelmiyor, dolayisiyla onbellege de
        // yazilamiyor. Onceki yerlesimde veri once cekilip sonra
        // reddediliyordu.
        //
        // Bulunamayan kayit 404 donuyor, 403 degil -- bilerek.
        // 403 "bu kayit VAR ama goremezsin" der ve varligini DOGRULAR.
        // 404 hicbir sey sizdirmaz.
        // ==============================================================
        if (!includeUnpublished)
        {
            query = query.Where(e => EventVisibility.PublicStatuses.Contains(e.Status));
        }

        return await query
            .Select(e => new EventDetail(
                e.Id,
                e.Title,
                e.Description,
                e.CategoryId,
                e.Category.Name,
                e.OrganizerId,

                // Organizator adini ALT SORGU ile aliyorum.
                //
                // Sebep: Event ile OrganizerProfile arasinda navigation
                // ozelligi tanimlamadik. Tanimlayabilirdik ama Event
                // sinifi zaten kalabalik ve bu bilgi yalnizca detay
                // ekraninda gerekiyor.
                _context.OrganizerProfiles
                    .Where(p => p.Id == e.OrganizerId)
                    .Select(p => p.CompanyName)
                    .FirstOrDefault() ?? "-",

                e.CityId,
                e.City.Name,
                e.VenueId,
                e.Venue.Name,
                e.Venue.Address,
                e.HallId,
                e.Hall.Name,
                e.PosterImagePath,
                e.MinimumAge,
                e.DurationMinutes,
                e.MaxTicketsPerUser,
                e.SalesStartDate,
                e.SalesEndDate,
                e.EventDate,
                e.Status,
                e.CancellationReason,
                e.Sessions
                    .OrderBy(s => s.StartDate)
                    .Select(s => new EventSessionDto(
                        s.Id,
                        s.StartDate,
                        s.EndDate,
                        s.HallId,

                        // EventSession'da Hall navigation'i YOK -- yalnizca
                        // HallId var. Alt sorgu ile adini aliyorum.
                        //
                        // Navigation eklemek de mumkundu ama EventSession'i
                        // olabildigince yalin tutmak istiyorum: Sprint 7'de
                        // bu entity koltuk kilitleme akisinin merkezinde
                        // olacak ve her ekstra navigation, yanlislikla
                        // yuklenip performans sorunu cikarma riski tasiyor.
                        _context.Halls
                            .Where(h => h.Id == s.HallId)
                            .Select(h => h.Name)
                            .FirstOrDefault() ?? "-",
                        s.SeatLayoutId,
                        _context.SeatLayouts
                            .Where(sl => sl.Id == s.SeatLayoutId)
                            .Select(sl => sl.Name)
                            .FirstOrDefault() ?? "-",
                        s.Status,
                        s.AreSeatsGenerated))
                    .ToList()))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
