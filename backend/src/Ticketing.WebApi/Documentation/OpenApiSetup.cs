using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;

namespace Ticketing.WebApi.Documentation;

/// <summary>
/// OpenAPI belgesi yapilandirmasi. PDF Sprint 18.
/// </summary>
/// <remarks>
/// ==================================================================
/// PDF'IN ON MADDESI VE NEREDE KARSILANDIGI
/// ==================================================================
///   1. Endpoint aciklamalari   -> XmlDocumentationTransformer
///   2. Request ornekleri       -> RequestExampleTransformer
///   3. Response ornekleri      -> XmlDocumentationTransformer
///                                 (response code etiketleri)
///   4. Validation hatalari     -> ProblemDetailsTransformer
///   5. Authentication          -> SecuritySchemeTransformer
///   6. Yetkili roller          -> AuthorizationTransformer
///   7. Pagination              -> DocumentInfoTransformer (aciklama)
///   8. Problem Details         -> ProblemDetailsTransformer
///   9. Idempotency-Key         -> IdempotencyHeaderTransformer
///  10. API version bilgisi     -> DocumentInfoTransformer
///
/// ------------------------------------------------------------------
/// NEDEN TRANSFORMER? Neden her uca oznitelik yazmiyoruz?
/// ------------------------------------------------------------------
/// Yazabilirdik ama 60'tan fazla ucumuz var. Her birine
/// [ProducesResponseType(401)] eklemek:
///   - Yuzlerce satir tekrar
///   - Birini unutunca belgeyle gercek arasinda sessiz bir fark
///
/// Transformer, kurali TEK YERDEN uyguluyor: "kimlik dogrulamasi
/// gerektiren her uca 401 ekle" gibi. Yeni bir uc eklendiginde
/// hicbir sey yapmak gerekmiyor.
/// ==================================================================
/// </remarks>
internal static class OpenApiSetup
{
    public static IServiceCollection AddApiDocumentation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOpenApi("v1", options =>
        {
            options.AddDocumentTransformer<DocumentInfoTransformer>();
            options.AddDocumentTransformer<SecuritySchemeTransformer>();

            // SIRA ONEMLI: XML once calisiyor ki Description'i
            // olustursun; AuthorizationTransformer sonra yetki notunu
            // onun sonuna EKLIYOR. Ters sirada olsaydi XML aciklamasi
            // yetki notunun uzerine yazilirdi.
            options.AddOperationTransformer<XmlDocumentationTransformer>();
            options.AddOperationTransformer<AuthorizationTransformer>();
            options.AddOperationTransformer<ProblemDetailsTransformer>();
            options.AddOperationTransformer<IdempotencyHeaderTransformer>();
            options.AddOperationTransformer<RequestExampleTransformer>();

            // Enum semalarina deger + isim ekliyor. Orval arastirmasi
            // sirasinda belgede enum isimlerinin HIC olmadigini
            // buldum (bkz. EnumSchemaTransformer).
            options.AddSchemaTransformer<EnumSchemaTransformer>();
        });

        return services;
    }
}

