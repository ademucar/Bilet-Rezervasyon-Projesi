using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Ticketing.Persistence;

/// <summary>
/// "dotnet ef migrations add" ve "dotnet ef database update" komutlarinin
/// kullandigi tasarim zamani fabrikasi.
///
/// ------------------------------------------------------------------
/// BU SINIF NEDEN GEREKLI?
/// ------------------------------------------------------------------
/// EF Core araclari migration uretirken uygulamayi baslatmaya calisir:
/// Program.cs'i calistirir, DI konteynerini kurar, DbContext'i alir.
///
/// Bunun iki sorunu var:
///
/// 1) appsettings.Development.json dosyasi .gitignore'da (icinde
///    connection string var). Projeyi yeni klonlayan biri
///    "dotnet ef migrations add" calistirdiginda uygulama
///    "connection string bulunamadi" diye patlardi -- oysa migration
///    URETMEK icin gercek bir veritabanina hic gerek yok.
///
/// 2) CI/CD ortaminda veritabani yokken de migration dogrulamasi
///    yapabilmek istiyoruz.
///
/// Bu fabrika devreye girdiginde EF, Program.cs'i hic calistirmaz.
/// Buradaki sahte connection string yalnizca SQL URETMEK icin kullanilir;
/// hicbir baglanti acilmaz.
///
/// Gercek veritabanina uygulama yaparken (database update) ise
/// asagidaki environment degiskeni okunur.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<TicketingDbContext>
{
    public TicketingDbContext CreateDbContext(string[] args)
    {
        // Once gercek baglantiyi dene (database update icin gerekli),
        // yoksa yalnizca SQL uretmeye yeten sahte bir degere dus.
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? "Host=localhost;Port=5432;Database=ticketing;Username=ticketing;Password=ticketing_dev_password";

        var options = new DbContextOptionsBuilder<TicketingDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new TicketingDbContext(options);
    }
}
