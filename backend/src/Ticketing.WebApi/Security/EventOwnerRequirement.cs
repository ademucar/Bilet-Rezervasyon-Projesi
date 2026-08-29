using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Abstractions.Persistence;
using Ticketing.Domain.Entities;

namespace Ticketing.WebApi.Security;

/// <summary>
/// "Bu etkinliğin sahibi misin?" gereksinimi.
///
/// PDF Sprint 3: "Resource based authorization uygulanmalıdır."
/// PDF Sprint 5: "Sadece kendi etkinliklerini guncelleyebilir."
/// </summary>
public sealed class EventOwnerRequirement : IAuthorizationRequirement;

/// <summary>
/// ROL BAZLI ile KAYNAK BAZLI YETKILENDIRME ARASINDAKI FARK
///
/// Rol bazlı:    "Organizatör musun?"        -> token'a bakar, DB'ye gitmez
/// Kaynak bazlı: "BU etkinliğin sahibi misin?" -> DB'ye BAKMAK ZORUNDA
///
/// Ikincisi olmadan su açık olusur: Organizatör rolune sahip herkes,
/// BASKA organizatorlerin etkinliklerini düzenleyebilir. Rakip bir
/// organizatorun konserini iptal edebilir.
///
/// [Authorize(Roles = "Organizer")] bunu ENGELLEYEMEZ -- çünkü token
/// yalnızca "bu kişi organizatör" der, "bu etkinlik onun" demez.
///
/// NEDEN Application katmaninda değil de BURADA?
/// Handler içinde de kontrol edebilirdik. Ama o zaman her handler'da
/// tekrar yazmamiz gerekirdi ve birinde unutmak yeterdi. Burada,
/// endpoint'e girmeden önce çalışıyor ve unutulmasi imkansiz --
/// policy adını yazmayi unutursan endpoint zaten korumasiz kalır ve
/// bu code review'da hemen görünür.
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
            return;   // Başarısız sayilir; Succeed cagrilmadi.
        }

        // ---- Admin her seye erisir ----
        //
        // Bu satır olmasaydı admin, uygunsuz bir etkinligi askiya
        // alamazdi -- çünkü sahibi değil. Destek islerini yapamazdi.
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

        // Etkinlik Id'sini route'tan okuyorum: /api/v1/events/{id}
        if (!httpContext.Request.RouteValues.TryGetValue("id", out var routeValue) ||
            !Guid.TryParse(routeValue?.ToString(), out var eventId))
        {
            return;
        }

        // KENDİ KAPSAMIMI (scope) OLUSTURUYORUM
        //
        // AuthorizationHandler SINGLETON olarak kaydedilir; DbContext ise
        // SCOPED. Singleton bir servise scoped bagimlilik enjekte etmek
        // "captive dependency" hatasidir: DbContext uygulama omru boyunca
        // yasar, baglantiyi tutar ve es zamanlı isteklerde bozulur.
        //
        // IServiceScopeFactory ile istek başına kendi kapsamimi acip
        // kapatiyorum. Bu, singleton'dan scoped servise erismenin
        // doğru yolu.
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        // Kullanıcının organizatör profili -> etkinliğin sahibi mi?
        //
        // Tek sorguda birlestiriyorum: iki ayrı sorgu yapsaydim
        // (önce profil, sonra etkinlik) her yetkilendirmede iki
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

        // Sahip degilse Succeed CAGIRMIYORUZ -> yetkilendirme başarısız
        // -> 403 Forbidden.
        //
        // context.Fail() de cagirabilirdik ama o, DIGER handler'larin
        // başarılı olmasini da engeller. Sessizce başarısız olmak,
        // ileride bu policy'ye alternatif bir yol eklersek (örneğin
        // "etkinlige atanmis yardimci kullanıcı") onun calismasina
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
