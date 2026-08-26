using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ticketing.Domain.Common;

namespace Ticketing.WebApi.Middleware;

/// <summary>
/// Yakalanmamis tum exception'lari Problem Details formatina cevirir.
///
/// PDF Sprint 2:
///   - "Global exception handling eklenmelidir."
///   - "Problem Details standardi kullanilmalidir."
///
/// ------------------------------------------------------------------
/// NEDEN IExceptionHandler, NEDEN KLASIK MIDDLEWARE DEGIL?
/// ------------------------------------------------------------------
/// Eskiden bu is try/catch iceren bir middleware ile yapilirdi.
/// .NET 8 ile gelen IExceptionHandler arayuzu daha iyi:
///
///   - Birden fazla handler zincirlenebilir; biri false donerse
///     sirada digeri denenir. Boylece "once ozel durumlar, sonra
///     genel yakalayici" duzeni kurulabilir.
///   - Framework'un kendi hata isleme altyapisiyla butunlesir.
///   - try/catch blogu yazmadan calisir; kod daha temiz.
/// </summary>
internal sealed partial class GlobalExceptionHandler : IExceptionHandler
{
    // ==================================================================
    // LoggerMessage KAYNAK URETECI
    // ==================================================================
    // Analizci (CA1848) bizi buna yonlendirdi ve HAKLI. Bastirmak yerine
    // uyduk. Neden daha iyi:
    //
    // 1) _logger.LogError(ex, "Hata: {StatusCode}", code) yazdiginda
    //    her cagride:
    //      - object[] dizisi ayrilir (boxing: int -> object)
    //      - sablon metni her seferinde ayristirilir
    //    Log seviyesi kapali olsa BILE bu maliyet oder.
    //
    // 2) Asagidaki [LoggerMessage] nitelikleri, derleme aninda kod
    //    URETIR. Uretilen kod once "bu seviye acik mi?" diye bakar;
    //    kapaliysa hicbir tahsis yapmadan aninda doner.
    //
    // 3) EventId sayesinde loglari koda gore filtreleyebiliyoruz:
    //    "5001 numarali olaylari goster" gibi.
    //
    // Bu, saniyede binlerce istek alan bir serviste olculebilir fark
    // yaratir. Ve maliyeti sadece birkac satirlik bildirim.
    //
    // Not: Bunun icin sinifin "partial" olmasi sart -- uretilen kod
    // ayni sinifin ikinci yarisi olarak eklenir.
    // ==================================================================

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
            // ==========================================================
            // 1. IS KURALI IHLALI -> 422 Unprocessable Entity
            // ==========================================================
            // "Suresi dolmus rezervasyonda odeme baslatilamaz" gibi.
            // Bu bir HATA DEGIL, beklenen bir durum. Gunde binlerce kez
            // olusabilir.
            //
            // Bu yuzden Warning seviyesinde logluyorum, Error degil.
            // Error olsaydi izleme panosu surekli alarm calardi ve
            // GERCEK hatalar bu gurultunun icinde kaybolurdu.
            DomainException domainEx => CreateProblem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "Is kurali ihlali",
                detail: domainEx.Message,
                errorCode: domainEx.ErrorCode),

            // ==========================================================
            // 2. ES ZAMANLILIK CAKISMASI -> 409 Conflict
            // ==========================================================
            // PROJENIN EN KRITIK HATA YOLU.
            //
            // Iki kullanici ayni koltugu ayni anda almaya calisti;
            // EventSeats uzerindeki xmin token'i ikinciyi reddetti.
            //
            // Kullaniciya "sunucu hatasi" demek YANLIS olurdu -- sunucu
            // tam olarak dogru calisti ve veri butunlugunu korudu.
            // 409 dondugumuzde frontend koltuk haritasini yenileyip
            // "bu koltuk az once alindi" diyebiliyor.
            DbUpdateConcurrencyException => CreateProblem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Cakisma",
                detail: "Bu kayit siz islem yaparken baskasi tarafindan degistirildi. " +
                        "Lutfen sayfayi yenileyip tekrar deneyin.",
                errorCode: "concurrency.conflict"),

            // ==========================================================
            // 3. VERITABANI KISITI IHLALI -> 409 Conflict
            // ==========================================================
            // Unique index ihlali buraya duser. Ornegin ayni koltuk icin
            // ikinci bir EventSeat olusturma girisimi.
            //
            // ONEMLI: Exception'in KENDI mesajini kullaniciya DONMUYORUM.
            // Icinde tablo adi, sutun adi ve index adi gecer:
            //     "duplicate key value violates unique constraint
            //      ix_event_seats_session_seat"
            // Bu, saldirgana veritabani semasi hakkinda bilgi verir.
            // Kendi genel mesajimizi donup detayi yalnizca loga yaziyoruz.
            DbUpdateException => CreateProblem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Veri cakismasi",
                detail: "Islem tamamlanamadi. Ayni kayit zaten mevcut olabilir.",
                errorCode: "database.constraint_violation"),

            // ==========================================================
            // 4. ISTEK IPTAL EDILDI -> 499 (istemci baglantisini kesti)
            // ==========================================================
            // Kullanici sayfayi kapatti veya yenilendi. Bu HIC hata degil.
            // 500 olarak loglasaydik hata grafiklerimiz sahte artislarla
            // dolardi.
            OperationCanceledException => CreateProblem(
                statusCode: 499,
                title: "Istek iptal edildi",
                detail: "Islem istemci tarafindan iptal edildi.",
                errorCode: "request.cancelled"),

            // ==========================================================
            // 5. GERCEK HATA -> 500
            // ==========================================================
            _ => CreateProblem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Sunucu hatasi",
                detail: "Beklenmeyen bir hata olustu. Lutfen daha sonra tekrar deneyin.",
                errorCode: "server.unexpected")
        };

        LogException(exception, problem.Status ?? 500);

        // Correlation ID'yi hata yanitina ekliyorum.
        //
        // Boylece kullanici destek talebinde bu kodu verebiliyor ve
        // biz loglarda tam olarak o istegi bulabiliyoruz. Bu tek alan,
        // destek surelerini saatlerden dakikalara indiriyor.
        if (httpContext.Response.Headers.TryGetValue(
                CorrelationIdMiddleware.HeaderName, out var correlationId))
        {
            problem.Extensions["correlationId"] = correlationId.ToString();
        }

        // ==========================================================
        // GELISTIRME ORTAMI ISTISNASI
        // ==========================================================
        // Stack trace'i YALNIZCA gelistirmede donuyorum.
        //
        // Uretimde stack trace dondurmek ciddi bir guvenlik acigidir:
        // saldirgana kullandigin kutuphaneleri, surumlerini, ic sinif
        // yapini ve dosya yollarini gosterir. Bu bilgilerle hedefli
        // saldiri hazirlanabilir.
        if (_environment.IsDevelopment())
        {
            problem.Extensions["exception"] = exception.GetType().Name;
            problem.Extensions["stackTrace"] = exception.StackTrace;
        }

        // RFC 7807: Problem Details icin zorunlu icerik tipi.
        httpContext.Response.StatusCode = problem.Status ?? 500;
        httpContext.Response.ContentType = "application/problem+json";

        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken).ConfigureAwait(false);

        // true = "bu exception'i ben islendim, baska handler'a gitmesin"
        return true;
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

            // RFC 7807'ye gore "type" hatanin dokumantasyonuna isaret
            // eden bir URI olmalidir. Standart HTTP durum kodlari icin
            // RFC'nin kendi adreslerini kullaniyorum.
            Type = $"https://datatracker.ietf.org/doc/html/rfc9110#name-{statusCode}"
        };

        // Makine tarafindan okunabilir hata kodu.
        //
        // Frontend "detail" metnine bakarak karar VERMEMELI -- metni
        // degistirdigimiz gun frontend bozulur. Bu kod sabit kalir.
        if (!string.IsNullOrWhiteSpace(errorCode))
        {
            problem.Extensions["errorCode"] = errorCode;
        }

        return problem;
    }

    private void LogException(Exception exception, int statusCode)
    {
        // ------------------------------------------------------------------
        // LOG SEVIYESI KARARI
        // ------------------------------------------------------------------
        // 4xx = istemci kaynakli, BEKLENEN durum   -> Warning
        // 5xx = sunucu kaynakli, GERCEK hata       -> Error
        //
        // Bu ayrimi yapmasaydik ne olurdu? "Koltuk dolu" gibi gunde
        // binlerce kez olusan normal durumlar Error olarak loglanirdi.
        // Uyari sistemleri surekli alarm calar, ekip alarmlari gormezden
        // gelmeye baslar ve GERCEK bir cokme oldugunda kimse fark etmez.
        //
        // Buna "alarm yorgunlugu" denir ve izleme sistemlerini ise
        // yaramaz hale getiren en yaygin sebeptir.
        if (statusCode >= 500)
        {
            LogUnexpectedError(exception, statusCode, Activity.Current?.Id);
        }
        else
        {
            // Stack trace GECMIYORUM: beklenen bir durum icin 40 satirlik
            // stack trace yazmak log dosyalarini gereksiz sisirir.
            LogClientError(statusCode, exception.Message);
        }
    }
}
