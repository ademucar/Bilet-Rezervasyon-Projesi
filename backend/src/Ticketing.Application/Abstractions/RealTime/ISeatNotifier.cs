namespace Ticketing.Application.Abstractions.RealTime;

/// <summary>
/// Koltuk durumu degisikliklerini bağlı istemcilere ANINDA bildirir.
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
///     kırmızı yanardi -- ve haklı olarak
///   - Birim testlerinde bir SignalR sunucusu ayaga kaldirmak
///     gerekirdi
///
/// Bu arayüz sayesinde Application yalnızca "koltuk kilitlendi, ilgili
/// herkese haber ver" diyor. Nasil haber verildigi (SignalR, WebSocket,
/// SSE, hatta hiçbir sey) WebApi katmaninin isi.
/// ==================================================================
///
/// ------------------------------------------------------------------
/// BU BILDIRIMLER NEDEN OUTBOX'A YAZILMIYOR?
/// ------------------------------------------------------------------
/// Sprint 9'da e-posta ve bildirimleri Outbox'a yazdik. Burada AYNISINI
/// YAPMIYORUZ ve bu bilinçli bir ayrim.
///
/// Fark, "kaybolursa ne olur?" sorusunun cevabinda:
///
///   E-POSTA kaybolursa: kullanıcı biletini aldigindan haberi olmaz.
///   Telafisi yok. Bu yüzden KALICI olmalı -> Outbox.
///
///   KOLTUK BILDIRIMI kaybolursa: kullanıcının ekranindaki harita
///   birkaç saniye eski kalır. Zaten yedek mekanizmalar var:
///     - Istemci yeniden baglandiginda listeyi bastan cekiyor
///     - Rezervasyon denemesi sunucuda dogrulaniyor (409)
///   Yani en kötü ihtimalle kullanıcı bir 409 görür.
///
/// Ustelik Outbox'a yazmak GERCEK ZAMANLILIGI BOZARDI: mesaj en fazla
/// 30 saniye sonra islenirdi. "Gerçek zamanlı" diye 30 saniye gecikmeli
/// bir sistem kurmak, amaci tamamen kacirmak olurdu.
///
/// Ozetle: Outbox DAYANIKLILIK için, SignalR HIZ için. Ikisi farklı
/// problemleri cozuyor.
/// ------------------------------------------------------------------
/// </summary>
public interface ISeatNotifier
{
    /// <summary>
    /// PDF olayi: <c>SeatLocked</c>.
    /// Koltuklar bir rezervasyon için kilitlendi.
    /// </summary>
    /// <remarks>
    /// KIMIN kilitledigi GONDERILMIYOR -- yalnızca hangi koltuklar.
    ///
    /// Kullanıcı kimligini yayinlasaydik, oturumu izleyen herkes
    /// "su kişi su koltuğu aldi" bilgisini gorurdu. Bu bir gizlilik
    /// ihlali olurdu ve ekranda hiçbir ise yaramazdi.
    ///
    /// Aynı gerekce GetSeatAvailability sorgusunda da uygulanmisti;
    /// burada tutarli davraniyoruz.
    /// </remarks>
    Task SeatsLockedAsync(
        Guid eventSessionId,
        IReadOnlyList<Guid> eventSeatIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// PDF olayi: <c>SeatReleased</c>.
    /// Koltuklar serbest birakildi (iptal, süre dolmasi veya iade).
    /// </summary>
    Task SeatsReleasedAsync(
        Guid eventSessionId,
        IReadOnlyList<Guid> eventSeatIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// PDF olayi: <c>SeatSold</c>.
    /// Ödeme tamamlandı, koltuklar KALICI olarak satıldı.
    /// </summary>
    /// <remarks>
    /// SeatLocked ile aynı gibi görünüyor ama istemci için FARKLI
    /// anlama geliyor:
    ///
    ///   Locked -> 10 dakika sonra bosalabilir, umut var
    ///   Sold   -> bir daha asla bosalmayacak
    ///
    /// PDF is kuralı: "Satılan koltuk yeniden secilememelidir."
    /// Istemci bu ayrimi bilmeden doğru rengi ve tiklanabilirligi
    /// belirleyemezdi.
    /// </remarks>
    Task SeatsSoldAsync(
        Guid eventSessionId,
        IReadOnlyList<Guid> eventSeatIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// PDF olayi: <c>ReservationExpired</c>.
    /// Bir rezervasyonun süresi doldu.
    /// </summary>
    /// <remarks>
    /// SeatsReleasedAsync ile aynı koltukları kapsiyor ama AYRI bir
    /// olay, çünkü iki farklı izleyicisi var:
    ///
    ///   - Oturumu izleyen HERKES: koltuklar bosaldi (SeatReleased)
    ///   - Rezervasyonun SAHIBI: "süreniz doldu" uyarısı
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
