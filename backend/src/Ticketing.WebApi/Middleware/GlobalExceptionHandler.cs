using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ticketing.Domain.Common;

using Ticketing.Application.Common.Security;

namespace Ticketing.WebApi.Middleware;

/// <summary>
/// Yakalanmamış tüm exception'lari Problem Details formatina cevirir.
///
/// PDF Sprint 2:
///   - "Global exception handling eklenmelidir."
///   - "Problem Details standardi kullanılmalıdır."
///
/// NEDEN IExceptionHandler, NEDEN KLASIK MIDDLEWARE DEĞİL?
///
/// Eskiden bu is try/catch iceren bir middleware ile yapilirdi.
/// .NET 8 ile gelen IExceptionHandler arayuzu daha iyi:
///
///   - Birden fazla handler zincirlenebilir; biri false donerse
///     sırada digeri denenir. Boylece "önce ozel durumlar, sonra
///     genel yakalayici" düzeni kurulabilir.
///   - Framework'un kendi hata isleme altyapisiyla butunlesir.
///   - try/catch blogu yazmadan çalışır; kod daha temiz.
/// </summary>
internal sealed partial class GlobalExceptionHandler : IExceptionHandler
{
    // LoggerMessage KAYNAK URETECI
    //
    // Analizci (CA1848) bizi buna yonlendirdi ve HAKLI. Bastirmak yerine
    // uyduk. Neden daha iyi:
    //
    // 1) _logger.LogError(ex, "Hata: {StatusCode}", code) yazdiginda
    //    her cagride:
    //      - object[] dizisi ayrilir (boxing: int -> object)
    //      - sablon metni her seferinde ayristirilir
    //    Log seviyesi kapalı olsa BILE bu maliyet oder.
    //
    // 2) Aşağıdaki [LoggerMessage] nitelikleri, derleme anında kod
    //    URETIR. Uretilen kod önce "bu seviye açık mi?" diye bakar;
    //    kapaliysa hiçbir tahsis yapmadan anında döner.
    //
    // 3) EventId sayesinde loglari koda göre filtreleyebiliyoruz:
    //    "5001 numarali olaylari goster" gibi.
    //
    // Bu, saniyede binlerce istek alan bir serviste olculebilir fark
    // yaratir. Ve maliyeti sadece birkaç satirlik bildirim.
    //
    // Not: Bunun için sinifin "partial" olmasını sart -- uretilen kod
    // aynı sinifin ikinci yarisi olarak eklenir.

    [LoggerMessage(
        EventId = 5000,
        Level = LogLevel.Error,
        Message = "Beklenmeyen hata. Durum kodu: {StatusCode}, Aktivite: {ActivityId}")]
    private partial void LogUnexpectedError(Exception exception, int statusCode, string? activityId);

    [LoggerMessage(
        EventId = 4000,
        Level = LogLevel.Warning,
        Message = "Istek reddedildi. Durum kodu: {StatusCode}, Sebep: {Reason}")]
    private partial void LogClientError(int statusCode, string reason);

    private readonly ILogger<GlobalExceptionHandler> _logger;
    private readonly IHostEnvironment _environment;

