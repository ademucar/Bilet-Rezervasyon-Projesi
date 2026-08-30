using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ticketing.Domain.Entities;

namespace Ticketing.Persistence.Seeding;

/// <summary>
/// Başlangıç verisi. YALNIZCA gelistirme ortaminda çalışır.
///
/// Neden migration'IN HasData'si değil?
///
/// Roller için HasData kullandim (RoleConfiguration'da). Şehirler ve
/// kategoriler için KULLANMIYORUM. Fark su:
///
///   Roller     -> sistemin calismasi için sart. Kod bu ID'lere
///                 doğrudan referans veriyor (Role.Ids.Admin).
///                 Her ortamda AYNI olmalı. -> HasData doğru yer.
///
///   Şehirler   -> Sadece VERIDIR. Admin arayuzunden eklenip
///                 silinebilir. HasData ile koysaydım, admin bir
///                 şehri sildiginde bir sonraki migration önü geri
///                 getirmeye calisirdi.
///
/// Ayrıca HasData'daki her degisiklik yeni bir migration gerektirir.
/// 81 sehirlik listeyi migration'a gommek, sema degisikligi ile veri
/// degisikligini birbirine karistirmak olurdu.
/// </summary>
public sealed class DatabaseSeeder
{
    private readonly TicketingDbContext _context;
    private readonly ILogger<DatabaseSeeder> _logger;

    public DatabaseSeeder(TicketingDbContext context, ILogger<DatabaseSeeder> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await SeedCitiesAsync(cancellationToken).ConfigureAwait(false);
        await SeedCategoriesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task SeedCitiesAsync(CancellationToken cancellationToken)
    {
        // IDEMPOTENT: zaten veri varsa hiçbir sey yapma.
        //
        // Bu kontrol olmasaydı uygulama her baslatildiginda şehirler
        // tekrar eklenmeye calisilir ve unique index ihlali alırdım.
        // Seeder'lar her zaman idempotent olmalı -- uygulamanin kac
        // kez baslatildigi belli olmaz.
        if (await _context.Cities.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        // Nufusu en yüksek 20 şehir. Tam 81 liste gereksiz; gelistirme
        // ve demo için bu yeterli, admin arayuzunden eklenebilir.
        var cities = new (string Name, int Plate)[]
        {
            ("Adana", 1), ("Ankara", 6), ("Antalya", 7), ("Aydın", 9),
            ("Balıkesir", 10), ("Bursa", 16), ("Denizli", 20), ("Diyarbakır", 21),
            ("Eskişehir", 26), ("Gaziantep", 27), ("Hatay", 31), ("İstanbul", 34),
            ("İzmir", 35), ("Kayseri", 38), ("Kocaeli", 41), ("Konya", 42),
            ("Manisa", 45), ("Mersin", 33), ("Samsun", 55), ("Trabzon", 61),
        };

        foreach (var (name, plate) in cities)
        {
            _context.Cities.Add(City.Create(name, plate));
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        LogSeeded(_logger, "sehir", cities.Length);
    }

    private async Task SeedCategoriesAsync(CancellationToken cancellationToken)
    {
        if (await _context.EventCategories.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var categories = new (string Name, string Slug, string Icon, int Order)[]
        {
            ("Konser", "konser", "music-note", 1),
            ("Tiyatro", "tiyatro", "masks", 2),
            ("Stand-up", "stand-up", "microphone", 3),
            ("Konferans", "konferans", "presentation", 4),
            ("Festival", "festival", "sparkles", 5),
            ("Spor", "spor", "trophy", 6),
            ("Çocuk", "cocuk", "balloon", 7),
            ("Sergi", "sergi", "photo", 8),
        };

        foreach (var (name, slug, icon, order) in categories)
        {
            _context.EventCategories.Add(EventCategory.Create(name, slug, icon, order));
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        LogSeeded(_logger, "kategori", categories.Length);
    }

    // LoggerMessage yerine basit bir yardimci kullanıyorum: bu kod
    // uygulama omru boyunca en fazla bir kez çalışıyor, performans
    // optimizasyonu gereksiz. CA1848'i bu yüzden burada uygulamiyorum --
    // aşağıdaki sarmalayici, analizciyi de memnun ediyor.
    private static readonly Action<ILogger, int, string, Exception?> SeedLog =
        LoggerMessage.Define<int, string>(
            LogLevel.Information,
            new EventId(1000, "DataSeeded"),
            "{Count} adet {EntityName} kaydı oluşturuldu.");

    private static void LogSeeded(ILogger logger, string entityName, int count)
        => SeedLog(logger, count, entityName, null);
}
