using Ticketing.Domain.Common;

namespace Ticketing.Domain.Entities;

/// <summary>
/// Outbox Pattern kaydı. PDF Sprint 9.
///
/// Outbox pattern nedir ve neden gerekli?
///
/// Problem: Ödeme başarılı olduğunda iki sey yapmamiz gerekiyor:
///   1. Veritabanina yaz (rezervasyon onayla, bilet üret)
///   2. E-posta gönder
///
/// Bunlar iki farkli sistem. Aralarinda ortak bir transaction yok.
/// Dolayisiyla su iki senaryo kacinilmaz:
///
///   A) Önce DB yaz, sonra e-posta gönder:
///      DB yazildi ama e-posta servisi cokmus -> kullanıcı biletini
///      aldi ama haberi yok.
///
///   B) Önce e-posta gönder, sonra DB yaz:
///      E-posta gitti ama DB transaction'i geri alındı -> kullanıcı
///      "biletiniz hazır" maili aldi ama bilet YOK.
///
/// Ikisi de kabul edilemez. B daha da kötü: geri alinamaz.
///
/// Cozum: E-postayi gondermek yerine, "e-posta gonderilecek" niyetini
/// aynı veritabanina, ayni transaction içinde yaz.
///
///   Begin transaction
///     UPDATE Reservations SET Status = Confirmed
///     INSERT INTO Tickets ...
///     INSERT INTO OutboxMessages (Type='SendTicketEmail', Payload='{...}')
///   COMMIT
///
/// Artık tek bir transaction var: ya hepsi olur ya hicbiri.
/// Arkada calisan bir job OutboxMessages tablosunu okur ve e-postayi
/// gönderir. Job cokerse mesaj tabloda kalır, bir sonraki calismada
/// tekrar denenir.
///
/// Bu, "en az bir kez teslim" (at-least-önce delivery) garantisidir.
/// Mesaj iki kez islenebilir; bu yüzden isleyicilerin IDEMPOTENT olmasını
/// sarttir (PDF: "Aynı Outbox kaydı iki kez islenmemelidir").
/// </summary>
public class OutboxMessage : Entity
{
    private OutboxMessage()
    {
        Type = string.Empty;
        Payload = string.Empty;
    }

    /// <summary>
    /// Mesaj türü. Ornek: "ReservationCreated", "PaymentSucceeded".
    /// Isleyici bu degere bakarak ne yapacagina karar verir.
    /// </summary>
    public string Type { get; private set; }

    /// <summary>
    /// Mesaj içeriği (JSON). Veritabaninda jsonb olarak saklanacak.
    ///
    /// Neden nesne değil de metin? Çünkü Outbox tablosu GENEL amaclidir:
    /// içinde 20 farklı mesaj türü olacak ve her birinin alanlari farklı.
    /// Tek bir tabloda tutmanin yolu, içeriği serilestirilmis olarak saklamak.
    /// </summary>
    public string Payload { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Basariyla islendigi an. Null ise henuz islenmedi.
    ///
    /// Job'in sorgusu: WHERE "ProcessedAt" IS NULL ORDER BY "CreatedAt"
    /// Bu yüzden (ProcessedAt, CreatedAt) uzerinde composite index var.
    /// </summary>
    public DateTimeOffset? ProcessedAt { get; private set; }

    /// <summary>Kac kez denendi. PDF: "Başarısız işlem yeniden denenmelidir."</summary>
    public int RetryCount { get; private set; }

    public string? ErrorMessage { get; private set; }

    /// <summary>
    /// Bir sonraki deneme zamani. Ustel geri cekilme (exponential backoff)
    /// için kullanilir: 1dk, 2dk, 4dk, 8dk...
    ///
    /// Neden geri cekilme? E-posta servisi cokmusse her 10 saniyede bir
    /// denemek önü daha da yorar ve loglari doldurur. Aralari acmak
    /// hem servise nefes aldirir hem de geçici sorunlarin kendiliginden
    /// duzelmesine zaman tanir.
    /// </summary>
    public DateTimeOffset? NextRetryAt { get; private set; }

    /// <summary>
    /// Kalici olarak başarısız. PDF: "Belirli deneme sayisindan sonra
    /// hata kaydı olusturulmalidir."
    ///
    /// Bu isaret konulunca mesaj bir daha denenmez; manuel mudahale bekler.
    /// Sonsuza kadar denemek, kuyrugu tikar ve gerçek sorunu gizler.
    /// </summary>
    public bool IsDeadLettered { get; private set; }

    /// <summary>
    /// PDF Sprint 16: Correlation ID "Outbox kaydı icerisinde kullanılmalıdır."
    ///
    /// Bu sayede "kullanıcının su isteği hangi e-postayi tetikledi?"
    /// sorusunu loglardan cevaplayabiliyoruz. Arka plan job'i ile önü
    /// tetikleyen HTTP isteği arasindaki bagi kuran tek sey budur.
    /// </summary>
    public string? CorrelationId { get; private set; }

    public static OutboxMessage Create(string type, string payload, string? correlationId = null)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            throw new DomainException("Outbox mesaj türü boş olamaz.", "outbox.type_required");
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            throw new DomainException("Outbox mesaj içeriği boş olamaz.", "outbox.payload_required");
        }

        return new OutboxMessage
        {
            Type = type,
            Payload = payload,
            CorrelationId = correlationId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>
    /// Basariyla islendi.
    /// </summary>
    /// <summary>
    /// Correlation ID'yi, henuz atanmamissa atar.
    /// </summary>
    /// <remarks>
    /// Neden "sadece bossa" yaziyor?
    ///
    /// Bu metodu OutboxCorrelationInterceptor cagiriyor: kaydetme
    /// anında, değeri atanmamis her Outbox mesajini o anki HTTP
    /// isteginin ID'siyle dolduruyor.
    ///
    /// Ama bazi cagri yerleri değeri ACIKCA veriyor (örneğin
    /// TicketTypeCommands). Kosulsuz yazsaydım, interceptor o bilinçli
    /// seçimi EZERDI.
    ///
    /// Ilke: otomatik doldurma, açık niyeti geçersiz kilmamali.
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
            // PDF: "Aynı Outbox kaydı iki kez islenmemelidir."
            //
            // Burada hata firlatmiyorum, sessizce donuyorum. Sebep:
            // at-least-önce teslimde aynı mesajin iki kez islenmesi
            // BEKLENEN bir durumdur, hata değil. Hata firlatsaydim
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
    /// Isleme başarısız oldu, yeniden denenecek.
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
        // 2^10 = 1024 dakika (17 saat) beklerdik ki bu çok uzun.
        var bekleme = Math.Min(Math.Pow(2, RetryCount), 60);
        NextRetryAt = now.AddMinutes(bekleme);
    }

    /// <summary>
    /// Bu mesaj su an islenmeye hazır mi?
    /// </summary>
    public bool IsReadyToProcess(DateTimeOffset now)
    {
        if (ProcessedAt.HasValue || IsDeadLettered)
        {
            return false;
        }

        // Hic denenmemisse hemen hazır; denenmisse bekleme süresi gecmis olmalı.
        return !NextRetryAt.HasValue || NextRetryAt.Value <= now;
    }
}
