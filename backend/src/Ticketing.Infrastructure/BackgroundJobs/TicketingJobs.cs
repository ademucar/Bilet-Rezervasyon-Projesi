using Hangfire;
using MediatR;
using Microsoft.Extensions.Logging;
using Ticketing.Application.Common.Observability;
using Ticketing.Application.Features.Events;
using Ticketing.Application.Features.Notifications;
using Ticketing.Application.Features.Outbox;
using Ticketing.Application.Features.Reservations;

namespace Ticketing.Infrastructure.BackgroundJobs;

/// <summary>
/// ARKA PLAN ISLERI -- PDF Sprint 9
///
/// PDF'in istedigi bes is:
///   1. Süresi dolan rezervasyonları iptal etme
///   2. Outbox mesajlarini isleme
///   3. Başarısız mesajlari yeniden deneme
///   4. Yaklasan etkinlik hatirlatmasi
///   5. Günlük satış özeti oluşturma
///
/// BU SINIFLAR NEDEN BU KADAR INCE?
///
/// Her is yalnızca bir MediatR komutu gönderiyor ve sonucu logluyor.
/// Is mantiginin TEK SATIRI bile burada değil.
///
/// Sebep mimari: Infrastructure katmani "isin nasil tetiklendigini"
/// bilir, "isin ne olduğunu" bilmez. Zamanlayiciyi Hangfire'dan
/// Quartz'a ya da bir Kubernetes CronJob'ina cevirmek istersek
/// yalnızca bu dosya degisir.
///
/// Ikinci fayda: is mantığı Application'da olduğu için HTTP ucundan
/// da tetiklenebiliyor (admin "simdi calistir" diyebiliyor) ve birim
/// testlerinde Hangfire'a hiç ihtiyac duyulmuyor.
/// </summary>
public sealed partial class TicketingJobs
{
    private readonly ISender _sender;
    private readonly ILogger<TicketingJobs> _logger;

    public TicketingJobs(ISender sender, ILogger<TicketingJobs> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    // 1) SURESI DOLAN REZERVASYONLARI İPTAL ETME

    /// <summary>
    /// Süresi dolmuş rezervasyonları iptal eder ve koltukları serbest birakir.
    /// </summary>
    /// <remarks>
    /// [DisableConcurrentExecution] -- NEDEN ŞART?
    ///
    /// Bu is dakikada bir çalışıyor. Bir calisma 70 saniye surerse
    /// (çok rezervasyon birikmisse olur) Hangfire varsayılan olarak
    /// IKINCISINI de baslatir.
    ///
    /// O anda iki calisma AYNI rezervasyonları secer ve ikisi de
    /// Expire() cagirir. Birinci kaydeder, ikinci
    /// DbUpdateConcurrencyException alır (xmin eslesmez) ve TÜM
    /// partisi başarısız olur.
    ///
    /// Yani sistem yanlış veri uretmez -- eszamanlilik korumamiz
    /// çalışır -- ama is boşuna başarısız olur ve loglar gurultuye
    /// bogulur.
    ///
    /// timeoutInSeconds: kilidi bekleme süresi. 0 verirsek beklemez,
    /// hemen atlar. 60 saniye beklemesi daha iyi: uzun suren bir
    /// calismanin ardindan bir sonraki hemen devam eder.
    /// </remarks>
    [DisableConcurrentExecution(timeoutInSeconds: 60)]

    // AutomaticRetry KAPALI.
    //
    // Bu is dakikada bir zaten çalışıyor. Hangfire'in ayrıca
    // 10 kez yeniden denemesi, geçici bir veritabani sorunu
    // anında 10 kopya olusturmak demek. Başarısız olsun, bir
    // dakika sonra normal turunda tekrar denesin.
    [AutomaticRetry(Attempts = 0)]
    public async Task ExpireReservationsAsync(CancellationToken cancellationToken)
    {
        // PDF Sprint 16: "Background job islemleri" izlenmelidir.
        //
        // Her is kendi izleme kapsamini aciyor. Boylece izleme
        // arayuzunde is, HTTP isteklerinden ayrı bir kok (root)
        // olarak görünüyor ve icindeki veritabani/Redis cagrilari
        // ona bağlanıyor.
        //
        // Kapsam olmadan: isin urettigi SQL sorgulari izlemede
        // SAHIPSIZ görünürdü -- "bu sorgu nereden geldi?"
        // sorusunun cevabi olmazdi.
        //
        // activity null olabilir (dinleyici yoksa); using bunu
        // sorunsuz karsiliyor.
        using var activity = AppActivitySource.StartJob(nameof(ExpireReservationsAsync));

        var result = await _sender
            .Send(new ExpireReservationsCommand(), cancellationToken)
            .ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            // Basarisizligi ISTISNA olarak firlatiyorum.
            //
            // Sessizce loglasaydim Hangfire is'i "başarılı" sayar ve
            // izleme ekraninda yesil görünürdü. Arka plan islerinde
            // en tehlikeli durum, calismadigi halde çalışıyor
            // gorunmektir.
            throw new InvalidOperationException(
                $"Rezervasyon temizligi başarısız: {result.Error.Code} - {result.Error.Message}");
        }

        // PDF: "Job sonuclari loglanmalidir."
        if (result.Value > 0)
        {
            LogReservationsExpired(_logger, result.Value);
        }
    }

