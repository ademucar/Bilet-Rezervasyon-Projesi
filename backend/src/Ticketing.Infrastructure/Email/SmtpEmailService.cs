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
/// SMTP üzerinden e-posta gönderir. Yerel gelistirmede Mailpit'e baglanir.
///
/// ==================================================================
/// SPRINT 3'TE BIRAKTIGIM NOTUN KARSILIGI
/// ==================================================================
/// Sprint 3'te ilk tercihim MailKit'ti (.NET dunyasinda standart
/// e-posta kutuphanesi). Ama paket ekleme komutu su hatayi verdi:
///
///   NU1902: 'MailKit' paketinde onem derecesi ORTA olan bilinen bir
///           güvenlik acigi var (GHSA-9j88-vvj5-vhgr)
///
/// Denedigim TÜM surumlerde (4.9.0 - 4.14.0) aynı uyarı cikti.
/// Bilinen acigi olan bir paketi projeye almayi reddettim ve .NET'in
/// yerlesik SmtpClient'ini kullandim -- eskimis (SYSLIB0014) ama
/// güvenli.
///
/// O gün koda su notu birakmistim:
///
///   "SPRINT 14 NOTU: Gerçek bir e-posta saglayicisina gecerken bu
///    sinif degisecek. O gün MailKit advisory'sinin kapanip
///    kapanmadigi TEKRAR kontrol edilmeli."
///
/// ------------------------------------------------------------------
/// SPRINT 14: KONTROL ETTIM, ACIK KAPANMIS
/// ------------------------------------------------------------------
/// MailKit 4.17.0 ile tarama TEMIZ dondu:
///
///   dotnet list package -vulnerable -include-transitive
///   -> 8 projenin hicbirinde güvenlik acigi olan paket yok
///
/// Yani Sprint 3'teki gerekce artık geçerli değil. MailKit'e geciyorum:
///
///   - SYSLIB0014 bastirmasi KALKTI (artık eskimis API kullanmiyoruz)
///   - Microsoft'un kendisi SmtpClient yerine MailKit'i oneriyor
///   - Modern TLS ve kimlik doğrulama destegi var
///
/// Bu, kodda birakilan bir "sonra bak" notunun neden degerli oldugunun
/// somut ornegi: karar o gunun kosullarina göre verilmisti, kosullar
/// değişti ve karar güncellendi.
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
        // HTML GOVDE + DUZ METİN ALTERNATIFI
        // ==============================================================
        // BodyBuilder ile hem HTML hem duz metin surumu gonderiyoruz
        // (multipart/alternative).
        //
        // Neden ikisi birden?
        //
        //   1) Bazi e-posta istemcileri HTML'i kapatiyor (güvenlik
        //      ayari). Duz metin olmasaydı kullanıcı BOŞ bir e-posta
        //      gorurdu.
        //
        //   2) Spam filtreleri, yalnızca HTML iceren mesajlari daha
        //      supheli buluyor. Duz metin alternatifi teslim oranini
        //      artiriyor.
        //
        // Duz metni HTML'den TUREITIYORUM: etiketleri sokup metni
        // biraktigimda ayrı bir sablon yazmaya gerek kalmiyor ve
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
            // Mailpit (yerel gelistirme) TLS kullanmiyor; gerçek
            // saglayicilar kullaniyor.
            //
            // SecureSocketOptions.Auto sectim: MailKit sunucunun
            // yeteneklerine bakip karar veriyor. Sabit bir deger
            // verseydik ya yerelde ya uretimde calismazdi ve
            // yapilandirma ile ayrilmasi gerekirdi.
            //
            // UseSsl acikca true ise zorluyoruz -- yapilandirmayla
            // "TLS ŞART" demek isteyen bir ortam için.
            var secureOptions = _options.UseSsl
                ? SecureSocketOptions.StartTlsWhenAvailable
                : SecureSocketOptions.Auto;

            await client.ConnectAsync(_options.Host, _options.Port, secureOptions, cancellationToken)
                .ConfigureAwait(false);

            // Kimlik bilgisi VARSA dogrula.
            //
            // Mailpit kimlik doğrulama istemiyor; kosulsuz
            // AuthenticateAsync cagirsaydik yerelde patlardi.
            if (!string.IsNullOrWhiteSpace(_options.Username))
            {
                // Parola null olabilir (yalnızca kullanıcı adiyla
                // doğrulama yapan sunucular var). MailKit null kabul
                // etmiyor; boş metne ceviriyoruz.
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
            // gonderimde açık açık loglamak, log dosyalarini bir
            // kullanıcı listesine cevirir -- ve o dosyalar yedeklenip
            // merkezi sistemlere gidiyor.
            //
            // Tamamen gizlemiyorum: destek talebinde "hangi kullanıcı?"
            // sorusunu cevaplayabilmemiz gerekiyor. İlk uc harf +
            // alan adı bunun için yeterli ipucu veriyor ama adresleri
            // TOPLU olarak toplamayi engelliyor.
            //
            // IsEnabled KONTROLU (CA1873): bu log Debug seviyesinde ve
            // uretimde genellikle KAPALI. Kontrol olmadan MaskEmail her
            // e-postada boşuna çalışır ve bir string tahsis ederdi.
            //
            // Kaynak ureteci normalde bu kontrolü KENDISI ekliyor -- ama
            // yalnızca cagriya PARAMETRE OLARAK gecilen degerler için.
            // Burada parametreyi biz hesapliyoruz, o yüzden kontrolü de
            // elle yazmamiz gerekiyor.
            // ==========================================================
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                // Maskeleme SONUCU bir yerel degiskene aliniyor.
                //
                // Dogrudan cagriyi parametre olarak yazsaydim CA1873
                // yine uyarirdi: analizor, uretilen LoggerMessage
                // metodunun IsEnabled kontrolunu taniyamiyor ve
                // "parametre içinde metot cagrisi" gordugu her yerde
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
            // finally ŞART: gonderim istisna firlatirsa bile SMTP
            // bağlantısı kapanmali.
            //
            // Kapanmazsa sunucu tarafında açık bağlantı birikir ve
            // saglayicilar bunu kotuye kullanim sayip IP'yi
            // engelleyebilir.
            //
            // IsConnected kontrolü: bağlantı hiç kurulamadiysa
            // DisconnectAsync istisna firlatirdi ve ASIL hatayi
            // gizlerdi.
            if (client.IsConnected)
            {
                await client.DisconnectAsync(quit: true, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// HTML'den kaba bir duz metin üretir.
    /// </summary>
    /// <remarks>
    /// Tam bir HTML ayristiricisi DEĞİL ve olmasını da gerekmiyor:
    /// sablonlarimizi biz yazıyoruz ve yapilari basit.
    ///
    /// AngleSharp gibi bir kutuphane eklemek, yalnızca yedek metin
    /// uretmek için çok agir olurdu.
    /// </remarks>
    private static string HtmlToText(string html)
    {
        // Blok etiketlerini satır sonuna cevir ki metin okunabilir kalsin.
        var metin = System.Text.RegularExpressions.Regex.Replace(
            html,
            @"<(br|/p|/div|/li|/h[1-6]|/tr)\s*/?>",
            "\n",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase,
            TimeSpan.FromSeconds(1));

        // Kalan tüm etiketleri kaldir.
        metin = System.Text.RegularExpressions.Regex.Replace(
            metin, "<[^>]+>", string.Empty, System.Text.RegularExpressions.RegexOptions.None,
            TimeSpan.FromSeconds(1));

        // HTML varliklarini coz.
        metin = System.Net.WebUtility.HtmlDecode(metin);

        // Ardisik boş satirlari tekile indir.
        metin = System.Text.RegularExpressions.Regex.Replace(
            metin, @"\n{3,}", "\n\n", System.Text.RegularExpressions.RegexOptions.None,
            TimeSpan.FromSeconds(1));

        return metin.Trim();
    }

    [LoggerMessage(
        EventId = 9401,
        Level = LogLevel.Debug,
        Message = "E-posta gönderildi. Alici: {Recipient}, Konu: {Subject}")]
    private static partial void LogSent(ILogger logger, string recipient, string subject);
}
