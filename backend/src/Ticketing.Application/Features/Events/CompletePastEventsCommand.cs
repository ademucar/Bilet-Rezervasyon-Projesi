using MediatR;
using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Abstractions.Caching;
using Ticketing.Application.Abstractions.Persistence;
using Ticketing.Application.Abstractions.Time;
using Ticketing.Application.Common.Results;
using Ticketing.Domain.Enums;

namespace Ticketing.Application.Features.Events;

/// <summary>
/// Gecmis etkinlikleri "tamamlandı" olarak isaretler.
/// </summary>
/// <remarks>
/// BU IS SPRINT 12'DE ORTAYA CIKAN BIR EKSIKTEN DOGDU
///
/// PDF Sprint 12 kuralı: "Etkinlik tamamlanmadan yorum yapılamaz."
///
/// Kurali uygulamaya oturunca fark ettim ki Event.Complete() metodu
/// VAR ama HICBIR YERDEN CAGRILMIYOR. Yani hiçbir etkinlik
/// Completed durumuna gecmiyordu.
///
/// Sonucu: kural teknik olarak doğru çalışır ama pratikte HİÇ KIMSE
/// yorum yapamazdi. Ozellik "yazildi" ama hiçbir zaman calismazdi --
/// ve bunu ancak gerçek veriyle deneyen biri fark ederdi.
///
/// PDF Sprint 9 arka plan isleri listesinde bu is SAYILMIYOR. Ama
/// Sprint 12'nin kuralı onsuz anlamsiz kaliyor. Sprintler arasindaki
/// bu bosluk, PDF'i tek tek okuyup "bu gerçekten çalışır mi?" diye
/// sormanin neden gerekli oldugunun iyi bir ornegi.
///
/// NEDEN "ETKİNLİK TARIHI GECTI" YETMIYOR?
///
/// Yorum kontrolunu "EventDate &lt; simdi" diye de yazabilirdim ve is
/// gereksiz olurdu.
///
/// Yazmadim çünkü DURUM, TARIHTEN daha fazla sey anlatiyor:
/// bir etkinlik iptal edilmiş (Cancelled) ya da askiya alinmis
/// (Suspended) olabilir. Tarihi gecmis olmasını "gerceklesti" demek
/// değil. İptal edilmiş bir konser için yorum yapilmasi sacma olurdu.
///
/// Durum makinesi bu ayrimi zaten tutuyor: Complete() yalnızca
/// SalesOpen/SalesClosed durumundan cagrilabiliyor. İptal edilmiş
/// etkinlik bu isten etkilenmiyor.
/// </remarks>
/// <param name="GracePeriodHours">
/// Etkinlik bittikten kac saat sonra tamamlanmis sayilsin.
///
/// Neden hemen değil? Çünkü EventDate etkinliğin BASLANGIC zamani.
/// Bir konser 20:00'de baslayip 23:00'te bitebilir. Tam 20:01'de
/// "tamamlandı" desek, etkinlik daha SURERKEN yorum yapilabilirdi.
///
/// 6 saat, en uzun etkinliklerin bile bitmesine yetiyor.
/// </param>
public sealed record CompletePastEventsCommand(int GracePeriodHours = 6, int BatchSize = 100)
    : IRequest<Result<int>>;

internal sealed class CompletePastEventsCommandHandler
    : IRequestHandler<CompletePastEventsCommand, Result<int>>
{
    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _clock;
    private readonly ICacheService _cache;

    public CompletePastEventsCommandHandler(
        IApplicationDbContext context,
        IDateTimeProvider clock,
        ICacheService cache)
    {
        _context = context;
        _clock = clock;
        _cache = cache;
    }

    public async Task<Result<int>> Handle(
        CompletePastEventsCommand request,
        CancellationToken cancellationToken)
    {
        var esik = _clock.UtcNow.AddHours(-request.GracePeriodHours);

        // Yalnızca Complete() cagrilabilir durumdakiler.
        //
        // Durum makinesi Draft veya Cancelled'dan Completed'a gecise
        // izin vermiyor; onlari sorguya dahil etseydim DomainException
        // firlar ve TÜM parti başarısız olurdu.
        var tamamlanacaklar = await _context.Events
            .Where(e => e.EventDate < esik
                     && (e.Status == EventStatus.SalesOpen
                      || e.Status == EventStatus.SalesClosed))
            .OrderBy(e => e.EventDate)
            .Take(request.BatchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (tamamlanacaklar.Count == 0)
        {
            return Result.Success(0);
        }

        foreach (var evt in tamamlanacaklar)
        {
            // IKI ADIM: ONCE SATISI KAPAT, SONRA TAMAMLA
            //
            // İlk yazimimda doğrudan Complete() cagiriyordum ve is
            // calistiginda DomainException aldim:
            //
            //   "Etkinlik SalesOpen durumundan Completed durumuna
            //    gecemez."
            //
            // Durum makinesine bakinca sebebini gordum:
            //     SalesOpen -> SalesClosed -> Completed
            //
            // Yani ARA DURUM atlanamiyor. Ve bu DOGRU bir kisit:
            // bir etkinlik satışı acikken "tamamlandı" olamaz --
            // gecmis bir etkinlige bilet satilmaya devam ediyor
            // olurdu.
            //
            // Durum makinesi burada beni HATADAN KORUDU. Mimari
            // testlerin ve derleyicinin yaptigi seyin aynisi:
            // varsayimimi sessizce kabul etmek yerine reddetti.
            //
            // Cozum ara durumu ATLAMAK değil, GECMEK.
            if (evt.Status == EventStatus.SalesOpen)
            {
                evt.CloseSales();
            }

            evt.Complete();
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Durum değişti: etkinlik detayı ve popüler listesi bayatladi.
        await _cache.RemoveByPrefixAsync(CacheKeys.EventPrefix, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(tamamlanacaklar.Count);
    }
}
