using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Ticketing.Persistence;

/// <summary>
/// "dotnet ef migrations add" ve "dotnet ef database update" komutlarinin
/// kullandigi tasarım zamani fabrikasi.
///
/// BU SINIF NEDEN GEREKLI?
///
/// EF Core araclari migration uretirken uygulamayi baslatmaya çalışır:
/// Program.cs'i calistirir, DI konteynerini kurar, DbContext'i alır.
///
/// Bunun iki sorunu var:
///
/// 1) appsettings.Development.json dosyasi .gitignore'da (içinde
///    connection string var). Projeyi yeni klonlayan biri
///    "dotnet ef migrations add" calistirdiginda uygulama
///    "connection string bulunamadı" diye patlardi -- oysa migration
///    URETMEK için gerçek bir veritabanina hiç gerek yok.
///
/// 2) CI/CD ortaminda veritabani yokken de migration dogrulamasi
///    yapabilmek istiyorum.
///
/// Bu fabrika devreye girdiginde EF, Program.cs'i hiç calistirmaz.
/// Buradaki sahte connection string yalnızca SQL URETMEK için kullanilir;
/// hiçbir bağlantı acilmaz.
///
/// Gerçek veritabanina uygulama yaparken (database update) ise
/// aşağıdaki environment degiskeni okunur.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<TicketingDbContext>
{
    public TicketingDbContext CreateDbContext(string[] args)
    {
        // Önce gerçek baglantiyi dene (database update için gerekli),
        // yoksa yalnızca SQL uretmeye yeten sahte bir degere dus.
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? "Host=localhost;Port=5432;Database=ticketing;Username=ticketing;Password=ticketing_dev_password";

        var options = new DbContextOptionsBuilder<TicketingDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new TicketingDbContext(options);
    }
}
