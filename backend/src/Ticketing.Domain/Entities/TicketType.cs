using Ticketing.Domain.Common;
using Ticketing.Domain.ValueObjects;

namespace Ticketing.Domain.Entities;

/// <summary>
/// Bilet turu ve fiyati. PDF Sprint 6.
/// Ornek: Standard, Student, VIP, EarlyBird, Balcony, FrontStage.
/// </summary>
public class TicketType : AuditableEntity
{
    private TicketType()
    {
        Name = string.Empty;
        Price = Money.Zero("TRY");
    }

    public Guid EventId { get; private set; }

    public string Name { get; private set; }

    /// <summary>
    /// Fiyat. Money value object olarak tutuluyor; veritabaninda
    /// Price_Amount (numeric(18,2)) ve Price_Currency (char(3)) diye
    /// iki sutuna eslenecek (EF Core ComplexProperty).
    /// </summary>
    public Money Price { get; private set; }

    /// <summary>
    /// Kontenjan. null ise sinirsiz (koltuk sayisi kadar).
    ///
    /// PDF: "Kontenjan salon kapasitesini asamaz." Bu kontrol Application
    /// katmaninda yapilacak cunku salon kapasitesi bu entity'de yok.
    /// </summary>
    public int? Quota { get; private set; }

    public DateTimeOffset? SalesStartDate { get; private set; }

    public DateTimeOffset? SalesEndDate { get; private set; }

    public bool IsActive { get; private set; }

    /// <summary>
    /// PDF: "Ogrenci bileti icin dogrulama alani tasarlanmalidir."
    /// true ise satin alma sirasinda ogrenci belgesi numarasi istenecek
    /// ve giriste kontrol edilecek.
    /// </summary>
    public bool RequiresStudentVerification { get; private set; }

    public Event Event { get; private set; } = null!;

    private readonly List<TicketTypeSection> _sections = [];

    /// <summary>Bu bilet turunun kapsadigi oturma plani bolumleri.</summary>
    public IReadOnlyCollection<TicketTypeSection> Sections => _sections.AsReadOnly();

    public static TicketType Create(
        Guid eventId,
        string name,
        Money price,
        int? quota = null,
        bool requiresStudentVerification = false)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Bilet turu adi bos olamaz.", "ticket_type.name_required");
        }

        // PDF: "Fiyat sifirdan kucuk olamaz."
        // Money zaten negatif tutari reddediyor, yani bu kural iki katmanda
        // korunuyor. Money'de olmasi genel kural, burada olmasi ise
        // okuyana bu kuralin bilincli oldugunu gosteriyor.
        if (quota is <= 0)
        {
            throw new DomainException("Kontenjan sifirdan buyuk olmalidir.", "ticket_type.invalid_quota");
        }

        return new TicketType
        {
            EventId = eventId,
            Name = name.Trim(),
            Price = price,
            Quota = quota,
            IsActive = true,
            RequiresStudentVerification = requiresStudentVerification
        };
    }

    /// <summary>
    /// PDF: "Bilet turu satis tarih araligi disinda satin alinamaz."
    /// </summary>
    public bool IsOnSaleAt(DateTimeOffset moment)
    {
        if (!IsActive)
        {
            return false;
        }

        if (SalesStartDate.HasValue && moment < SalesStartDate.Value)
        {
            return false;
        }

        if (SalesEndDate.HasValue && moment > SalesEndDate.Value)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// PDF: "Satis baslamis bilet turunun fiyati degistirilirse degisiklik
    /// loglanmalidir."
    ///
    /// Eski fiyati DONDURUYORUM ki cagiran taraf audit log kaydini
    /// olusturabilsin. Loglamayi burada yapmiyorum: Domain katmani
    /// ILogger'a bagimli olmamali.
    /// </summary>
    public Money ChangePrice(Money newPrice)
    {
        var eskiFiyat = Price;
        Price = newPrice;

        return eskiFiyat;
    }

    public void SetSalesPeriod(DateTimeOffset? start, DateTimeOffset? end)
    {
        if (start.HasValue && end.HasValue && start.Value >= end.Value)
        {
            throw new DomainException(
                "Satis baslangici bitisten once olmalidir.",
                "ticket_type.invalid_sales_period");
        }

        SalesStartDate = start;
        SalesEndDate = end;
    }

    /// <summary>
    /// Bu bilet turune bir bolum atar.
    /// PDF: POST /api/v1/ticket-types/{id}/assign-section
    /// </summary>
    public void AssignSection(Guid seatSectionId)
    {
        // Ayni bolumu iki kez atamayi sessizce yok sayiyorum.
        // "Bu bolumu bu bilet turune ata" istegi idempotent olmali:
        // iki kez cagrilirsa sonuc ayni olmali.
        if (_sections.Exists(s => s.SeatSectionId == seatSectionId))
        {
            return;
        }

        _sections.Add(new TicketTypeSection(Id, seatSectionId));
    }

    public void UnassignSection(Guid seatSectionId)
        => _sections.RemoveAll(s => s.SeatSectionId == seatSectionId);

    /// <summary>
    /// Bilet turunun temel bilgilerini gunceller.
    ///
    /// Fiyat BURADA degismiyor -- onun icin ayri bir metot var
    /// (ChangePrice), cunku fiyat degisikligi LOGLANMAK zorunda.
    /// Ayni metotta olsaydi, "sadece adi degistirdim" durumunda da
    /// gereksiz audit kaydi olusurdu.
    /// </summary>
    public void Update(string name, int? quota, bool requiresStudentVerification)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Bilet turu adi bos olamaz.", "ticket_type.name_required");
        }

        if (quota is <= 0)
        {
            throw new DomainException("Kontenjan sifirdan buyuk olmalidir.", "ticket_type.invalid_quota");
        }

        Name = name.Trim();
        Quota = quota;
        RequiresStudentVerification = requiresStudentVerification;
    }

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;
}
