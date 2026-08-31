using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ticketing.Application.Abstractions.Persistence;
using Ticketing.Application.Abstractions.Security;
using Ticketing.Application.Common.Auditing;
using Ticketing.Application.Common.Logging;
using Ticketing.Application.Common.Pagination;
using Ticketing.Application.Common.Results;
using Ticketing.Domain.Entities;

namespace Ticketing.Application.Features.Users;

internal static class UserManagementErrors
{
    public static readonly Error NotFound = Error.NotFound(
        "user.not_found", "Kullanıcı bulunamadı.");

    public static readonly Error RoleNotFound = Error.Validation(
        "user.role_not_found", "Rol bulunamadı.");

    // Adminin kendi kendini kilitlemesini engelliyorum.
    //
    // Bu bir "olmaz boyle sey" korumasi degil, YASANAN bir kaza:
    // tek admin hesabi kendini pasife alirsa sisteme girecek kimse
    // kalmiyor ve duzeltmenin tek yolu veritabanina elle mudahale.
    public static readonly Error SelfLockout = Error.Conflict(
        "user.self_lockout",
        "Kendi hesabınızı pasifleştiremez veya kendi admin rolünüzü kaldıramazsınız.");

    public static readonly Error LastAdmin = Error.Conflict(
        "user.last_admin",
        "Sistemdeki son admin hesabının yetkisi kaldırılamaz.");
}

// ---- Listeleme ----

public sealed record UserListItem(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string? PhoneNumber,
    bool IsActive,
    bool IsEmailConfirmed,
    bool IsLockedOut,
    DateTimeOffset CreatedAt,
    IReadOnlyList<string> Roles);

/// <summary>
/// Kullanici listesi -- PDF sayfa 5: "Admin: Tum kullanicilari yonetebilir."
/// </summary>
public sealed record GetUsersQuery : IRequest<Result<PagedResult<UserListItem>>>
{
    /// <summary>E-posta veya ad-soyadda arar.</summary>
    public string? Search { get; init; }

    /// <summary>Rol adina gore suzer: "User", "Organizer", "Admin".</summary>
    public string? Role { get; init; }

    /// <summary>true: yalnizca aktifler, false: yalnizca pasifler, null: hepsi.</summary>
    public bool? IsActive { get; init; }

