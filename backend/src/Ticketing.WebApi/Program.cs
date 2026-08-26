using Ticketing.Persistence;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------
// Servisler
// ---------------------------------------------------------------

builder.Services.AddControllers();
builder.Services.AddOpenApi();

// Persistence katmaninin TUM kayitlari tek satirda.
// Program.cs, DbContext'in veya repository'lerin varligini bilmiyor --
// bu bilgi Persistence katmaninin kendi sorumlulugunda.
builder.Services.AddPersistence(builder.Configuration);

var app = builder.Build();

// ---------------------------------------------------------------
// HTTP pipeline
// ---------------------------------------------------------------

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Docker icinde HTTPS sertifikasi yok; yonlendirme yalnizca
// gelistirme disinda ve gercek sertifika varken anlamli.
// Sprint 15'te reverse proxy arkasinda dogru yapilandiracagiz.
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAuthorization();

app.MapControllers();

await app.RunAsync();

/// <summary>
/// Integration testlerin WebApplicationFactory ile bu projeyi
/// baslatabilmesi icin gereken acik giris noktasi.
///
/// Top-level statement kullanan bir Program.cs varsayilan olarak
/// internal bir sinif uretir; test projesi ona erisemez.
/// Bu partial bildirim onu public yapiyor.
/// (PDF Sprint 17: integration test zorunlulugu.)
/// </summary>
public partial class Program;
