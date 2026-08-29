namespace Ticketing.Application.Abstractions.Email;

/// <summary>E-posta sablonlari. PDF Sprint 14'un saydığı sekiz sablon.</summary>
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
/// E-posta sablonlarini üretir. PDF Sprint 14.
/// </summary>
/// <remarks>
/// ==================================================================
/// NEDEN SABLON SISTEMI? Onceden ne vardi?
/// ==================================================================
/// Sprint 9'a kadar e-posta govdeleri handler'larin ICINE gomulu
/// HTML metinleriydi:
///
///     body.Append($"&lt;p&gt;Merhaba {user.FirstName},&lt;/p&gt;");
///     body.Append("&lt;p&gt;Ödemeniz alındı...&lt;/p&gt;");
///
/// Bu yaklasimin uc somut sorunu vardi:
///
/// 1) GORUNUM TUTARSIZLIGI: her e-posta farklı gorunuyordu. Birinde
///    başlık vardi digerinde yoktu; birinde imza vardi digerinde
///    yoktu. Kullanıcının gozunde bunlar aynı sirketten gelmiyor
///    gibi duruyordu.
///
/// 2) DEGISIKLIK MALIYETI: alt bilgiye "abonelikten cik" bağlantısı
///    eklemek gerekseydi SEKIZ ayrı dosyayı degistirmek gerekirdi --
///    ve birini unutmak kacinilmazdi.
///
/// 3) IS MANTIGI KIRLILIGI: ödeme handler'inin isi para islemek,
///    HTML yazmak değil.
///
/// Sablon sistemi ucunu de cozuyor: ortak bir cerceve (layout) var,
/// her sablon yalnızca KENDİ icerigini uretiyor, handler'lar da
/// yalnızca VERI gönderiyor.
/// ==================================================================
/// </remarks>
public interface IEmailTemplateRenderer
{
    /// <summary>
    /// Sablonu verilerle doldurup konu ve HTML govdeyi döndürür.
    /// </summary>
    /// <param name="data">
    /// Sablonun bekledigi alanlar. Anahtar adları her sablonun
    /// belgelerinde yazili.
    ///
    /// Neden sozluk, tipli bir nesne değil? Çünkü sekiz sablonun
    /// sekiz farklı alan kumesi var ve hepsi için ayrı tip tanimlamak
    /// (ve arayuzu jenerik yapmak) bu kadar küçük bir is için fazla
    /// karmasik olurdu. Eksik anahtar durumunda sablon "-" yazıyor,
    /// patlamiyor.
    /// </param>
    /// <remarks>
    /// Parametre adı "template" DEĞİL "emailTemplate": CA1716, arayüz
    /// uyelerinde başka dillerin ayrilmis kelimelerini (C++'ta
    /// "template") kullanmayi engelliyor. Bastirmak yerine uydum --
    /// yeniden adlandirmak zaten okunakliligi bozmuyor.
    /// </remarks>
    RenderedEmail Render(EmailTemplate emailTemplate, IReadOnlyDictionary<string, string> data);
}
