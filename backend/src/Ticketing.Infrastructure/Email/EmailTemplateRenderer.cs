using System.Globalization;
using System.Net;
using System.Text;
using Ticketing.Application.Abstractions;
using Ticketing.Application.Abstractions.Email;

namespace Ticketing.Infrastructure.Email;

/// <summary>
/// PDF Sprint 14'un istedigi sekiz e-posta sablonunu üretir.
/// </summary>
internal sealed class EmailTemplateRenderer : IEmailTemplateRenderer
{
    private readonly IAppUrlProvider _urls;

    public EmailTemplateRenderer(IAppUrlProvider urls) => _urls = urls;

    public RenderedEmail Render(
        EmailTemplate emailTemplate,
        IReadOnlyDictionary<string, string> data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var (subject, icerik) = emailTemplate switch
        {
            EmailTemplate.Welcome => Welcome(data),
            EmailTemplate.PasswordReset => PasswordReset(data),
            EmailTemplate.ReservationCreated => ReservationCreated(data),
            EmailTemplate.PaymentSucceeded => PaymentSucceeded(data),
            EmailTemplate.TicketDetails => TicketDetails(data),
            EmailTemplate.EventReminder => EventReminder(data),
            EmailTemplate.EventCancelled => EventCancelled(data),
            EmailTemplate.RefundCompleted => RefundCompleted(data),

            _ => throw new ArgumentOutOfRangeException(
                nameof(emailTemplate), emailTemplate, "Bilinmeyen e-posta sablonu."),
        };

        return new RenderedEmail(subject, Layout(subject, icerik));
    }

    // Güvenlik: HTML kacisi
    //
    /// <summary>
    /// Sablona giren her değeri HTML-kacisli döndürür.
    /// </summary>
    /// <remarks>
    /// Bu metot olmasaydi: e-posta uzerinden icerik enjeksiyonu
    ///
    /// Sablon verilerinin çoğu KULLANICIDAN geliyor: ad, soyad,
    /// etkinlik başlığı, iptal sebebi.
    ///
    /// Kullanıcı adını "&lt;script&gt;..." veya
    /// "&lt;a href='kötü-site'&gt;Hesabinizi dogrulayin&lt;/a&gt;"
    /// olarak kaydederse, kacis olmadan bu HTML e-postaya OLDUGU GIBI
    /// girerdi.
    ///
    /// Cogu e-posta istemcisi script calistirmiyor ama BAGLANTI
    /// çalışıyor. Yani saldirgan, BENIM alan adimizdan gonderilen
    /// bir e-postaya kendi kimlik avi baglantisini koyabilirdi --
    /// alicinin gozunde tamamen guvenilir görünen bir mesaj.
    ///
    /// Tek bir yerde kacis yapmak, sekiz sablonda tek tek dusunmekten
    /// çok daha güvenli.
    /// </remarks>
    private static string H(IReadOnlyDictionary<string, string> data, string key)
        => WebUtility.HtmlEncode(data.TryGetValue(key, out var v) ? v : "-");

    // Ortak cerceve

