using Ticketing.Domain.Common;

namespace Ticketing.Domain.Entities;

/// <summary>
/// FIZIKSEL koltuk. "Salon A, Orta Blok, C sirasi, 12 numara".
///
/// ------------------------------------------------------------------
/// EN ONEMLI AYRIM: Seat ile EventSeat karistirilmamali
/// ------------------------------------------------------------------
/// Seat      = Fiziksel koltuk. Salon yikilmadikca degismez.
///             Bir kere olusturulur, yillarca ayni kalir.
///
/// EventSeat = O koltugun BELIRLI BIR ETKINLIK OTURUMUNDAKI durumu.
///             "12 Mart konserinde C-12: satilmis, 450 TL, VIP kategorisi"
///
/// Neden iki ayri tablo?
/// Tek tablo olsaydi, ayni salonda iki farkli konser oldugunda ikisinin
/// koltuk durumu birbirine karisirdi. 12 Mart'ta satilan koltuk 15 Mart'ta
/// da satilmis gorunurdu.
///
/// EventSeat kayitlari her oturum icin Seat tablosundan KOPYALANARAK
/// uretilir. 1000 koltuklu salonda 3 oturumlu etkinlik -> 3000 EventSeat.
/// Bu kasitli bir veri cogaltmasidir ve dogrudur: her satirin BAGIMSIZ
/// olarak kilitlenebilmesi gerekiyor. Sprint 7'deki tum concurrency
/// cozumu bu bagimsizliga dayaniyor.
/// ------------------------------------------------------------------
/// </summary>
public class Seat : AuditableEntity
{
    private Seat() => RowLabel = string.Empty;

    private Seat(Guid seatSectionId, string rowLabel, int seatNumber)
    {
        SeatSectionId = seatSectionId;
        RowLabel = rowLabel;
        SeatNumber = seatNumber;
        IsActive = true;
    }

    public Guid SeatSectionId { get; private set; }

    /// <summary>
    /// Sira etiketi: "A", "B", "12"...
    ///
    /// Neden int degil string?
    /// Gercek salonlarda siralar harfle adlandirilir (A, B, C) veya
    /// karma olur (A1, B2, "Loca-3"). int secseydik bu salonlari
    /// modelleyemezdik. Siralama icin ayrica DisplayOrder gerekirse
    /// sonra ekleriz; simdi gereksiz karmasiklik yaratmiyorum.
    /// </summary>
    public string RowLabel { get; private set; }

    public int SeatNumber { get; private set; }

    /// <summary>
    /// Koltuk kullanimda mi?
    ///
    /// PDF Sprint 4: "Koltuk devre disi birakma".
    /// Kirik koltuk, sutun arkasi gorusu kapali koltuk, ses masasi icin
    /// ayrilan yer gibi durumlarda pasife alinir. Silinmez -- cunku
    /// gecmis etkinliklerde o koltuk satilmis olabilir.
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// Koltuk haritasinda gorsel konum. Frontend SVG cizerken kullanacak.
    /// Opsiyonel: duzenli izgara duzenlerde sira/numara bilgisi yeterli,
    /// ama duzensiz salonlarda (yuvarlak amfi, localar) gercek koordinat gerekir.
    /// </summary>
    public int? PositionX { get; private set; }

    public int? PositionY { get; private set; }

    public SeatSection SeatSection { get; private set; } = null!;

    internal static Seat Create(Guid seatSectionId, string rowLabel, int seatNumber)
    {
        if (string.IsNullOrWhiteSpace(rowLabel))
        {
            throw new DomainException("Sira etiketi bos olamaz.", "seat.row_label_required");
        }

        if (seatNumber <= 0)
        {
            throw new DomainException("Koltuk numarasi sifirdan buyuk olmalidir.", "seat.invalid_number");
        }

        return new Seat(seatSectionId, rowLabel.Trim().ToUpperInvariant(), seatNumber);
    }

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;

    public void SetPosition(int x, int y)
    {
        PositionX = x;
        PositionY = y;
    }

    /// <summary>
    /// Kullaniciya gosterilecek okunabilir etiket: "C-12".
    /// Bilet uzerinde ve koltuk haritasinda bu kullanilacak.
    /// Tek yerde tanimladim ki her ekranda ayni formati gorelim.
    /// </summary>
    public string GetDisplayLabel()
        => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{RowLabel}-{SeatNumber}");
}
