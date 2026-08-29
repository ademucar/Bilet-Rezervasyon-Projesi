using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Abstractions;
using Ticketing.Application.Abstractions.Email;
using Ticketing.Application.Abstractions.Messaging;
using Ticketing.Application.Abstractions.Persistence;
using Ticketing.Domain.Entities;
using Ticketing.Domain.Enums;

namespace Ticketing.Application.Features.Outbox;

/// <summary>
/// Outbox isleyicileri için ortak yardimcilar.
/// </summary>
internal static class OutboxPayload
{
    /// <summary>
    /// Payload'i cozer. Bozuksa aciklayici bir istisna firlatir.
    /// </summary>
    /// <remarks>
    /// Deserialize null dondurebilir ("null" metni geçerli JSON'dur).
    /// Kontrol etmeseydim isleyicide NullReferenceException alırdım ve
    /// ErrorMessage sutununda "Object reference not set..." yazardi --
    /// hangi mesajin neden bozuldugunu anlamak imkansiz olurdu.
    /// </remarks>
    public static T Parse<T>(string payload)
    {
        var parsed = JsonSerializer.Deserialize<T>(payload);

        return parsed ?? throw new InvalidOperationException(
            $"Outbox payload '{typeof(T).Name}' tipine cozulemedi. Icerik: {payload}");
    }

    /// <summary>
    /// Bu bildirim daha önce olusturulmus mu?
    ///
    /// IDEMPOTENCY'NIN SOMUT UYGULAMASI
    ///
    /// PDF: "Aynı Outbox kaydı iki kez islenmemelidir."
    ///
    /// Outbox "en az bir kez" garantisi verir; aynı mesaj tekrar
    /// islenebilir. Bunu tamamen ONLEMEK yerine ZARARSIZ kiliyorum:
    /// bildirim yazmadan önce aynı turden, aynı varliga bağlı bir
    /// bildirim var mi diye bakiyorum.
    ///
    /// Boylece kullanıcı "biletiniz hazır" bildirimini iki kez
    /// gormuyor.
    /// </summary>
    public static Task<bool> NotificationExistsAsync(
        IApplicationDbContext context,
        Guid userId,
        NotificationType type,
        Guid relatedEntityId,
        CancellationToken cancellationToken)
        => context.Notifications
            .AsNoTracking()
            .AnyAsync(
                n => n.UserId == userId
                  && n.Type == type
                  && n.RelatedEntityId == relatedEntityId,
                cancellationToken);
}

// 1) BİLET SATIN ALINDI -- PDF: "Bilet satin alındı e-postası"

/// <remarks>
/// PDF'IN IKI MADDESI BURADA CAKISIYOR -- VERDIGIM KARAR
///
/// PDF Sprint 9, Outbox senaryolari arasında "QR bilet oluşturma
/// islemi"ni sayiyor. Ama aynı PDF'in Sprint 8 bolumu, ödeme başarılı
/// olduğunda su alti isin TEK BIR SUREC ICINDE calismasini istiyor ve
/// listede "Bilet oluşturma" da var.
///
/// Ikisini birden yapmak mumkun değil: bilet oluşturma islemi tek
/// transaction icindeyse, QR üretimi de oradadir.
/// </remarks>
/// <summary>
/// KARARIM: QR, bilet ile birlikte transaction içinde uretiliyor
/// (Sprint 8 kuralı). Outbox'a birakilan sey QR'in URETIMI değil,
/// TESLIMI -- yani QR'i iceren e-postanin gonderilmesi.
///
/// Gerekce: QR'siz bilet YARIM bir kayittir. Kullanıcı ödemeyi yapip
/// "Biletlerim" ekranına gittiginde QR'i gormek zorunda; arka plan
/// job'inin calismasini beklemesi kabul edilemez. Ayrıca QR üretimi
/// bir dis servise cikmiyor -- birkaç mikrosaniyelik yerel bir hesap.
/// Outbox'in varlik sebebi olan "dis sistem cagrisi" burada yok.
///
/// Yani sapma bilinçli: PDF'in AMACI (kullanıcı isteği dis servis
/// beklemesin) korunuyor, e-posta gonderimi Outbox'a aliniyor.
/// </summary>
internal sealed class TicketsIssuedOutboxHandler : IOutboxMessageHandler
{
    private readonly IApplicationDbContext _context;
    private readonly IEmailService _emailService;
    private readonly IEmailTemplateRenderer _templates;

