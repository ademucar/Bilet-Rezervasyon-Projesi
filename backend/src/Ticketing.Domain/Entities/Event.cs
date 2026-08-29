using System.Diagnostics.CodeAnalysis;
using Ticketing.Domain.Common;
using Ticketing.Domain.Enums;
using Ticketing.Domain.Events;
using Ticketing.Domain.ValueObjects;

namespace Ticketing.Domain.Entities;

/// <summary>
/// Etkinlik. PDF Sprint 5.
///
/// ConcurrentEntity'den turuyor çünkü organizatör ve admin aynı etkinligi
/// aynı anda düzenleyebilir (biri fiyat degistirirken digeri askiya alabilir).
/// Optimistic concurrency ile kaybeden istek 409 alacak.
/// </summary>
[SuppressMessage(
    "Naming",
    "CA1716:Identifiers should not match keywords",
    Justification =
        "CA1716, 'Event' adinin VB.NET'te ayrilmis bir anahtar kelime olmasını " +
        "sebebiyle uyarir. Bu kural, sinifin BASKA .NET dillerinden kullanilacagi " +
        "senaryolar için vardir. Projemiz tamamen C# ve başka bir dilden " +
        "tuketilmeyecek. " +
        "Buna karsilik PDF'in ER diyagraminda tablo adı acikca 'Events' olarak " +
        "belirtilmis; sinifi TicketEvent gibi bir adla degistirmek sartnameden " +
        "sapma olurdu ve domain dilini (ubiquitous language) bozardi. " +
        "Bu yüzden kuralı EN DAR kapsamda, yalnızca bu sinif için bastiriyorum -- " +
        "proje genelinde NoWarn ile kapatmak yerine.")]
public class Event : ConcurrentEntity
{
    private Event()
    {
        Title = string.Empty;
        Description = string.Empty;
        CancellationPolicy = CancellationPolicy.Default;
    }

    // DURUM MAKINESI

