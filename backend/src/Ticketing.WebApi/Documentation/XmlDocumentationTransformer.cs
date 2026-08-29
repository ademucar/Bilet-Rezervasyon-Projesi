using System.Collections.Concurrent;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi.Models;

namespace Ticketing.WebApi.Documentation;

/// <summary>
/// Koddaki XML yorumlarini OpenAPI belgesine tasir.
/// PDF Sprint 18: "Endpoint aciklamalari", "Response ornekleri".
/// </summary>
/// <remarks>
/// BU SINIF NEDEN VAR? -- .NET 9'UN EKSIGI
///
/// GenerateDocumentationFile'i acip Swagger'a baktim: 78 ucun
/// HICBIRINDE açıklama yoktu.
///
/// Sebep: .NET 9'un yerlesik OpenAPI ureticisi
/// (Microsoft.AspNetCore.OpenApi) XML yorumlarini OKUMUYOR. O
/// ozellik .NET 10 ile geldi.
///
/// Swashbuckle kullansaydım hazır gelirdi -- ama o zaman iki ayrı
/// OpenAPI ureticisi çalışır ve iki farklı belge üretirdi.
///
/// Bu yüzden XML dosyalarini kendim okuyup belgeye bagliyorum.
/// Kod, cercevenin bir sonraki surumunde gereksiz hale gelecek;
/// o zaman silinebilir.
///
/// BILINCLI BASITLESTIRME: PARAMETRE IMZASI YOK SAYILIYOR
///
/// XML uye kimlikleri parametre turlerini de iceriyor:
///
///   M:Ticketing.WebApi.Controllers.EventsController.Publish(
///       System.Guid,System.Threading.CancellationToken)
///
/// Bu imzayi Reflection'dan birebir uretmek generic tipler, ref
/// parametreler ve dizi türleri yuzunden hataya çok açık.
///
/// Bunun yerine "tip adı + metot adı" ile esliyorum. Bedeli: ASIRI
/// YUKLENMIS (overload) metotlarda ilk eslesme kullanilir.
///
/// Bu bedeli kabul ediyorum çünkü controller'larimizda asiri yukleme
/// YOK -- her uc kendi adiyla duruyor. Ilerde eklenirse açıklama
/// yanlış uca gider; bu bir belge sorunu olur, calisma zamani
/// hatası değil.
/// </remarks>
internal sealed partial class XmlDocumentationTransformer : IOpenApiOperationTransformer
{
    /// <summary>Uye kimliği -> XML dugumu.</summary>
    private static readonly Lazy<Dictionary<string, XElement>> Belgeler =
        new(Yukle, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Aynı metot birden fazla kez sorulmasin.</summary>
    private static readonly ConcurrentDictionary<string, XElement?> Onbellek = new();

    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        var metot = context.Description.ActionDescriptor
            is Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor tanim
            ? tanim.MethodInfo
            : null;

        if (metot?.DeclaringType is null)
        {
            return Task.CompletedTask;
        }

        var dugum = Bul(metot);

        if (dugum is null)
        {
            return Task.CompletedTask;
        }

        // ---- <summary> -> özet ----
        var ozet = Metin(dugum.Element("summary"));

        if (!string.IsNullOrWhiteSpace(ozet))
        {
            operation.Summary = ozet;
        }

        // ---- <remarks> -> açıklama ----
        //
        // Aciklamayi EKLIYORUM, ustune yazmiyorum: AuthorizationTransformer
        // buraya yetki notunu koyuyor olabilir ve önü silmek istemiyorum.
        var aciklama = Metin(dugum.Element("remarks"));

        if (!string.IsNullOrWhiteSpace(aciklama))
        {
            operation.Description = string.IsNullOrWhiteSpace(operation.Description)
                ? aciklama
                : aciklama + operation.Description;
        }

        // ---- <response code="..."> -> yanit aciklamalari ----
        //
        // PDF: "Response ornekleri". Controller'larda zaten
        // <response code="201">Rezervasyon oluşturuldu...</response>
        // yaziliydi ama hiçbir yere gitmiyordu.
        foreach (var yanit in dugum.Elements("response"))
        {
            var kod = yanit.Attribute("code")?.Value;
            var metin = Metin(yanit);

            if (string.IsNullOrWhiteSpace(kod) || string.IsNullOrWhiteSpace(metin))
            {
                continue;
            }

            operation.Responses ??= new OpenApiResponses();

            if (operation.Responses.TryGetValue(kod, out var mevcut))
            {
                // Cercevenin urettigi genel metin ("OK", "Created")
                // yerine BENIM aciklamamiz gecsin.
                mevcut.Description = metin;
            }
            else
            {
                operation.Responses[kod] = new OpenApiResponse { Description = metin };
            }
        }

        // ---- <param> -> parametre aciklamalari ----
        foreach (var p in dugum.Elements("param"))
        {
            var ad = p.Attribute("name")?.Value;
            var metin = Metin(p);

            if (string.IsNullOrWhiteSpace(ad) || string.IsNullOrWhiteSpace(metin))
            {
                continue;
            }

            var hedef = operation.Parameters?
                .FirstOrDefault(x => string.Equals(x.Name, ad, StringComparison.OrdinalIgnoreCase));

            if (hedef is not null)
            {
                hedef.Description = metin;
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>Metodun XML dugumunu bulur (tip adı + metot adı ile).</summary>
    private static XElement? Bul(MethodInfo metot)
    {
        var anahtar = $"{metot.DeclaringType!.FullName}.{metot.Name}";

        return Onbellek.GetOrAdd(anahtar, a =>
        {
            var onek = $"M:{a}(";
            var tam = $"M:{a}";

            foreach (var (kimlik, dugum) in Belgeler.Value)
            {
                if (kimlik.StartsWith(onek, StringComparison.Ordinal)
                    || string.Equals(kimlik, tam, StringComparison.Ordinal))
                {
                    return dugum;
                }
            }

            return null;
        });
    }

    /// <summary>
    /// Uygulama klasorundeki TÜM XML dokumantasyon dosyalarini okur.
    /// </summary>
    /// <remarks>
    /// Yalnızca WebApi.xml'i okusaydim, Application katmanindaki
    /// DTO ve komut aciklamalari disarida kalırdı. Hepsini okumak
    /// daha fazla is değil ve belgeyi belirgin şekilde
    /// zenginlestiriyor.
    ///
    /// Dosya okunamazsa SESSIZCE geciyoruz: dokumantasyon eksikligi
    /// uygulamanin acilmasini engellememeli. Swagger'da bir açıklama
    /// gorunmemesi can sıkıcı; API'nin hiç acilmamasi felaket.
    ///
    /// CA1859: donus türü somut Dictionary. Analizor haklı --
    /// yalnızca bu sinif kullaniyor ve arayüz üzerinden erişim
    /// gereksiz bir dolayli cagri (interface dispatch) ekliyor.
    /// </remarks>
    private static Dictionary<string, XElement> Yukle()
    {
        var sonuc = new Dictionary<string, XElement>(StringComparer.Ordinal);

        foreach (var yol in Directory.GetFiles(AppContext.BaseDirectory, "Ticketing.*.xml"))
        {
            try
            {
                var belge = XDocument.Load(yol);

                foreach (var uye in belge.Root?.Element("members")?.Elements("member") ?? [])
                {
                    var ad = uye.Attribute("name")?.Value;

                    if (!string.IsNullOrEmpty(ad))
                    {
                        sonuc[ad] = uye;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or System.Xml.XmlException)
            {
                // Bozuk veya kilitli dosya: bu dosyayı atla.
            }
        }

        return sonuc;
    }

    /// <summary>
    /// XML dugumunun metnini okunabilir hale getirir.
    /// </summary>
    /// <remarks>
    /// YORUMLARIMIZ UZUN VE COK SATIRLI -- TEMIZLENMESI GEREKIYOR
    ///
    /// Kodda yazdigimiz yorumlar "=====" cizgileri ve girintiler
    /// iceriyor. Ham haliyle Swagger'a koysaydım okunamaz olurdu.
    ///
    /// Yaptiklarim:
    ///   - Her satirin bas/son bosluklarini kirp
    ///   - "=====" ve "-----" ayirici satirlarini at
    ///   - see cref etiketlerini sade metne cevir
    ///   - Ardisik boş satirlari tekile indir
    /// </remarks>
    private static string Metin(XElement? dugum)
    {
        if (dugum is null)
        {
            return string.Empty;
        }

        // <see cref="X"/> -> X (yalnızca son parca)
        foreach (var see in dugum.Descendants("see").ToList())
        {
            var hedef = see.Attribute("cref")?.Value ?? see.Attribute("langword")?.Value ?? string.Empty;
            var kisa = hedef.Contains('.', StringComparison.Ordinal)
                ? hedef[(hedef.LastIndexOf('.') + 1)..]
                : hedef.TrimStart('T', 'M', 'P', 'F', ':');

            see.ReplaceWith(new XText($"`{kisa}`"));
        }

        var satirlar = dugum.Value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Select(s => s.Trim())

            // Görsel ayiricilar Swagger'da anlamsiz.
            .Where(s => !AyiriciRegex().IsMatch(s))
            .ToList();

        var metin = string.Join('\n', satirlar);

        // Ucten fazla ardisik yeni satiri ikiye indir.
        metin = FazlaBoslukRegex().Replace(metin, "\n\n");

        return metin.Trim();
    }

    [GeneratedRegex(@"^[=\-]{3,}$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex AyiriciRegex();

    [GeneratedRegex(@"\n{3,}", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex FazlaBoslukRegex();
}
