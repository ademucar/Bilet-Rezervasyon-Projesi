using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Serilog;
using Ticketing.WebApi.Observability;
using Microsoft.AspNetCore.HttpOverrides;
using Ticketing.Application.Abstractions.RealTime;
using Ticketing.WebApi.Hubs;
using Hangfire;
using Ticketing.Infrastructure.BackgroundJobs;
using Ticketing.Infrastructure.Caching;
using Asp.Versioning;
using Ticketing.Application;
using Ticketing.Application.Abstractions.Security;
using Ticketing.Application.Common.Options;
using Ticketing.Infrastructure;
using Ticketing.Persistence;
using Ticketing.WebApi.Middleware;
using Ticketing.WebApi.Security;

var builder = WebApplication.CreateBuilder(args);

// ===================================================================
// LOGLAMA -- PDF Sprint 16
// ===================================================================
// Serilog'u EN BASTA baglıyorum.
//
// Sebep: bundan sonraki her satir (servis kayitlari, yapilandirma
// okuma, veritabani baglantisi) log uretebiliyor. Sonra baglasaydik
// uygulamanin ACILIS asamasindaki loglar varsayilan saglayiciya
// giderdi ve dosyaya HIC yazilmazdi.
//
// Acilista olusan hatalar ise tam olarak en cok ihtiyac duyulan
// loglardir: uygulama ayaga kalkmadiginda elimizde baska hicbir sey
// olmuyor.
builder.AddSerilogLogging();

// ===================================================================
// SERVISLER
// ===================================================================

builder.Services.AddControllers();
builder.Services.AddOpenApi();

// ---- API Versioning ----
//
// PDF Sprint 18: "API versioning uygulanmalidir." ve
// "/api/v1/events" bicimi isteniyor.
//
// URL segmenti tabanli surumleme sectim (header veya query yerine):
//   - Tarayicidan ve Postman'den denemesi kolay
//   - Onbellek (cache) anahtarlari dogal olarak ayrisir
//   - Loglarda hangi surumun cagrildigi aciktir
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;

    // Yanit header'inda desteklenen surumleri bildir.
    // Istemciler yeni surum ciktigini bu sayede fark eder.
    options.ReportApiVersions = true;
    options.ApiVersionReader = new UrlSegmentApiVersionReader();
})
.AddApiExplorer(options =>
{
    options.GroupNameFormat = "'v'VVV";
    options.SubstituteApiVersionInUrl = true;
});

// ---- Katmanlar ----
//
// Her katman kendi kayitlarini yapiyor. Program.cs, o katmanlarin
// IC DETAYLARINI bilmiyor -- hangi handler var, hangi DbContext var
// gibi bilgiler burada gecmiyor.
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddPersistence(builder.Configuration);

// ---- Guvenlik ayarlari ----
builder.Services.AddOptions<SecurityOptions>()
       .Bind(builder.Configuration.GetSection(SecurityOptions.SectionName))
       .ValidateDataAnnotations()
       .ValidateOnStart();

builder.Services.AddOptions<ReservationOptions>()
       .Bind(builder.Configuration.GetSection(ReservationOptions.SectionName))
       .ValidateDataAnnotations()
       .ValidateOnStart();

// ICurrentUser HttpContext'e erisiyor; bu erisim icin gerekli.
builder.Services.AddHttpContextAccessor();

// Scoped: her HTTP istegi icin bir ornek. Singleton OLAMAZ cunku
// istege ozel veri (kullanici kimligi) tasiyor.
builder.Services.AddScoped<ICurrentUser, CurrentUser>();

builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddAuthorizationPolicies();

// ---- Problem Details ----
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Instance =
            $"{context.HttpContext.Request.Method} {context.HttpContext.Request.Path}";

        context.ProblemDetails.Extensions["traceId"] =
            System.Diagnostics.Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
    };
});

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
// ---- Saglik kontrolleri (PDF Sprint 16) ----
builder.Services.AddApplicationHealthChecks(builder.Configuration);