/// <summary>
/// Belgenin basligi, aciklamasi ve surum bilgisi. PDF: "API version bilgisi".
/// </summary>
internal sealed class DocumentInfoTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        document.Info = new OpenApiInfo
        {
            Title = "Biletim API",
            Version = "v1",

            // ==========================================================
            // ACIKLAMA, ARAYUZUN ILK EKRANI
            // ==========================================================
            // Bir API'yi ilk kez kullanan kisinin cevabini aradigi
            // sorular burada: nasil kimlik dogrularim, hatalar hangi
            // bicimde gelir, sayfalama nasil calisir.
            //
            // Bu bilgileri ayri bir README'ye koysaydik kimse
            // bulamazdi -- Swagger'i acan kisi zaten "deneyerek
            // ogrenmek" istiyor.
            // ==========================================================
            Description = """
                Etkinlik, biletleme ve koltuk rezervasyon sistemi.

                ## Surumleme

                Adresler `/api/v{surum}/...` bicimindedir; su an `v1`
                yayinda. Surum URL segmentinde tasiniyor:

                    GET /api/v1/events

                Neden URL segmenti? Tarayicidan denemesi kolay, onbellek
                anahtarlari dogal olarak ayrisir ve loglarda hangi surumun
                cagrildigi aciktir. Header tabanli surumleme bu ucunu de
                zorlastirirdi.

                Yanit basliklarinda `api-supported-versions` ile
                desteklenen surumler bildirilir.

                ## Kimlik dogrulama

                Korumali uclar JWT Bearer token istiyor:

                    Authorization: Bearer <access_token>

                Token'i `POST /api/v1/auth/login` veya
                `POST /api/v1/auth/register` ucundan aliyorsunuz.

                Access token kisa omurlu (15 dk). Suresi dolunca
                `POST /api/v1/auth/refresh-token` ile yenileyin --
                her yenilemede refresh token de DEGISIR (rotation) ve
                eskisi gecersiz olur.

                ## Sayfalama

                Liste dondiren uclar `pageNumber` (1'den baslar) ve
                `pageSize` sorgu parametrelerini alir.

                `pageSize` icin ust sinir uygulanir; asan degerler
                sessizce sinira cekilir. Boylece tek bir istekle tum
                tabloyu cekmek mumkun olmuyor.

                Yanit bicimi:

                    {
                      "items": [ ... ],
                      "pageNumber": 1,
                      "pageSize": 20,
                      "totalCount": 137,
                      "totalPages": 7,
                      "hasPreviousPage": false,
                      "hasNextPage": true
                    }

                ## Hata bicimi

                Tum hatalar RFC 7807 Problem Details bicimindedir.
                Ek olarak iki alan doneriz:

                - `errorCode`: makine tarafindan okunabilir hata kodu
                  (ornegin `reservation.seat_taken`). Mesaj metni
                  degisebilir; bu kod SABITTIR, istemci mantigini buna
                  baglayin.
                - `correlationId`: destek talebinde bu degeri verin,
                  ilgili istegin tum loglarini tek sorguyla buluruz.

                Dogrulama hatalarinda ayrica `errors` alani gelir:
                alan adi -> hata mesajlari.

                ## Hiz siniri

                Uclar hiz sinirina tabidir. Sinir asildiginda `429`
                doner ve `Retry-After` basliginda kac saniye
                beklemeniz gerektigi yazar.

                ## Idempotency

                Rezervasyon, odeme ve iade uclari `Idempotency-Key`
                basligini destekler. Ag kopmasi sonrasi ayni istegi
                ayni anahtarla tekrar gonderirseniz YENI bir kayit
                olusmaz, ilk sonuc doner.
                """,

            Contact = new OpenApiContact
            {
                Name = "Biletim",
                Url = new Uri("https://github.com/ademucar"),
            },
        };

        return Task.CompletedTask;
    }
}

/// <summary>
/// JWT Bearer guvenlik semasi. PDF: "Authentication gereksinimleri".
/// </summary>
internal sealed class SecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    /// <summary>Guvenlik semasinin belge icindeki adi.</summary>
    public const string SchemeName = "Bearer";

    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        document.Components ??= new OpenApiComponents();

        document.Components.SecuritySchemes[SchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description =
                "JWT access token. Swagger'da 'Authorize' dugmesine basip " +
                "token'i yapistirin -- 'Bearer ' onekini EKLEMEYIN, arayuz " +
                "onu kendisi ekliyor.",
        };

        return Task.CompletedTask;
    }
}

/// <summary>
/// Ucun kimlik/rol gereksinimlerini belgeye yazar.
/// PDF: "Authentication gereksinimleri", "Yetkili roller".
/// </summary>
/// <remarks>
/// ==================================================================
/// BU BILGI KODDAN OKUNUYOR, ELLE YAZILMIYOR
/// ==================================================================
/// [Authorize] ozniteliklerini yansima (reflection) ile okuyup
/// belgeye aktariyoruz.
///
/// Elle yazsaydik: bir ucun yetkisi degistiginde belgeyi guncellemeyi
/// unuturduk ve Swagger "herkese acik" derken uc 403 donerdi.
/// Yanlis dokumantasyon, hic dokumantasyon olmamasindan kotudur --
/// cunku ona GUVENILIYOR.
/// ==================================================================
/// </remarks>
internal sealed class AuthorizationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        var metadata = context.Description.ActionDescriptor.EndpointMetadata;

        var anonim = metadata.OfType<Microsoft.AspNetCore.Authorization.IAllowAnonymous>().Any();

        var yetkiler = metadata
            .OfType<Microsoft.AspNetCore.Authorization.IAuthorizeData>()
            .ToList();

        if (anonim || yetkiler.Count == 0)
        {
            return Task.CompletedTask;
        }

        // Bu uc token istiyor: guvenlik gereksinimini isaretle.
        operation.Security =
        [
            new OpenApiSecurityRequirement
            {
                [new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = SecuritySchemeTransformer.SchemeName,
                    },
                }] = [],
            },
        ];

        // ==============================================================
        // ROL / POLITIKA BILGISI ACIKLAMAYA EKLENIYOR
        // ==============================================================
        // Politika adlari ("AdminOnly", "EventOwner") tek basina
        // anlasilir degil. Kisa bir aciklama ekliyorum ki Swagger'i
        // okuyan kisi 403 alinca sasirmasin.
        // ==============================================================
        var roller = yetkiler
            .Select(y => y.Policy ?? y.Roles)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct()
            .ToList();

        var not = roller.Count > 0
            ? $"\n\n**Yetki:** {string.Join(", ", roller)}"
            : "\n\n**Yetki:** giris yapmis herhangi bir kullanici";

        operation.Description = (operation.Description ?? string.Empty) + not;

        return Task.CompletedTask;
    }
}

