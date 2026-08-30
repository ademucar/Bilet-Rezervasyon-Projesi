using MediatR;
using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Abstractions.Persistence;
using Ticketing.Application.Abstractions.Security;
using Ticketing.Application.Common.Results;
using Ticketing.Domain.Entities;
using Ticketing.Domain.Enums;

namespace Ticketing.Application.Features.Reports;

// Ortak: kapsam (scope) belirleme

/// <summary>
/// Raporlarin hangi veriyi kapsayacagini belirler.
/// </summary>
/// <remarks>
/// Bu sinif bu dosyanin güvenlik omurgasi
///
/// Bes raporun HEPSI aynı soruyu sormak zorunda: "bu kullanıcı hangi
/// etkinliklerin verisini görebilir?"
///
///   ADMIN       -> hepsi
///   ORGANİZATÖR -> yalnızca kendi etkinlikleri
///   DIGER       -> hicbiri
///
/// Bu mantığı her raporda tekrar yazsaydim, birinde unutmak veya
/// yanlış yazmak kacinilmazdi -- ve sonucu bir organizatorun
/// RAKIPLERININ gelir rakamlarini gormesi olurdu.
///
/// Tek yerde tutuyorum. Yeni bir rapor eklendiginde bu metodu
/// cagirmamak, derleme hatası vermez ama kod incelemesinde hemen
/// goze carpar: "scope nerede?"
/// </remarks>
internal sealed record ReportScope(bool IsAdmin, Guid? OrganizerId)
{
    /// <summary>Bu kapsamin gorebilecegi etkinlikleri filtreler.</summary>
    public IQueryable<Event> Apply(IQueryable<Event> query)
    {
        ArgumentNullException.ThrowIfNull(query);

        return IsAdmin ? query : query.Where(e => e.OrganizerId == OrganizerId);
    }

    /// <summary>Bu kapsamin gorebilecegi biletleri filtreler.</summary>
    public IQueryable<Ticket> Apply(IQueryable<Ticket> query)
    {
        ArgumentNullException.ThrowIfNull(query);

        return IsAdmin
            ? query
            : query.Where(t => t.EventSeat.EventSession.Event.OrganizerId == OrganizerId);
    }
}

internal static class ReportScopeResolver
{
    public static async Task<Result<ReportScope>> ResolveAsync(
        IApplicationDbContext context,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid userId)
        {
            return Result.Failure<ReportScope>(
                Error.Unauthorized("auth.required", "Giriş yapmalisiniz."));
        }

        if (currentUser.Roles.Contains(Role.Names.Admin))
        {
            return Result.Success(new ReportScope(IsAdmin: true, OrganizerId: null));
        }

        var organizerId = await context.OrganizerProfiles
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (organizerId is null)
        {
            return Result.Failure<ReportScope>(Error.Forbidden(
                "report.forbidden",
                "Raporlar yalnızca organizatör ve yoneticilere aciktir."));
        }

        return Result.Success(new ReportScope(IsAdmin: false, OrganizerId: organizerId));
    }

    /// <summary>
    /// Kapsami DOGRUDAN kullanıcı kimliginden cozer.
    /// </summary>
    /// <remarks>
    /// Arka plan isleri icin -- HTTP baglami olmadan
    ///
    /// Rapor disa aktarimi arka planda çalışıyor ve orada
    /// ICurrentUser boş. Kimlik, talep anında DOGRULANIP Outbox
    /// payload'ina yazildi; burada önü kullanıyorum.
    ///
    /// Yetki kontrolü ZAYIFLAMIYOR: talep sırasında kullanıcının
    /// organizatör ya da admin olduğu zaten dogrulandi. Burada
    /// yalnızca aynı kapsami yeniden kuruyorum.
    ///
    /// Rolu veritabanindan OKUYORUM, payload'a yazmiyorum. Sebep:
    /// kullanıcının rolü talep ile isleme arasında degismis
    /// olabilir (admin yetkisi alinmis olabilir). Güncel rol her
    /// zaman doğru olandir.
    /// </remarks>
    public static async Task<ReportScope> ResolveForUserAsync(
        IApplicationDbContext context,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var isAdmin = await context.UserRoles
            .AsNoTracking()
            .AnyAsync(ur => ur.UserId == userId && ur.RoleId == Role.Ids.Admin, cancellationToken)
            .ConfigureAwait(false);

        if (isAdmin)
        {
            return new ReportScope(IsAdmin: true, OrganizerId: null);
        }

        var organizerId = await context.OrganizerProfiles
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        // Organizatör profili silinmisse: KAPSAMI BOŞ birak.
        //
        // OrganizerId null olunca Apply(...) hiçbir kayitla
        // eşleşmiyor ve rapor BOŞ cikiyor. Istisna firlatmak yerine
        // bunu tercih ettim: is, dead letter'a dusup operatoru
        // mesgul etmek yerine boş bir rapor uretiyor.
        return new ReportScope(IsAdmin: false, OrganizerId: organizerId);
    }
}

