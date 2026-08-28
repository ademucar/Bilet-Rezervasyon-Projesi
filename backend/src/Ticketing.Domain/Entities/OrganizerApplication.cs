using Ticketing.Domain.Common;
using Ticketing.Domain.Enums;

namespace Ticketing.Domain.Entities;

/// <summary>
/// Organizator olma basvurusu. PDF sayfa 5:
/// "Admin organizator basvurularini onaylayabilir."
/// </summary>
public class OrganizerApplication : AuditableEntity
{
    private OrganizerApplication()
    {
        CompanyName = string.Empty;
        ContactEmail = string.Empty;
    }

    public Guid UserId { get; private set; }

    public string CompanyName { get; private set; }

    public string? TaxNumber { get; private set; }

    public string ContactEmail { get; private set; }

    public string? ContactPhone { get; private set; }

    public string? Description { get; private set; }

    public OrganizerApplicationStatus Status { get; private set; }

    public Guid? ReviewedBy { get; private set; }

    public DateTimeOffset? ReviewedAt { get; private set; }

    public string? RejectionReason { get; private set; }

    public User User { get; private set; } = null!;

    public static OrganizerApplication Create(
        Guid userId,
        string companyName,
        string contactEmail,
        string? taxNumber = null,
        string? description = null)
    {
        if (string.IsNullOrWhiteSpace(companyName))
        {
            throw new DomainException("Firma adi bos olamaz.", "organizer_application.company_required");
        }

        return new OrganizerApplication
        {
            UserId = userId,
            CompanyName = companyName.Trim(),
            ContactEmail = contactEmail.Trim().ToLowerInvariant(),
            TaxNumber = taxNumber,
            Description = description,
            Status = OrganizerApplicationStatus.Pending,
        };
    }

    public void Approve(Guid adminId, DateTimeOffset now)
    {
        EnsurePending();

        Status = OrganizerApplicationStatus.Approved;
        ReviewedBy = adminId;
        ReviewedAt = now;
    }

    public void Reject(Guid adminId, string reason, DateTimeOffset now)
    {
        EnsurePending();

        if (string.IsNullOrWhiteSpace(reason))
        {
            // Red gerekcesini ZORUNLU tutuyorum.
            // Gerekcesiz red, kullanicinin ne duzeltecegini bilmemesi
            // demektir; ayni eksik basvuruyu tekrar tekrar gonderir.
            throw new DomainException(
                "Red gerekcesi belirtilmelidir.",
                "organizer_application.reason_required");
        }

        Status = OrganizerApplicationStatus.Rejected;
        ReviewedBy = adminId;
        ReviewedAt = now;
        RejectionReason = reason;
    }

    private void EnsurePending()
    {
        if (Status != OrganizerApplicationStatus.Pending)
        {
            throw new DomainException(
                $"Bu basvuru zaten degerlendirilmis. Durum: {Status}",
                "organizer_application.already_reviewed");
        }
    }
}
