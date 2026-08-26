using System.Diagnostics.CodeAnalysis;
using Ticketing.Domain.Common;
using Ticketing.Domain.Enums;
using Ticketing.Domain.Events;
using Ticketing.Domain.ValueObjects;

namespace Ticketing.Domain.Entities;

/// <summary>
/// Etkinlik. PDF Sprint 5.
///
/// ConcurrentEntity'den turuyor cunku organizator ve admin ayni etkinligi
/// ayni anda duzenleyebilir (biri fiyat degistirirken digeri askiya alabilir).
/// Optimistic concurrency ile kaybeden istek 409 alacak.
/// </summary>
[SuppressMessage(
    "Naming",
    "CA1716:Identifiers should not match keywords",
    Justification =
        "CA1716, 'Event' adinin VB.NET'te ayrilmis bir anahtar kelime olmasi " +
        "sebebiyle uyarir. Bu kural, sinifin BASKA .NET dillerinden kullanilacagi " +
        "senaryolar icin vardir. Projemiz tamamen C# ve baska bir dilden " +
        "tuketilmeyecek. " +
        "Buna karsilik PDF'in ER diyagraminda tablo adi acikca 'Events' olarak " +
        "belirtilmis; sinifi TicketEvent gibi bir adla degistirmek sartnameden " +
        "sapma olurdu ve domain dilini (ubiquitous language) bozardi. " +
        "Bu yuzden kurali EN DAR kapsamda, yalnizca bu sinif icin bastiriyorum -- " +
        "proje genelinde NoWarn ile kapatmak yerine.")]
public class Event : ConcurrentEntity
{
    private Event()
    {
        Title = string.Empty;
        Description = string.Empty;
        CancellationPolicy = CancellationPolicy.Default;
    }

    // ---------------------------------------------------------------
    // DURUM MAKINESI
    // ---------------------------------------------------------------

    /// <summary>
    /// Izin verilen durum gecislerinin TEK kaynagi.
    /// docs/01-is-analizi.md soru 4'teki tablonun birebir karsiligi.
    ///
    /// Neden dev bir switch yerine Dictionary?
    ///
    /// switch de calisirdi ama gecis kurallari koda dagilirdi. Burada tum
    /// kurallar tek bir yerde, dokumandaki tabloyla yan yana konulup
    /// karsilastirilabilir halde duruyor. Yeni bir gecis eklerken tek satir
    /// ekliyorum ve baska bir yeri unutma ihtimalim yok.
    ///
    /// Ayrica: bu sozlukte OLMAYAN her gecis YASAKTIR. Yani kural
    /// "neyin yasak oldugunu say" degil, "neyin serbest oldugunu say"
    /// seklinde. Yeni bir durum eklendiginde varsayilan davranis
    /// "hicbir yere gecemez" olur -- guvenli taraf.
    /// </summary>
    private static readonly Dictionary<EventStatus, EventStatus[]> AllowedTransitions = new()
    {
        [EventStatus.Draft] =
        [
            EventStatus.PendingApproval,
            EventStatus.Cancelled
        ],
        [EventStatus.PendingApproval] =
        [
            EventStatus.Published,
            EventStatus.Draft,          // admin reddetti
            EventStatus.Cancelled
        ],
        [EventStatus.Published] =
        [
            EventStatus.SalesOpen,
            EventStatus.Cancelled,
            EventStatus.Suspended
        ],
        [EventStatus.SalesOpen] =
        [
            EventStatus.SalesClosed,
            EventStatus.Cancelled,
            EventStatus.Suspended
        ],
        [EventStatus.SalesClosed] =
        [
            EventStatus.Completed,
            EventStatus.Cancelled
        ],
        [EventStatus.Suspended] =
        [
            EventStatus.Published,
            EventStatus.Cancelled
        ]

        // Completed ve Cancelled bilerek YOK.
        // Bunlar son durumlar; hicbir yere gecemezler.
    };

    // ---------------------------------------------------------------
    // Alanlar (PDF sayfa 12 "Etkinlik Alanlari")
    // ---------------------------------------------------------------

    public string Title { get; private set; }

    public string Description { get; private set; }

    public Guid CategoryId { get; private set; }

    public Guid OrganizerId { get; private set; }

    public Guid CityId { get; private set; }

    public Guid VenueId { get; private set; }

    public Guid HallId { get; private set; }

