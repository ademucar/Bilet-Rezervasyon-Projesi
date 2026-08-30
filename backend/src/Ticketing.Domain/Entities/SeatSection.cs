using Ticketing.Domain.Common;

namespace Ticketing.Domain.Entities;

/// <summary>
/// Oturma planinin bir bolumu. Ornek: "Orta Blok", "Balkon", "VIP Loca".
/// Bilet türleri (TicketType) bölüm bazinda fiyatlandirilacak --
/// balkon 150 TL, orta blok 400 TL gibi.
/// </summary>
public class SeatSection : AuditableEntity
{
    private SeatSection() => Name = string.Empty;

    private SeatSection(Guid seatLayoutId, string name, int displayOrder)
    {
        SeatLayoutId = seatLayoutId;
        Name = name;
        DisplayOrder = displayOrder;
    }

    public Guid SeatLayoutId { get; private set; }

    public string Name { get; private set; }

    /// <summary>
    /// Frontend'de bolumlerin gosterim sırası (sahneye yakinliktan uzaga).
    /// Alfabetik sıralama yanlış olurdu: "Balkon" ile "Orta Blok" arasindaki
    /// fiziksel iliskiyi harf sırası anlatmaz.
    /// </summary>
    public int DisplayOrder { get; private set; }

    /// <summary>
    /// Koltuk haritasında bolumu ayırt etmek için renk. Ornek: "#E63946".
    /// </summary>
    public string? ColorHex { get; private set; }

    public SeatLayout SeatLayout { get; private set; } = null!;

    private readonly List<Seat> _seats = [];

    public IReadOnlyCollection<Seat> Seats => _seats.AsReadOnly();

    internal static SeatSection Create(Guid seatLayoutId, string name, int displayOrder, string? colorHex)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Bölüm adı boş olamaz.", "seat_section.name_required");
        }

        return new SeatSection(seatLayoutId, name.Trim(), displayOrder) { ColorHex = colorHex };
    }

    /// <summary>
    /// Bolume toplu koltuk üretir.
    ///
    /// PDF Sprint 4: "POST /api/v1/seat-layouts/{id}/generate-seats"
    /// Bir bolumde 20 sıra x 30 koltuk = 600 koltuğu tek tek elle
    /// olusturmak mumkun değil; bu metot o isi yapar.
    /// </summary>
    /// <param name="rowCount">Sıra sayısı. 1'den başlar.</param>
    /// <param name="seatsPerRow">Her siradaki koltuk sayısı.</param>
    /// <param name="rowLabels">
    /// Sıra etiketleri. null ise "1, 2, 3..." kullanilir.
    /// Gerçek salonlarda siralar genelde "A, B, C" diye adlandirilir.
    /// </param>
    public void GenerateSeats(int rowCount, int seatsPerRow, IReadOnlyList<string>? rowLabels = null)
    {
        if (rowCount <= 0 || seatsPerRow <= 0)
        {
            throw new DomainException(
                "Sıra ve koltuk sayısı sıfırdan büyük olmalıdır.",
                "seat_section.invalid_dimensions");
        }

        if (rowLabels is not null && rowLabels.Count != rowCount)
        {
            throw new DomainException(
                $"Sıra etiketi sayısı ({rowLabels.Count}) sıra sayisiyla ({rowCount}) eşleşmiyor.",
                "seat_section.row_label_mismatch");
        }

        // Uretimden ONCE çakışma kontrolü yapıyorum.
        //
        // Neden önce? Çünkü dongu içinde kontrol edip ortada patlarsam
        // koltuklarin YARISI üretilmiş olur. Bellekteki nesne tutarsiz
        // hale gelir. "Ya hep ya hiç" davranisi istiyorum.
        if (_seats.Count > 0)
        {
            throw new DomainException(
                "Bu bolumde zaten koltuk var. Önce mevcut koltukları temizleyin.",
                "seat_section.seats_already_generated");
        }

        for (var row = 1; row <= rowCount; row++)
        {
            var rowLabel = rowLabels?[row - 1] ?? row.ToString(System.Globalization.CultureInfo.InvariantCulture);

            for (var seatNo = 1; seatNo <= seatsPerRow; seatNo++)
            {
                _seats.Add(Seat.Create(Id, rowLabel, seatNo));
            }
        }
    }

    /// <summary>Tek bir koltuk ekler (duzensiz salonlar için).</summary>
    public Seat AddSeat(string rowLabel, int seatNumber)
    {
        // PDF is kuralı: "Aynı bolumde aynı sıra ve koltuk numarasi
        // tekrar edemez." Veritabaninda AYRICA
        // UNIQUE (SeatSectionId, RowLabel, SeatNumber) index'i olacak.
        var varMi = _seats.Exists(s =>
            string.Equals(s.RowLabel, rowLabel, StringComparison.OrdinalIgnoreCase) &&
            s.SeatNumber == seatNumber);

        if (varMi)
        {
            throw new DomainException(
                $"{rowLabel} sırası {seatNumber} numarali koltuk bu bolumde zaten var.",
                "seat_section.duplicate_seat");
        }

        var seat = Seat.Create(Id, rowLabel, seatNumber);
        _seats.Add(seat);

        return seat;
    }
}
