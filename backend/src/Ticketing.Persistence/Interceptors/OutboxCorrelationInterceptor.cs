using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Ticketing.Application.Abstractions.Security;
using Ticketing.Domain.Entities;

namespace Ticketing.Persistence.Interceptors;

/// <summary>
/// Yeni Outbox mesajlarina, o anki istegin Correlation ID'sini yazar.
/// PDF Sprint 16: Correlation ID "Outbox kaydı icerisinde
/// kullanılmalıdır."
/// </summary>
/// <remarks>
/// Bu sinif sprint 16'da, olcerek bulunan bir bosluk icin yazildi
///
/// OutboxMessage.CorrelationId alanı Sprint 9'dan beri VARDI.
/// Create() metodunda parametresi vardi. Veritabaninda sutunu ve
/// hatta INDEKSI vardi. XML yorumunda "PDF Sprint 16" diye
/// isaretlenmisti.
///
/// Ama veritabanina bakinca durum su cikti:
///
///     Type                 adet   correlation_dolu
///     ReportExport            5          0
///     ReservationExpired      5          0
///     PaymentSucceeded        2          0
///     TicketsIssued           2          0
///     ReservationCreated      2          0
///     ...
///     TOPLAM                 22          0
///
/// Sekiz cagri yerinden YEDISI parametreyi hiç gecmiyordu. Alan
/// vardi, indeks vardi, niyet vardi -- veri yoktu.
///
/// Neden 7 cagri yerini tek tek duzeltmedim?
///
/// Duzeltebilirdim; 7 satirlik bir is. Ama aynı hata YENIDEN olurdu:
/// 9. cagri yerini yazan kişi (yani gelecekteki ben) parametreyi
/// yine unuturdu ve bunu kimse fark etmezdi -- çünkü unutmanin
/// belirtisi YOK. Kod derleniyor, testler geciyor, sistem çalışıyor.
/// Yalnızca uretimde bir sorunu arastirirken "bu e-postayi hangi
/// istek tetikledi?" diye sordugunda cevapsiz kaliyorsun.
///
/// Interceptor, unutulmasi MUMKUN OLMAYAN yere koyuyor: kaydetme
/// anında, otomatik.
///
/// Bu, Sprint 12'deki AuditFieldsInterceptor kararinin aynisi ve aynı
/// desende bir hatayi cozuyor: "alan tanimli ama kimse doldurmuyor".
///
/// NEDEN AuditFieldsInterceptor'A EKLEMEDIM?
///
/// Ekleyebilirdim ve ChangeTracker'i bir kez yerine iki kez gezmekten
/// kurtulurduk.
///
/// Ayirmayi sectim çünkü iki sinifin SORUMLULUGU farklı:
///   - AuditFieldsInterceptor: AuditableEntity turevleri, "kim/ne zaman"
///   - Bu sinif: yalnızca OutboxMessage, "hangi istek"
///
/// OutboxMessage zaten AuditableEntity DEĞİL (kendi CreatedAt'i var),
/// yani aynı doneceye sigmiyorlardi. Performans farki da olcusuz:
/// ChangeTracker gezintisi bellekte ve tipik bir kaydetmede birkaç
/// on giriş var.
/// </remarks>
internal sealed class OutboxCorrelationInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentUser _currentUser;

    public OutboxCorrelationInterceptor(ICurrentUser currentUser)
        => _currentUser = currentUser;

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
    /// Senkron kaydetme için aynı is.
    /// </summary>
    /// <remarks>
    /// Ikisini de geçersiz kilmak ŞART. Yalnızca async surumu
    /// yazsaydım, senkron SaveChanges() cagiran herhangi bir kod yolu
    /// (seed islemi, migration, bir test) sessizce boş correlation ID
    /// üretirdi -- ve bu, duzeltmeye calistigim hatanin ta kendisi.
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

        // ARKA PLAN ISLERINDE ICurrentUser BOŞ -- BU NORMAL
        //
        // ICurrentUser degerini IHttpContextAccessor'dan okuyor. Hangfire
        // isinde HTTP baglami YOK, dolayisiyla CorrelationId de yok.
        //
        // O durumda hiçbir sey yazmiyorum ve alan null kaliyor. Bu
        // DOGRU davranis: arka plan isinin urettigi yeni bir Outbox
        // mesajini, alakasiz bir HTTP istegine baglamak yanlış bilgi
        // üretirdi.
        //
        // Arka plan isleri kendi correlation ID'lerini ISLEDIKLERI
        // mesajdan devraliyor (bkz. ProcessOutboxMessagesCommand).
        var correlationId = _currentUser.CorrelationId;

        if (string.IsNullOrWhiteSpace(correlationId))
        {
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries<OutboxMessage>())
        {
            // Yalnızca YENI eklenen kayitlar.
            //
            // Guncellenen bir mesaja (örneğin "islendi" isaretlenen)
            // dokunmuyorum: onun correlation ID'si önü OLUSTURAN
            // isteğe ait ve oyle kalmali. Isleyen isin ID'siyle
            // degistirmek, zinciri tam ters yonde koparirdi.
            if (entry.State == EntityState.Added)
            {
                entry.Entity.SetCorrelationIdIfMissing(correlationId);
            }
        }
    }
}
