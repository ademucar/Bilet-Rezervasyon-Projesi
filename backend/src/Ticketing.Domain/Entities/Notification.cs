using Ticketing.Domain.Common;
using Ticketing.Domain.Enums;

namespace Ticketing.Domain.Entities;

/// <summary>
/// Uygulama ici bildirim. PDF Sprint 14.
/// E-posta AYRI bir kanaldir ve Outbox uzerinden gider; bu tablo
/// yalnizca uygulama icindeki zil ikonunun icerigini tutar.
/// </summary>
public class Notification : Entity
{
    private Notification()
    {
        Title = string.Empty;
        Message = string.Empty;
    }

    public Guid UserId { get; private set; }

    public NotificationType Type { get; private set; }

    public string Title { get; private set; }

    public string Message { get; private set; }

    /// <summary>
    /// Tiklandiginda gidilecek uygulama ici yol. Ornek: "/biletlerim/8f3a".
    /// Tam URL degil gorece yol: alan adi degisirse veriler bozulmasin.
    /// </summary>
    public string? ActionPath { get; private set; }

    /// <summary>
    /// Ilgili kaydin Id'si (rezervasyon, bilet, etkinlik...).
    /// Tur bilgisi Type alaninda oldugu icin ayrica EntityType tutmuyorum.
    /// </summary>
    public Guid? RelatedEntityId { get; private set; }

    public bool IsRead { get; private set; }

    public DateTimeOffset? ReadAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public User User { get; private set; } = null!;

    public static Notification Create(
        Guid userId,
        NotificationType type,
        string title,
        string message,
        Guid? relatedEntityId = null,
        string? actionPath = null)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainException("Bildirim basligi bos olamaz.", "notification.title_required");
        }

        return new Notification
        {
            UserId = userId,
            Type = type,
            Title = title,
            Message = message ?? string.Empty,
            RelatedEntityId = relatedEntityId,
            ActionPath = actionPath,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public void MarkAsRead(DateTimeOffset now)
    {
        if (IsRead)
        {
            // Idempotent: ilk okuma zamanini KORUYORUM.
            // Ustune yazsaydim "ne zaman okudu" bilgisi bozulurdu.
            return;
        }

        IsRead = true;
        ReadAt = now;
    }
}
