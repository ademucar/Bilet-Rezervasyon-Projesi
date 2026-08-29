using Ticketing.Domain.Common;

namespace Ticketing.Domain.Entities;

/// <summary>
/// Oturma planı. Bir salonun belirli bir koltuk düzeni.
///
/// Neden bir salonun birden fazla planı olabilir?
/// Aynı salon konser için sahne önü ayakta + arkası koltuklu,
/// tiyatro için tamamen koltuklu duzenlenebilir. Her duzen ayrı bir plandir.
///
/// PDF is kurallari (sayfa 11):
///   - "Aynı salonda aynı isimde iki oturma planı bulunmamalidir."
///     -> UNIQUE (HallId, Name) index'i ile saglanacak
///   - "Kullanılmış oturma planı fiziksel olarak silinmemelidir."
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
    /// ve "3 yil önceki konserde hangi koltuktaydim" sorusu cevapsiz kalırdı.
    /// </summary>
    public bool IsActive { get; private set; }

    public Hall Hall { get; private set; } = null!;

    private readonly List<SeatSection> _sections = [];

    public IReadOnlyCollection<SeatSection> Sections => _sections.AsReadOnly();

    public static SeatLayout Create(Guid hallId, string name, string? description = null)
    {
        if (hallId == Guid.Empty)
        {
            throw new DomainException("Salon seçilmelidir.", "seat_layout.hall_required");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Plan adı boş olamaz.", "seat_layout.name_required");
        }

        return new SeatLayout(hallId, name.Trim()) { Description = description };
    }

    public SeatSection AddSection(string name, int displayOrder, string? colorHex = null)
    {
        // Aynı plan içinde aynı isimde iki bölüm olamaz.
        //
        // Bu kontrolü BURADA yapıyorum çünkü bölümler zaten bellekte,
        // Sections koleksiyonunda. Veritabanina gitmeden karar verebiliyorum.
        //
        // Ama bu YETMEZ: iki kullanıcı aynı anda bölüm eklerse ikisi de
        // bellekte çakışma gormez. Bu yüzden veritabaninda AYRICA
        // UNIQUE (SeatLayoutId, Name) index'i olacak. Uygulama kontrolü
        // kullanıcıya anlamlı mesaj vermek için, index ise dogruluk için.
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
    /// Plandaki toplam koltuk sayısı.
    ///
    /// Metot, property değil -- çünkü her cagrildiginda tüm bolumleri
    /// dolasip topluyor. Property olsaydı cagiran kişi bunun ucuz bir
    /// alan okumasi olduğunu sanip donguler içinde kullanabilirdi.
    /// </summary>
    public int GetTotalSeatCount() => _sections.Sum(s => s.Seats.Count);

    /// <summary>
    /// PDF: "Koltuk kapasitesi salon kapasitesini asmamalidir."
    ///
    /// hallCapacity'yi PARAMETRE olarak alıyorum, Hall navigation'indan
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
                $"Koltuk sayısı ({toplamKoltuk}) salon kapasitesini ({hallCapacity}) aşamaz.",
                "seat_layout.capacity_exceeded");
        }
    }

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;
}
