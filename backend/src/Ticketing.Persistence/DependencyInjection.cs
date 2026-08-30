using Microsoft.EntityFrameworkCore;
using Ticketing.Persistence.Interceptors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ticketing.Application.Abstractions.Persistence;

namespace Ticketing.Persistence;

/// <summary>
/// Persistence katmaninin DI kayitlari.
///
/// Neden her katman kendi kayitlarini yapiyor?
///
/// Alternatif, Program.cs'te her seyi tek tek kaydetmekti:
///     builder.Services.AddDbContext&lt;TicketingDbContext&gt;(...);
///     builder.Services.AddScoped&lt;IUserRepository, UserRepository&gt;();
///     ... 50 satır daha
///
/// Bunu yapmadim çünkü:
///
/// 1) Program.cs, Persistence'in ic detaylarini bilmek zorunda kalırdı:
///    hangi DbContext var, hangi repository'ler var. Yarin bir repository
///    eklersem Program.cs'i degistirmem gerekirdi -- WebApi katmani
///    Persistence'in degisikliginden etkilenirdi.
///
/// 2) Bu metot Persistence içinde olduğu için internal siniflari da
///    kaydedebiliyor. Program.cs'ten internal bir sinifi kaydedemezdim,
///    hepsini public yapmak zorunda kalirdim.
///
/// Sonuç: Program.cs'te tek satır -> services.AddPersistence(configuration)
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
            // Uygulamayi BASLARKEN patlatiyorum, ilk istekte değil.
            //
            // Neden? Yanlis yapilandirmayla ayaga kalkan bir servis,
            // saglik kontrollerinden gecer, yuk dengeleyiciye "hazirim"
            // der ve gerçek kullanıcı trafigi almaya başlar. Sonra her
            // istek 500 döner.
            //
            // Baslangicta patlarsa deploy başarısız olur ve eski surum
            // ayakta kalır. Buna "fail fast" denir.
            throw new InvalidOperationException(
                "'Postgres' connection string bulunamadı. " +
                "appsettings.json veya ConnectionStrings__Postgres environment " +
                "degiskenini kontrol edin.");
        }

        // Denetim alani interceptor'I -- Sprint 12'de eklendi
        //
        // CreatedAt / UpdatedAt / soft delete alanlarini otomatik
        // dolduruyor. Gerekcesi ve nasil bulundugu
        // AuditFieldsInterceptor içinde ayrintili yazili.
        //
        // Scoped: ICurrentUser scoped (HttpContext'e bağlı) ve
        // interceptor önü kullaniyor. Singleton yapsaydim "captive
        // dependency" olusur, tüm istekler ILK istegin kullanicisini
        // gorurdu -- denetim izi tamamen yanlış olurdu.
        services.AddScoped<AuditFieldsInterceptor>();

        // Aynı gerekce (Scoped, çünkü ICurrentUser'a bağlı).
        // PDF Sprint 16: correlation ID Outbox kaydinda olmalı.
        services.AddScoped<OutboxCorrelationInterceptor>();

        services.AddDbContext<TicketingDbContext>((sp, options) =>
        {
            options.AddInterceptors(
                sp.GetRequiredService<AuditFieldsInterceptor>(),
                sp.GetRequiredService<OutboxCorrelationInterceptor>());

            options.UseNpgsql(connectionString, npgsql =>
            {
                // Migration'lar bu assembly'de aransin.
                npgsql.MigrationsAssembly(typeof(TicketingDbContext).Assembly.FullName);

                // Gecici bağlantı hatalarinda otomatik yeniden dene.
                //
                // Docker Compose'da API, PostgreSQL'den önce ayaga kalkabilir.
                // Ayrıca ag dalgalanmalari gerçek hayatta olur. Bu ayar
                // olmasaydı her geçici hata kullanıcıya 500 olarak yansirdi.
                npgsql.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(5),
                    errorCodesToAdd: null);
            });
        });

        // Application katmani somut TicketingDbContext'i değil bu arayuzu
        // görüyor. Boylece Application, Persistence'a bagimli olmuyor --
        // architecture testim bunu her derlemede dogruluyor.
        //
        // GetRequiredService ile AYNI ornegi cozumluyorum, yeni bir tane
        // olusturmuyorum. Aksi halde tek bir HTTP isteği içinde IKI ayrı
        // DbContext olurdu: biri değişiklikleri takip eder, digeri
        // kaydeder ve kayitlar sessizce kaybolurdu. Bu, tespit edilmesi
        // çok zor bir hata sinifidir.
        services.AddScoped<IApplicationDbContext>(sp =>
            sp.GetRequiredService<TicketingDbContext>());

        services.AddScoped<Seeding.DatabaseSeeder>();

        return services;
    }
}
