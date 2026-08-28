using MediatR;
using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Abstractions.Persistence;
using Ticketing.Application.Abstractions.Security;
using Ticketing.Application.Abstractions.Time;
using Ticketing.Application.Common.Results;
using Ticketing.Domain.Entities;
using Ticketing.Domain.Enums;

namespace Ticketing.Application.Features.Reports;

// ===================================================================
// ORTAK TIPLER
// ===================================================================

/// <summary>Gunluk satis grafigi noktasi.</summary>
public sealed record DailySalesPoint(DateOnly Date, int TicketCount, decimal Revenue);

/// <summary>Ad + deger ciftleri (en populer sehirler, kategoriler...).</summary>
public sealed record NamedCount(string Name, int Count);

public sealed record EventRevenue(Guid EventId, string Title, int TicketCount, decimal Revenue);

public sealed record SectionOccupancy(
    string SectionName,
    int TotalSeats,
    int SoldSeats,
    double OccupancyRate);

// ===================================================================
// ORGANIZATOR DASHBOARD -- PDF Sprint 13 (10 metrik)
// ===================================================================

public sealed record OrganizerDashboard(
    int TotalEvents,
    int PublishedEvents,
    int TotalTicketsSold,
    decimal TotalRevenue,
    int RefundedTickets,
    double OccupancyRate,
    string? TopTicketTypeName,
    int TopTicketTypeCount,
    IReadOnlyList<DailySalesPoint> DailySales,
    IReadOnlyList<EventRevenue> RevenueByEvent,
    IReadOnlyList<SectionOccupancy> SectionOccupancies,
    string Currency);

/// <param name="Days">Gunluk grafik kac gunu kapsasin. Varsayilan 30.</param>
public sealed record GetOrganizerDashboardQuery(int Days = 30)
    : IRequest<Result<OrganizerDashboard>>;

