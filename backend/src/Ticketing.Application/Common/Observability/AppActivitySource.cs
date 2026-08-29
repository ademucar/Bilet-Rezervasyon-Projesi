using System.Diagnostics;

namespace Ticketing.Application.Common.Observability;

/// <summary>
/// Uygulamanin KENDİ urettigi izleme (trace) kaynagi.
/// PDF Sprint 16: "Background job islemleri" takip edilmelidir.
/// </summary>
/// <remarks>
/// NEDEN APPLICATION KATMANINDA?
///
/// İlk yazdigimda bunu WebApi altina koymustum. Sonra arka plan
/// isleri (Infrastructure) buna ihtiyac duyunca sorun cikti:
/// Infrastructure, WebApi'ye referans VEREMEZ -- Onion mimarisinin
/// temel kuralı ve mimari testim bunu zaten reddediyor.
///
/// Her iki katmanin da (WebApi ve Infrastructure) gorebildigi tek
/// yer Application. Buraya tasidim.
///
/// Bu, bagimliligi TERS CEVIRMENIN küçük ama tipik bir ornegi:
/// ortak ihtiyac, ortak bagimlilik olan katmana cikiyor.
///
/// NEDEN System.Diagnostics, OpenTelemetry DEĞİL?
///
/// ActivitySource, .NET'in KENDİ sinifi. OpenTelemetry paketine
/// bagimli degiliz.
///
/// Bu önemli: Application katmani izleme SAGLAYICISINI bilmiyor.
/// Yarin OpenTelemetry yerine başka bir sey kullanilirsa buradaki
/// kodun tek satiri degismez -- yalnızca WebApi'deki dinleyici
/// yapilandirmasi degisir.
/// </remarks>
public static class AppActivitySource
{
    /// <summary>
    /// Kaynak adı. WebApi tarafında AddSource(...) ile dinleniyor.
    /// </summary>
    /// <remarks>
    /// Sabit olarak paylasiyorum çünkü ad iki yerde birden gecmek
    /// zorunda: burada (uretici) ve OpenTelemetry yapilandirmasinda
    /// (dinleyici). Iki yerde elle yazsaydım ve biri degisirse,
    /// izleme SESSIZCE durur -- hata vermez, sadece hiçbir iz
    /// uretilmez. Tam olarak fark edilmesi en zor ariza türü.
    /// </remarks>
    public const string Name = "Ticketing";

    /// <summary>
    /// Uygulama genelinde tek ActivitySource ornegi.
    /// </summary>
    /// <remarks>
    /// static readonly: dinleyiciler bu ORNEGE kaydoluyor. Her
    /// cagride yenisini uretseydik dinleyici hicbirini gormezdi ve
    /// hiçbir iz uretilmezdi.
    /// </remarks>
    public static readonly ActivitySource Instance = new(Name);

    /// <summary>
    /// Bir arka plan isi için izleme kapsami baslatir.
    /// </summary>
    /// <remarks>
    /// DONUS DEGERI null OLABILIR -- VE BU NORMAL
    ///
    /// Hicbir dinleyici yoksa StartActivity null döner. Bu bir hata
    /// değil, bilinçli bir performans tasarimi: izleme kapaliyken
    /// hiçbir nesne tahsis edilmiyor.
    ///
    /// Cagiran taraf "using var activity = ..." yazdigi için null
    /// olmasını sorun değil (using, null uzerinde hiçbir sey yapmaz).
    ///
    /// ActivityKind.Internal: bu is bir HTTP isteği değil, disari
    /// giden bir cagri da değil -- uygulamanin kendi ic islemi.
    /// Dogru türü vermek, izleme arayuzunde islerin HTTP
    /// isteklerinden ayrı gruplanmasini sagliyor.
    /// </remarks>
    public static Activity? StartJob(string jobName)
    {
        var activity = Instance.StartActivity(jobName, ActivityKind.Internal);

        // Etiket, izleme arayuzunde filtreleme için:
        // "job.name = ExpireReservations olan tüm izler".
        activity?.SetTag("job.name", jobName);

        return activity;
    }
}
