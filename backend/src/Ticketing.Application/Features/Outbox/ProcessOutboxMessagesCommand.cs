using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ticketing.Application.Abstractions.Messaging;
using Ticketing.Application.Abstractions.Persistence;
using Ticketing.Application.Abstractions.Time;
using Ticketing.Application.Common.Results;

namespace Ticketing.Application.Features.Outbox;

/// <summary>Bir Outbox turunun calisma sonucu. Loglama ve test için.</summary>
public sealed record OutboxProcessingResult(
    int Processed,
    int Failed,
    int DeadLettered);

/// <summary>
/// Bekleyen Outbox mesajlarini isler. PDF Sprint 9.
/// </summary>
/// <param name="BatchSize">
/// Tek calismada islenecek en fazla mesaj.
///
/// Sinirsiz olsaydı, e-posta servisi bir gün kapalı kalip 50.000
/// mesaj biriktiginde job hepsini tek seferde islemeye çalışır ve
/// dakikalarca calisirdi. Bu sırada Hangfire bir sonraki calismayi
/// baslatamaz, izleme ekrani "takildi" görünür.
///
/// Parca parca islemek job'in her turda kisa surmesini saglar.
/// </param>
public sealed record ProcessOutboxMessagesCommand(int BatchSize = 20)
    : IRequest<Result<OutboxProcessingResult>>;

