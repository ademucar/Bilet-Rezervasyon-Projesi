using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;

namespace Ticketing.WebApi.Documentation;

/// <summary>
/// OpenAPI belgesi yapilandirmasi. PDF Sprint 18.
/// </summary>
/// <remarks>
/// Pdf'in ON maddesi ve nerede karsilandigi
///
///   1. Endpoint aciklamalari   -> XmlDocumentationTransformer
///   2. Request ornekleri       -> RequestExampleTransformer
///   3. Response ornekleri      -> XmlDocumentationTransformer
///                                 (response code etiketleri)
///   4. Validation hatalari     -> ProblemDetailsTransformer
///   5. Authentication          -> SecuritySchemeTransformer
///   6. Yetkili roller          -> AuthorizationTransformer
///   7. Pagination              -> DocumentInfoTransformer (açıklama)
///   8. Problem Details         -> ProblemDetailsTransformer
///   9. Idempotency-Key         -> IdempotencyHeaderTransformer
///  10. API version bilgisi     -> DocumentInfoTransformer
///
/// NEDEN TRANSFORMER? Neden her uca oznitelik yazmiyorum?
///
/// Yazabilirdim ama 60'tan fazla ucum var. Her birine
/// [ProducesResponseType(401)] eklemek:
///   - Yuzlerce satır tekrar
///   - Birini unutunca belgeyle gerçek arasında sessiz bir fark
///
/// Transformer, kuralı TEK YERDEN uyguluyor: "kimlik dogrulamasi
/// gerektiren her uca 401 ekle" gibi. Yeni bir uc eklendiginde
/// hiçbir sey yapmak gerekmiyor.
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

            // SIRA ONEMLI: XML önce çalışıyor ki Description'i
            // olustursun; AuthorizationTransformer sonra yetki notunu
            // onun sonuna EKLIYOR. Ters sırada olsaydı XML açıklaması
            // yetki notunun uzerine yazilirdi.
            options.AddOperationTransformer<XmlDocumentationTransformer>();
            options.AddOperationTransformer<AuthorizationTransformer>();
            options.AddOperationTransformer<ProblemDetailsTransformer>();
            options.AddOperationTransformer<IdempotencyHeaderTransformer>();
            options.AddOperationTransformer<RequestExampleTransformer>();

            // Enum semalarina deger + isim ekliyor. Orval arastirmasi
            // sırasında belgede enum isimlerinin HİÇ olmadigini
            // buldum (bkz. EnumSchemaTransformer).
            options.AddSchemaTransformer<EnumSchemaTransformer>();
        });

        return services;
    }
}

