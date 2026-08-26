using System.Diagnostics.CodeAnalysis;

namespace Ticketing.Application.Common.Results;

/// <summary>
/// Hatanin TURU. Bu tur, HTTP durum koduna cevrilecek.
///
/// Neden ayri bir enum? Cunku Application katmani HTTP'yi BILMEMELI.
/// Burada "404 dondur" yazsaydim, Application katmani web'e bagimli
/// hale gelirdi ve ayni kodu bir konsol uygulamasindan veya gRPC
/// servisinden kullanamazdik.
///
/// Bunun yerine "kayit bulunamadi" diyoruz; HTTP'ye cevirme isi
/// WebApi katmaninin sorumlulugunda.
/// </summary>
public enum ErrorType
{
    /// <summary>Girdi dogrulama hatasi. -> HTTP 400</summary>
    Validation = 1,

    /// <summary>Kayit bulunamadi. -> HTTP 404</summary>
    NotFound = 2,

    /// <summary>Is kurali ihlali. -> HTTP 422</summary>
    Conflict = 3,

    /// <summary>Es zamanlilik cakismasi. -> HTTP 409</summary>
    Concurrency = 4,

    /// <summary>Kimlik dogrulanmamis. -> HTTP 401</summary>
    Unauthorized = 5,

    /// <summary>Yetki yok. -> HTTP 403</summary>
    Forbidden = 6,

    /// <summary>Beklenmeyen hata. -> HTTP 500</summary>
    Unexpected = 7
}

/// <summary>
/// Bir hatayi temsil eder.
///
/// ------------------------------------------------------------------
/// NEDEN EXCEPTION DEGIL DE BU?
/// ------------------------------------------------------------------
/// Exception'lar BEKLENMEYEN durumlar icindir. "Kullanici bulunamadi"
/// veya "bu koltuk dolu" beklenen durumlardir -- her gun yuzlerce kez
/// olusurlar.
///
/// Beklenen durumlar icin exception kullanmanin uc sorunu var:
///
/// 1) PAHALI. Exception firlatmak, stack trace toplamak demektir ve
///    normal bir donus degerinden yuzlerce kat yavastir. Populer bir
///    konserde saniyede 50 kisi ayni koltugu deneyebilir.
///
/// 2) GORUNMEZ. Metodun imzasina bakarak hangi hatalari firlatabilecegini
///    goremezsin. Result&lt;T&gt; donen bir metot "ben basarisiz olabilirim"
///    diye ACIKCA soyler ve derleyici seni kontrol etmeye zorlar.
///
/// 3) AKIS KONTROLU. try/catch ile is akisi yonetmek okunabilirligi bozar.
///
/// Not: Domain katmaninda hala DomainException kullaniyoruz. Orada
/// amac farkli: entity'nin ic tutarliligini korumak. Bir entity
/// gecersiz duruma DUSEMEMELI, bu yuzden orada patlamak dogru.
/// Application katmani ise o exception'i yakalayip Result'a cevirir.
/// </summary>
/// <param name="Code">Makine tarafindan okunabilir kod. Ornek: "seat.already_locked"</param>
/// <param name="Message">Kullaniciya gosterilebilir mesaj.</param>
/// <param name="Type">HTTP durum koduna cevrilecek tur.</param>
[SuppressMessage(
    "Naming",
    "CA1716:Identifiers should not match keywords",
    Justification =
        "CA1716, 'Error' adinin VB.NET'te ayrilmis bir anahtar kelime olmasi " +
        "sebebiyle uyarir ve yalnizca sinif baska .NET dillerinden tuketilecekse " +
        "anlamlidir. Projemiz tamamen C#. " +
        "'Error' bu baglamda dogru ve yerlesmis bir isimdir (Result/Error kalibi); " +
        "'ApplicationError' gibi bir ada cevirmek okunabilirligi dusururdu. " +
        "Kural yalnizca bu tip icin, en dar kapsamda bastirildi.")]
public sealed record Error(string Code, string Message, ErrorType Type)
{
    /// <summary>
    /// Basarili sonuclarda kullanilan "hata yok" degeri.
    ///
    /// null yerine bunu kullanmamin sebebi: Result.Error alanina her
    /// eristigimde null kontrolu yapmak zorunda kalmamak. Bu kalibin
    /// adi "Null Object Pattern".
    /// </summary>
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.Unexpected);

    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);

    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);

    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);

    public static Error Concurrency(string code, string message) => new(code, message, ErrorType.Concurrency);

    public static Error Unauthorized(string code, string message) => new(code, message, ErrorType.Unauthorized);

    public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden);

    public static Error Unexpected(string code, string message) => new(code, message, ErrorType.Unexpected);
}
