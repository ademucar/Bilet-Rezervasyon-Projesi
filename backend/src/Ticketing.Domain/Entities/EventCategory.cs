using Ticketing.Domain.Common;

namespace Ticketing.Domain.Entities;

/// <summary>
/// Etkinlik kategorisi: Konser, Tiyatro, Konferans, Spor, Festival...
/// PDF sayfa 5: "Kategori, şehir ve salon yönetimi" -- Admin sorumlulugunda.
/// </summary>
public class EventCategory : AuditableEntity
{
    private EventCategory()
    {
        Name = string.Empty;
        Slug = string.Empty;
    }

    private EventCategory(string name, string slug)
    {
        Name = name;
        Slug = slug;
    }

    public string Name { get; private set; }

    /// <summary>
    /// URL dostu kisa ad: "Rock Konseri" -> "rock-konseri".
    ///
    /// Neden gerekli? Etkinlik listesi sayfasinin adresi
    ///     /etkinlikler?kategori=rock-konseri
    /// seklinde olacak. Guid kullansaydım adres
    ///     /etkinlikler?kategori=8f3a...
    /// olurdu; ne kullanıcı okuyabilir ne de arama motoru anlamlandirabilir.
    /// </summary>
    public string Slug { get; private set; }

    /// <summary>Frontend'de gösterilecek ikon adı. Ornek: "music-note".</summary>
    public string? IconName { get; private set; }

    public int DisplayOrder { get; private set; }

    public static EventCategory Create(string name, string slug, string? iconName = null, int displayOrder = 0)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Kategori adı boş olamaz.", "category.name_required");
        }

        if (string.IsNullOrWhiteSpace(slug))
        {
            throw new DomainException("Kategori slug'i boş olamaz.", "category.slug_required");
        }

        return new EventCategory(name.Trim(), slug.Trim().ToLowerInvariant())
        {
            IconName = iconName,
            DisplayOrder = displayOrder,
        };
    }
}
