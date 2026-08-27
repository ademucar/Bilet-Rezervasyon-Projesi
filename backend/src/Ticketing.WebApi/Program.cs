using Asp.Versioning;
using Ticketing.Application;
using Ticketing.Application.Abstractions.Security;
using Ticketing.Application.Common.Options;
using Ticketing.Infrastructure;
using Ticketing.Persistence;
using Ticketing.WebApi.Middleware;
using Ticketing.WebApi.Security;

var builder = WebApplication.CreateBuilder(args);

// ===================================================================
// SERVISLER
// ===================================================================

builder.Services.AddControllers();
builder.Services.AddOpenApi();

// ---- API Versioning ----
//
// PDF Sprint 18: "API versioning uygulanmalidir." ve
// "/api/v1/events" bicimi isteniyor.
//
// URL segmenti tabanli surumleme sectim (header veya query yerine):
//   - Tarayicidan ve Postman'den denemesi kolay
//   - Onbellek (cache) anahtarlari dogal olarak ayrisir
//   - Loglarda hangi surumun cagrildigi aciktir
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;

    // Yanit header'inda desteklenen surumleri bildir.
    // Istemciler yeni surum ciktigini bu sayede fark eder.
    options.ReportApiVersions = true;
    options.ApiVersionReader = new UrlSegmentApiVersionReader();
})
.AddApiExplorer(options =>
{
    options.GroupNameFormat = "'v'VVV";
    options.SubstituteApiVersionInUrl = true;
});

// ---- Katmanlar ----
//
// Her katman kendi kayitlarini yapiyor. Program.cs, o katmanlarin
// IC DETAYLARINI bilmiyor -- hangi handler var, hangi DbContext var
// gibi bilgiler burada gecmiyor.
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddPersistence(builder.Configuration);

// ---- Guvenlik ayarlari ----
builder.Services.AddOptions<SecurityOptions>()
       .Bind(builder.Configuration.GetSection(SecurityOptions.SectionName))
       .ValidateDataAnnotations()
       .ValidateOnStart();

builder.Services.AddOptions<ReservationOptions>()
       .Bind(builder.Configuration.GetSection(ReservationOptions.SectionName))
       .ValidateDataAnnotations()
       .ValidateOnStart();

// ICurrentUser HttpContext'e erisiyor; bu erisim icin gerekli.
builder.Services.AddHttpContextAccessor();

// Scoped: her HTTP istegi icin bir ornek. Singleton OLAMAZ cunku
// istege ozel veri (kullanici kimligi) tasiyor.
builder.Services.AddScoped<ICurrentUser, CurrentUser>();

builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddAuthorizationPolicies();

// ---- Problem Details ----
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Instance =
            $"{context.HttpContext.Request.Method} {context.HttpContext.Request.Path}";

        context.ProblemDetails.Extensions["traceId"] =
            System.Diagnostics.Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
    };
});

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddHealthChecks();

var app = builder.Build();

// ===================================================================
// BASLANGIC VERISI -- YALNIZCA GELISTIRMEDE
// ===================================================================
// Uretimde ASLA otomatik seed calistirmiyoruz. Sebep: seed kodu
// yanlislikla veri uzerine yazabilir veya beklenmedik kayitlar
// olusturabilir. Uretimde veri, kontrollu migration'lar veya admin
// arayuzu uzerinden girilir.
//
// CreateScope kullaniyorum cunku DatabaseSeeder ve DbContext SCOPED
// kayitli; uygulama koku (root) singleton bir kapsam ve oradan scoped
// servis cozumlemek InvalidOperationException verir.
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var seeder = scope.ServiceProvider.GetRequiredService<Ticketing.Persistence.Seeding.DatabaseSeeder>();

    await seeder.SeedAsync().ConfigureAwait(false);
}

// ===================================================================
// HTTP PIPELINE -- SIRA ONEMLI
// ===================================================================

// 1) En basta: kendisinden sonraki her seyi sarmalar.
app.UseExceptionHandler();

// 2) Hata yanitina da correlation ID eklenebilsin diye hemen sonra.
app.UseMiddleware<CorrelationIdMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
else
{
    app.UseHttpsRedirection();
}

// ==================================================================
// SIRA KRITIK: Authentication ONCE, Authorization SONRA
// ==================================================================
// UseAuthentication  -> "Sen kimsin?"   (token'i okur, User'i doldurur)
// UseAuthorization   -> "Yetkin var mi?" (User'a bakip karar verir)
//
// Ters yazsaydik Authorization henuz doldurulmamis bir User goreceginden
// giris yapmis kullanicilar bile 401 alirdi. Ve bu hata cok kafa
// karistiricidir: token dogru, kod dogru ama calismiyor.
// ==================================================================
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

await app.RunAsync();

/// <summary>
/// Integration testlerin WebApplicationFactory ile bu projeyi
/// baslatabilmesi icin gereken acik giris noktasi. (PDF Sprint 17)
/// </summary>
public partial class Program;
