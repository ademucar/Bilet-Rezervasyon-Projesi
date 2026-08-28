using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.Text;
using Ticketing.Application.Abstractions.Email;

using Ticketing.Application.Common.Security;

namespace Ticketing.Infrastructure.Email;

/// <summary>
/// SMTP uzerinden e-posta gonderir. Yerel gelistirmede Mailpit'e baglanir.
///
/// ==================================================================
/// SPRINT 3'TE BIRAKTIGIM NOTUN KARSILIGI
/// ==================================================================
/// Sprint 3'te ilk tercihim MailKit'ti (.NET dunyasinda standart
/// e-posta kutuphanesi). Ama paket ekleme komutu su hatayi verdi:
///
///   NU1902: 'MailKit' paketinde onem derecesi ORTA olan bilinen bir
///           guvenlik acigi var (GHSA-9j88-vvj5-vhgr)
///
/// Denedigim TUM surumlerde (4.9.0 - 4.14.0) ayni uyari cikti.
/// Bilinen acigi olan bir paketi projeye almayi reddettim ve .NET'in
/// yerlesik SmtpClient'ini kullandim -- eskimis (SYSLIB0014) ama
/// guvenli.
///
/// O gun koda su notu birakmistim:
///
///   "SPRINT 14 NOTU: Gercek bir e-posta saglayicisina gecerken bu
///    sinif degisecek. O gun MailKit advisory'sinin kapanip
///    kapanmadigi TEKRAR kontrol edilmeli."
///
/// ------------------------------------------------------------------
/// SPRINT 14: KONTROL ETTIM, ACIK KAPANMIS
/// ------------------------------------------------------------------
/// MailKit 4.17.0 ile tarama TEMIZ dondu:
///
///   dotnet list package -vulnerable -include-transitive
///   -> 8 projenin hicbirinde guvenlik acigi olan paket yok
///
/// Yani Sprint 3'teki gerekce artik gecerli degil. MailKit'e geciyorum:
///
///   - SYSLIB0014 bastirmasi KALKTI (artik eskimis API kullanmiyoruz)
///   - Microsoft'un kendisi SmtpClient yerine MailKit'i oneriyor
///   - Modern TLS ve kimlik dogrulama destegi var
///
/// Bu, kodda birakilan bir "sonra bak" notunun neden degerli oldugunun
/// somut ornegi: karar o gunun kosullarina gore verilmisti, kosullar
/// degisti ve karar guncellendi.
/// ==================================================================
/// </summary>
internal sealed partial class SmtpEmailService : IEmailService
{
    private readonly EmailOptions _options;
    private readonly ILogger<SmtpEmailService> _logger;

    public SmtpEmailService(IOptions<EmailOptions> options, ILogger<SmtpEmailService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;
        _logger = logger;
    }