/// <summary>
/// Belgenin başlığı, açıklaması ve surum bilgisi. PDF: "API version bilgisi".
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

            // Aciklama, arayuzun ilk ekrani
            //
            // Bir API'yi ilk kez kullanan kisinin cevabini aradigi
            // sorular burada: nasil kimlik dogrularim, hatalar hangi
            // bicimde gelir, sayfalama nasil çalışır.
            //
            // Bu bilgileri ayrı bir README'ye koysaydım kimse
            // bulamazdi -- Swagger'i acan kişi zaten "deneyerek
            // ogrenmek" istiyor.
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
/// JWT Bearer güvenlik semasi. PDF: "Authentication gereksinimleri".
/// </summary>
internal sealed class SecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    /// <summary>Güvenlik semasinin belge icindeki adı.</summary>
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
                "token'i yapistirin -- 'Bearer ' onekini EKLEMEYIN, arayüz " +
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
/// Bu bilgi koddan okunuyor, elle yazilmiyor
///
/// [Authorize] ozniteliklerini yansima (reflection) ile okuyup
/// belgeye aktariyoruz.
///
/// Elle yazsaydım: bir ucun yetkisi degistiginde belgeyi guncellemeyi
/// unuturdum ve Swagger "herkese açık" derken uc 403 donerdi.
/// Yanlis dokumantasyon, hiç dokumantasyon olmamasindan kotudur --
/// çünkü ona GUVENILIYOR.
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

        // Bu uc token istiyor: güvenlik gereksinimini işaretle.
        // Sema referansı ayrı bir degiskene aliniyor.
        //
        // Ic ice sozluk baslaticisi olarak yazdigimda StyleCop
        // SA1500 verdi ("çok satirli blogun parantezleri aynı
        // satiri paylasmamali") -- ve hakliydi: "}] = []," satiri
        // uc farklı seyi aynı yere sikistiriyordu.
        var sema = new OpenApiSecurityScheme
        {
            Reference = new OpenApiReference
            {
                Type = ReferenceType.SecurityScheme,
                Id = SecuritySchemeTransformer.SchemeName,
            },
        };

        var gereksinim = new OpenApiSecurityRequirement
        {
            [sema] = [],
        };

        operation.Security = [gereksinim];

        // Rol / politika bilgisi aciklamaya ekleniyor
        //
        // Politika adları ("AdminOnly", "EventOwner") tek başına
        // anlasilir değil. Kisa bir açıklama ekliyorum ki Swagger'i
        // okuyan kişi 403 alınca sasirmasin.
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

        // 429: HER UCA -- hiz sınırı genel limitleyiciyle tumune uygulaniyor
        //
        // Sprint 15'te politikasi olmayan uclar için de genel bir sinir
        // koymustuk ("varsayılan olarak güvenli"). Yani 429 her uctan
        // gelebilir ve istemci buna hazır olmalı.
        Ekle(operation, "429", "Çok fazla istek. Retry-After basligina bakin.");

        Ekle(operation, "500", "Beklenmeyen sunucu hatası.");

        if (korumali)
        {
            Ekle(operation, "401", "Token yok, geçersiz veya süresi dolmuş.");
            Ekle(operation, "403", "Token geçerli ama bu işlem için yetkiniz yok.");
        }

        // 400: Yalnizca govde veya parametre alan uclara
        //
        // Parametresiz bir GET ucunda doğrulama hatası olusamaz.
        // Kosulsuz ekleseydik belge, olmayan bir davranisi vaat
        // ederdi.
        if (context.Description.ParameterDescriptions.Count > 0)
        {
            Ekle(operation, "400",
                "Dogrulama hatası. `errors` alaninda hangi alanin neden " +
                "reddedildigi yazar.");
        }

        return Task.CompletedTask;
    }

    /// <summary>Yanit zaten tanimliysa USTUNE YAZMIYOR.</summary>
    /// <remarks>
    /// Controller'da [ProducesResponseType] ile acikca yazilmis bir
    /// yanit, buradaki genel aciklamadan daha degerli: o uca ozgu.
    ///
    /// Ustune yazsaydım, ozenle yazilmis aciklamalar genel
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
            ["title"] = new OpenApiString("Doğrulama hatası"),
            ["status"] = new OpenApiInteger(400),
            ["detail"] = new OpenApiString("Gonderilen veriler geçerli değil."),
            ["errorCode"] = new OpenApiString("validation.failed"),
            ["errors"] = new OpenApiObject
            {
                ["Password"] = new OpenApiArray
                {
                    new OpenApiString("Şifre en az 8 karakter olmalıdır."),
                    new OpenApiString("Şifre en az bir rakam içermelidir."),
                },
            },
            ["correlationId"] = new OpenApiString("01a048ce0ea078e7a6420ec159235062"),
        },

        "401" => new OpenApiObject
        {
            ["title"] = new OpenApiString("Unauthorized"),
            ["status"] = new OpenApiInteger(401),
            ["detail"] = new OpenApiString("Giriş yapmalisiniz."),
            ["errorCode"] = new OpenApiString("auth.required"),
        },

        "403" => new OpenApiObject
        {
            ["title"] = new OpenApiString("Forbidden"),
            ["status"] = new OpenApiInteger(403),
            ["detail"] = new OpenApiString("Bu işlem için yetkiniz yok."),
            ["errorCode"] = new OpenApiString("auth.forbidden"),
        },

        "429" => new OpenApiObject
        {
            ["title"] = new OpenApiString("Çok fazla istek"),
            ["status"] = new OpenApiInteger(429),
            ["detail"] = new OpenApiString(
                "Çok sik istek gonderdiniz. Lütfen biraz bekleyip tekrar deneyin."),
            ["errorCode"] = new OpenApiString("rate_limit.exceeded"),
        },

        _ => new OpenApiObject
        {
            ["title"] = new OpenApiString("Sunucu hatası"),
            ["status"] = new OpenApiInteger(500),
            ["detail"] = new OpenApiString(
                "Beklenmeyen bir hata oluştu. Lütfen daha sonra tekrar deneyin."),
            ["errorCode"] = new OpenApiString("server.unexpected"),
            ["correlationId"] = new OpenApiString("01a048ce0ea078e7a6420ec159235062"),
        },
    };
}

/// <summary>
/// Idempotency-Key basligini destekleyen uclara belgeler.
/// PDF: "Idempotency-Key açıklaması".
/// </summary>
/// <remarks>
/// Hangi uclara ekleniyor ve neden yalnizca onlara?
///
/// Rezervasyon oluşturma, ödeme baslatma ve iade. Ucu de:
///   - Yeni bir kayıt URETIYOR
///   - Tekrari MALI veya operasyonel sonuç doguruyor
///
/// Her uca eklemek yanıltıcı olurdu: bir GET için Idempotency-Key
/// göstermek, o basligin bir etkisi varmis gibi dusundurur.
/// Belgede olmayan bir davranisi vaat etmemek, eksik belgelemekten
/// daha önemli.
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
                "Ag kopmasi sonrası isteği AYNI anahtarla tekrar " +
                "gonderirseniz yeni bir kayit olusmaz; ilk islemin " +
                "sonucu doner.\n\n" +
                "Her MANTIKSAL işlem için yeni bir deger üretin " +
                "(örneğin bir GUID). Tekrar denemede AYNI değeri " +
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
