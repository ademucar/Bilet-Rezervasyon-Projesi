using MediatR;
using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Abstractions.Persistence;
using Ticketing.Application.Abstractions.Time;
using Ticketing.Application.Common.Results;
using Ticketing.Domain.Entities;
using Ticketing.Domain.Enums;

namespace Ticketing.Application.Features.Notifications;

/// <summary>
/// Suresi dolmak uzere olan rezervasyonlar icin uyari bildirimi yazar.
/// PDF Sprint 14: "Rezervasyon suresi dolmak uzereyken".
/// </summary>
/// <param name="WarnBeforeMinutes">
/// Sure dolmasina kac dakika kala uyarilsin.
///
/// 3 dakika sectim. Gerekce:
///   - Kilit suresi toplam 10 dakika
///   - 5 dakika kala uyarmak cok erken: kullanici zaten odeme
///     ekraninda ve sayaci goruyor olabilir
///   - 1 dakika kala uyarmak cok gec: odemeyi tamamlamaya vakit
///     kalmaz, uyari yalnizca kaybi bildirmis olur
///
/// 3 dakika, kullanicinin baska sekmedeyse geri donup odemeyi
/// bitirebilecegi bir sure.
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

        // ==============================================================
        // HANGI REZERVASYONLAR?
        // ==============================================================
        // Kosullar:
        //   ExpiresAt > now      -> HENUZ dolmamis (dolmussa uyarinin
        //                           anlami yok, zaten "doldu"
        //                           bildirimi gidiyor)
        //   ExpiresAt <= esik    -> 3 dakikadan az kalmis
        //   Status Locked/PaymentPending -> odeme bekliyor
        //
        // Onaylanmis veya iptal edilmis rezervasyonlar disarida.
        // ==============================================================
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
                EventTitle = r.EventSession.Event.Title
            })
            .Take(request.BatchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (yaklasanlar.Count == 0)
        {
            return Result.Success(0);
        }

        // ==============================================================
        // IDEMPOTENCY: AYNI REZERVASYON ICIN IKINCI UYARI YOK
        // ==============================================================
        // Bu is DAKIKADA BIR calisiyor ve uyari penceresi 3 dakika.
        // Yani ayni rezervasyon UC KEZ secilir.
        //
        // Kontrol olmasaydi kullanici ust uste uc uyari alirdi -- ve
        // uyarinin amaci (dikkat cekmek) tam tersine donerdi: art arda
        // gelen bildirimler rahatsiz edici olur ve kullanici bildirimleri
        // kapatir.
        //
        // Zaten uyarilmis olanlari TEK sorguda cikariyorum; rezervasyon
        // basina sorgu atsaydik 200 rezervasyon icin 200 gidis donus
        // olurdu.
        // ==============================================================
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
            // Kalan sureyi DAKIKA olarak hesapliyorum.
            //
            // Yukari yuvarliyorum (Ceiling): 2.1 dakika kalmisken
            // "2 dakika" demek, kullanicinin sandigi kadar vakti
            // olmamasi demek olurdu. "3 dakika" demek daha guvenli
            // -- ama asla oldugundan FAZLA gostermiyor.
            var kalanDakika = Math.Max(1, (int)Math.Ceiling((r.ExpiresAt - now).TotalMinutes));

            _context.Notifications.Add(Notification.Create(
                r.UserId,
                NotificationType.ReservationExpiring,
                "Rezervasyon sureniz doluyor",
                $"{r.EventTitle} icin olusturdugunuz {r.ReservationCode} numarali " +
                $"rezervasyonun odeme suresine {kalanDakika} dakika kaldi. " +
                "Odemeyi tamamlamazsaniz koltuklariniz serbest birakilacak.",
                r.Id,
                "/rezervasyonlarim"));
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(uyarilacaklar.Count);
    }
}
