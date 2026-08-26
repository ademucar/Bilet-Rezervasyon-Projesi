using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ticketing.Application.Abstractions.Security;
using Ticketing.Application.Abstractions.Time;
using Ticketing.Infrastructure.Security;
using Ticketing.Infrastructure.Time;

namespace Ticketing.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // ------------------------------------------------------------------
        // JWT yapilandirmasi
        // ------------------------------------------------------------------
        services.AddOptions<JwtOptions>()
                .Bind(configuration.GetSection(JwtOptions.SectionName))

                // JwtOptions uzerindeki [Required] ve [MinLength] gibi
                // nitelikleri dogrular.
                .ValidateDataAnnotations()

                // ==============================================================
                // ValidateOnStart -- KRITIK
                // ==============================================================
                // Bu satir olmasaydi, dogrulama ancak JwtOptions ILK KEZ
                // ISTENDIGINDE calisirdi -- yani ilk login denemesinde.
                //
                // Yani: eksik JWT_SECRET ile uygulama sorunsuz ayaga kalkar,
                // saglik kontrolunden gecer, yuk dengeleyici trafik gondermeye
                // baslar ve ILK KULLANICI giris yapmaya calistiginda 500 alir.
                //
                // ValidateOnStart ile uygulama HIC baslamaz. Deploy basarisiz
                // olur, eski surum ayakta kalir, kimse etkilenmez.
                // Buna "fail fast" denir ve dagitim guvenliginin temelidir.
                .ValidateOnStart();

        // ------------------------------------------------------------------
        // Servisler
        // ------------------------------------------------------------------

        // Singleton: durum tutmuyor, sadece DateTimeOffset.UtcNow donuyor.
        // Her istekte yeni nesne uretmenin anlami yok.
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();

        // Singleton: BCrypt cagrilari saf fonksiyon, durum yok.
        services.AddSingleton<IPasswordHasher, PasswordHasher>();

        // Singleton: imzalama anahtarini yapicida bir kez olusturuyor.
        // Scoped olsaydi her istekte kriptografi nesnesi kurulurdu.
        services.AddSingleton<ITokenService, TokenService>();

        return services;
    }
}
