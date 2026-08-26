namespace Ticketing.Application.Abstractions.Security;

/// <summary>
/// Istegi yapan kullanicinin kimligi.
///
/// Neden HttpContext'i dogrudan kullanmiyoruz?
/// Cunku HttpContext ASP.NET Core'a aittir ve Application katmani
/// web'i bilmemelidir -- architecture testimiz bunu zaten engelliyor.
///
/// Bu arayuz sayesinde handler'lar "su an kim istekte bulunuyor?"
/// sorusunu HTTP'den bagimsiz olarak sorabiliyor. Testte de sahte
/// bir kullanici vermek cok kolay oluyor.
/// </summary>
public interface ICurrentUser
{
    /// <summary>Kimligi dogrulanmamissa null.</summary>
    Guid? UserId { get; }

    string? Email { get; }

    IReadOnlyCollection<string> Roles { get; }

    bool IsAuthenticated { get; }

    /// <summary>Bu istegin izleme kimligi (Correlation ID).</summary>
    string? CorrelationId { get; }

    string? IpAddress { get; }
}
