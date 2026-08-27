using Ticketing.Domain.Common;

namespace Ticketing.Domain.Entities;

/// <summary>
/// Mekan (bina). Ornek: "Zorlu PSM", "Kadikoy Sahnesi".
/// Bir mekanin icinde birden fazla salon (Hall) olabilir.
///
/// PDF: Mekan/salon yonetimi ADMIN'in sorumlulugundadir, organizatorun degil.
/// Sebep: Salonlar fiziksel gercekliktir. Organizator sadece var olan bir
/// salonu belirli bir tarih araligi icin secer. Bu ayrim olmasaydi
/// "ayni salon ayni saatte iki etkinlige atanamaz" kuralini uygulayamazdik --
/// herkes kendi salonunu tanimlardi ve cakisma tespiti anlamsizlasirdi.
/// </summary>
public class Venue : AuditableEntity
{
    private Venue()
    {
        Name = string.Empty;
        Address = string.Empty;
    }

    private Venue(Guid cityId, string name, string address)
    {
        CityId = cityId;
        Name = name;
        Address = address;
    }

    public Guid CityId { get; private set; }

    public string Name { get; private set; }

    public string Address { get; private set; }

    /// <summary>Harita gosterimi icin. Ikisi de opsiyonel.</summary>
    public decimal? Latitude { get; private set; }

    public decimal? Longitude { get; private set; }

    public City City { get; private set; } = null!;

    private readonly List<Hall> _halls = [];

    public IReadOnlyCollection<Hall> Halls => _halls.AsReadOnly();

    public static Venue Create(Guid cityId, string name, string address)
    {
        if (cityId == Guid.Empty)
        {
            throw new DomainException("Sehir secilmelidir.", "venue.city_required");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Mekan adi bos olamaz.", "venue.name_required");
        }

        if (string.IsNullOrWhiteSpace(address))
        {
            throw new DomainException("Adres bos olamaz.", "venue.address_required");
        }

        return new Venue(cityId, name.Trim(), address.Trim());
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Mekan adi bos olamaz.", "venue.name_required");
        }

        Name = name.Trim();
    }

    public void UpdateAddress(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            throw new DomainException("Adres bos olamaz.", "venue.address_required");
        }

        Address = address.Trim();
    }

    public void SetCoordinates(decimal latitude, decimal longitude)
    {
        // Enlem -90..90, boylam -180..180 araliginda olmalidir.
        // Bu kontrolu koymamin sebebi: frontend'den yanlislikla ters
        // gonderilen koordinatlar (lat/lng yer degistirmis) haritada
        // mekani okyanusun ortasinda gosterir ve kimse sebebini anlamaz.
        if (latitude is < -90 or > 90)
        {
            throw new DomainException("Enlem -90 ile 90 arasinda olmalidir.", "venue.invalid_latitude");
        }

        if (longitude is < -180 or > 180)
        {
            throw new DomainException("Boylam -180 ile 180 arasinda olmalidir.", "venue.invalid_longitude");
        }

        Latitude = latitude;
        Longitude = longitude;
    }
}
