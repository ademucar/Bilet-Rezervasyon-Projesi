using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Serilog;
using Scalar.AspNetCore;
using Ticketing.WebApi.Documentation;
using Ticketing.WebApi.Observability;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
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

// LOGLAMA -- PDF Sprint 16
//
// Serilog'u EN BASTA baglıyorum.
//
// Sebep: bundan sonraki her satır (servis kayitlari, yapilandirma
// okuma, veritabani bağlantısı) log uretebiliyor. Sonra baglasaydim
// uygulamanin ACILIS asamasindaki loglar varsayılan saglayiciya
// giderdi ve dosyaya HİÇ yazilmazdi.
//
// Acilista olusan hatalar ise tam olarak en çok ihtiyac duyulan
// loglardir: uygulama ayaga kalkmadiginda elimizde başka hiçbir sey
// olmuyor.
builder.AddSerilogLogging();

// SERVISLER

builder.Services.AddControllers();
// ---- API dokumantasyonu (PDF Sprint 18) ----
//
// XML yorumlari + transformer'lar ile PDF'in on maddesi
// karsilaniyor. Ayrintisi OpenApiSetup.cs içinde.
builder.Services.AddApiDocumentation();

// ---- API Versioning ----
//
// PDF Sprint 18: "API versioning uygulanmalıdır." ve
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

// ---- Güvenlik ayarlari ----
builder.Services.AddOptions<SecurityOptions>()
       .Bind(builder.Configuration.GetSection(SecurityOptions.SectionName))
       .ValidateDataAnnotations()
       .ValidateOnStart();

builder.Services.AddOptions<ReservationOptions>()
       .Bind(builder.Configuration.GetSection(ReservationOptions.SectionName))
       .ValidateDataAnnotations()
       .ValidateOnStart();

// ICurrentUser HttpContext'e erisiyor; bu erişim için gerekli.
builder.Services.AddHttpContextAccessor();

// Scoped: her HTTP isteği için bir örnek. Singleton OLAMAZ çünkü
// isteğe ozel veri (kullanıcı kimliği) tasiyor.
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

// API GUVENLIGI -- PDF Sprint 15

// ---- İstek hizi sinirlama ----
// Varsayılan açık; yalnızca yapilandirma acikca "false" derse
// kapaniyor (entegrasyon testleri için -- bkz. RateLimitingSetup).
builder.Services.AddRateLimiting(
    builder.Configuration.GetValue("RateLimiting:Enabled", defaultValue: true));

// ---- CORS ----
//
// GELISTIRMEDE CORS'A NEDEN IHTIYAC YOK AMA YINE DE TANIMLIYORUZ?
//
// Gelistirmede Vite proxy'si sayesinde istekler tarayıcı acisindan
// AYNI kaynaga (5173) gidiyor; CORS hiç devreye girmiyor.
//
// Uretimde ise frontend ve API farklı alan adlarinda olabilir. O gün
// yapilandirma yapmak yerine SIMDIDEN kuruyorum -- ama izin verilen
// kaynaklari YAPILANDIRMADAN okuyorum, kodda sabitlemiyorum.
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
            // "Yapilandirma eksikse en güvenli davranis" ilkesi --
            // eksik ayar, açık kapi anlamina gelmemeli.
            policy.WithOrigins();

            return;
        }

        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()

              // AllowCredentials + AllowAnyOrigin BIRLIKTE KULLANILAMAZ
              // (tarayıcı reddeder). Kaynaklari acikca listeledigimiz
              // için kimlik bilgisi tasiyabiliyoruz.
              .AllowCredentials()

              // Istemcinin okuyabilecegi ozel basliklar.
              // Varsayılan olarak yalnızca birkaç standart başlık
              // görünür; Retry-After ve correlation id'yi acikca
              // aciyoruz.
              .WithExposedHeaders("Retry-After", "X-Correlation-Id");
    });
});

