using Ticketing.Domain.Common;
using Ticketing.Domain.ValueObjects;

namespace Ticketing.Domain.Entities;

/// <summary>
/// Rezervasyon kalemi. Bir koltuk = bir kalem.
///
/// PDF Sprint 8: "Her rezervasyon kalemi için bilet olusturulmalidir."
/// Yani bu tablo, rezervasyon ile bilet arasindaki koprudur.
/// </summary>
public class ReservationItem : Entity
{
    private ReservationItem() => UnitPrice = Money.Zero("TRY");

    public Guid ReservationId { get; private set; }

    public Guid EventSeatId { get; private set; }

    public Guid TicketTypeId { get; private set; }

    /// <summary>
    /// Bu koltuğun rezervasyon anindaki fiyati.
    ///
    /// EventSeat.Price'tan kopyalaniyor. Neden tekrar kopyaliyoruz?
    /// Çünkü EventSeat iade sonrası tekrar satışa cikabilir ve o zaman
    /// fiyati guncellenmis olabilir. Kalem, o anki fiyati kalici olarak
    /// saklamali -- fatura ve iade hesabi buna dayanacak.
    /// </summary>
    public Money UnitPrice { get; private set; }

    /// <summary>
    /// Ödeme başarılı olunca uretilen bilet. Oncesinde null.
    /// PDF: "Ödeme başarılı olmadan bilet oluşturulamaz."
    /// </summary>
    public Guid? TicketId { get; private set; }

    public Reservation Reservation { get; private set; } = null!;

    public EventSeat EventSeat { get; private set; } = null!;

    internal static ReservationItem Create(Guid reservationId, Guid eventSeatId, Guid ticketTypeId, Money unitPrice)
        => new()
        {
            ReservationId = reservationId,
            EventSeatId = eventSeatId,
            TicketTypeId = ticketTypeId,
            UnitPrice = unitPrice,
        };

    internal void AttachTicket(Guid ticketId)
    {
        if (TicketId.HasValue)
        {
            // Aynı kalem için iki bilet uretmek, kullanıcıya iki bilet
            // vermek demektir -- koltuk bir ama bilet iki. Salona iki
            // kişi girer. Bu yüzden acikca engelliyorum.
            throw new DomainException(
                "Bu rezervasyon kalemi için zaten bilet üretilmiş.",
                "reservation_item.ticket_already_created");
        }

        TicketId = ticketId;
    }
}
