using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;

namespace Ticketing.ArchitectureTests;

/// <summary>
/// PDF Sprint 17 mimari kurali:
/// "Domain Entity dogrudan DTO dondurmemelidir."
/// </summary>
/// <remarks>
/// ==================================================================
/// BU KURAL NEDEN VAR?
/// ==================================================================
/// Bir entity'nin uzerine ToDto() yazmak cok pratik gorunuyor:
///
///     public EventDto ToDto() => new(Id, Title, ...);
///
/// Ama bunu yaptigin anda Domain katmani, DIS DUNYANIN sozlesmesini
/// bilmek zorunda kaliyor. Sonuclari:
///
///   1) API sozlesmesi degistiginde DOMAIN degisiyor. Frontend bir
///      alan istedi diye is kurallarinin durdugu dosyayi acmak
///      zorunda kaliyorsun.
///
///   2) Ayni entity'nin farkli baglamlarda farkli DTO'lari oluyor
///      (liste ozeti, detay, admin gorunumu). Entity uzerinde
///      ToListDto(), ToDetailDto(), ToAdminDto() birikiyor.
///
///   3) EN TEHLIKELISI: DTO'yu entity uretiyorsa, hangi alanlarin
///      disariya sizdigini kontrol etmek zorlasiyor. Entity'ye yeni
///      bir alan ekleyen kisi, o alanin API yanitina da eklendigini
///      fark etmeyebilir.
///
/// Projemizde DTO donusumu Application katmaninda, ToDto() uzanti
/// metotlariyla yapiliyor (ornegin PaymentQueries icindeki
/// projeksiyonlar). Bu test o sinirin korundugunu dogruluyor.
/// ==================================================================
/// </remarks>
public class DomainPurityTests
{
    /// <summary>
    /// Domain tiplerinin hicbir public metodu "Dto" ile biten bir tip
    /// dondurmemeli.
    /// </summary>
    [Fact]
    public void Domain_Entity_DTO_dondurmemeli()
    {
        var ihlaller = new List<string>();

        var domainTipleri = Types
            .InAssembly(Ticketing.Domain.AssemblyReference.Assembly)
            .GetTypes();

        foreach (var tip in domainTipleri)
        {
            // ==================================================
            // YALNIZCA public YUZEY INCELENIYOR
            // ==================================================
            // private bir yardimci metodun ne dondurdugu disariya
            // sizmaz. Kural, KATMANLAR ARASI sozlesmeyle ilgili --
            // ic ayrintiyla degil.
            //
            // DeclaredOnly: miras alinan (object.ToString gibi)
            // metotlar her tipte tekrar sayilmasin.
            // ==================================================
            var metotlar = tip.GetMethods(
                BindingFlags.Public | BindingFlags.Instance |
                BindingFlags.Static | BindingFlags.DeclaredOnly);

            foreach (var metot in metotlar)
            {
                if (DtoTuruMu(metot.ReturnType))
                {
                    ihlaller.Add($"{tip.Name}.{metot.Name} -> {metot.ReturnType.Name}");
                }
            }

            // Ozellikler (property) de ayni kurala tabi: bir entity
            // uzerinde "public EventDto Summary => ..." tanimlamak,
            // metotla yapmakla ayni sorunu yaratirdi.
            foreach (var ozellik in tip.GetProperties(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (DtoTuruMu(ozellik.PropertyType))
                {
                    ihlaller.Add($"{tip.Name}.{ozellik.Name} : {ozellik.PropertyType.Name}");
                }
            }
        }

        ihlaller.Should().BeEmpty(
            "Domain tipleri DTO dondurmemeli. DTO donusumu Application " +
            "katmaninin isi; aksi halde API sozlesmesi degistiginde is " +
            "kurallarinin durdugu dosyalari acmak gerekir. Ihlaller: {0}",
            string.Join(", ", ihlaller));
    }

    /// <summary>
    /// Domain katmani, Application katmanindaki tipleri TANIMAMALI.
    /// </summary>
    /// <remarks>
    /// Yukaridaki test ad tabanli ("Dto" ile biten). Bu test ise
    /// yapisal: Domain'in Application'a hic bagimliligi olmamali.
    ///
    /// Ikisi birlikte gerekli. Ad tabanli kural, DTO'su "Response"
    /// veya "Model" diye adlandirilmis bir tipi kaciririr; bagimlilik
    /// kurali ise onu da yakalar.
    ///
    /// Tersi de dogru: Domain kendi icinde "SeatMapDto" adinda bir
    /// tip tanimlarsa bagimlilik testi bunu goremez, ad testi gorur.
    /// </remarks>
    [Fact]
    public void Domain_Application_katmanini_referans_almamali()
    {
        var sonuc = Types
            .InAssembly(Ticketing.Domain.AssemblyReference.Assembly)
            .Should()
            .NotHaveDependencyOn(Layers.Application)
            .GetResult();

        sonuc.IsSuccessful.Should().BeTrue(
            "Domain, Application katmanini tanimamali. Ihlal eden tipler: {0}",
            string.Join(", ", sonuc.FailingTypeNames ?? []));
    }

    /// <summary>
    /// "Dto", "Response" veya "ViewModel" ile biten tipler DTO sayilir.
    /// </summary>
    private static bool DtoTuruMu(Type tip)
    {
        // Koleksiyonlarin ICINE de bakiyoruz: IReadOnlyList&lt;EventDto&gt;
        // donduren bir metot da ihlaldir.
        //
        // Bunu unutsaydik kural kolayca atlatilirdi: tekil DTO yerine
        // liste donduren bir metot yazmak yeterdi.
        if (tip.IsGenericType)
        {
            return tip.GetGenericArguments().Any(DtoTuruMu);
        }

        var ad = tip.Name;

        return ad.EndsWith("Dto", StringComparison.Ordinal)
            || ad.EndsWith("Response", StringComparison.Ordinal)
            || ad.EndsWith("ViewModel", StringComparison.Ordinal);
    }
}
