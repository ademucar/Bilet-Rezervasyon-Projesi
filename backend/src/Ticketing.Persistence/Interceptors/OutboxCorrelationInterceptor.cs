using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Ticketing.Application.Abstractions.Security;
using Ticketing.Domain.Entities;

namespace Ticketing.Persistence.Interceptors;

/// <summary>
/// Yeni Outbox mesajlarina, o anki istegin Correlation ID'sini yazar.
/// PDF Sprint 16: Correlation ID "Outbox kaydi icerisinde
/// kullanilmalidir."
/// </summary>
/// <remarks>
/// ==================================================================
/// BU SINIF SPRINT 16'DA, OLCEREK BULUNAN BIR BOSLUK ICIN YAZILDI
/// ==================================================================
/// OutboxMessage.CorrelationId alani Sprint 9'dan beri VARDI.
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
/// Sekiz cagri yerinden YEDISI parametreyi hic gecmiyordu. Alan
/// vardi, indeks vardi, niyet vardi -- veri yoktu.
///
/// ------------------------------------------------------------------
/// NEDEN 7 CAGRI YERINI TEK TEK DUZELTMEDIM?
/// ------------------------------------------------------------------
/// Duzeltebilirdim; 7 satirlik bir is. Ama ayni hata YENIDEN olurdu:
/// 9. cagri yerini yazan kisi (yani gelecekteki ben) parametreyi
/// yine unuturdu ve bunu kimse fark etmezdi -- cunku unutmanin
/// belirtisi YOK. Kod derleniyor, testler geciyor, sistem calisiyor.
/// Yalnizca uretimde bir sorunu arastirirken "bu e-postayi hangi
/// istek tetikledi?" diye sordugunda cevapsiz kaliyorsun.
///
/// Interceptor, unutulmasi MUMKUN OLMAYAN yere koyuyor: kaydetme
/// aninda, otomatik.
///
/// Bu, Sprint 12'deki AuditFieldsInterceptor kararinin aynisi ve ayni
/// desende bir hatayi cozuyor: "alan tanimli ama kimse doldurmuyor".
///
/// ------------------------------------------------------------------
/// NEDEN AuditFieldsInterceptor'A EKLEMEDIM?
/// ------------------------------------------------------------------
/// Ekleyebilirdim ve ChangeTracker'i bir kez yerine iki kez gezmekten
/// kurtulurduk.
///
/// Ayirmayi sectim cunku iki sinifin SORUMLULUGU farkli:
///   - AuditFieldsInterceptor: AuditableEntity turevleri, "kim/ne zaman"
///   - Bu sinif: yalnizca OutboxMessage, "hangi istek"
///
/// OutboxMessage zaten AuditableEntity DEGIL (kendi CreatedAt'i var),
/// yani ayni doneceye sigmiyorlardi. Performans farki da olcusuz:
/// ChangeTracker gezintisi bellekte ve tipik bir kaydetmede birkac
/// on giris var.
/// ==================================================================
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
    /// Senkron kaydetme icin ayni is.
    /// </summary>
    /// <remarks>
    /// Ikisini de gecersiz kilmak SART. Yalnizca async surumu
    /// yazsaydik, senkron SaveChanges() cagiran herhangi bir kod yolu
    /// (seed islemi, migration, bir test) sessizce bos correlation ID
    /// uretirdi -- ve bu, duzeltmeye calistigimiz hatanin ta kendisi.
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

        // ==============================================================
        // ARKA PLAN ISLERINDE ICurrentUser BOS -- BU NORMAL
        // ==============================================================
        // ICurrentUser degerini IHttpContextAccessor'dan okuyor. Hangfire
        // isinde HTTP baglami YOK, dolayisiyla CorrelationId de yok.
        //
        // O durumda hicbir sey yazmiyoruz ve alan null kaliyor. Bu
        // DOGRU davranis: arka plan isinin urettigi yeni bir Outbox
        // mesajini, alakasiz bir HTTP istegine baglamak yanlis bilgi
        // uretirdi.
        //
        // Arka plan isleri kendi correlation ID'lerini ISLEDIKLERI
        // mesajdan devraliyor (bkz. ProcessOutboxMessagesCommand).
        // ==============================================================
        var correlationId = _currentUser.CorrelationId;

        if (string.IsNullOrWhiteSpace(correlationId))
        {
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries<OutboxMessage>())
        {
            // Yalnizca YENI eklenen kayitlar.
            //
            // Guncellenen bir mesaja (ornegin "islendi" isaretlenen)
            // dokunmuyoruz: onun correlation ID'si onu OLUSTURAN
            // istege ait ve oyle kalmali. Isleyen isin ID'siyle
            // degistirmek, zinciri tam ters yonde koparirdi.
            if (entry.State == EntityState.Added)
            {
                entry.Entity.SetCorrelationIdIfMissing(correlationId);
            }
        }
    }
}
