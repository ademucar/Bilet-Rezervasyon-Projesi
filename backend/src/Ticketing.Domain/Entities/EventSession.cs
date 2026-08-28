using Ticketing.Domain.Common;
using Ticketing.Domain.Enums;
using Ticketing.Domain.ValueObjects;

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

    /// <summary>
    /// Fiziksel koltuklardan (Seat) bu oturuma ait EventSeat kayitlarini uretir.
    ///
    /// ------------------------------------------------------------------
    /// BU METOT NEDEN BURADA? Neden bir servis degil?
    /// ------------------------------------------------------------------
    /// EventSeat'ler bu oturuma AITTIR. Onlari uretme yetkisi de oturumun
    /// kendisinde olmali. Bir "SeatGeneratorService" yazsaydim, o servis
    /// EventSession'in ic durumunu (AreSeatsGenerated) disaridan
    /// degistirmek zorunda kalirdi ve kapsulleme bozulurdu.
    ///
    /// Bu, "Tell, Don't Ask" ilkesidir: nesneye durumunu SORUP disarida
    /// karar vermek yerine, ona ne yapmasi gerektigini SOYLE.
    ///
    /// ------------------------------------------------------------------
    /// NEDEN TEK FIYAT DEGIL DE ESLESTIRME FONKSIYONU ALIYOR?
    /// ------------------------------------------------------------------
    /// Ilk yazisimda tek bir ticketTypeId ve price aliyordu. Ama gercek
    /// salonlarda her BOLUM farkli fiyatlidir: "Orta Blok 450 TL",
    /// "Balkon 250 TL".
    ///
    /// Tek fiyatli surumu kullanip sonradan koltuklari tek tek
    /// duzeltebilirdim -- nitekim ilk denemem oyleydi. Ama o zaman
    /// EventSeat'in fiyat atama metodunu Application katmanina acmam
    /// gerekiyordu ve entity yarim kurulmus bir durumdan geciyordu.
    ///
    /// Fonksiyon parametresi ile koltuk DOGRU fiyatla DOGUYOR; ara bir
    /// gecersiz durum hic olusmuyor.
    /// </summary>
    /// <param name="seats">Oturma planindaki fiziksel koltuklar.</param>
    /// <param name="pricingResolver">
    /// Bir koltugun bolumune gore bilet turunu ve fiyatini dondurur.
    /// Bolum eslestirilmemisse null donmeli; o zaman uretim iptal edilir.
    /// </param>
    public IReadOnlyList<EventSeat> GenerateSeats(
        IReadOnlyList<Seat> seats,
        Func<Seat, (Guid TicketTypeId, Money Price)?> pricingResolver)
    {
        ArgumentNullException.ThrowIfNull(seats);
        ArgumentNullException.ThrowIfNull(pricingResolver);

        if (AreSeatsGenerated)
        {
            throw new DomainException(
                "Bu oturum icin koltuklar zaten uretilmis.",
                "event_session.seats_already_generated");
        }

        if (seats.Count == 0)
        {
            throw new DomainException(
                "Oturma planinda hic koltuk yok.",
                "event_session.no_seats_in_layout");
        }

        var uretilenler = new List<EventSeat>(seats.Count);

        foreach (var seat in seats)
        {
            // Pasif koltuklari (kirik, sutun arkasi) atliyorum.
            // Bunlar icin EventSeat uretmek, koltuk haritasinda satilamaz
            // ama gorunur bir kayit olusturmak demek olurdu.
            if (!seat.IsActive)
            {
                continue;
            }

            var pricing = pricingResolver(seat);

            if (pricing is null)
            {
                // ONEMLI: burada patliyorum ve HICBIR SEY kaydedilmiyor.
                //
                // Yarim uretim yapip "bu koltuklarin fiyati yok" durumuna
                // dusmek, sonradan temizlenmesi cok zor bir tutarsizlik
                // olurdu. "Ya hep ya hic".
                throw new DomainException(
                    $"'{seat.GetDisplayLabel()}' koltugunun bolumune bilet turu atanmamis.",
                    "event_session.section_not_priced");
            }

            var eventSeat = EventSeat.Create(
                Id, seat.Id, pricing.Value.TicketTypeId, pricing.Value.Price);

            _eventSeats.Add(eventSeat);
            uretilenler.Add(eventSeat);
        }

        AreSeatsGenerated = true;

        return uretilenler;
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
}
