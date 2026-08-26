using Ticketing.Domain.Common;
using Ticketing.Domain.Enums;
using Ticketing.Domain.ValueObjects;

namespace Ticketing.Domain.Entities;

/// <summary>
/// ==================================================================
/// PROJENIN KALBI
/// ==================================================================
///
/// Bir koltugun BELIRLI BIR ETKINLIK OTURUMUNDAKI durumu.
/// PDF'in "es zamanli rezervasyon" problemi tam olarak bu satirlarda cozuluyor.
///
/// Seat (fiziksel koltuk) ile karistirma:
///   Seat      = "Salon A, Orta Blok, C sirasi, 12 numara" -- degismez
///   EventSeat = "12 Mart 20:00 seansinda C-12: satilmis, 450 TL, VIP"
///
/// Her oturum icin Seat tablosundan kopyalanarak uretilir.
/// 1000 koltuklu salon + 3 oturum = 3000 EventSeat satiri.
/// Bu kasitli bir veri cogaltmasidir: her satirin BAGIMSIZ olarak
/// kilitlenebilmesi gerekiyor.
///
/// ------------------------------------------------------------------
/// UC KATMANLI SAVUNMA
/// ------------------------------------------------------------------
/// Ayni koltugun iki kisiye satilmasini uc ayri katman engelliyor:
///
///   1. Bu sinifin metotlari (Lock/MarkAsSold) durum kontrolu yapar.
///      -> Kullaniciya anlamli hata mesaji vermek icin.
///
///   2. RowVersion (PostgreSQL xmin) optimistic concurrency.
///      -> Iki istek ayni anda gelirse biri DbUpdateConcurrencyException alir.
///
///   3. UNIQUE (EventSessionId, SeatId) index'i.
///      -> Yukaridakilerin hepsi atlansa bile veritabani ikinci satiri
///         olusturmaz. SON savunma hatti.
///
/// Uc katmanin da olmasi gerekiyor. Biri "gereksiz" degil: her biri
/// farkli bir hata sinifini yakaliyor.
/// ==================================================================
/// </summary>
public class EventSeat : ConcurrentEntity
{
    private EventSeat() => Price = Money.Zero("TRY");

    private EventSeat(Guid eventSessionId, Guid seatId, Guid ticketTypeId, Money price)
    {
        EventSessionId = eventSessionId;
        SeatId = seatId;
        TicketTypeId = ticketTypeId;
        Price = price;
        Status = EventSeatStatus.Available;
    }

    public Guid EventSessionId { get; private set; }

    public Guid SeatId { get; private set; }

    /// <summary>
    /// Bu koltugun hangi bilet turune (dolayisiyla hangi fiyata) ait oldugu.
    ///
    /// PDF: "Ayni koltuk birden fazla aktif bilet turune atanamaz."
    /// Bu alan tekil oldugu icin kural yapisal olarak garanti -- bir koltuk
    /// ayni anda iki turde olamaz cunku tek bir TicketTypeId var.
    /// </summary>
    public Guid TicketTypeId { get; private set; }

    public EventSeatStatus Status { get; private set; }

    /// <summary>
    /// Koltugu kilitleyen rezervasyon. Status = Locked iken dolu olmali.
    /// </summary>
    public Guid? LockedByReservationId { get; private set; }

    /// <summary>
    /// Kilidin bitis zamani.
    ///
    /// Neden Redis TTL'ine ek olarak burada da tutuluyor?
    /// Redis bir CACHE'tir; cokebilir, temizlenebilir, restart edilebilir.
    /// Kilidin tek kaynagi Redis olsaydi Redis restart edildiginde tum
    /// kilitler kaybolur ve ayni koltuk iki kisiye satilabilirdi.
    ///
    /// Dogruluk kaynagi (source of truth) her zaman veritabani olmali.
    /// Redis hizlandirma icin var, dogruluk icin degil.
    /// </summary>
    public DateTimeOffset? LockedUntil { get; private set; }

    /// <summary>
    /// Satis anindaki fiyat. TicketType.Price'tan KOPYALANIR.
    ///
    /// Neden kopyaliyorum, her seferinde TicketType'tan okumuyorum?
    /// Cunku organizator yarin fiyati degistirebilir. Kullanicinin bugun
    /// 450 TL'ye aldigi biletin fiyati, iade hesabinda 600 TL gorunmemeli.
    /// Satis anindaki fiyat SABIT kalmali -- bu bir muhasebe gerekliligi.
    /// </summary>
    public Money Price { get; private set; }

    public EventSession EventSession { get; private set; } = null!;

    public Seat Seat { get; private set; } = null!;

    public TicketType TicketType { get; private set; } = null!;

    internal static EventSeat Create(Guid eventSessionId, Guid seatId, Guid ticketTypeId, Money price)
        => new(eventSessionId, seatId, ticketTypeId, price);

    // ---------------------------------------------------------------
    // Durum sorgulari
    // ---------------------------------------------------------------

    /// <summary>
    /// Koltuk su an satin alinabilir mi?
    ///
    /// DIKKAT: "Status == Available" demek YETMEZ. Suresi dolmus bir kilit
    /// de aslinda musait demektir -- background job henuz gelip temizlememis
    /// olabilir. Job dakikada bir calisiyor; o bir dakika icinde koltuk
    /// gereksiz yere dolu gorunurdu.
    ///
    /// Bu yuzden "kilitli ama suresi gecmis" durumunu da musait sayiyorum.
    /// </summary>
    public bool IsAvailableAt(DateTimeOffset moment)
    {
        if (Status == EventSeatStatus.Available)
        {
            return true;
        }

        if (Status == EventSeatStatus.Locked && LockedUntil.HasValue && LockedUntil.Value <= moment)
        {
            return true;
        }

        return false;
    }

