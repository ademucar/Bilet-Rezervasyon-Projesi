using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ticketing.Domain.Entities;

namespace Ticketing.Persistence.Configurations;

/// <summary>
/// Kimlik ve yetkilendirme tablolarinin EF eslestirmeleri.
///
/// Konfigurasyonlari AGREGA bazinda grupladim (kimlik / mekan / etkinlik /
/// rezervasyon / destek). Her sinif için ayrı dosya da yazilabilirdi ama
/// 28 dosya arasında dolasmak yerine ilgili olanlari yan yana gormek
/// bakimi kolaylastiriyor -- ozellikle iliskili tablolarin FK ve index
/// tanimlarini karsilastirirken.
/// </summary>
internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(u => u.Id);

        builder.ConfigureAuditFields();

        builder.Property(u => u.Email).HasMaxLength(256).IsRequired();
        builder.Property(u => u.PasswordHash).HasMaxLength(512).IsRequired();
        builder.Property(u => u.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(u => u.LastName).HasMaxLength(100).IsRequired();
        builder.Property(u => u.PhoneNumber).HasMaxLength(20);
        builder.Property(u => u.PasswordResetTokenHash).HasMaxLength(128);

        // Şifre sıfırlama tokeni ile kullanıcı arama sorgusu için.
        //
        // Partial index: yalnızca AKTIF talebi olan kullanıcılar index'te.
        // Kullanicilarin %99.9'unda bu alan null olduğu için index
        // neredeyse boş kaliyor -- tabloya yuk bindirmiyor.
        builder.HasIndex(u => u.PasswordResetTokenHash)
               .HasFilter("\"PasswordResetTokenHash\" IS NOT NULL")
               .HasDatabaseName("ix_users_password_reset_token");

        // ------------------------------------------------------------------
        // PARTIAL UNIQUE INDEX -- dikkat edilmesi gereken bir ayrinti
        // ------------------------------------------------------------------
        // Normal bir unique index koysaydık su sorun çıkardı:
        // Bir kullanıcıyı soft delete ile sildikten sonra AYNI e-postayla
        // yeni kayıt acilamazdi. Çünkü silinmis satır hâlâ index'te yer
        // tutuyor olurdu.
        //
        // HasFilter ile index'i yalnızca silinmemis satirlara uyguluyoruz:
        //     CREATE UNIQUE INDEX ... WHERE "IsDeleted" = false
        //
        // Bu, soft delete kullanan TÜM unique index'ler için geçerli bir
        // kural. Atlanirsa hata kullanıcı "hesabimi silip yeniden acmak
        // istiyorum" diyene kadar ortaya cikmaz.
        builder.HasIndex(u => u.Email)
               .IsUnique()
               .HasFilter("\"IsDeleted\" = false")
               .HasDatabaseName("ix_users_email");

        builder.HasMany(u => u.UserRoles)
               .WithOne(ur => ur.User)
               .HasForeignKey(ur => ur.UserId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(u => u.RefreshTokens)
               .WithOne(rt => rt.User)
               .HasForeignKey(rt => rt.UserId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("Roles");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Name).HasMaxLength(50).IsRequired();
        builder.Property(r => r.Description).HasMaxLength(256);

        builder.HasIndex(r => r.Name).IsUnique();

        // Başlangıç verisi (seed). Role.Ids'teki SABIT GUID'ler kullanılıyor.
        //
        // Neden sabit? Guid.CreateVersion7() kullansaydık migration her
        // calistiginda farklı ID üretir, EF "bu veri degismis" diyerek
        // her seferinde yeni migration istemek isterdi. Ayrıca gelistirme,
        // test ve production ortamlarinda Admin rolunun ID'si farklı olurdu.
        builder.HasData(
            new { Id = Role.Ids.User, Name = Role.Names.User },
            new { Id = Role.Ids.Organizer, Name = Role.Names.Organizer },
            new { Id = Role.Ids.Admin, Name = Role.Names.Admin });
    }
}

