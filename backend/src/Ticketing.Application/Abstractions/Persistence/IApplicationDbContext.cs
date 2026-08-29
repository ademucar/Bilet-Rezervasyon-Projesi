using Microsoft.EntityFrameworkCore;
using Ticketing.Domain.Entities;

namespace Ticketing.Application.Abstractions.Persistence;

/// <summary>
/// Application katmaninin veritabanina bakan yuzu.
///
/// NEDEN REPOSITORY DEĞİL DE DOGRUDAN DbSet?
///
/// Klasik yaklasim her entity için bir repository yazmaktir:
/// IUserRepository, IEventRepository, IReservationRepository...
///
/// Bunu YAPMADIM. Sebeplerim:
///
/// 1) DbSet&lt;T&gt; ZATEN bir repository'dir ve IQueryable ZATEN bir
///    sorgu soyutlamasidir. Ustune bir katman daha koymak, aynı isi
///    iki kez yapmaktir.
///
/// 2) Repository'ler kacinilmaz olarak sisiyor:
///    GetById, GetByIdWithSessions, GetByIdWithSessionsAndSeats,
///    GetByIdWithSessionsAndSeatsAndTicketTypes...
///    Her yeni ekran yeni bir metot dogurur.
///
/// 3) Repository, EF'in en güçlü ozelliklerini (projeksiyon, Include,
///    Split query, compiled query) ya gizler ya da her birini ayrı
///    metot olarak acmaya zorlar.
///
/// PDF de bu konuda esnek: "Repository abstraction GEREKIYORSA yalnızca
/// interface seviyesinde" diyor. Yani zorunlu tutmuyor.
///
/// PEKI TEST EDILEBILIRLIK?
///
/// Repository'nin ana gerekçesi "handler'i mock'layabilmek"tir.
/// Ama biz zaten Testcontainers ile GERCEK PostgreSQL uzerinde
/// integration test yazacagim (PDF Sprint 17 bunu zorunlu tutuyor).
///
/// Mock'lanmis bir repository ile yazilan test, gerçek veritabanindaki
/// unique index ihlallerini, concurrency cakismalarini ve transaction
/// davranisini YAKALAYAMAZ -- ki benim projemizin en kritik kisimlari
/// tam olarak bunlar.
///
/// Bu arayüz yine de degerli: Application katmani somut
/// TicketingDbContext'i değil bu soyutlamayi görüyor, yani Persistence
/// katmanina bagimli olmuyor. Architecture testimiz bunu zorunlu kiliyor.
/// </summary>
public interface IApplicationDbContext
{
    DbSet<User> Users { get; }
    DbSet<Role> Roles { get; }
    DbSet<UserRole> UserRoles { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<OrganizerProfile> OrganizerProfiles { get; }
    DbSet<OrganizerApplication> OrganizerApplications { get; }

    DbSet<City> Cities { get; }
    DbSet<Venue> Venues { get; }
    DbSet<Hall> Halls { get; }
    DbSet<SeatLayout> SeatLayouts { get; }
    DbSet<SeatSection> SeatSections { get; }
    DbSet<Seat> Seats { get; }

    DbSet<EventCategory> EventCategories { get; }
    DbSet<Event> Events { get; }
    DbSet<EventSession> EventSessions { get; }
    DbSet<TicketType> TicketTypes { get; }
    DbSet<TicketTypeSection> TicketTypeSections { get; }
    DbSet<EventSeat> EventSeats { get; }

    DbSet<Reservation> Reservations { get; }
    DbSet<ReservationItem> ReservationItems { get; }
    DbSet<Payment> Payments { get; }
    DbSet<PaymentTransaction> PaymentTransactions { get; }
    DbSet<Ticket> Tickets { get; }
    DbSet<TicketQrCode> TicketQrCodes { get; }

    DbSet<Favorite> Favorites { get; }
    DbSet<Review> Reviews { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<OutboxMessage> OutboxMessages { get; }
    DbSet<UploadedFile> UploadedFiles { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
