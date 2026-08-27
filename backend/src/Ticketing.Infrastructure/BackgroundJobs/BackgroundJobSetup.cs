using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ticketing.Infrastructure.BackgroundJobs;

/// <summary>
/// Hangfire kurulumu ve zamanlanmis islerin kaydi. PDF Sprint 9.
/// </summary>
public static class BackgroundJobSetup
{
    /// <summary>Tekrarlanan islerin kimlikleri. Elle tetiklemede de kullaniliyor.</summary>
    public static class JobIds
    {
        public const string ExpireReservations = "expire-reservations";
        public const string ProcessOutbox = "process-outbox";
        public const string EventReminders = "event-reminders";
        public const string DailySalesSummary = "daily-sales-summary";
    }

    /// <summary>
    /// ==============================================================
    /// NEDEN HANGFIRE, NEDEN QUARTZ.NET DEGIL?
    /// ==============================================================
    /// PDF ikisini de kabul ediyor. Hangfire'i sectim cunku:
    ///
    /// 1) IZLEME EKRANI HAZIR GELIYOR. /hangfire adresinde her isin
    ///    ne zaman calistigi, ne kadar surdugu, hangi hatayla
    ///    basarisiz oldugu gorunuyor. Quartz'da bunu kendimiz
    ///    yazmamiz gerekirdi. Arka plan islerinde en buyuk risk
    ///    "calismadigini fark etmemek" oldugu icin bu ekran bir
    ///    konfor degil, ihtiyac.
    ///
    /// 2) IS DURUMU VERITABANINDA. Uygulama yeniden baslatildiginda
    ///    yarim kalan isler kaybolmuyor.
    ///
    /// 3) ZATEN POSTGRESQL KULLANIYORUZ. Hangfire.PostgreSql ile ek
    ///    bir altyapi (Redis, SQL Server) gerekmiyor.
    ///
    /// Quartz daha hafif ve daha esnek zamanlama sunuyor; bizim
    /// ihtiyacimiz olan zamanlama basit oldugu icin bu avantaji
    /// kullanamazdik.
    /// ==============================================================
    /// </summary>
    public static IServiceCollection AddBackgroundJobs(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Anahtar adi "Postgres" -- Persistence katmaninin kullandigi
        // ile AYNI olmali. Ilk yazimda "Default" yazmistim; uygulama
        // ayaga bile kalkmazdi.
        //
        // Ayni veritabanini kullaniyoruz: Hangfire kendi tablolarini
        // "hangfire" semasi altinda olusturuyor, bizim tablolarimizla
        // karismiyor. Ayri bir veritabani kurmak, is durumu ile is
        // verisinin farkli yedekleme/geri yukleme noktalarina
        // dusmesine yol acardi.
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException(
                "Hangfire icin 'Postgres' baglanti dizesi bulunamadi.");

        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(options => options.UseNpgsqlConnection(connectionString)));

        // ==============================================================
        // ISCI SAYISI
        // ==============================================================
        // Varsayilan: CPU cekirdek sayisi x 5. 8 cekirdekli bir
        // makinede 40 esZamanli isci demek.
        //
        // Bizim isler VERITABANI AGIRLIKLI ve zaten
        // [DisableConcurrentExecution] ile teke dusuruluyor. 40 isci
        // yalnizca veritabani baglanti havuzunu tuketirdi -- HTTP
        // isteklerine baglanti kalmayabilirdi.
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

        // NOT: Outbox isleyicileri BURADA DEGIL, Application katmaninin
        // kendi DependencyInjection dosyasinda kayitli. Sebebi orada
        // yazili: isleyiciler `internal` ve oyle kalmali.

        return services;
    }

    /// <summary>
    /// Tekrarlanan isleri kaydeder. Uygulama ayaga kalktiktan sonra cagrilir.
    /// </summary>
    /// <remarks>
    /// AddOrUpdate kullaniyorum, Add degil: uygulama her yeniden
    /// baslatildiginda cagriliyor. Add olsaydi ikinci baslatmada
    /// "bu is zaten var" hatasi alirdik veya kopya isler olusurdu.
    ///
    /// Ayni JobId ile cagirmak, zamanlamayi guncelliyor. Yani cron
    /// ifadesini degistirip uygulamayi yeniden baslatmak yeterli.
    /// </remarks>
    public static void RegisterRecurringJobs(IRecurringJobManager recurringJobs)
    {
        ArgumentNullException.ThrowIfNull(recurringJobs);

        // ---- 1) Suresi dolan rezervasyonlar: DAKIKADA BIR ----
        //
        // Neden bu kadar sik? Cunku bu is DOGRUDAN GELIR etkiliyor.
        // Suresi dolmus bir rezervasyonun koltugu, is calisana kadar
        // kimseye satilamaz. 10 dakikada bir calissaydi, populer bir
        // konserde her koltuk ortalama 5 dakika bosuna bekletilirdi.
        //
        // Maliyeti dusuk: sorgu index'li ve genelde bos doner.
        recurringJobs.AddOrUpdate<TicketingJobs>(
            JobIds.ExpireReservations,
            job => job.ExpireReservationsAsync(CancellationToken.None),
            Cron.Minutely());

        // ---- 2) Outbox: 30 SANIYEDE BIR ----
        //
        // Cron dakikadan kisa aralik desteklemiyor; Hangfire'in
        // "* * * * * *" (saniye alanli) bicimini kullaniyorum.
        //
        // Neden 30 saniye? Kullanici odemeyi tamamladiktan sonra
        // "biletiniz hazir" e-postasini bekliyor. Dakikalarca
        // beklemek, odemenin gecip gecmediginden suphe ettirir.
        // 30 saniye, gonderimin "aninda" hissettirmesi icin yeterli
        // ve veritabanini yormuyor.
        recurringJobs.AddOrUpdate<TicketingJobs>(
            JobIds.ProcessOutbox,
            job => job.ProcessOutboxAsync(CancellationToken.None),
            "*/30 * * * * *");

        // ---- 3) Etkinlik hatirlatmasi: HER GUN 10:00 (UTC) ----
        //
        // Sabit bir saat sectim cunku hatirlatma bir BILDIRIMDIR;
        // gece 03:00'te telefon titretmek kullaniciyi kizdirir.
        //
        // Turkiye saatiyle 13:00'e denk geliyor -- ogle arasi,
        // insanlarin telefonuna baktigi bir vakit.
        recurringJobs.AddOrUpdate<TicketingJobs>(
            JobIds.EventReminders,
            job => job.SendEventRemindersAsync(CancellationToken.None),
            Cron.Daily(hour: 10));

        // ---- 4) Gunluk satis ozeti: HER GUN 00:30 (UTC) ----
        //
        // Gece yarisindan YARIM SAAT SONRA, tam 00:00'da degil.
        //
        // Sebep: 23:59:59'da tamamlanan bir odemenin veritabanina
        // yazilmasi ve transaction'in kapanmasi birkac yuz
        // milisaniye surebilir. Tam gece yarisi calisirsak o odemeyi
        // kacirir ve rapor eksik cikardi. Yarim saat, rahat bir pay.
        recurringJobs.AddOrUpdate<TicketingJobs>(
            JobIds.DailySalesSummary,
            job => job.GenerateDailySalesSummaryAsync(CancellationToken.None),
            "30 0 * * *");
    }
}
