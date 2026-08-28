using Ticketing.Domain.Common;

namespace Ticketing.Domain.Entities;

/// <summary>
/// Outbox Pattern kaydi. PDF Sprint 9.
///
/// ==================================================================
/// OUTBOX PATTERN NEDIR VE NEDEN GEREKLI?
/// ==================================================================
/// Problem: Odeme basarili oldugunda iki sey yapmamiz gerekiyor:
///   1. Veritabanina yaz (rezervasyon onayla, bilet uret)
///   2. E-posta gonder
///
/// Bunlar IKI FARKLI SISTEM. Aralarinda ortak bir transaction yok.
/// Dolayisiyla su iki senaryo kacinilmaz:
///
///   A) Once DB yaz, sonra e-posta gonder:
///      DB yazildi ama e-posta servisi cokmus -> kullanici biletini
///      aldi ama haberi yok.
///
///   B) Once e-posta gonder, sonra DB yaz:
///      E-posta gitti ama DB transaction'i geri alindi -> kullanici
///      "biletiniz hazir" maili aldi ama bilet YOK.
///
/// Ikisi de kabul edilemez. B daha da kotu: geri alinamaz.
///
/// COZUM: E-postayi gondermek yerine, "e-posta gonderilecek" NIYETINI
/// ayni veritabanina, AYNI TRANSACTION icinde yaz.
///
///   BEGIN TRANSACTION
///     UPDATE Reservations SET Status = Confirmed
///     INSERT INTO Tickets ...
///     INSERT INTO OutboxMessages (Type='SendTicketEmail', Payload='{...}')
///   COMMIT
///
/// Artik tek bir transaction var: ya hepsi olur ya hicbiri.
/// Arkada calisan bir job OutboxMessages tablosunu okur ve e-postayi
/// gonderir. Job cokerse mesaj tabloda kalir, bir sonraki calismada
/// tekrar denenir.
///
/// Bu, "en az bir kez teslim" (at-least-once delivery) garantisidir.
/// Mesaj iki kez islenebilir; bu yuzden isleyicilerin IDEMPOTENT olmasi
/// sarttir (PDF: "Ayni Outbox kaydi iki kez islenmemelidir").
/// ==================================================================
/// </summary>
public class OutboxMessage : Entity
{
    private OutboxMessage()
    {
        Type = string.Empty;
        Payload = string.Empty;
    }

    /// <summary>
    /// Mesaj turu. Ornek: "ReservationCreated", "PaymentSucceeded".
    /// Isleyici bu degere bakarak ne yapacagina karar verir.
    /// </summary>
    public string Type { get; private set; }

    /// <summary>
    /// Mesaj icerigi (JSON). Veritabaninda jsonb olarak saklanacak.
    ///
    /// Neden nesne degil de metin? Cunku Outbox tablosu GENEL amaclidir:
    /// icinde 20 farkli mesaj turu olacak ve her birinin alanlari farkli.
    /// Tek bir tabloda tutmanin yolu, icerigi serilestirilmis olarak saklamak.
    /// </summary>
    public string Payload { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Basariyla islendigi an. null ise HENUZ ISLENMEDI.
    ///
    /// Job'in sorgusu: WHERE "ProcessedAt" IS NULL ORDER BY "CreatedAt"
    /// Bu yuzden (ProcessedAt, CreatedAt) uzerinde composite index var.
    /// </summary>
    public DateTimeOffset? ProcessedAt { get; private set; }

    /// <summary>Kac kez denendi. PDF: "Basarisiz islem yeniden denenmelidir."</summary>
    public int RetryCount { get; private set; }

    public string? ErrorMessage { get; private set; }

    /// <summary>
    /// Bir sonraki deneme zamani. Ustel geri cekilme (exponential backoff)
    /// icin kullanilir: 1dk, 2dk, 4dk, 8dk...
    ///
    /// Neden geri cekilme? E-posta servisi cokmusse her 10 saniyede bir
    /// denemek onu daha da yorar ve loglari doldurur. Aralari acmak
    /// hem servise nefes aldirir hem de gecici sorunlarin kendiliginden
    /// duzelmesine zaman tanir.
    /// </summary>
    public DateTimeOffset? NextRetryAt { get; private set; }

    /// <summary>
    /// Kalici olarak basarisiz. PDF: "Belirli deneme sayisindan sonra
    /// hata kaydi olusturulmalidir."
    ///
    /// Bu isaret konulunca mesaj bir daha denenmez; manuel mudahale bekler.
    /// Sonsuza kadar denemek, kuyrugu tikar ve gercek sorunu gizler.
    /// </summary>
    public bool IsDeadLettered { get; private set; }

    /// <summary>
    /// PDF Sprint 16: Correlation ID "Outbox kaydi icerisinde kullanilmalidir."
    ///
    /// Bu sayede "kullanicinin su istegi hangi e-postayi tetikledi?"
    /// sorusunu loglardan cevaplayabiliyoruz. Arka plan job'i ile onu
    /// tetikleyen HTTP istegi arasindaki bagi kuran tek sey budur.
    /// </summary>
    public string? CorrelationId { get; private set; }

    public static OutboxMessage Create(string type, string payload, string? correlationId = null)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            throw new DomainException("Outbox mesaj turu bos olamaz.", "outbox.type_required");
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            throw new DomainException("Outbox mesaj icerigi bos olamaz.", "outbox.payload_required");
        }

