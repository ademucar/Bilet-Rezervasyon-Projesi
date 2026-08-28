using MediatR;
using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Abstractions.Caching;
using Ticketing.Application.Abstractions.Persistence;
using Ticketing.Application.Common.Results;
using Ticketing.Domain.Enums;

namespace Ticketing.Application.Features.Events;

/// <summary>
/// Populer etkinlikler. PDF Sprint 11: "Populer etkinlikler" cache edilebilir.
/// </summary>
/// <param name="Count">
/// Kac etkinlik donsun. Ust sinir 50.
///
/// Sinir SART: istemci count=1000000 gonderirse hem sorgu agirlasir hem
/// de her farkli deger AYRI bir onbellek anahtari uretir. Sinirsiz
/// birakmak, saldirganin binlerce anahtar uretip Redis bellegini
/// doldurmasina izin vermek olurdu (cache poisoning'in basit bir turu).
/// </param>
public sealed record GetPopularEventsQuery(int Count = 10)
    : IRequest<Result<IReadOnlyList<EventListItem>>>;

internal sealed class GetPopularEventsQueryHandler
    : IRequestHandler<GetPopularEventsQuery, Result<IReadOnlyList<EventListItem>>>
{
    private const int MaxCount = 50;

    private readonly IApplicationDbContext _context;
    private readonly ICacheService _cache;

    public GetPopularEventsQueryHandler(IApplicationDbContext context, ICacheService cache)
    {
        _context = context;
        _cache = cache;
    }

    public async Task<Result<IReadOnlyList<EventListItem>>> Handle(
        GetPopularEventsQuery request,
        CancellationToken cancellationToken)
    {
        // Once sinirla, SONRA anahtar uret.
        //
        // Ters sirada yapsaydik anahtar ham degerden uretilirdi ve
        // count=999 ile count=1000 ayri anahtarlar olurdu -- oysa
        // ikisi de ayni (50 elemanli) sonucu doner.
        var count = Math.Clamp(request.Count, 1, MaxCount);

        var events = await _cache.GetOrCreateAsync(
            CacheKeys.PopularEvents(count),
            ct => LoadAsync(count, ct),
            CacheDurations.PopularEvents,
            cancellationToken).ConfigureAwait(false);

        return Result.Success(events);
    }

    /// <summary>
    /// ==============================================================
    /// "POPULER" NASIL OLCULUYOR?
    /// ==============================================================
    /// Satilan AKTIF bilet sayisina gore. Basit ama dogru bir olcut:
    /// insanlarin parasiyla oy verdigi sey.
    ///
    /// Alternatifleri elerken dusundugum:
    ///
    ///   Goruntulenme sayisi -> henuz toplamiyoruz (Sprint 13)
    ///   Favori sayisi       -> Sprint 12'de gelecek
    ///   Doluluk orani       -> kucuk salonlari haksiz one cikarirdi
    ///                          (50 kisilik salon %100 dolu, 5000
    ///                          kisilik salon %80 -- ikincisi 4000
    ///                          bilet satmis)
    ///
    /// Iptal/iade edilmis biletleri SAYMIYORUZ: iade edilen bir
    /// etkinligi populer gostermek yaniltici olurdu.
    ///
    /// ==============================================================
    /// BU SORGU NEDEN ONBELLEKTEN EN COK KAZANAN SORGU?
    /// ==============================================================
    /// Icinde gruplama ve sayim var; veritabani her calismada
    /// Tickets tablosunu tarayip Events ile birlestiriyor. Bilet
    /// sayisi buyudukce maliyeti artiyor.
    ///
    /// Ustelik genellikle ANA SAYFADA gosteriliyor -- yani sitenin
    /// en cok cagrilan sorgusu. En pahali ve en sik: onbellek icin
    /// mukemmel aday.
    ///
    /// Sonuc kullanicidan bagimsiz oldugu icin ortak onbellekte
    /// tutulmasi guvenli.
    /// ==============================================================
    /// </summary>
    private async Task<IReadOnlyList<EventListItem>> LoadAsync(
        int count,
        CancellationToken cancellationToken)
    {
        return await _context.Events
            .AsNoTracking()

            // Yalnizca herkese acik VE henuz gecmemis etkinlikler.
            //
            // Gecmis etkinlikleri elemesek "en populer" listesi
            // yillar once yapilmis dev konserlerle dolardi ve
            // kullanici bilet alamayacagi seyleri gorurdu.
            .Where(e => EventVisibility.PublicStatuses.Contains(e.Status)
                     && e.EventDate > DateTimeOffset.UtcNow)

            // Siralama alt sorgusu: bu etkinligin aktif bilet sayisi.
            //
            // EF bunu SQL'de bir alt sorguya ceviriyor. Biletleri
            // bellege cekip C#'ta saymak felaket olurdu: milyonlarca
            // satir aktarilirdi.
            // NOT: EventSeat uzerinde Tickets navigation ozelligi YOK
            // (bilerek -- Sprint 7'de o entity koltuk kilitleme akisinin
            // merkezindeydi ve yalin tutulmustu). Bu yuzden sayimi
            // Tickets tablosundan alt sorgu ile yapiyorum.
            .OrderByDescending(e => _context.Tickets
                .Count(t => t.EventSeat.EventSession.EventId == e.Id
                         && t.Status == TicketStatus.Active))

            // Ikincil siralama: esitlik durumunda yaklasan once.
            //
            // Olmasaydi, hic bilet satilmamis etkinlikler (hepsi 0)
            // arasindaki sira veritabaninin keyfine kalirdi ve her
            // sorguda DEGISEBILIRDI. Kullanici sayfayi yenileyince
            // listenin karismasi, sistemin bozuk oldugu izlenimi verir.
            .ThenBy(e => e.EventDate)
            .Take(count)
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
    }
}
