using System.Globalization;
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
/// Outbox isleyicileri icin ortak yardimcilar.
/// </summary>
internal static class OutboxPayload
{
    /// <summary>
    /// Payload'i cozer. Bozuksa aciklayici bir istisna firlatir.
    /// </summary>
    /// <remarks>
    /// Deserialize null dondurebilir ("null" metni gecerli JSON'dur).
    /// Kontrol etmeseydik isleyicide NullReferenceException alirdik ve
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
    /// Bu bildirim daha once olusturulmus mu?
    ///
    /// ==============================================================
    /// IDEMPOTENCY'NIN SOMUT UYGULAMASI
    /// ==============================================================
    /// PDF: "Ayni Outbox kaydi iki kez islenmemelidir."
    ///
    /// Outbox "en az bir kez" garantisi verir; ayni mesaj tekrar
    /// islenebilir. Bunu tamamen ONLEMEK yerine ZARARSIZ kiliyoruz:
    /// bildirim yazmadan once ayni turden, ayni varliga bagli bir
    /// bildirim var mi diye bakiyoruz.
    ///
    /// Boylece kullanici "biletiniz hazir" bildirimini iki kez
    /// gormuyor.
    /// ==============================================================
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

// ===================================================================
// 1) BILET SATIN ALINDI -- PDF: "Bilet satin alindi e-postasi"
// ===================================================================

/// <remarks>
/// ==================================================================
/// PDF'IN IKI MADDESI BURADA CAKISIYOR -- VERDIGIM KARAR
/// ==================================================================
/// PDF Sprint 9, Outbox senaryolari arasinda "QR bilet olusturma
/// islemi"ni sayiyor. Ama ayni PDF'in Sprint 8 bolumu, odeme basarili
/// oldugunda su alti isin TEK BIR SUREC ICINDE calismasini istiyor ve
/// listede "Bilet olusturma" da var.
///
/// Ikisini birden yapmak mumkun degil: bilet olusturma islemi tek
/// transaction icindeyse, QR uretimi de oradadir.
/// </remarks>
/// <summary>
/// KARARIM: QR, bilet ile birlikte transaction icinde uretiliyor
/// (Sprint 8 kurali). Outbox'a birakilan sey QR'in URETIMI degil,
/// TESLIMI -- yani QR'i iceren e-postanin gonderilmesi.
///
/// Gerekce: QR'siz bilet YARIM bir kayittir. Kullanici odemeyi yapip
/// "Biletlerim" ekranina gittiginde QR'i gormek zorunda; arka plan
/// job'inin calismasini beklemesi kabul edilemez. Ayrica QR uretimi
/// bir dis servise cikmiyor -- birkac mikrosaniyelik yerel bir hesap.
/// Outbox'in varlik sebebi olan "dis sistem cagrisi" burada yok.
///
/// Yani sapma bilincli: PDF'in AMACI (kullanici istegi dis servis
/// beklemesin) korunuyor, e-posta gonderimi Outbox'a aliniyor.
/// </summary>
internal sealed class TicketsIssuedOutboxHandler : IOutboxMessageHandler
{
    private readonly IApplicationDbContext _context;
    private readonly IEmailService _emailService;
    private readonly IAppUrlProvider _urls;

    public TicketsIssuedOutboxHandler(
        IApplicationDbContext context,
        IEmailService emailService,
        IAppUrlProvider urls)
    {
        _context = context;
        _emailService = emailService;
        _urls = urls;
    }

    public string MessageType => OutboxMessageTypes.TicketsIssued;

    public async Task HandleAsync(string payload, CancellationToken cancellationToken)
    {
        var data = OutboxPayload.Parse<TicketsIssuedPayload>(payload);

        // Biletleri payload'daki Id listesinden DEGIL, veritabanindan
        // okuyorum.
        //
        // Sebep: payload mesaj yazildigi andaki durumu tasiyor. Aradan
        // gecen surede bilet iptal edilmis olabilir. Iptal edilmis bir
        // bilet icin "biletiniz hazir" e-postasi gondermek yanlis olur.
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
                Currency = t.Price.Currency
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (tickets.Count == 0)
        {
            // Gonderilecek aktif bilet kalmamis (hepsi iptal/iade
            // edilmis olabilir). Bu bir HATA DEGIL -- istisna
            // firlatirsak mesaj bosuna 5 kez denenip dead letter olur.
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
                $"Bilet e-postasi icin kullanici bulunamadi: {data.UserId}");
        }