    public TicketsIssuedOutboxHandler(
        IApplicationDbContext context,
        IEmailService emailService,
        IEmailTemplateRenderer templates)
    {
        _context = context;
        _emailService = emailService;
        _templates = templates;
    }

    public string MessageType => OutboxMessageTypes.TicketsIssued;

    public async Task HandleAsync(string payload, CancellationToken cancellationToken)
    {
        var data = OutboxPayload.Parse<TicketsIssuedPayload>(payload);

        // Biletleri payload'daki Id listesinden DEĞİL, veritabanindan
        // okuyorum.
        //
        // Sebep: payload mesaj yazildigi andaki durumu tasiyor. Aradan
        // gecen surede bilet iptal edilmiş olabilir. İptal edilmiş bir
        // bilet için "biletiniz hazır" e-postası gondermek yanlış olur.
        var tickets = await _context.Tickets
            .AsNoTracking()
            .Where(t => data.TicketIds.Contains(t.Id) && t.Status == TicketStatus.Active)
            .Select(t => new
            {
                t.TicketNumber,
                EventTitle = t.EventSeat.EventSession.Event.Title,
                StartDate = t.EventSeat.EventSession.StartDate,
                VenueName = t.EventSeat.EventSession.Event.Venue.Name,
                SeatLabel = t.EventSeat.Seat.RowLabel + "-" + t.EventSeat.Seat.SeatNumber,
                SectionName = t.EventSeat.Seat.SeatSection.Name,
                Price = t.Price.Amount,
                Currency = t.Price.Currency,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (tickets.Count == 0)
        {
            // Gonderilecek aktif bilet kalmamis (hepsi iptal/iade
            // edilmiş olabilir). Bu bir HATA DEĞİL -- istisna
            // firlatirsak mesaj boşuna 5 kez denenip dead letter olur.
            return;
        }

        var user = await _context.Users
            .AsNoTracking()
            .Where(u => u.Id == data.UserId)
            .Select(u => new { u.Email, u.FirstName })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (user is null)
        {
            throw new InvalidOperationException(
                $"Bilet e-postasi için kullanıcı bulunamadı: {data.UserId}");
        }

        // SPRINT 14: ELLE HTML YERINE SABLON
        //
        // Bu blok onceden StringBuilder ile HTML uretiyordu. Sprint
        // 14'te sablon sistemine tasidim.
        //
        // Kazanc: e-postanin GORUNUMU artık burada değil, tek bir
        // yerde (EmailTemplateRenderer). Alt bilgiye bir satır eklemek
        // gerekseydi sekiz dosya yerine bir dosya degisecek.
        //
        // Burada kalan tek sey VERI hazirlamak -- handler'in isi bu.
        var listeHtml = new StringBuilder(512);
        listeHtml.Append("<ul style=\"margin:0;padding-left:20px;\">");

        foreach (var ticket in tickets)
        {
            // Bilet verilerini BURADA kaciriyorum: bu metin sablona
            // HTML olarak giriyor ve orada tekrar kacirilmiyor.
            listeHtml.Append(
                CultureInfo.InvariantCulture,
                $"<li>{WebUtility.HtmlEncode(ticket.SectionName)} " +
                $"{WebUtility.HtmlEncode(ticket.SeatLabel)} - " +
                $"{ticket.Price} {WebUtility.HtmlEncode(ticket.Currency)} " +
                $"(Bilet no: {WebUtility.HtmlEncode(ticket.TicketNumber)})</li>");
        }

        listeHtml.Append("</ul>");

        var mail = _templates.Render(EmailTemplate.TicketDetails, new Dictionary<string, string>
        {
            ["FirstName"] = user.FirstName,
            ["EventTitle"] = tickets[0].EventTitle,
            ["EventDate"] = tickets[0].StartDate.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture),
            ["VenueName"] = tickets[0].VenueName,
            ["TicketList"] = listeHtml.ToString(),
        });

        await _emailService.SendAsync(
            user.Email,
            mail.Subject,
            mail.HtmlBody,
            cancellationToken).ConfigureAwait(false);
    }
}

// 2) ÖDEME BASARI BILDIRIMI -- PDF: "Ödeme basari bildirimi"

internal sealed class PaymentSucceededOutboxHandler : IOutboxMessageHandler
{
    private readonly IApplicationDbContext _context;