    /// <summary>
    /// Tüm e-postalarin ortak govdesi.
    /// </summary>
    /// <remarks>
    /// NEDEN SATIR ICI (inline) CSS?
    ///
    /// Web'de satır ici stil kötü bir aliskanliktir. E-postada ise
    /// ZORUNLULUK: Gmail, Outlook ve çoğu istemci &lt;style&gt;
    /// blogunu SILIYOR veya yok sayiyor.
    ///
    /// Ayrıca tablo yerine div kullanıyorum ama basit tutuyorum --
    /// eski Outlook surumleri karmasik flexbox/grid duzenlerini
    /// bozuyor.
    ///
    /// max-width 600px: e-posta istemcilerinde yaygin kabul goren
    /// genislik. Daha genisi mobilde yatay kaydirma yaratiyor.
    /// </remarks>
    private string Layout(string baslik, string icerik)
    {
        var sb = new StringBuilder(2048);

        sb.Append("<div style=\"font-family:-apple-system,Segoe UI,Roboto,Arial,sans-serif;");
        sb.Append("background:#f1f5f9;padding:24px 0;\">");
        sb.Append("<div style=\"max-width:600px;margin:0 auto;background:#ffffff;");
        sb.Append("border-radius:12px;overflow:hidden;border:1px solid #e2e8f0;\">");

        // Ust bant
        sb.Append("<div style=\"background:#2563eb;padding:20px 24px;\">");
        sb.Append("<span style=\"color:#ffffff;font-size:20px;font-weight:700;\">Biletim</span>");
        sb.Append("</div>");

        // Icerik
        sb.Append("<div style=\"padding:24px;color:#0f172a;font-size:15px;line-height:1.6;\">");
        sb.Append(icerik);
        sb.Append("</div>");

        // Alt bilgi
        //
        // "Bu e-postayi neden aldiniz?" açıklaması ŞART: aksi halde
        // kullanıcı mesaji spam olarak isaretleyebilir ve bu tüm
        // gonderim itibarimizi dusurur.
        sb.Append("<div style=\"background:#f8fafc;padding:16px 24px;border-top:1px solid #e2e8f0;");
        sb.Append("color:#64748b;font-size:12px;line-height:1.5;\">");
        sb.Append("Bu e-postayi, Biletim hesabinizla yaptiginiz bir işlem sebebiyle aldiniz.<br>");
        sb.Append(CultureInfo.InvariantCulture, $"<a href=\"{_urls.FrontendUrl}\" style=\"color:#2563eb;\">Biletim</a>");
        sb.Append(" &middot; Bu adrese yanit vermeyin.");
        sb.Append("</div>");

        sb.Append("</div></div>");

        return sb.ToString();
    }

    /// <summary>Vurgulu eylem dugmesi.</summary>
    private static string Buton(string url, string metin)
        => $"<p style=\"margin:24px 0;\"><a href=\"{url}\" " +
           "style=\"background:#2563eb;color:#ffffff;text-decoration:none;" +
           "padding:12px 24px;border-radius:8px;display:inline-block;" +
           $"font-weight:600;\">{WebUtility.HtmlEncode(metin)}</a></p>";

    /// <summary>Ad/deger satirlarindan olusan bilgi kutusu.</summary>
    private static string Kutu(params (string Etiket, string Deger)[] satirlar)
    {
        var sb = new StringBuilder();

        sb.Append("<div style=\"background:#f8fafc;border:1px solid #e2e8f0;");
        sb.Append("border-radius:8px;padding:16px;margin:16px 0;\">");

        foreach (var (etiket, deger) in satirlar)
        {
            sb.Append("<div style=\"margin:6px 0;\">");
            sb.Append(CultureInfo.InvariantCulture, $"<span style=\"color:#64748b;\">{etiket}: </span>");
            sb.Append(CultureInfo.InvariantCulture, $"<strong>{deger}</strong>");
            sb.Append("</div>");
        }

        sb.Append("</div>");

        return sb.ToString();
    }

    // 1) Hos geldiniz
    //
    // Beklenen alanlar: FirstName
    private (string, string) Welcome(IReadOnlyDictionary<string, string> d)
        => ("Biletim'e hos geldiniz",
            $"<p>Merhaba {H(d, "FirstName")},</p>" +
            "<p>Biletim hesabiniz oluşturuldu. Artık konser, tiyatro ve " +
            "daha bircok etkinlik icin bilet alabilirsiniz.</p>" +
            Buton($"{_urls.FrontendUrl}/etkinlikler", "Etkinlikleri kesfet"));

