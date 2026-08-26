using Ticketing.Domain.Common;
using Ticketing.Domain.Enums;

namespace Ticketing.Domain.Entities;

/// <summary>
/// Etkinlik oturumu. PDF sayfa 12 "Oturum Alanlari".
///
/// Bir etkinligin birden fazla oturumu olabilir:
///   - 3 gunluk festival -> 3 oturum
///   - Ayni gun 14:00 ve 20:00 tiyatro -> 2 oturum
///
/// ------------------------------------------------------------------
/// NEDEN KOLTUKLAR ETKINLIGE DEGIL OTURUMA BAGLI?
/// ------------------------------------------------------------------
/// Koltuk satisi OTURUM bazindadir, etkinlik bazinda degil. 14:00
/// seansinda dolu olan C-12 koltugu 20:00 seansinda bostur.
///
/// Bu yuzden EventSeat kayitlari EventSessionId'ye baglanir, EventId'ye degil.
/// Sprint 7'deki tum kilitleme mantigi bu iliskiye dayaniyor.
/// </summary>
public class EventSession : ConcurrentEntity
{
    private EventSession()
    {
    }

    private EventSession(
        Guid eventId,
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        Guid hallId,
        Guid seatLayoutId)
    {
        EventId = eventId;
        StartDate = startDate;
        EndDate = endDate;
        HallId = hallId;
        SeatLayoutId = seatLayoutId;
        Status = EventSessionStatus.Scheduled;
    }

    public Guid EventId { get; private set; }

    public DateTimeOffset StartDate { get; private set; }

    public DateTimeOffset EndDate { get; private set; }

    public Guid HallId { get; private set; }

    /// <summary>
    /// Bu oturumda kullanilan oturma plani.
    ///
    /// Neden salon degil de PLAN? Cunku ayni salon farkli duzenlerde
    /// kullanilabilir (konser duzeni / tiyatro duzeni). Koltuk kayitlari
    /// plandan uretilecegi icin plani bilmemiz sart.
    /// </summary>
    public Guid SeatLayoutId { get; private set; }

    public EventSessionStatus Status { get; private set; }

    /// <summary>
    /// Bu oturum icin EventSeat kayitlari uretildi mi?
    ///
    /// Neden ayri bir bayrak? Koltuk uretimi agir bir islem (1000 koltuk =
    /// 1000 INSERT). Iki kez calistirilirsa unique index hata verir ama
    /// o noktaya gelmeden once burada durdurmak hem hizli hem de
    /// kullaniciya anlamli bir mesaj vermemizi saglar.
    /// </summary>
    public bool AreSeatsGenerated { get; private set; }

    public Event Event { get; private set; } = null!;

    private readonly List<EventSeat> _eventSeats = [];

    public IReadOnlyCollection<EventSeat> EventSeats => _eventSeats.AsReadOnly();

    internal static EventSession Create(
        Guid eventId,
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        Guid hallId,
        Guid seatLayoutId)
    {
        // PDF sayfa 13: "Bitis tarihi baslangic tarihinden once olamaz."
        //
        // ">=" kullaniyorum, ">" degil. Cunku baslangic ve bitisin AYNI
        // an olmasi da anlamsiz -- sifir suren bir oturum olamaz.
        // Bu tur sinir kararlarini bilincli vermek gerekir; "=" durumunu
        // dusunmeden ">" yazmak en sik yapilan off-by-one hatasidir.
        if (startDate >= endDate)
        {
            throw new DomainException(
                "Oturum bitis tarihi, baslangic tarihinden sonra olmalidir.",
                "event_session.invalid_dates");
        }

        if (hallId == Guid.Empty)
        {
            throw new DomainException("Salon secilmelidir.", "event_session.hall_required");
        }

        if (seatLayoutId == Guid.Empty)
        {
            throw new DomainException("Oturma plani secilmelidir.", "event_session.layout_required");
        }

        return new EventSession(eventId, startDate, endDate, hallId, seatLayoutId);
    }

    /// <summary>
    /// Bu oturum verilen zaman araligiyla cakisiyor mu?
    ///
    /// ------------------------------------------------------------------
    /// ARALIK CAKISMA FORMULU
    /// ------------------------------------------------------------------
    /// Iki aralik [a1, a2] ve [b1, b2] cakisir ancak ve ancak:
    ///     a1 &lt; b2  VE  b1 &lt; a2
    ///
    /// Bu formul ilk bakista sezgisel degildir. Neden dogru oldugunu
    /// gormek icin cakismayan durumlari dusun -- sadece iki tane var:
    ///     1. A tamamen B'den once biter:  a2 &lt;= b1
    ///     2. A tamamen B'den sonra baslar: a1 &gt;= b2
    ///
    /// Bunlarin ikisi de degilse cakisiyorlardir. Bu iki kosulun
    /// degillemesi tam olarak yukaridaki formuldur.
    ///
    /// KATI ESITSIZLIK (&lt;) kullaniyorum, &lt;= degil. Boylece
    /// 14:00-16:00 ile 16:00-18:00 oturumlari CAKISMAZ. Bu dogru davranis:
    /// bir seans bitip digeri hemen baslayabilir. &lt;= kullansaydim
    /// arka arkaya seans koymak imkansiz olurdu.
    /// </summary>
    public bool OverlapsWith(DateTimeOffset otherStart, DateTimeOffset otherEnd)
        => StartDate < otherEnd && otherStart < EndDate;

    public void MarkSeatsGenerated()
    {
        if (AreSeatsGenerated)
        {
            throw new DomainException(
                "Bu oturum icin koltuklar zaten uretilmis.",
                "event_session.seats_already_generated");
        }

        AreSeatsGenerated = true;
    }

    public void Cancel()
    {
        if (Status == EventSessionStatus.Completed)
        {
            throw new DomainException(
                "Tamamlanmis oturum iptal edilemez.",
                "event_session.already_completed");
        }

        Status = EventSessionStatus.Cancelled;
    }

    public void Postpone() => Status = EventSessionStatus.Postponed;

    public void Complete() => Status = EventSessionStatus.Completed;

    /// <summary>
    /// PDF: "Satisi baslamis etkinligin oturma plani degistirilemez."
    /// Kontrolu burada yapiyorum cunku plan bu entity'nin alani.
    /// </summary>
    public void ChangeSeatLayout(Guid newSeatLayoutId)
    {
        if (AreSeatsGenerated)
        {
            throw new DomainException(
                "Koltuklari uretilmis oturumun oturma plani degistirilemez. " +
                "Mevcut rezervasyonlar ve biletler gecersiz kalirdi.",
                "event_session.seats_already_generated");
        }

        if (newSeatLayoutId == Guid.Empty)
        {
            throw new DomainException("Oturma plani secilmelidir.", "event_session.layout_required");
        }

        SeatLayoutId = newSeatLayoutId;
    }

    internal void AddEventSeat(EventSeat seat)
    {
        ArgumentNullException.ThrowIfNull(seat);
        _eventSeats.Add(seat);
    }
}
