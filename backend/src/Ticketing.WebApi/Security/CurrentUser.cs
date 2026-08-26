using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using Ticketing.Application.Abstractions.Security;
using Ticketing.WebApi.Middleware;

namespace Ticketing.WebApi.Security;

/// <summary>
/// ICurrentUser'in HTTP tabanli uygulamasi.
///
/// Bu sinif WebApi katmaninda cunku HttpContext'e erisiyor.
/// Application katmani yalnizca ICurrentUser arayuzunu goruyor ve
/// kimligin nereden geldigini (JWT, cerez, API anahtari) bilmiyor.
/// </summary>
internal sealed class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;

    public CurrentUser(IHttpContextAccessor accessor) => _accessor = accessor;

    private ClaimsPrincipal? Principal => _accessor.HttpContext?.User;

    public Guid? UserId
    {
        get
        {
            // JWT'de kullanici kimligi "sub" claim'inde.
            //
            // ASP.NET Core varsayilan olarak "sub" claim'ini
            // ClaimTypes.NameIdentifier'a ESLER. Bu esleme bazen kafa
            // karistirici hatalara yol acar, o yuzden IKISINI DE
            // kontrol ediyorum. Program.cs'te bu eslemeyi kapattik
            // ama savunmayi burada da tutuyorum.
            var value = Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub)
                     ?? Principal?.FindFirstValue(ClaimTypes.NameIdentifier);

            return Guid.TryParse(value, out var id) ? id : null;
        }
    }

    public string? Email
        => Principal?.FindFirstValue(JwtRegisteredClaimNames.Email)
        ?? Principal?.FindFirstValue(ClaimTypes.Email);

    public IReadOnlyCollection<string> Roles
        => Principal?.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList() ?? [];

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public string? CorrelationId
    {
        get
        {
            var context = _accessor.HttpContext;

            if (context is null)
            {
                return null;
            }

            return context.Response.Headers.TryGetValue(
                CorrelationIdMiddleware.HeaderName, out var value)
                ? value.ToString()
                : null;
        }
    }

    /// <summary>
    /// Istegin geldigi IP adresi.
    ///
    /// ==================================================================
    /// GUVENLIK UYARISI -- X-Forwarded-For BURADA OKUNMUYOR
    /// ==================================================================
    /// Uygulama bir reverse proxy (nginx, yuk dengeleyici) arkasindaysa
    /// RemoteIpAddress proxy'nin IP'sini verir, gercek kullanicininkini
    /// degil. Gercek IP "X-Forwarded-For" header'inda gelir.
    ///
    /// AMA o header'i BURADA elle okumak TEHLIKELIDIR: istemci bu
    /// header'i istedigi gibi UYDURABILIR. IP'ye gore kilit veya rate
    /// limit uyguluyorsak, saldirgan her istekte farkli bir IP yazarak
    /// tum korumalari atlatir.
    ///
    /// Dogru yontem: ASP.NET Core'un ForwardedHeaders middleware'ini
    /// GUVENILEN proxy listesiyle yapilandirmak. O zaman framework
    /// header'i yalnizca guvenilen bir proxy'den geldiginde dikkate
    /// alir ve RemoteIpAddress'i dogru degerle degistirir.
    ///
    /// Bunu Sprint 15'te (API guvenligi) yapacagiz. O zamana kadar
    /// RemoteIpAddress dogru calisiyor cunku proxy yok.
    /// ==================================================================
    /// </summary>
    public string? IpAddress
        => _accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
}
