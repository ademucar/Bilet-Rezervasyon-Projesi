using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;

namespace Ticketing.WebApi.Documentation;

/// <summary>
/// İstek govdelerine örnek deger ekler. PDF Sprint 18: "Request ornekleri".
/// </summary>
/// <remarks>
/// ==================================================================
/// NEDEN ORNEK GEREKLI? SEMA ZATEN VAR
/// ==================================================================
/// OpenAPI semasi alan adlarini ve turlerini söylüyor ama GECERLI
/// bir degerin neye benzedigini soylemiyor.
///
/// Swagger'in urettigi varsayılan örnek su:
///
///     { "email": "string", "password": "string" }
///
/// Bunu "Try it out" ile gonderirseniz 400 alirsiniz -- şifre
/// kurallarina uymuyor. API'yi ilk kez deneyen kişi burada takilir
/// ve hatanin kendisinde mi API'de mi olduğunu anlayamaz.
///
/// Gercekci ornekler, ilk denemeyi CALISIR hale getiriyor.
///
/// ------------------------------------------------------------------
/// ORNEKLER GERCEK KURALLARLA UYUMLU OLMALI
/// ------------------------------------------------------------------
/// Ornek şifre "Şifre123!" -- çünkü dogrulayici en az 8 karakter,
/// bir büyük harf ve bir rakam istiyor (Sprint 3). "string" veya
/// "test" yazsaydık örnek REDDEDILIRDI ve dokumantasyon kendi
/// API'siyle celisirdi.
/// ==================================================================
/// </remarks>
internal sealed class RequestExampleTransformer : IOpenApiOperationTransformer
{
    /// <summary>Yol soneki -> örnek govde.</summary>
    /// <remarks>
    /// Yola göre esliyorum, tipe göre değil.
    ///
    /// Tipe göre eslesydim aynı DTO'yu kullanan iki uc aynı ornegi
    /// alırdı -- oysa örneğin anlami baglama göre değişiyor
    /// (kayıt sirasindaki şifre ile şifre degistirmedeki şifre aynı
    /// tip ama farklı hikaye).
    /// </remarks>
    private static readonly (string Yol, string Metot, OpenApiObject Ornek)[] Ornekler =
    [
        ("auth/register", "POST", new OpenApiObject
        {
            ["email"] = new OpenApiString("adem@ornek.com"),
            ["password"] = new OpenApiString("Sifre123!"),
            ["firstName"] = new OpenApiString("Adem"),
            ["lastName"] = new OpenApiString("Ucar"),
            ["phoneNumber"] = new OpenApiString("+90 555 000 0000"),
        }),

        ("auth/login", "POST", new OpenApiObject
        {
            ["email"] = new OpenApiString("adem@ornek.com"),
            ["password"] = new OpenApiString("Sifre123!"),
        }),

        ("auth/refresh-token", "POST", new OpenApiObject
        {
            ["refreshToken"] = new OpenApiString("hK9v2mQ...  (girişte donen deger)"),
        }),

        ("reservations", "POST", new OpenApiObject
        {
            ["eventSessionId"] = new OpenApiString("01a0436e-7300-7bd1-a4bd-31dd7f662f8d"),

            // Coklu koltuk ornegi BILINCLI: tekil bir dizi gosterseydik
            // istemci gelistiricisi tek koltuk varsayabilir ve
            // arayuzunu ona göre kurgulardi.
            ["eventSeatIds"] = new OpenApiArray
            {
                new OpenApiString("01a0436e-75a3-7068-8b48-a4eb6eacb02a"),
                new OpenApiString("01a0436e-75a3-73b0-949a-da2655fa86f4"),
            },
        }),

        ("payments", "POST", new OpenApiObject
        {
            ["reservationId"] = new OpenApiString("01a048cc-d967-7a88-89ae-b3f62d94f492"),
        }),

        ("/refund", "POST", new OpenApiObject
        {
            // null = TAM iade. Bunu ornekte göstermek önemli: alanin
            // opsiyonel olduğunu semadan gormek mumkun ama "boş
            // birakirsam ne olur?" sorusunun cevabi ancak burada.
            ["amount"] = new OpenApiNull(),
            ["reason"] = new OpenApiString("Müşteri talebi"),
        }),

        // ---- Kimlik islemleri ----

        ("auth/change-password", "POST", new OpenApiObject
        {
            ["currentPassword"] = new OpenApiString("Sifre123!"),
            ["newPassword"] = new OpenApiString("YeniSifre456!"),
        }),

        ("auth/forgot-password", "POST", new OpenApiObject
        {
            ["email"] = new OpenApiString("adem@ornek.com"),
        }),

        ("auth/reset-password", "POST", new OpenApiObject
        {
            ["token"] = new OpenApiString("e-postayla gonderilen sifirlama kodu"),
            ["newPassword"] = new OpenApiString("YeniSifre456!"),
        }),

        // ---- Organizatör islemleri ----

        ("events", "POST", new OpenApiObject
        {
            ["title"] = new OpenApiString("Yaz Konseri 2026"),
            ["description"] = new OpenApiString(
                "Acik hava sahnesinde bir yaz aksami."),
            ["categoryId"] = new OpenApiString("01a041fa-9df5-733a-bf1b-50a07426cb8e"),
            ["cityId"] = new OpenApiString("01a041fa-9e18-7cea-9a7d-0a841f320100"),
            ["venueId"] = new OpenApiString("01a0436e-6f01-7c2d-9d3a-1f4e8a2b6c70"),
            ["hallId"] = new OpenApiString("01a0436e-6f4d-71b8-8e55-3c9d7f1a2e84"),
            ["eventDate"] = new OpenApiString("2026-07-15T20:00:00Z"),

            // Satış baslangici ETKINLIKTEN önce olmalı; ornekte de
            // oyle. Tersini gosterseydik örnek dogrulamadan gecmezdi.
            ["salesStartDate"] = new OpenApiString("2026-05-01T09:00:00Z"),
            ["salesEndDate"] = new OpenApiString("2026-07-15T19:00:00Z"),
            ["durationMinutes"] = new OpenApiInteger(120),
            ["maxTicketsPerUser"] = new OpenApiInteger(4),
            ["minimumAge"] = new OpenApiInteger(0),
        }),

        ("ticket-types", "POST", new OpenApiObject
        {
            ["name"] = new OpenApiString("Tam"),
            ["price"] = new OpenApiDouble(450),
            ["currency"] = new OpenApiString("TRY"),

            // null = SINIRSIZ kota. Semadan "opsiyonel" olduğu
            // gorulur ama "boş birakirsam ne olur?" sorusunun
            // cevabi ancak ornekte.
            ["quota"] = new OpenApiNull(),
            ["requiresStudentVerification"] = new OpenApiBoolean(false),
        }),

        ("venues", "POST", new OpenApiObject
        {
            ["cityId"] = new OpenApiString("01a041fa-9e18-7cea-9a7d-0a841f320100"),
            ["name"] = new OpenApiString("Acik Hava Sahnesi"),
            ["address"] = new OpenApiString("Harbiye, Sisli / Istanbul"),
        }),

        ("/halls", "POST", new OpenApiObject
        {
            ["name"] = new OpenApiString("Ana Salon"),
            ["capacity"] = new OpenApiInteger(500),
        }),

        ("reviews", "POST", new OpenApiObject
        {
            ["eventId"] = new OpenApiString("01a0436e-7065-757e-8d38-ada797b90295"),
            ["rating"] = new OpenApiInteger(5),
            ["comment"] = new OpenApiString("Harika bir konserdi, ses düzeni çok iyiydi."),
        }),
    ];

    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        if (operation.RequestBody?.Content is null)
        {
            return Task.CompletedTask;
        }

        var yol = context.Description.RelativePath ?? string.Empty;
        var metot = context.Description.HttpMethod ?? string.Empty;

        foreach (var (sonek, beklenenMetot, ornek) in Ornekler)
        {
            var eslesti = metot.Equals(beklenenMetot, StringComparison.OrdinalIgnoreCase)
                && (yol.EndsWith(sonek, StringComparison.OrdinalIgnoreCase)
                    || yol.Contains(sonek, StringComparison.OrdinalIgnoreCase));

            if (!eslesti)
            {
                continue;
            }

            foreach (var icerik in operation.RequestBody.Content.Values)
            {
                // Zaten örnek varsa dokunmuyoruz: elle yazilmis bir
                // örnek buradaki genel ornekten daha degerlidir.
                icerik.Example ??= ornek;
            }

            break;
        }

        return Task.CompletedTask;
    }
}
