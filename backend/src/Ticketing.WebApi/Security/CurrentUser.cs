using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using Ticketing.Application.Abstractions.Security;
using Ticketing.WebApi.Middleware;

namespace Ticketing.WebApi.Security;

/// <summary>
/// ICurrentUser'in HTTP tabanli uygulamasi.
///
/// Bu sinif WebApi katmaninda çünkü HttpContext'e erisiyor.
/// Application katmani yalnızca ICurrentUser arayuzunu görüyor ve
/// kimligin nereden geldigini (JWT, çerez, API anahtari) bilmiyor.
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
            // JWT'de kullanıcı kimliği "sub" claim'inde.
            //
            // ASP.NET Core varsayılan olarak "sub" claim'ini
            // ClaimTypes.NameIdentifier'a ESLER. Bu esleme bazen kafa
            // karistirici hatalara yol acar, o yüzden IKISINI DE
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

            // ONCE HttpContext.Items -- SONRA response header
            //
            // Eskiden YALNIZCA response header'ina bakiyordu ve bu
            // SESSIZ bir hataydi: header'i CorrelationIdMiddleware
            // OnStarting içinde yazıyor, o da handler CALISTIKTAN
            // SONRA tetikleniyor.
            //
            // Yani istek islenirken burasi her zaman null donuyordu
            // ve Outbox / AuditLog kayitlarina hiçbir zaman
            // correlation ID yazilmadi. Hicbir hata olusmadi, hiçbir
            // test kirilmadi -- sadece sutunlar boş kaldı.
            //
            // Items middleware'in ilk satirinda dolduruluyor;
            // handler önü görüyor.
            //
            // Header'a bakan dal FALLBACK olarak duruyor: yanit
            // yazilmaya baslandiktan sonra cagrilan kodlar (örneğin
            // istek özeti logu) için hâlâ geçerli bir kaynak.
            if (context.Items.TryGetValue(
                    CorrelationIdMiddleware.HeaderName, out var item)
                && item is string fromItems
                && !string.IsNullOrEmpty(fromItems))
            {
                return fromItems;
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
    /// GÜVENLİK UYARISI -- X-Forwarded-For BURADA OKUNMUYOR
    ///
    /// Uygulama bir reverse proxy (nginx, yuk dengeleyici) arkasindaysa
    /// RemoteIpAddress proxy'nin IP'sini verir, gerçek kullanicininkini
    /// değil. Gerçek IP "X-Forwarded-For" header'inda gelir.
    ///
    /// AMA o header'i BURADA elle okumak TEHLIKELIDIR: istemci bu
    /// header'i istedigi gibi UYDURABILIR. IP'ye göre kilit veya rate
    /// limit uyguluyorsak, saldirgan her istekte farklı bir IP yazarak
    /// tüm korumalari atlatir.
    ///
    /// Dogru yontem: ASP.NET Core'un ForwardedHeaders middleware'ini
    /// GUVENILEN proxy listesiyle yapilandirmak. O zaman framework
    /// header'i yalnızca guvenilen bir proxy'den geldiğinde dikkate
    /// alır ve RemoteIpAddress'i doğru degerle değiştirir.
    ///
    /// Bunu Sprint 15'te (API guvenligi) yapacagim. O zamana kadar
    /// RemoteIpAddress doğru çalışıyor çünkü proxy yok.
    /// </summary>
    public string? IpAddress
        => _accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
}