// ---- Izleme / tracing (PDF Sprint 16) ----
builder.Services.AddObservability(
    builder.Configuration,
    builder.Environment.EnvironmentName);

// ===================================================================
// API GUVENLIGI -- PDF Sprint 15
// ===================================================================

// ---- Istek hizi sinirlama ----
builder.Services.AddRateLimiting();

// ---- CORS ----
//
// ==================================================================
// GELISTIRMEDE CORS'A NEDEN IHTIYAC YOK AMA YINE DE TANIMLIYORUZ?
// ==================================================================
// Gelistirmede Vite proxy'si sayesinde istekler tarayici acisindan
// AYNI kaynaga (5173) gidiyor; CORS hic devreye girmiyor.
//
// Uretimde ise frontend ve API farkli alan adlarinda olabilir. O gun
// yapilandirma yapmak yerine SIMDIDEN kuruyorum -- ama izin verilen
// kaynaklari YAPILANDIRMADAN okuyorum, kodda sabitlemiyorum.
// ==================================================================
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? [];

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (allowedOrigins.Length == 0)
        {
            // Kaynak tanimlanmamissa HICBIR sey acmiyoruz.
            //
            // AllowAnyOrigin() yazmak cazip ama TEHLIKELI: herhangi
            // bir site tarayicidan API'mize istek atabilirdi.
            //
            // "Yapilandirma eksikse en guvenli davranis" ilkesi --
            // eksik ayar, acik kapi anlamina gelmemeli.
            policy.WithOrigins();

            return;
        }

        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()

              // AllowCredentials + AllowAnyOrigin BIRLIKTE KULLANILAMAZ
              // (tarayici reddeder). Kaynaklari acikca listeledigimiz
              // icin kimlik bilgisi tasiyabiliyoruz.
              .AllowCredentials()

              // Istemcinin okuyabilecegi ozel basliklar.
              // Varsayilan olarak yalnizca birkac standart baslik
              // gorunur; Retry-After ve correlation id'yi acikca
              // aciyoruz.
              .WithExposedHeaders("Retry-After", "X-Correlation-Id");
    });
});

// ---- Istek boyutu siniri ----
//
// ==================================================================
// PDF: "Request size limit"
// ==================================================================
// Varsayilan Kestrel siniri ~30 MB. Bizim en buyuk istegimiz bir
// JSON govdesi ve birkac kilobayt.
//
// Sinir olmasaydi saldirgan 30 MB'lik istekler gonderip bellegi ve
// bant genisligini tuketebilirdi (basit bir DoS).
//
// 1 MB: en buyuk mesru istegimizin (cok koltuklu rezervasyon)
// onlarca kati.
//
// NOT: Dosya yukleme ucu eklendiginde O UC ICIN ayri ve daha yuksek
// bir sinir gerekecek -- [RequestSizeLimit] ozniteligi ile uc bazinda
// verilebiliyor.
// ==================================================================
builder.Services.Configure<Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions>(options =>
{
    options.Limits.MaxRequestBodySize = 1 * 1024 * 1024;

    // ==============================================================
    // "Server: Kestrel" BASLIGINI KALDIR -- YAKALADIGIM HATA
    // ==============================================================
    // Once bunu SecurityHeadersMiddleware icinde
    // headers.Remove("Server") ile yapmaya calistim. CALISMADI.
    //
    // Sebep: Kestrel bu basligi OnStarting geri cagrimindan SONRA,
    // yaniti tel uzerine yazarken ekliyor. Middleware'in sildigi sey
    // henuz orada bile degildi.
    //
    // Basliklari gercekten kontrol ederek buldum:
    //   curl -D - -> "Server: Kestrel" hala goruluyordu.
    //
    // Dogru yer sunucunun kendi ayari. Sprint 13'teki BOM hatasiyla
    // ayni ders: kodun NIYETINI degil, URETTIGI CIKTIYI kontrol
    // etmek gerekiyor.
    //
    // Tek basina bir acik degil ama saldirgana bilgi veriyor:
    // hangi sunucu, hangi surum, hangi bilinen aciklar.
    // ==============================================================
    options.AddServerHeader = false;
});

