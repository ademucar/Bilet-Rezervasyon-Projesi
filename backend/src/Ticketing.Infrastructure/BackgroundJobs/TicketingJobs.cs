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
/// ==================================================================
/// ARKA PLAN ISLERI -- PDF Sprint 9
/// ==================================================================
/// PDF'in istedigi bes is:
///   1. Suresi dolan rezervasyonlari iptal etme
///   2. Outbox mesajlarini isleme
///   3. Basarisiz mesajlari yeniden deneme
///   4. Yaklasan etkinlik hatirlatmasi
///   5. Gunluk satis ozeti olusturma
///
/// ------------------------------------------------------------------
/// BU SINIFLAR NEDEN BU KADAR INCE?
/// ------------------------------------------------------------------
/// Her is yalnizca bir MediatR komutu gonderiyor ve sonucu logluyor.
/// Is mantiginin TEK SATIRI bile burada degil.
///
/// Sebep mimari: Infrastructure katmani "isin nasil tetiklendigini"
/// bilir, "isin ne oldugunu" bilmez. Zamanlayiciyi Hangfire'dan
/// Quartz'a ya da bir Kubernetes CronJob'ina cevirmek istersek
/// yalnizca bu dosya degisir.
///
/// Ikinci fayda: is mantigi Application'da oldugu icin HTTP ucundan
/// da tetiklenebiliyor (admin "simdi calistir" diyebiliyor) ve birim
/// testlerinde Hangfire'a hic ihtiyac duyulmuyor.
/// ==================================================================
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

    // ==================================================================
    // 1) SURESI DOLAN REZERVASYONLARI IPTAL ETME
    // ==================================================================

    /// <summary>
    /// Suresi dolmus rezervasyonlari iptal eder ve koltuklari serbest birakir.
    /// </summary>
    /// <remarks>
    /// ==============================================================
    /// [DisableConcurrentExecution] -- NEDEN SART?
    /// ==============================================================
    /// Bu is dakikada bir calisiyor. Bir calisma 70 saniye surerse
    /// (cok rezervasyon birikmisse olur) Hangfire varsayilan olarak
    /// IKINCISINI de baslatir.
    ///
    /// O anda iki calisma AYNI rezervasyonlari secer ve ikisi de
    /// Expire() cagirir. Birinci kaydeder, ikinci
    /// DbUpdateConcurrencyException alir (xmin eslesmez) ve TUM
    /// partisi basarisiz olur.
    ///
    /// Yani sistem yanlis veri uretmez -- eszamanlilik korumamiz
    /// calisir -- ama is bosuna basarisiz olur ve loglar gurultuye
    /// bogulur.
    ///
    /// timeoutInSeconds: kilidi bekleme suresi. 0 verirsek beklemez,
    /// hemen atlar. 60 saniye beklemesi daha iyi: uzun suren bir
    /// calismanin ardindan bir sonraki hemen devam eder.
    /// ==============================================================
    /// </remarks>
    [DisableConcurrentExecution(timeoutInSeconds: 60)]

    // AutomaticRetry KAPALI.
    //
    // Bu is dakikada bir zaten calisiyor. Hangfire'in ayrica
    // 10 kez yeniden denemesi, gecici bir veritabani sorunu
    // aninda 10 kopya olusturmak demek. Basarisiz olsun, bir
    // dakika sonra normal turunda tekrar denesin.
    [AutomaticRetry(Attempts = 0)]
    public async Task ExpireReservationsAsync(CancellationToken cancellationToken)
    {
        // ==========================================================
        // PDF Sprint 16: "Background job islemleri" izlenmelidir.
        // ==========================================================
        // Her is kendi izleme kapsamini aciyor. Boylece izleme
        // arayuzunde is, HTTP isteklerinden ayri bir kok (root)
        // olarak gorunuyor ve icindeki veritabani/Redis cagrilari
        // ona baglaniyor.
        //
        // Kapsam olmadan: isin urettigi SQL sorgulari izlemede
        // SAHIPSIZ gorunurdu -- "bu sorgu nereden geldi?"
        // sorusunun cevabi olmazdi.
        //
        // activity null olabilir (dinleyici yoksa); using bunu
        // sorunsuz karsiliyor.
        // ==========================================================
        using var activity = AppActivitySource.StartJob(nameof(ExpireReservationsAsync));

        var result = await _sender
            .Send(new ExpireReservationsCommand(), cancellationToken)
            .ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            // Basarisizligi ISTISNA olarak firlatiyorum.
            //
            // Sessizce loglasaydik Hangfire is'i "basarili" sayar ve
            // izleme ekraninda yesil gorunurdu. Arka plan islerinde
            // en tehlikeli durum, calismadigi halde calisiyor
            // gorunmektir.
            throw new InvalidOperationException(
                $"Rezervasyon temizligi basarisiz: {result.Error.Code} - {result.Error.Message}");
        }

        // PDF: "Job sonuclari loglanmalidir."
        if (result.Value > 0)
        {
            LogReservationsExpired(_logger, result.Value);
        }
    }

    // ==================================================================
    // 2 ve 3) OUTBOX MESAJLARINI ISLEME + BASARISIZLARI YENIDEN DENEME
    // ==================================================================

    /// <summary>
    /// Bekleyen Outbox mesajlarini isler.
    /// </summary>
    /// <remarks>
    /// ==============================================================
    /// "BASARISIZ MESAJLARI YENIDEN DENEME" NEDEN AYRI BIR IS DEGIL?
    /// ==============================================================
    /// PDF bu ikisini ayri maddeler olarak sayiyor. Ayri iki is
    /// yazmayi dusundum ve VAZGECTIM.
    ///
    /// Sebep: yeniden denenecek mesaj, bekleyen bir mesajdan yalnizca
    /// RetryCount > 0 olmasiyla ayriliyor. Processor'in sorgusu
    /// zaten "islenmemis VE (NextRetryAt bos VEYA zamani gelmis)"
    /// diyor -- yani yeni mesajlar ile yeniden denenecekleri AYNI
    /// sorgu topluyor.
    ///
    /// Ayri bir is yazsaydik ayni tabloyu ayni kosulla tarayan iki
    /// is olurdu ve ikisi ayni anda calisip ayni mesaji islemeye
    /// calisirdi.
    ///
    /// PDF'in istedigi DAVRANIS (basarisiz mesaj yeniden denenmeli)
    /// tam olarak karsilaniyor; ayri bir zamanlayici gerekmiyor.
    /// Ustel geri cekilme OutboxMessage.MarkAsFailed icinde.
    /// ==============================================================
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
                $"Outbox islemesi basarisiz: {result.Error.Code} - {result.Error.Message}");
        }

        var summary = result.Value;

        // Sifir mesaj islendiginde LOG YAZMIYORUZ.
        //
        // Bu is 30 saniyede bir calisiyor: gunde 2880 kez. Her
        // calismada "0 mesaj islendi" yazsaydik loglar gunde 2880
        // anlamsiz satirla dolar ve gercek hatalar arasinda
        // kaybolurdu.
        if (summary.Processed > 0 || summary.Failed > 0 || summary.DeadLettered > 0)
        {
            LogOutboxSummary(_logger, summary.Processed, summary.Failed, summary.DeadLettered);
        }
    }

    // ==================================================================
    // 4) YAKLASAN ETKINLIK HATIRLATMASI
    // ==================================================================

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
                $"Etkinlik hatirlatmasi basarisiz: {result.Error.Code} - {result.Error.Message}");
        }

        LogRemindersQueued(_logger, result.Value);
    }

    // ==================================================================
    // 5c) SURESI DOLMAK UZERE OLAN REZERVASYONLARI UYAR
    // ==================================================================
    // PDF Sprint 14: "Rezervasyon suresi dolmak uzereyken" bildirim.
    //
    // DAKIKADA BIR calisiyor -- uyarinin zamaninda gitmesi icin sart.
    // Bes dakikada bir calissaydi, 3 dakikalik uyari penceresini
    // tamamen KACIRABILIRDI.
    // ==================================================================

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
                $"Sure uyarisi basarisiz: {result.Error.Code} - {result.Error.Message}");
        }

        if (result.Value > 0)
        {
            LogExpiringWarned(_logger, result.Value);
        }
    }

    // ==================================================================
    // 5b) GECMIS ETKINLIKLERI TAMAMLA -- Sprint 12 icin eklendi
    // ==================================================================
    // PDF Sprint 9 bu isi SAYMIYOR. Sprint 12'yi yazarken ortaya cikti:
    // "Etkinlik tamamlanmadan yorum yapilamaz" kurali, etkinlikleri
    // Completed durumuna gecirecek bir mekanizma OLMADAN hicbir zaman
    // saglanamazdi.
    //
    // Yani PDF'in bir sprintteki kurali, baska bir sprintte olmayan bir
    // isi zorunlu kiliyor. Sprintleri tek tek okuyup "bu gercekten
    // calisir mi?" diye sormanin karsiligi.
    // ==================================================================

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
                $"Etkinlik tamamlama basarisiz: {result.Error.Code} - {result.Error.Message}");
        }

        if (result.Value > 0)
        {
            LogEventsCompleted(_logger, result.Value);
        }
    }

    // ==================================================================
    // 5) GUNLUK SATIS OZETI
    // ==================================================================

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
                $"Gunluk satis ozeti basarisiz: {result.Error.Code} - {result.Error.Message}");
        }

        var summary = result.Value;

        // DateOnly'yi OLDUGU GIBI geciyorum, ToString ile metne
        // cevirmeden.
        //
        // CA1873 bunu yakaladi: parametreyi burada bicimlendirseydik,
        // log seviyesi kapali olsa BILE her calismada bir metin
        // ureterek bosa tahsis yapardik. Kaynak ureteci, metni
        // yalnizca log gercekten yazilacaksa olusturuyor.
        LogDailySummary(
            _logger,
            summary.Date,
            summary.TicketCount,
            summary.GrossAmount,
            summary.Currency);
    }

    // ==================================================================
    // LOGLAMA -- kaynak ureteci ile (CA1848)
    // ==================================================================

    [LoggerMessage(
        EventId = 9101,
        Level = LogLevel.Information,
        Message = "{Count} rezervasyonun suresi doldu, koltuklari serbest birakildi.")]
    private static partial void LogReservationsExpired(ILogger logger, int count);

    [LoggerMessage(
        EventId = 9102,
        Level = LogLevel.Information,
        Message = "Outbox islendi. Basarili: {Processed}, Basarisiz: {Failed}, Dead letter: {DeadLettered}")]
    private static partial void LogOutboxSummary(
        ILogger logger, int processed, int failed, int deadLettered);

    [LoggerMessage(
        EventId = 9103,
        Level = LogLevel.Information,
        Message = "{Count} etkinlik hatirlatmasi kuyruga alindi.")]
    private static partial void LogRemindersQueued(ILogger logger, int count);

    [LoggerMessage(
        EventId = 9106,
        Level = LogLevel.Information,
        Message = "{Count} rezervasyon icin sure uyarisi gonderildi.")]
    private static partial void LogExpiringWarned(ILogger logger, int count);

    [LoggerMessage(
        EventId = 9105,
        Level = LogLevel.Information,
        Message = "{Count} etkinlik tamamlandi olarak isaretlendi.")]
    private static partial void LogEventsCompleted(ILogger logger, int count);

    [LoggerMessage(
        EventId = 9104,
        Level = LogLevel.Information,
        Message = "{Date} satis ozeti hazir. Bilet: {TicketCount}, Brut: {GrossAmount} {Currency}")]
    private static partial void LogDailySummary(
        ILogger logger, DateOnly date, int ticketCount, decimal grossAmount, string currency);
}
