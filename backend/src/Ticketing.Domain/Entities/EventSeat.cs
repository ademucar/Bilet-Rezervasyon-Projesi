using Ticketing.Domain.Common;
using Ticketing.Domain.Enums;
using Ticketing.Domain.ValueObjects;

namespace Ticketing.Domain.Entities;

/// <summary>
/// PROJENIN KALBI
///
/// Bir koltuğun BELIRLI BIR ETKİNLİK OTURUMUNDAKI durumu.
/// PDF'in "es zamanlı rezervasyon" problemi tam olarak bu satirlarda cozuluyor.
///
/// Seat (fiziksel koltuk) ile karistirma:
///   Seat      = "Salon A, Orta Blok, C sırası, 12 numara" -- degismez
///   EventSeat = "12 Mart 20:00 seansinda C-12: satılmış, 450 TL, VIP"
///
/// Her oturum için Seat tablosundan kopyalanarak üretilir.
/// 1000 koltuklu salon + 3 oturum = 3000 EventSeat satiri.
/// Bu kasitli bir veri cogaltmasidir: her satirin BAGIMSIZ olarak
/// kilitlenebilmesi gerekiyor.
///
/// UC KATMANLI SAVUNMA
///
/// Aynı koltuğun iki kisiye satilmasini uc ayrı katman engelliyor:
///
///   1. Bu sinifin metotlari (Lock/MarkAsSold) durum kontrolü yapar.
///      -> Kullanıcıya anlamlı hata mesaji vermek için.
///
///   2. RowVersion (PostgreSQL xmin) optimistic concurrency.
///      -> Iki istek aynı anda gelirse biri DbUpdateConcurrencyException alır.
///
///   3. UNIQUE (EventSessionId, SeatId) index'i.
///      -> Yukaridakilerin hepsi atlansa bile veritabani ikinci satiri
///         olusturmaz. SON savunma hatti.
///
/// Uc katmanin da olmasını gerekiyor. Biri "gereksiz" değil: her biri
/// farklı bir hata sinifini yakaliyor.
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
    /// Bu koltuğun hangi bilet turune (dolayisiyla hangi fiyata) ait olduğu.
    ///
    /// PDF: "Aynı koltuk birden fazla aktif bilet turune atanamaz."
    /// Bu alan tekil olduğu için kural yapisal olarak garanti -- bir koltuk
    /// aynı anda iki turde olamaz çünkü tek bir TicketTypeId var.
    /// </summary>
    public Guid TicketTypeId { get; private set; }

    public EventSeatStatus Status { get; private set; }

    /// <summary>
    /// Koltugu kilitleyen rezervasyon. Status = Locked iken dolu olmalı.
    /// </summary>
    public Guid? LockedByReservationId { get; private set; }

    /// <summary>
    /// Kilidin bitiş zamani.
    ///
    /// Neden Redis TTL'ine ek olarak burada da tutuluyor?
    /// Redis bir CACHE'tir; cokebilir, temizlenebilir, restart edilebilir.
    /// Kilidin tek kaynagi Redis olsaydı Redis restart edildiginde tüm
    /// kilitler kaybolur ve aynı koltuk iki kisiye satilabilirdi.
    ///
    /// Dogruluk kaynagi (source of truth) her zaman veritabani olmalı.
    /// Redis hizlandirma için var, dogruluk için değil.
    /// </summary>
    public DateTimeOffset? LockedUntil { get; private set; }

    /// <summary>
    /// Satış anindaki fiyat. TicketType.Price'tan KOPYALANIR.
    ///
    /// Neden kopyaliyorum, her seferinde TicketType'tan okumuyorum?
    /// Çünkü organizatör yarin fiyati degistirebilir. Kullanıcının bugun
    /// 450 TL'ye aldigi biletin fiyati, iade hesabinda 600 TL gorunmemeli.
    /// Satış anindaki fiyat SABIT kalmali -- bu bir muhasebe gerekliligi.
    /// </summary>
    public Money Price { get; private set; }

    public EventSession EventSession { get; private set; } = null!;

    public Seat Seat { get; private set; } = null!;

    public TicketType TicketType { get; private set; } = null!;

    internal static EventSeat Create(Guid eventSessionId, Guid seatId, Guid ticketTypeId, Money price)
        => new(eventSessionId, seatId, ticketTypeId, price);

    // Durum sorgulari

    /// <summary>
    /// Koltuk su an satin alinabilir mi?
    ///
    /// DIKKAT: "Status == Available" demek YETMEZ. Süresi dolmuş bir kilit
    /// de aslında musait demektir -- background job henüz gelip temizlememis
    /// olabilir. Job dakikada bir çalışıyor; o bir dakika içinde koltuk
    /// gereksiz yere dolu görünürdü.
    ///
    /// Bu yüzden "kilitli ama süresi gecmis" durumunu da musait sayiyorum.
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

    // Kilitleme

    /// <summary>
    /// Koltugu bir rezervasyon için kilitler.
    ///
    /// Bu metot BASARILI dondugunde is bitmis DEĞİLDİR. Nesne bellekte
    /// değişti; asil kritik an SaveChangesAsync cagrisi. Orada
    /// PostgreSQL su sorguyu calistiracak:
    ///
    ///     UPDATE "EventSeats" SET "Status" = 2, ...
    ///     WHERE "Id" = @id AND xmin = @okunanDeger
    ///
    /// Araya başkası girip satiri degistirmisse 0 satır etkilenir ve
    /// EF Core DbUpdateConcurrencyException firlatir. Bizim istegimiz
    /// kaybeder ama VERI BOZULMAZ -- ustune yazmayiz.
    /// </summary>
    /// <param name="reservationId">Kilidi alan rezervasyon.</param>
    /// <param name="lockedUntil">Kilidin bitecegi an (UTC).</param>
    /// <param name="now">Su anki zaman. Test edilebilirlik için disaridan aliniyor.</param>
    public void Lock(Guid reservationId, DateTimeOffset lockedUntil, DateTimeOffset now)
    {
        if (!IsAvailableAt(now))
        {
            // Kullanıcıya HANGI koltuğun kapildigini soyleyebilmek için
            // hata kodunu ayırt edici tutuyorum. Frontend bu kodu gorunce
            // koltuk haritasini yenileyecek.
            throw new DomainException(
                "Koltuk su anda musait değil.",
                Status == EventSeatStatus.Sold ? "seat.already_sold" : "seat.already_locked");
        }

        if (lockedUntil <= now)
        {
            throw new DomainException(
                "Kilit bitiş zamani gelecekte olmalıdır.",
                "seat.invalid_lock_expiry");
        }

        Status = EventSeatStatus.Locked;
        LockedByReservationId = reservationId;
        LockedUntil = lockedUntil;
    }

    /// <summary>
    /// Kilit suresini uzatir.
    ///
    /// Rezervasyon süresi uzatildiginda koltuğun süresi de uzatilmali;
    /// aksi halde rezervasyon geçerli gorunurken koltuk musait olur ve
    /// başkası alabilir.
    /// </summary>
    public void ExtendLock(DateTimeOffset newLockedUntil)
    {
        if (Status != EventSeatStatus.Locked)
        {
            throw new DomainException(
                "Yalnızca kilitli koltuğun süresi uzatilabilir.",
                "seat.not_locked");
        }

        // Süresi KISALTMAYI engelliyorum.
        //
        // Neden? Uzatma islemi yalnızca ileriye doğru olmalı. Yanlis
        // bir cagri süreyi kisaltsaydi kullanıcı koltuğunu beklenenden
        // önce kaybederdi -- ve sebebi hiç anlasilmazdi.
        if (LockedUntil.HasValue && newLockedUntil <= LockedUntil.Value)
        {
            return;
        }

        LockedUntil = newLockedUntil;
    }

    /// <summary>
    /// Kilidi kaldirir, koltuğu tekrar satışa acar.
    /// Cagrilma yerleri: rezervasyon iptali, ödeme basarisizligi,
    /// süre asimi job'i.
    /// </summary>
    public void Release()
    {
        if (Status == EventSeatStatus.Sold)
        {
            // Satılmış koltuğu "serbest birakmak" ciddi bir veri bozulmasi
            // olurdu: bileti olan kullanıcının koltuğu baskasina satilirdi.
            // İade sureci ayrı bir metot (Refund) üzerinden isler.
            throw new DomainException(
                "Satilmis koltuk serbest birakilamaz. İade sureci kullanılmalıdır.",
                "seat.already_sold");
        }

        Status = EventSeatStatus.Available;
        LockedByReservationId = null;
        LockedUntil = null;
    }

    /// <summary>
    /// Ödeme başarılı olduktan sonra koltuğu satılmış olarak isaretler.
    ///
    /// reservationId parametresini DOGRULAMA için alıyorum, atama için değil.
    /// Amac: A rezervasyonunun kilitledigi koltuğu B rezervasyonunun
    /// satmasini engellemek. Bu kontrol olmasaydı, ödeme akisindaki bir
    /// mantik hatası baskasinin koltuğunu satabilirdi.
    /// </summary>
    public void MarkAsSold(Guid reservationId)
    {
        if (Status != EventSeatStatus.Locked)
        {
            throw new DomainException(
                $"Yalnızca kilitli koltuk satilabilir. Mevcut durum: {Status}",
                "seat.not_locked");
        }

        if (LockedByReservationId != reservationId)
        {
            throw new DomainException(
                "Bu koltuk başka bir rezervasyon tarafından kilitlenmis.",
                "seat.locked_by_another_reservation");
        }

        Status = EventSeatStatus.Sold;
        LockedUntil = null;   // artık süre kavrami yok, koltuk kalici olarak satıldı
    }

    /// <summary>
    /// İade sonrası koltuğu tekrar satışa acar.
    /// </summary>
    public void Refund()
    {
        if (Status != EventSeatStatus.Sold)
        {
            throw new DomainException(
                "Yalnızca satilmis koltuk iade edilebilir.",
                "seat.not_sold");
        }

        Status = EventSeatStatus.Available;
        LockedByReservationId = null;
        LockedUntil = null;
    }

    /// <summary>
    /// Koltugu satışa kapatır (ses masasi, kırık koltuk, protokol yeri).
    /// </summary>
    public void Block()
    {
        if (Status == EventSeatStatus.Sold)
        {
            throw new DomainException(
                "Satılmış koltuk bloke edilemez.",
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
            throw new DomainException("Koltuk bloke değil.", "seat.not_blocked");
        }

        Status = EventSeatStatus.Available;
    }
}
