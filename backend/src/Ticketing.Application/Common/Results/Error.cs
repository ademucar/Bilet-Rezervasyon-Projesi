using System.Diagnostics.CodeAnalysis;

namespace Ticketing.Application.Common.Results;

/// <summary>
/// Hatanin TURU. Bu tur, HTTP durum koduna cevrilecek.
///
/// Neden ayrı bir enum? Çünkü Application katmani HTTP'yi BILMEMELI.
/// Burada "404 dondur" yazsaydim, Application katmani web'e bagimli
/// hale gelirdi ve aynı kodu bir konsol uygulamasindan veya gRPC
/// servisinden kullanamazdik.
///
/// Bunun yerine "kayıt bulunamadı" diyoruz; HTTP'ye cevirme isi
/// WebApi katmaninin sorumlulugunda.
/// </summary>
public enum ErrorType
{
    /// <summary>Girdi doğrulama hatası. -> HTTP 400</summary>
    Validation = 1,

    /// <summary>Kayıt bulunamadı. -> HTTP 404</summary>
    NotFound = 2,

    /// <summary>Is kuralı ihlali. -> HTTP 422</summary>
    Conflict = 3,

    /// <summary>Es zamanlilik çakışması. -> HTTP 409</summary>
    Concurrency = 4,

    /// <summary>Kimlik dogrulanmamis. -> HTTP 401</summary>
    Unauthorized = 5,

    /// <summary>Yetki yok. -> HTTP 403</summary>
    Forbidden = 6,

    /// <summary>Beklenmeyen hata. -> HTTP 500</summary>
    Unexpected = 7,
}

/// <summary>
/// Bir hatayi temsil eder.
///
/// ------------------------------------------------------------------
/// NEDEN EXCEPTION DEĞİL DE BU?
/// ------------------------------------------------------------------
/// Exception'lar BEKLENMEYEN durumlar icindir. "Kullanıcı bulunamadı"
/// veya "bu koltuk dolu" beklenen durumlardir -- her gün yuzlerce kez
/// olusurlar.
///
/// Beklenen durumlar için exception kullanmanin uc sorunu var:
///
/// 1) PAHALI. Exception firlatmak, stack trace toplamak demektir ve
///    normal bir donus degerinden yuzlerce kat yavastir. Popüler bir
///    konserde saniyede 50 kişi aynı koltuğu deneyebilir.
///
/// 2) GORUNMEZ. Metodun imzasina bakarak hangi hatalari firlatabilecegini
///    goremezsin. Result&lt;T&gt; donen bir metot "ben başarısız olabilirim"
///    diye ACIKCA söyler ve derleyici seni kontrol etmeye zorlar.
///
/// 3) AKIS KONTROLU. try/catch ile is akışı yonetmek okunabilirligi bozar.
///
/// Not: Domain katmaninda hâlâ DomainException kullanıyoruz. Orada
/// amac farklı: entity'nin ic tutarliligini korumak. Bir entity
/// geçersiz duruma DUSEMEMELI, bu yüzden orada patlamak doğru.
/// Application katmani ise o exception'i yakalayip Result'a cevirir.
/// </summary>
/// <param name="Code">Makine tarafından okunabilir kod. Ornek: "seat.already_locked"</param>
/// <param name="Message">Kullanıcıya gosterilebilir mesaj.</param>
/// <param name="Type">HTTP durum koduna cevrilecek tur.</param>
[SuppressMessage(
    "Naming",
    "CA1716:Identifiers should not match keywords",
    Justification =
        "CA1716, 'Error' adinin VB.NET'te ayrilmis bir anahtar kelime olmasını " +
        "sebebiyle uyarir ve yalnızca sinif başka .NET dillerinden tuketilecekse " +
        "anlamlidir. Projemiz tamamen C#. " +
        "'Error' bu baglamda dogru ve yerlesmis bir isimdir (Result/Error kalibi); " +
        "'ApplicationError' gibi bir ada cevirmek okunabilirligi dusururdu. " +
        "Kural yalnızca bu tip için, en dar kapsamda bastirildi.")]
public sealed record Error(string Code, string Message, ErrorType Type)
{
    /// <summary>
    /// Başarılı sonuclarda kullanilan "hata yok" değeri.
    ///
    /// null yerine bunu kullanmamin sebebi: Result.Error alanina her
    /// eristigimde null kontrolü yapmak zorunda kalmamak. Bu kalibin
    /// adı "Null Object Pattern".
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
