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

// DTO'lar

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

// LISTELEME -- PDF: GET /api/v1/events

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
    /// Controller bu alanı, kullanıcının ROLUNE göre SUNUCUDA set ediyor --
    /// istemciden gelen degere GUVENMIYORUZ. Guvenseydik, herhangi bir
    /// kullanıcı sorguya includeUnpublished=true ekleyip yayinlanmamis
    /// etkinlikleri gorurdu.
    /// </summary>
    public bool IncludeUnpublished { get; init; }

    /// <summary>
    /// Yalnızca bu organizatorun etkinlikleri.
    /// PDF Sprint 11 filtresi: "Organizatör".
    /// </summary>
    public Guid? OrganizerId { get; init; }

    // PDF Sprint 11'in istedigi kalan filtreler

    /// <summary>
    /// En düşük bilet fiyati. PDF filtresi: "Fiyat aralığı".
    /// </summary>
    /// <remarks>
    /// Etkinligin BIRDEN FAZLA bilet türü var (Tam, Ogrenci, VIP...)
    /// ve her birinin fiyati farklı.
    ///
    /// Kullanıcı "en fazla 300 TL" dediginde ne bekler? "300 TL'ye
    /// girebilecegim etkinlikler" -- yani EN UCUZ bileti 300'un
    /// altinda olanlar. VIP bileti 1000 TL olsa bile.
    ///
    /// Bu yüzden "herhangi bir bilet türü araliga giriyorsa" seklinde
    /// filtreliyoruz. "Tüm bilet türleri araliga girmeli" deseydim
    /// kullanıcı pahali bir VIP secenegi yuzunden uygun fiyatli
    /// etkinligi hiç goremezdi.
    /// </remarks>
    public decimal? MinPrice { get; init; }

    /// <summary>En yüksek bilet fiyati. Bkz. MinPrice.</summary>
    public decimal? MaxPrice { get; init; }

    /// <summary>
    /// Yaş sinirinin ust değeri. PDF filtresi: "Yaş sınırı".
    /// </summary>
    /// <remarks>
    /// Adi neden "MaxMinimumAge"? Kulaga garip geliyor ama doğru olan
    /// bu: etkinliğin MinimumAge alanı var ve biz onun EN FAZLA kac
    /// olabilecegini soruyorum.
    ///
    /// Kullanıcı acisindan anlami: "18 yasindayim, girebilecegim
    /// etkinlikleri goster" -> maxMinimumAge=18.
    ///
    /// Yalnızca "age" deseydim, "18 yaş sınırı OLAN etkinlikler" mi
    /// yoksa "18 yasindakinin girebilecegi etkinlikler" mi belirsiz
    /// kalırdı -- ve ikisi çok farklı sonuç verirdi.
    /// </remarks>
    public int? MaxMinimumAge { get; init; }

    /// <summary>
    /// Etkinlik durumu. PDF filtresi: "Satış durumu".
    /// </summary>
    /// <remarks>
    /// Istemci buraya Draft veya PendingApproval gonderebilir. Sorun
    /// değil: gorunurluk filtresi (PublicStatuses) DAHA SONRA
    /// uygulaniyor ve yetkisiz kullanıcı için sonuç yine boş döner.
    /// Iki filtre birlikte çalışıyor, biri digerini geçersiz kilmiyor.
    /// </remarks>
    public EventStatus? Status { get; init; }

    // SIRALAMA -- PDF: GET /api/v1/events?sortBy=startDate&sortDirection=asc

    /// <summary>
    /// Sıralama alanı. Geçerli degerler: date, title, created.
    /// Boş veya taninmayan bir deger verilirse tarihe göre siralanir.
    /// </summary>
    public string? SortBy { get; init; }

    /// <summary>Sıralama yonu: "asc" veya "desc". Varsayılan: asc.</summary>
    public string? SortDirection { get; init; }
}

