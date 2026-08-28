namespace Ticketing.Application.Abstractions.Email;

/// <summary>E-posta sablonlari. PDF Sprint 14'un saydigi sekiz sablon.</summary>
public enum EmailTemplate
{
    Welcome = 1,
    PasswordReset = 2,
    ReservationCreated = 3,
    PaymentSucceeded = 4,
    TicketDetails = 5,
    EventReminder = 6,
    EventCancelled = 7,
    RefundCompleted = 8,
}

/// <summary>Uretilmis e-posta: konu + HTML govde.</summary>
public sealed record RenderedEmail(string Subject, string HtmlBody);

/// <summary>
/// E-posta sablonlarini uretir. PDF Sprint 14.
/// </summary>
/// <remarks>
/// ==================================================================
/// NEDEN SABLON SISTEMI? Onceden ne vardi?
/// ==================================================================
/// Sprint 9'a kadar e-posta govdeleri handler'larin ICINE gomulu
/// HTML metinleriydi:
///
///     body.Append($"&lt;p&gt;Merhaba {user.FirstName},&lt;/p&gt;");
///     body.Append("&lt;p&gt;Odemeniz alindi...&lt;/p&gt;");
///
/// Bu yaklasimin uc somut sorunu vardi:
///
/// 1) GORUNUM TUTARSIZLIGI: her e-posta farkli gorunuyordu. Birinde
///    baslik vardi digerinde yoktu; birinde imza vardi digerinde
///    yoktu. Kullanicinin gozunde bunlar ayni sirketten gelmiyor
///    gibi duruyordu.
///
/// 2) DEGISIKLIK MALIYETI: alt bilgiye "abonelikten cik" baglantisi
///    eklemek gerekseydi SEKIZ ayri dosyayi degistirmek gerekirdi --
///    ve birini unutmak kacinilmazdi.
///
/// 3) IS MANTIGI KIRLILIGI: odeme handler'inin isi para islemek,
///    HTML yazmak degil.
///
/// Sablon sistemi ucunu de cozuyor: ortak bir cerceve (layout) var,
/// her sablon yalnizca KENDI icerigini uretiyor, handler'lar da
/// yalnizca VERI gonderiyor.
/// ==================================================================
/// </remarks>
public interface IEmailTemplateRenderer
{
    /// <summary>
    /// Sablonu verilerle doldurup konu ve HTML govdeyi dondurur.
    /// </summary>
    /// <param name="data">
    /// Sablonun bekledigi alanlar. Anahtar adlari her sablonun
    /// belgelerinde yazili.
    ///
    /// Neden sozluk, tipli bir nesne degil? Cunku sekiz sablonun
    /// sekiz farkli alan kumesi var ve hepsi icin ayri tip tanimlamak
    /// (ve arayuzu jenerik yapmak) bu kadar kucuk bir is icin fazla
    /// karmasik olurdu. Eksik anahtar durumunda sablon "-" yaziyor,
    /// patlamiyor.
    /// </param>
    /// <remarks>
    /// Parametre adi "template" DEGIL "emailTemplate": CA1716, arayuz
    /// uyelerinde baska dillerin ayrilmis kelimelerini (C++'ta
    /// "template") kullanmayi engelliyor. Bastirmak yerine uydum --
    /// yeniden adlandirmak zaten okunakliligi bozmuyor.
    /// </remarks>
    RenderedEmail Render(EmailTemplate emailTemplate, IReadOnlyDictionary<string, string> data);
}