    public GlobalExceptionHandler(
        ILogger<GlobalExceptionHandler> logger,
        IHostEnvironment environment)
    {
        _logger = logger;
        _environment = environment;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var problem = exception switch
        {
            // 0. GIRDI DOGRULAMA HATASI -> 400 Bad Request
            //
            // ValidationBehavior'in firlattigi hata.
            //
            // Bu daldan önce yoktu ve doğrulama hatalari 500 donuyordu --
            // uygulamayi ILK KEZ CALISTIRDIGIMDA fark ettim. Derleme
            // temizdi, testler yesildi ama endpoint yanlış cevap
            // veriyordu.
            //
            // Ders: birim testler ve derleme, "parcalar doğru mu" sorusunu
            // cevaplar; "sistem doğru mu" sorusunu ancak calistirmak
            // (veya integration test) cevaplar. Sprint 17'de bu akislari
            // integration testle koruyacagim.
            Application.Common.Exceptions.ValidationException validationEx
                => CreateValidationProblem(validationEx),

            // 1. IS KURALI IHLALI -> 422 Unprocessable Entity
            //
            // "Süresi dolmuş rezervasyonda ödeme baslatilamaz" gibi.
            // Bu bir HATA DEĞİL, beklenen bir durum. Gunde binlerce kez
            // olusabilir.
            //
            // Bu yüzden Warning seviyesinde logluyorum, Error değil.
            // Error olsaydı izleme panosu surekli alarm calardi ve
            // GERCEK hatalar bu gurultunun içinde kaybolurdu.
            DomainException domainEx => CreateProblem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "Is kuralı ihlali",
                detail: domainEx.Message,
                errorCode: domainEx.ErrorCode),

            // 2. ES ZAMANLILIK CAKISMASI -> 409 Conflict
            //
            // Projenin en kritik hata yolu.
            //
            // Iki kullanıcı aynı koltuğu aynı anda almaya calisti;
            // EventSeats uzerindeki xmin token'i ikinciyi reddetti.
            //
            // Kullanıcıya "sunucu hatası" demek YANLIS olurdu -- sunucu
            // tam olarak doğru calisti ve veri butunlugunu korudu.
            // 409 dondugumuzde frontend koltuk haritasini yenileyip
            // "bu koltuk az önce alındı" diyebiliyor.
            DbUpdateConcurrencyException => CreateProblem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Cakisma",
                detail: "Bu kayıt siz işlem yaparken başkası tarafından degistirildi. " +
                        "Lütfen sayfayı yenileyip tekrar deneyin.",
                errorCode: "concurrency.conflict"),

