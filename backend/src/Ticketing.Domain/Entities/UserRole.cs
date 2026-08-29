namespace Ticketing.Domain.Entities;

/// <summary>
/// User ile Role arasindaki çok-a-çok iliskinin ara tablosu.
///
/// NEDEN Entity'DEN TUREMIYOR? NEDEN Id ALANI YOK?
///
/// Bu tablonun kendine ait bir kimliği yok. "3 numarali kullanıcı-rol
/// iliskisi" diye bir sey anlamsiz. Kimligi, iliskilendirdigi iki
/// varligin birlesimidir: (UserId, RoleId).
///
/// Bu yüzden COMPOSITE KEY kullanıyorum. EF konfigurasyonunda:
///     builder.HasKey(ur =&gt; new { ur.UserId, ur.RoleId });
///
/// Bunun bana kazandirdiklari:
///
/// 1) Aynı kullanıcıya aynı rol IKI KEZ atanamaz -- veritabani seviyesinde
///    garanti. Ayrı bir Id sutunu olsaydı (Guid Id, UserId, RoleId),
///    aynı ciftten iki satır olusabilirdi ve bunu engellemek için AYRICA
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
    // null! yazıyorum çünkü: nullable referans tipleri açık (Nullable=enable).
    // Derleyici "bu alan hiç atanmiyor, null olabilir" diye uyarir.
    // Ama bu alanlari EF Core dolduruyor -- Include() ile yuklendiginde
    // dolu olacaklar, yuklenmediginde null kalacaklar.
    //
    // null! ile derleyiciye "bunun sorumlulugunu ben alıyorum" diyorum.
    // Alternatif olarak User? yazabilirdim ama o zaman her kullanımda
    // null kontrolü yapmam gerekirdi -- Include() ettigimi bildigim halde.
    public User User { get; private set; } = null!;

    public Role Role { get; private set; } = null!;
}