// ---- İstek boyutu sınırı ----
//
// PDF: "Request size limit"
//
// Varsayılan Kestrel sınırı ~30 MB. Bizim en büyük istegimiz bir
// JSON govdesi ve birkaç kilobayt.
//
// Sinir olmasaydı saldirgan 30 MB'lik istekler gonderip bellegi ve
// bant genisligini tuketebilirdi (basit bir DoS).
//
// 1 MB: en büyük mesru istegimizin (çok koltuklu rezervasyon)
// onlarca kati.
//
// NOT: Dosya yukleme ucu eklendiginde O UC ICIN ayrı ve daha yüksek
// bir sinir gerekecek -- [RequestSizeLimit] ozniteligi ile uc bazinda
// verilebiliyor.
builder.Services.Configure<Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions>(options =>
{
    options.Limits.MaxRequestBodySize = 1 * 1024 * 1024;

    // "Server: Kestrel" BASLIGINI KALDIR -- YAKALADIGIM HATA
    //
    // Önce bunu SecurityHeadersMiddleware içinde
    // headers.Remove("Server") ile yapmaya calistim. CALISMADI.
    //
    // Sebep: Kestrel bu başlığı OnStarting geri cagrimindan SONRA,
    // yaniti tel uzerine yazarken ekliyor. Middleware'in sildigi sey
    // henüz orada bile degildi.
    //
    // Basliklari gerçekten kontrol ederek buldum:
    //   curl -D - -> "Server: Kestrel" hâlâ goruluyordu.
    //
    // Dogru yer sunucunun kendi ayari. Sprint 13'teki BOM hatasiyla
    // aynı ders: kodun NIYETINI değil, URETTIGI CIKTIYI kontrol
    // etmek gerekiyor.
    //
    // Tek başına bir açık değil ama saldirgana bilgi veriyor:
    // hangi sunucu, hangi surum, hangi bilinen aciklar.
    options.AddServerHeader = false;
});

// ---- Ters vekil sunucu basliklari ----
//
// BU YAPILANDIRMA OLMADAN HIZ SINIRI URETIMDE YANLIS CALISIR
//
// Uretimde uygulama bir ters vekil sunucu (nginx, load balancer)
// arkasinda çalışıyor. O durumda RemoteIpAddress VEKILIN adresini
// gosterir -- gerçek istemciyi değil.
//
// Sonuç: TÜM istekler tek bir IP'den gelmis gibi görünür ve hiz
// sınırı butun kullanicilari BIRLIKTE etkiler. Bir kullanıcı sınırı
// doldurunca herkes 429 alır.
//
// ForwardedHeaders, X-Forwarded-For basligini okuyup gerçek istemci
// adresini geri koyuyor.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

    // KNOWN PROXIES TEMIZLENIYOR -- DIKKAT
    //
    // Varsayılan olarak yalnızca localhost'tan gelen X-Forwarded-For
    // basliklarina guveniliyor. Docker/Kubernetes'te vekil sunucu
    // farklı bir IP'de olur ve basliklar YOK SAYILIR.
    //
    // Listeleri bosaltmak "her vekile guven" demek. Bu, YALNIZCA
    // uygulama doğrudan internete açık DEGILSE guvenlidir: aksi
    // halde saldirgan X-Forwarded-For basligini uydurup hiz sinirini
    // atlatabilir.
    //
    // Üretim dagitiminda vekil sunucunun gerçek adresi buraya
    // yazilmali. Bunu bir NOT olarak birakiyorum çünkü değeri
    // ortama bağlı ve yanlış yapilandirmasi sessizce güvenlik
    // acigi olusturuyor.
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// ARKA PLAN ISLERI -- PDF Sprint 9
builder.Services.AddBackgroundJobs(builder.Configuration);

// REDIS ONBELLEK -- PDF Sprint 11
//
// Bağlantı dizesi yoksa veya Redis kapaliysa uygulama YINE ACILIR;
// önbellek devre dışı kalır ve sorgular veritabanindan karsilanir.
// PDF: "Cache kapalı olduğunda sistem calismaya devam edebilmelidir."
builder.Services.AddCaching(builder.Configuration);

// GERCEK ZAMANLI KOLTUK GUNCELLEME -- PDF Sprint 10
builder.Services.AddSignalR(options =>
{
    // Gelistirmede ayrintili hata dondur.
    //
    // Uretimde KAPALI kalmali: istisna ayrintilari (yigin izi, tip
    // adları, dosya yollari) istemciye gitmemeli. Bu, ic yapiyi
    // saldirgana anlatmak olurdu.
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
});

// Singleton: IHubContext zaten singleton ve bu sinif durum tutmuyor.
// Scoped yapsaydim her istekte gereksiz nesne uretilirdi.
builder.Services.AddSingleton<ISeatNotifier, SignalRSeatNotifier>();

var app = builder.Build();

