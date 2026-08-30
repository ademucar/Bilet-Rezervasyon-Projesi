namespace Ticketing.Domain.Entities;

/// <summary>
/// Bir bilet turunun hangi oturma planı bolumlerini kapsadigi.
///
/// Bu tablo pdf'in er diyagraminda yok -- neden ekliyorum?
///
/// PDF Sprint 6 su endpoint'i istiyor:
///     POST /api/v1/ticket-types/{id}/assign-section
///
/// Ve su is kuralini:
///     "Aynı koltuk birden fazla aktif bilet turune atanamaz."
///
/// Bu ikisi bir ESLESTIRME gerektiriyor: hangi bölüm hangi bilet
/// turune ait? Bunu tutacak bir yer olmadan endpoint'in yapacagi
/// bir sey yok.
///
/// Neden TicketType'a tek bir SeatSectionId eklemedim?
/// Çünkü bir bilet türü birden fazla bolumu kapsayabilir. Ornegin
/// "Standart" bileti hem "Orta Blok" hem "Yan Blok" için geçerli
/// olabilir. Tek alan bunu modelleyemezdi.
///
/// Ters yon ise tekildir: bir bölüm yalnızca bir bilet turune ait
/// olabilir -- yoksa o bolumdeki koltuğun fiyati belirsiz kalırdı.
/// Bu kisiti UNIQUE (SeatSectionId) index'i ile garanti ediyorum.
///
/// UserRole ve Favorite gibi bu da COMPOSITE KEY kullaniyor:
/// kendine ait bir kimliği yok, kimliği iliskilendirdigi iki
/// varligin birlesimi.
/// </summary>
public class TicketTypeSection
{
    private TicketTypeSection()
    {
    }

    internal TicketTypeSection(Guid ticketTypeId, Guid seatSectionId)
    {
        TicketTypeId = ticketTypeId;
        SeatSectionId = seatSectionId;
        AssignedAt = DateTimeOffset.UtcNow;
    }

    public Guid TicketTypeId { get; private set; }

    public Guid SeatSectionId { get; private set; }

    public DateTimeOffset AssignedAt { get; private set; }

    public TicketType TicketType { get; private set; } = null!;

    public SeatSection SeatSection { get; private set; } = null!;
}
