using Ticketing.Application.Abstractions.Time;

namespace Ticketing.Infrastructure.Time;

/// <summary>
/// Uretimde kullanilan gerçek zaman sağlayıcısı.
/// Testlerde bunun yerine sabit bir zaman donduren sahte bir uygulama gecirilir.
/// </summary>
internal sealed class DateTimeProvider : IDateTimeProvider
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
