using Ticketing.Domain.Common;
using Ticketing.Domain.Enums;
using Ticketing.Domain.Events;
using Ticketing.Domain.ValueObjects;

namespace Ticketing.Domain.Entities;

/// <summary>
/// Rezervasyon. PDF Sprint 7.
///
/// Kullanıcı koltuk sectiginde olusur, 10 dakika boyunca koltukları kilitler.
/// Bu süre içinde ödeme yapilmazsa background job tarafından iptal edilir.
/// </summary>
public class Reservation : ConcurrentEntity
{
    private Reservation()
    {
        ReservationCode = string.Empty;
        TotalAmount = Money.Zero("TRY");
    }

    // Durum makinesi
    // docs/02-domain-model.md'deki tablonun birebir karşılığı

    private static readonly Dictionary<ReservationStatus, ReservationStatus[]> AllowedTransitions = new()
    {
        [ReservationStatus.Locked] =
        [
            ReservationStatus.PaymentPending,
            ReservationStatus.Expired,
            ReservationStatus.Cancelled
        ],
        [ReservationStatus.PaymentPending] =
        [
            ReservationStatus.Confirmed,
            ReservationStatus.Locked,      // ödeme başarısız -> geri dön
            ReservationStatus.Expired,
            ReservationStatus.Cancelled
        ],
        [ReservationStatus.Confirmed] =
        [
            ReservationStatus.Refunded
        ],

        // Expired, Cancelled, Refunded bilerek YOK -- son durumlar.
        //
        // Ozellikle "Expired" anahtarinin olmamasi, PDF'in su kuralinin
        // doğrudan karşılığı:
        //   "Süresi dolmuş rezervasyon üzerinden ödeme baslatilamaz."
        // Expired'dan PaymentPending'e giden bir yol OLMADIGI için
        // bu kural yapisal olarak imkansiz hale geliyor.
    };

    // Alanlar

    public Guid UserId { get; private set; }

    public Guid EventSessionId { get; private set; }

    /// <summary>
    /// Kullanıcıya gösterilecek kisa kod. Ornek: "RSV-8F3A2C".
    ///
    /// Neden Id yetmiyor? Guid 36 karakter ve okunamaz. Kullanıcı cagri
    /// merkezini aradiginda "rezervasyon numaram 8f3a2c1d-..." diye
    /// okuyamaz. Kisa kod bu yüzden var.
    /// </summary>
    public string ReservationCode { get; private set; }

    public ReservationStatus Status { get; private set; }

    /// <summary>
    /// Toplam tutar. BACKEND tarafında hesaplanir.
    ///
    /// PDF Sprint 6: "Frontend tarafından gonderilen toplam tutara
    /// güvenilmemelidir."
    ///
    /// Bu sadece bir tavsiye değil, güvenlik gereksinimidir: frontend'den
    /// gelen tutara guvenirsek, kullanıcı tarayıcı konsolundan isteği
    /// degistirip 500 TL'lik bileti 1 TL'ye alabilir. Tutar her zaman
    /// sunucudaki EventSeat.Price degerlerinden hesaplanir.
    /// </summary>
    public Money TotalAmount { get; private set; }

    /// <summary>Kilit bitiş zamani. Bu andan sonra koltuklar serbest.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>
    /// Kac kez süre uzatıldı. PDF'te yok ama gerekli:
    /// sınırsız uzatma olsaydı bir kullanıcı popüler bir etkinlikte
    /// koltukları suresiz bloke edip satışı sabote edebilirdi.
    /// </summary>
    public int ExtensionCount { get; private set; }

