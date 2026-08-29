using Ticketing.Domain.Common;

namespace Ticketing.Domain.Entities;

/// <summary>
/// Şehir. Admin tarafından yonetilir, etkinlik filtrelemesinde kullanilir.
/// Redis'te 24 saat cache'lenecek (bkz. docs/01-is-analizi.md soru 12).
/// </summary>
public class City : AuditableEntity
{
    private City() => Name = string.Empty;

    private City(string name, int plateCode)
    {
        Name = name;
        PlateCode = plateCode;
    }

    public string Name { get; private set; }

    /// <summary>Plaka kodu (1-81). Sıralama ve arama kolayligi için.</summary>
    public int PlateCode { get; private set; }

    public static City Create(string name, int plateCode)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Şehir adı boş olamaz.", "city.name_required");
        }

        if (plateCode is < 1 or > 81)
        {
            throw new DomainException("Plaka kodu 1-81 arasında olmalıdır.", "city.invalid_plate_code");
        }

        return new City(name.Trim(), plateCode);
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Şehir adı boş olamaz.", "city.name_required");
        }

        Name = name.Trim();
    }
}
