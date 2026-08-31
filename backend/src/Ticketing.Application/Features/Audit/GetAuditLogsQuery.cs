using MediatR;
using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Abstractions.Persistence;
using Ticketing.Application.Common.Pagination;
using Ticketing.Application.Common.Results;

namespace Ticketing.Application.Features.Audit;

public sealed record AuditLogListItem(
    Guid Id,
    string EntityName,
    Guid EntityId,
    string Action,
    string? OldValues,
    string? NewValues,
    Guid? UserId,
    string? UserEmail,
    string? IpAddress,
    string? CorrelationId,
    DateTimeOffset CreatedAt);

/// <summary>
/// Denetim kayitlari -- PDF sayfa 5:
/// "Admin: Audit log kayitlarini inceleyebilir."
/// </summary>
/// <remarks>
/// SERILOG VARKEN BU TABLO NEDEN VAR?
///
/// Ikisi farkli sorulara cevap veriyor:
///
///   Serilog  -> "sistemde ne oldu?" Teknik akis, hata ayiklama
///               icin. 14 gun sonra donuyor (dosya sinki), yani
///               kalici degil.
///   AuditLog -> "bu KAYIT uzerinde kim ne degistirdi?" Is
///               sorusudur, kaydin kendisiyle birlikte yasar ve
///               silinmez.
///
/// Bir musteri alti ay sonra "bilet fiyatim neden degisti" diye
/// sorarsa Serilog'da hicbir sey bulunmaz; AuditLogs'ta eski ve yeni
/// fiyat, degistiren kisi ve tarih durur.
/// </remarks>
public sealed record GetAuditLogsQuery : IRequest<Result<PagedResult<AuditLogListItem>>>
{
    /// <summary>Hangi tur kayit: "Event", "User", "TicketType"...</summary>
    public string? EntityName { get; init; }

    /// <summary>Belirli bir kaydin gecmisi.</summary>
    public Guid? EntityId { get; init; }

    /// <summary>Islem adi: "UserDeactivated", "PriceChanged"...</summary>
    public string? Action { get; init; }

    /// <summary>Belirli bir kullanicinin yaptigi islemler.</summary>
    public Guid? UserId { get; init; }

    public DateTimeOffset? DateFrom { get; init; }

    public DateTimeOffset? DateTo { get; init; }

    public int PageNumber { get; init; } = 1;

    public int PageSize { get; init; } = 25;
}

internal sealed class GetAuditLogsQueryHandler
    : IRequestHandler<GetAuditLogsQuery, Result<PagedResult<AuditLogListItem>>>
{
    private readonly IApplicationDbContext _context;

    public GetAuditLogsQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<Result<PagedResult<AuditLogListItem>>> Handle(
        GetAuditLogsQuery request,
        CancellationToken cancellationToken)
    {
        var sayfa = Math.Max(1, request.PageNumber);
        var boyut = Math.Clamp(request.PageSize, 1, 100);

        var query = _context.AuditLogs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.EntityName))
        {
            var ad = request.EntityName;
            query = query.Where(a => a.EntityName == ad);
        }

        if (request.EntityId.HasValue)
        {
            query = query.Where(a => a.EntityId == request.EntityId.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Action))
        {
            var islem = request.Action;
            query = query.Where(a => a.Action == islem);
        }

        if (request.UserId.HasValue)
        {
            query = query.Where(a => a.UserId == request.UserId.Value);
        }

        if (request.DateFrom.HasValue)
        {
            query = query.Where(a => a.CreatedAt >= request.DateFrom.Value);
        }

        if (request.DateTo.HasValue)
        {
            query = query.Where(a => a.CreatedAt <= request.DateTo.Value);
        }

        var toplam = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        // Kullanici e-postasini JOIN ile aliyorum, AuditLog'a
        // kopyalamiyorum.
        //
        // Kopyalamak "denetim kaydi degismez olmali" ilkesine daha
        // uygun olurdu (kullanici silinse bile e-posta kalirdi) ama
        // AuditLogs'ta UserId zaten var ve e-postayi ikinci kez
        // saklamak KVKK acisindan gereksiz veri cogaltmasi. Kullanici
        // silinirse e-posta bos gorunur, kimlik yine UserId'de durur.
        var kayitlar = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip((sayfa - 1) * boyut)
            .Take(boyut)
            .Select(a => new AuditLogListItem(
                a.Id,
                a.EntityName,
                a.EntityId,
                a.Action,
                a.OldValues,
                a.NewValues,
                a.UserId,
                _context.Users
                    .Where(u => u.Id == a.UserId)
                    .Select(u => u.Email)
                    .FirstOrDefault(),
                a.IpAddress,
                a.CorrelationId,
                a.CreatedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Sira: (items, pageNumber, pageSize, totalCount). Dordu de
        // int oldugu icin yanlis sira derlenirken yakalanmiyor;
        // kullanici listesinde ayni hatayi yapmistim.
        return Result.Success(
            PagedResult<AuditLogListItem>.Create(kayitlar, sayfa, boyut, toplam));
    }
}