    /// <summary>
    /// Afis gorselinin depolama yolu. Tam URL DEGIL, gorece yol saklariz.
    ///
    /// Neden? Bugun dosyalar yerel diskte, yarin S3'e tasinabilir.
    /// Tam URL saklasaydik ("https://eski-sunucu.com/img/1.jpg"), tasima
    /// gununde veritabanindaki binlerce satiri guncellememiz gerekirdi.
    /// Gorece yol saklayip URL'i okuma aninda uretmek bizi buna baglanmaktan
    /// kurtarir.
    /// </summary>
    public string? PosterImagePath { get; private set; }

    /// <summary>Yas siniri. 0 = sinir yok.</summary>
    public int MinimumAge { get; private set; }

    /// <summary>Etkinlik suresi (dakika).</summary>
    public int DurationMinutes { get; private set; }

    public DateTimeOffset SalesStartDate { get; private set; }

    public DateTimeOffset SalesEndDate { get; private set; }

    public DateTimeOffset EventDate { get; private set; }

    public EventStatus Status { get; private set; }

    public CancellationPolicy CancellationPolicy { get; private set; }

    /// <summary>
    /// Bir kullanicinin bu etkinlikten alabilecegi maksimum bilet sayisi.
    /// PDF Sprint 7: "Bir kullanici ayni oturum icin belirlenen maksimum
    /// bilet sayisini asamaz."
    ///
    /// Karaborsaciligi engellemek icin. Populer konserlerde tipik deger 4-6.
    /// </summary>
    public int MaxTicketsPerUser { get; private set; }

    public string? CancellationReason { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    // Navigation'lar
    public EventCategory Category { get; private set; } = null!;

    public City City { get; private set; } = null!;

    public Venue Venue { get; private set; } = null!;

    public Hall Hall { get; private set; } = null!;

    private readonly List<EventSession> _sessions = [];

    public IReadOnlyCollection<EventSession> Sessions => _sessions.AsReadOnly();

    private readonly List<TicketType> _ticketTypes = [];

    public IReadOnlyCollection<TicketType> TicketTypes => _ticketTypes.AsReadOnly();

    // ---------------------------------------------------------------
    // Olusturma
    // ---------------------------------------------------------------

    public static Event Create(
        string title,
        string description,
        Guid categoryId,
        Guid organizerId,
        Guid cityId,
        Guid venueId,
        Guid hallId,
        DateTimeOffset eventDate,
        DateTimeOffset salesStartDate,
        DateTimeOffset salesEndDate,
        int durationMinutes,
        int maxTicketsPerUser = 4,
        int minimumAge = 0)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainException("Etkinlik basligi bos olamaz.", "event.title_required");
        }

        if (durationMinutes <= 0)
        {
            throw new DomainException("Etkinlik suresi sifirdan buyuk olmalidir.", "event.invalid_duration");
        }

        if (maxTicketsPerUser <= 0)
        {
            throw new DomainException(
                "Kullanici basina bilet limiti sifirdan buyuk olmalidir.",
                "event.invalid_ticket_limit");
        }

        if (minimumAge < 0)
        {
            throw new DomainException("Yas siniri negatif olamaz.", "event.invalid_minimum_age");
        }

        ValidateDates(eventDate, salesStartDate, salesEndDate);

