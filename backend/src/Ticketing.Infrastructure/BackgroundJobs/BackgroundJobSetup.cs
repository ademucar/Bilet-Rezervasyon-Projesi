using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ticketing.Infrastructure.BackgroundJobs;

/// <summary>
/// Hangfire kurulumu ve zamanlanmis islerin kaydı. PDF Sprint 9.
/// </summary>
public static class BackgroundJobSetup
{
    /// <summary>Tekrarlanan islerin kimlikleri. Elle tetiklemede de kullanılıyor.</summary>
    public static class JobIds
    {
        public const string ExpireReservations = "expire-reservations";
        public const string ProcessOutbox = "process-outbox";
        public const string EventReminders = "event-reminders";
        public const string DailySalesSummary = "daily-sales-summary";
        public const string CompletePastEvents = "complete-past-events";
        public const string ExpiringReservations = "expiring-reservations";
    }

    /// <summary>
    /// ==============================================================
    /// NEDEN HANGFIRE, NEDEN QUARTZ.NET DEĞİL?
    /// ==============================================================
    /// PDF ikisini de kabul ediyor. Hangfire'i sectim çünkü:
    ///
    /// 1) IZLEME EKRANI HAZIR GELIYOR. /hangfire adresinde her isin
    ///    ne zaman calistigi, ne kadar surdugu, hangi hatayla
    ///    başarısız olduğu görünüyor. Quartz'da bunu kendimiz
    ///    yazmamiz gerekirdi. Arka plan islerinde en büyük risk
    ///    "calismadigini fark etmemek" olduğu için bu ekran bir
    ///    konfor değil, ihtiyac.
    ///
    /// 2) IS DURUMU VERITABANINDA. Uygulama yeniden baslatildiginda
    ///    yarim kalan isler kaybolmuyor.
    ///
    /// 3) ZATEN POSTGRESQL KULLANIYORUZ. Hangfire.PostgreSql ile ek
    ///    bir altyapi (Redis, SQL Server) gerekmiyor.
    ///
    /// Quartz daha hafif ve daha esnek zamanlama sunuyor; bizim
    /// ihtiyacimiz olan zamanlama basit olduğu için bu avantaji
    /// kullanamazdik.
    /// ==============================================================
    /// </summary>
    public static IServiceCollection AddBackgroundJobs(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Anahtar adı "Postgres" -- Persistence katmaninin kullandigi
        // ile AYNI olmalı. İlk yazimda "Default" yazmistim; uygulama
        // ayaga bile kalkmazdi.
        //
        // Aynı veritabanini kullanıyoruz: Hangfire kendi tablolarini
        // "hangfire" semasi altinda olusturuyor, bizim tablolarimizla
        // karismiyor. Ayrı bir veritabani kurmak, is durumu ile is
        // verisinin farklı yedekleme/geri yukleme noktalarina
        // dusmesine yol acardi.
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException(
                "Hangfire için 'Postgres' bağlantı dizesi bulunamadı.");

        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(options => options.UseNpgsqlConnection(connectionString)));

        // ==============================================================
        // ISCI SAYISI
        // ==============================================================
        // Varsayılan: CPU cekirdek sayısı x 5. 8 cekirdekli bir
        // makinede 40 esZamanli isci demek.
        //
        // Bizim isler VERITABANI AGIRLIKLI ve zaten
        // [DisableConcurrentExecution] ile teke dusuruluyor. 40 isci
        // yalnızca veritabani bağlantı havuzunu tuketirdi -- HTTP
        // isteklerine bağlantı kalmayabilirdi.
        //
        // 4 isci: dort isimiz var, her biri kendi kuyrugunda
        // rahatca ilerler.
        // ==============================================================
        services.AddHangfireServer(options =>
        {
            options.WorkerCount = 4;
            options.ServerName = $"ticketing-{Environment.MachineName}";
        });

        services.AddScoped<TicketingJobs>();

        // NOT: Outbox isleyicileri BURADA DEĞİL, Application katmaninin
        // kendi DependencyInjection dosyasinda kayıtlı. Sebebi orada
        // yazili: isleyiciler `internal` ve oyle kalmali.

