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

// ===================================================================
// 1) LISTE -- PDF: GET /api/v1/notifications
// ===================================================================

public sealed record GetNotificationsQuery : PaginationRequest,
    IRequest<Result<PagedResult<NotificationDto>>>
{
    /// <summary>Yalnizca okunmamislari getir.</summary>
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
                Error.Unauthorized("auth.required", "Giris yapmalisiniz."));
        }

        // ==============================================================
        // KULLANICI FILTRESI -- BU SORGUNUN EN ONEMLI SATIRI
        // ==============================================================
        // Bildirimler tanim geregi KISISEL: rezervasyon kodlari, odeme
        // tutarlari, hangi etkinlige gittiginiz.
        //
        // Bu filtre olmasaydi herkes herkesin bildirimlerini gorurdu.
        // Ve bu, hicbir hata mesaji vermeden calisirdi -- yalnizca
        // "cok fazla bildirim" olarak gorunurdu.
        //
        // Bu sorgu ASLA onbelleklenmiyor (PDF Sprint 11 kurali:
        // "Kullaniciya ozel hassas veriler ortak cache icinde
        // tutulmamalidir").
        // ==============================================================
        var query = _context.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == userId);

        if (request.UnreadOnly)
        {
            query = query.Where(n => !n.IsRead);
        }

        var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        var items = await query
            // En yeni once: bildirim listesinde kullanici SON olani
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

// ===================================================================
// 2) OKUNMAMIS SAYISI -- PDF: GET /api/v1/notifications/unread-count
// ===================================================================

/// <summary>
/// Okunmamis bildirim sayisi. Zil ikonundaki rozet icin.
/// </summary>
/// <remarks>
/// ==================================================================
/// NEDEN AYRI BIR UC? Listeden de sayilabilirdi.
/// ==================================================================
/// Sayiyi liste ucundan da alabilirdik (totalCount). Ama zil rozeti
/// HER SAYFADA ve DUZENLI ARALIKLARLA yenileniyor.
///
/// Liste ucunu cagirsaydik her yenilemede 20 bildirimin tum metnini
/// (baslik, mesaj, adres) bosuna tasirdik. Bu uc tek bir SAYI
/// donuyor -- SQL tarafinda da yalnizca COUNT calisiyor, satirlar
/// hic okunmuyor.
///
/// ix_notifications_user_isread index'i bu sorguyu karsiliyor.
/// ==================================================================
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
            // Giris yapmamis kullanici icin HATA degil SIFIR donuyorum.
            //
            // Zil ikonu her sayfada var ve oturum suresi dolmus bir
            // kullanicida 401 hatasi, arayuzde gereksiz bir hata
            // kutusu olarak gorunurdu. Sifir bildirim gostermek
            // dogru davranis.
            return Result.Success(0);
        }

        var count = await _context.Notifications
            .AsNoTracking()
            .CountAsync(n => n.UserId == userId && !n.IsRead, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(count);
    }
}

// ===================================================================
// 3) OKUNDU ISARETLE -- PDF: PATCH /api/v1/notifications/{id}/read
// ===================================================================

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
            return Result.Failure(Error.Unauthorized("auth.required", "Giris yapmalisiniz."));
        }

        // Sahiplik kontrolu SORGUYA dahil.
        //
        // Once kaydi cekip sonra "senin mi?" diye sorsaydik, iki
        // adimda yaptigimiz seyi tek adimda yapiyoruz ve yanlislikla
        // kontrolu atlamak imkansizlasiyor.
        var notification = await _context.Notifications
            .FirstOrDefaultAsync(
                n => n.Id == request.Id && n.UserId == userId,
                cancellationToken)
            .ConfigureAwait(false);

        if (notification is null)
        {
            // Baskasinin bildirimi de buraya duser ve 404 alir.
            //
            // 403 deseydik "bu bildirim VAR ama senin degil" demis
            // olurduk -- baskasinin bildirim aldigini dogrulamak
            // bile gereksiz bir sizinti.
            return Result.Failure(Error.NotFound(
                "notification.not_found", "Bildirim bulunamadi."));
        }

        // MarkAsRead zaten okunmussa hicbir sey yapmiyor (idempotent).
        notification.MarkAsRead(_clock.UtcNow);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

// ===================================================================
// 4) TUMUNU OKUNDU ISARETLE -- PDF: PATCH /api/v1/notifications/read-all
// ===================================================================

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
            return Result.Failure<int>(Error.Unauthorized("auth.required", "Giris yapmalisiniz."));
        }

        var now = _clock.UtcNow;

        // ==============================================================
        // NEDEN ExecuteUpdateAsync DEGIL?
        // ==============================================================
        // EF Core 7+ ile toplu guncelleme yapilabilir:
        //
        //     await query.ExecuteUpdateAsync(s => s
        //         .SetProperty(n => n.IsRead, true)
        //         .SetProperty(n => n.ReadAt, now));
        //
        // Tek SQL cumlesi, cok daha hizli. Kullanmadim cunku:
        //
        //   1) Entity metodunu (MarkAsRead) ATLAR. Bugun basit ama
        //      ilerde bir kural eklenirse (ornegin "arsivlenmis
        //      bildirim okundu isaretlenemez") toplu guncelleme onu
        //      GORMEZ ve iki farkli davranis olusur.
        //
        //   2) Denetim interceptor'ini ATLAR: UpdatedAt/UpdatedBy
        //      dolmaz. Sprint 12'de tam bu tur bir bosluk yuzunden
        //      CreatedAt'in hic dolmadigini bulmustum.
        //
        // Okunmamis bildirim sayisi kullanici basina onlarla olculur;
        // tek tek yuklemenin maliyeti kabul edilebilir.
        //
        // Binlerce satira ciksaydi karar degisirdi -- o zaman toplu
        // guncelleme yapip denetim alanlarini elle yazardim.
        // ==============================================================
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