// ---- Ters vekil sunucu basliklari ----
//
// ==================================================================
// BU YAPILANDIRMA OLMADAN HIZ SINIRI URETIMDE YANLIS CALISIR
// ==================================================================
// Uretimde uygulama bir ters vekil sunucu (nginx, load balancer)
// arkasinda calisiyor. O durumda RemoteIpAddress VEKILIN adresini
// gosterir -- gercek istemciyi degil.
//
// Sonuc: TUM istekler tek bir IP'den gelmis gibi gorunur ve hiz
// siniri butun kullanicilari BIRLIKTE etkiler. Bir kullanici siniri
// doldurunca herkes 429 alir.
//
// ForwardedHeaders, X-Forwarded-For basligini okuyup gercek istemci
// adresini geri koyuyor.
// ==================================================================
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

    // ==============================================================
    // KNOWN PROXIES TEMIZLENIYOR -- DIKKAT
    // ==============================================================
    // Varsayilan olarak yalnizca localhost'tan gelen X-Forwarded-For
    // basliklarina guveniliyor. Docker/Kubernetes'te vekil sunucu
    // farkli bir IP'de olur ve basliklar YOK SAYILIR.
    //
    // Listeleri bosaltmak "her vekile guven" demek. Bu, YALNIZCA
    // uygulama dogrudan internete acik DEGILSE guvenlidir: aksi
    // halde saldirgan X-Forwarded-For basligini uydurup hiz sinirini
    // atlatabilir.
    //
    // Uretim dagitiminda vekil sunucunun gercek adresi buraya
    // yazilmali. Bunu bir NOT olarak birakiyorum cunku degeri
    // ortama bagli ve yanlis yapilandirmasi sessizce guvenlik
    // acigi olusturuyor.
    // ==============================================================
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// ===================================================================
// ARKA PLAN ISLERI -- PDF Sprint 9
// ===================================================================
builder.Services.AddBackgroundJobs(builder.Configuration);

// ===================================================================
// REDIS ONBELLEK -- PDF Sprint 11
// ===================================================================
// Baglanti dizesi yoksa veya Redis kapaliysa uygulama YINE ACILIR;
// onbellek devre disi kalir ve sorgular veritabanindan karsilanir.
// PDF: "Cache kapali oldugunda sistem calismaya devam edebilmelidir."
builder.Services.AddCaching(builder.Configuration);

// ===================================================================
// GERCEK ZAMANLI KOLTUK GUNCELLEME -- PDF Sprint 10
// ===================================================================
builder.Services.AddSignalR(options =>
{
    // Gelistirmede ayrintili hata dondur.
    //
    // Uretimde KAPALI kalmali: istisna ayrintilari (yigin izi, tip
    // adlari, dosya yollari) istemciye gitmemeli. Bu, ic yapiyi
    // saldirgana anlatmak olurdu.
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
});

// Singleton: IHubContext zaten singleton ve bu sinif durum tutmuyor.
// Scoped yapsaydik her istekte gereksiz nesne uretilirdi.
builder.Services.AddSingleton<ISeatNotifier, SignalRSeatNotifier>();

var app = builder.Build();

