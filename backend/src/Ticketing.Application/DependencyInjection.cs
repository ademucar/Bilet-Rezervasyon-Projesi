using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Ticketing.Application.Behaviors;

namespace Ticketing.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var assembly = AssemblyReference.Assembly;

        services.AddMediatR(cfg =>
        {
            // Bu assembly'deki TUM handler'lari tarayip kaydeder.
            // Her handler'i elle kaydetseydik 100. handler'da birini
            // unutmak kacinilmaz olurdu -- ve hata calisma zamaninda
            // "handler bulunamadi" olarak ortaya cikardi.
            cfg.RegisterServicesFromAssembly(assembly);

            // ==============================================================
            // PIPELINE SIRASI ONEMLIDIR
            // ==============================================================
            // Behavior'lar KAYIT SIRASIYLA calisir. Su an tek behavior var
            // ama Sprint 7'de TransactionBehavior eklendiginde sira soyle
            // olmali:
            //
            //   Validation -> Logging -> Transaction -> Handler
            //
            // Validation EN BASTA cunku gecersiz bir istek icin transaction
            // acmanin veya log yazmanin anlami yok. Transaction en ICTE
            // cunku yalnizca handler'in veritabani islemlerini sarmalamali;
            // dogrulama suresi boyunca acik kalmis bir transaction
            // baglanti havuzunu (connection pool) gereksiz mesgul eder.
            // ==============================================================
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });

        // Bu assembly'deki tum AbstractValidator siniflarini kaydeder.
        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        return services;
    }
}