        return new Event
        {
            Title = title.Trim(),
            Description = description?.Trim() ?? string.Empty,
            CategoryId = categoryId,
            OrganizerId = organizerId,
            CityId = cityId,
            VenueId = venueId,
            HallId = hallId,
            EventDate = eventDate,
            SalesStartDate = salesStartDate,
            SalesEndDate = salesEndDate,
            DurationMinutes = durationMinutes,
            MaxTicketsPerUser = maxTicketsPerUser,
            MinimumAge = minimumAge,
            Status = EventStatus.Draft,
            CancellationPolicy = CancellationPolicy.Default
        };
    }

    /// <summary>
    /// PDF sayfa 13'teki tarih kurallari:
    ///   - "Satis baslangic tarihi satis bitis tarihinden sonra olamaz."
    ///   - "Satis bitis tarihi etkinlik baslangicindan sonra olamaz."
    ///
    /// Bu metodu static ve private yaptim: hem Create hem UpdateDates
    /// ayni kurali kullaniyor. Iki yerde ayri ayri yazsaydim biri
    /// guncellenirken digeri unutulurdu -- klasik hata.
    /// </summary>
    private static void ValidateDates(
        DateTimeOffset eventDate,
        DateTimeOffset salesStartDate,
        DateTimeOffset salesEndDate)
    {
        if (salesStartDate >= salesEndDate)
        {
            throw new DomainException(
                "Satis baslangic tarihi, satis bitis tarihinden once olmalidir.",
                "event.invalid_sales_period");
        }

        if (salesEndDate > eventDate)
        {
            throw new DomainException(
                "Satis bitis tarihi, etkinlik baslangicindan sonra olamaz.",
                "event.sales_end_after_event");
        }
    }

    // ---------------------------------------------------------------
    // Durum gecisleri
    // ---------------------------------------------------------------

    private void TransitionTo(EventStatus target)
    {
        if (!AllowedTransitions.TryGetValue(Status, out var allowed) || !Array.Exists(allowed, s => s == target))
        {
            throw new DomainException(
                $"Etkinlik {Status} durumundan {target} durumuna gecemez.",
                "event.invalid_transition");
        }

        Status = target;
    }

    /// <summary>Organizator etkinligi onaya gonderir.</summary>
    public void SubmitForApproval()
    {
        // Onaya gondermeden once en az bir oturum olmali.
        // Oturumsuz etkinlik satilamaz; admin'in onune bos bir kayit
        // gitmesinin anlami yok.
        if (_sessions.Count == 0)
        {
            throw new DomainException(
                "Onaya gondermeden once en az bir oturum eklenmelidir.",
                "event.no_sessions");
        }

        if (_ticketTypes.Count == 0)
        {
            throw new DomainException(
                "Onaya gondermeden once en az bir bilet turu tanimlanmalidir.",
                "event.no_ticket_types");
        }

        TransitionTo(EventStatus.PendingApproval);
    }

    /// <summary>Admin onaylar, etkinlik yayina alinir.</summary>
    public void Publish()
    {
        TransitionTo(EventStatus.Published);

        // Domain event'i durum degistikten SONRA firlatiyorum.
        // Once firlatsaydim, TransitionTo hata verdiginde "yayinlandi"
        // diye bir olay duyurmus olurduk -- oysa yayinlanmadi.
        Raise(new EventPublishedDomainEvent(Id, OrganizerId, Title, DateTimeOffset.UtcNow));
    }

    /// <summary>Admin onayi reddeder, taslaga geri doner.</summary>
    public void Reject() => TransitionTo(EventStatus.Draft);

    /// <summary>
    /// Satisi acar. Normalde background job cagirir (dakikada bir kontrol).
    /// </summary>
    public void OpenSales() => TransitionTo(EventStatus.SalesOpen);

    public void CloseSales() => TransitionTo(EventStatus.SalesClosed);

    public void Complete() => TransitionTo(EventStatus.Completed);

    public void Suspend() => TransitionTo(EventStatus.Suspended);

    /// <summary>Askidan cikarip yayina geri alir.</summary>
    public void Reinstate() => TransitionTo(EventStatus.Published);

    public void Cancel(string? reason = null)
    {
        TransitionTo(EventStatus.Cancelled);

        CancellationReason = reason;
        CancelledAt = DateTimeOffset.UtcNow;

        // Rezervasyon iptali, iade, bildirim... hicbirini BURADA yapmiyorum.
        // Event sinifinin odeme servisini veya e-posta servisini bilmesi
        // gerekseydi Domain katmani altyapiya bagimli olurdu ve
        // architecture testimiz kirmizi yanardi.
        //
        // Sadece "iptal edildim" diyorum; gerisini Application katmanindaki
        // handler halleder.
        Raise(new EventCancelledDomainEvent(Id, OrganizerId, Title, reason, DateTimeOffset.UtcNow));
    }

    // ---------------------------------------------------------------
    // Guncelleme kurallari
    // ---------------------------------------------------------------

    /// <summary>
    /// Satis baslamis mi? Bu soru guncelleme kurallarinin merkezinde.
    ///
    /// Metot, property degil: Status'e bakan bir hesaplama ve ileride
    /// daha karmasik hale gelebilir (ornegin satilmis bilet sayisi kontrolu).
    /// </summary>
    public bool HasSalesStarted()
        => Status is EventStatus.SalesOpen or EventStatus.SalesClosed or EventStatus.Completed;

    /// <summary>
    /// PDF: "Yayina alinmis etkinligin kritik alanlari kontrolsuz degistirilemez."
    ///
    /// Kritik alanlar: tarih, salon, mekan. Bunlar degisirse bilet almis
    /// kullanicinin plani bozulur -- baska bir sehre gitmesi gerekebilir.
    /// </summary>
    public void UpdateDates(
        DateTimeOffset eventDate,
        DateTimeOffset salesStartDate,
        DateTimeOffset salesEndDate)
    {
        if (HasSalesStarted())
        {
            throw new DomainException(
                "Satisi baslamis etkinligin tarihleri degistirilemez. " +
                "Once etkinligi askiya alin veya iptal edin.",
                "event.sales_started");
        }

        ValidateDates(eventDate, salesStartDate, salesEndDate);

        EventDate = eventDate;
        SalesStartDate = salesStartDate;
        SalesEndDate = salesEndDate;
    }

    /// <summary>
    /// Baslik ve aciklama gibi kritik OLMAYAN alanlar. Bunlar yayindayken
    /// de degistirilebilir -- yazim hatasi duzeltmek yasak olmamali.
    /// Iptal edilmis etkinlikte ise hicbir sey degismez.
    /// </summary>
    public void UpdateDetails(string title, string description, int? minimumAge = null)
    {
        if (Status is EventStatus.Cancelled or EventStatus.Completed)
        {
            throw new DomainException(
                "Iptal edilmis veya tamamlanmis etkinlik duzenlenemez.",
                "event.not_editable");
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainException("Etkinlik basligi bos olamaz.", "event.title_required");
        }

        Title = title.Trim();
        Description = description?.Trim() ?? string.Empty;

        if (minimumAge.HasValue)
        {
            if (minimumAge.Value < 0)
            {
                throw new DomainException("Yas siniri negatif olamaz.", "event.invalid_minimum_age");
            }

            MinimumAge = minimumAge.Value;
        }
    }

    public void SetPosterImage(string? path) => PosterImagePath = path;

    public void SetCancellationPolicy(CancellationPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        if (HasSalesStarted())
        {
            // Satis basladiktan sonra iade politikasini degistirmek
            // sozlesme ihlalidir: kullanici bileti "7 gun kala tam iade"
            // vaadiyle aldi. Sonradan degistirilmesi haksizlik olur.
            throw new DomainException(
                "Satisi baslamis etkinligin iade politikasi degistirilemez.",
                "event.sales_started");
        }

        CancellationPolicy = policy;
    }

    // ---------------------------------------------------------------
    // Oturum yonetimi
    // ---------------------------------------------------------------

    public EventSession AddSession(
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        Guid hallId,
        Guid seatLayoutId)
    {
        if (HasSalesStarted())
        {
            throw new DomainException(
                "Satisi baslamis etkinlige yeni oturum eklenemez.",
                "event.sales_started");
        }

        // Ayni etkinlik icindeki oturumlar birbiriyle cakisamaz.
        //
        // DIKKAT: Bu kontrol sadece BU etkinligin oturumlarini kapsar.
        // PDF'in "Ayni salon ayni zaman araliginda iki ETKINLIGE atanamaz"
        // kurali FARKLI etkinlikler arasindaki cakismayi da kapsiyor ve
        // onu buradan kontrol edemem -- diger etkinliklerin oturumlari
        // bellekte degil, veritabaninda.
        //
        // O kural iki yerde uygulanacak (Sprint 5):
        //   1. Application handler'inda veritabani sorgusu ile
        //   2. PostgreSQL EXCLUDE constraint'i ile (son savunma hatti)
        var cakisan = _sessions.Find(s =>
            s.HallId == hallId &&
            s.Status != EventSessionStatus.Cancelled &&
            s.OverlapsWith(startDate, endDate));

        if (cakisan is not null)
        {
            throw new DomainException(
                "Bu salonda ayni saatte baska bir oturum var.",
                "event.session_overlap");
        }

        var session = EventSession.Create(Id, startDate, endDate, hallId, seatLayoutId);
        _sessions.Add(session);

        return session;
    }

    /// <summary>
    /// Etkinlige bilet turu ekler.
    ///
    /// AddSession ile ayni kalibi kullaniyorum: nesneyi disarida uretip
    /// iceri vermek yerine, uretimi de bu metot yapiyor.
    ///
    /// Sebep: boylece cakisma kontrolu (ayni isimde iki bilet turu) ve
    /// ekleme islemi atomik olarak burada kaliyor. Disarida uretilseydi
    /// cagiran kisi kontrol etmeden dogrudan listeye ekleyebilirdi.
    /// </summary>
    public TicketType AddTicketType(
        string name,
        Money price,
        int? quota = null,
        bool requiresStudentVerification = false)
    {
        if (HasSalesStarted())
        {
            throw new DomainException(
                "Satisi baslamis etkinlige yeni bilet turu eklenemez.",
                "event.sales_started");
        }

        if (_ticketTypes.Exists(t => string.Equals(t.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            throw new DomainException(
                $"'{name}' isimli bilet turu bu etkinlikte zaten var.",
                "event.duplicate_ticket_type");
        }

        var ticketType = TicketType.Create(Id, name!, price, quota, requiresStudentVerification);
        _ticketTypes.Add(ticketType);

        return ticketType;
    }
}
