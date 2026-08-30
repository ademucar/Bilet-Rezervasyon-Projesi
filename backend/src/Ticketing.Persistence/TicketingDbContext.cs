using Microsoft.EntityFrameworkCore;
using Ticketing.Domain.Entities;

namespace Ticketing.Persistence;

/// <summary>
/// Uygulamanin EF Core veritabani baglami.
///
/// NEDEN DbSet'ler VAR AMA IS MANTIGI YOK?
///
/// DbContext'in tek isi veri erisimidir. Icine "rezervasyon oluştur"
/// gibi metotlar yazsaydım, is mantığı Persistence katmanina sizardi
/// ve architecture testim bunu yakalamasa bile tasarım bozulurdu.
///
/// Ayrıca: Controller'lar bu sinifi DOGRUDAN kullanmayacak
/// (PDF: "Controller doğrudan DbContext kullanmamalidir"). Bunu
/// architecture testi ile zorunlu kildik.
/// </summary>
public partial class TicketingDbContext : DbContext
{
    public TicketingDbContext(DbContextOptions<TicketingDbContext> options)
        : base(options)
    {
    }

    // --- Kimlik ve yetkilendirme ---
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<OrganizerProfile> OrganizerProfiles => Set<OrganizerProfile>();
    public DbSet<OrganizerApplication> OrganizerApplications => Set<OrganizerApplication>();

    // --- Mekan hiyerarsisi ---
    public DbSet<City> Cities => Set<City>();
    public DbSet<Venue> Venues => Set<Venue>();
    public DbSet<Hall> Halls => Set<Hall>();
    public DbSet<SeatLayout> SeatLayouts => Set<SeatLayout>();
    public DbSet<SeatSection> SeatSections => Set<SeatSection>();
    public DbSet<Seat> Seats => Set<Seat>();

    // --- Etkinlik ---
    public DbSet<EventCategory> EventCategories => Set<EventCategory>();
    public DbSet<Event> Events => Set<Event>();
    public DbSet<EventSession> EventSessions => Set<EventSession>();
    public DbSet<TicketType> TicketTypes => Set<TicketType>();
    public DbSet<TicketTypeSection> TicketTypeSections => Set<TicketTypeSection>();
    public DbSet<EventSeat> EventSeats => Set<EventSeat>();

    // --- Rezervasyon ve ödeme ---
    public DbSet<Reservation> Reservations => Set<Reservation>();
    public DbSet<ReservationItem> ReservationItems => Set<ReservationItem>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentTransaction> PaymentTransactions => Set<PaymentTransaction>();
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<TicketQrCode> TicketQrCodes => Set<TicketQrCode>();

    // --- Destek tablolari ---
    public DbSet<Favorite> Favorites => Set<Favorite>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<UploadedFile> UploadedFiles => Set<UploadedFile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        base.OnModelCreating(modelBuilder);

        // Bu assembly'deki TÜM IEntityTypeConfiguration siniflarini bulur
        // ve uygular.
        //
        // Alternatif, her konfigurasyonu tek tek burada cagirmakti:
        //     modelBuilder.ApplyConfiguration(new UserConfiguration());
        //     modelBuilder.ApplyConfiguration(new EventConfiguration());
        //     ... x28
        //
        // Bunu YAPMADIM çünkü 29. entity'yi eklerken bu satiri eklemeyi
        // unutmak çok kolay ve hata sessizdir: EF entity'yi varsayılan
        // kurallarla esler, unique index'ler ve concurrency token'lar
        // OLUSMAZ. Migration'a bakmadan fark edemezsin.
        //
        // Tarama yontemiyle boyle bir unutma ihtimali yok.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TicketingDbContext).Assembly);

        ConfigureClientGeneratedKeys(modelBuilder);
    }

    /// <summary>
    /// Tüm Guid birincil anahtarlari "istemci tarafında üretilir" olarak isaretler.
    ///
    /// Bu metot gercek bir hatayi duzeltiyor -- hikayesi
    ///
    /// Sprint 4'te oturma planina bölüm eklerken su hatayi aldim:
    ///
    ///     DbUpdateConcurrencyException: 1 satır etkilenmesi bekleniyordu,
    ///     0 satır etkilendi.
    ///
    /// Loglara bakinca EF'in INSERT yerine UPDATE urettigini gorduk:
    ///     UPDATE "SeatSections" SET ... WHERE "Id" = @p11
    /// Satir henüz var olmadığı için 0 satır etkilendi.
    ///
    /// SEBEP:
    /// Entity taban sinifimda Id'yi BIZ uretiyorum:
    ///     public Guid Id { get; protected set; } = Guid.CreateVersion7();
    ///
    /// EF Core ise Guid anahtarlari varsayılan olarak
    /// "ValueGeneratedOnAdd" (veritabani/EF üretir) kabul eder.
    ///
    /// Bu ikisi celisince su olur: EF, bir navigation koleksiyonuna
    /// eklenmis yeni nesneyi gordugunde "anahtari dolu, demek ki
    /// veritabaninda ZATEN VAR" diye dusunur ve Modified isaretler.
    ///
    /// NEDEN Venue ve Hall'da OLMADI?
    /// Çünkü onlari _context.Venues.Add(...) ile ACIKCA ekledim --
    /// Add() her zaman Added isaretler. Hata yalnızca nesne bir
    /// KOLEKSIYON üzerinden eklendiginde ortaya cikiyor.
    ///
    /// Neden tek tek değil de toplu duzeltiyorum?
    /// Aynı tuzak su yerlerde de patlayacakti:
    ///     Reservation -> ReservationItems   (Sprint 7)
    ///     Payment     -> PaymentTransactions (Sprint 8)
    ///     EventSession -> EventSeats         (Sprint 5)
    ///     SeatSection -> Seats               (koltuk üretimi)
    ///
    /// Yani projenin EN KRITIK akislarinin hepsi. Tek tek duzeltseydim
    /// birini unutmam kacinilmazdi ve hata aylar sonra, rezervasyon
    /// akisinda ortaya çıkardı.
    ///
    /// Model uzerinde donerek toplu uygulamak, gelecekte eklenecek
    /// entity'leri de otomatik kapsiyor.
    /// </summary>
    private static void ConfigureClientGeneratedKeys(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var primaryKey = entityType.FindPrimaryKey();

            // Composite key'leri atliyorum (UserRole, Favorite).
            // Onlarin anahtarlari zaten yabanci anahtar degerlerinden
            // olusuyor ve EF onlari uretmeye calismiyor.
            if (primaryKey is null || primaryKey.Properties.Count != 1)
            {
                continue;
            }

            var keyProperty = primaryKey.Properties[0];

            if (keyProperty.ClrType == typeof(Guid))
            {
                keyProperty.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never;
            }
        }
    }
}