        var body = new StringBuilder(1024);
        body.Append(CultureInfo.InvariantCulture, $"<p>Merhaba {user.FirstName},</p>");
        body.Append("<p>Odemeniz alindi ve biletleriniz hazir.</p>");
        body.Append(CultureInfo.InvariantCulture, $"<h3>{tickets[0].EventTitle}</h3>");
        body.Append(CultureInfo.InvariantCulture,
            $"<p>{tickets[0].StartDate:dd.MM.yyyy HH:mm} - {tickets[0].VenueName}</p>");
        body.Append("<ul>");

        foreach (var ticket in tickets)
        {
            body.Append(CultureInfo.InvariantCulture,
                $"<li>{ticket.SectionName} {ticket.SeatLabel} - {ticket.Price} {ticket.Currency} " +
                $"(Bilet no: {ticket.TicketNumber})</li>");
        }

        body.Append("</ul>");

        // QR kodunu e-postaya GOMMUYORUZ, adres veriyoruz.
        //
        // Sebep: QR degeri bilet gecerliligini kaniltlayan hassas bir
        // veri. E-posta kutusuna dusen bir goruntu, iletildiginde
        // baskasinin biletle girmesine yol acabilir. Kullanicinin
        // giris yapip kendi ekraninda gormesi daha guvenli.
        body.Append(CultureInfo.InvariantCulture,
            $"<p>QR kodlariniz icin: <a href=\"{_urls.FrontendUrl}/biletlerim\">Biletlerim</a></p>");

        await _emailService.SendAsync(
            user.Email,
            $"Biletleriniz hazir - {tickets[0].EventTitle}",
            body.ToString(),
            cancellationToken).ConfigureAwait(false);
    }
}

// ===================================================================
// 2) ODEME BASARI BILDIRIMI -- PDF: "Odeme basari bildirimi"
// ===================================================================

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
            "Odemeniz alindi",
            $"{data.Amount} {data.Currency} tutarindaki odemeniz basariyla tamamlandi.",
            data.PaymentId,
            "/biletlerim"));

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

// ===================================================================
// 3) REZERVASYON SURESI DOLDU -- PDF: "Rezervasyon suresi doldu bildirimi"
// ===================================================================

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
            "Rezervasyon suresi doldu",
            $"{data.ReservationCode} numarali rezervasyonunuzun odeme suresi doldu. " +
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

        // ==============================================================
        // BILDIRIM ONCE, E-POSTA SONRA -- SIRA ONEMLI
        // ==============================================================
        // E-posta once gonderilseydi ve SaveChanges basarisiz olsaydi,
        // mesaj yeniden denenirdi ve kullanici IKINCI bir e-posta
        // alirdi.
        //
        // Bu sirada ise kotu senaryo "bildirim yazildi ama e-posta
        // gitmedi" olur: tekrar denendiginde bildirim zaten var diye
        // atlanir ve e-posta... o da gitmez.
        //
        // Ikisi de kusurlu. E-posta bir HATIRLATMA oldugu ve
        // uygulamadaki bildirim asil kayit oldugu icin, "iki kez
        // e-posta" yerine "e-posta kacirilabilir" tarafini sectim.
        // Kullaniciyi rahatsiz etmemek, ikinci bir kanaldan haber
        // vermekten onemli.
        // ==============================================================
        await _emailService.SendAsync(
            user.Email,
            "Rezervasyon sureniz doldu",
            $"<p>Merhaba {user.FirstName},</p>" +
            $"<p>{data.EventTitle} etkinligi icin olusturdugunuz " +
            $"{data.ReservationCode} numarali rezervasyonun odeme suresi doldu " +
            "ve koltuklariniz serbest birakildi.</p>" +
            $"<p><a href=\"{_urls.FrontendUrl}/etkinlikler\">Tekrar koltuk secmek icin tiklayin</a></p>",
            cancellationToken).ConfigureAwait(false);
    }
}

