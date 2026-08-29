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
    // GUVENLIK AGI

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

    // PDF: "Controller dogrudan DbContext kullanmamalidir."

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

    // PDF: "Handler siniflari dogru namespace altinda bulunmalidir."

    [Fact]
    public void Handler_SiniflariApplicationKatmanindaOlmali()
    {
        // Handler'lar CQRS'in is mantigini tasir. Bunlarin WebApi veya
        // Infrastructure'da olmasi, is mantiginin altyapiya sizmasi demektir.
        //
        // BU TEST BIR KEZ HAKLI OLARAK KIRMIZI YANDI -- VE KURAL DARALTILDI
        //
        // Ilk yazisimda kural "adi 'Handler' ile biten HER sinif" seklindeydi.
        // WebApi'ye GlobalExceptionHandler eklendiginde test kirmizi yandi.
        //
        // Inceleyince gorduk ki bu bir CQRS handler'i DEGIL: ASP.NET Core'un
        // IExceptionHandler arayuzunu uygulayan bir altyapi bileseni ve
        // dogru yerde duruyor.
        //
        // Yani KOD dogruydu, KURAL fazla genisti. Testi susturmak yerine
        // kurali gercekte ne demek istedigimize gore daralttim: ASP.NET
        // altyapi arayuzlerini uygulayan tipler bu kuralin disinda.
        //
        // Bu ayrimi yapmak onemli: bir test kirmizi yandiginda refleksle
        // "testi kaldirayim" demek, testin degerini yok eder. Once
        // "kod mu yanlis, kural mi?" diye sorulmali.
        // ASP.NET Core'un "Handler" ile biten altyapi arayuzleri.
        //
        // Bu liste ZAMANLA BUYUYOR ve bu NORMAL:
        //   Sprint 2'de -> IExceptionHandler       (GlobalExceptionHandler)
        //   Sprint 5'te -> IAuthorizationHandler   (EventOwnerAuthorizationHandler)
        //
        // Her seferinde test kirmizi yaniyor, bakiyorum, "bu bir CQRS
        // handler'i degil, framework bileseni" diyip listeye ekliyorum.
        //
        // Bu dongu SAGLIKLI: test her yeni "Handler" sinifini onumuze
        // getiriyor ve bilincli bir karar vermemizi zorluyor. Kurali
        // bastan cok gevsek yazsaydik (ornegin yalnizca "CommandHandler"
        // ile bitenlere baksaydim) yanlis yere konmus gercek bir CQRS
        // handler'i gozden kacardi.
        var altyapiArayuzleri = new[]
        {
            typeof(Microsoft.AspNetCore.Diagnostics.IExceptionHandler),
            typeof(Microsoft.AspNetCore.Authorization.IAuthorizationHandler)
        };

        var yanlisYerdekiHandlerlar = Types.InAssemblies(
            [
                Ticketing.WebApi.AssemblyReference.Assembly,
                Ticketing.Infrastructure.AssemblyReference.Assembly,
                Ticketing.Persistence.AssemblyReference.Assembly
            ])
            .That().HaveNameEndingWith("Handler")
            .GetTypes()
            .Where(t => !altyapiArayuzleri.Any(i => i.IsAssignableFrom(t)))
            .ToList();

        yanlisYerdekiHandlerlar.Should().BeEmpty(
            "CQRS handler siniflari yalnizca Ticketing.Application icinde bulunmalidir. " +
            "Yanlis yerdekiler: {0}",
            string.Join(", ", yanlisYerdekiHandlerlar.Select(t => t.FullName)));
    }

    // Ek kural: Sealed olmayan siniflar

    [Fact]
    public void Handler_SiniflariSealedOlmali()
    {
        // Handler'lardan miras alinmasi icin bir sebep yok. sealed yapmak
        // hem niyeti acikca belirtir hem de JIT'in metod cagrilarini
        // devirtualize etmesine izin vererek kucuk bir performans kazandirir.
        //
        // BU TEST SPRINT 9'DA KIRMIZI YANDI -- YINE KURAL FAZLA GENISTI
        //
        // Sprint 9'da IOutboxMessageHandler arayuzunu ekleyince test
        // basarisiz oldu: "IOutboxMessageHandler sealed degil".
        //
        // Elbette degil -- ARAYUZLER SEALED OLAMAZ. Bir arayuzu sealed
        // yapmak dilde mumkun degildir ve zaten anlamsizdir: arayuzun
        // varlik sebebi uygulanabilmesidir.
        //
        // Yani kod dogruydu, kural yine fazla genisti. Kurali daralttim:
        // yalnizca SINIFLARA bakiyor.
        //
        // Bu, ayni dosyada ucuncu daraltma (bkz. yukaridaki
        // altyapiArayuzleri listesi). Her seferinde ayni soruyu
        // soruyorum: "kod mu yanlis, kural mi?" Ve her seferinde
        // testi silmek yerine kurali kesinlestiriyorum. Boylece test
        // gercek ihlalleri yakalamaya devam ediyor.
        var sonuc = Types.InAssembly(Ticketing.Application.AssemblyReference.Assembly)
            .That().AreClasses()
            .And().HaveNameEndingWith("Handler")
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