/// <summary>
/// Ortak hata yanitlarini her uca ekler.
/// PDF: "Validation hatalari", "Problem Details hata modeli".
/// </summary>
internal sealed class ProblemDetailsTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        var anonim = metadata.OfType<Microsoft.AspNetCore.Authorization.IAllowAnonymous>().Any();
        var korumali = !anonim
            && metadata.OfType<Microsoft.AspNetCore.Authorization.IAuthorizeData>().Any();

        // ==============================================================
        // 429: HER UCA -- hiz siniri genel limitleyiciyle tumune uygulaniyor
        // ==============================================================
        // Sprint 15'te politikasi olmayan uclar icin de genel bir sinir
        // koymustuk ("varsayilan olarak guvenli"). Yani 429 her uctan
        // gelebilir ve istemci buna hazir olmali.
        // ==============================================================
        Ekle(operation, "429", "Cok fazla istek. Retry-After basligina bakin.");

        Ekle(operation, "500", "Beklenmeyen sunucu hatasi.");

        if (korumali)
        {
            Ekle(operation, "401", "Token yok, gecersiz veya suresi dolmus.");
            Ekle(operation, "403", "Token gecerli ama bu islem icin yetkiniz yok.");
        }

        // ==============================================================
        // 400: YALNIZCA GOVDE VEYA PARAMETRE ALAN UCLARA
        // ==============================================================
        // Parametresiz bir GET ucunda dogrulama hatasi olusamaz.
        // Kosulsuz ekleseydik belge, olmayan bir davranisi vaat
        // ederdi.
        // ==============================================================
        if (context.Description.ParameterDescriptions.Count > 0)
        {
            Ekle(operation, "400",
                "Dogrulama hatasi. `errors` alaninda hangi alanin neden " +
                "reddedildigi yazar.");
        }

        return Task.CompletedTask;
    }

    /// <summary>Yanit zaten tanimliysa USTUNE YAZMIYOR.</summary>
    /// <remarks>
    /// Controller'da [ProducesResponseType] ile acikca yazilmis bir
    /// yanit, buradaki genel aciklamadan daha degerli: o uca ozgu.
    ///
    /// Ustune yazsaydik, ozenle yazilmis aciklamalar genel
    /// metinlerle degistirilirdi.
    /// </remarks>
    private static void Ekle(OpenApiOperation operation, string kod, string aciklama)
    {
        operation.Responses ??= new OpenApiResponses();

        if (operation.Responses.ContainsKey(kod))
        {
            return;
        }

        operation.Responses[kod] = new OpenApiResponse
        {
            Description = aciklama,
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/problem+json"] = new()
                {
                    Example = ProblemOrnegi(kod),
                },
            },
        };
    }

    /// <summary>PDF: "Response ornekleri" -- hata tarafi.</summary>
    private static OpenApiObject ProblemOrnegi(string kod) => kod switch
    {
        "400" => new OpenApiObject
        {
            ["title"] = new OpenApiString("Dogrulama hatasi"),
            ["status"] = new OpenApiInteger(400),
            ["detail"] = new OpenApiString("Gonderilen veriler gecerli degil."),
            ["errorCode"] = new OpenApiString("validation.failed"),
            ["errors"] = new OpenApiObject
            {
                ["Password"] = new OpenApiArray
                {
                    new OpenApiString("Sifre en az 8 karakter olmalidir."),
                    new OpenApiString("Sifre en az bir rakam icermelidir."),
                },
            },
            ["correlationId"] = new OpenApiString("01a048ce0ea078e7a6420ec159235062"),
        },

        "401" => new OpenApiObject
        {
            ["title"] = new OpenApiString("Unauthorized"),
            ["status"] = new OpenApiInteger(401),
            ["detail"] = new OpenApiString("Giris yapmalisiniz."),
            ["errorCode"] = new OpenApiString("auth.required"),
        },

        "403" => new OpenApiObject
        {
            ["title"] = new OpenApiString("Forbidden"),
            ["status"] = new OpenApiInteger(403),
            ["detail"] = new OpenApiString("Bu islem icin yetkiniz yok."),
            ["errorCode"] = new OpenApiString("auth.forbidden"),
        },

        "429" => new OpenApiObject
        {
            ["title"] = new OpenApiString("Cok fazla istek"),
            ["status"] = new OpenApiInteger(429),
            ["detail"] = new OpenApiString(
                "Cok sik istek gonderdiniz. Lutfen biraz bekleyip tekrar deneyin."),
            ["errorCode"] = new OpenApiString("rate_limit.exceeded"),
        },

        _ => new OpenApiObject
        {
            ["title"] = new OpenApiString("Sunucu hatasi"),
            ["status"] = new OpenApiInteger(500),
            ["detail"] = new OpenApiString(
                "Beklenmeyen bir hata olustu. Lutfen daha sonra tekrar deneyin."),
            ["errorCode"] = new OpenApiString("server.unexpected"),
            ["correlationId"] = new OpenApiString("01a048ce0ea078e7a6420ec159235062"),
        },
    };
}

