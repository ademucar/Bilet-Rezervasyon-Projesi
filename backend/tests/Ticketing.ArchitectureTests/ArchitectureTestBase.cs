namespace Ticketing.ArchitectureTests;

/// <summary>
/// Tum architecture testlerinin ortak sabitleri.
///
/// Katman isimlerini string sabit olarak topluyorum cunku NetArchTest
/// namespace'leri metin olarak karsilastiriyor. Bu isimleri 20 ayri testte
/// tekrar tekrar yazsaydim, bir gun proje adini degistirdigimde
/// testler sessizce "hicbir tip bulamadim, demek ki kural saglaniyor"
/// diyerek YESIL kalirdi. En tehlikeli test, yanlis sebepten gecen testtir.
///
/// Bu yuzden asagida ayrica bir "koruma testi" var: her katmanda en az
/// bir tip bulundugunu dogruluyor.
/// </summary>
public static class Layers
{
    public const string Domain = "Ticketing.Domain";
    public const string Application = "Ticketing.Application";
    public const string Infrastructure = "Ticketing.Infrastructure";
    public const string Persistence = "Ticketing.Persistence";
    public const string WebApi = "Ticketing.WebApi";
}