    // 2 ve 3) OUTBOX MESAJLARINI ISLEME + BASARISIZLARI YENIDEN DENEME

    /// <summary>
    /// Bekleyen Outbox mesajlarini isler.
    /// </summary>
    /// <remarks>
    /// "BASARISIZ MESAJLARI YENIDEN DENEME" NEDEN AYRI BIR IS DEĞİL?
    ///
    /// PDF bu ikisini ayrı maddeler olarak sayiyor. Ayrı iki is
    /// yazmayi dusundum ve VAZGECTIM.
    ///
    /// Sebep: yeniden denenecek mesaj, bekleyen bir mesajdan yalnızca
    /// RetryCount > 0 olmasiyla ayriliyor. Processor'in sorgusu
    /// zaten "islenmemis VE (NextRetryAt boş VEYA zamani gelmis)"
    /// diyor -- yani yeni mesajlar ile yeniden denenecekleri AYNI
    /// sorgu topluyor.
    ///
    /// Ayrı bir is yazsaydım aynı tabloyu aynı kosulla tarayan iki
    /// is olurdu ve ikisi aynı anda calisip aynı mesaji islemeye
    /// calisirdi.
    ///
    /// PDF'in istedigi DAVRANIS (başarısız mesaj yeniden denenmeli)
    /// tam olarak karsilaniyor; ayrı bir zamanlayici gerekmiyor.
    /// Ustel geri cekilme OutboxMessage.MarkAsFailed içinde.
    /// </remarks>
    [DisableConcurrentExecution(timeoutInSeconds: 30)]
    [AutomaticRetry(Attempts = 0)]
    public async Task ProcessOutboxAsync(CancellationToken cancellationToken)
    {
        using var activity = AppActivitySource.StartJob(nameof(ProcessOutboxAsync));

        var result = await _sender
            .Send(new ProcessOutboxMessagesCommand(), cancellationToken)
            .ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                $"Outbox işlemesi başarısız: {result.Error.Code} - {result.Error.Message}");
        }

        var summary = result.Value;

        // Sifir mesaj islendiginde LOG YAZMIYORUZ.
        //
        // Bu is 30 saniyede bir çalışıyor: günde 2880 kez. Her
        // calismada "0 mesaj islendi" yazsaydım loglar günde 2880
        // anlamsiz satirla dolar ve gerçek hatalar arasında
        // kaybolurdu.
        if (summary.Processed > 0 || summary.Failed > 0 || summary.DeadLettered > 0)
        {
            LogOutboxSummary(_logger, summary.Processed, summary.Failed, summary.DeadLettered);
        }
    }

    // 4) YAKLASAN ETKİNLİK HATIRLATMASI

    [DisableConcurrentExecution(timeoutInSeconds: 120)]
    [AutomaticRetry(Attempts = 0)]
    public async Task SendEventRemindersAsync(CancellationToken cancellationToken)
    {
        using var activity = AppActivitySource.StartJob(nameof(SendEventRemindersAsync));

        var result = await _sender
            .Send(new SendEventRemindersCommand(), cancellationToken)
            .ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                $"Etkinlik hatirlatmasi başarısız: {result.Error.Code} - {result.Error.Message}");
        }

        LogRemindersQueued(_logger, result.Value);
    }

    // 5c) SURESI DOLMAK UZERE OLAN REZERVASYONLARI UYAR
    //
    // PDF Sprint 14: "Rezervasyon süresi dolmak uzereyken" bildirim.
    //
    // DAKIKADA BIR çalışıyor -- uyarinin zamaninda gitmesi için sart.
    // Bes dakikada bir calissaydi, 3 dakikalik uyarı penceresini
    // tamamen KACIRABILIRDI.

    [DisableConcurrentExecution(timeoutInSeconds: 30)]
    [AutomaticRetry(Attempts = 0)]
    public async Task NotifyExpiringReservationsAsync(CancellationToken cancellationToken)
    {
        using var activity = AppActivitySource.StartJob(nameof(NotifyExpiringReservationsAsync));

        var result = await _sender
            .Send(new NotifyExpiringReservationsCommand(), cancellationToken)
            .ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                $"Süre uyarısı başarısız: {result.Error.Code} - {result.Error.Message}");
        }

        if (result.Value > 0)
        {
            LogExpiringWarned(_logger, result.Value);
        }
    }

    // 5b) GECMIS ETKINLIKLERI TAMAMLA -- Sprint 12 için eklendi
    //
    // PDF Sprint 9 bu isi SAYMIYOR. Sprint 12'yi yazarken ortaya cikti:
    // "Etkinlik tamamlanmadan yorum yapılamaz" kuralı, etkinlikleri
    // Completed durumuna gecirecek bir mekanizma OLMADAN hiçbir zaman
    // saglanamazdi.
    //
    // Yani PDF'in bir sprintteki kuralı, başka bir sprintte olmayan bir
    // isi zorunlu kiliyor. Sprintleri tek tek okuyup "bu gerçekten
    // çalışır mi?" diye sormanin karşılığı.

    [DisableConcurrentExecution(timeoutInSeconds: 60)]
    [AutomaticRetry(Attempts = 0)]
    public async Task CompletePastEventsAsync(CancellationToken cancellationToken)
    {
        using var activity = AppActivitySource.StartJob(nameof(CompletePastEventsAsync));

        var result = await _sender
            .Send(new CompletePastEventsCommand(), cancellationToken)
            .ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                $"Etkinlik tamamlama başarısız: {result.Error.Code} - {result.Error.Message}");
        }

        if (result.Value > 0)
        {
            LogEventsCompleted(_logger, result.Value);
        }
    }

    // 5) GUNLUK SATIS OZETI

    [DisableConcurrentExecution(timeoutInSeconds: 120)]
    [AutomaticRetry(Attempts = 0)]
    public async Task GenerateDailySalesSummaryAsync(CancellationToken cancellationToken)
    {
        using var activity = AppActivitySource.StartJob(nameof(GenerateDailySalesSummaryAsync));

        var result = await _sender
            .Send(new GenerateDailySalesSummaryCommand(), cancellationToken)
            .ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                $"Günlük satış özeti başarısız: {result.Error.Code} - {result.Error.Message}");
        }

        var summary = result.Value;

        // DateOnly'yi OLDUGU GIBI geciyorum, ToString ile metne
        // cevirmeden.
        //
        // CA1873 bunu yakaladi: parametreyi burada bicimlendirseydik,
        // log seviyesi kapalı olsa BILE her calismada bir metin
        // ureterek bosa tahsis yapardik. Kaynak ureteci, metni
        // yalnızca log gerçekten yazilacaksa olusturuyor.
        LogDailySummary(
            _logger,
            summary.Date,
            summary.TicketCount,
            summary.GrossAmount,
            summary.Currency);
    }

    // LOGLAMA -- kaynak ureteci ile (CA1848)

    [LoggerMessage(
        EventId = 9101,
        Level = LogLevel.Information,
        Message = "{Count} rezervasyonun süresi doldu, koltukları serbest birakildi.")]
    private static partial void LogReservationsExpired(ILogger logger, int count);

    [LoggerMessage(
        EventId = 9102,
        Level = LogLevel.Information,
        Message = "Outbox islendi. Başarılı: {Processed}, Başarısız: {Failed}, Dead letter: {DeadLettered}")]
    private static partial void LogOutboxSummary(
        ILogger logger, int processed, int failed, int deadLettered);

    [LoggerMessage(
        EventId = 9103,
        Level = LogLevel.Information,
        Message = "{Count} etkinlik hatirlatmasi kuyruga alındı.")]
    private static partial void LogRemindersQueued(ILogger logger, int count);

    [LoggerMessage(
        EventId = 9106,
        Level = LogLevel.Information,
        Message = "{Count} rezervasyon için süre uyarısı gönderildi.")]
    private static partial void LogExpiringWarned(ILogger logger, int count);

    [LoggerMessage(
        EventId = 9105,
        Level = LogLevel.Information,
        Message = "{Count} etkinlik tamamlandı olarak isaretlendi.")]
    private static partial void LogEventsCompleted(ILogger logger, int count);

    [LoggerMessage(
        EventId = 9104,
        Level = LogLevel.Information,
        Message = "{Date} satış özeti hazır. Bilet: {TicketCount}, Brut: {GrossAmount} {Currency}")]
    private static partial void LogDailySummary(
        ILogger logger, DateOnly date, int ticketCount, decimal grossAmount, string currency);
}
