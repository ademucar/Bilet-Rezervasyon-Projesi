using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ticketing.Application.Abstractions;
using Ticketing.Application.Abstractions.Email;
using Ticketing.Application.Abstractions.Payments;
using Ticketing.Application.Abstractions.Security;
using Ticketing.Application.Abstractions.Time;
using Ticketing.Infrastructure.Configuration;
using Ticketing.Infrastructure.Email;
using Ticketing.Application.Abstractions.Reporting;
using Ticketing.Application.Features.Reports;
using Ticketing.Infrastructure.Payments;
using Ticketing.Infrastructure.Reporting;
using Ticketing.Application.Abstractions.Storage;
using Ticketing.Infrastructure.Security;
using Ticketing.Infrastructure.Storage;
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

        // ---- E-posta ----
        services.AddOptions<EmailOptions>()
                .Bind(configuration.GetSection(EmailOptions.SectionName))
                .ValidateDataAnnotations()
                .ValidateOnStart();

        services.AddSingleton<IEmailService, SmtpEmailService>();

        // Sprint 14: e-posta sablonlari.
        // Singleton: durum tutmuyor, yalnizca IAppUrlProvider okuyor.
        services.AddSingleton<IEmailTemplateRenderer, EmailTemplateRenderer>();

        // ---- Uygulama adresleri ----
        services.AddOptions<AppUrlOptions>()
                .Bind(configuration.GetSection(AppUrlOptions.SectionName))
                .ValidateDataAnnotations()
                .ValidateOnStart();

        services.AddSingleton<IAppUrlProvider, AppUrlProvider>();

        // ---- Raporlama (PDF Sprint 13) ----
        //
        // Singleton: ikisi de durum tutmuyor.
        // ReportExporter yalnizca girdi -> cikti donusumu yapiyor;
        // FileSystemReportStore ise kok klasoru bir kez okuyor.
        services.AddSingleton<IReportExporter, ReportExporter>();
        services.AddSingleton<IReportFileStore, FileSystemReportStore>();

        // ---- Dosya depolama (PDF Sprint 15) ----
        //
        // Singleton: kok klasoru bir kez okuyup olusturuyor, baska
        // durum tutmuyor. Her istekte yeniden Directory.CreateDirectory
        // cagirmanin anlami yok.
        services.AddSingleton<IFileStorage, LocalFileStorage>();

        // ---- Odeme saglayicisi ----
        //
        // Hangi saglayicinin kullanilacagi YAPILANDIRMADAN seciliyor.
        // Boylece gelistirme ortaminda "Failed" secip basarisiz odeme
        // akisini deneyebiliyoruz -- kod degistirmeden.
        //
        // PDF Sprint 8: "En az iki implementasyon hazirlanabilir:
        // MockPaymentProvider, FailedPaymentProvider."
        var paymentProvider = configuration["Payment:Provider"] ?? "Mock";

        if (string.Equals(paymentProvider, "Failed", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IPaymentService, FailedPaymentProvider>();
        }
        else
        {
            services.AddSingleton<IPaymentService, MockPaymentProvider>();
        }

        return services;
    }
}
