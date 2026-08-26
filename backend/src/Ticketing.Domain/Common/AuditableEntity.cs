namespace Ticketing.Domain.Common;

/// <summary>
/// Denetim (audit) ve soft delete alanlarini tasiyan taban sinif.
/// PDF'in "CreatedAt ve UpdatedAt alanlari", "Audit alanlari" ve
/// "Soft Delete kullanilacak tablolar" maddelerinin karsiligidir.
/// </summary>
public abstract class AuditableEntity : Entity
{
    /// <summary>
    /// Neden DateTimeOffset, DateTime degil?
    ///
    /// DateTime saat dilimi bilgisi TASIMAZ. Elinizdeki bir DateTime'a bakip
    /// UTC mi yoksa Istanbul saati mi oldugunu anlayamazsiniz. (DateTime.Kind
    /// diye bir alan var ama veritabanina yazilip okundugunda kaybolur.)
    ///
    /// Bizim projede rezervasyon suresi 10 dakika. Saat dilimi karisirsa
    /// 3 saatlik bir kayma olusur ve ya herkesin rezervasyonu aninda dolar
    /// ya da hic dolmaz. Ikisi de felaket.
    ///
    /// DateTimeOffset offset bilgisini de saklar, bu belirsizligi ortadan
    /// kaldirir. PostgreSQL'de "timestamptz" tipine karsilik gelir.
    /// PDF: "Tarih ve saat bilgilerinin UTC tutulmasi".
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Islemi yapan kullanicinin Id'si. Nullable, cunku bazi kayitlari
    /// SISTEM olusturur (background job, seed data) -- ortada kullanici yoktur.
    /// </summary>
    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    /// <summary>
    /// Soft delete isareti.
    ///
    /// Bu alani EF Core'un global query filter'i ile eslestirecegiz:
    ///     modelBuilder.Entity&lt;Event&gt;().HasQueryFilter(e =&gt; !e.IsDeleted);
    ///
    /// O satirdan sonra _context.Events.ToListAsync() yazdigimda EF sorguya
    /// otomatik olarak WHERE "IsDeleted" = false ekler. Her sorguda elle
    /// yazmayi unutma riski ortadan kalkar -- ki bu risk gercektir, 50 sorgudan
    /// birinde mutlaka unutulur ve silinmis kayitlar kullaniciya gorunur.
    ///
    /// Admin'in silinmisleri gormesi gerektiginde IgnoreQueryFilters() kullanacagiz.
    /// </summary>
    public bool IsDeleted { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public Guid? DeletedBy { get; set; }
}
