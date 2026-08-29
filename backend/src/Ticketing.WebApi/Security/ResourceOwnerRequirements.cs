using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Abstractions.Persistence;
using Ticketing.Domain.Entities;

namespace Ticketing.WebApi.Security;

// ===================================================================
// KAYNAK BAZLI YETKILENDIRME -- PDF: "Resource based authorization"
// ===================================================================
// Sprint 3'te TicketOwner ve ReservationOwner politikalari
// tanimlanmisti ama yalnızca RequireAuthenticatedUser() yapiyorlardi.
// Koddaki not soyluyordu: "gerçek sahiplik kontrollerini Sprint 7-8'de
// yazacagiz."
//
// Sprint 19 denetiminde yazilmadiklarini buldum.
//
// ------------------------------------------------------------------
// PEKI SISTEM ACIK MIYDI? -- HAYIR, VE BUNU OLCTUM
// ------------------------------------------------------------------
// Iki kullanıcı olusturup birinin rezervasyonuna digerinin erismesini
// denedim:
//
//   Rezervasyonu OKU      -> 404
//   Rezervasyonu İPTAL ET -> 404
//   Sureyi UZAT           -> 404
//   Ödeme AC              -> 404
//
// Yani handler'lar sahiplik kontrolunu ZATEN yapiyor (ve varligi
// sizdirmamak için 403 yerine 404 donuyorlar -- doğru davranis).
//
// ------------------------------------------------------------------
// O ZAMAN BU DOSYA NEDEN VAR?
// ------------------------------------------------------------------
// Uc sebep:
//
// 1) POLITIKA YANILTICIYDI. Bir controller'a
//    [Authorize(Policy = TicketOwner)] yazan kişi, sahiplik
//    kontrolunun POLITIKA tarafından yapildigini sanirdi. Oysa tek
//    koruma handler'in icindeki bir Where kosuluydu. Birisi o kosulu
//    kaldirsa, politika hiçbir sey fark etmezdi.
//
// 2) SAVUNMA TEK KATMANLIYDI. Simdi iki bağımsız katman var:
//    politika isteği kapida durduruyor, handler kendi sorgusunda
//    yine filtreliyor. Birinin unutulmasi digerini geçersiz kilmiyor.
//
// 3) PDF ACIKCA ISTIYOR: "Resource based authorization
//    uygulanmalıdır" ve ornekler arasında TicketOwner ile
//    ReservationOwner sayiliyor.
//
// Kalip EventOwnerRequirement ile birebir aynı -- bilinçli: uc
// politika aynı şekilde okunuyor ve aynı tuzaklardan (captive
// dependency, admin muafiyeti) aynı şekilde korunuyor.
// ===================================================================

/// <summary>Kullanıcı bu bilete sahip mi? PDF: TicketOwner.</summary>
public sealed class TicketOwnerRequirement : IAuthorizationRequirement;

/// <summary>Kullanıcı bu rezervasyona sahip mi? PDF: ReservationOwner.</summary>
public sealed class ReservationOwnerRequirement : IAuthorizationRequirement;

/// <summary>
/// Sahiplik kontrolü yapan handler'lar için ortak temel.
/// </summary>
/// <remarks>
/// ==================================================================
/// NEDEN ORTAK TEMEL SINIF?
/// ==================================================================
/// Iki handler da AYNI dort adimi yapiyor:
///   1) HttpContext var mi?
///   2) Admin mi? (evetse geç)
///   3) Kullanıcı kimliği nedir?
///   4) Route'taki kaynak bu kullanıcıya mi ait?
///
/// Yalnızca 4. adim farklı. Ikisini ayrı ayrı yazsaydık, ilk uc adim
/// kopyalanirdi ve biri degistiginde digerini guncellemeyi unutmak
/// çok kolay olurdu -- ozellikle admin muafiyetini.
///
/// Admin muafiyetinin unutulmasi somut bir hataya yol acardi: destek
/// ekibi bir kullanıcının biletini inceleyemezdi.
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

    /// <summary>Route'ta aranan parametrenin adı.</summary>
    protected abstract string RouteParameterName { get; }

    /// <summary>Kaynak bu kullanıcıya mi ait?</summary>
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
        // Destek ekibi bir kullanıcının biletini inceleyebilmeli;
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
            // Bu politikayi parametresiz bir uca (örneğin liste ucuna)
            // yanlislikla eklersek, "kontrol edecek bir kaynak yok"
            // durumu olusuyor.
            //
            // Sessizce GECIRSEYDIK o uc korumasiz kalırdı ve kimse
            // fark etmezdi. Reddetmek, yanlış kullanimi HEMEN görünür
            // kiliyor: uc 403 döner ve gelistirici sebebini arar.
            // ==========================================================
            return;
        }

        // ==============================================================
        // KENDİ KAPSAMIM (scope) -- captive dependency'den kacinmak için
        // ==============================================================
        // AuthorizationHandler singleton; DbContext scoped. Singleton'a
        // scoped enjekte etmek DbContext'i uygulama omru boyunca
        // yasatir, baglantiyi tutar ve es zamanlı isteklerde bozar.
        //
        // EventOwnerAuthorizationHandler ile aynı çözüm.
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

        // Ticket.UserId doğrudan sahibi tutuyor; ek bir JOIN gerekmiyor.
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