    public PaymentSucceededOutboxHandler(IApplicationDbContext context) => _context = context;

    public string MessageType => OutboxMessageTypes.PaymentSucceeded;

    public async Task HandleAsync(string payload, CancellationToken cancellationToken)
    {
        var data = OutboxPayload.Parse<PaymentSucceededPayload>(payload);

        var exists = await OutboxPayload.NotificationExistsAsync(
            _context, data.UserId, NotificationType.PaymentSucceeded, data.PaymentId, cancellationToken)
            .ConfigureAwait(false);

        if (exists)
        {
            return;   // idempotent
        }

        _context.Notifications.Add(Notification.Create(
            data.UserId,
            NotificationType.PaymentSucceeded,
            "Ödemeniz alındı",
            $"{data.Amount} {data.Currency} tutarindaki ödemeniz basariyla tamamlandı.",
            data.PaymentId,
            "/biletlerim"));

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

// 3) REZERVASYON SURESI DOLDU -- PDF: "Rezervasyon süresi doldu bildirimi"

internal sealed class ReservationExpiredOutboxHandler : IOutboxMessageHandler
{
    private readonly IApplicationDbContext _context;
    private readonly IEmailService _emailService;
    private readonly IAppUrlProvider _urls;

    public ReservationExpiredOutboxHandler(
        IApplicationDbContext context,
        IEmailService emailService,
        IAppUrlProvider urls)
    {
        _context = context;
        _emailService = emailService;
        _urls = urls;
    }

    public string MessageType => OutboxMessageTypes.ReservationExpired;

    public async Task HandleAsync(string payload, CancellationToken cancellationToken)
    {
        var data = OutboxPayload.Parse<ReservationExpiredPayload>(payload);

        var exists = await OutboxPayload.NotificationExistsAsync(
            _context, data.UserId, NotificationType.ReservationExpired, data.ReservationId, cancellationToken)
            .ConfigureAwait(false);

        if (exists)
        {
            return;
        }

        _context.Notifications.Add(Notification.Create(
            data.UserId,
            NotificationType.ReservationExpired,
            "Rezervasyon süresi doldu",
            $"{data.ReservationCode} numarali rezervasyonunuzun ödeme süresi doldu. " +
            $"{data.SeatCount} koltuk serbest birakildi.",
            data.ReservationId,
            "/rezervasyonlarim"));

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var user = await _context.Users
            .AsNoTracking()
            .Where(u => u.Id == data.UserId)
            .Select(u => new { u.Email, u.FirstName })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (user is null)
        {
            return;
        }

        // BILDIRIM ONCE, E-POSTA SONRA -- SIRA ONEMLI
        //
        // E-posta önce gonderilseydi ve SaveChanges başarısız olsaydı,
        // mesaj yeniden denenirdi ve kullanıcı IKINCI bir e-posta
        // alırdı.
        //
        // Bu sırada ise kötü senaryo "bildirim yazildi ama e-posta
        // gitmedi" olur: tekrar denendiginde bildirim zaten var diye
        // atlanir ve e-posta... o da gitmez.
        //
        // Ikisi de kusurlu. E-posta bir HATIRLATMA olduğu ve
        // uygulamadaki bildirim asil kayıt olduğu için, "iki kez
        // e-posta" yerine "e-posta kacirilabilir" tarafini sectim.
        // Kullaniciyi rahatsiz etmemek, ikinci bir kanaldan haber
        // vermekten önemli.
        //
        // Süre dolmasi için PDF'te ayrı bir sablon YOK.
        //
        // "Rezervasyon oluşturuldu" sablonunu kullanmak yanlış olurdu
        // (metni tam tersini söylüyor). Bu yüzden burada kisa ve
        // doğrudan bir mesaj uretiyorum -- sablon sisteminin
        // Layout'unu kullanmadan.
        //
        // Alternatif dokuzuncu bir sablon eklemekti; PDF'in listesine
        // sadik kalmayi tercih ettim ve karari buraya yazdim.
        await _emailService.SendAsync(
            user.Email,
            "Rezervasyon süreniz doldu",
            $"<p>Merhaba {WebUtility.HtmlEncode(user.FirstName)},</p>" +
            $"<p>{WebUtility.HtmlEncode(data.EventTitle)} etkinligi için olusturdugunuz " +
            $"{WebUtility.HtmlEncode(data.ReservationCode)} numarali rezervasyonun ödeme " +
            "suresi doldu ve koltuklariniz serbest birakildi.</p>" +
            $"<p><a href=\"{_urls.FrontendUrl}/etkinlikler\">Tekrar koltuk secmek icin tiklayin</a></p>",
            cancellationToken).ConfigureAwait(false);
    }
}

// 4) ETKİNLİK İPTAL -- PDF: "Etkinlik iptal bildirimi"

/// <summary>
/// Etkinlik iptal edildiginde bilet sahiplerinin HEPSINE bildirim yazar.
/// </summary>
internal sealed class EventCancelledOutboxHandler : IOutboxMessageHandler
{
    private readonly IApplicationDbContext _context;

    public EventCancelledOutboxHandler(IApplicationDbContext context) => _context = context;

    public string MessageType => OutboxMessageTypes.EventCancelled;

    public async Task HandleAsync(string payload, CancellationToken cancellationToken)
    {
        var data = OutboxPayload.Parse<EventCancelledPayload>(payload);

        // Etkilenen kullanıcılar: bu etkinlige AKTIF bileti olanlar.
        //
        // Distinct ŞART: bir kullanıcı 4 bilet almis olabilir ve
        // 4 ayrı bildirim gondermek istemeyiz.
        var userIds = await _context.Tickets
            .AsNoTracking()
            .Where(t => t.EventSeat.EventSession.EventId == data.EventId
                     && t.Status == TicketStatus.Active)
            .Select(t => t.UserId)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (userIds.Count == 0)
        {
            return;
        }

        // Zaten bildirim almis olanlari çıkar (idempotency).
        //
        // Tek sorguda cekiyorum; kullanıcı başına sorgu atsaydim
        // 500 bilet sahibi için 500 gidis donus olurdu.
        var alreadyNotified = await _context.Notifications
            .AsNoTracking()
            .Where(n => n.Type == NotificationType.EventCancelled
                     && n.RelatedEntityId == data.EventId)
            .Select(n => n.UserId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var pending = userIds.Except(alreadyNotified).ToList();

        if (pending.Count == 0)
        {
            return;
        }

        var message = string.IsNullOrWhiteSpace(data.Reason)
            ? $"{data.EventTitle} etkinligi iptal edildi. Ödemeniz iade edilecektir."
            : $"{data.EventTitle} etkinligi iptal edildi. Sebep: {data.Reason}. " +
              "Ödemeniz iade edilecektir.";

        foreach (var userId in pending)
        {
            _context.Notifications.Add(Notification.Create(
                userId,
                NotificationType.EventCancelled,
                "Etkinlik iptal edildi",
                message,
                data.EventId,
                "/biletlerim"));
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

// 5) YAKLASAN ETKİNLİK HATIRLATMASI
//    PDF Background Job: "Yaklasan etkinlik hatirlatmasi"

internal sealed class EventReminderOutboxHandler : IOutboxMessageHandler
{
    private readonly IApplicationDbContext _context;

    public EventReminderOutboxHandler(IApplicationDbContext context) => _context = context;

    public string MessageType => OutboxMessageTypes.EventReminder;

    public async Task HandleAsync(string payload, CancellationToken cancellationToken)
    {
        var data = OutboxPayload.Parse<EventReminderPayload>(payload);

        // RelatedEntityId olarak OTURUM kimligini kullanıyorum,
        // etkinlik kimligini değil.
        //
        // Sebep: bir etkinliğin bes oturumu olabilir ve kullanıcı
        // bunlarin ucune bilet almis olabilir. Etkinlik kimligiyle
        // kontrol etseydim, ilk hatirlatmadan sonra digerleri
        // "zaten gonderilmis" sayilip hiç gitmezdi.
        var exists = await OutboxPayload.NotificationExistsAsync(
            _context, data.UserId, NotificationType.EventReminder, data.EventSessionId, cancellationToken)
            .ConfigureAwait(false);

        if (exists)
        {
            return;
        }

        _context.Notifications.Add(Notification.Create(
            data.UserId,
            NotificationType.EventReminder,
            "Etkinliginiz yaklasiyor",
            $"{data.EventTitle} etkinligi {data.StartDate:dd.MM.yyyy HH:mm} tarihinde " +
            $"{data.VenueName} adresinde başlıyor. QR kodunuzu yaninizda bulundurun.",
            data.EventSessionId,
            "/biletlerim"));

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

// 6) GUNLUK SATIS OZETI -- PDF: "Rapor hazirlama"

/// <summary>
/// Günlük satış ozetini adminlere bildirim olarak yazar.
///
/// Raporun kendisi job içinde HESAPLANIYOR, burada yalnızca TESLIM
/// ediliyor. Boylece rapor üretimi ile dagitimi ayrı ayrı yeniden
/// denenebiliyor: e-posta servisi cokerse rapor kaybolmuyor,
/// payload'da duruyor.
/// </summary>
internal sealed class DailySalesSummaryOutboxHandler : IOutboxMessageHandler
{
    private readonly IApplicationDbContext _context;

    public DailySalesSummaryOutboxHandler(IApplicationDbContext context) => _context = context;

    public string MessageType => OutboxMessageTypes.DailySalesSummary;

    public async Task HandleAsync(string payload, CancellationToken cancellationToken)
    {
        var data = OutboxPayload.Parse<DailySalesSummaryPayload>(payload);

        var adminIds = await _context.UserRoles
            .AsNoTracking()
            .Where(ur => ur.RoleId == Role.Ids.Admin)
            .Select(ur => ur.UserId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (adminIds.Count == 0)
        {
            return;
        }

        var title = $"{data.Date:dd.MM.yyyy} satış özeti";

        var message =
            $"Bilet: {data.TicketCount} adet. " +
            $"Brut: {data.GrossAmount} {data.Currency}. " +
            $"İade: {data.RefundedAmount} {data.Currency}. " +
            $"Net: {data.GrossAmount - data.RefundedAmount} {data.Currency}. " +
            $"Rezervasyon: {data.ReservationCount} (süresi dolan: {data.ExpiredReservationCount}).";

        // Idempotency için aynı gunun raporunun daha önce yazilip
        // yazilmadigina bakiyorum. RelatedEntityId'yi tarihten
        // TURETILMIS deterministik bir Guid ile uretiyorum ki aynı
        // gün için aynı deger ciksin.
        var reportKey = DeterministicGuidFromDate(data.Date);

        var already = await _context.Notifications
            .AsNoTracking()
            .Where(n => n.Type == NotificationType.ReportReady && n.RelatedEntityId == reportKey)
            .Select(n => n.UserId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var adminId in adminIds.Except(already))
        {
            _context.Notifications.Add(Notification.Create(
                adminId,
                NotificationType.ReportReady,
                title,
                message,
                reportKey));
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Tarihten sabit bir Guid üretir: 2026-08-27 -> 20260827-0000-...
    ///
    /// Rastgele Guid kullansaydım idempotency kontrolü HİÇ calismazdi:
    /// her calismada yeni bir anahtar üretilir, "bu rapor zaten var mi"
    /// sorusu her zaman "hayir" cevabini alırdı.
    /// </summary>
    private static Guid DeterministicGuidFromDate(DateOnly date)
    {
        var bytes = new byte[16];
        var value = (date.Year * 10000) + (date.Month * 100) + date.Day;

        BitConverter.TryWriteBytes(bytes, value);

        return new Guid(bytes);
    }
}

// 7) REZERVASYON OLUSTURULDU E-POSTASI
//    PDF Sprint 14 sablonu: "Rezervasyon oluşturuldu"

/// <summary>
/// Rezervasyon olusturuldugunda bilgilendirme e-postası gönderir.
/// </summary>
/// <remarks>
/// UYGULAMA ICI BILDIRIM ILE E-POSTA AYRI YERLERDE -- BILINCLI
///
/// Uygulama ici bildirim, rezervasyonla AYNI transaction'da yaziliyor
/// (CreateReservationCommandHandler içinde). E-posta ise burada,
/// Outbox üzerinden.
///
/// Sebep: bildirim kendi veritabanimiza yaziliyor -- atomik olabilir
/// ve olmalı. E-posta DIS bir servise cikiyor ve yavas olabilir;
/// rezervasyon olusturmayi bekletmemeli.
///
/// Kullanıcı acisindan sonuç: koltuklar anında ayriliyor, e-posta
/// birkaç saniye sonra geliyor. Dogru oncelik.
/// </remarks>
internal sealed class ReservationCreatedOutboxHandler : IOutboxMessageHandler
{
    private readonly IApplicationDbContext _context;
    private readonly IEmailService _emailService;
    private readonly IEmailTemplateRenderer _templates;

    public ReservationCreatedOutboxHandler(
        IApplicationDbContext context,
        IEmailService emailService,
        IEmailTemplateRenderer templates)
    {
        _context = context;
        _emailService = emailService;
        _templates = templates;
    }

    public string MessageType => OutboxMessageTypes.ReservationCreated;

    public async Task HandleAsync(string payload, CancellationToken cancellationToken)
    {
        var data = OutboxPayload.Parse<ReservationCreatedPayload>(payload);

        // Rezervasyonu veritabanindan OKUYORUM, payload'a guvenmiyorum.
        //
        // Aradan gecen surede rezervasyon iptal edilmiş veya odenmis
        // olabilir. "Ödemeyi tamamlayin" e-postası gondermek, zaten
        // odemis bir kullanıcı için kafa karistirici olurdu.
        var rezervasyon = await _context.Reservations
            .AsNoTracking()
            .Where(r => r.Id == data.ReservationId)
            .Select(r => new
            {
                r.Status,
                r.ReservationCode,
                r.TotalAmount,
                EventTitle = r.EventSession.Event.Title,
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        // Rezervasyon artık ödeme beklemiyorsa e-posta GONDERMIYORUZ.
        //
        // Bu bir HATA DEĞİL: istisna firlatirsak mesaj bes kez denenip
        // dead letter olur ve operatoru boşuna mesgul eder.
        if (rezervasyon is null || rezervasyon.Status != ReservationStatus.Locked)
        {
            return;
        }

        var user = await _context.Users
            .AsNoTracking()
            .Where(u => u.Id == data.UserId)
            .Select(u => new { u.Email, u.FirstName })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (user is null)
        {
            throw new InvalidOperationException(
                $"Rezervasyon e-postasi için kullanıcı bulunamadı: {data.UserId}");
        }

        var mail = _templates.Render(EmailTemplate.ReservationCreated, new Dictionary<string, string>
        {
            ["FirstName"] = user.FirstName,
            ["EventTitle"] = rezervasyon.EventTitle,
            ["ReservationCode"] = rezervasyon.ReservationCode,
            ["SeatCount"] = data.SeatCount.ToString(CultureInfo.InvariantCulture),
            ["TotalAmount"] = $"{rezervasyon.TotalAmount.Amount} {rezervasyon.TotalAmount.Currency}",
            ["ExpiresInMinutes"] = data.ExpiresInMinutes.ToString(CultureInfo.InvariantCulture),
        });

        await _emailService.SendAsync(user.Email, mail.Subject, mail.HtmlBody, cancellationToken)
            .ConfigureAwait(false);
    }
}