    // 2) Sifre sifirlama
    //
    // Beklenen alanlar: FirstName, ResetUrl, ExpiryMinutes
    private (string, string) PasswordReset(IReadOnlyDictionary<string, string> d)
        => ("Şifre sıfırlama talebi",
            $"<p>Merhaba {H(d, "FirstName")},</p>" +
            "<p>Hesabınız için şifre sıfırlama talebinde bulunuldu. " +
            "Yeni sifrenizi belirlemek icin asagidaki dugmeye tiklayin.</p>" +

            // Baglantiyi KACIRMADAN kullanıyorum: bu deger kullanicidan
            // gelmiyor, sunucunun urettigi bir adres. Kacirsaydim
            // "&amp;" gibi karakterler bozulur ve bağlantı calismazdi.
            Buton(
                d.TryGetValue("ResetUrl", out var url) ? url : _urls.FrontendUrl,
                "Şifremi sıfırla") +

            $"<p style=\"color:#64748b;font-size:13px;\">Bu bağlantı " +
            $"{H(d, "ExpiryMinutes")} dakika gecerlidir.</p>" +

            // Güvenlik uyarısı ŞART: talebi yapmayan biri bu e-postayi
            // aldiysa hesabinin hedef alindigini bilmeli.
            "<p style=\"color:#64748b;font-size:13px;\">Bu talebi siz " +
            "yapmadiysaniz bu e-postayi yok sayabilirsiniz; sifreniz " +
            "degismeyecektir.</p>");

    // 3) Rezervasyon olusturuldu
    //
    // Beklenen: FirstName, EventTitle, ReservationCode, SeatCount,
    //           TotalAmount, ExpiresInMinutes
    private (string, string) ReservationCreated(IReadOnlyDictionary<string, string> d)
        => ($"Rezervasyonunuz oluşturuldu - {H(d, "EventTitle")}",
            $"<p>Merhaba {H(d, "FirstName")},</p>" +
            "<p>Koltuklariniz sizin için ayrildi. Ödemeyi tamamlayarak " +
            "biletlerinizi olusturabilirsiniz.</p>" +
            Kutu(
                ("Etkinlik", H(d, "EventTitle")),
                ("Rezervasyon kodu", H(d, "ReservationCode")),
                ("Koltuk", H(d, "SeatCount")),
                ("Tutar", H(d, "TotalAmount"))) +

            // Sureyi VURGULU yazıyorum: bu e-postanin tek amaci
            // kullanıcıyı zamaninda ödemeye yonlendirmek.
            $"<p style=\"color:#b45309;\"><strong>Ödeme için " +
            $"{H(d, "ExpiresInMinutes")} dakikaniz var.</strong> " +
            "Sure dolarsa koltuklar serbest birakilir.</p>" +
            Buton($"{_urls.FrontendUrl}/rezervasyonlarim", "Ödemeye devam et"));

    // 4) Ödeme basarili
    //
    // Beklenen: FirstName, EventTitle, Amount, ReservationCode
    private (string, string) PaymentSucceeded(IReadOnlyDictionary<string, string> d)
        => ("Ödemeniz alındı",
            $"<p>Merhaba {H(d, "FirstName")},</p>" +
            "<p>Odemeniz basariyla tamamlandi ve biletleriniz olusturuldu.</p>" +
            Kutu(
                ("Etkinlik", H(d, "EventTitle")),
                ("Rezervasyon kodu", H(d, "ReservationCode")),
                ("Odenen tutar", H(d, "Amount"))) +
            Buton($"{_urls.FrontendUrl}/biletlerim", "Biletlerimi gor"));

