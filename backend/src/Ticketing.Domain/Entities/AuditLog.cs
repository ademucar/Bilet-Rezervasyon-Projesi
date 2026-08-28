using Ticketing.Domain.Common;

namespace Ticketing.Domain.Entities;

/// <summary>
/// Denetim kaydi. PDF sayfa 5: "Admin audit log kayitlarini inceleyebilir."
///
/// APPEND-ONLY bir tablodur: kayitlar eklenir, ASLA guncellenmez veya
/// silinmez. Bu yuzden AuditableEntity'den turemiyor -- UpdatedAt,
/// IsDeleted gibi alanlarin burada anlami yok. Bir denetim kaydinin
/// degistirilebilir olmasi, denetim fikrinin kendisini gecersiz kilar.
/// </summary>
public class AuditLog : Entity
{
    private AuditLog()
    {
        EntityName = string.Empty;
        Action = string.Empty;
    }

    /// <summary>Hangi tablo/entity. Ornek: "TicketType".</summary>
    public string EntityName { get; private set; }

    public Guid EntityId { get; private set; }

    /// <summary>Ne yapildi. Ornek: "PriceChanged", "EventPublished".</summary>
    public string Action { get; private set; }

    /// <summary>
    /// Degisiklik oncesi ve sonrasi degerler (JSON).
    ///
    /// PDF Sprint 6: "Satis baslamis bilet turunun fiyati degistirilirse
    /// degisiklik loglanmalidir." Iste o log burada, eski ve yeni fiyatla.
    /// </summary>
    public string? OldValues { get; private set; }

    public string? NewValues { get; private set; }

    /// <summary>
    /// Islemi yapan. null olabilir: background job'lar da kayit uretir.
    /// </summary>
    public Guid? UserId { get; private set; }

    public string? IpAddress { get; private set; }

    /// <summary>
    /// PDF Sprint 16: Correlation ID. Bu denetim kaydini, onu tetikleyen
    /// HTTP istegine ve o istegin tum loglarina baglar.
    /// </summary>
    public string? CorrelationId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static AuditLog Create(
        string entityName,
        Guid entityId,
        string action,
        Guid? userId = null,
        string? oldValues = null,
        string? newValues = null,
        string? ipAddress = null,
        string? correlationId = null)
        => new()
        {
            EntityName = entityName,
            EntityId = entityId,
            Action = action,
            UserId = userId,
            OldValues = oldValues,
            NewValues = newValues,
            IpAddress = ipAddress,
            CorrelationId = correlationId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
}