// ===================================================================
// 4) ETKINLIK IPTAL -- PDF: "Etkinlik iptal bildirimi"
// ===================================================================

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

        // Etkilenen kullanicilar: bu etkinlige AKTIF bileti olanlar.
        //
        // Distinct SART: bir kullanici 4 bilet almis olabilir ve
        // 4 ayri bildirim gondermek istemeyiz.
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

        // Zaten bildirim almis olanlari cikar (idempotency).
        //
        // Tek sorguda cekiyorum; kullanici basina sorgu atsaydik
        // 500 bilet sahibi icin 500 gidis donus olurdu.
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
            ? $"{data.EventTitle} etkinligi iptal edildi. Odemeniz iade edilecektir."
            : $"{data.EventTitle} etkinligi iptal edildi. Sebep: {data.Reason}. " +
              "Odemeniz iade edilecektir.";

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

// ===================================================================
// 5) YAKLASAN ETKINLIK HATIRLATMASI
//    PDF Background Job: "Yaklasan etkinlik hatirlatmasi"
// ===================================================================

internal sealed class EventReminderOutboxHandler : IOutboxMessageHandler
{
    private readonly IApplicationDbContext _context;

    public EventReminderOutboxHandler(IApplicationDbContext context) => _context = context;

    public string MessageType => OutboxMessageTypes.EventReminder;

    public async Task HandleAsync(string payload, CancellationToken cancellationToken)
    {
        var data = OutboxPayload.Parse<EventReminderPayload>(payload);

        // RelatedEntityId olarak OTURUM kimligini kullaniyorum,
        // etkinlik kimligini degil.
        //
        // Sebep: bir etkinligin bes oturumu olabilir ve kullanici
        // bunlarin ucune bilet almis olabilir. Etkinlik kimligiyle
        // kontrol etseydik, ilk hatirlatmadan sonra digerleri
        // "zaten gonderilmis" sayilip hic gitmezdi.
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
            $"{data.VenueName} adresinde basliyor. QR kodunuzu yaninizda bulundurun.",
            data.EventSessionId,
            "/biletlerim"));

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

// ===================================================================
// 6) GUNLUK SATIS OZETI -- PDF: "Rapor hazirlama"
// ===================================================================

/// <summary>
/// Gunluk satis ozetini adminlere bildirim olarak yazar.
///
/// Raporun kendisi job icinde HESAPLANIYOR, burada yalnizca TESLIM
/// ediliyor. Boylece rapor uretimi ile dagitimi ayri ayri yeniden
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

        var title = $"{data.Date:dd.MM.yyyy} satis ozeti";

        var message =
            $"Bilet: {data.TicketCount} adet. " +
            $"Brut: {data.GrossAmount} {data.Currency}. " +
            $"Iade: {data.RefundedAmount} {data.Currency}. " +
            $"Net: {data.GrossAmount - data.RefundedAmount} {data.Currency}. " +
            $"Rezervasyon: {data.ReservationCount} (suresi dolan: {data.ExpiredReservationCount}).";

        // Idempotency icin ayni gunun raporunun daha once yazilip
        // yazilmadigina bakiyorum. RelatedEntityId'yi tarihten
        // TURETILMIS deterministik bir Guid ile uretiyorum ki ayni
        // gun icin ayni deger ciksin.
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
    /// Tarihten sabit bir Guid uretir: 2026-08-27 -> 20260827-0000-...
    ///
    /// Rastgele Guid kullansaydik idempotency kontrolu HIC calismazdi:
    /// her calismada yeni bir anahtar uretilir, "bu rapor zaten var mi"
    /// sorusu her zaman "hayir" cevabini alirdi.
    /// </summary>
    private static Guid DeterministicGuidFromDate(DateOnly date)
    {
        var bytes = new byte[16];
        var value = (date.Year * 10000) + (date.Month * 100) + date.Day;

        BitConverter.TryWriteBytes(bytes, value);

        return new Guid(bytes);
    }
}
