using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Abstractions.Persistence;
using Ticketing.Domain.Entities;

namespace Ticketing.WebApi.Security;

/// <summary>
/// "Bu etkinligin sahibi misin?" gereksinimi.
///
/// PDF Sprint 3: "Resource based authorization uygulanmalidir."
/// PDF Sprint 5: "Sadece kendi etkinliklerini guncelleyebilir."
/// </summary>
public sealed class EventOwnerRequirement : IAuthorizationRequirement;

/// <summary>
/// ==================================================================
/// ROL BAZLI ile KAYNAK BAZLI YETKILENDIRME ARASINDAKI FARK
/// ==================================================================
/// Rol bazli:    "Organizator musun?"        -> token'a bakar, DB'ye gitmez
/// Kaynak bazli: "BU etkinligin sahibi misin?" -> DB'ye BAKMAK ZORUNDA
///
/// Ikincisi olmadan su acik olusur: Organizator rolune sahip herkes,
/// BASKA organizatorlerin etkinliklerini duzenleyebilir. Rakip bir
/// organizatorun konserini iptal edebilir.
///
/// [Authorize(Roles = "Organizer")] bunu ENGELLEYEMEZ -- cunku token
/// yalnizca "bu kisi organizator" der, "bu etkinlik onun" demez.
/// ==================================================================
///
/// NEDEN Application katmaninda degil de BURADA?
/// Handler icinde de kontrol edebilirdik. Ama o zaman her handler'da
/// tekrar yazmamiz gerekirdi ve birinde unutmak yeterdi. Burada,
/// endpoint'e girmeden once calisiyor ve unutulmasi imkansiz --
/// policy adini yazmayi unutursan endpoint zaten korumasiz kalir ve
/// bu code review'da hemen gorunur.
/// </summary>
internal sealed class EventOwnerAuthorizationHandler
    : AuthorizationHandler<EventOwnerRequirement>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IServiceScopeFactory _scopeFactory;

    public EventOwnerAuthorizationHandler(
        IHttpContextAccessor httpContextAccessor,
        IServiceScopeFactory scopeFactory)
    {
        _httpContextAccessor = httpContextAccessor;
        _scopeFactory = scopeFactory;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        EventOwnerRequirement requirement)
    {
        var httpContext = _httpContextAccessor.HttpContext;

        if (httpContext is null)
        {
            return;   // Basarisiz sayilir; Succeed cagrilmadi.
        }

        // ---- Admin her seye erisir ----
        //
        // Bu satir olmasaydi admin, uygunsuz bir etkinligi askiya
        // alamazdi -- cunku sahibi degil. Destek islerini yapamazdi.
        if (context.User.IsInRole(Role.Names.Admin))
        {
            context.Succeed(requirement);

            return;
        }

        var currentUserId = GetUserId(context);

        if (currentUserId is null)
        {
            return;
        }

        // Etkinlik Id'sini route'tan okuyoruz: /api/v1/events/{id}
        if (!httpContext.Request.RouteValues.TryGetValue("id", out var routeValue) ||
            !Guid.TryParse(routeValue?.ToString(), out var eventId))
        {
            return;
        }

        // ==============================================================
        // KENDI KAPSAMIMI (scope) OLUSTURUYORUM
        // ==============================================================
        // AuthorizationHandler SINGLETON olarak kaydedilir; DbContext ise
        // SCOPED. Singleton bir servise scoped bagimlilik enjekte etmek
        // "captive dependency" hatasidir: DbContext uygulama omru boyunca
        // yasar, baglantiyi tutar ve es zamanli isteklerde bozulur.
        //
        // IServiceScopeFactory ile istek basina kendi kapsamimi acip
        // kapatiyorum. Bu, singleton'dan scoped servise erismenin
        // dogru yolu.
        // ==============================================================
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        // Kullanicinin organizator profili -> etkinligin sahibi mi?
        //
        // Tek sorguda birlestiriyorum: iki ayri sorgu yapsaydik
        // (once profil, sonra etkinlik) her yetkilendirmede iki
        // gidis-donus olurdu.
        var isOwner = await dbContext.Events
            .AsNoTracking()
            .AnyAsync(e => e.Id == eventId
                        && dbContext.OrganizerProfiles
                            .Any(p => p.Id == e.OrganizerId && p.UserId == currentUserId.Value))
            .ConfigureAwait(false);

        if (isOwner)
        {
            context.Succeed(requirement);
        }

        // Sahip degilse Succeed CAGIRMIYORUZ -> yetkilendirme basarisiz
        // -> 403 Forbidden.
        //
        // context.Fail() de cagirabilirdik ama o, DIGER handler'larin
        // basarili olmasini da engeller. Sessizce basarisiz olmak,
        // ileride bu policy'ye alternatif bir yol eklersek (ornegin
        // "etkinlige atanmis yardimci kullanici") onun calismasina
        // izin verir.
    }

    private static Guid? GetUserId(AuthorizationHandlerContext context)
    {
        var value = context.User.FindFirst(
                        Microsoft.IdentityModel.JsonWebTokens.JwtRegisteredClaimNames.Sub)?.Value
                 ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(value, out var id) ? id : null;
    }
}