// ===================================================================
// BASLANGIC VERISI -- YALNIZCA GELISTIRMEDE
// ===================================================================
// Uretimde ASLA otomatik seed calistirmiyoruz. Sebep: seed kodu
// yanlislikla veri uzerine yazabilir veya beklenmedik kayitlar
// olusturabilir. Uretimde veri, kontrollu migration'lar veya admin
// arayuzu uzerinden girilir.
//
// CreateScope kullaniyorum cunku DatabaseSeeder ve DbContext SCOPED
// kayitli; uygulama koku (root) singleton bir kapsam ve oradan scoped
// servis cozumlemek InvalidOperationException verir.
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var seeder = scope.ServiceProvider.GetRequiredService<Ticketing.Persistence.Seeding.DatabaseSeeder>();

    await seeder.SeedAsync().ConfigureAwait(false);
}

// ===================================================================
// HTTP PIPELINE -- SIRA ONEMLI
// ===================================================================

// 1) En basta: kendisinden sonraki her seyi sarmalar.
app.UseExceptionHandler();

// 2) Hata yanitina da correlation ID eklenebilsin diye hemen sonra.
app.UseMiddleware<CorrelationIdMiddleware>();

// 2b) Istek ozeti logu: correlation ID middleware'inden SONRA.
//
// Once olsaydi ozet satirinda CorrelationId alani BOS olurdu --
// cunku deger henuz uretilmemis olurdu. Bu, PDF'in "correlation ID
// application log icinde olmali" maddesini sessizce karsilanmamis
// birakirdi: kod var, alan bos.
app.UseRequestLogging();

// ===================================================================
// GUVENLIK KATMANLARI -- PDF Sprint 15
// ===================================================================

// 3) Ters vekil basliklari: hiz sinirindan ONCE olmali.
//
// Sonra olsaydi hiz siniri hala vekilin IP'sini gorurdu ve butun
// kullanicilari tek kotada toplardi.
app.UseForwardedHeaders();

// 4) Guvenlik basliklari: mumkun oldugunca ERKEN.
//
// Hata sayfalari ve statik dosyalar dahil TUM yanitlara eklensin
// istiyoruz.
app.UseMiddleware<SecurityHeadersMiddleware>();

// 5) CORS: kimlik dogrulamadan ONCE.
//
// Tarayicinin gonderdigi on kontrol (preflight OPTIONS) istegi
// kimlik bilgisi TASIMAZ. Kimlik dogrulamadan sonra olsaydi
// preflight 401 alir ve gercek istek hic gonderilmezdi.
app.UseCors();

// 6) Hiz siniri: kimlik dogrulamadan SONRA.
//
// Boylece giris yapmis kullanicilar icin kota KULLANICI bazli
// olabiliyor (bkz. ClientKey). Once olsaydi herkes IP bazli
// sayilirdi ve ayni agdaki kullanicilar birbirini engellerdi.
//
// Sira: Authentication -> RateLimiter -> Authorization

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
else
{
    app.UseHttpsRedirection();
}

// ==================================================================
// SIRA KRITIK: Authentication ONCE, Authorization SONRA
// ==================================================================
// UseAuthentication  -> "Sen kimsin?"   (token'i okur, User'i doldurur)
// UseAuthorization   -> "Yetkin var mi?" (User'a bakip karar verir)
//
// Ters yazsaydik Authorization henuz doldurulmamis bir User goreceginden
// giris yapmis kullanicilar bile 401 alirdi. Ve bu hata cok kafa
// karistiricidir: token dogru, kod dogru ama calismiyor.
// ==================================================================
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();
// ===================================================================
// SAGLIK UCLARI -- PDF Sprint 16
// ===================================================================
// Ucu de AllowAnonymous: yuk dengeleyici ve Kubernetes probe'lari
// token tasiyamaz. Bu yuzden yanitlarda hicbir hassas bilgi yok
// (baglanti dizesi, surum, ic hata mesaji donmuyor).

// 1) Insan icin: her seyin ayrintili ozeti.
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse,
});

// 2) "Trafik alabilir miyim?" -- TUM bagimliliklar kontrol ediliyor.
//
// Kubernetes readiness probe bunu cagirir. Basarisiz olursa
// kapsayici OLDURULMEZ, yalnizca yuk dengeleyiciden cikarilir.
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains(HealthChecksSetup.ReadyTag),
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse,
});

