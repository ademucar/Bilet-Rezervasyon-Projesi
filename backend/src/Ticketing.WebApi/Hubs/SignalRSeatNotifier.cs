using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Ticketing.Application.Abstractions.RealTime;

namespace Ticketing.WebApi.Hubs;

/// <summary>
/// ISeatNotifier'in SignalR uygulamasi. PDF Sprint 10.
///
/// Application katmanindaki arayuzu burada, WebApi'de karsiliyoruz --
/// çünkü SignalR bir ASP.NET Core teknolojisi ve is mantiginin önü
/// tanimasi gerekmiyor.
/// </summary>
internal sealed partial class SignalRSeatNotifier : ISeatNotifier
{
    // OLAY ADLARI -- PDF Sprint 10'da SAYILAN ADLAR
    //
    // Bu metinler istemcideki `connection.on("SeatLocked", ...)` ile
    // BIREBIR eslesmek zorunda. SignalR eslesmeyen bir olay adını
    // HATA SAYMAZ; mesaj sessizce hiçbir yere gitmez.
    //
    // Sabit olarak yazıyorum ki en azindan sunucu tarafında tek
    // doğru kaynak olsun. Istemci tarafi TypeScript'te aynı adlar
    // yine elle yaziliyor -- Sprint 18'de Swagger/Orval ile
    // uretilecek sozlesmeye dahil edilecek.
    private static class Events
    {
        public const string SeatLocked = "SeatLocked";
        public const string SeatReleased = "SeatReleased";
        public const string SeatSold = "SeatSold";
        public const string ReservationExpired = "ReservationExpired";
        public const string EventCancelled = "EventCancelled";
    }

    private readonly IHubContext<SeatHub> _hub;
    private readonly ILogger<SignalRSeatNotifier> _logger;

    public SignalRSeatNotifier(IHubContext<SeatHub> hub, ILogger<SignalRSeatNotifier> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public Task SeatsLockedAsync(
        Guid eventSessionId,
        IReadOnlyList<Guid> eventSeatIds,
        CancellationToken cancellationToken = default)
        => SendAsync(eventSessionId, Events.SeatLocked, eventSeatIds, cancellationToken);

    public Task SeatsReleasedAsync(
        Guid eventSessionId,
        IReadOnlyList<Guid> eventSeatIds,
        CancellationToken cancellationToken = default)
        => SendAsync(eventSessionId, Events.SeatReleased, eventSeatIds, cancellationToken);

    public Task SeatsSoldAsync(
        Guid eventSessionId,
        IReadOnlyList<Guid> eventSeatIds,
        CancellationToken cancellationToken = default)
        => SendAsync(eventSessionId, Events.SeatSold, eventSeatIds, cancellationToken);

    public Task ReservationExpiredAsync(
        Guid eventSessionId,
        Guid reservationId,
        CancellationToken cancellationToken = default)
        => SafeSendAsync(
            Events.ReservationExpired,
            () => _hub.Clients
                .Group(SeatHub.GroupNameFor(eventSessionId))
                .SendAsync(Events.ReservationExpired, new { reservationId }, cancellationToken));

    public Task EventCancelledAsync(
        IReadOnlyList<Guid> eventSessionIds,
        Guid eventId,
        string eventTitle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventSessionIds);

        if (eventSessionIds.Count == 0)
        {
            return Task.CompletedTask;
        }

        // Bir etkinliğin birden fazla oturumu var; her birinin ayrı
        // grubu. Groups(...) coklu gonderimi TEK cagrida yapiyor --
        // dongu ile tek tek gondermekten daha verimli.
        var groups = eventSessionIds
            .Select(SeatHub.GroupNameFor)
            .ToList();

        return SafeSendAsync(
            Events.EventCancelled,
            () => _hub.Clients
                .Groups(groups)
                .SendAsync(Events.EventCancelled, new { eventId, eventTitle }, cancellationToken));
    }

    private Task SendAsync(
        Guid eventSessionId,
        string eventName,
        IReadOnlyList<Guid> eventSeatIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(eventSeatIds);

        // Boş listeyle mesaj gondermenin anlami yok.
        // Istemci boş bir dizi alip hiçbir sey yapmazdi; sadece
        // ag trafigi ve log gurultusu olurdu.
        if (eventSeatIds.Count == 0)
        {
            return Task.CompletedTask;
        }

        return SafeSendAsync(
            eventName,
            () => _hub.Clients
                .Group(SeatHub.GroupNameFor(eventSessionId))
                .SendAsync(eventName, new { eventSessionId, eventSeatIds }, cancellationToken));
    }

    /// <summary>
    /// BILDIRIM HATASI IS AKISINI ASLA BOZMAMALI
    ///
    /// Bu, bu dosyadaki en önemli karar.
    ///
    /// Bildirim gonderimi rezervasyon olusturmanin SONUNDA cagriliyor
    /// -- veritabani islemi COKTAN commit edilmiş oluyor.
    ///
    /// Eger SignalR bir sebeple hata firlatsaydi (istemci bağlantısı
    /// yarida koptu, bellek baskisi, seri hale getirme hatası) ve biz
    /// bu hatayi yukari birakirsak:
    ///
    ///   - Kullanıcı 500 hatası alırdı
    ///   - AMA REZERVASYONU BASARIYLA OLUSMUS OLURDU
    ///   - Kullanıcı "olmadi" deyip tekrar denerdi
    ///   - Koltuklar zaten kendisinde olduğu için... 409 alırdı
    ///   - Yani KENDİ rezervasyonu yuzunden engellenirdi
    ///
    /// Bu, teshis edilmesi en zor hata turlerinden biri olurdu.
    ///
    /// Kaybedilen sey ise küçük: bir kullanıcının ekrani birkaç saniye
    /// eski kalır. Zaten yedegi var -- istemci yeniden baglandiginda
    /// listeyi bastan cekiyor (PDF: "Güncel koltuk listesini yeniden
    /// çekme").
    ///
    /// Yani hatayi YUTMUYORUZ, logluyorum; ama kullanıcıya
    /// yansitmiyorum.
    /// </summary>
    private async Task SafeSendAsync(string eventName, Func<Task> send)
    {
        try
        {
            await send().ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Genel istisna yakalama
        // CA1031 normalde haklı: beklenmedik hatayi yutmak sorunu gizler.
        //
        // Burada bilinçli olarak susturuyorum. Gerekce yukarida
        // ayrintili yazili: bu bir "en iyi caba" (best-effort)
        // bildirim kanali. Hangi istisnalarin gelebilecegini
        // onceden saymak mumkun değil (ag, seri hale getirme,
        // istemci durumu) ve sayamadigim bir tanesi kullanıcının
        // başarılı islemini hataya cevirirdi.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogNotificationFailed(_logger, eventName, ex);
        }
    }

    [LoggerMessage(
        EventId = 9201,
        Level = LogLevel.Warning,
        Message = "Gerçek zamanlı bildirim gonderilemedi: {EventName}. Is akışı etkilenmedi.")]
    private static partial void LogNotificationFailed(
        ILogger logger, string eventName, Exception exception);
}
