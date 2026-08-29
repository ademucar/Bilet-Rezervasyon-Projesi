namespace Ticketing.Domain.Enums;

/// <summary>
/// Bir koltuğun BELIRLI BIR ETKİNLİK OTURUMUNDAKI durumu.
///
/// Bu enum PDF'te acikca listelenmiyor ama EventSeats tablosu isteniyor.
/// Koltugun oturum bazindaki durumunu tutan bir alan olmadan koltuk
/// haritasini cizmek imkansiz. Bu yüzden ekledim.
///
/// DIKKAT: Bu, Seat (fiziksel koltuk) ile karistirilmamali.
///   Seat      = "Salon A, B bolumu, 5. sıra, 12 numara" -- salon yikilmadikca degismez
///   EventSeat = "12 Mart konserinde o koltuk: satılmış, 450 TL, VIP"
/// </summary>
public enum EventSeatStatus
{
    /// <summary>Satin alinabilir.</summary>
    Available = 1,

    /// <summary>Bir rezervasyon tarafından geçici kilitli. LockedUntil'e bak.</summary>
    Locked = 2,

    /// <summary>Ödemesi tamamlanmis, satılmış. Geri donusu iade ile olur.</summary>
    Sold = 3,

    /// <summary>
    /// Organizatör/admin satışa kapatti.
    /// Gerçek salonlarda ses-isik masasi, engelli erişim koridoru gibi
    /// sebeplerle satışa kapatilan koltuklar olur.
    /// </summary>
    Blocked = 4,
}