// VERITABANI SEMASI -- HER ORTAMDA
//
// Bu blok YOKTU ve eksikligi ancak yayin yigini ilk kez temiz bir
// birimle ayaga kaldirilinca ortaya cikti: butun uclar 500 donuyordu,
// log'da tek satir vardi:
//
//     42P01: relation "Cities" does not exist
//
// Uygulama migration'lari HIC uygulamiyordu. Gelistirmede sorun
// gorunmuyordu cunku semayi bir kez elle
// (dotnet ef database update) olusturmustum ve Docker birimi
// duruyordu. Yani sema, kimsenin bir daha calistirmadigi tek
// seferlik bir komutla var olmustu.
//
// NEDEN UYGULAMA ICINDE? Neden ayri bir adim degil?
//
// "Dogrusu" CI/CD'de ayri bir migration adimidir. Bu proje tek
// sunucuda ve TEK API container'i ile calisiyor
// (docker-compose.prod.yml); orada ayri bir adim, unutuldugunda
// sessizce bozulan bir el isi olurdu.
//
// DIKKAT: API birden fazla kopya olarak calistirilirsa bu satir
// yaris olusturur (ayni migration'i ayni anda iki surec uygular).
// O gun geldiginde buradan cikarilip dagitim hattina tasinmali --
// bu yuzden not olarak birakiyorum.
//
// MigrateAsync bekleyen migration yoksa hicbir sey yapmiyor;
// her aciliste calismasinin maliyeti tek bir sorgu.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TicketingDbContext>();

    var bekleyen = (await db.Database.GetPendingMigrationsAsync().ConfigureAwait(false)).ToList();

    if (bekleyen.Count > 0)
    {
        // Serilog'un statik Log'u: bu dosyanin geri kalani da boyle
        // logluyor (asagida basari/hata satirlari). ILogger<Program>
        // kullansaydim CA1848 LoggerMessage temsilcisi isterdi --
        // uygulama omrunde bir kez calisan bu blok icin gereksiz.
        Log.Information(
            "Uygulanacak {Adet} migration var: {Migrationlar}",
            bekleyen.Count,
            string.Join(", ", bekleyen));

        await db.Database.MigrateAsync().ConfigureAwait(false);

        Log.Information("Migration'lar uygulandi.");
    }
}

// REFERANS VERISI -- HER ORTAMDA
//
// Burada onceden "Uretimde ASLA otomatik seed calistirmiyoruz"
// yaziyordu ve seed yalnizca Development'ta kosuyordu. Yayin yigini
// ilk kez temiz bir veritabaniyla ayaga kalkinca bunun tutmadigi
// goruldu:
//
//   Cities: 0, EventCategories: 0
//
// Sifir sehirle mekan olusturulamiyor (sehir zorunlu alan),
// kategori olmadan etkinlik acilamiyor ve filtre paneli bos
// geliyor. Yani uygulama yayina cikar cikmaz KULLANILAMAZ
// durumdaydi ve bunu ancak biri elle SQL yazarak duzeltebilirdi.
//
// ESKI GEREKCE NEDEN GECERLI DEGIL?
//
// Eski not "seed kodu yanlislikla veri uzerine yazabilir" diyordu.
// DatabaseSeeder IDEMPOTENT: tablo bossa ekliyor, doluysa hicbir
// sey yapmiyor. Ustune yazma ihtimali yok.
//
// Ayrica seed edilen sey DEMO VERISI DEGIL: 81 il ve etkinlik
// kategorileri. Bunlar uygulamanin calismasi icin gereken REFERANS
// VERISI -- rol tablosu gibi. (Roller zaten migration icinde
// HasData ile geliyor; sehir/kategori de ayni siniftan.)
//
// Demo etkinlik/kullanici gibi seyler seeder'da YOK; olsalardi
// bu blok yine ortama bagli kalmaliydi.
//
// CreateScope kullaniyorum cunku DatabaseSeeder ve DbContext SCOPED
// kayitli; uygulama koku (root) singleton bir kapsam ve oradan
// scoped servis cozumlemek InvalidOperationException verir.
using (var scope = app.Services.CreateScope())
{
    var seeder = scope.ServiceProvider.GetRequiredService<Ticketing.Persistence.Seeding.DatabaseSeeder>();

    await seeder.SeedAsync().ConfigureAwait(false);
}

// HTTP PIPELINE -- SIRA ONEMLI

// 1) En basta: kendisinden sonraki her seyi sarmalar.
app.UseExceptionHandler();

// 2) Hata yanitina da correlation ID eklenebilsin diye hemen sonra.
app.UseMiddleware<CorrelationIdMiddleware>();

