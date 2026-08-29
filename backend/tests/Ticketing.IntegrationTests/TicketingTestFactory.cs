using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Respawn;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Ticketing.Persistence;

namespace Ticketing.IntegrationTests;

/// <summary>
/// Entegrasyon testleri icin uygulamayi GERCEK PostgreSQL ve Redis
/// kapsayicilariyla ayaga kaldirir. PDF Sprint 17: "Testcontainers".
/// </summary>
/// <remarks>
/// NEDEN GERCEK VERITABANI? -- InMemory NEDEN YETMEZ?
///
/// EF Core'un InMemory saglayicisi bir veritabani DEGIL, bir sozluk.
/// Su ozelliklerin HICBIRI orada yok:
///
///   - xmin tabanli iyimser eszamanlilik  &lt;- projemizin KALBI
///   - Gercek transaction ve izolasyon seviyeleri
///   - UNIQUE / FOREIGN KEY kisitlari
///   - LOWER(), ILIKE gibi PostgreSQL fonksiyonlari
///   - Sorgu CEVIRISI (LINQ -> SQL)
///
/// Sonuncusu ozellikle sinsi: Sprint 13'te GroupBy + record
/// constructor kombinasyonunun EF tarafindan CEVRILEMEDIGINI ancak
/// calisma zamaninda 500 hatasi alarak ogrendim. InMemory saglayici
/// LINQ'u BELLEKTE calistirdigi icin o hatalarin HICBIRINI
/// yakalamaz -- test yesil doner, uretim patlar.
///
/// "Ayni koltugu iki kullanici alamaz" testini InMemory ile yazsaydik
/// yesil olurdu ve HICBIR SEY kanitlamazdi. PDF de zaten gercek
/// kapsayici istiyor.
///
/// MALIYETI: yaklasik 10-20 saniyelik bir baslangic
///
/// Kapsayicilar TEK KEZ baslatiliyor (ICollectionFixture) ve tum
/// testler paylasiyor. Her test icin ayri kapsayici baslatsaydik
/// paket dakikalarca surerdi.
/// </remarks>
public sealed class TicketingTestFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // POSTGRESQL KAPSAYICISI
    //
    // Surumu ACIKCA sabitliyorum ("17-alpine"), "latest" DEGIL.
    //
    // "latest" kullansaydik: bugun gecen bir test, PostgreSQL 18
    // ciktigi gun hicbir kod degismeden kirilabilirdi. Testin ne
    // zaman ve neden kirildigini anlamak imkansiz olurdu.
    //
    // 17, uretimde kullandigim surumle ayni (docker-compose.yml).
    // Farkli olsaydi testler "baska bir veritabaninda" gecerdi.
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("ticketing_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder("redis:7-alpine").Build();

    private Respawner? _respawner;
    private NpgsqlConnection? _respawnConnection;

    /// <summary>Testlerin dogrudan veritabanina bakmasi icin.</summary>
    /// <remarks>
    /// Bazi dogrulamalar HTTP uzerinden yapilamiyor: "koltuk gercekten
    /// Available'a dondu mu?" sorusunun cevabi bir uctan gorunmuyor.
    /// O durumlarda testler veritabanina dogrudan bakiyor.
    /// </remarks>
    public TicketingDbContext CreateDbContext()
    {
        var scope = Services.CreateScope();

        return scope.ServiceProvider.GetRequiredService<TicketingDbContext>();
    }

    // IMZA NOTU: xunit 2.x IAsyncLifetime, Task doner (ValueTask degil)
    //
    // WebApplicationFactory ise IAsyncDisposable'dan ValueTask donen
    // bir DisposeAsync tasiyor. Ikisi ayni sinifta cakisiyor.
    //
    // Cozum: IAsyncLifetime.DisposeAsync'i ACIK ARAYUZ UYGULAMASI
    // olarak yaziyorum (asagida) ve taban sinifin surumune
    // yonlendiriyorum. Boylece iki sozlesme de bozulmadan
    // karsilaniyor.
    public async Task InitializeAsync()
    {
        // Ikisini PARALEL baslatiyorum: sirayla baslatmanin bir
        // sebebi yok ve toplam sureyi neredeyse yariya indiriyor.
        await Task.WhenAll(
            _postgres.StartAsync(),
            _redis.StartAsync()).ConfigureAwait(false);

        // SEMAYI MIGRATION ILE KUR -- EnsureCreated DEGIL
        //
        // EnsureCreated() semayi model'den uretiyor ve
        // MIGRATION'LARI HIC CALISTIRMIYOR.
        //
        // Sonuc: bozuk bir migration'i testler ASLA yakalamaz.
        // Uretime cikarken "migration calismiyor" hatasini ilk kez
        // orada gorurduk.
        //
        // Migrate() ile testler, uretimde calisacak olan AYNI yoldan
        // gecen bir sema uzerinde calisiyor.
        using (var scope = Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketingDbContext>();

            await db.Database.MigrateAsync().ConfigureAwait(false);
        }

        // RESPAWN: testler arasi temizlik
        //
        // Her testten once tablolari bosaltiyor.
        //
        // Olmasaydi testler birbirinin verisini gorurdu ve
        // SIRALARINA gore gecip kalirlardi. Bu, hata ayiklamasi en
        // zor test turudur: tek basina calistirinca geciyor, paket
        // halinde kiriliyor.
        //
        // "__EFMigrationsHistory" HARIC: onu silseydim migration
        // gecmisi kaybolur ve EF semayi yeniden kurmaya calisirdi.
        _respawnConnection = new NpgsqlConnection(_postgres.GetConnectionString());
        await _respawnConnection.OpenAsync().ConfigureAwait(false);

        _respawner = await Respawner.CreateAsync(
            _respawnConnection,
            new RespawnerOptions
            {
                DbAdapter = DbAdapter.Postgres,
                SchemasToInclude = ["public"],
                TablesToIgnore = [new Respawn.Graph.Table("public", "__EFMigrationsHistory")],
            }).ConfigureAwait(false);
    }

    /// <summary>Veritabanini bosaltir. Her testin basinda cagriliyor.</summary>
    public async Task ResetDatabaseAsync()
    {
        if (_respawner is not null && _respawnConnection is not null)
        {
            await _respawner.ResetAsync(_respawnConnection).ConfigureAwait(false);
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // BAGLANTI DIZELERINI KAPSAYICILARA YONLENDIR
        //
        // Uygulama baglanti dizelerini yapilandirmadan okuyor
        // (Sprint 1 karari: hassas degerler kodda sabit degil).
        //
        // Bu, tam olarak burada ise yariyor: tek satir kod
        // degistirmeden testleri baska bir veritabanina
        // yonlendirebiliyoruz.
        builder.UseSetting("ConnectionStrings:Postgres", _postgres.GetConnectionString());
        builder.UseSetting("ConnectionStrings:Redis", _redis.GetConnectionString());

        // ZORUNLU AYARLAR -- UYGULAMA BUNLAR OLMADAN ACILMIYOR
        //
        // Ilk calistirmada 8 testin 8'i de ayni hatayla dustu:
        //
        //   DataAnnotation validation failed for 'JwtOptions'
        //   members: 'Secret' with the error: 'The Secret field is required.'
        //
        // Bu bir test hatasi degil, Sprint 1'de kurdugum
        // ValidateOnStart korumasinin CALISTIGININ kaniti: uygulama
        // eksik yapilandirmayla ayaga kalkmayi reddediyor.
        //
        // Alternatif tasarimda (ayarlar opsiyonel olsaydi) uygulama
        // sessizce acilir ve JWT bos bir anahtarla imzalanirdi --
        // yani herkesin uretebilecegi token'larla. Testin burada
        // patlamasi, o felaketin onlenmis olmasi demek.
        //
        // Test degerleri GERCEK degerlerden farkli ve zararsiz.
        builder.UseSetting("Jwt:Secret", "test-icin-en-az-32-karakterlik-gizli-anahtar-degeri");
        builder.UseSetting("Jwt:Issuer", "Ticketing.Tests");
        builder.UseSetting("Jwt:Audience", "Ticketing.Tests.Client");

        builder.UseSetting("AppUrls:Frontend", "http://localhost:5173");
        builder.UseSetting("AppUrls:Api", "http://localhost:5000");

        // Testte gercek e-posta gonderilmemeli. Var olmayan bir
        // sunucu vererek gonderimi basarisiz kiliyorum -- Outbox
        // zaten hatayi yakalayip yeniden deniyor, is akisi
        // etkilenmiyor.
        builder.UseSetting("Smtp:Host", "localhost");
        builder.UseSetting("Smtp:Port", "1");
        builder.UseSetting("Smtp:From", "test@ornek.local");

        // HIZ SINIRLAMASI TESTLERDE KAPALI
        //
        // Sprint 15'te auth ucuna 5 dakikada 10 istek siniri koydum.
        // Testler ayni IP'den (bellek ici sunucu) onlarca giris
        // yapiyor ve 11. testten sonra hepsi 429 alirdi.
        //
        // Yani hiz siniri, KENDI testlerimi engellerdi. Ayri bir
        // testte (RateLimitTests) acikca dogruluyorum; digerlerinde
        // kapatiyoruz.
        builder.UseSetting("RateLimiting:Enabled", "false");

        builder.UseEnvironment("Testing");
    }

    /// <summary>xunit'in cagirdigi surum: taban sinifa yonlendiriyor.</summary>
    Task IAsyncLifetime.DisposeAsync() => DisposeAsync().AsTask();

    public override async ValueTask DisposeAsync()
    {
        if (_respawnConnection is not null)
        {
            await _respawnConnection.DisposeAsync().ConfigureAwait(false);
        }

        await base.DisposeAsync().ConfigureAwait(false);

        await Task.WhenAll(
            _postgres.DisposeAsync().AsTask(),
            _redis.DisposeAsync().AsTask()).ConfigureAwait(false);

        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Tum entegrasyon testlerinin PAYLASTIGI kapsayici kumesi.
/// </summary>
/// <remarks>
/// NEDEN COLLECTION FIXTURE?
///
/// Kapsayici baslatmak 10-20 saniye suruyor. Her test sinifi kendi
/// kapsayicisini baslatsaydi paket dakikalarca surerdi ve kimse
/// testleri calistirmak istemezdi -- calistirilmayan test, olmayan
/// testtir.
///
/// Collection fixture, TUM test siniflari icin tek bir kurulum
/// yapiyor. Bunun bedeli, o siniflarin PARALEL calisamamasi:
/// xUnit ayni koleksiyondaki siniflari sirayla calistiriyor.
///
/// Bu bedeli bilincli odedim. Paralel calissalardi ayni
/// veritabanini paylasip birbirlerinin verisini silerlerdi
/// (Respawn her testin basinda TUM tablolari bosaltiyor).
/// </remarks>
[CollectionDefinition(Name)]
// CA1711: tur adi "Collection" ile bitmemeli -- o son ek .NET'te
// koleksiyon turlerine ayrilmis. Bu bir xunit isaretci sinifi,
// koleksiyon degil; adi ona gore secildi.
public sealed class TicketingTestSuite : ICollectionFixture<TicketingTestFactory>
{
    public const string Name = "ticketing-integration";
}
