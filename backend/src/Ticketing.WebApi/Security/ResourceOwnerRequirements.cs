using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Abstractions.Persistence;
using Ticketing.Domain.Entities;

namespace Ticketing.WebApi.Security;

// ===================================================================
// KAYNAK BAZLI YETKILENDIRME -- PDF: "Resource based authorization"
// ===================================================================
// Sprint 3'te TicketOwner ve ReservationOwner politikalari
// tanimlanmisti ama yalnizca RequireAuthenticatedUser() yapiyorlardi.
// Koddaki not soyluyordu: "gercek sahiplik kontrollerini Sprint 7-8'de
// yazacagiz."
//
// Sprint 19 denetiminde yazilmadiklarini buldum.
//
// ------------------------------------------------------------------
// PEKI SISTEM ACIK MIYDI? -- HAYIR, VE BUNU OLCTUM
// ------------------------------------------------------------------
// Iki kullanici olusturup birinin rezervasyonuna digerinin erismesini
// denedim:
//
//   Rezervasyonu OKU      -> 404
//   Rezervasyonu IPTAL ET -> 404
//   Sureyi UZAT           -> 404
//   Odeme AC              -> 404
//
// Yani handler'lar sahiplik kontrolunu ZATEN yapiyor (ve varligi
// sizdirmamak icin 403 yerine 404 donuyorlar -- dogru davranis).
//
// ------------------------------------------------------------------
// O ZAMAN BU DOSYA NEDEN VAR?
// ------------------------------------------------------------------
// Uc sebep:
//
// 1) POLITIKA YANILTICIYDI. Bir controller'a
//    [Authorize(Policy = TicketOwner)] yazan kisi, sahiplik
//    kontrolunun POLITIKA tarafindan yapildigini sanirdi. Oysa tek
//    koruma handler'in icindeki bir Where kosuluydu. Birisi o kosulu
//    kaldirsa, politika hicbir sey fark etmezdi.
//
// 2) SAVUNMA TEK KATMANLIYDI. Simdi iki bagimsiz katman var:
//    politika istegi kapida durduruyor, handler kendi sorgusunda
//    yine filtreliyor. Birinin unutulmasi digerini gecersiz kilmiyor.
//
// 3) PDF ACIKCA ISTIYOR: "Resource based authorization
//    uygulanmalidir" ve ornekler arasinda TicketOwner ile
//    ReservationOwner sayiliyor.
//
// Kalip EventOwnerRequirement ile birebir ayni -- bilincli: uc
// politika ayni sekilde okunuyor ve ayni tuzaklardan (captive
// dependency, admin muafiyeti) ayni sekilde korunuyor.
// ===================================================================

/// <summary>Kullanici bu bilete sahip mi? PDF: TicketOwner.</summary>
public sealed class TicketOwnerRequirement : IAuthorizationRequirement;

/// <summary>Kullanici bu rezervasyona sahip mi? PDF: ReservationOwner.</summary>
public sealed class ReservationOwnerRequirement : IAuthorizationRequirement;

