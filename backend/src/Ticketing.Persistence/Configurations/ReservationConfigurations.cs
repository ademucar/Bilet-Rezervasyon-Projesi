using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ticketing.Domain.Entities;

namespace Ticketing.Persistence.Configurations;

internal sealed class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
{
    public void Configure(EntityTypeBuilder<Reservation> builder)
    {
        builder.ToTable("Reservations");
        builder.HasKey(r => r.Id);

        builder.ConfigureAuditFields();
        builder.ConfigureConcurrencyToken();

        builder.Property(r => r.ReservationCode).HasMaxLength(20).IsRequired();
        builder.Property(r => r.CancellationReason).HasMaxLength(500);
        builder.Property(r => r.IdempotencyKey).HasMaxLength(100);
        builder.Property(r => r.Status).HasConversion<int>();

        builder.ConfigureMoney(r => r.TotalAmount, "TotalAmount_");

        builder.HasOne(r => r.User)
               .WithMany()
               .HasForeignKey(r => r.UserId)
               .OnDelete(DeleteBehavior.Restrict);   // rezervasyonu olan kullanıcı silinemez

        builder.HasOne(r => r.EventSession)
               .WithMany()
               .HasForeignKey(r => r.EventSessionId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(r => r.Items)
               .WithOne(i => i.Reservation)
               .HasForeignKey(i => i.ReservationId)
               .OnDelete(DeleteBehavior.Cascade);    // kalem rezervasyonsuz anlamsiz

        builder.HasIndex(r => r.ReservationCode).IsUnique();

        // IDEMPOTENCY -- PDF Sprint 15
        //
        // Kullanıcı butona iki kez basarsa ikinci istek bu unique index'e
        // takilir ve yeni rezervasyon OLUSMAZ.
        //
        // HasFilter ile NULL olanlari disarida biraktim: idempotency key
        // gondermeyen (opsiyonel) istekler birbirini engellememeli.
        // PostgreSQL'de NULL'lar unique index'te birbirinden farklı sayilir
        // ama filtre koymak hem niyeti netlestiriyor hem de index'i
        // kucultuyor.
        builder.HasIndex(r => r.IdempotencyKey)
               .IsUnique()
               .HasFilter("\"IdempotencyKey\" IS NOT NULL")
               .HasDatabaseName("ix_reservations_idempotency_key");

        // "Benim rezervasyonlarim" sayfası
        builder.HasIndex(r => new { r.UserId, r.Status });

        // SURE ASIMI JOB'ININ SORGUSU
        //
        //     WHERE Status IN (Locked, PaymentPending) AND ExpiresAt <= now()
        //
        // Bu sorgu DAKIKADA BIR calisacak. Index olmasaydı her calismada
        // tüm Reservations tablosunu tararsdi. 100.000 rezervasyondan sonra
        // bu, veritabanini surekli mesgul eden bir yuke donusurdu.
        builder.HasIndex(r => new { r.Status, r.ExpiresAt })
               .HasDatabaseName("ix_reservations_status_expires");
    }
}

internal sealed class ReservationItemConfiguration : IEntityTypeConfiguration<ReservationItem>
{
    public void Configure(EntityTypeBuilder<ReservationItem> builder)
    {
        builder.ToTable("ReservationItems");
        builder.HasKey(i => i.Id);

        builder.ConfigureMoney(i => i.UnitPrice, "UnitPrice_");

        builder.HasOne(i => i.EventSeat)
               .WithMany()
               .HasForeignKey(i => i.EventSeatId)
               .OnDelete(DeleteBehavior.Restrict);   // rezerve koltuk silinemez

        // Bir koltuk aynı anda yalnızca BIR aktif rezervasyon kalemine
        // ait olabilir. Bunu unique index ile garanti edemeyiz çünkü
        // gecmis (iptal olmuş) rezervasyonlarin kalemleri de aynı koltuga
        // isaret ediyor. Asil garanti EventSeat.Status uzerinde.
        //
        // Bu index yalnızca sorgu hizi için.
        builder.HasIndex(i => i.EventSeatId);
    }
}

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments");
        builder.HasKey(p => p.Id);

        builder.ConfigureAuditFields();
        builder.ConfigureConcurrencyToken();

        builder.Property(p => p.ProviderName).HasMaxLength(100).IsRequired();
        builder.Property(p => p.ProviderReference).HasMaxLength(200);
        builder.Property(p => p.FailureReason).HasMaxLength(1000);
        builder.Property(p => p.IdempotencyKey).HasMaxLength(100);
        builder.Property(p => p.Status).HasConversion<int>();

