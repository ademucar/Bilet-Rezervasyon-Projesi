using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Ticketing.Application.Abstractions.Security;
using Ticketing.Application.Abstractions.Time;
using Ticketing.Domain.Common;

namespace Ticketing.Persistence.Interceptors;

/// <summary>
/// ==================================================================
/// DENETIM ALANLARINI OTOMATIK DOLDURAN INTERCEPTOR
/// ==================================================================
/// AuditableEntity uzerindeki CreatedAt / CreatedBy / UpdatedAt /
/// UpdatedBy alanlarini kaydetme aninda dolduruyor.
///
/// ------------------------------------------------------------------
/// BU SINIF SPRINT 12'DE, GERCEK BIR HATA BULUNCA YAZILDI
/// ------------------------------------------------------------------
/// Yorum ozelligini tarayicida denerken yorum tarihi "01 Ocak 1"
/// gorundu. Veritabanina bakinca sebebi cikti:
///
///     CreatedAt = -infinity     (yani DateTimeOffset.MinValue)
///
/// AuditableEntity'de bu alanlar TANIMLI ama HICBIR YERDE
/// DOLDURULMUYORDU. Sprint 2'den beri boyleymis.
///
/// Etkilenen tablolar (dogrulandi):
///     Tickets       0 / 4  dolu
///     Reservations  0 / 7  dolu
///     Payments      0 / 3  dolu
///     Reviews       0 / 2  dolu
///
/// ------------------------------------------------------------------
/// DAHA ONCE FARK EDEMEDIM -- CUNKU BELIRTISINI YANLIS YORUMLADIM
/// ------------------------------------------------------------------
/// Sprint 11'de gunluk satis ozeti isini test ederken rapor "0 bilet,
/// 0 rezervasyon" dondu. O sorgu tam olarak su filtreyi kullaniyor:
///
///     .Where(t =&gt; t.CreatedAt &gt;= start &amp;&amp; t.CreatedAt &lt; end)
///
/// Ben bunu "dun hic satis olmamis, normal" diye yorumladim ve
/// gectim. Oysa rapor CreatedAt bos oldugu icin HICBIR ZAMAN veri
/// bulamayacakti.
///
/// Ders: bekledigim sonucu goren bir test, gecen bir test degildir.
/// "0 dondu ve bu makul" ile "0 dondu cunku sorgu bozuk" ayni
/// gorunuyordu.
///
/// ------------------------------------------------------------------
/// NEDEN ENTITY ICINDE DEGIL DE INTERCEPTOR?
/// ------------------------------------------------------------------
/// Her Create() metoduna "CreatedAt = DateTimeOffset.UtcNow" satiri
/// eklemek de mumkundu. Yapmadim:
///
///   1) 29 entity var. Birinde unutmak kacinilmaz -- ve bu hatanin
///      tam olarak bu sekilde olustugunu dusunuyorum.
///   2) UpdatedAt'i entity icinde tutmak imkansiz: hangi metodun
///      "guncelleme" sayilacagini her seferinde elle isaretlemek
///      gerekirdi.
///   3) Domain katmani ZAMANI ve KULLANICIYI bilmemeli. Interceptor
///      Persistence katmaninda; oraya ait.
///
/// Interceptor TEK YERDE ve otomatik. Yeni bir entity eklendiginde
/// hicbir sey yapmaya gerek yok.
/// ==================================================================
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
    /// Async yol. Uygulamadaki tum cagrilar bunu kullaniyor.
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
    /// Uygulamada senkron SaveChanges KULLANMIYORUZ ama bu metodu
    /// yine de yaziyorum.
    ///
    /// Sebep: birisi ilerde (test kodunda, bir seed scriptinde,
    /// aceleyle yazilmis bir yerde) senkron cagirirsa denetim
    /// alanlari SESSIZCE bos kalirdi -- yani duzelttigim hatanin
    /// aynisi geri gelirdi.
    ///
    /// Iki satirlik bir yonlendirme, bu riski tamamen kapatiyor.
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

                    // ==================================================
                    // CreatedAt UZERINE YAZILMASINI ENGELLE
                    // ==================================================
                    // EF, bir entity Modified durumundayken TUM
                    // ozelliklerini UPDATE cumlesine dahil edebilir.
                    // CreatedAt'e dokunmasak bile, bellekteki deger
                    // yanlissa (ornegin kismi bir sorgu ile
                    // yuklenmisse) veritabanindakinin uzerine yazardi.
                    //
                    // IsModified = false demek "bu sutunu UPDATE'e
                    // hic koyma" demek. Olusturulma bilgisi bir kez
                    // yazilir ve bir daha degismez.
                    // ==================================================
                    entry.Property(nameof(AuditableEntity.CreatedAt)).IsModified = false;
                    entry.Property(nameof(AuditableEntity.CreatedBy)).IsModified = false;
                    break;

                case EntityState.Deleted:
                    // ==================================================
                    // SOFT DELETE: SILME ISLEMINI GUNCELLEMEYE CEVIR
                    // ==================================================
                    // AuditableEntity soft delete destekliyor
                    // (IsDeleted alani ve global query filter).
                    //
                    // Ama birisi context.Remove(entity) cagirirsa EF
                    // GERCEK bir DELETE uretir ve kayit KAYBOLUR --
                    // soft delete altyapisi hicbir ise yaramaz.
                    //
                    // Burada durumu Modified'a cevirip IsDeleted
                    // bayragini set ediyorum. Boylece "Remove"
                    // cagrisi da soft delete olarak calisiyor ve
                    // veri kaybi imkansiz hale geliyor.
                    //
                    // NOT: Favorite bir AuditableEntity DEGIL, bu
                    // yuzden gercekten siliniyor -- Sprint 12'de
                    // bilincli olarak boyle tasarlandi.
                    // ==================================================
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
                    // dokunmuyoruz.
                    break;
            }
        }
    }
}