    /// <summary>
    /// PDF Sprint 15: "Aynı isteğin tekrar gonderilmesine karsi
    /// idempotency uygulanmalıdır."
    ///
    /// Senaryo: Kullanıcı butona basiyor, internet yavas, sabirsizlanip
    /// tekrar basiyor. Iki istek de sunucuya ulasiyor.
    ///
    /// Cozum: Bu alan veritabaninda UNIQUE. Ikinci istek geldiğinde unique
    /// ihlali olusur, biz yakalar ve ILK istegin sonucunu doneriz.
    ///
    /// Neden ayrı bir IdempotencyKeys tablosu değil? Çünkü o zaman
    /// "key kaydet" ve "rezervasyon oluştur" iki ayrı işlem olurdu ve
    /// aralarinda yine yaris durumu dogardi. Aynı satirda tutmak,
    /// unique constraint'in ATOMIK garantisinden faydalanmamizi sagliyor.
    /// </summary>
    public string? IdempotencyKey { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public string? CancellationReason { get; private set; }

    public User User { get; private set; } = null!;

    public EventSession EventSession { get; private set; } = null!;

    private readonly List<ReservationItem> _items = [];

    public IReadOnlyCollection<ReservationItem> Items => _items.AsReadOnly();

    // Olusturma

    /// <summary>
    /// Yeni rezervasyon oluşturur ve koltukları kilitler.
    /// </summary>
    /// <param name="seats">
    /// Kilitlenecek koltuklar. Fiyat bilgisi BUNLARDAN okunur -- cagirandan
    /// gelen bir tutar parametresi bilerek YOK.
    /// </param>
    /// <param name="now">
    /// Su anki zaman. Disaridan alıyorum ki "süre doldu" senaryolarini
    /// sistem saatini degistirmeden test edebileyim.
    /// </param>
    public static Reservation Create(
        Guid userId,
        Guid eventSessionId,
        IReadOnlyList<EventSeat> seats,
        TimeSpan lockDuration,
        DateTimeOffset now,
        string? idempotencyKey = null)
    {
        ArgumentNullException.ThrowIfNull(seats);

        if (seats.Count == 0)
        {
            throw new DomainException("En az bir koltuk seçilmelidir.", "reservation.no_seats");
        }

        // Aynı koltuğun iki kez gonderilmesini engelliyorum.
        // Frontend'de bir hata olusursa veya kötü niyetli bir istek gelirse
        // aynı koltuk için iki kalem olusur, tutar iki katina çıkar ve
        // ikinci kilit girisimi zaten patlar. Bastan kesmek daha temiz.
        var tekilKoltukSayisi = seats.Select(s => s.Id).Distinct().Count();
        if (tekilKoltukSayisi != seats.Count)
        {
            throw new DomainException("Aynı koltuk birden fazla kez secilemez.", "reservation.duplicate_seats");
        }

        var reservation = new Reservation
        {
            UserId = userId,
            EventSessionId = eventSessionId,
            Status = ReservationStatus.Locked,
            ExpiresAt = now.Add(lockDuration),
            IdempotencyKey = idempotencyKey,
            ReservationCode = GenerateCode(),
            TotalAmount = Money.Zero(seats[0].Price.Currency),
        };

        foreach (var seat in seats)
        {
            // Koltugu kilitle. Musait degilse burada DomainException firlar
            // ve rezervasyon HİÇ olusmaz -- "ya hep ya hiç".
            seat.Lock(reservation.Id, reservation.ExpiresAt, now);

            reservation._items.Add(ReservationItem.Create(
                reservation.Id, seat.Id, seat.TicketTypeId, seat.Price));

            // Toplami koltuğun KENDİ fiyatindan hesapliyorum.
            reservation.TotalAmount += seat.Price;
        }

        reservation.Raise(new ReservationCreatedDomainEvent(
            reservation.Id,
            userId,
            eventSessionId,
            seats.Select(s => s.Id).ToList(),
            reservation.ExpiresAt,
            now));

        return reservation;
    }

    /// <summary>
    /// Okunabilir rezervasyon kodu üretir. Ornek: "RSV-8F3A2C".
    ///
    /// Karisabilecek karakterleri (0/O, 1/I/L) bilerek CIKARIYORUM.
    /// Kullanıcı kodu telefonda okuyacak veya elle yazacak; "0" mi "O" mu
    /// diye dusunmesini istemiyorum. Bu detay cagri merkezi yukunu
    /// gerçekten azaltir.
    /// </summary>
    private static string GenerateCode()
    {
        const string alfabe = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
        var karakterler = new char[6];

        for (var i = 0; i < karakterler.Length; i++)
        {
            karakterler[i] = alfabe[System.Security.Cryptography.RandomNumberGenerator.GetInt32(alfabe.Length)];
        }

        return string.Concat("RSV-", new string(karakterler));
    }

    // Durum gecisleri

    private void TransitionTo(ReservationStatus target)
    {
        if (!AllowedTransitions.TryGetValue(Status, out var allowed) || !Array.Exists(allowed, s => s == target))
        {
            throw new DomainException(
                $"Rezervasyon {Status} durumundan {target} durumuna gecemez.",
                "reservation.invalid_transition");
        }

        Status = target;
    }

    /// <summary>
    /// Süre doldu mu?
    /// Metot, property değil -- sonuç zamana bağlı.
    /// </summary>
    public bool IsExpiredAt(DateTimeOffset now) => now >= ExpiresAt;

    public TimeSpan GetRemainingTime(DateTimeOffset now)
    {
        var kalan = ExpiresAt - now;

        // Negatif süre donmuyorum: frontend geri sayimda "-00:03" gostermemeli.
        return kalan > TimeSpan.Zero ? kalan : TimeSpan.Zero;
    }

    /// <summary>
    /// Ödeme baslatir.
    ///
    /// Süre kontrolunu durum gecisinden ONCE yapıyorum. Sebep: bu bir
    /// IS KURALI ihlali, geçersiz bir durum gecisi değil. Kullanıcıya
    /// "süreniz doldu" demek, "gecis yapılamaz" demekten çok daha anlamlı.
    /// Hata kodu da farklı olduğu için frontend ikisine farklı tepki verebilir.
    /// </summary>
    public void StartPayment(DateTimeOffset now)
    {
        if (IsExpiredAt(now))
        {
            throw new DomainException(
                "Rezervasyon süresi dolmuş, ödeme baslatilamaz.",
                "reservation.expired");
        }

        TransitionTo(ReservationStatus.PaymentPending);
    }

    /// <summary>
    /// Ödeme başarısız oldu, kilitli duruma geri dön.
    /// Süre UZATILMAZ -- bkz. docs/01-is-analizi.md soru 8.
    /// </summary>
    public void RevertToLocked() => TransitionTo(ReservationStatus.Locked);

    public void Confirm(Guid paymentId, DateTimeOffset now)
    {
        TransitionTo(ReservationStatus.Confirmed);

        Raise(new ReservationConfirmedDomainEvent(Id, UserId, paymentId, now));
    }

    /// <summary>
    /// Süre doldu. Background job cagirir.
    /// </summary>
    public void Expire(DateTimeOffset now)
    {
        TransitionTo(ReservationStatus.Expired);

        Raise(new ReservationExpiredDomainEvent(
            Id, UserId, EventSessionId, _items.Select(i => i.EventSeatId).ToList(), now));
    }

    public void Cancel(string? reason = null)
    {
        TransitionTo(ReservationStatus.Cancelled);

        CancelledAt = DateTimeOffset.UtcNow;
        CancellationReason = reason;
    }

    public void MarkAsRefunded() => TransitionTo(ReservationStatus.Refunded);

    /// <summary>
    /// Rezervasyon suresini uzatir.
    /// PDF Sprint 7: "POST /api/v1/reservations/{id}/extend"
    /// </summary>
    /// <param name="extension">Eklenecek süre.</param>
    /// <param name="maxExtensions">Izin verilen maksimum uzatma sayısı.</param>
    public void Extend(TimeSpan extension, int maxExtensions, DateTimeOffset now)
    {
        if (Status != ReservationStatus.Locked)
        {
            throw new DomainException(
                "Yalnızca kilitli rezervasyonun süresi uzatilabilir.",
                "reservation.not_extendable");
        }

        if (IsExpiredAt(now))
        {
            // Süresi dolmuş rezervasyonu "uzatmak" önü diriltmek olurdu.
            // Koltuklar bu arada baskasina satılmış olabilir.
            throw new DomainException(
                "Süresi dolmuş rezervasyon uzatilamaz.",
                "reservation.expired");
        }

        if (ExtensionCount >= maxExtensions)
        {
            throw new DomainException(
                $"Rezervasyon en fazla {maxExtensions} kez uzatilabilir.",
                "reservation.extension_limit_reached");
        }

        ExpiresAt = ExpiresAt.Add(extension);
        ExtensionCount++;
    }

    /// <summary>Rezervasyondaki bilet sayısı.</summary>
    public int GetTicketCount() => _items.Count;
}
