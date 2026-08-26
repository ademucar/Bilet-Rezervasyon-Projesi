using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ticketing.Application.Abstractions.Persistence;

namespace Ticketing.Persistence;

/// <summary>
/// Persistence katmaninin DI kayitlari.
///
/// ------------------------------------------------------------------
/// NEDEN HER KATMAN KENDI KAYITLARINI YAPIYOR?
/// ------------------------------------------------------------------
/// Alternatif, Program.cs'te her seyi tek tek kaydetmekti:
///     builder.Services.AddDbContext&lt;TicketingDbContext&gt;(...);
///     builder.Services.AddScoped&lt;IUserRepository, UserRepository&gt;();
///     ... 50 satir daha
///
/// Bunu yapmadim cunku:
///
/// 1) Program.cs, Persistence'in IC DETAYLARINI bilmek zorunda kalirdi:
///    hangi DbContext var, hangi repository'ler var. Yarin bir repository
///    eklersem Program.cs'i degistirmem gerekirdi -- WebApi katmani
///    Persistence'in degisikliginden etkilenirdi.
///
/// 2) Bu metot Persistence icinde oldugu icin internal siniflari da
///    kaydedebiliyor. Program.cs'ten internal bir sinifi kaydedemezdim,
///    hepsini public yapmak zorunda kalirdim.
///
/// Sonuc: Program.cs'te tek satir -> services.AddPersistence(configuration)
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("Postgres");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // Uygulamayi BASLARKEN patlatiyorum, ilk istekte degil.
            //
            // Neden? Yanlis yapilandirmayla ayaga kalkan bir servis,
            // saglik kontrollerinden gecer, yuk dengeleyiciye "hazirim"
            // der ve gercek kullanici trafigi almaya baslar. Sonra her
            // istek 500 doner.
            //
            // Baslangicta patlarsa deploy basarisiz olur ve eski surum
            // ayakta kalir. Buna "fail fast" denir.
            throw new InvalidOperationException(
                "'Postgres' connection string bulunamadi. " +
                "appsettings.json veya ConnectionStrings__Postgres environment " +
                "degiskenini kontrol edin.");
        }

        services.AddDbContext<TicketingDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsql =>
            {
                // Migration'lar bu assembly'de aransin.
                npgsql.MigrationsAssembly(typeof(TicketingDbContext).Assembly.FullName);

                // Gecici baglanti hatalarinda otomatik yeniden dene.
                //
                // Docker Compose'da API, PostgreSQL'den once ayaga kalkabilir.
                // Ayrica ag dalgalanmalari gercek hayatta olur. Bu ayar
                // olmasaydi her gecici hata kullaniciya 500 olarak yansirdi.
                npgsql.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(5),
                    errorCodesToAdd: null);
            });
        });

        // Application katmani somut TicketingDbContext'i degil bu arayuzu
        // goruyor. Boylece Application, Persistence'a bagimli olmuyor --
        // architecture testimiz bunu her derlemede dogruluyor.
        //
        // GetRequiredService ile AYNI ornegi cozumluyorum, yeni bir tane
        // olusturmuyorum. Aksi halde tek bir HTTP istegi icinde IKI ayri
        // DbContext olurdu: biri degisiklikleri takip eder, digeri
        // kaydeder ve kayitlar sessizce kaybolurdu. Bu, tespit edilmesi
        // cok zor bir hata sinifidir.
        services.AddScoped<IApplicationDbContext>(sp =>
            sp.GetRequiredService<TicketingDbContext>());

        return services;
    }
}
