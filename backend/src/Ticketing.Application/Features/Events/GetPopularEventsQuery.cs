using MediatR;
using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Abstractions.Caching;
using Ticketing.Application.Abstractions.Persistence;
using Ticketing.Application.Common.Results;
using Ticketing.Domain.Enums;

namespace Ticketing.Application.Features.Events;

/// <summary>
/// Popüler etkinlikler. PDF Sprint 11: "Popüler etkinlikler" cache edilebilir.
/// </summary>
/// <param name="Count">
/// Kac etkinlik donsun. Ust sinir 50.
///
/// Sinir ŞART: istemci count=1000000 gonderirse hem sorgu agirlasir hem
/// de her farklı deger AYRI bir önbellek anahtari üretir. Sinirsiz
/// birakmak, saldirganin binlerce anahtar uretip Redis bellegini
/// doldurmasina izin vermek olurdu (cache poisoning'in basit bir türü).
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
        // Önce sinirla, SONRA anahtar üret.
        //
        // Ters sırada yapsaydik anahtar ham degerden uretilirdi ve
        // count=999 ile count=1000 ayrı anahtarlar olurdu -- oysa
        // ikisi de aynı (50 elemanli) sonucu döner.
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
    /// Satılan AKTIF bilet sayısına göre. Basit ama doğru bir olcut:
    /// insanlarin parasiyla oy verdiği sey.
    ///
    /// Alternatifleri elerken dusundugum:
    ///
    ///   Goruntulenme sayısı -> henüz toplamiyoruz (Sprint 13)
    ///   Favori sayısı       -> Sprint 12'de gelecek
    ///   Doluluk oranı       -> küçük salonlari haksiz one cikarirdi
    ///                          (50 kisilik salon %100 dolu, 5000
    ///                          kisilik salon %80 -- ikincisi 4000
    ///                          bilet satmis)
    ///
    /// İptal/iade edilmiş biletleri SAYMIYORUZ: iade edilen bir
    /// etkinligi popüler göstermek yanıltıcı olurdu.
    ///
    /// ==============================================================
    /// BU SORGU NEDEN ONBELLEKTEN EN COK KAZANAN SORGU?
    /// ==============================================================
    /// Icinde gruplama ve sayım var; veritabani her calismada
    /// Tickets tablosunu tarayip Events ile birlestiriyor. Bilet
    /// sayısı buyudukce maliyeti artiyor.
    ///
    /// Ustelik genellikle ANA SAYFADA gösteriliyor -- yani sitenin
    /// en çok cagrilan sorgusu. En pahali ve en sik: önbellek için
    /// mukemmel aday.
    ///
    /// Sonuç kullanicidan bağımsız olduğu için ortak onbellekte
    /// tutulmasi güvenli.
    /// ==============================================================
    /// </summary>
    private async Task<IReadOnlyList<EventListItem>> LoadAsync(
        int count,
        CancellationToken cancellationToken)
    {
        return await _context.Events
            .AsNoTracking()

            // Yalnızca herkese açık VE henüz gecmemis etkinlikler.
            //
            // Gecmis etkinlikleri elemesek "en popüler" listesi
            // yillar önce yapilmis dev konserlerle dolardi ve
            // kullanıcı bilet alamayacagi seyleri gorurdu.
            .Where(e => EventVisibility.PublicStatuses.Contains(e.Status)
                     && e.EventDate > DateTimeOffset.UtcNow)

            // Sıralama alt sorgusu: bu etkinliğin aktif bilet sayısı.
            //
            // EF bunu SQL'de bir alt sorguya ceviriyor. Biletleri
            // bellege cekip C#'ta saymak felaket olurdu: milyonlarca
            // satır aktarilirdi.
            // NOT: EventSeat uzerinde Tickets navigation ozelligi YOK
            // (bilerek -- Sprint 7'de o entity koltuk kilitleme akisinin
            // merkezindeydi ve yalin tutulmustu). Bu yüzden sayimi
            // Tickets tablosundan alt sorgu ile yapıyorum.
            .OrderByDescending(e => _context.Tickets
                .Count(t => t.EventSeat.EventSession.EventId == e.Id
                         && t.Status == TicketStatus.Active))

            // Ikincil sıralama: esitlik durumunda yaklasan önce.
            //
            // Olmasaydı, hiç bilet satilmamis etkinlikler (hepsi 0)
            // arasindaki sıra veritabaninin keyfine kalırdı ve her
            // sorguda DEGISEBILIRDI. Kullanıcı sayfayı yenileyince
            // listenin karismasi, sistemin bozuk olduğu izlenimi verir.
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