internal sealed class GetEventsQueryHandler
    : IRequestHandler<GetEventsQuery, Result<PagedResult<EventListItem>>>
{
    /// <summary>
    /// Anonim kullanıcının gorebilecegi durumlar.
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
            "ToLower() bir IFADE AGACI içinde ve .NET'te calismiyor; EF Core " +
            "önü SQL'deki LOWER() fonksiyonuna ceviriyor. Kultur ayarinin " +
            "sonuca etkisi yok. ToLowerInvariant() ise EF tarafından SQL'e " +
            "cevrilemedigi icin kullanilamaz.")]
    [SuppressMessage(
        "Globalization",
        "CA1311:Specify a culture or use an invariant version",
        Justification = "Bkz. CA1304 açıklaması.")]
    public async Task<Result<PagedResult<EventListItem>>> Handle(
        GetEventsQuery request,
        CancellationToken cancellationToken)
    {
        var query = _context.Events.AsNoTracking();

        // GORUNURLUK FILTRESI -- BU DOSYANIN EN ONEMLI SATIRI
        //
        // Bu filtre olmasaydı herkes Draft ve PendingApproval durumundaki
        // etkinlikleri gorurdu: organizatorun henüz yayinlamadigi,
        // fiyatlari belirlenmemis, belki de vazgecilecek etkinlikler.
        //
        // Ayrıca Suspended (admin tarafından uygunsuz bulunup askiya
        // alinmis) etkinlikler de görünürdü -- askiya almanin hiçbir
        // anlami kalmazdi.
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

        // ---- PDF Sprint 11: Satış durumu ----
        if (request.Status.HasValue)
        {
            query = query.Where(e => e.Status == request.Status.Value);
        }

        // ---- PDF Sprint 11: Yaş sınırı ----
        if (request.MaxMinimumAge.HasValue)
        {
            query = query.Where(e => e.MinimumAge <= request.MaxMinimumAge.Value);
        }

        // ---- PDF Sprint 11: Fiyat aralığı ----
        //
        // Any(...) ile "en az bir bilet türü araliga giriyorsa" diyorum.
        // EF bunu SQL'de EXISTS alt sorgusuna ceviriyor -- yani tüm
        // bilet turlerini bellege cekmiyoruz.
        //
        // Silinmis bilet türleri için AYRICA filtre YAZMIYORUM: EF
        // global query filter (HasQueryFilter) zaten navigation
        // koleksiyonlarina da WHERE "IsDeleted" = false ekliyor.
        // Elle tekrar yazmak, aynı kuralı iki yerde tutmak olurdu.
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

            // Başlık VE aciklamada ariyorum.
            //
            // Kullanıcı "rock" yazdiginda, baslikta gecmese bile
            // aciklamasinda gecen konserleri de bulmali. Yalnızca
            // baslikta arasaydik arama sonuclari fakir kalırdı.
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
    /// NEDEN switch? Neden alan adını doğrudan kullanmiyorum?
    ///
    /// Bazi kutuphaneler `OrderBy("Title")` gibi METİN alarak sıralama
    /// yapmayi mumkun kiliyor. Cazip ama TEHLIKELI.
    ///
    /// Iki sebeple:
    ///
    /// 1) GÜVENLİK: Ham SQL uretilen bir yapida bu, sıralama üzerinden
    ///    SQL enjeksiyonuna kapi acar. (EF Core'un LINQ'i buna karsi
    ///    korumali ama aliskanlik olarak dogrusunu yazıyorum.)
    ///
    /// 2) VERI SIZINTISI: Istemci sortBy=PasswordHash yazarsa, sonuclar
    ///    o alana göre SIRALANIR. Alan yanitta gorunmese bile,
    ///    siralamanin kendisi bilgi verir -- birden fazla sorguyla
    ///    degerler ikili aramayla cikarilabilir.
    ///
    /// Beyaz liste (whitelist) ile yalnızca IZIN VERDIGIM alanlar
    /// siralanabiliyor. Taninmayan deger sessizce varsayilana dusuyor:
    /// hata donmek yerine mantikli bir sonuç vermek, listeleme
    /// uclarinda daha iyi bir davranis.
    /// </remarks>
    private static IQueryable<EventEntity> ApplySorting(
        IQueryable<EventEntity> query,
        string? sortBy,
        string? sortDirection)
    {
        // OrdinalIgnoreCase: kultur bağımsız karsilastirma.
        //
        // Turkce kulturde "TITLE".ToLower() "tıtle" verir (noktasiz i)
        // ve "title" ile ESLESMEZ. Sunucunun kultur ayarina göre
        // calisan/çalışmayan bir sıralama, teshisi çok zor bir hata
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

            // Varsayılan: etkinlik tarihi, yaklasanlar önce.
            //
            // Kullanıcı "bu hafta ne var" diye bakiyor; en uzak
            // tarihli konseri ilk sırada gormek istemez.
            _ => descending
                ? query.OrderByDescending(e => e.EventDate)
                : query.OrderBy(e => e.EventDate),
        };
    }
}