// 2b) İstek özeti logu: correlation ID middleware'inden SONRA.
//
// Önce olsaydı özet satirinda CorrelationId alanı BOŞ olurdu --
// çünkü deger henüz üretilmemiş olurdu. Bu, PDF'in "correlation ID
// application log içinde olmalı" maddesini sessizce karsilanmamis
// birakirdi: kod var, alan boş.
app.UseRequestLogging();

// GÜVENLİK KATMANLARI -- PDF Sprint 15

// 3) Ters vekil basliklari: hiz sinirindan ONCE olmalı.
//
// Sonra olsaydı hiz sınırı hâlâ vekilin IP'sini gorurdu ve butun
// kullanicilari tek kotada toplardi.
app.UseForwardedHeaders();

// 4) Güvenlik basliklari: mumkun oldugunca ERKEN.
//
// Hata sayfalari ve statik dosyalar dahil TÜM yanitlara eklensin
// istiyorum.
app.UseMiddleware<SecurityHeadersMiddleware>();

// 5) CORS: kimlik dogrulamadan ONCE.
//
// Tarayicinin gonderdigi on kontrol (preflight OPTIONS) isteği
// kimlik bilgisi TASIMAZ. Kimlik dogrulamadan sonra olsaydı
// preflight 401 alır ve gerçek istek hiç gonderilmezdi.
app.UseCors();

// 6) Hiz sınırı: kimlik dogrulamadan SONRA.
//
// Boylece giriş yapmış kullanıcılar için kota KULLANICI bazlı
// olabiliyor (bkz. ClientKey). Önce olsaydı herkes IP bazlı
// sayilirdi ve aynı agdaki kullanıcılar birbirini engellerdi.
//
// Sıra: Authentication -> RateLimiter -> Authorization

if (app.Environment.IsDevelopment())
{
    // Ham OpenAPI belgesi: /openapi/v1.json
    app.MapOpenApi();

    // SCALAR ARAYUZU -- /scalar
    //
    // Yalnızca GELISTIRMEDE aciliyor.
    //
    // Uretimde açık birakmak, tüm uclarin, parametrelerin ve hata
    // kodlarinin haritasini saldirgana hazır sunmak olurdu. API'nin
    // kendisi zaten korumali ama "hangi uclar var?" sorusunu
    // bedavaya cevaplamanin bir sebebi yok.
    //
    // Gerçek bir uretimde bu arayüz ayrı bir ic ag adresinde veya
    // kimlik dogrulamali olarak sunulur.
    app.MapScalarApiReference(options =>
    {
        options.Title = "Biletim API";

        // Arayuzun urettigi örnek kod parcasi: varsayılan olarak
        // birden fazla dil gosteriyor. Bizim istemcimiz TypeScript.
        options.DefaultHttpClient =
            new(ScalarTarget.JavaScript, ScalarClient.Fetch);
    });
}
else
{
    app.UseHttpsRedirection();
}

// SIRA KRITIK: Authentication ONCE, Authorization SONRA
//
// UseAuthentication  -> "Sen kimsin?"   (token'i okur, User'i doldurur)
// UseAuthorization   -> "Yetkin var mi?" (User'a bakip karar verir)
//
// Ters yazsaydım Authorization henüz doldurulmamis bir User goreceginden
// giriş yapmış kullanıcılar bile 401 alırdı. Ve bu hata çok kafa
// karistiricidir: token doğru, kod doğru ama calismiyor.
app.UseAuthentication();
app.UseRateLimiter();
// SAHIPLIK REDDINDE 404 -- PDF Sprint 19 denetiminde eklendi
//
// UseAuthorization'dan ONCE kaydediliyor. İlk denememde SONRASINA
// koymustum ve middleware HİÇ CALISMADI.
//
// Sebep: middleware zinciri ic ice halkalar gibi çalışıyor. Bir
// middleware "sonraki"ni cagirir, o döner, sonra kendi isini
// bitirir.
//
// Yetkilendirme reddettiginde KISA DEVRE yapiyor: 403 yazip
// dönüyor ve sonraki halkayi HİÇ CAGIRMIYOR. Yani sonrasina
// konan bir middleware o durumda calismaz.
//
// Önce koydugumuzda ise: benim _next() cagrimiz yetkilendirmeyi
// KAPSIYOR. O reddedip donunce kontrol bana geri geliyor ve
// yaniti duzeltebiliyoruz.
//
// Ders: "sonra calissin" istiyorsan middleware'i ONCE kaydet.
// Sirala mantığı isteklerde ileri, YANITLARDA geri isliyor.
app.UseMiddleware<OwnershipNotFoundMiddleware>();

