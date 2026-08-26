using Microsoft.EntityFrameworkCore;
using Ticketing.Domain.Entities;

namespace Ticketing.Persistence;

/// <summary>
/// Uygulamanin EF Core veritabani baglami.
///
/// ------------------------------------------------------------------
/// NEDEN DbSet'ler VAR AMA IS MANTIGI YOK?
/// ------------------------------------------------------------------
/// DbContext'in tek isi veri erisimidir. Icine "rezervasyon olustur"
/// gibi metotlar yazsaydik, is mantigi Persistence katmanina sizardi
/// ve architecture testimiz bunu yakalamasa bile tasarim bozulurdu.
///
/// Ayrica: Controller'lar bu sinifi DOGRUDAN kullanmayacak
/// (PDF: "Controller dogrudan DbContext kullanmamalidir"). Bunu
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
    public DbSet<EventSeat> EventSeats => Set<EventSeat>();

    // --- Rezervasyon ve odeme ---
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

        // Bu assembly'deki TUM IEntityTypeConfiguration siniflarini bulur
        // ve uygular.
        //
        // Alternatif, her konfigurasyonu tek tek burada cagirmakti:
        //     modelBuilder.ApplyConfiguration(new UserConfiguration());
        //     modelBuilder.ApplyConfiguration(new EventConfiguration());
        //     ... x28
        //
        // Bunu YAPMADIM cunku 29. entity'yi eklerken bu satiri eklemeyi
        // unutmak cok kolay ve hata sessizdir: EF entity'yi varsayilan
        // kurallarla esler, unique index'ler ve concurrency token'lar
        // OLUSMAZ. Migration'a bakmadan fark edemezsin.
        //
        // Tarama yontemiyle boyle bir unutma ihtimali yok.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TicketingDbContext).Assembly);
    }
}