/// <summary>
/// Idempotency-Key basligini destekleyen uclara belgeler.
/// PDF: "Idempotency-Key aciklamasi".
/// </summary>
/// <remarks>
/// ==================================================================
/// HANGI UCLARA EKLENIYOR VE NEDEN YALNIZCA ONLARA?
/// ==================================================================
/// Rezervasyon olusturma, odeme baslatma ve iade. Ucu de:
///   - Yeni bir kayit URETIYOR
///   - Tekrari MALI veya operasyonel sonuc doguruyor
///
/// Her uca eklemek yaniltici olurdu: bir GET icin Idempotency-Key
/// gostermek, o basligin bir etkisi varmis gibi dusundurur.
/// Belgede olmayan bir davranisi vaat etmemek, eksik belgelemekten
/// daha onemli.
/// ==================================================================
/// </remarks>
internal sealed class IdempotencyHeaderTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        var yol = context.Description.RelativePath ?? string.Empty;
        var metot = context.Description.HttpMethod ?? string.Empty;

        var destekliyor =
            metot.Equals("POST", StringComparison.OrdinalIgnoreCase)
            && (yol.EndsWith("reservations", StringComparison.OrdinalIgnoreCase)
             || yol.EndsWith("payments", StringComparison.OrdinalIgnoreCase)
             || yol.EndsWith("/refund", StringComparison.OrdinalIgnoreCase));

        if (!destekliyor)
        {
            return Task.CompletedTask;
        }

        operation.Parameters ??= [];

        // Zaten tanimliysa (controller'da [FromHeader] ile) tekrar ekleme.
        if (operation.Parameters.Any(p =>
                string.Equals(p.Name, "Idempotency-Key", StringComparison.OrdinalIgnoreCase)))
        {
            return Task.CompletedTask;
        }

        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "Idempotency-Key",
            In = ParameterLocation.Header,
            Required = false,
            Description =
                "Ayni istegin tekrarlanmasini guvenli kilar.\n\n" +
                "Ag kopmasi sonrasi istegi AYNI anahtarla tekrar " +
                "gonderirseniz yeni bir kayit olusmaz; ilk islemin " +
                "sonucu doner.\n\n" +
                "Her MANTIKSAL islem icin yeni bir deger uretin " +
                "(ornegin bir GUID). Tekrar denemede AYNI degeri " +
                "kullanin -- degistirirseniz sistem bunu yeni bir " +
                "istek sayar.",
            Schema = new OpenApiSchema
            {
                Type = "string",
                MaxLength = 100,
                Example = new OpenApiString("9f2c8b14-3d5e-4a71-9c0f-2b8e6d41a7c3"),
            },
        });

        return Task.CompletedTask;
    }
}