            // 3. VERITABANI KISITI IHLALI -> 409 Conflict
            //
            // Unique index ihlali buraya duser. Ornegin aynı koltuk için
            // ikinci bir EventSeat oluşturma girisimi.
            //
            // ONEMLI: Exception'in KENDİ mesajini kullanıcıya DONMUYORUM.
            // Icinde tablo adı, sutun adı ve index adı gecer:
            //     "duplicate key value violates unique constraint
            //      ix_event_seats_session_seat"
            // Bu, saldirgana veritabani semasi hakkında bilgi verir.
            // Kendi genel mesajimi donup detayı yalnızca loga yazıyorum.
            DbUpdateException => CreateProblem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Veri çakışması",
                detail: "İşlem tamamlanamadi. Aynı kayıt zaten mevcut olabilir.",
                errorCode: "database.constraint_violation"),

            // 4. ISTEK İPTAL EDILDI -> 499 (istemci baglantisini kesti)
            //
            // Kullanıcı sayfayı kapatti veya yenilendi. Bu HİÇ hata değil.
            // 500 olarak loglasaydim hata grafiklerim sahte artislarla
            // dolardi.
            OperationCanceledException => CreateProblem(
                statusCode: 499,
                title: "İstek iptal edildi",
                detail: "İşlem istemci tarafından iptal edildi.",
                errorCode: "request.cancelled"),

            // 5. ISTEK COK BUYUK / BOZUK -> 413 veya 400
            //
            // Bu dali sprint 15'te testle buldum
            //
            // Program.cs'te MaxRequestBodySize = 1 MB ayarladiktan sonra
            // 2 MB'lik bir istek gonderip dogruladim. Sonuç 500 dondu.
            //
            // 500 YANLIS ve iki acidan zararli:
            //   1) Istemciye "sunucu bozuk" diyor. Oysa sunucu tam olarak
            //      doğru calisti -- korumasi devreye girdi. Istemci
            //      "sonra tekrar denerim" diye dusunup aynı büyük isteği
            //      tekrar gönderiyor ve sonsuza kadar başarısız oluyor.
            //   2) 500'ler izleme panosunda alarm uretiyor. Saldirgan
            //      büyük istekler gondererek sahte alarm yagmuru
            //      olusturabilirdi.
            //
            // Kestrel sinir asimini BadHttpRequestException olarak
            // firlatiyor ve içinde DOGRU durum kodunu tasiyor
            // (413 Payload Too Large). Onu kullanıyorum -- kendim
            // tahmin etmiyorum, çünkü aynı istisna bozuk govde için
            // 400 ile de gelebiliyor.
            //
            // DERS: bir korumayi eklemek yetmiyor; TETIKLENDIGINDE ne
            // dondugunu de dogrulamak gerekiyor. Ayar dogruydu, yanit
            // yanlisti ve bunu yalnızca calistirinca gordum.
            BadHttpRequestException badRequestEx => CreateProblem(
                statusCode: badRequestEx.StatusCode,
                title: badRequestEx.StatusCode == StatusCodes.Status413PayloadTooLarge
                    ? "İstek çok büyük"
                    : "Geçersiz istek",
                detail: badRequestEx.StatusCode == StatusCodes.Status413PayloadTooLarge
                    ? "Gonderdiginiz istek izin verilen boyutu asiyor."
                    : "Istek govdesi okunamadi veya bicimi geçersiz.",

                // Istisnanin KENDİ mesajini donmuyorum: içinde Kestrel'in
                // ic ayrintilari (sinir değeri, ayristirici durumu) gecer
                // ve bu saldirgana yapilandirmamizi açık eder.
                errorCode: badRequestEx.StatusCode == StatusCodes.Status413PayloadTooLarge
                    ? "request.too_large"
                    : "request.malformed"),

            // 6. GERCEK HATA -> 500
            _ => CreateProblem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Sunucu hatası",
                detail: "Beklenmeyen bir hata oluştu. Lütfen daha sonra tekrar deneyin.",
                errorCode: "server.unexpected")
        };

        LogException(exception, problem.Status ?? 500);

        // Correlation ID'yi hata yanitina ekliyorum.
        //
        // Boylece kullanıcı destek talebinde bu kodu verebiliyor ve
        // biz loglarda tam olarak o isteği bulabiliyoruz. Bu tek alan,
        // destek surelerini saatlerden dakikalara indiriyor.
        if (httpContext.Response.Headers.TryGetValue(
                CorrelationIdMiddleware.HeaderName, out var correlationId))
        {
            problem.Extensions["correlationId"] = correlationId.ToString();
        }

        // Gelistirme ortami istisnasi
        //
        // Stack trace'i YALNIZCA gelistirmede donuyorum.
        //
        // Uretimde stack trace dondurmek ciddi bir güvenlik acigidir:
        // saldirgana kullandigin kutuphaneleri, surumlerini, ic sinif
        // yapini ve dosya yollarini gosterir. Bu bilgilerle hedefli
        // saldiri hazirlanabilir.
        if (_environment.IsDevelopment())
        {
            problem.Extensions["exception"] = exception.GetType().Name;

            // Stack trace de maskeleniyor: içinde degisken değerleri
            // gecmese bile, ic ice sarilmis istisnalarin mesajlari
            // stack trace metnine dahil olabiliyor.
            //
            // Burasi yalnızca gelistirme ortami ama gelistirme
            // ortamindaki veriler de gerçek: tarayıcı konsolundan
            // kopyalanip hata kaydina yapistiriliyor.
            problem.Extensions["stackTrace"] = SensitiveDataMasker.Mask(exception.StackTrace);
        }

        // RFC 7807: Problem Details için zorunlu içerik tipi.
        httpContext.Response.StatusCode = problem.Status ?? 500;
        httpContext.Response.ContentType = "application/problem+json";

        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken).ConfigureAwait(false);

        // true = "bu exception'i ben islendim, başka handler'a gitmesin"
        return true;
    }

    /// <summary>
    /// Doğrulama hatalarini RFC 7807'nin "errors" uzantisiyla döndürür.
    ///
    /// Biçim:
    ///     {
    ///       "status": 400, "title": "Doğrulama hatası",
    ///       "errors": {
    ///         "Email":    ["Geçerli bir e-posta adresi giriniz."],
    ///         "Password": ["Şifre en az 8 karakter olmalıdır."]
    ///       }
    ///     }
    ///
    /// Duz bir liste değil ALAN BAZINDA sozluk donuyorum çünkü frontend
    /// her mesaji ilgili form alaninin altinda göstermek zorunda.
    /// Liste donseydim hangi mesajin hangi alana ait olduğu bilinemezdi.
    /// </summary>
    private static ProblemDetails CreateValidationProblem(
        Application.Common.Exceptions.ValidationException exception)
    {
        var problem = CreateProblem(
            StatusCodes.Status400BadRequest,
            "Doğrulama hatası",
            "Gonderilen veriler geçerli değil. Lütfen alanlari kontrol edin.",
            "validation.failed");

        problem.Extensions["errors"] = exception.Errors;

        return problem;
    }

    private static ProblemDetails CreateProblem(
        int statusCode,
        string title,
        string detail,
        string? errorCode)
    {
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,

            // RFC 7807'ye göre "type" hatanin dokumantasyonuna isaret
            // eden bir URI olmalıdır. Standart HTTP durum kodlari için
            // RFC'nin kendi adreslerini kullanıyorum.
            Type = $"https://datatracker.ietf.org/doc/html/rfc9110#name-{statusCode}"
        };

        // Makine tarafından okunabilir hata kodu.
        //
        // Frontend "detail" metnine bakarak karar VERMEMELI -- metni
        // degistirdigim gün frontend bozulur. Bu kod sabit kalır.
        if (!string.IsNullOrWhiteSpace(errorCode))
        {
            problem.Extensions["errorCode"] = errorCode;
        }

        return problem;
    }

    private void LogException(Exception exception, int statusCode)
    {
        // Log seviyesi karari
        //
        // 4xx = istemci kaynakli, BEKLENEN durum   -> Warning
        // 5xx = sunucu kaynakli, GERCEK hata       -> Error
        //
        // Bu ayrimi yapmasaydim ne olurdu? "Koltuk dolu" gibi günde
        // binlerce kez olusan normal durumlar Error olarak loglanirdi.
        // Uyari sistemleri surekli alarm calar, ekip alarmlari gormezden
        // gelmeye başlar ve GERCEK bir cokme olduğunda kimse fark etmez.
        //
        // Buna "alarm yorgunlugu" denir ve izleme sistemlerini ise
        // yaramaz hale getiren en yaygin sebeptir.
        if (statusCode >= 500)
        {
            LogUnexpectedError(exception, statusCode, Activity.Current?.Id);
        }
        else
        {
            // Stack trace GECMIYORUM: beklenen bir durum için 40 satirlik
            // stack trace yazmak log dosyalarini gereksiz sisirir.
            //
            // MESAJ MASKELENIYOR -- PDF Sprint 15: "Hassas veri maskeleme"
            //
            // exception.Message KULLANICI GIRDISI ICEREBILIYOR. Somut
            // ornekler:
            //
            //   - JSON ayristirma hatası, govdenin bir parcasini mesaja
            //     koyar. Login isteği başarısız ayristirilirsa SIFRE
            //     loga duser.
            //   - FluentValidation mesajlari, dogrulanan değeri
            //     iceren bicimde yazilabiliyor.
            //   - Npgsql kisit ihlali mesajlari, cakisan DEGERI yazıyor.
            //
            // Loglar "güvenli" degildir: yedeklenir, merkezi sisteme
            // gonderilir, ekran goruntusu alinip paylasilir. Oraya
            // dusen bir JWT, o kullanıcının hesabi demektir.
            LogClientError(statusCode, SensitiveDataMasker.Mask(exception.Message));
        }
    }
}