        builder.ConfigureMoney(p => p.Amount, "Amount_");
        builder.ConfigureMoney(p => p.RefundedAmount, "RefundedAmount_");

        builder.HasOne(p => p.Reservation)
               .WithMany()
               .HasForeignKey(p => p.ReservationId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(p => p.Transactions)
               .WithOne(t => t.Payment)
               .HasForeignKey(t => t.PaymentId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(p => p.IdempotencyKey)
               .IsUnique()
               .HasFilter("\"IdempotencyKey\" IS NOT NULL");

        builder.HasIndex(p => p.ReservationId);
        builder.HasIndex(p => p.ProviderReference);
    }
}

internal sealed class PaymentTransactionConfiguration : IEntityTypeConfiguration<PaymentTransaction>
{
    public void Configure(EntityTypeBuilder<PaymentTransaction> builder)
    {
        builder.ToTable("PaymentTransactions");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.ProviderReference).HasMaxLength(200);
        builder.Property(t => t.Message).HasMaxLength(1000);
        builder.Property(t => t.Type).HasConversion<int>();
        builder.Property(t => t.Status).HasConversion<int>();

        builder.HasIndex(t => new { t.PaymentId, t.CreatedAt });
    }
}

internal sealed class TicketConfiguration : IEntityTypeConfiguration<Ticket>
{
    public void Configure(EntityTypeBuilder<Ticket> builder)
    {
        builder.ToTable("Tickets");
        builder.HasKey(t => t.Id);

        builder.ConfigureAuditFields();

        builder.Property(t => t.TicketNumber).HasMaxLength(50).IsRequired();
        builder.Property(t => t.StudentVerificationCode).HasMaxLength(50);
        builder.Property(t => t.Status).HasConversion<int>();

        builder.ConfigureMoney(t => t.Price, "Price_");

        // PDF sayfa 8: "Bilet numarasi benzersiz olmalıdır."
        builder.HasIndex(t => t.TicketNumber)
               .IsUnique()
               .HasDatabaseName("ix_tickets_number");

        // Bir rezervasyon kalemi için YALNIZCA BIR bilet.
        //
        // Bu, "aynı koltuk için iki bilet üretildi" hatasinin veritabani
        // seviyesindeki karşılığı. Boyle bir hata olsa salona iki kişi
        // girerdi ve kapida tartisma çıkardı.
        builder.HasIndex(t => t.ReservationItemId)
               .IsUnique()
               .HasFilter("\"IsDeleted\" = false")
               .HasDatabaseName("ix_tickets_reservation_item");

        builder.HasOne(t => t.ReservationItem)
               .WithMany()
               .HasForeignKey(t => t.ReservationItemId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.User)
               .WithMany()
               .HasForeignKey(t => t.UserId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.EventSeat)
               .WithMany()
               .HasForeignKey(t => t.EventSeatId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.QrCode)
               .WithOne(q => q.Ticket)
               .HasForeignKey<TicketQrCode>(q => q.TicketId)
               .OnDelete(DeleteBehavior.Cascade);

        // "Biletlerim" sayfası
        builder.HasIndex(t => new { t.UserId, t.Status });

        // Etkinlik girisinde kontrol
        builder.HasIndex(t => t.EventSessionId);
    }
}

internal sealed class TicketQrCodeConfiguration : IEntityTypeConfiguration<TicketQrCode>
{
    public void Configure(EntityTypeBuilder<TicketQrCode> builder)
    {
        builder.ToTable("TicketQrCodes");
        builder.HasKey(q => q.Id);

        builder.Property(q => q.QrValue).HasMaxLength(128).IsRequired();
        builder.Property(q => q.ImagePath).HasMaxLength(512);

        // PDF sayfa 8: "QR kod değeri benzersiz olmalıdır."
        //
        // Bu index aynı zamanda GIRIS KONTROLUNUN sorgusudur:
        // gorevli QR'i okuttugunda "SELECT ... WHERE QrValue = @deger"
        // çalışır. Index olmadan her okutmada tam tarama olurdu ve
        // kapida kuyruk olusurdu.
        builder.HasIndex(q => q.QrValue)
               .IsUnique()
               .HasDatabaseName("ix_ticket_qr_codes_value");
    }
}
