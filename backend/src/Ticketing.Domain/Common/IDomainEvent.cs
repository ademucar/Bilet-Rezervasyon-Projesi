namespace Ticketing.Domain.Common;

/// <summary>
/// Domain'de "olup bitmis" bir seyi temsil eder.
///
/// Isimlendirme kuralı: GECMIS ZAMAN kullanilir.
///   Dogru:  ReservationCreated, PaymentSucceeded, EventCancelled
///   Yanlis: CreateReservation, SendEmail
///
/// Sebep: Bir event emir degildir, olmuş bir olayin bildirimidir.
/// "Rezervasyon oluştu" demek, o rezervasyonun ARTIK var olduğunu söyler.
/// Event'i dinleyenler bu bilgiyle ne yapacaklarina kendileri karar verir:
/// biri e-posta gönderir, biri SignalR yayini yapar, biri istatistik günceller.
///
/// Bu ayrim önemli, çünkü entity'nin kimin dinledigini bilmesi gerekmez.
/// Reservation sinifi "e-posta gönder" demez; "ben olustum" der. Yarin
/// SMS de eklersek Reservation sinifina hiç dokunmayiz.
/// </summary>
public interface IDomainEvent
{
    /// <summary>
    /// Olayin gerceklestigi an (UTC).
    /// Outbox'a yazarken ve sıralama yaparken kullanilacak.
    /// </summary>
    DateTimeOffset OccurredOn { get; }
}
