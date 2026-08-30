using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Ticketing.Application.Abstractions.Security;
using Ticketing.Application.Abstractions.Time;
using Ticketing.Domain.Common;

namespace Ticketing.Persistence.Interceptors;

/// <summary>
/// Denetim alanlarini otomatik dolduran interceptor
///
/// AuditableEntity uzerindeki CreatedAt / CreatedBy / UpdatedAt /
/// UpdatedBy alanlarini kaydetme anında dolduruyor.
///
/// Bu sinif sprint 12'de, gercek bir hata bulunca yazildi
///
/// Yorum ozelligini tarayıcıda denerken yorum tarihi "01 Ocak 1"
/// gorundu. Veritabanina bakinca sebebi cikti:
///
///     CreatedAt = -infinity     (yani DateTimeOffset.MinValue)
///
/// AuditableEntity'de bu alanlar tanimli ama hicbir yerde
/// DOLDURULMUYORDU. Sprint 2'den beri boyleymis.
///
/// Etkilenen tablolar (dogrulandi):
///     Tickets       0 / 4  dolu
///     Reservations  0 / 7  dolu
///     Payments      0 / 3  dolu
///     Reviews       0 / 2  dolu
///
/// Daha once fark edemedim -- çünkü belirtisini yanlis yorumladim
///
/// Sprint 11'de günlük satış özeti isini test ederken rapor "0 bilet,
/// 0 rezervasyon" dondu. O sorgu tam olarak su filtreyi kullaniyor:
///
///     .Where(t =&gt; t.CreatedAt &gt;= start &amp;&amp; t.CreatedAt &lt; end)
///
/// Ben bunu "dun hiç satış olmamış, normal" diye yorumladim ve
/// gectim. Oysa rapor CreatedAt boş olduğu için hicbir zaman veri
/// bulamayacakti.
///
/// Ders: bekledigim sonucu goren bir test, gecen bir test degildir.
/// "0 dondu ve bu makul" ile "0 dondu çünkü sorgu bozuk" aynı
/// gorunuyordu.
///
/// Neden entity icinde değil de interceptor?
///
/// Her Create() metoduna "CreatedAt = DateTimeOffset.UtcNow" satiri
/// eklemek de mumkundu. Yapmadim:
///
///   1) 29 entity var. Birinde unutmak kacinilmaz -- ve bu hatanin
///      tam olarak bu şekilde olustugunu dusunuyorum.
///   2) UpdatedAt'i entity içinde tutmak imkansiz: hangi metodun
///      "güncelleme" sayilacagini her seferinde elle isaretlemek
///      gerekirdi.
///   3) Domain katmani zamani ve kullaniciyi bilmemeli. Interceptor
///      Persistence katmaninda; oraya ait.
///
/// Interceptor tek yerde ve otomatik. Yeni bir entity eklendiginde
/// hiçbir sey yapmaya gerek yok.
/// </summary>
internal sealed class AuditFieldsInterceptor : SaveChangesInterceptor
{
    private readonly IDateTimeProvider _clock;
    private readonly ICurrentUser _currentUser;

    public AuditFieldsInterceptor(IDateTimeProvider clock, ICurrentUser currentUser)
    {
        _clock = clock;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Async yol. Uygulamadaki tüm cagrilar bunu kullaniyor.
    /// </summary>
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Doldur(eventData.Context);

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    /// <summary>
    /// Senkron yol.
    /// </summary>
    /// <remarks>
    /// Uygulamada senkron SaveChanges KULLANMIYORUM ama bu metodu
    /// yine de yazıyorum.
    ///
    /// Sebep: birisi ilerde (test kodunda, bir seed scriptinde,
    /// aceleyle yazilmis bir yerde) senkron cagirirsa denetim
    /// alanlari SESSIZCE boş kalırdı -- yani duzelttigim hatanin
    /// aynisi geri gelirdi.
    ///
    /// Iki satirlik bir yönlendirme, bu riski tamamen kapatiyor.
    /// </remarks>
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Doldur(eventData.Context);

        return base.SavingChanges(eventData, result);
    }

    private void Doldur(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = _clock.UtcNow;
        var userId = _currentUser.UserId;

        foreach (var entry in context.ChangeTracker.Entries<AuditableEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    entry.Entity.CreatedBy = userId;
                    break;

                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    entry.Entity.UpdatedBy = userId;

                    // CreatedAt uzerine yazilmasini engelle
                    //
                    // EF, bir entity Modified durumundayken TÜM
                    // ozelliklerini UPDATE cumlesine dahil edebilir.
                    // CreatedAt'e dokunmasak bile, bellekteki deger
                    // yanlissa (örneğin kismi bir sorgu ile
                    // yuklenmisse) veritabanindakinin uzerine yazardi.
                    //
                    // IsModified = false demek "bu sutunu UPDATE'e
                    // hiç koyma" demek. Oluşturulma bilgisi bir kez
                    // yazilir ve bir daha degismez.
                    entry.Property(nameof(AuditableEntity.CreatedAt)).IsModified = false;
                    entry.Property(nameof(AuditableEntity.CreatedBy)).IsModified = false;
                    break;

                case EntityState.Deleted:
                    // Soft DELETE: silme islemini guncellemeye cevir
                    //
                    // AuditableEntity soft delete destekliyor
                    // (IsDeleted alanı ve global query filter).
                    //
                    // Ama birisi context.Remove(entity) cagirirsa EF
                    // gercek bir DELETE üretir ve kayıt kaybolur --
                    // soft delete altyapisi hiçbir ise yaramaz.
                    //
                    // Burada durumu Modified'a cevirip IsDeleted
                    // bayragini set ediyorum. Boylece "Remove"
                    // cagrisi da soft delete olarak çalışıyor ve
                    // veri kaybi imkansiz hale geliyor.
                    //
                    // NOT: Favorite bir AuditableEntity DEĞİL, bu
                    // yüzden gerçekten siliniyor -- Sprint 12'de
                    // bilinçli olarak boyle tasarlandi.
                    entry.State = EntityState.Modified;
                    entry.Entity.IsDeleted = true;
                    entry.Entity.DeletedAt = now;
                    entry.Entity.DeletedBy = userId;

                    entry.Property(nameof(AuditableEntity.CreatedAt)).IsModified = false;
                    entry.Property(nameof(AuditableEntity.CreatedBy)).IsModified = false;
                    break;

                case EntityState.Detached:
                case EntityState.Unchanged:
                default:
                    // Degismemis veya takip edilmeyen kayitlara
                    // dokunmuyorum.
                    break;
            }
        }
    }
}
