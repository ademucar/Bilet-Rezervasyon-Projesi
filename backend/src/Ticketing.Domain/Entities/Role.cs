using Ticketing.Domain.Common;

namespace Ticketing.Domain.Entities;

/// <summary>
/// Sistem rolü. PDF sayfa 4-5: Kullanıcı, Organizatör, Admin.
/// </summary>
public class Role : Entity
{
    private Role() => Name = string.Empty;

    private Role(Guid id, string name)
    {
        Id = id;
        Name = name;
    }

    public string Name { get; private set; }

    public string? Description { get; private set; }

    private readonly List<UserRole> _userRoles = [];

    public IReadOnlyCollection<UserRole> UserRoles => _userRoles.AsReadOnly();

    // ---------------------------------------------------------------
    // Sabit roller
    // ---------------------------------------------------------------

    /// <summary>
    /// Rol isimleri. Kod içinde "Admin" diye metin yazmak yerine
    /// RoleNames.Admin kullanacagiz.
    ///
    /// Neden? Metin yazarsan yazım hatası derleme zamaninda YAKALANMAZ.
    /// [Authorize(Roles = "Adnim")] yazdiginda kod derlenir, çalışır ve
    /// hiçbir admin o endpoint'e giremez. Hatayi bulmak saatler alır.
    /// Sabit kullandiginda derleyici seni korur.
    /// </summary>
    public static class Names
    {
        public const string User = "User";
        public const string Organizer = "Organizer";
        public const string Admin = "Admin";
    }

    /// <summary>
    /// Rollerin ID'lerini SABIT tutuyorum, rastgele uretmiyorum.
    ///
    /// Sebep: Seed data (başlangıç verisi) her ortamda aynı olmalı.
    /// Guid.CreateVersion7() kullansaydim, migration her calistiginda
    /// farklı ID üretirdi ve EF Core "bu veri degismis" diyerek her
    /// seferinde yeni bir migration olusturmak isterdi.
    ///
    /// Ayrıca gelistirme, test ve production ortamlarinda Admin rolunun
    /// ID'si farklı olurdu; veri tasima ve hata ayiklama zorlasirdi.
    ///
    /// Bunlar elle yazilmis sabit GUID'ler -- "well-known ID" denir.
    /// </summary>
    public static class Ids
    {
        public static readonly Guid User = new("11111111-1111-1111-1111-111111111111");
        public static readonly Guid Organizer = new("22222222-2222-2222-2222-222222222222");
        public static readonly Guid Admin = new("33333333-3333-3333-3333-333333333333");
    }

    public static Role Create(Guid id, string name, string? description = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Rol adı boş olamaz.", "role.name_required");
        }

        return new Role(id, name.Trim()) { Description = description };
    }
}