/// <summary>Raporlarin ortak tarih aralığı parametreleri.</summary>
public abstract record ReportRangeRequest
{
    public DateTimeOffset? From { get; init; }

    public DateTimeOffset? To { get; init; }
}

// 1) SATIS OZETI -- GET /api/v1/reports/sales-summary

public sealed record SalesSummaryReport(
    int TicketCount,
    decimal GrossRevenue,
    decimal RefundedAmount,
    decimal NetRevenue,
    int RefundedTicketCount,
    int ReservationCount,
    int ExpiredReservationCount,
    string Currency);

public sealed record GetSalesSummaryReportQuery : ReportRangeRequest,
    IRequest<Result<SalesSummaryReport>>;

internal sealed class GetSalesSummaryReportQueryHandler
    : IRequestHandler<GetSalesSummaryReportQuery, Result<SalesSummaryReport>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;

    public GetSalesSummaryReportQueryHandler(
        IApplicationDbContext context,
        ICurrentUser currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<Result<SalesSummaryReport>> Handle(
        GetSalesSummaryReportQuery request,
        CancellationToken cancellationToken)
    {
        var scopeResult = await ReportScopeResolver
            .ResolveAsync(_context, _currentUser, cancellationToken)
            .ConfigureAwait(false);

        if (!scopeResult.IsSuccess)
        {
            return Result.Failure<SalesSummaryReport>(scopeResult.Error);
        }

        var rapor = await RunAsync(
            _context, scopeResult.Value, request.From, request.To, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(rapor);
    }

    /// <summary>
    /// Sorgunun KENDISI. Kapsami disaridan aliyor.
    /// </summary>
    /// <remarks>
    /// NEDEN AYRI BIR static METOT?
    ///
    /// Bu sorgu IKI FARKLI YERDEN çalışıyor:
    ///
    ///   1) HTTP ucu       -> kapsam ICurrentUser'dan cozuluyor
    ///   2) Arka plan isi  -> kapsam Outbox payload'indaki userId'den
    ///
    /// Arka planda HTTP baglami YOK, yani ICurrentUser boş döner.
    /// Handler'i doğrudan cagirsaydim rapor "yetkisiz" hatası verirdi
    /// ya da (daha kotusu) kapsam boş kalip TÜM VERIYI dondururdu.
    ///
    /// Sorguyu kapsamdan ayirinca ikisi de aynı kodu kullaniyor ve
    /// yetki kurallari HER IKI YOLDA da AYNEN uygulaniyor. Arka planda
    /// "her seyi gor" gibi bir ayricalik YOK.
    /// </remarks>
    internal static async Task<SalesSummaryReport> RunAsync(
        IApplicationDbContext _context,
        ReportScope scope,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        var request = new { From = from, To = to };

        var tickets = scope.Apply(_context.Tickets.AsNoTracking());

        // Tarih aralığı ISTEGE BAGLI. Verilmezse tüm zamanlar.
        if (request.From.HasValue)
        {
            tickets = tickets.Where(t => t.CreatedAt >= request.From.Value);
        }

        if (request.To.HasValue)
        {
            tickets = tickets.Where(t => t.CreatedAt <= request.To.Value);
        }

        var sold = tickets.Where(t => t.Status == TicketStatus.Active
                                   || t.Status == TicketStatus.Used);

        var ticketCount = await sold.CountAsync(cancellationToken).ConfigureAwait(false);

        var gross = await sold.SumAsync(t => t.Price.Amount, cancellationToken)
            .ConfigureAwait(false);

        var refundedTickets = tickets.Where(t => t.Status == TicketStatus.Refunded);

        var refundedCount = await refundedTickets.CountAsync(cancellationToken)
            .ConfigureAwait(false);

        var refundedAmount = await refundedTickets
            .SumAsync(t => t.Price.Amount, cancellationToken)
            .ConfigureAwait(false);

        // Rezervasyon sayilari: kapsam etkinlik üzerinden uygulaniyor.
        var reservations = _context.Reservations.AsNoTracking();

        if (!scope.IsAdmin)
        {
            reservations = reservations.Where(
                r => r.EventSession.Event.OrganizerId == scope.OrganizerId);
        }

        if (request.From.HasValue)
        {
            reservations = reservations.Where(r => r.CreatedAt >= request.From.Value);
        }

        if (request.To.HasValue)
        {
            reservations = reservations.Where(r => r.CreatedAt <= request.To.Value);
        }

        var reservationCount = await reservations.CountAsync(cancellationToken)
            .ConfigureAwait(false);

        var expiredCount = await reservations
            .CountAsync(r => r.Status == ReservationStatus.Expired, cancellationToken)
            .ConfigureAwait(false);

        var currency = await sold
            .Select(t => t.Price.Currency)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false) ?? "TRY";

        return new SalesSummaryReport(
            ticketCount,
            gross,
            refundedAmount,

            // NET gelir: brüt eksi iade.
            //
            // Admin panelindeki "işlem hacmi" ile KARISTIRILMAMALI --
            // orada iade dusulmuyor çünkü o metrik sistemden gecen
            // parayi olcuyor. Burada organizatorun eline gecen parayi
            // olcuyorum; iade dusulmek ZORUNDA.
            gross - refundedAmount,
            refundedCount,
            reservationCount,
            expiredCount,
            currency);
    }
}

