using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ticketing.Application.Abstractions.Messaging;
using Ticketing.Application.Abstractions.Persistence;
using Ticketing.Application.Abstractions.Time;
using Ticketing.Application.Common.Results;

namespace Ticketing.Application.Features.Outbox;

/// <summary>Bir Outbox turunun calisma sonucu. Loglama ve test icin.</summary>
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
/// Sinirsiz olsaydi, e-posta servisi bir gun kapali kalip 50.000
/// mesaj biriktiginde job hepsini tek seferde islemeye calisir ve
/// dakikalarca calisirdi. Bu sirada Hangfire bir sonraki calismayi
/// baslatamaz, izleme ekrani "takildi" gorunur.
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
    /// hata kaydi olusturulmalidir."
    ///
    /// Neden 5? Ustel geri cekilme ile 2+4+8+16 = 30 dakikaya karsilik
    /// geliyor. Gecici bir kesinti (servis yeniden baslatma, ag sorunu)
    /// bu sure icinde neredeyse her zaman duzelir. Duzelmiyorsa sorun
    /// gecici degildir ve sonsuza kadar denemek yalnizca kuyrugu tikar.
    /// </summary>
    private const int MaxRetries = 5;

    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<ProcessOutboxMessagesCommandHandler> _logger;

    /// <summary>
    /// Mesaj turu -> isleyici eslemesi.
    ///
    /// DI konteyneri kayitli TUM IOutboxMessageHandler'lari enjekte
    /// ediyor; biz bunlari bir sozluge ceviriyoruz. Yeni bir isleyici
    /// eklemek icin bu dosyaya dokunmaya gerek yok -- yalnizca
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

        // ToDictionary, ayni turu iki isleyici sahiplenirse
        // ArgumentException firlatir. Bu ISTEDIGIMIZ davranis:
        // boyle bir cakisma sessizce "biri kazanir" seklinde
        // cozulseydi, hangi isleyicinin calistigi tesadufe kalirdi.
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
        // Filtreyi ENTITY'deki IsReadyToProcess ile degil, SORGUDA
        // yaziyorum. Sebep: IsReadyToProcess bir C# metodu; EF onu
        // SQL'e ceviremez ve tabloyu KOMPLE bellege cekerdi.
        //
        // Kural ayni, yeri farkli. Entity'deki metot birim testlerde
        // ve tekil kontrollerde kullaniliyor.
        //
        // ix_outbox_unprocessed index'i bu sorguyu karsiliyor.
        var messages = await _context.OutboxMessages
            .Where(m => m.ProcessedAt == null
                     && !m.IsDeadLettered
                     && (m.NextRetryAt == null || m.NextRetryAt <= now))

            // ESKI MESAJ ONCE.
            //
            // Sirali islemek sart: "rezervasyon olusturuldu" bildirimi
            // "rezervasyon suresi doldu" bildiriminden SONRA gitseydi
            // kullanici olaylari ters sirada gorurdu.
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
            // PDF: correlation ID "Background job log" icinde de
            // kullanilmalidir.
            //
            // Arka plan isinin HTTP baglami yok, yani kendi correlation
            // ID'sini uretemez. Ama ISLEDIGI mesaj, onu olusturan HTTP
            // isteginin ID'sini tasiyor (OutboxCorrelationInterceptor
            // yaziyor).
            //
            // Burada onu bir log kapsamina (scope) alarak zinciri
            // TAMAMLIYORUZ:
            //
            //   HTTP istegi         CorrelationId = abc
            //     -> Outbox kaydi   CorrelationId = abc
            //        -> Bu is       CorrelationId = abc
            //           -> E-posta  CorrelationId = abc
            //
            // Boylece "kullanicinin su istegi hangi e-postayi
            // tetikledi?" sorusu tek bir sorguyla cevaplanabiliyor --
            // adimlar farkli zamanlarda ve farkli process'lerde
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
            // HER MESAJ KENDI BASINA -- BIRI DIGERINI DEVIRMESIN
            // ==========================================================
            // try/catch DONGUNUN ICINDE. Disinda olsaydi tek bir bozuk
            // mesaj (ornegin gecersiz JSON) partinin geri kalanini da
            // durdururdu ve o mesaj her turda ayni engeli olustururdu:
            // kuyruk kalici olarak tikanirdi.
            //
            // PDF: "Basarisiz islem yeniden denenmelidir." -- yeniden
            // denenmesi gereken YALNIZCA basarisiz olan mesaj.
            // ==========================================================
            try
            {
                if (!_handlers.TryGetValue(message.Type, out var handler))
                {
                    // Isleyicisi olmayan mesaj.
                    //
                    // Bu bir PROGRAMLAMA hatasi: birisi Outbox'a mesaj
                    // yazmis ama isleyicisini kaydetmeyi unutmus.
                    // Sessizce gecmek, bildirimlerin hic gitmemesine ve
                    // kimsenin fark etmemesine yol acardi.
                    //
                    // Basarisiz sayiyorum ki RetryCount artsin, esik
                    // asilinca dead letter olsun ve izleme ekraninda
                    // gorunsun.
                    LogHandlerNotFound(_logger, message.Type, message.Id);

                    message.MarkAsFailed(
                        $"'{message.Type}' turu icin kayitli isleyici yok.",
                        MaxRetries,
                        now);

                    if (message.IsDeadLettered) { deadLettered++; } else { failed++; }

                    continue;
                }

                await handler.HandleAsync(message.Payload, cancellationToken).ConfigureAwait(false);

                message.MarkAsProcessed(now);
                processed++;

                LogProcessed(_logger, message.Type, message.Id);
            }
            catch (OperationCanceledException)
            {
                // Uygulama kapaniyor. Bu bir HATA DEGIL.
                //
                // MarkAsFailed cagirsaydik, her yeniden baslatmada
                // isleme sirasindaki mesajlarin RetryCount'u bosuna
                // artardi ve saglam mesajlar zamanla dead letter
                // olurdu. Mesaji oldugu gibi birakiyoruz; bir
                // sonraki calismada bastan denenecek.
                throw;
            }
