namespace Ticketing.Application.Abstractions.RealTime;

/// <summary>
/// Koltuk durumu degisikliklerini bagli istemcilere ANINDA bildirir.
/// PDF Sprint 10.
///
/// ==================================================================
/// NEDEN ARAYUZ? Application neden SignalR'i tanimiyor?
/// ==================================================================
/// SignalR bir ASP.NET Core teknolojisi. Application katmanina
/// IHubContext enjekte etseydik:
///
///   - Application, Microsoft.AspNetCore.SignalR paketine baglanirdi
///   - Mimari testimiz (Application_AltyapiKatmanlariniReferansAlmamali)
///     kirmizi yanardi -- ve hakli olarak
///   - Birim testlerinde bir SignalR sunucusu ayaga kaldirmak
///     gerekirdi
///
/// Bu arayuz sayesinde Application yalnizca "koltuk kilitlendi, ilgili
/// herkese haber ver" diyor. Nasil haber verildigi (SignalR, WebSocket,
/// SSE, hatta hicbir sey) WebApi katmaninin isi.
/// ==================================================================
///
/// ------------------------------------------------------------------
/// BU BILDIRIMLER NEDEN OUTBOX'A YAZILMIYOR?
/// ------------------------------------------------------------------
/// Sprint 9'da e-posta ve bildirimleri Outbox'a yazdik. Burada AYNISINI
/// YAPMIYORUZ ve bu bilincli bir ayrim.
///
/// Fark, "kaybolursa ne olur?" sorusunun cevabinda:
///
///   E-POSTA kaybolursa: kullanici biletini aldigindan haberi olmaz.
///   Telafisi yok. Bu yuzden KALICI olmali -> Outbox.
///
///   KOLTUK BILDIRIMI kaybolursa: kullanicinin ekranindaki harita
///   birkac saniye eski kalir. Zaten yedek mekanizmalar var:
///     - Istemci yeniden baglandiginda listeyi bastan cekiyor
///     - Rezervasyon denemesi sunucuda dogrulaniyor (409)
///   Yani en kotu ihtimalle kullanici bir 409 gorur.
///
/// Ustelik Outbox'a yazmak GERCEK ZAMANLILIGI BOZARDI: mesaj en fazla
/// 30 saniye sonra islenirdi. "Gercek zamanli" diye 30 saniye gecikmeli
/// bir sistem kurmak, amaci tamamen kacirmak olurdu.
///
/// Ozetle: Outbox DAYANIKLILIK icin, SignalR HIZ icin. Ikisi farkli
/// problemleri cozuyor.
/// ------------------------------------------------------------------
/// </summary>
public interface ISeatNotifier
{
    /// <summary>
    /// PDF olayi: <c>SeatLocked</c>.
    /// Koltuklar bir rezervasyon icin kilitlendi.
    /// </summary>
    /// <remarks>
    /// KIMIN kilitledigi GONDERILMIYOR -- yalnizca hangi koltuklar.
    ///
    /// Kullanici kimligini yayinlasaydik, oturumu izleyen herkes
    /// "su kisi su koltugu aldi" bilgisini gorurdu. Bu bir gizlilik
    /// ihlali olurdu ve ekranda hicbir ise yaramazdi.
    ///
    /// Ayni gerekce GetSeatAvailability sorgusunda da uygulanmisti;
    /// burada tutarli davraniyoruz.
    /// </remarks>
    Task SeatsLockedAsync(
        Guid eventSessionId,
        IReadOnlyList<Guid> eventSeatIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// PDF olayi: <c>SeatReleased</c>.
    /// Koltuklar serbest birakildi (iptal, sure dolmasi veya iade).
    /// </summary>
    Task SeatsReleasedAsync(
        Guid eventSessionId,
        IReadOnlyList<Guid> eventSeatIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// PDF olayi: <c>SeatSold</c>.
    /// Odeme tamamlandi, koltuklar KALICI olarak satildi.
    /// </summary>
    /// <remarks>
    /// SeatLocked ile ayni gibi gorunuyor ama istemci icin FARKLI
    /// anlama geliyor:
    ///
    ///   Locked -> 10 dakika sonra bosalabilir, umut var
    ///   Sold   -> bir daha asla bosalmayacak
    ///
    /// PDF is kurali: "Satilan koltuk yeniden secilememelidir."
    /// Istemci bu ayrimi bilmeden dogru rengi ve tiklanabilirligi
    /// belirleyemezdi.
    /// </remarks>
    Task SeatsSoldAsync(
        Guid eventSessionId,
        IReadOnlyList<Guid> eventSeatIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// PDF olayi: <c>ReservationExpired</c>.
    /// Bir rezervasyonun suresi doldu.
    /// </summary>
    /// <remarks>
    /// SeatsReleasedAsync ile ayni koltuklari kapsiyor ama AYRI bir
    /// olay, cunku iki farkli izleyicisi var:
    ///
    ///   - Oturumu izleyen HERKES: koltuklar bosaldi (SeatReleased)
    ///   - Rezervasyonun SAHIBI: "sureniz doldu" uyarisi
    ///
    /// Ikisini tek olayda birlestirseydik, sahibi kendi
    /// rezervasyonunun mu yoksa baskasininkinin mi bittigini
    /// anlayamazdi.
    /// </remarks>
    Task ReservationExpiredAsync(
        Guid eventSessionId,
        Guid reservationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// PDF olayi: <c>EventCancelled</c>.
    /// Etkinlik iptal edildi; oturumlarini izleyenler haberdar edilir.
    /// </summary>
    Task EventCancelledAsync(
        IReadOnlyList<Guid> eventSessionIds,
        Guid eventId,
        string eventTitle,
        CancellationToken cancellationToken = default);
}
