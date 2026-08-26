using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;

namespace Ticketing.ArchitectureTests;

/// <summary>
/// PDF Sprint 17'nin istedigi kod duzeni kurallari + testlerin
/// "bos yere gecmesini" engelleyen guvenlik agi.
/// </summary>
public class ConventionTests
{
    // ---------------------------------------------------------------
    // GUVENLIK AGI
    // ---------------------------------------------------------------

    /// <summary>
    /// Bu test, diger tum architecture testlerinin ANLAMLI olmasini garanti eder.
    ///
    /// Problem su: NetArchTest "su kurali ihlal eden tip var mi?" diye bakar.
    /// Eger bir assembly'yi hic yukleyemezse veya icinde hic tip yoksa,
    /// "ihlal eden yok" der ve test YESIL yanar. Yani kural calismadigi halde
    /// calisiyormus gibi gorunur.
    ///
    /// Bu, bir testin yapabilecegi en kotu seydir: yanlis guven vermek.
    /// Asagidaki test, her katmanda gercekten tip oldugunu dogrular.
    /// Bir katman bos kalirsa veya assembly yuklenemezse burasi kirmizi yanar.
    /// </summary>
    [Theory]
    [MemberData(nameof(TumKatmanAssemblyleri))]
    public void HerKatman_EnAzBirTipIcermeli(string katmanAdi, Assembly assembly)
    {
        var tipSayisi = Types.InAssembly(assembly).GetTypes().Count();

        tipSayisi.Should().BeGreaterThan(0,
            "{0} katmaninda hic tip bulunamadi. Bu durumda o katmani hedefleyen " +
            "architecture testleri bos yere gecer ve yanlis guven verir.",
            katmanAdi);
    }

    public static TheoryData<string, Assembly> TumKatmanAssemblyleri() => new()
    {
        { Layers.Domain,         Ticketing.Domain.AssemblyReference.Assembly },
        { Layers.Application,    Ticketing.Application.AssemblyReference.Assembly },
        { Layers.Infrastructure, Ticketing.Infrastructure.AssemblyReference.Assembly },
        { Layers.Persistence,    Ticketing.Persistence.AssemblyReference.Assembly },
        { Layers.WebApi,         Ticketing.WebApi.AssemblyReference.Assembly }
    };

    // ---------------------------------------------------------------
    // PDF: "Controller dogrudan DbContext kullanmamalidir."
    // ---------------------------------------------------------------

    [Fact]
    public void Controller_DogrudanDbContextKullanmamali()
    {
        // WebApi projesi Persistence'i referans aliyor (DI kaydi icin gerekli),
        // yani teknik olarak bir controller DbContext enjekte EDEBILIR.
        // Bu test tam da bunu engellemek icin var.
        //
        // Neden yasak? Controller'in isi HTTP'dir: istegi almak, yetkiyi
        // dogrulamak, cevabi donmek. Veri erisimi Application katmaninin isidir.
        // Controller'da sorgu yazilirsa o mantik test edilemez hale gelir ve
        // ayni sorgu baska bir endpoint'te tekrar yazilir.
        var sonuc = Types.InAssembly(Ticketing.WebApi.AssemblyReference.Assembly)
            .That().HaveNameEndingWith("Controller")
            .Should()
            .NotHaveDependencyOnAny("Microsoft.EntityFrameworkCore", Layers.Persistence)
            .GetResult();

        sonuc.IsSuccessful.Should().BeTrue(
            "Controller veri erisimini MediatR uzerinden Application katmanina " +
            "devretmelidir. Ihlal edenler: {0}",
            Ihlaller(sonuc));
    }

    // ---------------------------------------------------------------
    // PDF: "Handler siniflari dogru namespace altinda bulunmalidir."
    // ---------------------------------------------------------------

    [Fact]
    public void Handler_SiniflariApplicationKatmanindaOlmali()
    {
        // Handler'lar CQRS'in is mantigini tasir. Bunlarin WebApi veya
        // Infrastructure'da olmasi, is mantiginin altyapiya sizmasi demektir.
        var yanlisYerdekiHandlerlar = Types.InAssemblies(
            [
                Ticketing.WebApi.AssemblyReference.Assembly,
                Ticketing.Infrastructure.AssemblyReference.Assembly,
                Ticketing.Persistence.AssemblyReference.Assembly
            ])
            .That().HaveNameEndingWith("Handler")
            .GetTypes()
            .ToList();

        yanlisYerdekiHandlerlar.Should().BeEmpty(
            "Handler siniflari yalnizca Ticketing.Application icinde bulunmalidir. " +
            "Yanlis yerdekiler: {0}",
            string.Join(", ", yanlisYerdekiHandlerlar.Select(t => t.FullName)));
    }

    // ---------------------------------------------------------------
    // Ek kural: Sealed olmayan siniflar
    // ---------------------------------------------------------------

    [Fact]
    public void Handler_SiniflariSealedOlmali()
    {
        // Handler'lardan miras alinmasi icin bir sebep yok. sealed yapmak
        // hem niyeti acikca belirtir hem de JIT'in metod cagrilarini
        // devirtualize etmesine izin vererek kucuk bir performans kazandirir.
        //
        // Not: Bu test su an hic handler olmadigi icin bos gecer. Sprint 3'te
        // ilk handler'i yazdigimizda anlam kazanacak. Simdiden yaziyorum ki
        // ilk handler yanlis yazildiginda hemen fark edelim.
        var sonuc = Types.InAssembly(Ticketing.Application.AssemblyReference.Assembly)
            .That().HaveNameEndingWith("Handler")
            .Should().BeSealed()
            .GetResult();

        sonuc.IsSuccessful.Should().BeTrue(
            "Handler siniflari sealed olmalidir. Ihlal edenler: {0}",
            Ihlaller(sonuc));
    }

    private static string Ihlaller(TestResult sonuc)
        => sonuc.FailingTypeNames is null
            ? "(yok)"
            : string.Join(", ", sonuc.FailingTypeNames);
}