    public int PageNumber { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}

internal sealed class GetUsersQueryHandler
    : IRequestHandler<GetUsersQuery, Result<PagedResult<UserListItem>>>
{
    private readonly IApplicationDbContext _context;

    public GetUsersQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<Result<PagedResult<UserListItem>>> Handle(
        GetUsersQuery request,
        CancellationToken cancellationToken)
    {
        var sayfa = Math.Max(1, request.PageNumber);

        // Ust siniri 100'de tutuyorum: istemci pageSize=100000
        // gonderip tum tabloyu tek istekte cekmeye calismasin.
        var boyut = Math.Clamp(request.PageSize, 1, 100);

        var query = _context.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var arama = request.Search.Trim();

            // Once EF.Functions.ILike yazmistim (PostgreSQL'in
            // buyuk/kucuk harf duyarsiz LIKE'i) ama derlenmedi:
            // ILike Npgsql saglayicisina ait ve Application katmani
            // saglayiciyi TANIMIYOR -- Onion mimarisinin geregi, ve
            // mimari testi de bunu zorluyor.
            //
            // ToLower() ile gidiyorum. EF bunu SQL'de lower()'a
            // ceviriyor; sonuc yine buyuk/kucuk harf duyarsiz. Bedeli:
            // sutundaki index kullanilamiyor. Kullanici tablosu bu
            // proje olceginde kucuk oldugu icin kabul ediyorum;
            // gerekirse cozum bir ifade index'i (lower("Email"))
            // eklemek olurdu, sorguyu degistirmek degil.
            var kucuk = arama.ToLowerInvariant();

            // Analyzer'i BURADA bastiriyorum (CA1304/CA1311/CA1862).
            //
            // Uyarilar hakli GORUNUYOR ama bu baglamda gecersiz: bu
            // ifade .NET'te CALISMIYOR. EF onu SQL'e ceviriyor ve
            // veritabaninda lower() olarak kosuyor -- yani .NET
            // kulturu hic devreye girmiyor, CultureInfo verecek bir
            // yer de yok. StringComparison alan asiri yuklemeyi
            // kullansaydim EF ifadeyi CEVIREMEZ ve sorguyu bellege
            // cekip orada filtrelerdi; yani tum kullanici tablosunu
            // uygulamaya tasirdi.
            //
            // Bastirmayi dosya genelinde degil, tam bu uc satirda
            // yapiyorum: baska bir yerde ayni cagri gercekten hata
            // olurdu ve analyzer'in orada konusmasini istiyorum.
#pragma warning disable CA1304, CA1311, CA1862
            query = query.Where(u =>
                u.Email.ToLower().Contains(kucuk)
                || u.FirstName.ToLower().Contains(kucuk)
                || u.LastName.ToLower().Contains(kucuk));
#pragma warning restore CA1304, CA1311, CA1862
        }

        if (request.IsActive.HasValue)
        {
            query = query.Where(u => u.IsActive == request.IsActive.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Role))
        {
            var rol = request.Role;
            query = query.Where(u => u.UserRoles.Any(ur => ur.Role.Name == rol));
        }

        var toplam = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        var kullanicilar = await query
            // En yeni kayitlar once: adminin arayacagi kisi genellikle
            // yeni katilan biri.
            .OrderByDescending(u => u.CreatedAt)
            .Skip((sayfa - 1) * boyut)
            .Take(boyut)
            .Select(u => new UserListItem(
                u.Id,
                u.Email,
                u.FirstName,
                u.LastName,
                u.PhoneNumber,
                u.IsActive,
                u.IsEmailConfirmed,

                // Kilit "su an kilitli mi" sorusunun cevabi: gecmis
                // bir tarih artik kilit degil.
                u.LockoutEndAt != null && u.LockoutEndAt > DateTimeOffset.UtcNow,

                u.CreatedAt,
                u.UserRoles.Select(ur => ur.Role.Name).ToList()))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Parametre sirasi: (items, pageNumber, pageSize, totalCount).
        //
        // Ilk yazista (items, toplam, sayfa, boyut) diye gecirmistim
        // -- dordu de int oldugu icin derleyici sikayet etmedi ve
        // ekranda "20 kullanici" yaziyordu, oysa 5 kullanici vardi:
        // sayfa boyutunu toplam sanmisim. Tarayicida gercek veriyle
        // bakinca cikti.
        return Result.Success(
            PagedResult<UserListItem>.Create(kullanicilar, sayfa, boyut, toplam));
    }
}

// ---- Aktif / pasif ----

public sealed record SetUserActiveCommand(Guid UserId, bool IsActive) : IRequest<Result>;

// ---- Rol atama ----

public sealed record SetUserRoleCommand(Guid UserId, string RoleName, bool Assign) : IRequest<Result>;

