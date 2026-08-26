namespace Ticketing.Domain.Entities;

/// <summary>
/// User ile Role arasindaki cok-a-cok iliskinin ara tablosu.
///
/// ------------------------------------------------------------------
/// NEDEN Entity'DEN TUREMIYOR? NEDEN Id ALANI YOK?
/// ------------------------------------------------------------------
/// Bu tablonun kendine ait bir kimligi yok. "3 numarali kullanici-rol
/// iliskisi" diye bir sey anlamsiz. Kimligi, iliskilendirdigi iki
/// varligin birlesimidir: (UserId, RoleId).
///
/// Bu yuzden COMPOSITE KEY kullaniyoruz. EF konfigurasyonunda:
///     builder.HasKey(ur =&gt; new { ur.UserId, ur.RoleId });
///
/// Bunun bize kazandirdiklari:
///
/// 1) Ayni kullaniciya ayni rol IKI KEZ atanamaz -- veritabani seviyesinde
///    garanti. Ayri bir Id sutunu olsaydi (Guid Id, UserId, RoleId),
///    ayni ciftten iki satir olusabilirdi ve bunu engellemek icin AYRICA
///    bir unique index eklemek gerekirdi.
///
/// 2) Bir sutun ve bir index daha az. Milyonlarca satirda fark eder.
///
/// PDF: "Composite Key kullanilan tablolar" -- bu tablo onlardan biri.
/// </summary>
public class UserRole
{
    private UserRole()
    {
    }

    internal UserRole(Guid userId, Guid roleId)
    {
        UserId = userId;
        RoleId = roleId;
        AssignedAt = DateTimeOffset.UtcNow;
    }

    public Guid UserId { get; private set; }

    public Guid RoleId { get; private set; }

    public DateTimeOffset AssignedAt { get; private set; }

    // Navigation property'ler.
    //
    // null! yaziyorum cunku: nullable referans tipleri acik (Nullable=enable).
    // Derleyici "bu alan hic atanmiyor, null olabilir" diye uyarir.
    // Ama bu alanlari EF Core dolduruyor -- Include() ile yuklendiginde
    // dolu olacaklar, yuklenmediginde null kalacaklar.
    //
    // null! ile derleyiciye "bunun sorumlulugunu ben aliyorum" diyorum.
    // Alternatif olarak User? yazabilirdim ama o zaman her kullanimda
    // null kontrolu yapmam gerekirdi -- Include() ettigimi bildigim halde.
    public User User { get; private set; } = null!;

    public Role Role { get; private set; } = null!;
}