    public bool IsLockExpiredAt(DateTimeOffset moment)
        => Status == EventSeatStatus.Locked && LockedUntil.HasValue && LockedUntil.Value <= moment;

    // ---------------------------------------------------------------
    // Kilitleme
    // ---------------------------------------------------------------

    /// <summary>
    /// Koltugu bir rezervasyon icin kilitler.
    ///
    /// Bu metot BASARILI dondugunde is bitmis DEGILDIR. Nesne bellekte
    /// degisti; asil kritik an SaveChangesAsync cagrisi. Orada
    /// PostgreSQL su sorguyu calistiracak:
    ///
    ///     UPDATE "EventSeats" SET "Status" = 2, ...
    ///     WHERE "Id" = @id AND xmin = @okunanDeger
    ///
    /// Araya baskasi girip satiri degistirmisse 0 satir etkilenir ve
    /// EF Core DbUpdateConcurrencyException firlatir. Bizim istegimiz
    /// kaybeder ama VERI BOZULMAZ -- ustune yazmayiz.
    /// </summary>
    /// <param name="reservationId">Kilidi alan rezervasyon.</param>
    /// <param name="lockedUntil">Kilidin bitecegi an (UTC).</param>
    /// <param name="now">Su anki zaman. Test edilebilirlik icin disaridan aliniyor.</param>
    public void Lock(Guid reservationId, DateTimeOffset lockedUntil, DateTimeOffset now)
    {
        if (!IsAvailableAt(now))
        {
            // Kullaniciya HANGI koltugun kapildigini soyleyebilmek icin
            // hata kodunu ayirt edici tutuyorum. Frontend bu kodu gorunce
            // koltuk haritasini yenileyecek.
            throw new DomainException(
                "Koltuk su anda musait degil.",
                Status == EventSeatStatus.Sold ? "seat.already_sold" : "seat.already_locked");
        }

        if (lockedUntil <= now)
        {
            throw new DomainException(
                "Kilit bitis zamani gelecekte olmalidir.",
                "seat.invalid_lock_expiry");
        }

        Status = EventSeatStatus.Locked;
        LockedByReservationId = reservationId;
        LockedUntil = lockedUntil;
    }

    /// <summary>
    /// Kilidi kaldirir, koltugu tekrar satisa acar.
    /// Cagrilma yerleri: rezervasyon iptali, odeme basarisizligi,
    /// sure asimi job'i.
    /// </summary>
    public void Release()
    {
        if (Status == EventSeatStatus.Sold)
        {
            // Satilmis koltugu "serbest birakmak" ciddi bir veri bozulmasi
            // olurdu: bileti olan kullanicinin koltugu baskasina satilirdi.
            // Iade sureci ayri bir metot (Refund) uzerinden isler.
            throw new DomainException(
                "Satilmis koltuk serbest birakilamaz. Iade sureci kullanilmalidir.",
                "seat.already_sold");
        }

        Status = EventSeatStatus.Available;
        LockedByReservationId = null;
        LockedUntil = null;
    }

    /// <summary>
    /// Odeme basarili olduktan sonra koltugu satilmis olarak isaretler.
    ///
    /// reservationId parametresini DOGRULAMA icin aliyorum, atama icin degil.
    /// Amac: A rezervasyonunun kilitledigi koltugu B rezervasyonunun
    /// satmasini engellemek. Bu kontrol olmasaydi, ödeme akisindaki bir
    /// mantik hatasi baskasinin koltugunu satabilirdi.
    /// </summary>
    public void MarkAsSold(Guid reservationId)
    {
        if (Status != EventSeatStatus.Locked)
        {
            throw new DomainException(
                $"Yalnizca kilitli koltuk satilabilir. Mevcut durum: {Status}",
                "seat.not_locked");
        }

        if (LockedByReservationId != reservationId)
        {
            throw new DomainException(
                "Bu koltuk baska bir rezervasyon tarafindan kilitlenmis.",
                "seat.locked_by_another_reservation");
        }

        Status = EventSeatStatus.Sold;
        LockedUntil = null;   // artik sure kavrami yok, koltuk kalici olarak satildi
    }

    /// <summary>
    /// Iade sonrasi koltugu tekrar satisa acar.
    /// </summary>
    public void Refund()
    {
        if (Status != EventSeatStatus.Sold)
        {
            throw new DomainException(
                "Yalnizca satilmis koltuk iade edilebilir.",
                "seat.not_sold");
        }

        Status = EventSeatStatus.Available;
        LockedByReservationId = null;
        LockedUntil = null;
    }

    /// <summary>
    /// Koltugu satisa kapatir (ses masasi, kirik koltuk, protokol yeri).
    /// </summary>
    public void Block()
    {
        if (Status == EventSeatStatus.Sold)
        {
            throw new DomainException(
                "Satilmis koltuk bloke edilemez.",
                "seat.already_sold");
        }

        Status = EventSeatStatus.Blocked;
        LockedByReservationId = null;
        LockedUntil = null;
    }

    public void Unblock()
    {
        if (Status != EventSeatStatus.Blocked)
        {
            throw new DomainException("Koltuk bloke degil.", "seat.not_blocked");
        }

        Status = EventSeatStatus.Available;
    }
}
