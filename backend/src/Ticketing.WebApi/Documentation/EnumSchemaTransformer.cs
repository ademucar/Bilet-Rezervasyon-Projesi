using System.Globalization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;

namespace Ticketing.WebApi.Documentation;

/// <summary>
/// Enum semalarina değerleri ve ISIMLERINI ekler.
/// PDF Sprint 18: "Endpoint aciklamalari" ve OpenAPI client arastirmasi.
/// </summary>
/// <remarks>
/// BU SINIF, ORVAL ARASTIRMASI SIRASINDA BULUNAN BOSLUGU KAPATIYOR
///
/// Sprint 18'de OpenAPI'den TypeScript istemci uretimini denedim
/// (Orval). Uretilen tip su cikti:
///
///     export type ReservationStatus = number;
///
/// Yani hiçbir sey. Sebep belgede goruldu:
///
///     "ReservationStatus": { "type": "integer" }
///
/// Enum'un HANGI sayinin NE anlama geldigi belgede HİÇ YOKTU.
///
/// Bunun sonucu yalnızca kod uretimiyle sinirli değil: Swagger'i
/// acan bir istemci gelistiricisi `status: 3` gordugunde ne
/// yapacagini bilemiyordu. Kaynak koda erişimi olmayan biri için
/// bu alan tamamen anlamsizdi.
///
/// NEDEN STRING'E CEVIRMEDIM?
///
/// JsonStringEnumConverter ekleyip enum'lari metin olarak
/// gonderebilirdim ("Confirmed" gibi). Daha okunakli olurdu.
///
/// YAPMADIM çünkü bu KIRICI bir degisiklik: frontend'imiz sayilarla
/// karsilastirma yapiyor (ReservationStatus.Confirmed === 4) ve
/// Sprint 17'de yazdigim testler de oyle. Dokumantasyonu
/// iyilestirmek için calisan bir sozlesmeyi bozmak yanlış takas.
///
/// Bunun yerine sayilari KORUYUP anlamlarini belgeye ekliyorum:
///
///     "ReservationStatus": {
///       "type": "integer",
///       "enum": [1, 2, 3, 4, 5, 6],
///       "x-enum-varnames": ["Pending", "Locked", ...],
///       "description": "1 = Pending, 2 = Locked, ..."
///     }
///
/// x-enum-varnames, OpenAPI Generator ve NSwag'in tanidigi yaygin
/// bir uzanti; description ise HER aracta ve insan gozunde
/// çalışıyor.
/// </remarks>
internal sealed class EnumSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(context);

        var tip = context.JsonTypeInfo.Type;

        // Nullable enum'lari da yakala: ReservationStatus? -> ReservationStatus
        var gercekTip = Nullable.GetUnderlyingType(tip) ?? tip;

        if (!gercekTip.IsEnum)
        {
            return Task.CompletedTask;
        }

        var adlar = Enum.GetNames(gercekTip);
        var degerler = Enum.GetValues(gercekTip);

        schema.Enum.Clear();

        var eslesme = new List<string>();

        for (var i = 0; i < adlar.Length; i++)
        {
            var sayi = Convert.ToInt32(degerler.GetValue(i), CultureInfo.InvariantCulture);

            schema.Enum.Add(new OpenApiInteger(sayi));
            eslesme.Add($"`{sayi}` = {adlar[i]}");
        }

        // x-enum-varnames: KOD URETICILERI ICIN
        //
        // OpenAPI standardinda enum değerleri var ama ISIMLERI yok.
        // Bu uzanti, araclarin anlamlı enum uretebilmesi için
        // toplulukta yaygınlasan çözüm.
        //
        // Taniyamayan bir arac için ZARARSIZ: bilinmeyen "x-" alanlari
        // yok sayiliyor.
        //
        // OpenApiArray, List<IOpenApiAny> turevi; koleksiyon
        // baslatici yerine AddRange kullanıyorum (spread operatoru
        // burada Index olarak yorumlanip derlenmiyor).
        var isimler = new OpenApiArray();
        isimler.AddRange(adlar.Select(a => new OpenApiString(a)));

        schema.Extensions["x-enum-varnames"] = isimler;

        // Açıklama HER yerde çalışıyor: insan da okuyor, arac da
        // gostermeye devam ediyor.
        var mevcut = string.IsNullOrWhiteSpace(schema.Description)
            ? string.Empty
            : schema.Description + "\n\n";

        schema.Description = mevcut + string.Join(", ", eslesme);

        return Task.CompletedTask;
    }
}