internal sealed partial class ProcessOutboxMessagesCommandHandler
    : IRequestHandler<ProcessOutboxMessagesCommand, Result<OutboxProcessingResult>>
{
    /// <summary>
    /// Kalici basarisizlik esigi. PDF: "Belirli deneme sayisindan sonra
    /// hata kaydı olusturulmalidir."
    ///
    /// Neden 5? Ustel geri cekilme ile 2+4+8+16 = 30 dakikaya karsilik
    /// geliyor. Gecici bir kesinti (servis yeniden baslatma, ag sorunu)
    /// bu süre içinde neredeyse her zaman duzelir. Duzelmiyorsa sorun
    /// geçici degildir ve sonsuza kadar denemek yalnızca kuyrugu tikar.
    /// </summary>
    private const int MaxRetries = 5;

    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<ProcessOutboxMessagesCommandHandler> _logger;

    /// <summary>
    /// Mesaj türü -> isleyici eslemesi.
    ///
    /// DI konteyneri kayıtlı TÜM IOutboxMessageHandler'lari enjekte
    /// ediyor; biz bunlari bir sozluge ceviriyoruz. Yeni bir isleyici
    /// eklemek için bu dosyaya dokunmaya gerek yok -- yalnızca
    /// DI'ya kaydetmek yeterli.
    /// </summary>
    private readonly Dictionary<string, IOutboxMessageHandler> _handlers;

    public ProcessOutboxMessagesCommandHandler(
        IApplicationDbContext context,
        IDateTimeProvider clock,
        IEnumerable<IOutboxMessageHandler> handlers,
        ILogger<ProcessOutboxMessagesCommandHandler> logger)
    {
        _context = context;
        _clock = clock;
        _logger = logger;

        // ToDictionary, aynı türü iki isleyici sahiplenirse
        // ArgumentException firlatir. Bu ISTEDIGIMIZ davranis:
        // boyle bir çakışma sessizce "biri kazanir" seklinde
        // cozulseydi, hangi isleyicinin calistigi tesadufe kalırdı.
        _handlers = handlers.ToDictionary(h => h.MessageType, StringComparer.Ordinal);
    }

    public async Task<Result<OutboxProcessingResult>> Handle(
        ProcessOutboxMessagesCommand request,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        // ==============================================================
        // BEKLEYEN MESAJLARI SEC
        // ==============================================================
        // Filtreyi ENTITY'deki IsReadyToProcess ile değil, SORGUDA
        // yazıyorum. Sebep: IsReadyToProcess bir C# metodu; EF önü
        // SQL'e ceviremez ve tabloyu KOMPLE bellege cekerdi.
        //
        // Kural aynı, yeri farklı. Entity'deki metot birim testlerde
        // ve tekil kontrollerde kullanılıyor.
        //
        // ix_outbox_unprocessed index'i bu sorguyu karsiliyor.
        var messages = await _context.OutboxMessages
            .Where(m => m.ProcessedAt == null
                     && !m.IsDeadLettered
                     && (m.NextRetryAt == null || m.NextRetryAt <= now))

            // ESKİ MESAJ ONCE.
            //
            // Sirali islemek sart: "rezervasyon oluşturuldu" bildirimi
            // "rezervasyon süresi doldu" bildiriminden SONRA gitseydi
            // kullanıcı olaylari ters sırada gorurdu.
            .OrderBy(m => m.CreatedAt)
            .Take(request.BatchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (messages.Count == 0)
        {
            return Result.Success(new OutboxProcessingResult(0, 0, 0));
        }

        var processed = 0;
        var failed = 0;
        var deadLettered = 0;

        foreach (var message in messages)
        {
            // ==============================================================
            // CORRELATION ID'YI MESAJDAN DEVRAL -- PDF Sprint 16
            // ==============================================================
            // PDF: correlation ID "Background job log" içinde de
            // kullanılmalıdır.
            //
            // Arka plan isinin HTTP baglami yok, yani kendi correlation
            // ID'sini uretemez. Ama ISLEDIGI mesaj, önü olusturan HTTP
            // isteginin ID'sini tasiyor (OutboxCorrelationInterceptor
            // yazıyor).
            //
            // Burada önü bir log kapsamina (scope) alarak zinciri
            // TAMAMLIYORUZ:
            //
            //   HTTP isteği         CorrelationId = abc
            //     -> Outbox kaydı   CorrelationId = abc
            //        -> Bu is       CorrelationId = abc
            //           -> E-posta  CorrelationId = abc
            //
            // Boylece "kullanıcının su isteği hangi e-postayi
            // tetikledi?" sorusu tek bir sorguyla cevaplanabiliyor --
            // adimlar farklı zamanlarda ve farklı process'lerde
            // calismis olsa bile.
            //
            // Kapsam DONGUNUN ICINDE: her mesajin kendi ID'si var,
            // disarida acsaydik hepsi ilk mesajin ID'siyle loglanirdi.
            // ==============================================================
            using var kapsam = string.IsNullOrWhiteSpace(message.CorrelationId)
                ? null
                : _logger.BeginScope(new Dictionary<string, object>
                {
                    ["CorrelationId"] = message.CorrelationId,
                    ["OutboxMessageId"] = message.Id,
                });

            // ==========================================================
            // HER MESAJ KENDİ BASINA -- BIRI DIGERINI DEVIRMESIN
            // ==========================================================
            // try/catch DONGUNUN ICINDE. Disinda olsaydı tek bir bozuk
            // mesaj (örneğin geçersiz JSON) partinin geri kalanini da
            // durdururdu ve o mesaj her turda aynı engeli olustururdu:
            // kuyruk kalici olarak tikanirdi.
            //
            // PDF: "Başarısız işlem yeniden denenmelidir." -- yeniden
            // denenmesi gereken YALNIZCA başarısız olan mesaj.
            // ==========================================================
            try
            {
                if (!_handlers.TryGetValue(message.Type, out var handler))
                {
                    // Isleyicisi olmayan mesaj.
                    //
                    // Bu bir PROGRAMLAMA hatası: birisi Outbox'a mesaj
                    // yazmis ama isleyicisini kaydetmeyi unutmus.
                    // Sessizce gecmek, bildirimlerin hiç gitmemesine ve
                    // kimsenin fark etmemesine yol acardi.
                    //
                    // Başarısız sayiyorum ki RetryCount artsin, esik
                    // asilinca dead letter olsun ve izleme ekraninda
                    // gorunsun.
                    LogHandlerNotFound(_logger, message.Type, message.Id);

                    message.MarkAsFailed(
                        $"'{message.Type}' türü için kayıtlı isleyici yok.",
                        MaxRetries,
                        now);

                    if (message.IsDeadLettered)
                    {
                        deadLettered++;
                    }
                    else
                    {
                        failed++;
                    }

                    continue;
                }

                await handler.HandleAsync(message.Payload, cancellationToken).ConfigureAwait(false);

                message.MarkAsProcessed(now);
                processed++;

                LogProcessed(_logger, message.Type, message.Id);
            }
            catch (OperationCanceledException)
            {
                // Uygulama kapaniyor. Bu bir HATA DEĞİL.
                //
                // MarkAsFailed cagirsaydik, her yeniden baslatmada
                // isleme sirasindaki mesajlarin RetryCount'u boşuna
                // artardi ve saglam mesajlar zamanla dead letter
                // olurdu. Mesaji olduğu gibi birakiyoruz; bir
                // sonraki calismada bastan denenecek.
                throw;
            }
#pragma warning disable CA1031 // Genel istisna yakalama
            // ==========================================================
            // NEDEN GENEL catch? -- CA1031 BILINCLI OLARAK SUSTURULDU
            // ==========================================================
            // Analiz kuralı haklı: normalde yalnızca bekledigin
            // istisnalari yakalamalisin, çünkü beklenmedik bir hatayi
            // yutmak sorunu gizler.
            //
            // Ama burada durum tersine: bu bir ARKA PLAN ISLEYICISI ve
            // isleyiciler çok cesitli istisnalar firlatabilir --
            // SmtpException, JsonException, HttpRequestException,
            // DbUpdateException, NullReferenceException...
            //
            // Hepsini tek tek saymak hem imkansiz hem de yeni bir
            // isleyici eklendiginde listeyi guncellemeyi unutmak
            // kacinilmaz. Sayilmayan bir istisna job'i tumden
            // cokertirdi ve TÜM kuyruk dururdu.
            //
            // Hatayi YUTMUYORUZ: veritabanina ErrorMessage olarak
            // yazıyor, loga hata seviyesinde dusuyor ve izleme
            // ekraninda görünüyor. Yani gizlenmiyor, KAYIT ALTINA
            // aliniyor -- bir arka plan islemcisinden beklenen tam
            // olarak budur.
            // ==========================================================
            catch (Exception ex)
#pragma warning restore CA1031
            {
                // Mesajin tamami değil, ilk 1000 karakteri.
                // Yigin izi (stack trace) bazen onbinlerce karakter
                // olur; tabloyu sismekten koruyoruz. Tam ayrinti
                // zaten logda.
                var error = ex.ToString();
                message.MarkAsFailed(
                    error.Length > 1000 ? error[..1000] : error,
                    MaxRetries,
                    now);

                if (message.IsDeadLettered)
                {
                    deadLettered++;
                    LogDeadLettered(_logger, message.Type, message.Id, message.RetryCount, ex);
                }
                else
                {
                    failed++;
                    LogFailed(_logger, message.Type, message.Id, message.RetryCount, ex);
                }
            }
        }

        // ==============================================================
        // TEK SaveChanges -- DONGUNUN ICINDE DEĞİL
        // ==============================================================
        // Her mesajtan sonra kaydetseydik 20 ayrı veritabani gidis
        // donusu olurdu. Burada tek turda hepsi yaziliyor.
        //
        // Riski kabul ediyoruz: kayıt oncesi cokme olursa islenmis
        // mesajlar tekrar islenir. Isleyiciler zaten idempotent
        // olmak zorunda olduğu için bu tolere edilebilir.
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // PDF: "Job sonuclari loglanmalidir."
        LogBatchCompleted(_logger, processed, failed, deadLettered);

        return Result.Success(new OutboxProcessingResult(processed, failed, deadLettered));
    }

    // ==================================================================
    // KAYNAK URETECI ILE LOGLAMA ([LoggerMessage])
    // ==================================================================
    // logger.LogInformation("... {A} {B}", a, b) yazmak yerine bunu
    // kullanıyorum çünkü:
    //   - Kutu (boxing) ve dizi tahsisi olmuyor
    //   - Log seviyesi kapaliysa parametreler hiç degerlendirilmiyor
    //   - CA1848 analiz kuralı bunu zorunlu kiliyor
    //
    // Kod uretecinin metotlari doldurabilmesi için sinif `partial`.
    // ==================================================================

    [LoggerMessage(
        EventId = 9001,
        Level = LogLevel.Debug,
        Message = "Outbox mesaji islendi. Tur: {Type}, Id: {MessageId}")]
    private static partial void LogProcessed(ILogger logger, string type, Guid messageId);

    [LoggerMessage(
        EventId = 9002,
        Level = LogLevel.Warning,
        Message = "Outbox mesaji başarısız, yeniden denenecek. Tur: {Type}, Id: {MessageId}, Deneme: {RetryCount}")]
    private static partial void LogFailed(
        ILogger logger, string type, Guid messageId, int retryCount, Exception exception);

    [LoggerMessage(
        EventId = 9003,
        Level = LogLevel.Error,
        Message = "Outbox mesaji KALICI OLARAK başarısız (dead letter). Tur: {Type}, Id: {MessageId}, Deneme: {RetryCount}")]
    private static partial void LogDeadLettered(
        ILogger logger, string type, Guid messageId, int retryCount, Exception exception);

    [LoggerMessage(
        EventId = 9004,
        Level = LogLevel.Error,
        Message = "'{Type}' türü için kayıtlı Outbox isleyicisi yok. Id: {MessageId}")]
    private static partial void LogHandlerNotFound(ILogger logger, string type, Guid messageId);

    [LoggerMessage(
        EventId = 9005,
        Level = LogLevel.Information,
        Message = "Outbox partisi tamamlandı. Islenen: {Processed}, Başarısız: {Failed}, Dead letter: {DeadLettered}")]
    private static partial void LogBatchCompleted(
        ILogger logger, int processed, int failed, int deadLettered);
}
