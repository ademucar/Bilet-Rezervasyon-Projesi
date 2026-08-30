using FluentAssertions;
using NetArchTest.Rules;

namespace Ticketing.ArchitectureTests;

/// <summary>
/// Onion Architecture'in bagimlilik kurallarini test eder.
///
/// PDF Sprint 2 ve Sprint 17 acikca su iki kurali istiyor:
///   - "Domain katmani Infrastructure katmanini referans almamalidir."
///   - "Application katmani Web API katmanini referans almamalidir."
///
/// Bu testleri projenin en basinda yaziyorum, sonunda degil.
/// Sebebi su: katman ihlali yavas yavas ve fark edilmeden olur. Birisi
/// Application'da bir DbContext'e ihtiyac duyar, "sadece bu seferlik" der,
/// referansi ekler. Uc ay sonra 40 yerde ayni ihlal vardir ve geri donusu
/// imkansizdir. Bu test, ihlalin oldugu gun derlemeyi kirar.
/// </summary>
public class LayerDependencyTests
{
    // 1. DOMAIN — Onion'in en ic halkasi. Hicbir seye bagli olamaz.

    [Fact]
    public void Domain_DisKatmanlarinHicbirineBagliOlmamali()
    {
        // Domain'in bagimli olmasi YASAK olan tum katmanlar
        var yasakliKatmanlar = new[]
        {
            Layers.Application,
            Layers.Infrastructure,
            Layers.Persistence,
            Layers.WebApi
        };

        var sonuc = Types.InAssembly(Ticketing.Domain.AssemblyReference.Assembly)
            .Should()
            .NotHaveDependencyOnAny(yasakliKatmanlar)
            .GetResult();

        sonuc.IsSuccessful.Should().BeTrue(
            "Domain katmani Onion'in merkezidir ve disariya bagimli olamaz. " +
            "Ihlal eden tipler: {0}",
            IhlalleriYazdir(sonuc));
    }

    [Fact]
    public void Domain_EntityFrameworkKullanmamali()
    {
        // Bu, yukaridaki testin yakalayamayacagi bir ihlali yakalar:
        // Domain'in dogrudan NuGet uzerinden EF Core'a bagimli olmasi.
        // Domain saf C# olmali; [Key], [Table] gibi EF attribute'lari
        // veya DbSet<> gibi tipler burada bulunmamali.
        var sonuc = Types.InAssembly(Ticketing.Domain.AssemblyReference.Assembly)
            .Should()
            .NotHaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Npgsql")
            .GetResult();

        sonuc.IsSuccessful.Should().BeTrue(
            "Domain framework bagimsiz olmalidir. EF konfigurasyonu Persistence " +
            "katmaninda IEntityTypeConfiguration ile yapilacak. Ihlal edenler: {0}",
            IhlalleriYazdir(sonuc));
    }

    // 2. APPLICATION — Domain'i bilir, disariyi bilmez.

    [Fact]
    public void Application_WebApiKatmaniniReferansAlmamali()
    {
        var sonuc = Types.InAssembly(Ticketing.Application.AssemblyReference.Assembly)
            .Should()
            .NotHaveDependencyOn(Layers.WebApi)
            .GetResult();

        sonuc.IsSuccessful.Should().BeTrue(
            "Application, kendisini kimin cagirdigini bilmemelidir. " +
            "Yarin Web API yerine bir gRPC servisi veya konsol uygulamasi " +
            "ayni use case'leri cagirabilmeli. Ihlal edenler: {0}",
            IhlalleriYazdir(sonuc));
    }

    [Fact]
    public void Application_AltyapiKatmanlariniReferansAlmamali()
    {
        var sonuc = Types.InAssembly(Ticketing.Application.AssemblyReference.Assembly)
            .Should()
            .NotHaveDependencyOnAny(Layers.Infrastructure, Layers.Persistence)
            .GetResult();

        sonuc.IsSuccessful.Should().BeTrue(
            "Application altyapiyi ARAYUZ uzerinden kullanmalidir (IUnitOfWork, " +
            "ICacheService, IEmailService gibi). Somut implementasyon Infrastructure " +
            "veya Persistence'ta yasar ve DI ile baglanir. Bu, Dependency Inversion " +
            "ilkesinin ta kendisidir. Ihlal edenler: {0}",
            IhlalleriYazdir(sonuc));
    }

    // 3. Persistence / infrastructure — kardestirler, birbirini bilmezler.

    [Fact]
    public void Persistence_WebApiKatmaniniReferansAlmamali()
    {
        var sonuc = Types.InAssembly(Ticketing.Persistence.AssemblyReference.Assembly)
            .Should()
            .NotHaveDependencyOn(Layers.WebApi)
            .GetResult();

        sonuc.IsSuccessful.Should().BeTrue(
            "Ihlal edenler: {0}", IhlalleriYazdir(sonuc));
    }

    [Fact]
    public void Infrastructure_WebApiKatmaniniReferansAlmamali()
    {
        var sonuc = Types.InAssembly(Ticketing.Infrastructure.AssemblyReference.Assembly)
            .Should()
            .NotHaveDependencyOn(Layers.WebApi)
            .GetResult();

        sonuc.IsSuccessful.Should().BeTrue(
            "Ihlal edenler: {0}", IhlalleriYazdir(sonuc));
    }

    /// <summary>
    /// NetArchTest basarisiz oldugunda ihlal eden tip isimlerini dondurur.
    /// Test mesajinda "su test patladi" demek yetmez; HANGI sinifin
    /// kurali ihlal ettigini de soylemeli ki hemen duzeltebilelim.
    /// </summary>
    private static string IhlalleriYazdir(TestResult sonuc)
        => sonuc.FailingTypeNames is null
            ? "(yok)"
            : string.Join(", ", sonuc.FailingTypeNames);
}
