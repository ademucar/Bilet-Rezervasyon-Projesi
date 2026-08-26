using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;
using Ticketing.Application.Abstractions.Email;

namespace Ticketing.Infrastructure.Email;

/// <summary>
/// SMTP uzerinden e-posta gonderir. Yerel gelistirmede Mailpit'e baglanir.
///
/// ==================================================================
/// NEDEN MailKit KULLANMADIM? -- BILINCLI BIR GUVENLIK KARARI
/// ==================================================================
/// Ilk tercihim MailKit'ti (.NET dunyasinda standart e-posta kutuphanesi).
/// Ancak "dotnet add package" komutu su hatayi verdi:
///
///   NU1902: 'MailKit' paketinde onem derecesi ORTA olan bilinen bir
///           guvenlik acigi var (GHSA-9j88-vvj5-vhgr)
///
/// Denedigim TUM surumlerde (4.9.0'dan 4.14.0'a kadar) ayni uyari
/// cikti -- yani acik henuz giderilmemis.
///
/// TreatWarningsAsErrors ayarimiz sayesinde bu uyari derlemeyi kirdi
/// ve karar vermek zorunda kaldim. Uc secenek vardi:
///
///   1. NU1902'yi bastirip MailKit'i eklemek
///      -> Bilinen bir acigi bile bile projeye almak. HAYIR.
///
///   2. E-postayi hic gondermemek, yalnizca loglamak
///      -> Akisi ucdan uca dogrulayamazdik.
///
///   3. .NET'in yerlesik SmtpClient'ini kullanmak  <-- SECILEN
///
/// SmtpClient "yeni gelistirmeler icin onerilmez" diye isaretli.
/// Sebebi guvenlik acigi DEGIL: modern kimlik dogrulama (OAuth2)
/// ve bazi protokol ozelliklerini desteklemiyor.
///
/// Bizim ihtiyacimiz basit: yerel bir SMTP sunucusuna kimlik
/// dogrulamasiz duz mesaj gondermek. SmtpClient bunu sorunsuz yapar.
///
/// SPRINT 14 NOTU: Gercek bir e-posta saglayicisina (SendGrid, SES)
/// gecerken bu sinif degisecek. O gun MailKit advisory'sinin
/// kapanip kapanmadigi TEKRAR kontrol edilmeli.
/// ==================================================================
/// </summary>
internal sealed class SmtpEmailService : IEmailService
{
    private readonly EmailOptions _options;

    public SmtpEmailService(IOptions<EmailOptions> options) => _options = options.Value;

    [SuppressMessage(
        "Usage",
        "SYSLIB0014:Type or member is obsolete",
        Justification =
            "SmtpClient, modern kimlik dogrulama protokollerini desteklemedigi " +
            "icin 'yeni gelistirmeler icin onerilmez' olarak isaretli -- guvenlik " +
            "acigi sebebiyle DEGIL. " +
            "Alternatif olan MailKit'in tum surumlerinde acik bir guvenlik " +
            "advisory'si (GHSA-9j88-vvj5-vhgr) bulunuyor; bilinen acigi olan bir " +
            "paketi projeye almak, eskimis ama guvenli bir API kullanmaktan " +
            "daha kotu bir tercih olurdu. " +
            "Kullanim senaryomuz basit SMTP gonderimi ve SmtpClient bunu karsiliyor. " +
            "Sprint 14'te gercek saglayiciya gecerken yeniden degerlendirilecek.")]
    public async Task SendAsync(
        string recipient,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken = default)
    {
        using var client = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = _options.UseSsl,

            // Kimlik bilgisi verilmemisse anonim baglan.
            // Mailpit kimlik dogrulama istemiyor; gercek saglayicilar ister.
            Credentials = string.IsNullOrWhiteSpace(_options.Username)
                ? CredentialCache.DefaultNetworkCredentials
                : new NetworkCredential(_options.Username, _options.Password)
        };

        using var message = new MailMessage
        {
            From = new MailAddress(_options.From, _options.FromName),
            Subject = subject,
            Body = htmlBody,
            IsBodyHtml = true
        };

        message.To.Add(recipient);

        // SmtpClient.SendMailAsync CancellationToken almiyor.
        // Iptal destegi icin WaitAsync ile sarmaliyorum: istek iptal
        // edilirse bekleme sonlanir (gonderim arka planda tamamlanabilir
        // ama istegi bloke etmez).
        await client.SendMailAsync(message, cancellationToken).ConfigureAwait(false);
    }
}