    /// <summary>
    /// Izin verilen durum gecislerinin TEK kaynagi.
    /// docs/01-is-analizi.md soru 4'teki tablonun birebir karşılığı.
    ///
    /// Neden dev bir switch yerine Dictionary?
    ///
    /// switch de calisirdi ama gecis kurallari koda dagilirdi. Burada tüm
    /// kurallar tek bir yerde, dokumandaki tabloyla yan yana konulup
    /// karsilastirilabilir halde duruyor. Yeni bir gecis eklerken tek satır
    /// ekliyorum ve başka bir yeri unutma ihtimalim yok.
    ///
    /// Ayrıca: bu sozlukte OLMAYAN her gecis YASAKTIR. Yani kural
    /// "neyin yasak olduğunu say" değil, "neyin serbest olduğunu say"
    /// seklinde. Yeni bir durum eklendiginde varsayılan davranis
    /// "hiçbir yere gecemez" olur -- güvenli taraf.
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
        ],

        // Completed ve Cancelled bilerek YOK.
        // Bunlar son durumlar; hiçbir yere gecemezler.
    };

    // Alanlar (PDF sayfa 12 "Etkinlik Alanlari")

    public string Title { get; private set; }

    public string Description { get; private set; }

    public Guid CategoryId { get; private set; }

    public Guid OrganizerId { get; private set; }

    public Guid CityId { get; private set; }

    public Guid VenueId { get; private set; }

    public Guid HallId { get; private set; }

    /// <summary>
    /// Afis gorselinin depolama yolu. Tam URL DEĞİL, gorece yol saklariz.
    ///
    /// Neden? Bugun dosyalar yerel diskte, yarin S3'e tasinabilir.
    /// Tam URL saklasaydik ("https://eski-sunucu.com/img/1.jpg"), tasima
    /// gununde veritabanindaki binlerce satiri guncellememiz gerekirdi.
    /// Gorece yol saklayip URL'i okuma anında uretmek bizi buna baglanmaktan
    /// kurtarir.
    /// </summary>
    public string? PosterImagePath { get; private set; }

    /// <summary>Yaş sınırı. 0 = sinir yok.</summary>
    public int MinimumAge { get; private set; }

    /// <summary>Etkinlik süresi (dakika).</summary>
    public int DurationMinutes { get; private set; }

    public DateTimeOffset SalesStartDate { get; private set; }

    public DateTimeOffset SalesEndDate { get; private set; }

    public DateTimeOffset EventDate { get; private set; }

    public EventStatus Status { get; private set; }

    public CancellationPolicy CancellationPolicy { get; private set; }

    /// <summary>
    /// Bir kullanıcının bu etkinlikten alabilecegi maksimum bilet sayısı.
    /// PDF Sprint 7: "Bir kullanıcı aynı oturum için belirlenen maksimum
    /// bilet sayisini aşamaz."
    ///
    /// Karaborsaciligi engellemek için. Popüler konserlerde tipik deger 4-6.
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

    // Olusturma

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
            throw new DomainException("Etkinlik basligi boş olamaz.", "event.title_required");
        }

        if (durationMinutes <= 0)
        {
            throw new DomainException("Etkinlik süresi sıfırdan büyük olmalıdır.", "event.invalid_duration");
        }

        if (maxTicketsPerUser <= 0)
        {
            throw new DomainException(
                "Kullanıcı başına bilet limiti sıfırdan büyük olmalıdır.",
                "event.invalid_ticket_limit");
        }

        if (minimumAge < 0)
        {
            throw new DomainException("Yaş sınırı negatif olamaz.", "event.invalid_minimum_age");
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
            CancellationPolicy = CancellationPolicy.Default,
        };
    }

    /// <summary>
    /// PDF sayfa 13'teki tarih kurallari:
    ///   - "Satış başlangıç tarihi satış bitiş tarihinden sonra olamaz."
    ///   - "Satış bitiş tarihi etkinlik baslangicindan sonra olamaz."
    ///
    /// Bu metodu static ve private yaptım: hem Create hem UpdateDates
    /// aynı kuralı kullaniyor. Iki yerde ayrı ayrı yazsaydim biri
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
                "Satış başlangıç tarihi, satış bitiş tarihinden önce olmalıdır.",
                "event.invalid_sales_period");
        }

        if (salesEndDate > eventDate)
        {
            throw new DomainException(
                "Satış bitiş tarihi, etkinlik baslangicindan sonra olamaz.",
                "event.sales_end_after_event");
        }
    }

    // Durum gecisleri

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

    /// <summary>Organizatör etkinligi onaya gönderir.</summary>
    public void SubmitForApproval()
    {
        // Onaya gondermeden önce en az bir oturum olmalı.
        // Oturumsuz etkinlik satilamaz; admin'in onune boş bir kayıt
        // gitmesinin anlami yok.
        if (_sessions.Count == 0)
        {
            throw new DomainException(
                "Onaya gondermeden önce en az bir oturum eklenmelidir.",
                "event.no_sessions");
        }

        if (_ticketTypes.Count == 0)
        {
            throw new DomainException(
                "Onaya gondermeden önce en az bir bilet türü tanimlanmalidir.",
                "event.no_ticket_types");
        }

        TransitionTo(EventStatus.PendingApproval);
    }

    /// <summary>Admin onaylar, etkinlik yayina alinir.</summary>
    public void Publish()
    {
        TransitionTo(EventStatus.Published);

        // Domain event'i durum degistikten SONRA firlatiyorum.
        // Önce firlatsaydim, TransitionTo hata verdiginde "yayinlandi"
        // diye bir olay duyurmus olurdum -- oysa yayinlanmadi.
        Raise(new EventPublishedDomainEvent(Id, OrganizerId, Title, DateTimeOffset.UtcNow));
    }

    /// <summary>Admin onayı reddeder, taslaga geri döner.</summary>
    public void Reject() => TransitionTo(EventStatus.Draft);

    /// <summary>
    /// Satışı acar. Normalde background job cagirir (dakikada bir kontrol).
    /// </summary>
    public void OpenSales() => TransitionTo(EventStatus.SalesOpen);

    public void CloseSales() => TransitionTo(EventStatus.SalesClosed);

    public void Complete() => TransitionTo(EventStatus.Completed);

    public void Suspend() => TransitionTo(EventStatus.Suspended);

    /// <summary>Askidan cikarip yayina geri alır.</summary>
    public void Reinstate() => TransitionTo(EventStatus.Published);

    public void Cancel(string? reason = null)
    {
        TransitionTo(EventStatus.Cancelled);

        CancellationReason = reason;
        CancelledAt = DateTimeOffset.UtcNow;

        // Rezervasyon iptali, iade, bildirim... hicbirini BURADA yapmiyorum.
        // Event sinifinin ödeme servisini veya e-posta servisini bilmesi
        // gerekseydi Domain katmani altyapiya bagimli olurdu ve
        // architecture testim kırmızı yanardi.
        //
        // Sadece "iptal edildim" diyorum; gerisini Application katmanindaki
        // handler halleder.
        Raise(new EventCancelledDomainEvent(Id, OrganizerId, Title, reason, DateTimeOffset.UtcNow));
    }

    // Guncelleme kurallari

    /// <summary>
    /// Satış baslamis mi? Bu soru güncelleme kurallarinin merkezinde.
    ///
    /// Metot, property değil: Status'e bakan bir hesaplama ve ileride
    /// daha karmasik hale gelebilir (örneğin satılmış bilet sayısı kontrolü).
    /// </summary>
    public bool HasSalesStarted()
        => Status is EventStatus.SalesOpen or EventStatus.SalesClosed or EventStatus.Completed;

    /// <summary>
    /// PDF: "Yayina alinmis etkinliğin kritik alanlari kontrolsuz degistirilemez."
    ///
    /// Kritik alanlar: tarih, salon, mekan. Bunlar degisirse bilet almis
    /// kullanıcının planı bozulur -- başka bir sehre gitmesi gerekebilir.
    /// </summary>
    public void UpdateDates(
        DateTimeOffset eventDate,
        DateTimeOffset salesStartDate,
        DateTimeOffset salesEndDate)
    {
        if (HasSalesStarted())
        {
            throw new DomainException(
                "Satışı baslamis etkinliğin tarihleri degistirilemez. " +
                "Önce etkinligi askiya alin veya iptal edin.",
                "event.sales_started");
        }

        ValidateDates(eventDate, salesStartDate, salesEndDate);

        EventDate = eventDate;
        SalesStartDate = salesStartDate;
        SalesEndDate = salesEndDate;
    }

    /// <summary>
    /// Başlık ve açıklama gibi kritik OLMAYAN alanlar. Bunlar yayindayken
    /// de degistirilebilir -- yazım hatası duzeltmek yasak olmamali.
    /// İptal edilmiş etkinlikte ise hiçbir sey degismez.
    /// </summary>
    public void UpdateDetails(string title, string description, int? minimumAge = null)
    {
        if (Status is EventStatus.Cancelled or EventStatus.Completed)
        {
            throw new DomainException(
                "İptal edilmiş veya tamamlanmis etkinlik düzenlenemez.",
                "event.not_editable");
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainException("Etkinlik basligi boş olamaz.", "event.title_required");
        }

        Title = title.Trim();
        Description = description?.Trim() ?? string.Empty;

        if (minimumAge.HasValue)
        {
            if (minimumAge.Value < 0)
            {
                throw new DomainException("Yaş sınırı negatif olamaz.", "event.invalid_minimum_age");
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
            // Satış basladiktan sonra iade politikasini degistirmek
            // sozlesme ihlalidir: kullanıcı bileti "7 gün kala tam iade"
            // vaadiyle aldi. Sonradan degistirilmesi haksizlik olur.
            throw new DomainException(
                "Satışı baslamis etkinliğin iade politikasi degistirilemez.",
                "event.sales_started");
        }

        CancellationPolicy = policy;
    }

    // Oturum yönetimi

    public EventSession AddSession(
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        Guid hallId,
        Guid seatLayoutId)
    {
        if (HasSalesStarted())
        {
            throw new DomainException(
                "Satışı baslamis etkinlige yeni oturum eklenemez.",
                "event.sales_started");
        }

        // Aynı etkinlik icindeki oturumlar birbiriyle cakisamaz.
        //
        // DIKKAT: Bu kontrol sadece BU etkinliğin oturumlarini kapsar.
        // PDF'in "Aynı salon aynı zaman araliginda iki ETKINLIGE atanamaz"
        // kuralı FARKLI etkinlikler arasindaki cakismayi da kapsiyor ve
        // önü buradan kontrol edemem -- diger etkinliklerin oturumlari
        // bellekte değil, veritabaninda.
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
                "Bu salonda aynı saatte başka bir oturum var.",
                "event.session_overlap");
        }

        var session = EventSession.Create(Id, startDate, endDate, hallId, seatLayoutId);
        _sessions.Add(session);

        return session;
    }

    /// <summary>
    /// Etkinlige bilet türü ekler.
    ///
    /// AddSession ile aynı kalibi kullanıyorum: nesneyi disarida uretip
    /// iceri vermek yerine, üretimi de bu metot yapiyor.
    ///
    /// Sebep: boylece çakışma kontrolü (aynı isimde iki bilet türü) ve
    /// ekleme islemi atomik olarak burada kaliyor. Disarida uretilseydi
    /// cagiran kişi kontrol etmeden doğrudan listeye ekleyebilirdi.
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
                "Satışı baslamis etkinlige yeni bilet türü eklenemez.",
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