/// <summary>
/// Sahiplik kontrolu yapan handler'lar icin ortak temel.
/// </summary>
/// <remarks>
/// ==================================================================
/// NEDEN ORTAK TEMEL SINIF?
/// ==================================================================
/// Iki handler da AYNI dort adimi yapiyor:
///   1) HttpContext var mi?
///   2) Admin mi? (evetse gec)
///   3) Kullanici kimligi nedir?
///   4) Route'taki kaynak bu kullaniciya mi ait?
///
/// Yalnizca 4. adim farkli. Ikisini ayri ayri yazsaydik, ilk uc adim
/// kopyalanirdi ve biri degistiginde digerini guncellemeyi unutmak
/// cok kolay olurdu -- ozellikle admin muafiyetini.
///
/// Admin muafiyetinin unutulmasi somut bir hataya yol acardi: destek
/// ekibi bir kullanicinin biletini inceleyemezdi.
/// ==================================================================
/// </remarks>
internal abstract class ResourceOwnerHandlerBase<TRequirement>
    : AuthorizationHandler<TRequirement>
    where TRequirement : IAuthorizationRequirement
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IServiceScopeFactory _scopeFactory;

    protected ResourceOwnerHandlerBase(
        IHttpContextAccessor httpContextAccessor,
        IServiceScopeFactory scopeFactory)
    {
        _httpContextAccessor = httpContextAccessor;
        _scopeFactory = scopeFactory;
    }

    /// <summary>Route'ta aranan parametrenin adi.</summary>
    protected abstract string RouteParameterName { get; }

    /// <summary>Kaynak bu kullaniciya mi ait?</summary>
    protected abstract Task<bool> IsOwnerAsync(
        IApplicationDbContext context,
        Guid resourceId,
        Guid userId,
        CancellationToken cancellationToken);

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        TRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);

        var httpContext = _httpContextAccessor.HttpContext;

        if (httpContext is null)
        {
            return;   // Succeed cagrilmadi -> reddedildi.
        }

        // ---- Admin muafiyeti ----
        //
        // Destek ekibi bir kullanicinin biletini inceleyebilmeli;
        // iade islemini zaten admin yapiyor (Sprint 8).
        if (context.User.IsInRole(Role.Names.Admin))
        {
            context.Succeed(requirement);

            return;
        }

        if (GetUserId(context) is not Guid userId)
        {
            return;
        }

        if (!httpContext.Request.RouteValues.TryGetValue(RouteParameterName, out var deger)
            || !Guid.TryParse(deger?.ToString(), out var resourceId))
        {
            // ==========================================================
            // ROUTE PARAMETRESI YOKSA REDDEDIYORUZ
            // ==========================================================
            // Bu politikayi parametresiz bir uca (ornegin liste ucuna)
            // yanlislikla eklersek, "kontrol edecek bir kaynak yok"
            // durumu olusuyor.
            //
            // Sessizce GECIRSEYDIK o uc korumasiz kalirdi ve kimse
            // fark etmezdi. Reddetmek, yanlis kullanimi HEMEN gorunur
            // kiliyor: uc 403 doner ve gelistirici sebebini arar.
            // ==========================================================
            return;
        }

        // ==============================================================
        // KENDI KAPSAMIM (scope) -- captive dependency'den kacinmak icin
        // ==============================================================
        // AuthorizationHandler singleton; DbContext scoped. Singleton'a
        // scoped enjekte etmek DbContext'i uygulama omru boyunca
        // yasatir, baglantiyi tutar ve es zamanli isteklerde bozar.
        //
        // EventOwnerAuthorizationHandler ile ayni cozum.
        // ==============================================================
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        if (await IsOwnerAsync(db, resourceId, userId, httpContext.RequestAborted)
                .ConfigureAwait(false))
        {
            context.Succeed(requirement);
        }
    }

    private static Guid? GetUserId(AuthorizationHandlerContext context)
    {
        var deger = context.User.FindFirst("sub")?.Value
            ?? context.User.FindFirst(
                System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(deger, out var id) ? id : null;
    }
}

/// <summary>PDF policy: TicketOwner.</summary>
internal sealed class TicketOwnerAuthorizationHandler
    : ResourceOwnerHandlerBase<TicketOwnerRequirement>
{
    public TicketOwnerAuthorizationHandler(
        IHttpContextAccessor httpContextAccessor,
        IServiceScopeFactory scopeFactory)
        : base(httpContextAccessor, scopeFactory)
    {
    }

    protected override string RouteParameterName => "id";

    protected override async Task<bool> IsOwnerAsync(
        IApplicationDbContext context,
        Guid resourceId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Ticket.UserId dogrudan sahibi tutuyor; ek bir JOIN gerekmiyor.
        return await context.Tickets
            .AsNoTracking()
            .AnyAsync(t => t.Id == resourceId && t.UserId == userId, cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>PDF policy: ReservationOwner.</summary>
internal sealed class ReservationOwnerAuthorizationHandler
    : ResourceOwnerHandlerBase<ReservationOwnerRequirement>
{
    public ReservationOwnerAuthorizationHandler(
        IHttpContextAccessor httpContextAccessor,
        IServiceScopeFactory scopeFactory)
        : base(httpContextAccessor, scopeFactory)
    {
    }

    protected override string RouteParameterName => "id";

    protected override async Task<bool> IsOwnerAsync(
        IApplicationDbContext context,
        Guid resourceId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        return await context.Reservations
            .AsNoTracking()
            .AnyAsync(r => r.Id == resourceId && r.UserId == userId, cancellationToken)
            .ConfigureAwait(false);
    }
}
