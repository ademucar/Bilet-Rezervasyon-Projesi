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
            // Bu assembly'deki TÜM handler'lari tarayip kaydeder.
            // Her handler'i elle kaydetseydik 100. handler'da birini
            // unutmak kacinilmaz olurdu -- ve hata calisma zamaninda
            // "handler bulunamadı" olarak ortaya çıkardı.
            cfg.RegisterServicesFromAssembly(assembly);

            // ==============================================================
            // PIPELINE SIRASI ONEMLIDIR
            // ==============================================================
            // Behavior'lar KAYIT SIRASIYLA çalışır. Su an tek behavior var
            // ama Sprint 7'de TransactionBehavior eklendiginde sıra soyle
            // olmalı:
            //
            //   Validation -> Logging -> Transaction -> Handler
            //
            // Validation EN BASTA çünkü geçersiz bir istek için transaction
            // acmanin veya log yazmanin anlami yok. Transaction en ICTE
            // çünkü yalnızca handler'in veritabani islemlerini sarmalamali;
            // doğrulama süresi boyunca açık kalmis bir transaction
            // bağlantı havuzunu (connection pool) gereksiz mesgul eder.
            // ==============================================================
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });

        // Bu assembly'deki tüm AbstractValidator siniflarini kaydeder.
        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        // ==============================================================
        // OUTBOX ISLEYICILERI -- PDF Sprint 9
        // ==============================================================
        // Bu kaydı ONCE Infrastructure'a yazmistim; derlenmedi, çünkü
        // isleyiciler `internal`. Hatayi gorunce iki secenegim vardi:
        //
        //   A) Isleyicileri public yapmak
        //   B) Kaydi bu katmana tasimak            <-- SECILEN
        //
        // (A) yanlış olurdu: bu siniflar disaridan cagrilmak için
        // değil, IOutboxMessageHandler arayuzu üzerinden calismak
        // için var. public yapmak, başka bir katmanin onlari
        // doğrudan cagirabilmesi demekti.
        //
        // Derleyici burada mimariyi KORUDU: "Application'in ic
        // detayını disaridan kullanamazsin" dedi ve hakliydi.
        // Kaydi ait olduğu yere tasimak doğru çözüm.
        //
        // Scoped: hepsi IApplicationDbContext kullaniyor ve o scoped.
        // Singleton yapsaydik "captive dependency" olusurdu --
        // uygulama omru boyunca yasayan tek bir DbContext.
        //
        // Assembly taramasi yerine ACIKCA yazıyorum: bir isleyici
        // eklendiginde bu listeye de eklenmesi gerektigi belli olsun.
        // Unutulursa processor "kayıtlı isleyici yok" hatası verip
        // dead letter'a dusurur; sessizce kaybolmaz.
        // ==============================================================
        services.AddScoped<IOutboxMessageHandler, TicketsIssuedOutboxHandler>();
        services.AddScoped<IOutboxMessageHandler, PaymentSucceededOutboxHandler>();
        services.AddScoped<IOutboxMessageHandler, ReservationExpiredOutboxHandler>();
        services.AddScoped<IOutboxMessageHandler, EventCancelledOutboxHandler>();
        services.AddScoped<IOutboxMessageHandler, EventReminderOutboxHandler>();
        services.AddScoped<IOutboxMessageHandler, DailySalesSummaryOutboxHandler>();

        // Sprint 13: rapor disa aktarimi.
        // PDF: "Rapor üretimi background job olarak calistirilmali."
        services.AddScoped<IOutboxMessageHandler, Features.Reports.ReportExportOutboxHandler>();

        // Sprint 14: rezervasyon oluşturuldu e-postası.
        services.AddScoped<IOutboxMessageHandler, ReservationCreatedOutboxHandler>();

        return services;
    }
}
