using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ticketing.Domain.Entities;

namespace Ticketing.Persistence.Configurations;

internal sealed class CityConfiguration : IEntityTypeConfiguration<City>
{
    public void Configure(EntityTypeBuilder<City> builder)
    {
        builder.ToTable("Cities");
        builder.HasKey(c => c.Id);

        builder.ConfigureAuditFields();

        builder.Property(c => c.Name).HasMaxLength(100).IsRequired();

        builder.HasIndex(c => c.Name).IsUnique().HasFilter("\"IsDeleted\" = false");
        builder.HasIndex(c => c.PlateCode).IsUnique().HasFilter("\"IsDeleted\" = false");
    }
}

internal sealed class VenueConfiguration : IEntityTypeConfiguration<Venue>
{
    public void Configure(EntityTypeBuilder<Venue> builder)
    {
        builder.ToTable("Venues");
        builder.HasKey(v => v.Id);

        builder.ConfigureAuditFields();

        builder.Property(v => v.Name).HasMaxLength(200).IsRequired();
        builder.Property(v => v.Address).HasMaxLength(500).IsRequired();

        // Koordinatlar için numeric kullanıyorum, double DEĞİL.
        //
        // Enlem/boylam için double "yeterince iyi" gorulur ama numeric(9,6)
        // ~11 cm hassasiyet verir ve YUVARLAMA HATASI YAPMAZ. Aynı koordinat
        // yazilip okundugunda birebir aynı değeri döndürür; double'da
        // son basamaklarda kayma olabilir ve "bu mekan tasindi mi?" gibi
        // karsilastirmalar yanıltıcı sonuç verir.
        builder.Property(v => v.Latitude).HasColumnType("numeric(9,6)");
        builder.Property(v => v.Longitude).HasColumnType("numeric(9,6)");

        builder.HasOne(v => v.City)
               .WithMany()
               .HasForeignKey(v => v.CityId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(v => v.Halls)
               .WithOne(h => h.Venue)
               .HasForeignKey(h => h.VenueId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(v => v.CityId);
    }
}

internal sealed class HallConfiguration : IEntityTypeConfiguration<Hall>
{
    public void Configure(EntityTypeBuilder<Hall> builder)
    {
        builder.ToTable("Halls");
        builder.HasKey(h => h.Id);

        builder.ConfigureAuditFields();

        builder.Property(h => h.Name).HasMaxLength(150).IsRequired();

        builder.HasMany(h => h.SeatLayouts)
               .WithOne(sl => sl.Hall)
               .HasForeignKey(sl => sl.HallId)
               .OnDelete(DeleteBehavior.Restrict);

        // Aynı mekanda aynı isimde iki salon olamaz.
        builder.HasIndex(h => new { h.VenueId, h.Name })
               .IsUnique()
               .HasFilter("\"IsDeleted\" = false");
    }
}

internal sealed class SeatLayoutConfiguration : IEntityTypeConfiguration<SeatLayout>
{
    public void Configure(EntityTypeBuilder<SeatLayout> builder)
    {
        builder.ToTable("SeatLayouts");
        builder.HasKey(sl => sl.Id);

        builder.ConfigureAuditFields();

        builder.Property(sl => sl.Name).HasMaxLength(150).IsRequired();
        builder.Property(sl => sl.Description).HasMaxLength(1000);

        builder.HasMany(sl => sl.Sections)
               .WithOne(ss => ss.SeatLayout)
               .HasForeignKey(ss => ss.SeatLayoutId)
               .OnDelete(DeleteBehavior.Cascade);

        // PDF is kuralı (sayfa 11):
        // "Aynı salonda aynı isimde iki oturma planı bulunmamalidir."
        //
        // Bu kural SeatLayout.AddSection içinde de kontrol ediliyor ama
        // orasi yalnızca BELLEKTEKI koleksiyona bakabiliyor. Iki kullanıcı
        // aynı anda plan eklerse ikisi de çakışma gormez. Bu index son
        // savunma hattidir.
        builder.HasIndex(sl => new { sl.HallId, sl.Name })
               .IsUnique()
               .HasFilter("\"IsDeleted\" = false");
    }
}

internal sealed class SeatSectionConfiguration : IEntityTypeConfiguration<SeatSection>
{
    public void Configure(EntityTypeBuilder<SeatSection> builder)
    {
        builder.ToTable("SeatSections");
        builder.HasKey(ss => ss.Id);

        builder.ConfigureAuditFields();

        builder.Property(ss => ss.Name).HasMaxLength(100).IsRequired();
        builder.Property(ss => ss.ColorHex).HasMaxLength(7);   // "#RRGGBB"

        builder.HasMany(ss => ss.Seats)
               .WithOne(s => s.SeatSection)
               .HasForeignKey(s => s.SeatSectionId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(ss => new { ss.SeatLayoutId, ss.Name })
               .IsUnique()
               .HasFilter("\"IsDeleted\" = false");
    }
}

internal sealed class SeatConfiguration : IEntityTypeConfiguration<Seat>
{
    public void Configure(EntityTypeBuilder<Seat> builder)
    {
        builder.ToTable("Seats");
        builder.HasKey(s => s.Id);

        builder.ConfigureAuditFields();

        builder.Property(s => s.RowLabel).HasMaxLength(10).IsRequired();

        // PDF is kuralı (sayfa 11):
        // "Aynı bolumde aynı sıra ve koltuk numarasi tekrar edemez."
        builder.HasIndex(s => new { s.SeatSectionId, s.RowLabel, s.SeatNumber })
               .IsUnique()
               .HasFilter("\"IsDeleted\" = false")
               .HasDatabaseName("ix_seats_section_row_number");
    }
}
