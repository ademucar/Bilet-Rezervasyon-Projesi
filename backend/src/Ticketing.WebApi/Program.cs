using Ticketing.Persistence;
using Ticketing.WebApi.Middleware;

var builder = WebApplication.CreateBuilder(args);

// ===================================================================
// SERVISLER
// ===================================================================

builder.Services.AddControllers();
builder.Services.AddOpenApi();

// Persistence katmaninin TUM kayitlari tek satirda.
// Program.cs, DbContext'in veya repository'lerin varligini bilmiyor.
builder.Services.AddPersistence(builder.Configuration);

// ---- Problem Details ----
//
// PDF Sprint 2: "Problem Details standardi kullanilmalidir."
//
// Bu kayit yalnizca bizim firlattigimiz exception'lari degil,
// framework'un urettigi hatalari da (404 Not Found, 405 Method Not
// Allowed, model binding hatalari) RFC 7807 formatina cevirir.
//
// Boylece API'nin TUM hata yanitlari ayni sekle sahip olur ve
// frontend tek bir hata isleyici yazabilir.
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        // Hangi endpoint'in hata verdigi. Hata ayiklamada cok ise yarar.
        context.ProblemDetails.Instance =
            $"{context.HttpContext.Request.Method} {context.HttpContext.Request.Path}";

        context.ProblemDetails.Extensions["traceId"] =
            System.Diagnostics.Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
    };
});

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// ---- Health check ----
//
// PDF Sprint 16: "/health, /health/ready, /health/live"
// Su an temel kayit; veritabani ve Redis kontrolleri Sprint 16'da eklenecek.
builder.Services.AddHealthChecks();

var app = builder.Build();

// ===================================================================
// HTTP PIPELINE
// ===================================================================
//
// SIRA ONEMLI. Middleware'ler yazildiklari sirayla calisir ve
// yanlis sira sessiz hatalara yol acar.

// 1) Exception handler EN BASTA olmali.
//    Kendisinden SONRA gelen her seyi sarmalar. Asagida olsaydi,
//    ustundeki middleware'lerin hatalarini yakalayamazdi.
app.UseExceptionHandler();

// 2) Correlation ID, exception handler'dan HEMEN SONRA.
//    Boylece hata yanitina da correlation ID eklenebiliyor.
app.UseMiddleware<CorrelationIdMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
else
{
    // Docker icinde HTTPS sertifikasi yok; yonlendirme yalnizca
    // gercek sertifika varken anlamli. Sprint 15'te reverse proxy
    // arkasinda dogru yapilandiracagiz.
    app.UseHttpsRedirection();
}

app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

await app.RunAsync();

/// <summary>
/// Integration testlerin WebApplicationFactory ile bu projeyi
/// baslatabilmesi icin gereken acik giris noktasi.
///
/// Top-level statement kullanan bir Program.cs varsayilan olarak
/// internal bir sinif uretir; test projesi ona erisemez.
/// Bu partial bildirim onu public yapiyor. (PDF Sprint 17)
/// </summary>
public partial class Program;
