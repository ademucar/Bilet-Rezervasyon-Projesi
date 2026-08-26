using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ticketing.Domain.Entities;
using Ticketing.Domain.ValueObjects;

namespace Ticketing.Persistence.Configurations;

internal sealed class EventCategoryConfiguration : IEntityTypeConfiguration<EventCategory>
{
    public void Configure(EntityTypeBuilder<EventCategory> builder)
    {
        builder.ToTable("EventCategories");
        builder.HasKey(c => c.Id);

        builder.ConfigureAuditFields();

        builder.Property(c => c.Name).HasMaxLength(100).IsRequired();
        builder.Property(c => c.Slug).HasMaxLength(100).IsRequired();
        builder.Property(c => c.IconName).HasMaxLength(50);

        builder.HasIndex(c => c.Slug).IsUnique().HasFilter("\"IsDeleted\" = false");
    }
}

internal sealed class EventConfiguration : IEntityTypeConfiguration<Event>
{
    public void Configure(EntityTypeBuilder<Event> builder)
    {
        builder.ToTable("Events");
        builder.HasKey(e => e.Id);

        builder.ConfigureAuditFields();
        builder.ConfigureConcurrencyToken();

        builder.Property(e => e.Title).HasMaxLength(250).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(5000).IsRequired();
        builder.Property(e => e.PosterImagePath).HasMaxLength(512);
        builder.Property(e => e.CancellationReason).HasMaxLength(1000);
        builder.Property(e => e.Status).HasConversion<int>();

        // ------------------------------------------------------------------
        // CancellationPolicy -> jsonb
        // ------------------------------------------------------------------
        // Uc ayri sutun (FullRefundHours, PartialRefundHours, Percentage)
        // yerine tek bir jsonb sutunu kullaniyorum.
        //
        // Neden? Bu uc deger BIRBIRINE BAGLI ve birlikte anlam tasiyor.
        // Ileride politikaya yeni bir kural eklersek (ornegin "VIP biletler
        // icin farkli oran") migration gerektirmeden jsonb icine
        // ekleyebiliriz.
        //
        // Ne zaman jsonb kullanilmaz? Uzerinde sorgu/filtre yapilacaksa.
        // Iade politikasina gore etkinlik filtrelemeyecegiz, o yuzden
        // jsonb burada dogru tercih.
        builder.OwnsOne(e => e.CancellationPolicy, policy =>
        {
            policy.ToJson("CancellationPolicy");
        });

        builder.HasOne(e => e.Category)
               .WithMany()
               .HasForeignKey(e => e.CategoryId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.City)
               .WithMany()
               .HasForeignKey(e => e.CityId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Venue)
               .WithMany()
               .HasForeignKey(e => e.VenueId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Hall)
               .WithMany()
               .HasForeignKey(e => e.HallId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(e => e.Sessions)
               .WithOne(s => s.Event)
               .HasForeignKey(s => s.EventId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(e => e.TicketTypes)
               .WithOne(t => t.Event)
               .HasForeignKey(t => t.EventId)
               .OnDelete(DeleteBehavior.Restrict);

        // ------------------------------------------------------------------
        // INDEX'LER -- docs/01-is-analizi.md soru 16
        // ------------------------------------------------------------------

        // Ana listeleme sorgusu: WHERE Status = SalesOpen ORDER BY EventDate
        builder.HasIndex(e => new { e.Status, e.EventDate })
               .HasDatabaseName("ix_events_status_date");

        // Filtreleme kombinasyonu: sehir + kategori + tarih
        //
        // Sutun SIRASI onemli: PostgreSQL composite index'i soldan saga
        // kullanir. (CityId, CategoryId, EventDate) index'i
        //   - sadece CityId ile        -> KULLANILIR
        //   - CityId + CategoryId ile  -> KULLANILIR
        //   - sadece CategoryId ile    -> KULLANILMAZ
        // Kullanicilar once sehir sectigi icin CityId'yi basa koydum.
        builder.HasIndex(e => new { e.CityId, e.CategoryId, e.EventDate })
               .HasDatabaseName("ix_events_city_category_date");

        // Organizator paneli: "benim etkinliklerim"
        builder.HasIndex(e => e.OrganizerId);
    }
}

internal sealed class EventSessionConfiguration : IEntityTypeConfiguration<EventSession>
{
    public void Configure(EntityTypeBuilder<EventSession> builder)
    {
        builder.ToTable("EventSessions");
        builder.HasKey(s => s.Id);

        builder.ConfigureAuditFields();
        builder.ConfigureConcurrencyToken();

        builder.Property(s => s.Status).HasConversion<int>();

        builder.HasMany(s => s.EventSeats)
               .WithOne(es => es.EventSession)
               .HasForeignKey(es => es.EventSessionId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(s => new { s.EventId, s.StartDate });

        // PDF: "Ayni salon ayni zaman araliginda iki etkinlige atanamaz."
        //
        // Bu index cakisma sorgusunu hizlandirir:
        //     WHERE HallId = @hall AND StartDate < @end AND EndDate > @start
        //
        // NOT: Index tek basina kurali GARANTI ETMEZ -- sadece hizlandirir.
        // Tam garanti icin PostgreSQL'in EXCLUDE constraint'i gerekiyor:
        //     EXCLUDE USING gist (HallId WITH =, tsrange(StartDate, EndDate) WITH &&)
        // Bunu Sprint 5'te ham SQL migration'i olarak ekleyecegiz;
        // EF Core bu constraint tipini fluent API ile desteklemiyor.
        builder.HasIndex(s => new { s.HallId, s.StartDate, s.EndDate })
               .HasDatabaseName("ix_event_sessions_hall_period");
    }
}

internal sealed class TicketTypeConfiguration : IEntityTypeConfiguration<TicketType>
{
    public void Configure(EntityTypeBuilder<TicketType> builder)
    {
        builder.ToTable("TicketTypes");
        builder.HasKey(t => t.Id);

        builder.ConfigureAuditFields();

        builder.Property(t => t.Name).HasMaxLength(100).IsRequired();

        builder.ConfigureMoney(t => t.Price, "Price_");

        // Ayni etkinlikte ayni isimde iki bilet turu olamaz.
        builder.HasIndex(t => new { t.EventId, t.Name })
               .IsUnique()
               .HasFilter("\"IsDeleted\" = false");
    }
}

internal sealed class EventSeatConfiguration : IEntityTypeConfiguration<EventSeat>
{
    public void Configure(EntityTypeBuilder<EventSeat> builder)
    {
        builder.ToTable("EventSeats");
        builder.HasKey(es => es.Id);

        builder.ConfigureAuditFields();

        // ==================================================================
        // BU IKI SATIR PROJENIN EN KRITIK KISMI
        // ==================================================================

        // 1) OPTIMISTIC CONCURRENCY
        // Iki kullanici ayni koltugu ayni anda kilitlemeye calisirsa
        // EF'in urettigi UPDATE su hale gelir:
        //     UPDATE "EventSeats" SET "Status" = 2 ...
        //     WHERE "Id" = @id AND xmin = @okunanDeger
        // Ikinci istek 0 satir gunceller ve DbUpdateConcurrencyException alir.
        builder.ConfigureConcurrencyToken();

        // 2) UNIQUE INDEX -- SON SAVUNMA HATTI
        // PDF sayfa 8: "Ayni etkinlik oturumunda ayni koltuk yalnizca bir
        // kez bulunmalidir."
        //
        // Uygulama kodumuz ne kadar hatali olursa olsun, kac es zamanli
        // istek gelirse gelsin, PostgreSQL ayni oturumda ayni koltuk icin
        // IKINCI BIR SATIR OLUSTURMAZ.
        //
        // Bu index'i silmek, projenin en temel garantisini kaldirmak demektir.
        builder.HasIndex(es => new { es.EventSessionId, es.SeatId })
               .IsUnique()
               .HasFilter("\"IsDeleted\" = false")
               .HasDatabaseName("ix_event_seats_session_seat");

        // ==================================================================

        builder.Property(es => es.Status).HasConversion<int>();

        builder.ConfigureMoney(es => es.Price, "Price_");

        builder.HasOne(es => es.Seat)
               .WithMany()
               .HasForeignKey(es => es.SeatId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(es => es.TicketType)
               .WithMany()
               .HasForeignKey(es => es.TicketTypeId)
               .OnDelete(DeleteBehavior.Restrict);

        // Koltuk haritasi sorgusu: bir oturumun tum koltuklarini durumuyla getir.
        // Bu, sistemdeki EN SIK calisan sorgulardan biri olacak.
        builder.HasIndex(es => new { es.EventSessionId, es.Status })
               .HasDatabaseName("ix_event_seats_session_status");

        // Sure asimi job'i: "kilidi dolmus koltuklari bul"
        builder.HasIndex(es => es.LockedUntil)
               .HasFilter("\"LockedUntil\" IS NOT NULL")
               .HasDatabaseName("ix_event_seats_locked_until");
    }
}
