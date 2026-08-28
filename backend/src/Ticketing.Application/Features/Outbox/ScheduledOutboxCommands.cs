using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Abstractions.Persistence;
using Ticketing.Application.Abstractions.Time;
using Ticketing.Application.Common.Results;
using Ticketing.Domain.Entities;
using Ticketing.Domain.Enums;

namespace Ticketing.Application.Features.Outbox;

// ===================================================================
// YAKLASAN ETKINLIK HATIRLATMASI
// PDF Sprint 9 Background Job: "Yaklasan etkinlik hatirlatmasi"
// ===================================================================

/// <param name="WithinHours">
/// Kac saat icinde baslayan oturumlar icin hatirlatma gonderilecek.
///
/// 24 saat sectim: kullanicinin hala plan yapabilecegi (izin almak,
/// yol ayarlamak) ama etkinligi unutmus olabilecegi araliktir.
/// 1 saat cok gec, 1 hafta cok erken olurdu.
/// </param>
public sealed record SendEventRemindersCommand(int WithinHours = 24)
    : IRequest<Result<int>>;

internal sealed class SendEventRemindersCommandHandler
    : IRequestHandler<SendEventRemindersCommand, Result<int>>
{
    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _clock;

    public SendEventRemindersCommandHandler(IApplicationDbContext context, IDateTimeProvider clock)
    {
        _context = context;
        _clock = clock;
    }

    public async Task<Result<int>> Handle(
        SendEventRemindersCommand request,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var upperBound = now.AddHours(request.WithinHours);

        // ==============================================================
        // GECMIS OTURUMLARI DISLA (StartDate > now)
        // ==============================================================
        // Yalnizca "StartDate <= upperBound" yazsaydik, GECMISTEKI tum
        // oturumlar da kosula uyardi ve sistem bir yil onceki
        // etkinlikler icin hatirlatma gondermeye calisirdi.
        //
        // Bu, kolay atlanan ama sonucu utanc verici bir hata: musteri
        // "gecen yilki konseriniz yarin basliyor" e-postasi alir.
        // ==============================================================
        var sessions = await _context.EventSessions
            .AsNoTracking()
            .Where(s => s.StartDate > now
                     && s.StartDate <= upperBound
                     && s.Status == EventSessionStatus.Scheduled)
            .Select(s => new
            {
                s.Id,
                s.StartDate,
                EventTitle = s.Event.Title,
                VenueName = s.Event.Venue.Name,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (sessions.Count == 0)
        {
            return Result.Success(0);
        }

        var sessionIds = sessions.ConvertAll(s => s.Id);

        // ==============================================================
        // BILET SAHIPLERI TEK SORGUDA -- OTURUM BASINA DEGIL
        // ==============================================================
        // Ilk yazimda bunu oturum projeksiyonunun icine gomecektim.
        // Ayirdim cunku ic ice koleksiyon projeksiyonu EF'te ya
        // cevrilemez ya da her oturum icin ayri sorgu uretir (N+1):
        // 50 oturum = 51 gidis donus.
        //
        // Distinct SUNUCUDA calisiyor: bir kullanicinin ayni oturuma
        // 4 bileti varsa 4 degil 1 satir doner ve 1 hatirlatma alir.
        // ==============================================================
        var ticketHolders = await _context.Tickets
            .AsNoTracking()
            .Where(t => sessionIds.Contains(t.EventSessionId) && t.Status == TicketStatus.Active)
            .Select(t => new { t.EventSessionId, t.UserId })
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var holdersBySession = ticketHolders
            .GroupBy(t => t.EventSessionId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.UserId).ToList());

        var created = 0;

        foreach (var session in sessions)
        {
            if (!holdersBySession.TryGetValue(session.Id, out var userIds))
            {
                continue;
            }

            foreach (var userId in userIds)
            {
                // ==================================================
                // JOB DOGRUDAN BILDIRIM YAZMIYOR, OUTBOX'A YAZIYOR
                // ==================================================
                // Neden dolayli yol? Cunku PDF'in kurali su:
                // "Job islemleri kullanici istegini gereksiz yere
                // bekletmemelidir" ve "Basarisiz islem yeniden
                // denenmelidir".
                //
                // Bildirimi burada yazsaydik ve e-posta gonderimi
                // basarisiz olsaydi, yeniden deneme mekanizmasi
                // olmazdi -- job bir sonraki gun calisana kadar
                // hicbir sey olmazdi ve o zaman da etkinlik gecmis
                // olurdu.
                //
                // Outbox'a yazinca, isleyici basarisiz olursa ustel
                // geri cekilme ile dakikalar icinde tekrar denenir.
                // ==================================================
                _context.OutboxMessages.Add(OutboxMessage.Create(
                    OutboxMessageTypes.EventReminder,
                    JsonSerializer.Serialize(new EventReminderPayload(
                        session.Id,
                        userId,
                        session.EventTitle,
                        session.VenueName,
                        session.StartDate))));

                created++;
            }
        }

        if (created > 0)
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return Result.Success(created);
    }
}

// ===================================================================
// GUNLUK SATIS OZETI -- PDF: "Gunluk satis ozeti olusturma"
// ===================================================================

/// <param name="Date">
/// Raporlanacak gun. null ise DUNU raporlar.
///
/// Neden dun, bugun degil? Cunku job gece yarisindan sonra calisiyor
/// ve "bugun" henuz baslamis, verisi bos olurdu. Ayrica tamamlanmis
/// bir gunun rakamlari bir daha degismez -- rapor sabittir.
/// </param>
public sealed record GenerateDailySalesSummaryCommand(DateOnly? Date = null)
    : IRequest<Result<DailySalesSummaryPayload>>;

internal sealed class GenerateDailySalesSummaryCommandHandler
    : IRequestHandler<GenerateDailySalesSummaryCommand, Result<DailySalesSummaryPayload>>
{
    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _clock;

    public GenerateDailySalesSummaryCommandHandler(
        IApplicationDbContext context,
        IDateTimeProvider clock)
    {
        _context = context;
        _clock = clock;
    }

    public async Task<Result<DailySalesSummaryPayload>> Handle(
        GenerateDailySalesSummaryCommand request,
        CancellationToken cancellationToken)
    {
        var date = request.Date ?? DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime.AddDays(-1));

        // Gunun sinirlari UTC olarak. Veritabaninda her sey UTC
        // sakladigimiz icin karsilastirma tutarli.
        var start = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var end = start.AddDays(1);

        // ==============================================================
        // TEK SORGU, IKI TOPLAM -- GroupBy ile
        // ==============================================================
        // Brut ve iade tutarini ayri sorgularla da alabilirdim ama o
        // zaman iki tur veritabani gidis donusu olurdu. Daha onemlisi:
        // iki sorgu arasinda yeni bir odeme gelirse rakamlar birbiriyle
        // TUTARSIZ olurdu.
        var payments = await _context.Payments
            .AsNoTracking()
            .Where(p => p.CompletedAt >= start
                     && p.CompletedAt < end
                     && (p.Status == PaymentStatus.Successful || p.Status == PaymentStatus.Refunded))
            .GroupBy(p => p.Amount.Currency)
            .Select(g => new
            {
                Currency = g.Key,
                Gross = g.Sum(p => p.Amount.Amount),
                Refunded = g.Sum(p => p.RefundedAmount.Amount),
                Count = g.Count(),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Coklu para birimi bu ozette DESTEKLENMIYOR: en cok islem
        // yapilan para birimini raporluyorum.
        //
        // Dogru cozum para birimi basina ayri satir olurdu ama PDF
        // Sprint 13'te gercek raporlama ekrani gelecek; burada
        // gunluk bir ozet bildirimi yeterli. Bunu sessizce
        // toplamiyorum -- farkli para birimlerini toplamak
        // (100 TRY + 50 USD = 150) acik bir hata olurdu.
        var main = payments.OrderByDescending(p => p.Count).FirstOrDefault();

        var ticketCount = await _context.Tickets
            .AsNoTracking()
            .CountAsync(t => t.CreatedAt >= start && t.CreatedAt < end, cancellationToken)
            .ConfigureAwait(false);

        var reservationCount = await _context.Reservations
            .AsNoTracking()
            .CountAsync(r => r.CreatedAt >= start && r.CreatedAt < end, cancellationToken)
            .ConfigureAwait(false);

        var expiredCount = await _context.Reservations
            .AsNoTracking()
            .CountAsync(
                r => r.CreatedAt >= start
                  && r.CreatedAt < end
                  && r.Status == ReservationStatus.Expired,
                cancellationToken)
            .ConfigureAwait(false);

        var summary = new DailySalesSummaryPayload(
            date,
            ticketCount,
            main?.Gross ?? 0m,
            main?.Refunded ?? 0m,
            main?.Currency ?? "TRY",
            reservationCount,
            expiredCount);

        // Raporu Outbox'a yaziyorum: hesaplama ile dagitim ayri.
        _context.OutboxMessages.Add(OutboxMessage.Create(
            OutboxMessageTypes.DailySalesSummary,
            JsonSerializer.Serialize(summary)));

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(summary);
    }
}