internal sealed class GetOrganizerDashboardQueryHandler
    : IRequestHandler<GetOrganizerDashboardQuery, Result<OrganizerDashboard>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public GetOrganizerDashboardQueryHandler(
        IApplicationDbContext context,
        ICurrentUser currentUser,
        IDateTimeProvider clock)
    {
        _context = context;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result<OrganizerDashboard>> Handle(
        GetOrganizerDashboardQuery request,
        CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not Guid userId)
        {
            return Result.Failure<OrganizerDashboard>(
                Error.Unauthorized("auth.required", "Giris yapmalisiniz."));
        }

        // ==============================================================
        // BU PANELIN EN KRITIK SATIRI: KAPSAM SINIRI
        // ==============================================================
        // Organizator YALNIZCA kendi etkinliklerinin verisini gorebilir.
        //
        // Bu filtreyi unutsaydik, herhangi bir organizator RAKIPLERININ
        // gelir rakamlarini, bilet satislarini ve doluluk oranlarini
        // gorurdu. Ticari acidan felaket bir sizinti olurdu -- ve
        // arayuzde hicbir hata gorunmezdi, sadece "cok fazla veri".
        //
        // Organizator profili yoksa panel bos degil, HATA doner:
        // "verisi olmayan bir panel" ile "yetkisiz erisim" farkli
        // seyler ve kullaniciya dogrusunu soylemek gerekiyor.
        // ==============================================================
        var organizerId = await _context.OrganizerProfiles
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (organizerId is null)
        {
            return Result.Failure<OrganizerDashboard>(Error.Forbidden(
                "report.not_organizer",
                "Bu panel yalnizca organizatorlere aciktir."));
        }

        var events = _context.Events.AsNoTracking().Where(e => e.OrganizerId == organizerId);

        // Bu organizatorun etkinliklerine ait TUM biletler.
        // Asagidaki metriklerin cogu bu kumeden turetiliyor.
        var tickets = _context.Tickets
            .AsNoTracking()
            .Where(t => t.EventSeat.EventSession.Event.OrganizerId == organizerId);

        // ---- 1 ve 2: etkinlik sayilari ----
        var totalEvents = await events.CountAsync(cancellationToken).ConfigureAwait(false);

        var publishedEvents = await events
            .CountAsync(
                e => e.Status == EventStatus.Published
                  || e.Status == EventStatus.SalesOpen
                  || e.Status == EventStatus.SalesClosed,
                cancellationToken)
            .ConfigureAwait(false);

        // ---- 3, 4 ve 5: satis, gelir, iade ----
        //
        // GELIRI BILETLERDEN hesapliyorum, odemelerden degil.
        //
        // Sebep: bir odeme birden fazla bileti kapsayabilir ve
        // organizator bazinda ayristirmak icin yine biletlere inmek
        // gerekir. Bilet basina fiyat zaten kayitli.
        var soldTickets = tickets.Where(t => t.Status == TicketStatus.Active
                                          || t.Status == TicketStatus.Used);

        var totalTicketsSold = await soldTickets.CountAsync(cancellationToken).ConfigureAwait(false);

        // SumAsync bos kumede 0 doner (SQL SUM null doner ama EF
        // decimal icin 0'a cevirir). Yine de ?? 0 yazmiyorum cunku
        // decimal (nullable degil) donuyor.
        var totalRevenue = await soldTickets
            .SumAsync(t => t.Price.Amount, cancellationToken)
            .ConfigureAwait(false);

        var refundedTickets = await tickets
            .CountAsync(t => t.Status == TicketStatus.Refunded, cancellationToken)
            .ConfigureAwait(false);

        // ---- 6: doluluk orani ----
        //
        // Tanim: satilan koltuk / uretilmis toplam koltuk.
        //
        // Payda olarak SALON KAPASITESINI degil URETILMIS KOLTUK
        // sayisini aliyorum. Fark onemli: organizator salonun bir
        // bolumunu satisa hic acmamis olabilir. Kapasiteyi payda
        // yapsaydik doluluk haksiz yere dusuk gorunurdu.
        var totalSeats = await _context.EventSeats
            .AsNoTracking()
            .CountAsync(
                es => es.EventSession.Event.OrganizerId == organizerId,
                cancellationToken)
            .ConfigureAwait(false);

        var soldSeats = await _context.EventSeats
            .AsNoTracking()
            .CountAsync(
                es => es.EventSession.Event.OrganizerId == organizerId
                   && es.Status == EventSeatStatus.Sold,
                cancellationToken)
            .ConfigureAwait(false);

        // Sifira bolme korumasi: henuz koltuk uretilmemisse 0.
        var occupancyRate = totalSeats == 0
            ? 0
            : Math.Round((double)soldSeats / totalSeats * 100, 1);

        // ---- 7: en cok satan bilet turu ----
        // NOT: GroupBy sonucunu ANONIM tipe projelendiriyoruz.
        //
        // Dogrudan "new NamedCount(...)" yazsaydik EF Core bunu SQL'e
        // ceviremezdi (bkz. RevenueByEvent'teki ayrintili aciklama).
        // Bu dosyadaki dort gruplamada da ayni desen uygulaniyor.
        var topTicketType = await soldTickets
            .GroupBy(t => t.EventSeat.TicketType.Name)
            .Select(g => new { Name = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        // ---- 8: gunluk satis grafigi ----
        var since = _clock.UtcNow.AddDays(-request.Days);

        var dailyRaw = await soldTickets
            .Where(t => t.CreatedAt >= since)

            // Gune gore gruplamak icin DateOnly'ye indirgiyorum.
            // Npgsql bunu SQL'de DATE(...) olarak cevirebiliyor.
            .GroupBy(t => DateOnly.FromDateTime(t.CreatedAt.UtcDateTime))
            .Select(g => new
            {
                Date = g.Key,
                Count = g.Count(),
                Revenue = g.Sum(t => t.Price.Amount),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // ==============================================================
        // BOS GUNLERI DOLDUR
        // ==============================================================
        // Veritabani yalnizca satis OLAN gunleri donduruyor. Grafige
        // oldugu gibi verseydik, satis olmayan gunler ATLANIRDI ve
        // cizgi grafik yaniltici olurdu: 1 Ocak ile 15 Ocak yan yana
        // cizilir, aradaki 13 gunluk durgunluk gorunmezdi.
        //
        // Sifir degerli gunleri ekleyerek zaman eksenini gercek
        // kiliyoruz.
        // ==============================================================
        var bugun = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);

        var dailySales = Enumerable.Range(0, request.Days)
            .Select(offset => bugun.AddDays(-(request.Days - 1 - offset)))
            .Select(gun =>
            {
                var kayit = dailyRaw.Find(d => d.Date == gun);

                return kayit is null
                    ? new DailySalesPoint(gun, 0, 0)
                    : new DailySalesPoint(kayit.Date, kayit.Count, kayit.Revenue);
            })
            .ToList();

        // ---- 9: etkinlik bazli gelir ----
        var revenueRaw = await soldTickets
            .GroupBy(t => new
            {
                t.EventSeat.EventSession.Event.Id,
                t.EventSeat.EventSession.Event.Title,
            })

            // ==========================================================
            // ANONIM TIPE PROJEKSIYON, RECORD'A BELLEKTE CEVIRIM
            // ==========================================================
            // Once dogrudan "new EventRevenue(...)" yaziyordum ve uc
            // 500 dondu:
            //
            //   InvalidOperationException: The LINQ expression ...
            //   could not be translated
            //
            // EF Core, GroupBy sonucunu bir RECORD KURUCUSUNA
            // projelendiremiyor (anonim tipe ise sorunsuz cevirebiliyor).
            //
            // Cozum: SQL'e cevrilebilen anonim tiple gruplayip,
            // record'a bellekte gecmek. Gruplama sonucu zaten kucuk
            // (etkinlik sayisi kadar satir), yani bellekte islemenin
            // maliyeti yok.
            //
            // ONEMLI: bu, "veriyi bellege cekip C#'ta grupla" DEGIL.
            // Gruplama ve toplama HALA SQL'de yapiliyor; yalnizca
            // sonucun tipe donusumu bellekte.
            // ==========================================================
            .Select(g => new
            {
                g.Key.Id,
                g.Key.Title,
                Count = g.Count(),
                Revenue = g.Sum(t => t.Price.Amount),
            })
            .OrderByDescending(x => x.Revenue)
            .Take(10)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var revenueByEvent = revenueRaw.ConvertAll(
            r => new EventRevenue(r.Id, r.Title, r.Count, r.Revenue));

        // ---- 10: bolum bazli doluluk ----
        var sectionOccupancies = await _context.EventSeats
            .AsNoTracking()
            .Where(es => es.EventSession.Event.OrganizerId == organizerId)
            .GroupBy(es => es.Seat.SeatSection.Name)
            .Select(g => new
            {
                SectionName = g.Key,
                Total = g.Count(),
                Sold = g.Count(x => x.Status == EventSeatStatus.Sold),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Orani BELLEKTE hesapliyorum.
        //
        // SQL'de yapsaydik tam sayi bolmesi tuzagina duserdik:
        // PostgreSQL'de 3/4 = 0 (integer division). Cast eklemek
        // mumkun ama okunakli degil ve satir sayisi zaten az.
        var sections = sectionOccupancies
            .Select(s => new SectionOccupancy(
                s.SectionName,
                s.Total,
                s.Sold,
                s.Total == 0 ? 0 : Math.Round((double)s.Sold / s.Total * 100, 1)))
            .OrderByDescending(s => s.OccupancyRate)
            .ToList();

        // Para birimi: ilk satilan biletten.
        //
        // Coklu para birimi bu panelde DESTEKLENMIYOR ve bunu
        // sessizce toplamak yerine acikca yaziyorum. Sprint 11'de
        // gunluk satis ozetinde de ayni karari vermistim.
        var currency = await soldTickets
            .Select(t => t.Price.Currency)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false) ?? "TRY";

        return Result.Success(new OrganizerDashboard(
            totalEvents,
            publishedEvents,
            totalTicketsSold,
            totalRevenue,
            refundedTickets,
            occupancyRate,
            topTicketType?.Name,
            topTicketType?.Count ?? 0,
            dailySales,
            revenueByEvent,
            sections,
            currency));
    }
}

// ===================================================================
// ADMIN DASHBOARD -- PDF Sprint 13 (10 metrik)
// ===================================================================

public sealed record AdminDashboard(
    int TotalUsers,
    int TotalOrganizers,
    int TotalEvents,
    int ActiveSales,
    decimal TotalTransactionVolume,
    int CancelledEvents,
    double FailedPaymentRate,
    IReadOnlyList<NamedCount> TopCities,
    IReadOnlyList<NamedCount> TopCategories,
    int SystemErrorCount,
    string Currency);

public sealed record GetAdminDashboardQuery : IRequest<Result<AdminDashboard>>;

internal sealed class GetAdminDashboardQueryHandler
    : IRequestHandler<GetAdminDashboardQuery, Result<AdminDashboard>>
{
    private readonly IApplicationDbContext _context;

    public GetAdminDashboardQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<Result<AdminDashboard>> Handle(
        GetAdminDashboardQuery request,
        CancellationToken cancellationToken)
    {
        // Yetki kontrolu CONTROLLER'da (AdminOnly policy).
        // Burada tekrar kontrol etmiyorum: tek bir yerde olmasi,
        // iki yerde tutup birini guncellemeyi unutmaktan iyi.

        var totalUsers = await _context.Users
            .AsNoTracking()
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);

        var totalOrganizers = await _context.OrganizerProfiles
            .AsNoTracking()
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);

        var totalEvents = await _context.Events
            .AsNoTracking()
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);

        // "Aktif satislar": su an bilet satilabilen etkinlikler.
        var activeSales = await _context.Events
            .AsNoTracking()
            .CountAsync(e => e.Status == EventStatus.SalesOpen, cancellationToken)
            .ConfigureAwait(false);

        var cancelledEvents = await _context.Events
            .AsNoTracking()
            .CountAsync(e => e.Status == EventStatus.Cancelled, cancellationToken)
            .ConfigureAwait(false);

        // ---- Toplam islem hacmi ----
        //
        // Basarili odemelerin toplami. IADELERI DUSMUYORUM.
        //
        // Sebep: "islem hacmi" (transaction volume) finansal bir
        // terim ve sistemden GECEN paranin toplamini anlatir. Net
        // gelir farkli bir metriktir ve karistirilmamali.
        //
        // Iade bilgisi ayrica raporlarda mevcut.
        var successfulPayments = _context.Payments
            .AsNoTracking()
            .Where(p => p.Status == PaymentStatus.Successful
                     || p.Status == PaymentStatus.Refunded);

        var totalVolume = await successfulPayments
            .SumAsync(p => p.Amount.Amount, cancellationToken)
            .ConfigureAwait(false);

        // ---- Basarisiz odeme orani ----
        //
        // Payda: SONUCLANMIS odemeler (basarili + basarisiz).
        //
        // Pending ve Processing durumundakileri HARIC tutuyorum:
        // henuz sonuclanmamis bir odeme "basarisiz" sayilamaz.
        // Dahil etseydik oran, o anda islemde olan odeme sayisina
        // gore dalgalanirdi ve hicbir sey ifade etmezdi.
        var finalizedPayments = await _context.Payments
            .AsNoTracking()
            .CountAsync(
                p => p.Status == PaymentStatus.Successful
                  || p.Status == PaymentStatus.Refunded
                  || p.Status == PaymentStatus.Failed,
                cancellationToken)
            .ConfigureAwait(false);

        var failedPayments = await _context.Payments
            .AsNoTracking()
            .CountAsync(p => p.Status == PaymentStatus.Failed, cancellationToken)
            .ConfigureAwait(false);

        var failedRate = finalizedPayments == 0
            ? 0
            : Math.Round((double)failedPayments / finalizedPayments * 100, 1);

        // ---- En populer sehirler ve kategoriler ----
        //
        // "Populer" olcutu: SATILAN BILET sayisi.
        //
        // Etkinlik sayisina gore de siralanabilirdi ama o, talebi
        // degil ARZI olcerdi: 50 etkinligi olup hicbiri satmayan bir
        // sehir "populer" gorunurdu.
        var topCities = await _context.Tickets
            .AsNoTracking()
            .Where(t => t.Status == TicketStatus.Active || t.Status == TicketStatus.Used)
            .GroupBy(t => t.EventSeat.EventSession.Event.City.Name)
            .Select(g => new { Name = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(5)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var topCategories = await _context.Tickets
            .AsNoTracking()
            .Where(t => t.Status == TicketStatus.Active || t.Status == TicketStatus.Used)
            .GroupBy(t => t.EventSeat.EventSession.Event.Category.Name)
            .Select(g => new { Name = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(5)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // ==============================================================
        // "SISTEM HATA SAYISI" -- PDF'in TANIMLAMADIGI METRIK
        // ==============================================================
        // PDF bu metrigi istiyor ama neyin "sistem hatasi" sayilacagini
        // soylemiyor. Tanimi ben veriyorum ve acikca yaziyorum:
        //
        //   DEAD LETTER OLMUS OUTBOX MESAJLARI
        //
        // Neden bu? Cunku dead letter, sistemde GERCEKTEN yanlis giden
        // ve INSAN MUDAHALESI bekleyen tek kalici kayittir. Bes kez
        // denenmis ve hala basarisiz bir mesaj, gonderilmemis bir
        // e-posta veya olusmamis bir bildirim demektir.
        //
        // Elemediklerim ve sebepleri:
        //
        //   HTTP 500 sayisi -> loglarda, veritabaninda degil. Sayabilmek
        //   icin log toplama altyapisi gerekir (PDF Sprint 16).
        //
        //   Basarisiz odemeler -> bunlar SISTEM hatasi degil, IS
        //   sonucudur. Kart limiti yetmemesi bizim hatamiz degil.
        //   Ayrica zaten ayri bir metrik olarak yukarida var.
        //
        //   Eszamanlilik cakismalari (409) -> bunlar sistemin DOGRU
        //   calistiginin kaniti. Hata saymak yaniltici olurdu.
        //
        // Yani bu sayi "operatorun bakmasi gereken is sayisi".
        // Sifirdan buyukse Hangfire panelinde islenecek bir sey var.
        // ==============================================================
        var systemErrors = await _context.OutboxMessages
            .AsNoTracking()
            .CountAsync(m => m.IsDeadLettered, cancellationToken)
            .ConfigureAwait(false);

        var cities = topCities.ConvertAll(x => new NamedCount(x.Name, x.Count));
        var categories = topCategories.ConvertAll(x => new NamedCount(x.Name, x.Count));

        var currency = await successfulPayments
            .Select(p => p.Amount.Currency)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false) ?? "TRY";

        return Result.Success(new AdminDashboard(
            totalUsers,
            totalOrganizers,
            totalEvents,
            activeSales,
            totalVolume,
            cancelledEvents,
            failedRate,
            cities,
            categories,
            systemErrors,
            currency));
    }
}