internal sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        builder.ToTable("UserRoles");

        // COMPOSITE KEY -- PDF: "Composite Key kullanilan tablolar"
        //
        // Bu tablonun kendine ait bir kimliği yok; kimliği iliskilendirdigi
        // iki varligin birlesimidir. Composite key sayesinde aynı kullanıcıya
        // aynı rol IKI KEZ atanamaz -- veritabani seviyesinde garanti.
        //
        // Ayrı bir Id sutunu olsaydı aynı ciftten iki satır olusabilirdi ve
        // engellemek için AYRICA bir unique index gerekirdi.
        builder.HasKey(ur => new { ur.UserId, ur.RoleId });

        builder.HasOne(ur => ur.Role)
               .WithMany(r => r.UserRoles)
               .HasForeignKey(ur => ur.RoleId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("RefreshTokens");
        builder.HasKey(rt => rt.Id);

        builder.Property(rt => rt.TokenHash).HasMaxLength(128).IsRequired();
        builder.Property(rt => rt.ReplacedByTokenHash).HasMaxLength(128);
        builder.Property(rt => rt.CreatedByIp).HasMaxLength(45);   // IPv6 için 45
        builder.Property(rt => rt.RevokedByIp).HasMaxLength(45);

        builder.HasIndex(rt => rt.TokenHash).IsUnique();

        // Kullanıcının aktif token'larini bulmak için.
        // Token calindiginda "bu kullanıcının TÜM token'larini iptal et"
        // sorgusu bu index'i kullanacak.
        builder.HasIndex(rt => new { rt.UserId, rt.ExpiresAt });
    }
}

internal sealed class OrganizerProfileConfiguration : IEntityTypeConfiguration<OrganizerProfile>
{
    public void Configure(EntityTypeBuilder<OrganizerProfile> builder)
    {
        builder.ToTable("OrganizerProfiles");
        builder.HasKey(op => op.Id);

        builder.ConfigureAuditFields();

        builder.Property(op => op.CompanyName).HasMaxLength(200).IsRequired();
        builder.Property(op => op.ContactEmail).HasMaxLength(256).IsRequired();
        builder.Property(op => op.ContactPhone).HasMaxLength(20);
        builder.Property(op => op.TaxNumber).HasMaxLength(20);
        builder.Property(op => op.Website).HasMaxLength(256);
        builder.Property(op => op.LogoPath).HasMaxLength(512);
        builder.Property(op => op.Description).HasMaxLength(2000);

        // 1-1 iliski: bir kullanıcının en fazla bir organizatör profili olur.
        builder.HasOne(op => op.User)
               .WithOne()
               .HasForeignKey<OrganizerProfile>(op => op.UserId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(op => op.UserId).IsUnique();
    }
}

internal sealed class OrganizerApplicationConfiguration : IEntityTypeConfiguration<OrganizerApplication>
{
    public void Configure(EntityTypeBuilder<OrganizerApplication> builder)
    {
        builder.ToTable("OrganizerApplications");
        builder.HasKey(oa => oa.Id);

        builder.ConfigureAuditFields();

        builder.Property(oa => oa.CompanyName).HasMaxLength(200).IsRequired();
        builder.Property(oa => oa.ContactEmail).HasMaxLength(256).IsRequired();
        builder.Property(oa => oa.ContactPhone).HasMaxLength(20);
        builder.Property(oa => oa.TaxNumber).HasMaxLength(20);
        builder.Property(oa => oa.Description).HasMaxLength(2000);
        builder.Property(oa => oa.RejectionReason).HasMaxLength(1000);
        builder.Property(oa => oa.Status).HasConversion<int>();

        builder.HasOne(oa => oa.User)
               .WithMany()
               .HasForeignKey(oa => oa.UserId)
               .OnDelete(DeleteBehavior.Cascade);

        // Admin panelinde "bekleyen basvurular" sorgusu için.
        builder.HasIndex(oa => oa.Status);
    }
}
