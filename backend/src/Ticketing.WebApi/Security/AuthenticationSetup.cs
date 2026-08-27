using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Ticketing.Domain.Entities;

namespace Ticketing.WebApi.Security;

/// <summary>
/// JWT dogrulama ve yetkilendirme politikalari.
/// PDF Sprint 3: "Role based / Policy based / Resource based authorization".
/// </summary>
internal static class AuthenticationSetup
{
    /// <summary>Policy adlari. Metin yerine bu sabitler kullanilir.</summary>
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
                // ==============================================================
                // TOKEN DOGRULAMA PARAMETRELERI
                // Her biri KAPATILDIGINDA ne olacagini yazdim.
                // ==============================================================
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    // Imza gecerli mi? Kapatilirsa HERKES kendi token'ini
                    // uretip Admin rolu yazabilir. Asla kapatilmaz.
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),

                    // Token'i BIZ mi urettik? Kapatilirsa, ayni imzalama
                    // anahtarini kullanan BASKA bir sistemin token'i da
                    // kabul edilir.
                    ValidateIssuer = true,
                    ValidIssuer = configuration["Jwt:Issuer"],

                    // Token BIZIM icin mi uretildi? Ornegin bir mobil
                    // uygulama icin uretilmis token'in web API'de
                    // kullanilmasini engeller.
                    ValidateAudience = true,
                    ValidAudience = configuration["Jwt:Audience"],

                    // Suresi dolmus token reddedilsin. Kapatilirsa
                    // access token'in kisa omurlu olmasinin hicbir
                    // anlami kalmaz.
                    ValidateLifetime = true,

                    // ==============================================================
                    // ClockSkew = ZERO -- VARSAYILANI DEGISTIRIYORUM
                    // ==============================================================
                    // Varsayilan deger BES DAKIKADIR. Yani 15 dakikalik bir
                    // token aslinda 20 dakika gecerli olur.
                    //
                    // Bu tolerans, sunucu saatleri arasindaki farki telafi
                    // etmek icin var. Ama biz tum zamanlari UTC tutuyoruz
                    // ve container'lar ana makine saatini paylasiyor --
                    // sapma yok.
                    //
                    // Sifira cekmeyi tercih ediyorum cunku "15 dakika"
                    // dedigimizde gercekten 15 dakika olmali. Aksi halde
                    // guvenlik hesaplarimiz yanlis olur ve testlerde
                    // "suresi dolmus token hala calisiyor" gibi kafa
                    // karistirici durumlarla ugrasırız.
                    ClockSkew = TimeSpan.Zero,

                    // "sub" claim'ini oldugu gibi birak.
                    //
                    // ASP.NET Core varsayilan olarak "sub"u
                    // ClaimTypes.NameIdentifier'a (uzun bir XML URI'sine)
                    // esler. Bu esleme, token'a ne yazdiginizla kodda ne
                    // okudugunuzun tutmamasina yol acan klasik bir
                    // tuzaktir. Kapatarak standart JWT adlarini koruyoruz.
                    NameClaimType = JwtRegisteredClaimNames.Sub,
                    RoleClaimType = System.Security.Claims.ClaimTypes.Role
                };

                // Token gecersizse yanit header'ina sebebini ekle.
                // Frontend "token suresi doldu" ile "token gecersiz"
                // durumlarini ayirt edip birincisinde sessizce yenileme
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

            // ---- Rol bazli politikalar ----
            .AddPolicy(Policies.AdminOnly, policy =>
                policy.RequireRole(Role.Names.Admin))

            // Admin, organizatorun yapabildigi her seyi yapabilmeli.
            // RequireRole birden fazla rol aldiginda VEYA mantigi uygular:
            // "Organizer VEYA Admin".
            //
            // Bunu yazmasaydik admin, organizator endpoint'lerine
            // erisemezdi ve destek islerini yapamazdi.
            .AddPolicy(Policies.OrganizerOnly, policy =>
                policy.RequireRole(Role.Names.Organizer, Role.Names.Admin))

            // ---- Kaynak bazli politikalar ----
            //
            // PDF Sprint 3: "Resource based authorization uygulanmalidir."
            //
            // EventOwner artik GERCEK sahiplik kontrolu yapiyor:
            // EventOwnerAuthorizationHandler veritabanina bakip
            // "bu etkinlik bu kullanicinin organizator profiline mi ait?"
            // sorusunu cevapliyor. Admin her zaman gecer.
            //
            // Rol bazli kontrol bunu YAPAMAZ: token yalnizca "bu kisi
            // organizator" der, "bu etkinlik onun" demez. O kontrol
            // olmasaydi her organizator digerlerinin etkinliklerini
            // duzenleyebilirdi.
            .AddPolicy(Policies.EventOwner, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.AddRequirements(new EventOwnerRequirement());
            })
            // TicketOwner ve ReservationOwner henuz yalnizca "giris yapmis
            // ol" istiyor. Sebep: bilet ve rezervasyon Sprint 7-8'de
            // olusacak; gercek sahiplik kontrollerini o sprintlerde
            // EventOwner ile ayni kalibi kullanarak yazacagiz.
            //
            // Iskeleti simdiden birakiyorum ki controller'lar dogru
            // policy adini bugunden kullansin ve o gun yalnizca
            // requirement eklemek yeterli olsun.
            .AddPolicy(Policies.TicketOwner, policy => policy.RequireAuthenticatedUser())
            .AddPolicy(Policies.ReservationOwner, policy => policy.RequireAuthenticatedUser());

        // Handler'i kaydediyorum. Bu satir olmasaydi policy sessizce
        // BASARISIZ olurdu -- requirement var ama onu degerlendirecek
        // kimse yok. Herkes 403 alirdi ve sebebi cok gec anlasilirdi.
        services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationHandler,
                              EventOwnerAuthorizationHandler>();

        return services;
    }
}
