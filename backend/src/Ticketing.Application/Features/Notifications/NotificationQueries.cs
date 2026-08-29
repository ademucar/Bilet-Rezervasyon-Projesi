using MediatR;
using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Abstractions.Persistence;
using Ticketing.Application.Abstractions.Security;
using Ticketing.Application.Abstractions.Time;
using Ticketing.Application.Common.Pagination;
using Ticketing.Application.Common.Results;
using Ticketing.Domain.Enums;

namespace Ticketing.Application.Features.Notifications;

public sealed record NotificationDto(
    Guid Id,
    NotificationType Type,
    string Title,
    string Message,
    string? ActionPath,
    Guid? RelatedEntityId,
    bool IsRead,
    DateTimeOffset CreatedAt);

// 1) LISTE -- PDF: GET /api/v1/notifications

public sealed record GetNotificationsQuery : PaginationRequest,
    IRequest<Result<PagedResult<NotificationDto>>>
{
    /// <summary>Yalnızca okunmamislari getir.</summary>
    public bool UnreadOnly { get; init; }
}

internal sealed class GetNotificationsQueryHandler
    : IRequestHandler<GetNotificationsQuery, Result<PagedResult<NotificationDto>>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;

    public GetNotificationsQueryHandler(IApplicationDbContext context, ICurrentUser currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<Result<PagedResult<NotificationDto>>> Handle(
        GetNotificationsQuery request,
        CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not Guid userId)
        {
            return Result.Failure<PagedResult<NotificationDto>>(
                Error.Unauthorized("auth.required", "Giriş yapmalisiniz."));
        }

        // KULLANICI FILTRESI -- BU SORGUNUN EN ONEMLI SATIRI
        //
        // Bildirimler tanim geregi KISISEL: rezervasyon kodlari, ödeme
        // tutarlari, hangi etkinlige gittiginiz.
        //
        // Bu filtre olmasaydı herkes herkesin bildirimlerini gorurdu.
        // Ve bu, hiçbir hata mesaji vermeden calisirdi -- yalnızca
        // "çok fazla bildirim" olarak görünürdü.
        //
        // Bu sorgu ASLA onbelleklenmiyor (PDF Sprint 11 kuralı:
        // "Kullanıcıya ozel hassas veriler ortak cache içinde
        // tutulmamalidir").
        var query = _context.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == userId);

        if (request.UnreadOnly)
        {
            query = query.Where(n => !n.IsRead);
        }

        var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        var items = await query
            // En yeni önce: bildirim listesinde kullanıcı SON olani
            // gormek ister.
            .OrderByDescending(n => n.CreatedAt)
            .Skip(request.Skip)
            .Take(request.PageSize)
            .Select(n => new NotificationDto(
                n.Id,
                n.Type,
                n.Title,
                n.Message,
                n.ActionPath,
                n.RelatedEntityId,
                n.IsRead,
                n.CreatedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(
            PagedResult<NotificationDto>.Create(
                items, request.PageNumber, request.PageSize, totalCount));
    }
}

// 2) OKUNMAMIS SAYISI -- PDF: GET /api/v1/notifications/unread-count

/// <summary>
/// Okunmamis bildirim sayısı. Zil ikonundaki rozet için.
/// </summary>
/// <remarks>
/// NEDEN AYRI BIR UC? Listeden de sayilabilirdi.
///
/// Sayiyi liste ucundan da alabilirdim (totalCount). Ama zil rozeti
/// HER SAYFADA ve DUZENLI ARALIKLARLA yenileniyor.
///
/// Liste ucunu cagirsaydim her yenilemede 20 bildirimin tüm metnini
/// (başlık, mesaj, adres) boşuna tasirdim. Bu uc tek bir SAYI
/// dönüyor -- SQL tarafında da yalnızca COUNT çalışıyor, satirlar
/// hiç okunmuyor.
///
/// ix_notifications_user_isread index'i bu sorguyu karsiliyor.
/// </remarks>
public sealed record GetUnreadNotificationCountQuery : IRequest<Result<int>>;

