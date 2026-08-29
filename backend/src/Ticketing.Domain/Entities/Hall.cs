using Ticketing.Domain.Common;

namespace Ticketing.Domain.Entities;

/// <summary>
/// Salon. Bir mekanin icindeki fiziksel salon. Ornek: "Turkcell Sahnesi".
/// Bir salonun birden fazla oturma planı (SeatLayout) olabilir --
/// örneğin konser düzeni ve tiyatro düzeni farklı koltuk yerlesimleri kullanir.
/// </summary>
public class Hall : AuditableEntity
{
    private Hall() => Name = string.Empty;

    private Hall(Guid venueId, string name, int capacity)
    {
        VenueId = venueId;
        Name = name;
        Capacity = capacity;
    }

    public Guid VenueId { get; private set; }

    public string Name { get; private set; }

    /// <summary>
    /// Salonun fiziksel kapasitesi (itfaiye/ruhsat limiti).
    ///
    /// PDF is kuralı: "Koltuk kapasitesi salon kapasitesini asmamalidir."
    /// Yani bir oturma planinda uretilen koltuk sayısı bu değeri gecemez.
    /// Kontrolu SeatLayout.ValidateCapacity metodunda yapacagiz.
    /// </summary>
    public int Capacity { get; private set; }

    public Venue Venue { get; private set; } = null!;

    private readonly List<SeatLayout> _seatLayouts = [];

    public IReadOnlyCollection<SeatLayout> SeatLayouts => _seatLayouts.AsReadOnly();

    public static Hall Create(Guid venueId, string name, int capacity)
    {
        if (venueId == Guid.Empty)
        {
            throw new DomainException("Mekan seçilmelidir.", "hall.venue_required");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Salon adı boş olamaz.", "hall.name_required");
        }

        if (capacity <= 0)
        {
            throw new DomainException("Salon kapasitesi sıfırdan büyük olmalıdır.", "hall.invalid_capacity");
        }

        return new Hall(venueId, name.Trim(), capacity);
    }

    public void Update(string name, int capacity)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Salon adı boş olamaz.", "hall.name_required");
        }

        if (capacity <= 0)
        {
            throw new DomainException("Salon kapasitesi sıfırdan büyük olmalıdır.", "hall.invalid_capacity");
        }

        Name = name.Trim();
        Capacity = capacity;
    }
}
