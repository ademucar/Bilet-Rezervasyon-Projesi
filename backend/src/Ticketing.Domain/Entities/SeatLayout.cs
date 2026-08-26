using Ticketing.Domain.Common;

namespace Ticketing.Domain.Entities;

/// <summary>
/// Oturma plani. Bir salonun belirli bir koltuk duzeni.
///
/// Neden bir salonun birden fazla plani olabilir?
/// Ayni salon konser icin sahne onu ayakta + arkasi koltuklu,
/// tiyatro icin tamamen koltuklu duzenlenebilir. Her duzen ayri bir plandir.
///
/// PDF is kurallari (sayfa 11):
///   - "Ayni salonda ayni isimde iki oturma plani bulunmamalidir."
///     -> UNIQUE (HallId, Name) index'i ile saglanacak
///   - "Kullanilmis oturma plani fiziksel olarak silinmemelidir."
///     -> AuditableEntity'den gelen soft delete ile saglanacak
///   - "Koltuk kapasitesi salon kapasitesini asmamalidir."
///     -> ValidateCapacity metodu ile saglanacak
/// </summary>
public class SeatLayout : AuditableEntity
{
    private SeatLayout() => Name = string.Empty;

    private SeatLayout(Guid hallId, string name)
    {
        HallId = hallId;
        Name = name;
        IsActive = true;
    }

    public Guid HallId { get; private set; }

    public string Name { get; private set; }

    public string? Description { get; private set; }

    /// <summary>
    /// Plan yeni etkinliklerde kullanilabilir mi?
    ///
    /// Silmek yerine pasife almanin sebebi: gecmis etkinlikler bu plana
    /// referans veriyor. Silseydik eski biletlerin koltuk bilgisi kaybolurdu
    /// ve "3 yil onceki konserde hangi koltuktaydim" sorusu cevapsiz kalirdi.
    /// </summary>
    public bool IsActive { get; private set; }

    public Hall Hall { get; private set; } = null!;

    private readonly List<SeatSection> _sections = [];

    public IReadOnlyCollection<SeatSection> Sections => _sections.AsReadOnly();

    public static SeatLayout Create(Guid hallId, string name, string? description = null)
    {
        if (hallId == Guid.Empty)
        {
            throw new DomainException("Salon secilmelidir.", "seat_layout.hall_required");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Plan adi bos olamaz.", "seat_layout.name_required");
        }

        return new SeatLayout(hallId, name.Trim()) { Description = description };
    }

    public SeatSection AddSection(string name, int displayOrder, string? colorHex = null)
    {
        // Ayni plan icinde ayni isimde iki bolum olamaz.
        //
        // Bu kontrolu BURADA yapiyorum cunku bolumler zaten bellekte,
        // Sections koleksiyonunda. Veritabanina gitmeden karar verebiliyorum.
        //
        // Ama bu YETMEZ: iki kullanici ayni anda bolum eklerse ikisi de
        // bellekte cakisma gormez. Bu yuzden veritabaninda AYRICA
        // UNIQUE (SeatLayoutId, Name) index'i olacak. Uygulama kontrolu
        // kullaniciya anlamli mesaj vermek icin, index ise dogruluk icin.
        if (_sections.Exists(s => string.Equals(s.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            throw new DomainException(
                $"'{name}' isimli bolum bu planda zaten var.",
                "seat_layout.duplicate_section");
        }

        var section = SeatSection.Create(Id, name, displayOrder, colorHex);
        _sections.Add(section);

        return section;
    }

    /// <summary>
    /// Plandaki toplam koltuk sayisi.
    ///
    /// Metot, property degil -- cunku her cagrildiginda tum bolumleri
    /// dolasip topluyor. Property olsaydi cagiran kisi bunun ucuz bir
    /// alan okumasi oldugunu sanip donguler icinde kullanabilirdi.
    /// </summary>
    public int GetTotalSeatCount() => _sections.Sum(s => s.Seats.Count);

    /// <summary>
    /// PDF: "Koltuk kapasitesi salon kapasitesini asmamalidir."
    ///
    /// hallCapacity'yi PARAMETRE olarak aliyorum, Hall navigation'indan
    /// okumuyorum. Sebep: Hall her zaman yuklu olmayabilir (Include
    /// edilmemisse null'dur). Parametre olarak almak, cagiran kisiyi
    /// veriyi saglamaya zorlar ve sessiz NullReferenceException riskini
    /// ortadan kaldirir.
    /// </summary>
    public void ValidateCapacity(int hallCapacity)
    {
        var toplamKoltuk = GetTotalSeatCount();

        if (toplamKoltuk > hallCapacity)
        {
            throw new DomainException(
                $"Koltuk sayisi ({toplamKoltuk}) salon kapasitesini ({hallCapacity}) asamaz.",
                "seat_layout.capacity_exceeded");
        }
    }

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;
}
