using Ticketing.Domain.Common;

namespace Ticketing.Domain.Entities;

/// <summary>
/// FIZIKSEL koltuk. "Salon A, Orta Blok, C sırası, 12 numara".
///
/// EN ONEMLI AYRIM: Seat ile EventSeat karistirilmamali
///
/// Seat      = Fiziksel koltuk. Salon yikilmadikca degismez.
///             Bir kere olusturulur, yillarca aynı kalır.
///
/// EventSeat = O koltuğun BELIRLI BIR ETKİNLİK OTURUMUNDAKI durumu.
///             "12 Mart konserinde C-12: satılmış, 450 TL, VIP kategorisi"
///
/// Neden iki ayrı tablo?
/// Tek tablo olsaydı, aynı salonda iki farklı konser olduğunda ikisinin
/// koltuk durumu birbirine karisirdi. 12 Mart'ta satılan koltuk 15 Mart'ta
/// da satılmış görünürdü.
///
/// EventSeat kayitlari her oturum için Seat tablosundan KOPYALANARAK
/// üretilir. 1000 koltuklu salonda 3 oturumlu etkinlik -> 3000 EventSeat.
/// Bu kasitli bir veri cogaltmasidir ve dogrudur: her satirin BAGIMSIZ
/// olarak kilitlenebilmesi gerekiyor. Sprint 7'deki tüm concurrency
/// cozumu bu bagimsizliga dayaniyor.
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
    /// Sıra etiketi: "A", "B", "12"...
    ///
    /// Neden int değil string?
    /// Gerçek salonlarda siralar harfle adlandirilir (A, B, C) veya
    /// karma olur (A1, B2, "Loca-3"). int secseydik bu salonlari
    /// modelleyemezdim. Sıralama için ayrıca DisplayOrder gerekirse
    /// sonra ekleriz; simdi gereksiz karmasiklik yaratmiyorum.
    /// </summary>
    public string RowLabel { get; private set; }

    public int SeatNumber { get; private set; }

    /// <summary>
    /// Koltuk kullanımda mi?
    ///
    /// PDF Sprint 4: "Koltuk devre dışı birakma".
    /// Kirik koltuk, sutun arkası gorusu kapalı koltuk, ses masasi için
    /// ayrilan yer gibi durumlarda pasife alinir. Silinmez -- çünkü
    /// gecmis etkinliklerde o koltuk satılmış olabilir.
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// Koltuk haritasında görsel konum. Frontend SVG cizerken kullanacak.
    /// Opsiyonel: duzenli izgara duzenlerde sıra/numara bilgisi yeterli,
    /// ama duzensiz salonlarda (yuvarlak amfi, localar) gerçek koordinat gerekir.
    /// </summary>
    public int? PositionX { get; private set; }

    public int? PositionY { get; private set; }

    public SeatSection SeatSection { get; private set; } = null!;

    internal static Seat Create(Guid seatSectionId, string rowLabel, int seatNumber)
    {
        if (string.IsNullOrWhiteSpace(rowLabel))
        {
            throw new DomainException("Sıra etiketi boş olamaz.", "seat.row_label_required");
        }

        if (seatNumber <= 0)
        {
            throw new DomainException("Koltuk numarasi sıfırdan büyük olmalıdır.", "seat.invalid_number");
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
    /// Kullanıcıya gösterilecek okunabilir etiket: "C-12".
    /// Bilet uzerinde ve koltuk haritasında bu kullanilacak.
    /// Tek yerde tanimladim ki her ekranda aynı formati goreyim.
    /// </summary>
    public string GetDisplayLabel()
        => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{RowLabel}-{SeatNumber}");
}