internal sealed class GetUnreadNotificationCountQueryHandler
    : IRequestHandler<GetUnreadNotificationCountQuery, Result<int>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;

    public GetUnreadNotificationCountQueryHandler(
        IApplicationDbContext context,
        ICurrentUser currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<Result<int>> Handle(
        GetUnreadNotificationCountQuery request,
        CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not Guid userId)
        {
            // Giriş yapmamis kullanıcı için HATA değil SIFIR donuyorum.
            //
            // Zil ikonu her sayfada var ve oturum süresi dolmuş bir
            // kullanicida 401 hatası, arayüzde gereksiz bir hata
            // kutusu olarak görünürdü. Sifir bildirim göstermek
            // doğru davranis.
            return Result.Success(0);
        }

        var count = await _context.Notifications
            .AsNoTracking()
            .CountAsync(n => n.UserId == userId && !n.IsRead, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(count);
    }
}

// 3) OKUNDU ISARETLE -- PDF: PATCH /api/v1/notifications/{id}/read

public sealed record MarkNotificationReadCommand(Guid Id) : IRequest<Result>;

internal sealed class MarkNotificationReadCommandHandler
    : IRequestHandler<MarkNotificationReadCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public MarkNotificationReadCommandHandler(
        IApplicationDbContext context,
        ICurrentUser currentUser,
        IDateTimeProvider clock)
    {
        _context = context;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result> Handle(
        MarkNotificationReadCommand request,
        CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not Guid userId)
        {
            return Result.Failure(Error.Unauthorized("auth.required", "Giriş yapmalisiniz."));
        }

        // Sahiplik kontrolü SORGUYA dahil.
        //
        // Önce kaydı cekip sonra "senin mi?" diye sorsaydik, iki
        // adımda yaptigim seyi tek adımda yapiyorum ve yanlislikla
        // kontrolü atlamak imkansizlasiyor.
        var notification = await _context.Notifications
            .FirstOrDefaultAsync(
                n => n.Id == request.Id && n.UserId == userId,
                cancellationToken)
            .ConfigureAwait(false);

        if (notification is null)
        {
            // Baskasinin bildirimi de buraya duser ve 404 alır.
            //
            // 403 deseydim "bu bildirim VAR ama senin değil" demis
            // olurdum -- baskasinin bildirim aldigini dogrulamak
            // bile gereksiz bir sizinti.
            return Result.Failure(Error.NotFound(
                "notification.not_found", "Bildirim bulunamadı."));
        }

        // MarkAsRead zaten okunmussa hiçbir sey yapmiyor (idempotent).
        notification.MarkAsRead(_clock.UtcNow);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

// 4) TUMUNU OKUNDU ISARETLE -- PDF: PATCH /api/v1/notifications/read-all

public sealed record MarkAllNotificationsReadCommand : IRequest<Result<int>>;

internal sealed class MarkAllNotificationsReadCommandHandler
    : IRequestHandler<MarkAllNotificationsReadCommand, Result<int>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public MarkAllNotificationsReadCommandHandler(
        IApplicationDbContext context,
        ICurrentUser currentUser,
        IDateTimeProvider clock)
    {
        _context = context;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result<int>> Handle(
        MarkAllNotificationsReadCommand request,
        CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not Guid userId)
        {
            return Result.Failure<int>(Error.Unauthorized("auth.required", "Giriş yapmalisiniz."));
        }

        var now = _clock.UtcNow;

        // NEDEN ExecuteUpdateAsync DEĞİL?
        //
        // EF Core 7+ ile toplu güncelleme yapilabilir:
        //
        //     await query.ExecuteUpdateAsync(s => s
        //         .SetProperty(n => n.IsRead, true)
        //         .SetProperty(n => n.ReadAt, now));
        //
        // Tek SQL cumlesi, çok daha hizli. Kullanmadim çünkü:
        //
        //   1) Entity metodunu (MarkAsRead) ATLAR. Bugun basit ama
        //      ilerde bir kural eklenirse (örneğin "arsivlenmis
        //      bildirim okundu isaretlenemez") toplu güncelleme önü
        //      GORMEZ ve iki farklı davranis olusur.
        //
        //   2) Denetim interceptor'ini ATLAR: UpdatedAt/UpdatedBy
        //      dolmaz. Sprint 12'de tam bu tur bir bosluk yuzunden
        //      CreatedAt'in hiç dolmadigini bulmustum.
        //
        // Okunmamis bildirim sayısı kullanıcı başına onlarla olculur;
        // tek tek yuklemenin maliyeti kabul edilebilir.
        //
        // Binlerce satira ciksaydi karar degisirdi -- o zaman toplu
        // güncelleme yapip denetim alanlarini elle yazardim.
        var unread = await _context.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (unread.Count == 0)
        {
            return Result.Success(0);
        }

        foreach (var notification in unread)
        {
            notification.MarkAsRead(now);
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(unread.Count);
    }
}