// DETAY -- PDF: GET /api/v1/events/{id}

public sealed record GetEventByIdQuery(Guid Id, bool IncludeUnpublished)
    : IRequest<Result<EventDetail>>;

/// <summary>
/// Herkese açık etkinlik durumlari.
/// </summary>
/// <remarks>
/// Iki handler da (liste ve detay) aynı listeyi kullaniyor. Ayrı ayrı
/// yazsaydım, ilerde yeni bir durum eklendiginde birini guncelleyip
/// digerini unutmak kacinilmazdi -- ve sonuç bir GÜVENLİK acigi olurdu:
/// listede gorunmeyen bir etkinlik detayda görünür (veya tersi).
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
        // PDF Sprint 11: "Etkinlik detaylari" cache edilebilir.
        //
        // PDF kuralı: "Kullanıcıya ozel hassas veriler ortak cache
        // içinde tutulmamalidir."
        //
        // Bu sorgu ILK BAKISTA kullanicidan bağımsız görünüyor -- aynı
        // etkinlik herkese aynı döner. Ama bir alan var:
        // IncludeUnpublished. Admin için true, herkes için false.
        // Yani AYNI Id, ROLE GORE FARKLI sonuç veriyor.
        //
        // ILK AKLIMA GELEN COZUM VE NEDEN VAZGECTIM
        //
        // "Anahtara rolü de ekleyeyim" diye dusundum:
        //
        //     event:detail:{id}:admin
        //     event:detail:{id}:public
        //
        // Calisirdi. Ama YAYINLANMAMIS etkinliğin tüm içeriği Redis e
        // yazilmis olurdu. Redis e erisen herhangi biri (yanlış
        // yapilandirilmis bir port, başka bir uygulama, bir yedek
        // dosyasi) organizatorun henüz yayinlamadigi etkinlikleri
        // okuyabilirdi.
        //
        // SECTIGIM COZUM: YAYINLANMAMIS ICERIK HİÇ ONBELLEKLENMEZ
        //
        // Admin gorunumu önbelleği TAMAMEN ATLIYOR ve doğrudan
        // veritabanina gidiyor. Onbellege giren sorgu ise YALNIZCA
        // yayinlanmis etkinlikleri donduren surum. Redis te hiçbir
        // zaman taslak veri bulunmuyor.
        //
        // Maliyeti: admin istekleri onbellekten yararlanmiyor. Kabul
        // edilebilir -- admin trafigi toplam trafigin binde biri bile
        // değil ve önbellek zaten olcek için var.
        //
        // Yan fayda: anahtar sayısı ikiye katlanmiyor.
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

        // IDOR KORUMASI -- ARTIK SORGUNUN ICINDE
        //
        // Onceden bu kontrol veriyi CEKTIKTEN SONRA yapiliyordu.
        // Onbellek eklerken sorgunun icine tasidim ve daha da güvenli
        // oldu.
        //
        // Detay endpoint i Id ile doğrudan cagrilabiliyor. Kontrol
        // olmasaydı birisi Id yi gorup (veya tahmin edip) taslak
        // etkinligi okuyabilirdi. Buna "guvensiz doğrudan nesne
        // referansı" (IDOR) denir.
        //
        // Sorguya tasimanin ek faydasi: yetkisiz kullanıcı için
        // veritabanindan HİÇ veri gelmiyor, dolayisiyla onbellege de
        // yazılamıyor. Önceki yerlesimde veri önce cekilip sonra
        // reddediliyordu.
        //
        // Bulunamayan kayıt 404 dönüyor, 403 değil -- bilerek.
        // 403 "bu kayıt VAR ama goremezsin" der ve varligini DOGRULAR.
        // 404 hiçbir sey sizdirmaz.
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

                // Organizatör adını ALT SORGU ile alıyorum.
                //
                // Sebep: Event ile OrganizerProfile arasında navigation
                // ozelligi tanimlamadim. Tanimlayabilirdim ama Event
                // sinifi zaten kalabalik ve bu bilgi yalnızca detay
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

                        // EventSession'da Hall navigation'i YOK -- yalnızca
                        // HallId var. Alt sorgu ile adını alıyorum.
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