// 3) "Process ayakta mi?" -- HICBIR bagimlilik kontrol edilmiyor.
//
// ==================================================================
// Predicate = _ => false  SATIRI BU DOSYADAKI EN KRITIK SATIR
// ==================================================================
// Buraya veritabani kontrolu eklemek cok mantikli gorunur ve
// FELAKETLE sonuclanir:
//
//   PostgreSQL 30 saniye yanit vermez -> tum kapsayicilarin live
//   probe'u duser -> Kubernetes hepsini OLDURUR -> yeniden
//   baslarlar, veritabani hala yok -> yine olurler...
//
// Gecici bir veritabani sorunu, kalici bir uygulama cokusune
// donusur. Uygulama, kendi yeniden baslatmasiyla COZEMEYECEGI bir
// sey icin surekli yeniden baslatilir.
//
// Live probe yalnizca "bu process kilitlendi mi?" sorusunu
// cevaplamali. Cevabi bagimliliklara BAGLI OLMAMALI.
// ==================================================================
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
});

// ===================================================================
// KOLTUK HUB'I -- PDF Sprint 10
// ===================================================================
// Adres frontend'deki VITE proxy'siyle eslesiyor: /hubs/seats
app.MapHub<SeatHub>("/hubs/seats");

// ===================================================================
// HANGFIRE IZLEME EKRANI -- PDF Sprint 9
// ===================================================================
// UseAuthentication/UseAuthorization SONRASINA konuldu.
//
// Once konsaydi, filtre calistiginda HttpContext.User henuz
// doldurulmamis olurdu: admin olan kullanici bile panele
// giremezdi ve sebebi anlasilmazdi.
// ===================================================================
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = [new HangfireDashboardAuthorizationFilter()],

    // Panelden is SILME ve YENIDEN CALISTIRMA yetkisi.
    //
    // Acik birakiyorum cunku bir mesaj dead letter oldugunda
    // adminin sorunu duzeltip yeniden denemesi gerekiyor -- panelin
    // asil faydasi bu.
    //
    // Erisim zaten Admin roluyle sinirli; salt okunur yapsaydik
    // dead letter mesajlari icin elle SQL yazmak gerekirdi ki
    // uretimde cok daha risklidir.
    IsReadOnlyFunc = _ => false,

    // Panelin kendi "olcum" sayfalarini kapatiyorum: sunucu adi,
    // makine adi gibi bilgileri gereksiz yere yaymanin anlami yok.
    DisplayStorageConnectionString = false
});

// ===================================================================
// TEKRARLANAN ISLERI KAYDET
// ===================================================================
// Uygulama AYAGA KALKTIKTAN SONRA cagiriliyor.
//
// builder asamasinda yapsaydik Hangfire deposu (storage) henuz
// hazir olmazdi ve kayit sirasinda istisna alirdik.
BackgroundJobSetup.RegisterRecurringJobs(
    app.Services.GetRequiredService<IRecurringJobManager>());

// ===================================================================
// CALISTIR
// ===================================================================
// try/finally icinde: Log.CloseAndFlush() cagrilmazsa dosya sink'i
// tamponundaki son loglar DISKE YAZILMADAN process sonlanir.
//
// Yani uygulamanin cokme anindaki loglari -- en cok ihtiyac
// duyacaklarimiz -- kaybolur. Tam olarak isimize yarayacak an.
try
{
    Log.Information("Ticketing API baslatiliyor. Ortam: {Environment}",
        app.Environment.EnvironmentName);

    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Uygulama baslatilamadi.");

    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}

/// <summary>
/// Integration testlerin WebApplicationFactory ile bu projeyi
/// baslatabilmesi icin gereken acik giris noktasi. (PDF Sprint 17)
/// </summary>
public partial class Program;
