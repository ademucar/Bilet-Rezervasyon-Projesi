using Ticketing.Application.Behaviors;

namespace Ticketing.Application.Common.Exceptions;

/// <summary>
/// Bir veya daha fazla girdi doğrulama hatası.
/// GlobalExceptionHandler bunu 400 Bad Request + Problem Details'a cevirir.
///
/// Hatalari ALAN BAZINDA gruplandiriyoruz çünkü RFC 7807'nin
/// doğrulama uzantisi bu bicimi bekler:
///
///     {
///       "type": "...", "title": "Doğrulama hatası", "status": 400,
///       "errors": {
///         "Email":    ["Geçerli bir e-posta adresi giriniz."],
///         "Password": ["Şifre en az 8 karakter olmalıdır.",
///                      "Şifre en az bir rakam içermelidir."]
///       }
///     }
///
/// Bu biçim, frontend'in her form alanini kendi hatalariyla
/// eslestirmesini saglar. Duz bir liste donseydik frontend hangi
/// mesajin hangi alana ait olduğunu bilemezdi.
/// </summary>
public sealed class ValidationException : Exception
{
    public ValidationException()
        : base("Bir veya daha fazla doğrulama hatası oluştu.")
        => Errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

    public ValidationException(string message)
        : base(message)
        => Errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

    public ValidationException(string message, Exception innerException)
        : base(message, innerException)
        => Errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

    public ValidationException(IReadOnlyCollection<ValidationError> errors)
        : this()
    {
        ArgumentNullException.ThrowIfNull(errors);

        Errors = errors
            .GroupBy(e => e.PropertyName, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => e.ErrorMessage).ToArray(),
                StringComparer.Ordinal);
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}
