using Ticketing.Domain.Common;
using Ticketing.Domain.Enums;
using Ticketing.Domain.Events;
using Ticketing.Domain.ValueObjects;

namespace Ticketing.Domain.Entities;

/// <summary>
/// Rezervasyon. PDF Sprint 7.
///
/// Kullanici koltuk sectiginde olusur, 10 dakika boyunca koltuklari kilitler.
/// Bu sure icinde odeme yapilmazsa background job tarafindan iptal edilir.
/// </summary>
public class Reservation : ConcurrentEntity
{
    private Reservation()
    {
        ReservationCode = string.Empty;
        TotalAmount = Money.Zero("TRY");
    }

    // ---------------------------------------------------------------
    // DURUM MAKINESI
    // docs/02-domain-model.md'deki tablonun birebir karsiligi
    // ---------------------------------------------------------------

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
            ReservationStatus.Locked,      // odeme basarisiz -> geri don
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
        // dogrudan karsiligi:
        //   "Suresi dolmus rezervasyon uzerinden odeme baslatilamaz."
        // Expired'dan PaymentPending'e giden bir yol OLMADIGI icin
        // bu kural yapisal olarak imkansiz hale geliyor.
    };

    // ---------------------------------------------------------------
    // Alanlar
    // ---------------------------------------------------------------

    public Guid UserId { get; private set; }

    public Guid EventSessionId { get; private set; }

    /// <summary>
    /// Kullaniciya gosterilecek kisa kod. Ornek: "RSV-8F3A2C".
    ///
    /// Neden Id yetmiyor? Guid 36 karakter ve okunamaz. Kullanici cagri
    /// merkezini aradiginda "rezervasyon numaram 8f3a2c1d-..." diye
    /// okuyamaz. Kisa kod bu yuzden var.
    /// </summary>
    public string ReservationCode { get; private set; }

    public ReservationStatus Status { get; private set; }

    /// <summary>
    /// Toplam tutar. BACKEND tarafinda hesaplanir.
    ///
    /// PDF Sprint 6: "Frontend tarafindan gonderilen toplam tutara
    /// guvenilmemelidir."
    ///
    /// Bu sadece bir tavsiye degil, guvenlik gereksinimidir: frontend'den
    /// gelen tutara guvenirsek, kullanici tarayici konsolundan istegi
    /// degistirip 500 TL'lik bileti 1 TL'ye alabilir. Tutar her zaman
    /// sunucudaki EventSeat.Price degerlerinden hesaplanir.
    /// </summary>
    public Money TotalAmount { get; private set; }

    /// <summary>Kilit bitis zamani. Bu andan sonra koltuklar serbest.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>
    /// Kac kez sure uzatildi. PDF'te yok ama gerekli:
    /// sinirsiz uzatma olsaydi bir kullanici populer bir etkinlikte
    /// koltuklari suresiz bloke edip satisi sabote edebilirdi.
    /// </summary>
    public int ExtensionCount { get; private set; }

    /// <summary>
    /// PDF Sprint 15: "Ayni isteğin tekrar gonderilmesine karsi
    /// idempotency uygulanmalidir."
    ///
    /// Senaryo: Kullanici butona basiyor, internet yavas, sabirsizlanip
    /// tekrar basiyor. Iki istek de sunucuya ulasiyor.
    ///
    /// Cozum: Bu alan veritabaninda UNIQUE. Ikinci istek geldiginde unique
    /// ihlali olusur, biz yakalar ve ILK istegin sonucunu doneriz.
    ///
    /// Neden ayri bir IdempotencyKeys tablosu degil? Cunku o zaman
    /// "key kaydet" ve "rezervasyon olustur" iki ayri islem olurdu ve
    /// aralarinda yine yaris durumu dogardi. Ayni satirda tutmak,
    /// unique constraint'in ATOMIK garantisinden faydalanmamizi sagliyor.
    /// </summary>
    public string? IdempotencyKey { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public string? CancellationReason { get; private set; }

    public User User { get; private set; } = null!;

    public EventSession EventSession { get; private set; } = null!;

    private readonly List<ReservationItem> _items = [];

    public IReadOnlyCollection<ReservationItem> Items => _items.AsReadOnly();

    // ---------------------------------------------------------------
    // Olusturma
    // ---------------------------------------------------------------

    /// <summary>
    /// Yeni rezervasyon olusturur ve koltuklari kilitler.
    /// </summary>
    /// <param name="seats">
    /// Kilitlenecek koltuklar. Fiyat bilgisi BUNLARDAN okunur -- cagirandan
    /// gelen bir tutar parametresi bilerek YOK.
    /// </param>
    /// <param name="now">
    /// Su anki zaman. Disaridan aliyorum ki "sure doldu" senaryolarini
    /// sistem saatini degistirmeden test edebilelim.
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
            throw new DomainException("En az bir koltuk secilmelidir.", "reservation.no_seats");
        }

        // Ayni koltugun iki kez gonderilmesini engelliyorum.
        // Frontend'de bir hata olusursa veya kotu niyetli bir istek gelirse
        // ayni koltuk icin iki kalem olusur, tutar iki katina cikar ve
        // ikinci kilit girisimi zaten patlar. Bastan kesmek daha temiz.
        var tekilKoltukSayisi = seats.Select(s => s.Id).Distinct().Count();
        if (tekilKoltukSayisi != seats.Count)
        {
            throw new DomainException("Ayni koltuk birden fazla kez secilemez.", "reservation.duplicate_seats");
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
            // ve rezervasyon HIC olusmaz -- "ya hep ya hic".
            seat.Lock(reservation.Id, reservation.ExpiresAt, now);

            reservation._items.Add(ReservationItem.Create(
                reservation.Id, seat.Id, seat.TicketTypeId, seat.Price));

            // Toplami koltugun KENDI fiyatindan hesapliyorum.
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
    /// Okunabilir rezervasyon kodu uretir. Ornek: "RSV-8F3A2C".
    ///
    /// Karisabilecek karakterleri (0/O, 1/I/L) bilerek CIKARIYORUM.
    /// Kullanici kodu telefonda okuyacak veya elle yazacak; "0" mi "O" mu
    /// diye dusunmesini istemiyoruz. Bu detay cagri merkezi yukunu
    /// gercekten azaltir.
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

    // ---------------------------------------------------------------
    // Durum gecisleri
    // ---------------------------------------------------------------

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
    /// Sure doldu mu?
    /// Metot, property degil -- sonuc zamana bagli.
    /// </summary>
    public bool IsExpiredAt(DateTimeOffset now) => now >= ExpiresAt;

    public TimeSpan GetRemainingTime(DateTimeOffset now)
    {
        var kalan = ExpiresAt - now;

        // Negatif sure donmuyorum: frontend geri sayimda "-00:03" gostermemeli.
        return kalan > TimeSpan.Zero ? kalan : TimeSpan.Zero;
    }

    /// <summary>
    /// Odeme baslatir.
    ///
    /// Sure kontrolunu durum gecisinden ONCE yapiyorum. Sebep: bu bir
    /// IS KURALI ihlali, gecersiz bir durum gecisi degil. Kullaniciya
    /// "sureniz doldu" demek, "gecis yapilamaz" demekten cok daha anlamli.
    /// Hata kodu da farkli oldugu icin frontend ikisine farkli tepki verebilir.
    /// </summary>
    public void StartPayment(DateTimeOffset now)
    {
        if (IsExpiredAt(now))
        {
            throw new DomainException(
                "Rezervasyon suresi dolmus, odeme baslatilamaz.",
                "reservation.expired");
        }

        TransitionTo(ReservationStatus.PaymentPending);
    }

    /// <summary>
    /// Odeme basarisiz oldu, kilitli duruma geri don.
    /// Sure UZATILMAZ -- bkz. docs/01-is-analizi.md soru 8.
    /// </summary>
    public void RevertToLocked() => TransitionTo(ReservationStatus.Locked);

    public void Confirm(Guid paymentId, DateTimeOffset now)
    {
        TransitionTo(ReservationStatus.Confirmed);

        Raise(new ReservationConfirmedDomainEvent(Id, UserId, paymentId, now));
    }

    /// <summary>
    /// Sure doldu. Background job cagirir.
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
    /// <param name="extension">Eklenecek sure.</param>
    /// <param name="maxExtensions">Izin verilen maksimum uzatma sayisi.</param>
    public void Extend(TimeSpan extension, int maxExtensions, DateTimeOffset now)
    {
        if (Status != ReservationStatus.Locked)
        {
            throw new DomainException(
                "Yalnizca kilitli rezervasyonun suresi uzatilabilir.",
                "reservation.not_extendable");
        }

        if (IsExpiredAt(now))
        {
            // Suresi dolmus rezervasyonu "uzatmak" onu diriltmek olurdu.
            // Koltuklar bu arada baskasina satilmis olabilir.
            throw new DomainException(
                "Suresi dolmus rezervasyon uzatilamaz.",
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

    /// <summary>Rezervasyondaki bilet sayisi.</summary>
    public int GetTicketCount() => _items.Count;
}
