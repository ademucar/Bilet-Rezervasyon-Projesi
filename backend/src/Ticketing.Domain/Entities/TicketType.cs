using Ticketing.Domain.Common;
using Ticketing.Domain.ValueObjects;

namespace Ticketing.Domain.Entities;

/// <summary>
/// Bilet türü ve fiyati. PDF Sprint 6.
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
    /// Kontenjan. null ise sınırsız (koltuk sayısı kadar).
    ///
    /// PDF: "Kontenjan salon kapasitesini aşamaz." Bu kontrol Application
    /// katmaninda yapilacak çünkü salon kapasitesi bu entity'de yok.
    /// </summary>
    public int? Quota { get; private set; }

    public DateTimeOffset? SalesStartDate { get; private set; }

    public DateTimeOffset? SalesEndDate { get; private set; }

    public bool IsActive { get; private set; }

    /// <summary>
    /// PDF: "Ogrenci bileti için doğrulama alanı tasarlanmalidir."
    /// true ise satin alma sırasında ogrenci belgesi numarasi istenecek
    /// ve girişte kontrol edilecek.
    /// </summary>
    public bool RequiresStudentVerification { get; private set; }

    public Event Event { get; private set; } = null!;

    private readonly List<TicketTypeSection> _sections = [];

    /// <summary>Bu bilet turunun kapsadigi oturma planı bolumleri.</summary>
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
            throw new DomainException("Bilet türü adı boş olamaz.", "ticket_type.name_required");
        }

        // PDF: "Fiyat sıfırdan küçük olamaz."
        // Money zaten negatif tutarı reddediyor, yani bu kural iki katmanda
        // korunuyor. Money'de olmasını genel kural, burada olmasını ise
        // okuyana bu kuralin bilinçli olduğunu gosteriyor.
        if (quota is <= 0)
        {
            throw new DomainException("Kontenjan sıfırdan büyük olmalıdır.", "ticket_type.invalid_quota");
        }

        return new TicketType
        {
            EventId = eventId,
            Name = name.Trim(),
            Price = price,
            Quota = quota,
            IsActive = true,
            RequiresStudentVerification = requiresStudentVerification,
        };
    }

    /// <summary>
    /// PDF: "Bilet türü satış tarih aralığı disinda satin alinamaz."
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
    /// PDF: "Satış baslamis bilet turunun fiyati degistirilirse degisiklik
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
                "Satış baslangici bitisten önce olmalıdır.",
                "ticket_type.invalid_sales_period");
        }

        SalesStartDate = start;
        SalesEndDate = end;
    }

    /// <summary>
    /// Bu bilet turune bir bölüm atar.
    /// PDF: POST /api/v1/ticket-types/{id}/assign-section
    /// </summary>
    public void AssignSection(Guid seatSectionId)
    {
        // Aynı bolumu iki kez atamayi sessizce yok sayiyorum.
        // "Bu bolumu bu bilet turune ata" isteği idempotent olmalı:
        // iki kez cagrilirsa sonuç aynı olmalı.
        if (_sections.Exists(s => s.SeatSectionId == seatSectionId))
        {
            return;
        }

        _sections.Add(new TicketTypeSection(Id, seatSectionId));
    }

    public void UnassignSection(Guid seatSectionId)
        => _sections.RemoveAll(s => s.SeatSectionId == seatSectionId);

    /// <summary>
    /// Bilet turunun temel bilgilerini günceller.
    ///
    /// Fiyat BURADA degismiyor -- onun için ayrı bir metot var
    /// (ChangePrice), çünkü fiyat degisikligi LOGLANMAK zorunda.
    /// Aynı metotta olsaydı, "sadece adı degistirdim" durumunda da
    /// gereksiz audit kaydı olusurdu.
    /// </summary>
    public void Update(string name, int? quota, bool requiresStudentVerification)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Bilet türü adı boş olamaz.", "ticket_type.name_required");
        }

        if (quota is <= 0)
        {
            throw new DomainException("Kontenjan sıfırdan büyük olmalıdır.", "ticket_type.invalid_quota");
        }

        Name = name.Trim();
        Quota = quota;
        RequiresStudentVerification = requiresStudentVerification;
    }

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;
}