app.UseAuthorization();

app.MapControllers();
// SAGLIK UCLARI -- PDF Sprint 16
//
// Ucu de AllowAnonymous: yuk dengeleyici ve Kubernetes probe'lari
// token tasiyamaz. Bu yüzden yanitlarda hiçbir hassas bilgi yok
// (bağlantı dizesi, surum, ic hata mesaji donmuyor).

// 1) Insan için: her seyin ayrintili özeti.
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse,
});

// 2) "Trafik alabilir miyim?" -- TÜM bagimliliklar kontrol ediliyor.
//
// Kubernetes readiness probe bunu cagirir. Başarısız olursa
// kapsayici OLDURULMEZ, yalnızca yuk dengeleyiciden cikarilir.
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains(HealthChecksSetup.ReadyTag),
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse,
});

// 3) "Process ayakta mi?" -- HICBIR bagimlilik kontrol edilmiyor.
//
// Predicate = _ => false  SATIRI BU DOSYADAKI EN KRITIK SATIR
//
// Buraya veritabani kontrolü eklemek çok mantikli görünür ve
// FELAKETLE sonuclanir:
//
//   PostgreSQL 30 saniye yanit vermez -> tüm kapsayicilarin live
//   probe'u duser -> Kubernetes hepsini OLDURUR -> yeniden
//   baslarlar, veritabani hâlâ yok -> yine olurler...
//
// Gecici bir veritabani sorunu, kalici bir uygulama cokusune
// donusur. Uygulama, kendi yeniden baslatmasiyla COZEMEYECEGI bir
// sey için surekli yeniden baslatilir.
//
// Live probe yalnızca "bu process kilitlendi mi?" sorusunu
// cevaplamali. Cevabi bagimliliklara BAGLI OLMAMALI.
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
});

// KOLTUK HUB'I -- PDF Sprint 10
//
// Adres frontend'deki VITE proxy'siyle eslesiyor: /hubs/seats
app.MapHub<SeatHub>("/hubs/seats");

// HANGFIRE IZLEME EKRANI -- PDF Sprint 9
//
// UseAuthentication/UseAuthorization SONRASINA konuldu.
//
// Önce konsaydi, filtre calistiginda HttpContext.User henüz
// doldurulmamis olurdu: admin olan kullanıcı bile panele
// giremezdi ve sebebi anlasilmazdi.
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = [new HangfireDashboardAuthorizationFilter()],

    // Panelden is SILME ve YENIDEN CALISTIRMA yetkisi.
    //
    // Acik birakiyorum çünkü bir mesaj dead letter olduğunda
    // adminin sorunu duzeltip yeniden denemesi gerekiyor -- panelin
    // asil faydasi bu.
    //
    // Erişim zaten Admin roluyle sinirli; salt okunur yapsaydim
    // dead letter mesajlari için elle SQL yazmak gerekirdi ki
    // uretimde çok daha risklidir.
    IsReadOnlyFunc = _ => false,

    // Panelin kendi "olcum" sayfalarini kapatiyorum: sunucu adı,
    // makine adı gibi bilgileri gereksiz yere yaymanin anlami yok.
    DisplayStorageConnectionString = false
});

// TEKRARLANAN ISLERI KAYDET
//
// Uygulama AYAGA KALKTIKTAN SONRA cagiriliyor.
//
// builder asamasinda yapsaydim Hangfire deposu (storage) henüz
// hazır olmazdi ve kayıt sırasında istisna alırdım.
BackgroundJobSetup.RegisterRecurringJobs(
    app.Services.GetRequiredService<IRecurringJobManager>());

// CALISTIR
//
// try/finally içinde: Log.CloseAndFlush() cagrilmazsa dosya sink'i
// tamponundaki son loglar DISKE YAZILMADAN process sonlanir.
//
// Yani uygulamanin cokme anindaki loglari -- en çok ihtiyac
// duyacaklarimiz -- kaybolur. Tam olarak isimize yarayacak an.
try
{
    Log.Information(
        "Ticketing API baslatiliyor. Ortam: {Environment}",
        app.Environment.EnvironmentName);

    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Uygulama başlatılamadı.");

    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}

/// <summary>
/// Integration testlerin WebApplicationFactory ile bu projeyi
/// baslatabilmesi için gereken açık giriş noktasi. (PDF Sprint 17)
/// </summary>
public partial class Program;
