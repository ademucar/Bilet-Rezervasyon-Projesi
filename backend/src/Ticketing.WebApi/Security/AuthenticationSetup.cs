using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Ticketing.Domain.Entities;

namespace Ticketing.WebApi.Security;

/// <summary>
/// JWT doğrulama ve yetkilendirme politikalari.
/// PDF Sprint 3: "Role based / Policy based / Resource based authorization".
/// </summary>
internal static class AuthenticationSetup
{
    /// <summary>Policy adları. Metin yerine bu sabitler kullanilir.</summary>
    public static class Policies
    {
        public const string AdminOnly = "AdminOnly";
        public const string OrganizerOnly = "OrganizerOnly";
        public const string EventOwner = "EventOwner";
        public const string TicketOwner = "TicketOwner";
        public const string ReservationOwner = "ReservationOwner";
    }

    public static IServiceCollection AddJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var secret = configuration["Jwt:Secret"]
            ?? throw new InvalidOperationException(
                "Jwt:Secret yapilandirilmamis. Jwt__Secret environment degiskenini kontrol edin.");

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Token dogrulama parametreleri
                // Her biri KAPATILDIGINDA ne olacagini yazdim.
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    // Imza geçerli mi? Kapatilirsa HERKES kendi token'ini
                    // uretip Admin rolü yazabilir. Asla kapatılmaz.
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),

                    // Token'i BIZ mi urettik? Kapatilirsa, aynı imzalama
                    // anahtarini kullanan BASKA bir sistemin token'i da
                    // kabul edilir.
                    ValidateIssuer = true,
                    ValidIssuer = configuration["Jwt:Issuer"],

                    // Token BENIM için mi üretildi? Ornegin bir mobil
                    // uygulama için üretilmiş token'in web API'de
                    // kullanilmasini engeller.
                    ValidateAudience = true,
                    ValidAudience = configuration["Jwt:Audience"],

                    // Süresi dolmuş token reddedilsin. Kapatilirsa
                    // access token'in kisa omurlu olmasinin hiçbir
                    // anlami kalmaz.
                    ValidateLifetime = true,

                    // ClockSkew = zero -- varsayilani degistiriyorum
                    //
                    // Varsayılan deger bes dakikadir. Yani 15 dakikalik bir
                    // token aslında 20 dakika geçerli olur.
                    //
                    // Bu tolerans, sunucu saatleri arasindaki farki telafi
                    // etmek için var. Ama biz tüm zamanlari UTC tutuyorum
                    // ve container'lar ana makine saatini paylasiyor --
                    // sapma yok.
                    //
                    // Sifira cekmeyi tercih ediyorum çünkü "15 dakika"
                    // dedigimde gerçekten 15 dakika olmalı. Aksi halde
                    // güvenlik hesaplarim yanlış olur ve testlerde
                    // "süresi dolmuş token hâlâ çalışıyor" gibi kafa
                    // karistirici durumlarla ugrasırız.
                    ClockSkew = TimeSpan.Zero,

                    // "sub" claim'ini olduğu gibi birak.
                    //
                    // Asp.net Core varsayılan olarak "sub"u
                    // ClaimTypes.NameIdentifier'a (uzun bir XML URI'sine)
                    // esler. Bu esleme, token'a ne yazdiginizla kodda ne
                    // okudugunuzun tutmamasina yol acan klasik bir
                    // tuzaktir. Kapatarak standart JWT adlarini koruyorum.
                    NameClaimType = JwtRegisteredClaimNames.Sub,
                    RoleClaimType = System.Security.Claims.ClaimTypes.Role
                };

                // Token gecersizse yanit header'ina sebebini ekle.
                // Frontend "token süresi doldu" ile "token geçersiz"
                // durumlarini ayırt edip birincisinde sessizce yenileme
                // yapabiliyor.
                options.Events = new JwtBearerEvents
                {
                    OnAuthenticationFailed = context =>
                    {
                        if (context.Exception is SecurityTokenExpiredException)
                        {
                            context.Response.Headers.Append("X-Token-Expired", "true");
                        }

                        return Task.CompletedTask;
                    }
                };
            });

        return services;
    }

    public static IServiceCollection AddAuthorizationPolicies(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()

            // ---- Rol bazlı politikalar ----
            .AddPolicy(Policies.AdminOnly, policy =>
                policy.RequireRole(Role.Names.Admin))

            // Admin, organizatorun yapabildigi her seyi yapabilmeli.
            // RequireRole birden fazla rol aldiginda VEYA mantığı uygular:
            // "Organizer VEYA Admin".
            //
            // Bunu yazmasaydim admin, organizatör endpoint'lerine
            // erisemezdi ve destek islerini yapamazdi.
            .AddPolicy(Policies.OrganizerOnly, policy =>
                policy.RequireRole(Role.Names.Organizer, Role.Names.Admin))

            // ---- Kaynak bazlı politikalar ----
            //
            // PDF Sprint 3: "Resource based authorization uygulanmalıdır."
            //
            // EventOwner artık GERCEK sahiplik kontrolü yapiyor:
            // EventOwnerAuthorizationHandler veritabanina bakip
            // "bu etkinlik bu kullanıcının organizatör profiline mi ait?"
            // sorusunu cevapliyor. Admin her zaman gecer.
            //
            // Rol bazlı kontrol bunu YAPAMAZ: token yalnızca "bu kişi
            // organizatör" der, "bu etkinlik onun" demez. O kontrol
            // olmasaydı her organizatör digerlerinin etkinliklerini
            // duzenleyebilirdi.
            .AddPolicy(Policies.EventOwner, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.AddRequirements(new EventOwnerRequirement());
            })
            // TicketOwner / ReservationOwner -- sprint 19'da tamamlandi
            //
            // Sprint 3'te iskelet olarak birakilmislardi: yalnızca
            // RequireAuthenticatedUser() yapiyorlardi ve koddaki not
            // "gerçek kontrolleri Sprint 7-8'de yazacagim" diyordu.
            // Yazilmamislar.
            //
            // Sprint 19 denetiminde olctum: sistem açık degildi --
            // handler'lar sahiplik kontrolunu zaten yapiyor ve
            // baskasinin rezervasyonuna erişim 404 dönüyor.
            //
            // Yine de tamamlandı, çünkü politika YANILTICIYDI:
            // [Authorize(Policy = TicketOwner)] yazan biri kontrolun
            // politikada olduğunu sanirdi. Simdi iki bağımsız katman
            // var; birinin unutulmasi digerini geçersiz kilmiyor.
            .AddPolicy(Policies.TicketOwner, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.AddRequirements(new TicketOwnerRequirement());
            })
            .AddPolicy(Policies.ReservationOwner, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.AddRequirements(new ReservationOwnerRequirement());
            });

        // Handler'i kaydediyorum. Bu satır olmasaydı policy sessizce
        // BASARISIZ olurdu -- requirement var ama önü degerlendirecek
        // kimse yok. Herkes 403 alırdı ve sebebi çok geç anlasilirdi.
        services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationHandler,
                              EventOwnerAuthorizationHandler>();

        services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationHandler,
                              TicketOwnerAuthorizationHandler>();

        services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationHandler,
                              ReservationOwnerAuthorizationHandler>();

        return services;
    }
}