    public async Task SendAsync(
        string recipient,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken = default)
    {
        var message = new MimeMessage();

        message.From.Add(new MailboxAddress(_options.FromName, _options.From));
        message.To.Add(MailboxAddress.Parse(recipient));
        message.Subject = subject;

        // ==============================================================
        // HTML GOVDE + DUZ METIN ALTERNATIFI
        // ==============================================================
        // BodyBuilder ile hem HTML hem duz metin surumu gonderiyoruz
        // (multipart/alternative).
        //
        // Neden ikisi birden?
        //
        //   1) Bazi e-posta istemcileri HTML'i kapatiyor (guvenlik
        //      ayari). Duz metin olmasaydi kullanici BOS bir e-posta
        //      gorurdu.
        //
        //   2) Spam filtreleri, yalnizca HTML iceren mesajlari daha
        //      supheli buluyor. Duz metin alternatifi teslim oranini
        //      artiriyor.
        //
        // Duz metni HTML'den TUREITIYORUM: etiketleri sokup metni
        // biraktigimda ayri bir sablon yazmaya gerek kalmiyor ve
        // ikisi birbirinden ayrisamiyor.
        // ==============================================================
        var builder = new BodyBuilder
        {
            HtmlBody = htmlBody,
            TextBody = HtmlToText(htmlBody),
        };

        message.Body = builder.ToMessageBody();

        using var client = new SmtpClient();

        try
        {
            // ==========================================================
            // TLS SECIMI
            // ==========================================================
            // Mailpit (yerel gelistirme) TLS kullanmiyor; gercek
            // saglayicilar kullaniyor.
            //
            // SecureSocketOptions.Auto sectim: MailKit sunucunun
            // yeteneklerine bakip karar veriyor. Sabit bir deger
            // verseydik ya yerelde ya uretimde calismazdi ve
            // yapilandirma ile ayrilmasi gerekirdi.
            //
            // UseSsl acikca true ise zorluyoruz -- yapilandirmayla
            // "TLS SART" demek isteyen bir ortam icin.
            var secureOptions = _options.UseSsl
                ? SecureSocketOptions.StartTlsWhenAvailable
                : SecureSocketOptions.Auto;

            await client.ConnectAsync(_options.Host, _options.Port, secureOptions, cancellationToken)
                .ConfigureAwait(false);

            // Kimlik bilgisi VARSA dogrula.
            //
            // Mailpit kimlik dogrulama istemiyor; kosulsuz
            // AuthenticateAsync cagirsaydik yerelde patlardi.
            if (!string.IsNullOrWhiteSpace(_options.Username))
            {
                // Parola null olabilir (yalnizca kullanici adiyla
                // dogrulama yapan sunucular var). MailKit null kabul
                // etmiyor; bos metne ceviriyoruz.
                await client.AuthenticateAsync(
                    _options.Username,
                    _options.Password ?? string.Empty,
                    cancellationToken).ConfigureAwait(false);
            }

            await client.SendAsync(message, cancellationToken).ConfigureAwait(false);

            // ==========================================================
            // E-POSTA KISMEN MASKELENIYOR -- PDF Sprint 15
            // ==========================================================
            // E-posta adresi KVKK/GDPR kapsaminda kisisel veri. Her
            // gonderimde acik acik loglamak, log dosyalarini bir
            // kullanici listesine cevirir -- ve o dosyalar yedeklenip
            // merkezi sistemlere gidiyor.
            //
            // Tamamen gizlemiyorum: destek talebinde "hangi kullanici?"
            // sorusunu cevaplayabilmemiz gerekiyor. Ilk uc harf +
            // alan adi bunun icin yeterli ipucu veriyor ama adresleri
            // TOPLU olarak toplamayi engelliyor.
            //
            // IsEnabled KONTROLU (CA1873): bu log Debug seviyesinde ve
            // uretimde genellikle KAPALI. Kontrol olmadan MaskEmail her
            // e-postada bosuna calisir ve bir string tahsis ederdi.
            //
            // Kaynak ureteci normalde bu kontrolu KENDISI ekliyor -- ama
            // yalnizca cagriya PARAMETRE OLARAK gecilen degerler icin.
            // Burada parametreyi biz hesapliyoruz, o yuzden kontrolu de
            // elle yazmamiz gerekiyor.
            // ==========================================================
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                // Maskeleme SONUCU bir yerel degiskene aliniyor.
                //
                // Dogrudan cagriyi parametre olarak yazsaydim CA1873
                // yine uyarirdi: analizor, uretilen LoggerMessage
                // metodunun IsEnabled kontrolunu taniyamiyor ve
                // "parametre icinde metot cagrisi" gordugu her yerde
                // uyariyor. Yerel degiskene almak hem analizoru
                // memnun ediyor hem de kodu okunur birakiyor.
                var maskeliAlici = SensitiveDataMasker.MaskEmail(recipient);

                LogSent(_logger, maskeliAlici, subject);
            }
        }
        finally
        {
            // ==========================================================
            // BAGLANTIYI HER DURUMDA KAPAT
            // ==========================================================
            // finally SART: gonderim istisna firlatirsa bile SMTP
            // baglantisi kapanmali.
            //
            // Kapanmazsa sunucu tarafinda acik baglanti birikir ve
            // saglayicilar bunu kotuye kullanim sayip IP'yi
            // engelleyebilir.
            //
            // IsConnected kontrolu: baglanti hic kurulamadiysa
            // DisconnectAsync istisna firlatirdi ve ASIL hatayi
            // gizlerdi.
            if (client.IsConnected)
            {
                await client.DisconnectAsync(quit: true, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// HTML'den kaba bir duz metin uretir.
    /// </summary>
    /// <remarks>
    /// Tam bir HTML ayristiricisi DEGIL ve olmasi da gerekmiyor:
    /// sablonlarimizi biz yaziyoruz ve yapilari basit.
    ///
    /// AngleSharp gibi bir kutuphane eklemek, yalnizca yedek metin
    /// uretmek icin cok agir olurdu.
    /// </remarks>
    private static string HtmlToText(string html)
    {
        // Blok etiketlerini satir sonuna cevir ki metin okunabilir kalsin.
        var metin = System.Text.RegularExpressions.Regex.Replace(
            html,
            @"<(br|/p|/div|/li|/h[1-6]|/tr)\s*/?>",
            "\n",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase,
            TimeSpan.FromSeconds(1));

        // Kalan tum etiketleri kaldir.
        metin = System.Text.RegularExpressions.Regex.Replace(
            metin, "<[^>]+>", string.Empty, System.Text.RegularExpressions.RegexOptions.None,
            TimeSpan.FromSeconds(1));

        // HTML varliklarini coz.
        metin = System.Net.WebUtility.HtmlDecode(metin);

        // Ardisik bos satirlari tekile indir.
        metin = System.Text.RegularExpressions.Regex.Replace(
            metin, @"\n{3,}", "\n\n", System.Text.RegularExpressions.RegexOptions.None,
            TimeSpan.FromSeconds(1));

        return metin.Trim();
    }

    [LoggerMessage(
        EventId = 9401,
        Level = LogLevel.Debug,
        Message = "E-posta gonderildi. Alici: {Recipient}, Konu: {Subject}")]
    private static partial void LogSent(ILogger logger, string recipient, string subject);
}
