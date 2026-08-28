using System.Diagnostics;

namespace Ticketing.Application.Common.Observability;

/// <summary>
/// Uygulamanin KENDI urettigi izleme (trace) kaynagi.
/// PDF Sprint 16: "Background job islemleri" takip edilmelidir.
/// </summary>
/// <remarks>
/// ==================================================================
/// NEDEN APPLICATION KATMANINDA?
/// ==================================================================
/// Ilk yazdigimda bunu WebApi altina koymustum. Sonra arka plan
/// isleri (Infrastructure) buna ihtiyac duyunca sorun cikti:
/// Infrastructure, WebApi'ye referans VEREMEZ -- Onion mimarisinin
/// temel kurali ve mimari testimiz bunu zaten reddediyor.
///
/// Her iki katmanin da (WebApi ve Infrastructure) gorebildigi tek
/// yer Application. Buraya tasidim.
///
/// Bu, bagimliligi TERS CEVIRMENIN kucuk ama tipik bir ornegi:
/// ortak ihtiyac, ortak bagimlilik olan katmana cikiyor.
///
/// ------------------------------------------------------------------
/// NEDEN System.Diagnostics, OpenTelemetry DEGIL?
/// ------------------------------------------------------------------
/// ActivitySource, .NET'in KENDI sinifi. OpenTelemetry paketine
/// bagimli degiliz.
///
/// Bu onemli: Application katmani izleme SAGLAYICISINI bilmiyor.
/// Yarin OpenTelemetry yerine baska bir sey kullanilirsa buradaki
/// kodun tek satiri degismez -- yalnizca WebApi'deki dinleyici
/// yapilandirmasi degisir.
/// ==================================================================
/// </remarks>
public static class AppActivitySource
{
    /// <summary>
    /// Kaynak adi. WebApi tarafinda AddSource(...) ile dinleniyor.
    /// </summary>
    /// <remarks>
    /// Sabit olarak paylasiyorum cunku ad iki yerde birden gecmek
    /// zorunda: burada (uretici) ve OpenTelemetry yapilandirmasinda
    /// (dinleyici). Iki yerde elle yazsaydik ve biri degisirse,
    /// izleme SESSIZCE durur -- hata vermez, sadece hicbir iz
    /// uretilmez. Tam olarak fark edilmesi en zor ariza turu.
    /// </remarks>
    public const string Name = "Ticketing";

    /// <summary>
    /// Uygulama genelinde tek ActivitySource ornegi.
    /// </summary>
    /// <remarks>
    /// static readonly: dinleyiciler bu ORNEGE kaydoluyor. Her
    /// cagride yenisini uretseydik dinleyici hicbirini gormezdi ve
    /// hicbir iz uretilmezdi.
    /// </remarks>
    public static readonly ActivitySource Instance = new(Name);

    /// <summary>
    /// Bir arka plan isi icin izleme kapsami baslatir.
    /// </summary>
    /// <remarks>
    /// ==============================================================
    /// DONUS DEGERI null OLABILIR -- VE BU NORMAL
    /// ==============================================================
    /// Hicbir dinleyici yoksa StartActivity null doner. Bu bir hata
    /// degil, bilincli bir performans tasarimi: izleme kapaliyken
    /// hicbir nesne tahsis edilmiyor.
    ///
    /// Cagiran taraf "using var activity = ..." yazdigi icin null
    /// olmasi sorun degil (using, null uzerinde hicbir sey yapmaz).
    ///
    /// ActivityKind.Internal: bu is bir HTTP istegi degil, disari
    /// giden bir cagri da degil -- uygulamanin kendi ic islemi.
    /// Dogru turu vermek, izleme arayuzunde islerin HTTP
    /// isteklerinden ayri gruplanmasini sagliyor.
    /// ==============================================================
    /// </remarks>
    public static Activity? StartJob(string jobName)
    {
        var activity = Instance.StartActivity(jobName, ActivityKind.Internal);

        // Etiket, izleme arayuzunde filtreleme icin:
        // "job.name = ExpireReservations olan tum izler".
        activity?.SetTag("job.name", jobName);

        return activity;
    }
}