        return new OutboxMessage
        {
            Type = type,
            Payload = payload,
            CorrelationId = correlationId,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Basariyla islendi.
    /// </summary>
    /// <summary>
    /// Correlation ID'yi, HENUZ ATANMAMISSA atar.
    /// </summary>
    /// <remarks>
    /// ==============================================================
    /// NEDEN "SADECE BOSSA" YAZIYOR?
    /// ==============================================================
    /// Bu metodu OutboxCorrelationInterceptor cagiriyor: kaydetme
    /// aninda, degeri atanmamis her Outbox mesajini o anki HTTP
    /// isteginin ID'siyle dolduruyor.
    ///
    /// Ama bazi cagri yerleri degeri ACIKCA veriyor (ornegin
    /// TicketTypeCommands). Kosulsuz yazsaydik, interceptor o bilincli
    /// secimi EZERDI.
    ///
    /// Ilke: otomatik doldurma, acik niyeti gecersiz kilmamali.
    /// ==============================================================
    /// </remarks>
    public void SetCorrelationIdIfMissing(string? correlationId)
    {
        if (string.IsNullOrWhiteSpace(CorrelationId)
            && !string.IsNullOrWhiteSpace(correlationId))
        {
            CorrelationId = correlationId;
        }
    }

    public void MarkAsProcessed(DateTimeOffset now)
    {
        if (ProcessedAt.HasValue)
        {
            // PDF: "Ayni Outbox kaydi iki kez islenmemelidir."
            //
            // Burada HATA FIRLATMIYORUM, sessizce donuyorum. Sebep:
            // at-least-once teslimde ayni mesajin iki kez islenmesi
            // BEKLENEN bir durumdur, hata degil. Hata firlatsaydim
            // job loglari gereksiz alarmlarla dolardi.
            //
            // Asil koruma isleyicinin kendisinde: e-posta gonderen kod
            // "bu e-posta zaten gonderilmis mi?" diye kontrol etmeli.
            return;
        }

        ProcessedAt = now;
        ErrorMessage = null;
        NextRetryAt = null;
    }

    /// <summary>
    /// Isleme basarisiz oldu, yeniden denenecek.
    /// </summary>
    /// <param name="maxRetries">Bu sayiya ulasinca dead letter olur.</param>
    public void MarkAsFailed(string error, int maxRetries, DateTimeOffset now)
    {
        RetryCount++;
        ErrorMessage = error;

        if (RetryCount >= maxRetries)
        {
            IsDeadLettered = true;
            NextRetryAt = null;

            return;
        }

        // Ustel geri cekilme: 2^RetryCount dakika.
        // 1. hata -> 2 dk, 2. hata -> 4 dk, 3. hata -> 8 dk...
        //
        // Math.Min ile ust sinir koyuyorum: 10 denemeden sonra
        // 2^10 = 1024 dakika (17 saat) beklerdik ki bu cok uzun.
        var bekleme = Math.Min(Math.Pow(2, RetryCount), 60);
        NextRetryAt = now.AddMinutes(bekleme);
    }

    /// <summary>
    /// Bu mesaj su an islenmeye hazir mi?
    /// </summary>
    public bool IsReadyToProcess(DateTimeOffset now)
    {
        if (ProcessedAt.HasValue || IsDeadLettered)
        {
            return false;
        }

        // Hic denenmemisse hemen hazir; denenmisse bekleme suresi gecmis olmali.
        return !NextRetryAt.HasValue || NextRetryAt.Value <= now;
    }
}
