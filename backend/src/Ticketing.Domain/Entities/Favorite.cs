using Ticketing.Domain.Common;

namespace Ticketing.Domain.Entities;

/// <summary>
/// Kullanicinin favori etkinligi. PDF Sprint 12.
///
/// UserRole gibi bu da COMPOSITE KEY kullaniyor: (UserId, EventId).
/// PDF kurali: "Ayni kullanici ayni etkinligi bir kez favorileyebilmelidir."
/// Composite key bunu yapisal olarak garanti eder -- ayri bir unique
/// index'e gerek kalmaz.
/// </summary>
public class Favorite
{
    private Favorite()
    {
    }

    public Guid UserId { get; private set; }

    public Guid EventId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public User User { get; private set; } = null!;

    public Event Event { get; private set; } = null!;

    public static Favorite Create(Guid userId, Guid eventId)
        => new()
        {
            UserId = userId,
            EventId = eventId,
            CreatedAt = DateTimeOffset.UtcNow
        };
}