        return services;
    }

    /// <summary>
    /// Tekrarlanan isleri kaydeder. Uygulama ayaga kalktiktan sonra cagrilir.
    /// </summary>
    /// <remarks>
    /// AddOrUpdate kullanıyorum, Add değil: uygulama her yeniden
    /// baslatildiginda cagriliyor. Add olsaydı ikinci baslatmada
    /// "bu is zaten var" hatası alırdık veya kopya isler olusurdu.
    ///
    /// Aynı JobId ile cagirmak, zamanlamayi guncelliyor. Yani cron
    /// ifadesini degistirip uygulamayi yeniden baslatmak yeterli.
    /// </remarks>
    public static void RegisterRecurringJobs(IRecurringJobManager recurringJobs)
    {
        ArgumentNullException.ThrowIfNull(recurringJobs);

        // ---- 1) Süresi dolan rezervasyonlar: DAKIKADA BIR ----
        //
        // Neden bu kadar sik? Çünkü bu is DOGRUDAN GELIR etkiliyor.
        // Süresi dolmuş bir rezervasyonun koltuğu, is calisana kadar
        // kimseye satilamaz. 10 dakikada bir calissaydi, popüler bir
        // konserde her koltuk ortalama 5 dakika boşuna bekletilirdi.
        //
        // Maliyeti düşük: sorgu index'li ve genelde boş döner.
        recurringJobs.AddOrUpdate<TicketingJobs>(
            JobIds.ExpireReservations,
            job => job.ExpireReservationsAsync(CancellationToken.None),
            Cron.Minutely());

        // ---- 2) Outbox: 30 SANIYEDE BIR ----
        //
        // Cron dakikadan kisa aralık desteklemiyor; Hangfire'in
        // "* * * * * *" (saniye alanli) bicimini kullanıyorum.
        //
        // Neden 30 saniye? Kullanıcı ödemeyi tamamladiktan sonra
        // "biletiniz hazır" e-postasini bekliyor. Dakikalarca
        // beklemek, ödemenin gecip gecmediginden suphe ettirir.
        // 30 saniye, gonderimin "anında" hissettirmesi için yeterli
        // ve veritabanini yormuyor.
        recurringJobs.AddOrUpdate<TicketingJobs>(
            JobIds.ProcessOutbox,
            job => job.ProcessOutboxAsync(CancellationToken.None),
            "*/30 * * * * *");

        // ---- 3) Etkinlik hatirlatmasi: HER GUN 10:00 (UTC) ----
        //
        // Sabit bir saat sectim çünkü hatirlatma bir BILDIRIMDIR;
        // gece 03:00'te telefon titretmek kullanıcıyı kizdirir.
        //
        // Turkiye saatiyle 13:00'e denk geliyor -- ogle arasi,
        // insanlarin telefonuna baktigi bir vakit.
        recurringJobs.AddOrUpdate<TicketingJobs>(
            JobIds.EventReminders,
            job => job.SendEventRemindersAsync(CancellationToken.None),
            Cron.Daily(hour: 10));

        // ---- 4) Günlük satış özeti: HER GUN 00:30 (UTC) ----
        //
        // Gece yarisindan YARIM SAAT SONRA, tam 00:00'da değil.
        //
        // Sebep: 23:59:59'da tamamlanan bir ödemenin veritabanina
        // yazilmasi ve transaction'in kapanmasi birkaç yuz
        // milisaniye sürebilir. Tam gece yarisi calisirsak o ödemeyi
        // kacirir ve rapor eksik çıkardı. Yarim saat, rahat bir pay.
        recurringJobs.AddOrUpdate<TicketingJobs>(
            JobIds.DailySalesSummary,
            job => job.GenerateDailySalesSummaryAsync(CancellationToken.None),
            "30 0 * * *");

        // ---- 5) Gecmis etkinlikleri tamamla: SAATTE BIR ----
        //
        // Sprint 12 için eklendi (bkz. TicketingJobs açıklaması).
        //
        // Neden saatte bir? Bu is kullanıcıyı BEKLETMIYOR ama
        // geciktikce yorum yapmayi geciktiriyor. Etkinlik bitiminden
        // sonraki 6 saatlik pay zaten var; ustune bir saatlik gecikme
        // fark etmez.
        //
        // Dakikada bir calistirmanin anlami yok: etkinlikler dakika
        // dakika bitmiyor. Gunde bir de çok seyrek olurdu -- sabah
        // biten bir etkinlik için aksama kadar yorum yapilamazdi.
        recurringJobs.AddOrUpdate<TicketingJobs>(
            JobIds.CompletePastEvents,
            job => job.CompletePastEventsAsync(CancellationToken.None),
            Cron.Hourly());

        // ---- 6) Süre uyarısı: DAKIKADA BIR ----
        //
        // PDF Sprint 14: "Rezervasyon süresi dolmak uzereyken" bildirim.
        //
        // Uyari penceresi 3 dakika. Bes dakikada bir calissaydi
        // pencereyi tamamen kacirabilirdi -- yani bildirim HİÇ
        // gitmezdi ve hata da vermezdi.
        //
        // Maliyeti düşük: sorgu index'li ve genelde boş döner.
        recurringJobs.AddOrUpdate<TicketingJobs>(
            JobIds.ExpiringReservations,
            job => job.NotifyExpiringReservationsAsync(CancellationToken.None),
            Cron.Minutely());
    }
}
