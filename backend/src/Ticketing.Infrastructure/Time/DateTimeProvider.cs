using Ticketing.Application.Abstractions.Time;

namespace Ticketing.Infrastructure.Time;

/// <summary>
/// Uretimde kullanilan gercek zaman saglayicisi.
/// Testlerde bunun yerine sabit bir zaman donduren sahte bir uygulama gecirilir.
/// </summary>
internal sealed class DateTimeProvider : IDateTimeProvider
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
