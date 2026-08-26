namespace Ticketing.Application.Abstractions.Email;

/// <summary>
/// E-posta gonderimi.
///
/// Application katmani "e-posta nasil gonderilir" bilmiyor -- SMTP mi,
/// SendGrid mi, Amazon SES mi umurunda degil. Yalnizca bu arayuzu
/// cagiriyor. Saglayici degistiginde Application'da tek satir degismez.
///
/// NOT (Sprint 9): Su an handler'lar bu servisi DOGRUDAN cagiriyor.
/// Outbox Pattern uygulandiginda, handler'lar bunun yerine Outbox'a
/// mesaj yazacak ve gonderimi arka plandaki job yapacak.
///
/// Neden simdiden oyle yapmiyorum? Cunku Outbox islemcisi Sprint 9'da
/// yazilacak. Simdi outbox'a yazsaydik e-postalar HIC gonderilmezdi ve
/// akisi ucdan uca dogrulayamazdik. Calisir bir sey birakip sonra
/// iyilestirmek, calismayan bir iskelet birakmaktan iyidir.
/// </summary>
public interface IEmailService
{
    Task SendAsync(
        string recipient,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken = default);
}
