using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ticketing.Domain.Entities;

namespace Ticketing.Persistence.Configurations;

internal sealed class FavoriteConfiguration : IEntityTypeConfiguration<Favorite>
{
    public void Configure(EntityTypeBuilder<Favorite> builder)
    {
        builder.ToTable("Favorites");

        // PDF sayfa 8: "Aynı kullanıcı aynı etkinligi bir kez
        // favorileyebilmelidir."
        //
        // Composite key bunu YAPISAL olarak garanti eder. Ayrı bir Id
        // sutunu + unique index yerine bunu tercih ettim: bir sutun ve
        // bir index daha az, aynı garanti.
        builder.HasKey(f => new { f.UserId, f.EventId });

        builder.HasOne(f => f.User)
               .WithMany()
               .HasForeignKey(f => f.UserId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(f => f.Event)
               .WithMany()
               .HasForeignKey(f => f.EventId)
               .OnDelete(DeleteBehavior.Cascade);

        // "Favorilerim" sayfası için: kullanıcının favorilerini
        // en yeniden eskiye sirala.
        builder.HasIndex(f => new { f.UserId, f.CreatedAt });
    }
}

internal sealed class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> builder)
    {
        builder.ToTable("Reviews");
        builder.HasKey(r => r.Id);

        builder.ConfigureAuditFields();

        builder.Property(r => r.Comment).HasMaxLength(2000).IsRequired();
        builder.Property(r => r.HiddenReason).HasMaxLength(500);

        // PDF sayfa 8: "Aynı kullanıcı aynı etkinlige yalnızca bir yorum
        // yapabilmelidir."
        builder.HasIndex(r => new { r.UserId, r.EventId })
               .IsUnique()
               .HasFilter("\"IsDeleted\" = false")
               .HasDatabaseName("ix_reviews_user_event");

        builder.HasOne(r => r.User)
               .WithMany()
               .HasForeignKey(r => r.UserId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Event)
               .WithMany()
               .HasForeignKey(r => r.EventId)
               .OnDelete(DeleteBehavior.Restrict);

        // Etkinlik detay sayfası: gizlenmemis yorumlari getir.
        builder.HasIndex(r => new { r.EventId, r.IsHidden });

        // Puan aralığı kontrolü. Review.Create zaten kontrol ediyor ama
        // veritabani seviyesinde de garanti altina alıyorum.
        //
        // Neden iki kez? Uygulama disindan (SQL ile toplu veri yukleme,
        // veri tasima scripti) gelen kayitlar entity metodlarindan
        // gecmez. CHECK constraint bu yolu da kapatiyor.
        builder.ToTable(t => t.HasCheckConstraint(
            "ck_reviews_rating_range",
            "\"Rating\" BETWEEN 1 AND 5"));
    }
}

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("Notifications");
        builder.HasKey(n => n.Id);

        builder.Property(n => n.Title).HasMaxLength(200).IsRequired();
        builder.Property(n => n.Message).HasMaxLength(1000).IsRequired();
        builder.Property(n => n.ActionPath).HasMaxLength(512);
        builder.Property(n => n.Type).HasConversion<int>();

        builder.HasOne(n => n.User)
               .WithMany()
               .HasForeignKey(n => n.UserId)
               .OnDelete(DeleteBehavior.Cascade);

        // PDF Sprint 14: "GET /api/v1/notifications/unread-count"
        //
        // Bu endpoint frontend'de zil ikonunun yanindaki sayiyi besliyor
        // ve HER SAYFA YUKLENISINDE cagriliyor. Index olmadan her cagride
        // kullanıcının tüm bildirimleri taranirdi.
        //
        // IsRead'i partial filter yaparak index'i daha da kucultuyorum:
        // okunmus bildirimler (cogunluk) index'te yer tutmuyor.
        builder.HasIndex(n => new { n.UserId, n.IsRead })
               .HasFilter("\"IsRead\" = false")
               .HasDatabaseName("ix_notifications_user_unread");

        // Bildirim listesi: en yeniden eskiye
        builder.HasIndex(n => new { n.UserId, n.CreatedAt });
    }
}

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.EntityName).HasMaxLength(100).IsRequired();
        builder.Property(a => a.Action).HasMaxLength(100).IsRequired();
        builder.Property(a => a.IpAddress).HasMaxLength(45);
        builder.Property(a => a.CorrelationId).HasMaxLength(100);

        // Eski/yeni degerler JSON olarak. jsonb secmemin sebebi:
        // ileride "su alanı kim degistirdi" gibi sorgular gerekirse
        // PostgreSQL jsonb içinde arama yapabilir; duz text'te yapamaz.
        builder.Property(a => a.OldValues).HasColumnType("jsonb");
        builder.Property(a => a.NewValues).HasColumnType("jsonb");

        // "Su kaydin gecmisi" sorgusu
        builder.HasIndex(a => new { a.EntityName, a.EntityId, a.CreatedAt })
               .HasDatabaseName("ix_audit_logs_entity");

        // "Su kullanıcı ne yapti" sorgusu
        builder.HasIndex(a => new { a.UserId, a.CreatedAt });

        // Correlation ID ile bir istegin tüm izini surmek için
        builder.HasIndex(a => a.CorrelationId);
    }
}

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages");
        builder.HasKey(o => o.Id);

        builder.Property(o => o.Type).HasMaxLength(200).IsRequired();
        builder.Property(o => o.Payload).HasColumnType("jsonb").IsRequired();
        builder.Property(o => o.ErrorMessage).HasMaxLength(4000);
        builder.Property(o => o.CorrelationId).HasMaxLength(100);

        // OUTBOX JOB'ININ ANA SORGUSU
        //
        //     WHERE ProcessedAt IS NULL
        //       AND IsDeadLettered = false
        //       AND (NextRetryAt IS NULL OR NextRetryAt <= now())
        //     ORDER BY CreatedAt
        //     LIMIT 100
        //
        // Bu sorgu 10 SANIYEDE BIR calisacak. Yani günde ~8600 kez.
        //
        // Partial index kullanıyorum: yalnızca ISLENMEMIS mesajlar index'te.
        // Islenmis mesajlar zamanla milyonlari bulacak ama hicbiri bu
        // index'te yer tutmayacak. Boylece index tablonun buyumesinden
        // BAGIMSIZ olarak küçük kaliyor -- sorgu süresi sabit kaliyor.
        //
        // Normal (partial olmayan) bir index olsaydı, 6 ay sonra
        // 5 milyon islenmis mesaj arasindan 3 islenmemisi bulmak
        // giderek yavaslardi.
        builder.HasIndex(o => new { o.ProcessedAt, o.CreatedAt })
               .HasFilter("\"ProcessedAt\" IS NULL")
               .HasDatabaseName("ix_outbox_unprocessed");

        // Dead letter incelemesi için
        builder.HasIndex(o => o.IsDeadLettered)
               .HasFilter("\"IsDeadLettered\" = true");
    }
}

internal sealed class UploadedFileConfiguration : IEntityTypeConfiguration<UploadedFile>
{
    public void Configure(EntityTypeBuilder<UploadedFile> builder)
    {
        builder.ToTable("UploadedFiles");
        builder.HasKey(f => f.Id);

        builder.ConfigureAuditFields();

        builder.Property(f => f.FileName).HasMaxLength(255).IsRequired();
        builder.Property(f => f.StoredFileName).HasMaxLength(255).IsRequired();
        builder.Property(f => f.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(f => f.StoragePath).HasMaxLength(512).IsRequired();
        builder.Property(f => f.RelatedEntityName).HasMaxLength(100);

        builder.HasIndex(f => f.StoredFileName).IsUnique();

        // Sahipsiz (orphan) dosya temizligi job'i için:
        //     WHERE RelatedEntityId IS NULL AND CreatedAt < now() - interval '24 hours'
        builder.HasIndex(f => new { f.RelatedEntityName, f.RelatedEntityId });
    }
}
