namespace Ticketing.Domain.Enums;

/// <summary>
/// Bir koltugun BELIRLI BIR ETKINLIK OTURUMUNDAKI durumu.
///
/// Bu enum PDF'te acikca listelenmiyor ama EventSeats tablosu isteniyor.
/// Koltugun oturum bazindaki durumunu tutan bir alan olmadan koltuk
/// haritasini cizmek imkansiz. Bu yuzden ekledim.
///
/// DIKKAT: Bu, Seat (fiziksel koltuk) ile karistirilmamali.
///   Seat      = "Salon A, B bolumu, 5. sira, 12 numara" -- salon yikilmadikca degismez
///   EventSeat = "12 Mart konserinde o koltuk: satilmis, 450 TL, VIP"
/// </summary>
public enum EventSeatStatus
{
    /// <summary>Satin alinabilir.</summary>
    Available = 1,

    /// <summary>Bir rezervasyon tarafindan gecici kilitli. LockedUntil'e bak.</summary>
    Locked = 2,

    /// <summary>Odemesi tamamlanmis, satilmis. Geri donusu iade ile olur.</summary>
    Sold = 3,

    /// <summary>
    /// Organizator/admin satisa kapatti.
    /// Gercek salonlarda ses-isik masasi, engelli erisim koridoru gibi
    /// sebeplerle satisa kapatilan koltuklar olur.
    /// </summary>
    Blocked = 4,
}
