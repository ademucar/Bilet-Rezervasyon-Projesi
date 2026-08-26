using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ticketing.Domain.Common;
using Ticketing.Domain.ValueObjects;

namespace Ticketing.Persistence.Configurations;

/// <summary>
/// Tum konfigurasyonlarda tekrarlanan eslestirmeleri toplayan yardimcilar.
///
/// Bunlari ayri bir yere almamin sebebi: xmin eslestirmesini 5 farkli
/// dosyada elle yazsaydim, birinde bir harf hatasi yapmam yeterdi --
/// o tablo icin optimistic concurrency SESSIZCE calismazdi. Derleme
/// hatasi vermez, test kirmizi yanmaz; sadece ayni koltuk iki kisiye
/// satilirdi. Tek bir metotta toplamak bu riski ortadan kaldiriyor.
/// </summary>
internal static class ConfigurationExtensions
{
    /// <summary>
    /// PostgreSQL'in "xmin" sistem sutununu optimistic concurrency
    /// token'i olarak esler.
    ///
    /// ------------------------------------------------------------------
    /// BU DORT SATIR NE YAPIYOR?
    /// ------------------------------------------------------------------
    /// HasColumnName("xmin")        -> PostgreSQL'in gizli sistem sutunu.
    ///                                 Her satirda zaten var; biz sadece
    ///                                 ona bir ad veriyoruz.
    ///
    /// HasColumnType("xid")         -> xmin'in veri tipi. 32 bit isaretsiz.
    ///
    /// ValueGeneratedOnAddOrUpdate  -> Bu degeri BIZ yazmiyoruz;
    ///                                 PostgreSQL her INSERT ve UPDATE'te
    ///                                 kendisi guncelliyor. EF'e "sen
    ///                                 dokunma, okuduktan sonra geri al" diyoruz.
    ///
    /// IsConcurrencyToken()         -> KRITIK OLAN BU. Bu satirdan sonra
    ///                                 EF her UPDATE sorgusuna
    ///                                     WHERE Id = @id AND xmin = @okunan
    ///                                 kosulunu OTOMATIK ekler.
    ///                                 Araya baskasi girmisse 0 satir
    ///                                 etkilenir ve EF
    ///                                 DbUpdateConcurrencyException firlatir.
    ///
    /// MALIYET: sifir. Ekstra sutun yok, ekstra index yok, ekstra yazma yok.
    /// PostgreSQL bu bilgiyi zaten tutuyor.
    /// </summary>
    public static void ConfigureConcurrencyToken<TEntity>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : ConcurrentEntity
    {
        builder.Property(x => x.RowVersion)
               .HasColumnName("xmin")
               .HasColumnType("xid")
               .ValueGeneratedOnAddOrUpdate()
               .IsConcurrencyToken();
    }

    /// <summary>
    /// AuditableEntity'den gelen ortak alanlarin eslestirmesi.
    /// </summary>
    public static void ConfigureAuditFields<TEntity>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : AuditableEntity
    {
        builder.Property(x => x.CreatedAt).IsRequired();

        // Soft delete: silinmis kayitlar varsayilan olarak sorgulara gelmez.
        //
        // Bu satirdan sonra context.Events.ToListAsync() yazdigimda EF
        // sorguya otomatik olarak WHERE "IsDeleted" = false ekler.
        // Her sorguda elle yazmayi unutma riski ortadan kalkar -- ki bu
        // risk gercektir: 50 sorgudan birinde mutlaka unutulur ve
        // silinmis kayitlar kullaniciya gorunur.
        //
        // Admin'in silinmisleri gormesi gerektiginde IgnoreQueryFilters().
        builder.HasQueryFilter(x => !x.IsDeleted);

        // Soft delete'li tablolarda IsDeleted'i index'e dahil ediyorum
        // cunku ARTIK HER SORGUDA bu kosul var. Index olmadan her sorgu
        // tam tarama yapardi.
        builder.HasIndex(x => x.IsDeleted);
    }

    /// <summary>
    /// Money value object'ini iki sutuna esler: {ad}_Amount ve {ad}_Currency.
    ///
    /// numeric(18,2) kullaniyorum:
    ///   - numeric = PostgreSQL'in TAM HASSASIYETLI ondalik tipi.
    ///     real/double precision gibi yuvarlama hatasi yapmaz.
    ///   - 18 basamak, 2'si kurus. 999 trilyon TL'ye kadar yeter.
    ///
    /// PDF Sprint 6: "Para degerleri decimal olarak tutulmalidir.
    /// Floating point kullanilmamalidir."
    /// </summary>
    public static void ConfigureMoney<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        System.Linq.Expressions.Expression<Func<TEntity, Money>> selector,
        string columnPrefix)
        where TEntity : class
    {
        builder.ComplexProperty(selector, money =>
        {
            money.Property(m => m.Amount)
                 .HasColumnName($"{columnPrefix}Amount")
                 .HasColumnType("numeric(18,2)")
                 .IsRequired();

            money.Property(m => m.Currency)
                 .HasColumnName($"{columnPrefix}Currency")
                 .HasMaxLength(3)
                 .IsFixedLength()
                 .IsRequired();
        });
    }
}