// 2) ETKİNLİK DOLULUGU -- GET /api/v1/reports/event-occupancy

public sealed record EventOccupancyRow(
    Guid EventId,
    string Title,
    DateTimeOffset EventDate,
    int TotalSeats,
    int SoldSeats,
    int LockedSeats,
    int AvailableSeats,
    double OccupancyRate);

public sealed record GetEventOccupancyReportQuery : IRequest<Result<IReadOnlyList<EventOccupancyRow>>>;

internal sealed class GetEventOccupancyReportQueryHandler
    : IRequestHandler<GetEventOccupancyReportQuery, Result<IReadOnlyList<EventOccupancyRow>>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;

    public GetEventOccupancyReportQueryHandler(
        IApplicationDbContext context,
        ICurrentUser currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<EventOccupancyRow>>> Handle(
        GetEventOccupancyReportQuery request,
        CancellationToken cancellationToken)
    {
        var scopeResult = await ReportScopeResolver
            .ResolveAsync(_context, _currentUser, cancellationToken)
            .ConfigureAwait(false);

        if (!scopeResult.IsSuccess)
        {
            return Result.Failure<IReadOnlyList<EventOccupancyRow>>(scopeResult.Error);
        }

        var rapor = await RunAsync(_context, scopeResult.Value, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(rapor);
    }

    /// <summary>Sorgunun kendisi. Bkz. SalesSummary açıklaması.</summary>
    internal static async Task<IReadOnlyList<EventOccupancyRow>> RunAsync(
        IApplicationDbContext _context,
        ReportScope scope,
        CancellationToken cancellationToken)
    {
        var rows = await scope.Apply(_context.Events.AsNoTracking())
            .Select(e => new
            {
                e.Id,
                e.Title,
                e.EventDate,

                // Koltuk sayimlarini ALT SORGU ile alıyorum.
                //
                // GroupBy ile de yapilabilirdi ama o zaman koltuğu
                // OLMAYAN etkinlikler sonuctan DUSERDI (inner join
                // davranisi). Oysa "0 koltuk üretilmiş" bilgisi de
                // rapor için degerli -- organizatör eksik kurulumu
                // gorebilmeli.
                Total = e.Sessions.SelectMany(s => s.EventSeats).Count(),
                Sold = e.Sessions.SelectMany(s => s.EventSeats)
                    .Count(es => es.Status == EventSeatStatus.Sold),
                Locked = e.Sessions.SelectMany(s => s.EventSeats)
                    .Count(es => es.Status == EventSeatStatus.Locked),
                Available = e.Sessions.SelectMany(s => s.EventSeats)
                    .Count(es => es.Status == EventSeatStatus.Available),
            })
            .OrderByDescending(x => x.EventDate)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var result = rows.ConvertAll(r => new EventOccupancyRow(
            r.Id,
            r.Title,
            r.EventDate,
            r.Total,
            r.Sold,
            r.Locked,
            r.Available,
            r.Total == 0 ? 0 : Math.Round((double)r.Sold / r.Total * 100, 1)));

        return result;
    }
}

// 3) ETKİNLİK BAZLI GELIR -- GET /api/v1/reports/revenue-by-event

public sealed record GetRevenueByEventReportQuery : ReportRangeRequest,
    IRequest<Result<IReadOnlyList<EventRevenue>>>;

internal sealed class GetRevenueByEventReportQueryHandler
    : IRequestHandler<GetRevenueByEventReportQuery, Result<IReadOnlyList<EventRevenue>>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;

    public GetRevenueByEventReportQueryHandler(
        IApplicationDbContext context,
        ICurrentUser currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<EventRevenue>>> Handle(
        GetRevenueByEventReportQuery request,
        CancellationToken cancellationToken)
    {
        var scopeResult = await ReportScopeResolver
            .ResolveAsync(_context, _currentUser, cancellationToken)
            .ConfigureAwait(false);

        if (!scopeResult.IsSuccess)
        {
            return Result.Failure<IReadOnlyList<EventRevenue>>(scopeResult.Error);
        }

        var rapor = await RunAsync(
            _context, scopeResult.Value, request.From, request.To, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(rapor);
    }

    /// <summary>Sorgunun kendisi. Bkz. SalesSummary açıklaması.</summary>
    internal static async Task<IReadOnlyList<EventRevenue>> RunAsync(
        IApplicationDbContext _context,
        ReportScope scope,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        var request = new { From = from, To = to };

        var tickets = scope
            .Apply(_context.Tickets.AsNoTracking())
            .Where(t => t.Status == TicketStatus.Active || t.Status == TicketStatus.Used);

        if (request.From.HasValue)
        {
            tickets = tickets.Where(t => t.CreatedAt >= request.From.Value);
        }

        if (request.To.HasValue)
        {
            tickets = tickets.Where(t => t.CreatedAt <= request.To.Value);
        }

        var rows = await tickets
            .GroupBy(t => new
            {
                t.EventSeat.EventSession.Event.Id,
                t.EventSeat.EventSession.Event.Title,
            })

            // Anonim tipe projeksiyon, record'a bellekte cevirim
            //
            // Önce doğrudan "new EventRevenue(...)" yaziyordum ve uc
            // 500 dondu:
            //
            //   InvalidOperationException: The LINQ expression ...
            //   could not be translated
            //
            // EF Core, GroupBy sonucunu bir RECORD KURUCUSUNA
            // projelendiremiyor (anonim tipe ise sorunsuz cevirebiliyor).
            //
            // Cozum: SQL'e cevrilebilen anonim tiple gruplayip,
            // record'a bellekte gecmek. Gruplama sonucu zaten küçük
            // (etkinlik sayısı kadar satır), yani bellekte islemenin
            // maliyeti yok.
            //
            // ONEMLI: bu, "veriyi bellege cekip C#'ta grupla" DEĞİL.
            // Gruplama ve toplama HALA SQL'de yapiliyor; yalnızca
            // sonucun tipe donusumu bellekte.
            .Select(g => new
            {
                g.Key.Id,
                g.Key.Title,
                Count = g.Count(),
                Revenue = g.Sum(t => t.Price.Amount),
            })
            .OrderByDescending(x => x.Revenue)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.ConvertAll(r => new EventRevenue(r.Id, r.Title, r.Count, r.Revenue));
    }
}

// 4) BİLET TURU SATISLARI -- GET /api/v1/reports/ticket-type-sales

public sealed record TicketTypeSalesRow(
    string TicketTypeName,
    int SoldCount,
    int RefundedCount,
    decimal Revenue,
    decimal AveragePrice);

public sealed record GetTicketTypeSalesReportQuery : ReportRangeRequest,
    IRequest<Result<IReadOnlyList<TicketTypeSalesRow>>>;

internal sealed class GetTicketTypeSalesReportQueryHandler
    : IRequestHandler<GetTicketTypeSalesReportQuery, Result<IReadOnlyList<TicketTypeSalesRow>>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;

    public GetTicketTypeSalesReportQueryHandler(
        IApplicationDbContext context,
        ICurrentUser currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<TicketTypeSalesRow>>> Handle(
        GetTicketTypeSalesReportQuery request,
        CancellationToken cancellationToken)
    {
        var scopeResult = await ReportScopeResolver
            .ResolveAsync(_context, _currentUser, cancellationToken)
            .ConfigureAwait(false);

        if (!scopeResult.IsSuccess)
        {
            return Result.Failure<IReadOnlyList<TicketTypeSalesRow>>(scopeResult.Error);
        }

        var rapor = await RunAsync(
            _context, scopeResult.Value, request.From, request.To, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(rapor);
    }

    /// <summary>Sorgunun kendisi. Bkz. SalesSummary açıklaması.</summary>
    internal static async Task<IReadOnlyList<TicketTypeSalesRow>> RunAsync(
        IApplicationDbContext _context,
        ReportScope scope,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        var request = new { From = from, To = to };

        var tickets = scope.Apply(_context.Tickets.AsNoTracking());

        if (request.From.HasValue)
        {
            tickets = tickets.Where(t => t.CreatedAt >= request.From.Value);
        }

        if (request.To.HasValue)
        {
            tickets = tickets.Where(t => t.CreatedAt <= request.To.Value);
        }

        var rows = await tickets
            .GroupBy(t => t.EventSeat.TicketType.Name)
            .Select(g => new
            {
                Name = g.Key,

                // Satılan ve iade edilen AYNI gruplamada.
                //
                // Iki ayrı sorgu yapip birlestirseydim, aralarinda bir
                // iade gerceklesirse rakamlar tutarsiz olurdu.
                Sold = g.Count(t => t.Status == TicketStatus.Active
                                 || t.Status == TicketStatus.Used),
                Refunded = g.Count(t => t.Status == TicketStatus.Refunded),
                Revenue = g.Where(t => t.Status == TicketStatus.Active
                                    || t.Status == TicketStatus.Used)
                           .Sum(t => t.Price.Amount),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var result = rows
            .Select(r => new TicketTypeSalesRow(
                r.Name,
                r.Sold,
                r.Refunded,
                r.Revenue,

                // Ortalama fiyati BİLET TURUNUN listelenen fiyatindan
                // değil, GERCEKLESEN satistan hesapliyorum.
                //
                // Fark önemli: bilet turunun fiyati sonradan
                // degistirilmis olabilir. Satılan biletler eski
                // fiyati tasiyor ve rapor gercekte ne kazanildigini
                // gostermeli.
                r.Sold == 0 ? 0 : Math.Round(r.Revenue / r.Sold, 2)))
            .OrderByDescending(r => r.Revenue)
            .ToList();

        return result;
    }
}

// 5) ÖDEME DURUMLARI -- GET /api/v1/reports/payment-statuses

public sealed record PaymentStatusRow(
    PaymentStatus Status,
    string StatusName,
    int Count,
    decimal TotalAmount,
    double Percentage);

public sealed record GetPaymentStatusReportQuery : ReportRangeRequest,
    IRequest<Result<IReadOnlyList<PaymentStatusRow>>>;

internal sealed class GetPaymentStatusReportQueryHandler
    : IRequestHandler<GetPaymentStatusReportQuery, Result<IReadOnlyList<PaymentStatusRow>>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;

    public GetPaymentStatusReportQueryHandler(
        IApplicationDbContext context,
        ICurrentUser currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<PaymentStatusRow>>> Handle(
        GetPaymentStatusReportQuery request,
        CancellationToken cancellationToken)
    {
        var scopeResult = await ReportScopeResolver
            .ResolveAsync(_context, _currentUser, cancellationToken)
            .ConfigureAwait(false);

        if (!scopeResult.IsSuccess)
        {
            return Result.Failure<IReadOnlyList<PaymentStatusRow>>(scopeResult.Error);
        }

        var rapor = await RunAsync(
            _context, scopeResult.Value, request.From, request.To, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(rapor);
    }

    /// <summary>Sorgunun kendisi. Bkz. SalesSummary açıklaması.</summary>
    internal static async Task<IReadOnlyList<PaymentStatusRow>> RunAsync(
        IApplicationDbContext _context,
        ReportScope scope,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        var request = new { From = from, To = to };

        var payments = _context.Payments.AsNoTracking();

        // Odemelerde kapsam, rezervasyon -> oturum -> etkinlik
        // zinciri üzerinden uygulaniyor.
        if (!scope.IsAdmin)
        {
            payments = payments.Where(
                p => p.Reservation.EventSession.Event.OrganizerId == scope.OrganizerId);
        }

        if (request.From.HasValue)
        {
            payments = payments.Where(p => p.CreatedAt >= request.From.Value);
        }

        if (request.To.HasValue)
        {
            payments = payments.Where(p => p.CreatedAt <= request.To.Value);
        }

        var rows = await payments
            .GroupBy(p => p.Status)
            .Select(g => new
            {
                Status = g.Key,
                Count = g.Count(),
                Total = g.Sum(p => p.Amount.Amount),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var toplam = rows.Sum(r => r.Count);

        var result = rows
            .Select(r => new PaymentStatusRow(
                r.Status,

                // Enum adını METİN olarak da donuyorum.
                //
                // Yalnızca sayi donseydim her istemci kendi cevirim
                // tablosunu tutmak zorunda kalırdı -- ve enum değişince
                // biri guncellenmeyi unuturdu.
                r.Status.ToString(),
                r.Count,
                r.Total,
                toplam == 0 ? 0 : Math.Round((double)r.Count / toplam * 100, 1)))
            .OrderByDescending(r => r.Count)
            .ToList();

        return result;
    }
}
