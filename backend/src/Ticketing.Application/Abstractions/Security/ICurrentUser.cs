namespace Ticketing.Application.Abstractions.Security;

/// <summary>
/// Istegi yapan kullanıcının kimliği.
///
/// Neden HttpContext'i doğrudan kullanmiyorum?
/// Çünkü HttpContext ASP.NET Core'a aittir ve Application katmani
/// web'i bilmemelidir -- architecture testim bunu zaten engelliyor.
///
/// Bu arayüz sayesinde handler'lar "su an kim istekte bulunuyor?"
/// sorusunu HTTP'den bağımsız olarak sorabiliyor. Testte de sahte
/// bir kullanıcı vermek çok kolay oluyor.
/// </summary>
public interface ICurrentUser
{
    /// <summary>Kimligi dogrulanmamissa null.</summary>
    Guid? UserId { get; }

    string? Email { get; }

    IReadOnlyCollection<string> Roles { get; }

    bool IsAuthenticated { get; }

    /// <summary>Bu istegin izleme kimliği (Correlation ID).</summary>
    string? CorrelationId { get; }

    string? IpAddress { get; }
}
