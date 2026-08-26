using System.Globalization;
using System.Security.Cryptography;
using Ticketing.Domain.Common;
using Ticketing.Domain.Enums;
using Ticketing.Domain.ValueObjects;

namespace Ticketing.Domain.Entities;

/// <summary>
/// Bilet. PDF Sprint 8: "Odeme basarili olmadan bilet olusturulamaz."
///
/// Bu kural yapisal olarak korunuyor: Ticket ancak Reservation Confirmed
/// olduktan sonra, ReservationItem uzerinden uretilebiliyor.
/// </summary>
public class Ticket : AuditableEntity
{
    private Ticket()
    {
        TicketNumber = string.Empty;
        Price = Money.Zero("TRY");
    }

    public Guid ReservationItemId { get; private set; }

    public Guid UserId { get; private set; }

    public Guid EventSessionId { get; private set; }

    public Guid EventSeatId { get; private set; }

    /// <summary>
    /// PDF: "Bilet numarasi benzersiz olmalidir."
    /// Format: TKT-20260315-A7B3C9D2
    /// Veritabaninda UNIQUE index ile korunuyor.
    /// </summary>
    public string TicketNumber { get; private set; }

    public TicketStatus Status { get; private set; }

    /// <summary>
    /// Biletin satildigi fiyat. ReservationItem.UnitPrice'tan kopyalanir.
    /// Iade hesabi bu degere gore yapilir.
    /// </summary>
    public Money Price { get; private set; }

    /// <summary>Etkinlik girisinde QR okutuldugu an.</summary>
    public DateTimeOffset? UsedAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    /// <summary>
    /// Ogrenci bileti icin dogrulama alani.
    /// PDF Sprint 6: "Ogrenci bileti icin dogrulama alani tasarlanmalidir."
    /// </summary>
    public string? StudentVerificationCode { get; private set; }

    public ReservationItem ReservationItem { get; private set; } = null!;

    public User User { get; private set; } = null!;

    public EventSeat EventSeat { get; private set; } = null!;

    public TicketQrCode? QrCode { get; private set; }

    /// <summary>
    /// Bilet uretir.
    ///
    /// reservationItem parametresi uzerinden AttachTicket cagriliyor.
    /// Boylece ayni kalem icin ikinci bir bilet uretilirse orada
    /// DomainException firlar. Bu, "koltuk bir ama bilet iki" hatasinin
    /// (salona iki kisi girer) onune geciyor.
    /// </summary>
    public static Ticket Create(
        ReservationItem reservationItem,
        Guid userId,
        Guid eventSessionId,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(reservationItem);

        var ticket = new Ticket
        {
            ReservationItemId = reservationItem.Id,
            UserId = userId,
            EventSessionId = eventSessionId,
            EventSeatId = reservationItem.EventSeatId,
            Price = reservationItem.UnitPrice,
            Status = TicketStatus.Active,
            TicketNumber = GenerateTicketNumber(now)
        };

        reservationItem.AttachTicket(ticket.Id);

        return ticket;
    }

    /// <summary>
    /// Benzersiz bilet numarasi uretir: TKT-20260315-A7B3C9D2
    ///
    /// Rastgele kismi icin RandomNumberGenerator kullaniyorum, Random degil.
    ///
    /// Neden onemli? Random tahmin edilebilir bir dizidir; tohumu (seed)
    /// bilen biri sonraki degerleri hesaplayabilir. Bilet numarasi
    /// tahmin edilebilirse saldirgan gecerli bilet numaralari uretip
    /// sistemi yoklayabilir. RandomNumberGenerator kriptografik olarak
    /// guvenlidir.
    ///
    /// Bu yine de tek basina yeterli DEGIL: veritabanindaki UNIQUE index
    /// carpisma (collision) ihtimaline karsi son savunma hatti.
    /// 8 hex karakter = 4 milyar ihtimal; tarih onekiyle birlikte
    /// carpisma pratikte imkansiza yakin ama "imkansiza yakin" ile
    /// "imkansiz" ayni sey degildir.
    /// </summary>
    private static string GenerateTicketNumber(DateTimeOffset now)
    {
        var rastgele = RandomNumberGenerator.GetBytes(4);
        var hex = Convert.ToHexString(rastgele);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"TKT-{now:yyyyMMdd}-{hex}");
    }

    /// <summary>
    /// Etkinlik girisinde QR okutuldugunda cagrilir.
    /// </summary>
    public void MarkAsUsed(DateTimeOffset now)
    {
        if (Status != TicketStatus.Active)
        {
            throw new DomainException(
                $"Bilet kullanilamaz. Mevcut durum: {Status}",
                "ticket.not_active");
        }

        Status = TicketStatus.Used;
        UsedAt = now;
    }

    /// <summary>
    /// Bileti iptal eder.
    /// </summary>
    /// <param name="withRefund">Para iadesi yapildi mi?</param>
    public void Cancel(bool withRefund, DateTimeOffset now)
    {
        if (Status == TicketStatus.Used)
        {
            // PDF: kullanilmis bilet iade edilemez.
            // Kullanici etkinlige girdi; hizmeti aldi.
            throw new DomainException(
                "Kullanilmis bilet iptal edilemez.",
                "ticket.already_used");
        }

        if (Status is TicketStatus.Cancelled or TicketStatus.Refunded)
        {
            return;   // idempotent
        }

        Status = withRefund ? TicketStatus.Refunded : TicketStatus.Cancelled;
        CancelledAt = now;
    }

    /// <summary>Etkinlik gecti, bilet kullanilmadi. Background job cagirir.</summary>
    public void MarkAsExpired()
    {
        if (Status != TicketStatus.Active)
        {
            return;
        }

        Status = TicketStatus.Expired;
    }

    public void SetStudentVerificationCode(string? code) => StudentVerificationCode = code;

    /// <summary>
    /// Iade edilebilir mi?
    /// Iade orani hesabi CancellationPolicy'nin isi; burada sadece
    /// biletin durumuna bakiyoruz.
    /// </summary>
    public bool IsRefundable() => Status == TicketStatus.Active;
}
