using Ticketing.Domain.Common;

namespace Ticketing.Domain.Entities;

/// <summary>
/// Organizator profili. Bir kullanicinin organizator olarak ek bilgileri.
///
/// Neden User tablosuna sutun eklemedik?
/// Cunku bu alanlar kullanicilarin YALNIZCA KUCUK BIR KISMI icin doludur.
/// 100.000 kullanicidan 50'si organizatorse, User tablosunda 99.950 satirda
/// bos duran 8 sutun olurdu. Ayri tablo hem yer tasarrufu saglar hem de
/// "bu kullanici organizator mu?" sorusunu netlestirir.
///
/// User ile 1-1 iliski: UserId hem PK hem FK.
/// </summary>
public class OrganizerProfile : AuditableEntity
{
    private OrganizerProfile()
    {
        CompanyName = string.Empty;
        ContactEmail = string.Empty;
    }

    public Guid UserId { get; private set; }

    public string CompanyName { get; private set; }

    /// <summary>Vergi numarasi veya TC kimlik no (sahis firmasi ise).</summary>
    public string? TaxNumber { get; private set; }

    public string ContactEmail { get; private set; }

    public string? ContactPhone { get; private set; }

    public string? Website { get; private set; }

    public string? LogoPath { get; private set; }

    public string? Description { get; private set; }

    /// <summary>
    /// Admin tarafindan dogrulanmis mi? Dogrulanmamis organizatorun
    /// etkinlikleri her seferinde onaydan gecer.
    /// </summary>
    public bool IsVerified { get; private set; }

    public User User { get; private set; } = null!;

    public static OrganizerProfile Create(Guid userId, string companyName, string contactEmail)
    {
        if (string.IsNullOrWhiteSpace(companyName))
        {
            throw new DomainException("Firma adi bos olamaz.", "organizer_profile.company_required");
        }

        if (string.IsNullOrWhiteSpace(contactEmail))
        {
            throw new DomainException("Iletisim e-postasi bos olamaz.", "organizer_profile.email_required");
        }

        return new OrganizerProfile
        {
            UserId = userId,
            CompanyName = companyName.Trim(),
            ContactEmail = contactEmail.Trim().ToLowerInvariant()
        };
    }

    public void Verify() => IsVerified = true;

    public void Update(string companyName, string contactEmail, string? contactPhone, string? description)
    {
        if (string.IsNullOrWhiteSpace(companyName))
        {
            throw new DomainException("Firma adi bos olamaz.", "organizer_profile.company_required");
        }

        CompanyName = companyName.Trim();
        ContactEmail = contactEmail.Trim().ToLowerInvariant();
        ContactPhone = contactPhone;
        Description = description;
    }

    public void SetLogo(string? path) => LogoPath = path;
}
