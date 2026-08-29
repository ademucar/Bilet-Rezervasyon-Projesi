using MediatR;
using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Abstractions.Persistence;
using Ticketing.Application.Abstractions.Time;
using Ticketing.Application.Common.Results;
using Ticketing.Domain.Entities;
using Ticketing.Domain.Enums;

namespace Ticketing.Application.Features.Notifications;

/// <summary>
/// Süresi dolmak uzere olan rezervasyonlar için uyarı bildirimi yazar.
/// PDF Sprint 14: "Rezervasyon süresi dolmak uzereyken".
/// </summary>
/// <param name="WarnBeforeMinutes">
/// Süre dolmasina kac dakika kala uyarilsin.
///
/// 3 dakika sectim. Gerekce:
///   - Kilit süresi toplam 10 dakika
///   - 5 dakika kala uyarmak çok erken: kullanıcı zaten ödeme
///     ekraninda ve sayaci görüyor olabilir
///   - 1 dakika kala uyarmak çok geç: ödemeyi tamamlamaya vakit
///     kalmaz, uyarı yalnızca kaybi bildirmis olur
///
/// 3 dakika, kullanıcının başka sekmedeyse geri donup ödemeyi
/// bitirebilecegi bir süre.
/// </param>
public sealed record NotifyExpiringReservationsCommand(
    int WarnBeforeMinutes = 3,
    int BatchSize = 200) : IRequest<Result<int>>;

internal sealed class NotifyExpiringReservationsCommandHandler
    : IRequestHandler<NotifyExpiringReservationsCommand, Result<int>>
{
    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _clock;

    public NotifyExpiringReservationsCommandHandler(
        IApplicationDbContext context,
        IDateTimeProvider clock)
    {
        _context = context;
        _clock = clock;
    }

    public async Task<Result<int>> Handle(
        NotifyExpiringReservationsCommand request,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var esik = now.AddMinutes(request.WarnBeforeMinutes);

        // HANGI REZERVASYONLAR?
        //
        // Kosullar:
        //   ExpiresAt > now      -> HENUZ dolmamis (dolmussa uyarinin
        //                           anlami yok, zaten "doldu"
        //                           bildirimi gidiyor)
        //   ExpiresAt <= esik    -> 3 dakikadan az kalmis
        //   Status Locked/PaymentPending -> ödeme bekliyor
        //
        // Onaylanmis veya iptal edilmiş rezervasyonlar disarida.
        var yaklasanlar = await _context.Reservations
            .AsNoTracking()
            .Where(r => r.ExpiresAt > now
                     && r.ExpiresAt <= esik
                     && (r.Status == ReservationStatus.Locked
                      || r.Status == ReservationStatus.PaymentPending))
            .Select(r => new
            {
                r.Id,
                r.UserId,
                r.ReservationCode,
                r.ExpiresAt,
                EventTitle = r.EventSession.Event.Title,
            })
            .Take(request.BatchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (yaklasanlar.Count == 0)
        {
            return Result.Success(0);
        }

        // IDEMPOTENCY: AYNI REZERVASYON ICIN IKINCI UYARI YOK
        //
        // Bu is DAKIKADA BIR çalışıyor ve uyarı penceresi 3 dakika.
        // Yani aynı rezervasyon UC KEZ secilir.
        //
        // Kontrol olmasaydı kullanıcı ust uste uc uyarı alırdı -- ve
        // uyarinin amaci (dikkat cekmek) tam tersine donerdi: art arda
        // gelen bildirimler rahatsiz edici olur ve kullanıcı bildirimleri
        // kapatır.
        //
        // Zaten uyarilmis olanlari TEK sorguda cikariyorum; rezervasyon
        // başına sorgu atsaydim 200 rezervasyon için 200 gidis donus
        // olurdu.
        var ids = yaklasanlar.ConvertAll(r => r.Id);

        var uyarilmisOlanlar = await _context.Notifications
            .AsNoTracking()
            .Where(n => n.Type == NotificationType.ReservationExpiring
                     && n.RelatedEntityId != null
                     && ids.Contains(n.RelatedEntityId.Value))
            .Select(n => n.RelatedEntityId!.Value)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var uyarilacaklar = yaklasanlar
            .Where(r => !uyarilmisOlanlar.Contains(r.Id))
            .ToList();

        if (uyarilacaklar.Count == 0)
        {
            return Result.Success(0);
        }

        foreach (var r in uyarilacaklar)
        {
            // Kalan süreyi DAKIKA olarak hesapliyorum.
            //
            // Yukari yuvarliyorum (Ceiling): 2.1 dakika kalmisken
            // "2 dakika" demek, kullanıcının sandigi kadar vakti
            // olmamasi demek olurdu. "3 dakika" demek daha güvenli
            // -- ama asla oldugundan FAZLA gostermiyor.
            var kalanDakika = Math.Max(1, (int)Math.Ceiling((r.ExpiresAt - now).TotalMinutes));

            _context.Notifications.Add(Notification.Create(
                r.UserId,
                NotificationType.ReservationExpiring,
                "Rezervasyon süreniz doluyor",
                $"{r.EventTitle} için olusturdugunuz {r.ReservationCode} numarali " +
                $"rezervasyonun ödeme suresine {kalanDakika} dakika kaldı. " +
                "Ödemeyi tamamlamazsanız koltuklarınız serbest birakilacak.",
                r.Id,
                "/rezervasyonlarim"));
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(uyarilacaklar.Count);
    }
}