internal sealed partial class UserManagementCommandHandler
    : IRequestHandler<SetUserActiveCommand, Result>,
      IRequestHandler<SetUserRoleCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<UserManagementCommandHandler> _logger;

    [LoggerMessage(
        EventId = LogEvents.KullaniciDurumuDegisti,
        Level = LogLevel.Warning,
        Message = "Kullanici durumu degisti. Kullanici: {Eposta}, Aktif: {Aktif}")]
    private static partial void LogUserStateChanged(ILogger logger, string eposta, bool aktif);

    [LoggerMessage(
        EventId = LogEvents.KullaniciRoluDegisti,
        Level = LogLevel.Warning,
        Message = "Kullanici rolu degisti. Kullanici: {Eposta}, Rol: {Rol}, Atandi: {Atandi}")]
    private static partial void LogUserRoleChanged(
        ILogger logger, string eposta, string rol, bool atandi);

    public UserManagementCommandHandler(
        IApplicationDbContext context,
        ICurrentUser currentUser,
        ILogger<UserManagementCommandHandler> logger)
    {
        _context = context;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<Result> Handle(SetUserActiveCommand request, CancellationToken cancellationToken)
    {
        // Kendi hesabini pasife alma girisimi.
        //
        // Kontrolu EN BASA koydum: kullaniciyi veritabanindan
        // cekmeden once. Boylece "once yukle, sonra reddet" gibi
        // gereksiz bir sorgu olmuyor ve niyet okurken hemen goze
        // carpiyor.
        if (!request.IsActive && _currentUser.UserId == request.UserId)
        {
            return Result.Failure(UserManagementErrors.SelfLockout);
        }

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken)
            .ConfigureAwait(false);

        if (user is null)
        {
            return Result.Failure(UserManagementErrors.NotFound);
        }

        if (user.IsActive == request.IsActive)
        {
            // Zaten istenen durumda: hata degil, sessizce basarili.
            // "Aktiflestir" iki kez cagrilirsa sonuc ayni olmali.
            return Result.Success();
        }

        var eskiDurum = user.IsActive;

        if (request.IsActive)
        {
            user.Activate();
        }
        else
        {
            user.Deactivate();
        }

        // PDF sayfa 5: "Admin audit log kayitlarini inceleyebilir."
        //
        // Bir hesabi kapatmak, o kullanicinin sisteme erisimini
        // kesiyor. "Kim, ne zaman, kimi kapatti?" sorusunun cevabi
        // kalici olarak durmali -- Serilog kayitlari 14 gun sonra
        // donuyor, AuditLogs donmuyor.
        _context.AddAudit(
            _currentUser,
            nameof(User),
            user.Id,
            request.IsActive ? "UserActivated" : "UserDeactivated",
            oldValues: new { IsActive = eskiDurum },
            newValues: new { IsActive = request.IsActive });

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        LogUserStateChanged(_logger, user.Email, request.IsActive);

        return Result.Success();
    }

    public async Task<Result> Handle(SetUserRoleCommand request, CancellationToken cancellationToken)
    {
        var rol = await _context.Roles
            .FirstOrDefaultAsync(r => r.Name == request.RoleName, cancellationToken)
            .ConfigureAwait(false);

        if (rol is null)
        {
            return Result.Failure(UserManagementErrors.RoleNotFound);
        }

        // Kendi admin rolunu kaldirma girisimi.
        if (!request.Assign
            && rol.Name == Role.Names.Admin
            && _currentUser.UserId == request.UserId)
        {
            return Result.Failure(UserManagementErrors.SelfLockout);
        }

        // Include SART: AssignRole/RemoveRole _userRoles koleksiyonu
        // uzerinde calisiyor. Include etmezsem koleksiyon BOS gelir,
        // RemoveRole hicbir sey silmez ve islem sessizce basarili
        // gorunur. Bu tuzaga bilet iptalinde bir kez dustum
        // (bkz. TicketCancellationCommands).
        var user = await _context.Users
            .Include(u => u.UserRoles)
            .FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken)
            .ConfigureAwait(false);

        if (user is null)
        {
            return Result.Failure(UserManagementErrors.NotFound);
        }

        // Son adminin yetkisini kaldirmayi engelliyorum.
        //
        // Kendi kendini korumadan AYRI bir kontrol: admin A, admin
        // B'nin yetkisini kaldirabilir -- ama sistemde baska admin
        // kalmiyorsa kimse kimseyi yonetemez hale gelir.
        if (!request.Assign && rol.Name == Role.Names.Admin)
        {
            var adminSayisi = await _context.UserRoles
                .CountAsync(ur => ur.RoleId == rol.Id, cancellationToken)
                .ConfigureAwait(false);

            if (adminSayisi <= 1)
            {
                return Result.Failure(UserManagementErrors.LastAdmin);
            }
        }

        if (request.Assign)
        {
            user.AssignRole(rol);
        }
        else
        {
            user.RemoveRole(rol.Id);
        }

        _context.AddAudit(
            _currentUser,
            nameof(User),
            user.Id,
            request.Assign ? "RoleAssigned" : "RoleRemoved",
            newValues: new { Role = rol.Name });

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        LogUserRoleChanged(_logger, user.Email, rol.Name, request.Assign);

        // NOT: kullanicinin ELINDEKI access token'da hala eski roller
        // var. Yeni rolu gorebilmesi icin token'in yenilenmesi
        // gerekiyor (en gec 15 dakika). Organizator basvurusu
        // onayinda da ayni notu dusmustum.
        return Result.Success();
    }
}
