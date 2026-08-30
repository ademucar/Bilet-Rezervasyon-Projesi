using Ticketing.Domain.Common;

namespace Ticketing.Domain.Entities;

/// <summary>
/// Etkinlik yorumu ve puanı. PDF Sprint 12.
///
/// Is kurallari (PDF sayfa 20):
///   - Puan 1-5 arasında olmalı            -> burada kontrol ediliyor
///   - Kullanıcı etkinlik başına BIR yorum -> UNIQUE (UserId, EventId)
///   - Yalnızca geçerli bilet almis kullanıcı yorum yapabilir
///   - Etkinlik tamamlanmadan yorum yapılamaz
///
/// Son iki kural burada kontrol edilemez: bilet ve etkinlik durumu
/// bilgisi bu entity'de yok, veritabaninda. Onlar Application katmanindaki
/// handler'da kontrol edilecek. Bunu acikca yazıyorum ki "neden burada
/// yok?" sorusu havada kalmasin.
/// </summary>
public class Review : AuditableEntity
{
    private Review() => Comment = string.Empty;

    public Guid UserId { get; private set; }

    public Guid EventId { get; private set; }

    /// <summary>1-5 arasi puan.</summary>
    public int Rating { get; private set; }

    public string Comment { get; private set; }

    /// <summary>
    /// Admin tarafından gizlendi mi?
    /// PDF: "Admin uygunsuz yorumu kaldirabilir."
    ///
    /// Silmek yerine gizliyoruz (soft delete zaten var, bu ayrı bir bayrak):
    /// boylece denetim izi kalır ve kullanıcı "yorumum nerede?" derse
    /// cevap verebiliriz.
    /// </summary>
    public bool IsHidden { get; private set; }

    public string? HiddenReason { get; private set; }

    public User User { get; private set; } = null!;

    public Event Event { get; private set; } = null!;

    public static Review Create(Guid userId, Guid eventId, int rating, string comment)
    {
        ValidateRating(rating);

        return new Review
        {
            UserId = userId,
            EventId = eventId,
            Rating = rating,
            Comment = comment?.Trim() ?? string.Empty,
        };
    }

    private static void ValidateRating(int rating)
    {
        if (rating is < 1 or > 5)
        {
            throw new DomainException("Puan 1 ile 5 arasında olmalıdır.", "review.invalid_rating");
        }
    }

    /// <summary>
    /// PDF: "Kullanıcı yalnızca kendi yorumunu düzenleyebilir."
    /// Sahiplik kontrolü Application katmanindaki ReviewOwner policy'sinde
    /// yapilacak; burada sadece veri kurallari var.
    /// </summary>
    public void Update(int rating, string comment)
    {
        if (IsHidden)
        {
            throw new DomainException(
                "Gizlenmis yorum düzenlenemez.",
                "review.hidden");
        }

        ValidateRating(rating);

        Rating = rating;
        Comment = comment?.Trim() ?? string.Empty;
    }

    public void Hide(string? reason)
    {
        IsHidden = true;
        HiddenReason = reason;
    }

    public void Unhide()
    {
        IsHidden = false;
        HiddenReason = null;
    }
}
