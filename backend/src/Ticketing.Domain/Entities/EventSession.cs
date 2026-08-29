using Ticketing.Domain.Common;
using Ticketing.Domain.Enums;
using Ticketing.Domain.ValueObjects;

namespace Ticketing.Domain.Entities;

/// <summary>
/// Etkinlik oturumu. PDF sayfa 12 "Oturum Alanlari".
///
/// Bir etkinliğin birden fazla oturumu olabilir:
///   - 3 günlük festival -> 3 oturum
///   - Aynı gün 14:00 ve 20:00 tiyatro -> 2 oturum
///
/// ------------------------------------------------------------------
/// NEDEN KOLTUKLAR ETKINLIGE DEĞİL OTURUMA BAGLI?
/// ------------------------------------------------------------------
/// Koltuk satışı OTURUM bazindadir, etkinlik bazinda değil. 14:00
/// seansinda dolu olan C-12 koltuğu 20:00 seansinda bostur.
///
/// Bu yüzden EventSeat kayitlari EventSessionId'ye baglanir, EventId'ye değil.
/// Sprint 7'deki tüm kilitleme mantığı bu iliskiye dayaniyor.
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
    /// Bu oturumda kullanilan oturma planı.
    ///
    /// Neden salon değil de PLAN? Çünkü aynı salon farklı duzenlerde
    /// kullanilabilir (konser düzeni / tiyatro düzeni). Koltuk kayitlari
    /// plandan uretilecegi için planı bilmemiz sart.
    /// </summary>
    public Guid SeatLayoutId { get; private set; }

    public EventSessionStatus Status { get; private set; }

    /// <summary>
    /// Bu oturum için EventSeat kayitlari üretildi mi?
    ///
    /// Neden ayrı bir bayrak? Koltuk üretimi agir bir işlem (1000 koltuk =
    /// 1000 INSERT). Iki kez calistirilirsa unique index hata verir ama
    /// o noktaya gelmeden önce burada durdurmak hem hizli hem de
    /// kullanıcıya anlamlı bir mesaj vermemizi saglar.
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
        // PDF sayfa 13: "Bitiş tarihi başlangıç tarihinden önce olamaz."
        //
        // ">=" kullanıyorum, ">" değil. Çünkü başlangıç ve bitisin AYNI
        // an olmasını da anlamsiz -- sifir suren bir oturum olamaz.
        // Bu tur sinir kararlarini bilinçli vermek gerekir; "=" durumunu
        // dusunmeden ">" yazmak en sik yapilan off-by-one hatasidir.
        if (startDate >= endDate)
        {
            throw new DomainException(
                "Oturum bitiş tarihi, başlangıç tarihinden sonra olmalıdır.",
                "event_session.invalid_dates");
        }

        if (hallId == Guid.Empty)
        {
            throw new DomainException("Salon seçilmelidir.", "event_session.hall_required");
        }

        if (seatLayoutId == Guid.Empty)
        {
            throw new DomainException("Oturma planı seçilmelidir.", "event_session.layout_required");
        }

        return new EventSession(eventId, startDate, endDate, hallId, seatLayoutId);
    }

    /// <summary>
    /// Bu oturum verilen zaman araligiyla cakisiyor mu?
    ///
    /// ------------------------------------------------------------------
    /// ARALIK CAKISMA FORMULU
    /// ------------------------------------------------------------------
    /// Iki aralık [a1, a2] ve [b1, b2] cakisir ancak ve ancak:
    ///     a1 &lt; b2  VE  b1 &lt; a2
    ///
    /// Bu formul ilk bakista sezgisel degildir. Neden doğru olduğunu
    /// gormek için cakismayan durumlari dusun -- sadece iki tane var:
    ///     1. A tamamen B'den önce biter:  a2 &lt;= b1
    ///     2. A tamamen B'den sonra başlar: a1 &gt;= b2
    ///
    /// Bunlarin ikisi de degilse cakisiyorlardir. Bu iki kosulun
    /// degillemesi tam olarak yukaridaki formuldur.
    ///
    /// KATI ESITSIZLIK (&lt;) kullanıyorum, &lt;= değil. Boylece
    /// 14:00-16:00 ile 16:00-18:00 oturumlari CAKISMAZ. Bu doğru davranis:
    /// bir seans bitip digeri hemen baslayabilir. &lt;= kullansaydim
    /// arka arkaya seans koymak imkansiz olurdu.
    /// </summary>
    public bool OverlapsWith(DateTimeOffset otherStart, DateTimeOffset otherEnd)
        => StartDate < otherEnd && otherStart < EndDate;

    /// <summary>
    /// Fiziksel koltuklardan (Seat) bu oturuma ait EventSeat kayitlarini üretir.
    ///
    /// ------------------------------------------------------------------
    /// BU METOT NEDEN BURADA? Neden bir servis değil?
    /// ------------------------------------------------------------------
    /// EventSeat'ler bu oturuma AITTIR. Onlari üretme yetkisi de oturumun
    /// kendisinde olmalı. Bir "SeatGeneratorService" yazsaydim, o servis
    /// EventSession'in ic durumunu (AreSeatsGenerated) disaridan
    /// degistirmek zorunda kalırdı ve kapsulleme bozulurdu.
    ///
    /// Bu, "Tell, Don't Ask" ilkesidir: nesneye durumunu SORUP disarida
    /// karar vermek yerine, ona ne yapmasi gerektigini SOYLE.
    ///
    /// ------------------------------------------------------------------
    /// NEDEN TEK FIYAT DEĞİL DE ESLESTIRME FONKSIYONU ALIYOR?
    /// ------------------------------------------------------------------
    /// İlk yazisimda tek bir ticketTypeId ve price aliyordu. Ama gerçek
    /// salonlarda her BOLUM farklı fiyatlidir: "Orta Blok 450 TL",
    /// "Balkon 250 TL".
    ///
    /// Tek fiyatli surumu kullanip sonradan koltukları tek tek
    /// duzeltebilirdim -- nitekim ilk denemem oyleydi. Ama o zaman
    /// EventSeat'in fiyat atama metodunu Application katmanina acmam
    /// gerekiyordu ve entity yarim kurulmus bir durumdan geciyordu.
    ///
    /// Fonksiyon parametresi ile koltuk DOGRU fiyatla DOGUYOR; ara bir
    /// geçersiz durum hiç olusmuyor.
    /// </summary>
    /// <param name="seats">Oturma planindaki fiziksel koltuklar.</param>
    /// <param name="pricingResolver">
    /// Bir koltuğun bolumune göre bilet turunu ve fiyatini döndürür.
    /// Bölüm eslestirilmemisse null donmeli; o zaman üretim iptal edilir.
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
                "Bu oturum için koltuklar zaten üretilmiş.",
                "event_session.seats_already_generated");
        }

        if (seats.Count == 0)
        {
            throw new DomainException(
                "Oturma planinda hiç koltuk yok.",
                "event_session.no_seats_in_layout");
        }

        var uretilenler = new List<EventSeat>(seats.Count);

        foreach (var seat in seats)
        {
            // Pasif koltukları (kırık, sutun arkası) atliyorum.
            // Bunlar için EventSeat uretmek, koltuk haritasında satilamaz
            // ama görünür bir kayıt olusturmak demek olurdu.
            if (!seat.IsActive)
            {
                continue;
            }

            var pricing = pricingResolver(seat);

            if (pricing is null)
            {
                // ONEMLI: burada patliyorum ve HICBIR SEY kaydedilmiyor.
                //
                // Yarim üretim yapip "bu koltuklarin fiyati yok" durumuna
                // dusmek, sonradan temizlenmesi çok zor bir tutarsizlik
                // olurdu. "Ya hep ya hiç".
                throw new DomainException(
                    $"'{seat.GetDisplayLabel()}' koltugunun bolumune bilet türü atanmamis.",
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
    /// PDF: "Satışı baslamis etkinliğin oturma planı degistirilemez."
    /// Kontrolu burada yapıyorum çünkü plan bu entity'nin alanı.
    /// </summary>
    public void ChangeSeatLayout(Guid newSeatLayoutId)
    {
        if (AreSeatsGenerated)
        {
            throw new DomainException(
                "Koltukları üretilmiş oturumun oturma planı degistirilemez. " +
                "Mevcut rezervasyonlar ve biletler geçersiz kalırdı.",
                "event_session.seats_already_generated");
        }

        if (newSeatLayoutId == Guid.Empty)
        {
            throw new DomainException("Oturma planı seçilmelidir.", "event_session.layout_required");
        }

        SeatLayoutId = newSeatLayoutId;
    }
}