    // 5) Bilet bilgileri
    //
    // Beklenen: FirstName, EventTitle, EventDate, VenueName, TicketList
    private (string, string) TicketDetails(IReadOnlyDictionary<string, string> d)
        => ($"Biletleriniz hazır - {H(d, "EventTitle")}",
            $"<p>Merhaba {H(d, "FirstName")},</p>" +
            "<p>Biletleriniz hazir. Girise QR kodunuzu okutmaniz yeterli.</p>" +
            Kutu(
                ("Etkinlik", H(d, "EventTitle")),
                ("Tarih", H(d, "EventDate")),
                ("Mekan", H(d, "VenueName"))) +

            // Bilet listesi ZATEN kacirilmis olarak geliyor (cagiran
            // taraf HTML uretiyor). Burada tekrar kacirmiyoruz --
            // aksi halde "&lt;li&gt;" olarak görünürdü.
            "<div style=\"margin:16px 0;\">" +
            (d.TryGetValue("TicketList", out var liste) ? liste : string.Empty) +
            "</div>" +

            // QR KODU E-POSTAYA GOMULMUYOR -- Sprint 8 karari
            //
            // QR değeri bilet gecerliligini kanitlayan hassas bir veri.
            // E-posta kutusuna dusen bir goruntu, iletildiginde
            // baskasinin biletle girmesine yol acabilir.
            //
            // Kullanıcının giriş yapip kendi ekraninda gormesi daha
            // güvenli.
            Buton($"{_urls.FrontendUrl}/biletlerim", "QR kodlarimi gor"));

    // 6) Etkinlik hatirlatma
    //
    // Beklenen: FirstName, EventTitle, EventDate, VenueName
    private (string, string) EventReminder(IReadOnlyDictionary<string, string> d)
        => ($"Yarin: {H(d, "EventTitle")}",
            $"<p>Merhaba {H(d, "FirstName")},</p>" +
            "<p>Etkinliginiz yaklasiyor. QR kodunuzu yaninizda " +
            "bulundurmayi unutmayin.</p>" +
            Kutu(
                ("Etkinlik", H(d, "EventTitle")),
                ("Tarih", H(d, "EventDate")),
                ("Mekan", H(d, "VenueName"))) +
            Buton($"{_urls.FrontendUrl}/biletlerim", "Biletimi ac"));

    // 7) Etkinlik iptali
    //
    // Beklenen: FirstName, EventTitle, Reason
    private (string, string) EventCancelled(IReadOnlyDictionary<string, string> d)
        => ($"Etkinlik iptal edildi - {H(d, "EventTitle")}",
            $"<p>Merhaba {H(d, "FirstName")},</p>" +

            // Kotu haberi BASTA ve NET veriyorum.
            //
            // Uzun bir girisin ardindan söylemek, kullanıcının
            // e-postayi bastan sona okumasini gerektirirdi ve
            // "ne oldu?" belirsizligi yaratirdi.
            "<p style=\"color:#b91c1c;\"><strong>" +
            $"{H(d, "EventTitle")} etkinligi iptal edildi.</strong></p>" +
            Kutu(("Sebep", H(d, "Reason"))) +

            // Paranin ne olacagi EN COK merak edilen sey; hemen
            // söylemek destek taleplerini azaltiyor.
            "<p>Ödemeniz otomatik olarak iade edilecektir. İade, " +
            "bankaniza bagli olarak 3-10 is gunu icinde hesabiniza " +
            "gecer.</p>" +
            Buton($"{_urls.FrontendUrl}/biletlerim", "Biletlerimi gor"));

    // 8) İade tamamlandi
    //
    // Beklenen: FirstName, ReservationCode, Amount
    // static: bu sablon _urls kullanmiyor (içinde dugme yok).
    // CA1822 bunu yakaladi ve haklı -- örnek verisine erismeyen bir
    // metodu örnek metodu yapmak yanıltıcı.
    private static (string, string) RefundCompleted(IReadOnlyDictionary<string, string> d)
        => ("Iadeniz tamamlandı",
            $"<p>Merhaba {H(d, "FirstName")},</p>" +
            "<p>Iade isleminiz tamamlandi.</p>" +
            Kutu(
                ("Rezervasyon kodu", H(d, "ReservationCode")),
                ("İade tutari", H(d, "Amount"))) +
            "<p style=\"color:#64748b;font-size:13px;\">Tutarin hesabiniza " +
            "gecmesi bankaniza bagli olarak 3-10 is gunu surebilir.</p>");
}
