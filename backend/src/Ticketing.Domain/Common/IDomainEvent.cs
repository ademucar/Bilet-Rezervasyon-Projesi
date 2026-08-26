namespace Ticketing.Domain.Common;

/// <summary>
/// Domain'de "olup bitmis" bir seyi temsil eder.
///
/// Isimlendirme kurali: GECMIS ZAMAN kullanilir.
///   Dogru:  ReservationCreated, PaymentSucceeded, EventCancelled
///   Yanlis: CreateReservation, SendEmail
///
/// Sebep: Bir event emir degildir, olmus bir olayin bildirimidir.
/// "Rezervasyon olustu" demek, o rezervasyonun ARTIK var oldugunu soyler.
/// Event'i dinleyenler bu bilgiyle ne yapacaklarina kendileri karar verir:
/// biri e-posta gonderir, biri SignalR yayini yapar, biri istatistik gunceller.
///
/// Bu ayrim onemli, cunku entity'nin kimin dinledigini bilmesi gerekmez.
/// Reservation sinifi "e-posta gonder" demez; "ben olustum" der. Yarin
/// SMS de eklersek Reservation sinifina hic dokunmayiz.
/// </summary>
public interface IDomainEvent
{
    /// <summary>
    /// Olayin gerceklestigi an (UTC).
    /// Outbox'a yazarken ve siralama yaparken kullanilacak.
    /// </summary>
    DateTimeOffset OccurredOn { get; }
}