#pragma warning disable CA1031 // Genel istisna yakalama
            // ==========================================================
            // NEDEN GENEL catch? -- CA1031 BILINCLI OLARAK SUSTURULDU
            // ==========================================================
            // Analiz kurali hakli: normalde yalnizca bekledigin
            // istisnalari yakalamalisin, cunku beklenmedik bir hatayi
            // yutmak sorunu gizler.
            //
            // Ama burada durum tersine: bu bir ARKA PLAN ISLEYICISI ve
            // isleyiciler cok cesitli istisnalar firlatabilir --
            // SmtpException, JsonException, HttpRequestException,
            // DbUpdateException, NullReferenceException...
            //
            // Hepsini tek tek saymak hem imkansiz hem de yeni bir
            // isleyici eklendiginde listeyi guncellemeyi unutmak
            // kacinilmaz. Sayilmayan bir istisna job'i tumden
            // cokertirdi ve TUM kuyruk dururdu.
            //
            // Hatayi YUTMUYORUZ: veritabanina ErrorMessage olarak
            // yaziyor, loga hata seviyesinde dusuyor ve izleme
            // ekraninda gorunuyor. Yani gizlenmiyor, KAYIT ALTINA
            // aliniyor -- bir arka plan islemcisinden beklenen tam
            // olarak budur.
            // ==========================================================
            catch (Exception ex)
#pragma warning restore CA1031
            {
                // Mesajin tamami degil, ilk 1000 karakteri.
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
        // TEK SaveChanges -- DONGUNUN ICINDE DEGIL
        // ==============================================================
        // Her mesajtan sonra kaydetseydik 20 ayri veritabani gidis
        // donusu olurdu. Burada tek turda hepsi yaziliyor.
        //
        // Riski kabul ediyoruz: kayit oncesi cokme olursa islenmis
        // mesajlar tekrar islenir. Isleyiciler zaten idempotent
        // olmak zorunda oldugu icin bu tolere edilebilir.
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // PDF: "Job sonuclari loglanmalidir."
        LogBatchCompleted(_logger, processed, failed, deadLettered);

        return Result.Success(new OutboxProcessingResult(processed, failed, deadLettered));
    }

    // ==================================================================
    // KAYNAK URETECI ILE LOGLAMA ([LoggerMessage])
    // ==================================================================
    // logger.LogInformation("... {A} {B}", a, b) yazmak yerine bunu
    // kullaniyorum cunku:
    //   - Kutu (boxing) ve dizi tahsisi olmuyor
    //   - Log seviyesi kapaliysa parametreler hic degerlendirilmiyor
    //   - CA1848 analiz kurali bunu zorunlu kiliyor
    //
    // Kod uretecinin metotlari doldurabilmesi icin sinif `partial`.
    // ==================================================================

    [LoggerMessage(
        EventId = 9001,
        Level = LogLevel.Debug,
        Message = "Outbox mesaji islendi. Tur: {Type}, Id: {MessageId}")]
    private static partial void LogProcessed(ILogger logger, string type, Guid messageId);

    [LoggerMessage(
        EventId = 9002,
        Level = LogLevel.Warning,
        Message = "Outbox mesaji basarisiz, yeniden denenecek. Tur: {Type}, Id: {MessageId}, Deneme: {RetryCount}")]
    private static partial void LogFailed(
        ILogger logger, string type, Guid messageId, int retryCount, Exception exception);

    [LoggerMessage(
        EventId = 9003,
        Level = LogLevel.Error,
        Message = "Outbox mesaji KALICI OLARAK basarisiz (dead letter). Tur: {Type}, Id: {MessageId}, Deneme: {RetryCount}")]
    private static partial void LogDeadLettered(
        ILogger logger, string type, Guid messageId, int retryCount, Exception exception);

    [LoggerMessage(
        EventId = 9004,
        Level = LogLevel.Error,
        Message = "'{Type}' turu icin kayitli Outbox isleyicisi yok. Id: {MessageId}")]
    private static partial void LogHandlerNotFound(ILogger logger, string type, Guid messageId);

    [LoggerMessage(
        EventId = 9005,
        Level = LogLevel.Information,
        Message = "Outbox partisi tamamlandi. Islenen: {Processed}, Basarisiz: {Failed}, Dead letter: {DeadLettered}")]
    private static partial void LogBatchCompleted(
        ILogger logger, int processed, int failed, int deadLettered);
}
