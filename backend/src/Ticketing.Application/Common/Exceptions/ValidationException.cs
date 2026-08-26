using Ticketing.Application.Behaviors;

namespace Ticketing.Application.Common.Exceptions;

/// <summary>
/// Bir veya daha fazla girdi dogrulama hatasi.
/// GlobalExceptionHandler bunu 400 Bad Request + Problem Details'a cevirir.
///
/// Hatalari ALAN BAZINDA gruplandiriyoruz cunku RFC 7807'nin
/// dogrulama uzantisi bu bicimi bekler:
///
///     {
///       "type": "...", "title": "Dogrulama hatasi", "status": 400,
///       "errors": {
///         "Email":    ["Gecerli bir e-posta adresi giriniz."],
///         "Password": ["Sifre en az 8 karakter olmalidir.",
///                      "Sifre en az bir rakam icermelidir."]
///       }
///     }
///
/// Bu bicim, frontend'in her form alanini kendi hatalariyla
/// eslestirmesini saglar. Duz bir liste donseydik frontend hangi
/// mesajin hangi alana ait oldugunu bilemezdi.
/// </summary>
public sealed class ValidationException : Exception
{
    public ValidationException()
        : base("Bir veya daha fazla dogrulama hatasi olustu.")
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
