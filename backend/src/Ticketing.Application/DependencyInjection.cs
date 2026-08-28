using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Ticketing.Application.Abstractions.Messaging;
using Ticketing.Application.Behaviors;
using Ticketing.Application.Features.Outbox;

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

        // ==============================================================
        // OUTBOX ISLEYICILERI -- PDF Sprint 9
        // ==============================================================
        // Bu kaydi ONCE Infrastructure'a yazmistim; derlenmedi, cunku
        // isleyiciler `internal`. Hatayi gorunce iki secenegim vardi:
        //
        //   A) Isleyicileri public yapmak
        //   B) Kaydi bu katmana tasimak            <-- SECILEN
        //
        // (A) yanlis olurdu: bu siniflar disaridan cagrilmak icin
        // degil, IOutboxMessageHandler arayuzu uzerinden calismak
        // icin var. public yapmak, baska bir katmanin onlari
        // dogrudan cagirabilmesi demekti.
        //
        // Derleyici burada mimariyi KORUDU: "Application'in ic
        // detayini disaridan kullanamazsin" dedi ve hakliydi.
        // Kaydi ait oldugu yere tasimak dogru cozum.
        //
        // Scoped: hepsi IApplicationDbContext kullaniyor ve o scoped.
        // Singleton yapsaydik "captive dependency" olusurdu --
        // uygulama omru boyunca yasayan tek bir DbContext.
        //
        // Assembly taramasi yerine ACIKCA yaziyorum: bir isleyici
        // eklendiginde bu listeye de eklenmesi gerektigi belli olsun.
        // Unutulursa processor "kayitli isleyici yok" hatasi verip
        // dead letter'a dusurur; sessizce kaybolmaz.
        // ==============================================================
        services.AddScoped<IOutboxMessageHandler, TicketsIssuedOutboxHandler>();
        services.AddScoped<IOutboxMessageHandler, PaymentSucceededOutboxHandler>();
        services.AddScoped<IOutboxMessageHandler, ReservationExpiredOutboxHandler>();
        services.AddScoped<IOutboxMessageHandler, EventCancelledOutboxHandler>();
        services.AddScoped<IOutboxMessageHandler, EventReminderOutboxHandler>();
        services.AddScoped<IOutboxMessageHandler, DailySalesSummaryOutboxHandler>();

        // Sprint 13: rapor disa aktarimi.
        // PDF: "Rapor uretimi background job olarak calistirilmali."
        services.AddScoped<IOutboxMessageHandler, Features.Reports.ReportExportOutboxHandler>();

        // Sprint 14: rezervasyon olusturuldu e-postasi.
        services.AddScoped<IOutboxMessageHandler, ReservationCreatedOutboxHandler>();

        return services;
    }
}
