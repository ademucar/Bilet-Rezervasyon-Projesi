namespace Ticketing.Domain.Entities;

/// <summary>
/// Bir bilet turunun hangi oturma plani bolumlerini kapsadigi.
///
/// ==================================================================
/// BU TABLO PDF'IN ER DIYAGRAMINDA YOK -- NEDEN EKLIYORUM?
/// ==================================================================
/// PDF Sprint 6 su endpoint'i istiyor:
///     POST /api/v1/ticket-types/{id}/assign-section
///
/// Ve su is kuralini:
///     "Ayni koltuk birden fazla aktif bilet turune atanamaz."
///
/// Bu ikisi bir ESLESTIRME gerektiriyor: hangi bolum hangi bilet
/// turune ait? Bunu tutacak bir yer olmadan endpoint'in yapacagi
/// bir sey yok.
///
/// Neden TicketType'a tek bir SeatSectionId eklemedim?
/// Cunku bir bilet turu BIRDEN FAZLA bolumu kapsayabilir. Ornegin
/// "Standart" bileti hem "Orta Blok" hem "Yan Blok" icin gecerli
/// olabilir. Tek alan bunu modelleyemezdi.
///
/// Ters yon ise TEKILDIR: bir bolum yalnizca BIR bilet turune ait
/// olabilir -- yoksa o bolumdeki koltugun fiyati belirsiz kalirdi.
/// Bu kisiti UNIQUE (SeatSectionId) index'i ile garanti ediyoruz.
/// ==================================================================
///
/// UserRole ve Favorite gibi bu da COMPOSITE KEY kullaniyor:
/// kendine ait bir kimligi yok, kimligi iliskilendirdigi iki
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
