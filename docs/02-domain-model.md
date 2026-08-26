# Sprint 1 — Domain Modeli ve Durum Makineleri

Bu dokümandaki C# kodları Sprint 2'de `Ticketing.Domain` projesine olduğu gibi taşınacak.
Her birinin neden böyle yazıldığını altında açıkladım.

---

## 1. Enum Tanımları

### Neden enum değerlerine açıkça sayı veriyorum?

```csharp
namespace Ticketing.Domain.Enums;

public enum EventStatus
{
    Draft            = 1,
    PendingApproval  = 2,
    Published        = 3,
    SalesOpen        = 4,
    SalesClosed      = 5,
    Completed        = 6,
    Cancelled        = 7,
    Suspended        = 8
}
```

Enum değerlerini veritabanına `int` olarak yazacağız. Eğer sayıları elle vermezsem
C# bunları sırayla 0, 1, 2... diye atar. O zaman listenin ortasına yeni bir durum
eklediğim gün — mesela `Draft` ile `PendingApproval` arasına bir şey koyduğumda —
sonraki tüm değerler kayar. Veritabanındaki eski kayıtlar bir anda yanlış duruma
işaret etmeye başlar ve bunu fark etmek çok zordur. Sayıları sabitleyerek bu riski
tamamen ortadan kaldırıyorum.

`0`'dan değil `1`'den başlattım. Çünkü C#'ta bir enum alanının varsayılan değeri
her zaman `0`'dır. Eğer `Draft = 0` olsaydı, birisi `Status` alanını hiç set etmeden
kayıt oluşturduğunda o kayıt sessizce `Draft` olurdu ve hata görünmezdi. `0` hiçbir
duruma karşılık gelmediğinde, "atanmamış" hatası hemen ortaya çıkar.

```csharp
public enum ReservationStatus
{
    Pending         = 1,   // Kayıt oluşturuldu, koltuklar henüz kilitlenmedi
    Locked          = 2,   // Koltuklar kilitli, geri sayım işliyor
    PaymentPending  = 3,   // Ödeme başlatıldı, sonuç bekleniyor
    Confirmed       = 4,   // Ödeme başarılı, biletler üretildi
    Expired         = 5,   // Süre doldu, koltuklar serbest bırakıldı
    Cancelled       = 6,   // Kullanıcı veya sistem iptal etti
    Refunded        = 7    // İade edildi
}

public enum PaymentStatus
{
    Pending     = 1,
    Processing  = 2,
    Successful  = 3,
    Failed      = 4,
    Cancelled   = 5,
    Refunded    = 6
}

public enum TicketStatus
{
    Active     = 1,   // Geçerli bilet
    Used       = 2,   // Girişte QR okutuldu
    Cancelled  = 3,   // İptal, iade yok
    Refunded   = 4,   // İptal + para iadesi yapıldı
    Expired    = 5    // Etkinlik geçti, kullanılmadı
}
```

### PDF'te olmayan ama şart olan bir enum

```csharp
public enum EventSeatStatus
{
    Available   = 1,   // Satın alınabilir
    Locked      = 2,   // Bir rezervasyon tarafından geçici kilitli
    Sold        = 3,   // Ödemesi tamamlanmış, satılmış
    Blocked     = 4    // Organizatör/admin satışa kapattı (teknik ekip koltuğu vb.)
}
```

PDF bu enum'u listelemiyor ama `EventSeats` tablosunu istiyor. Koltuğun oturum
bazındaki durumunu tutan bir alan olmadan koltuk haritasını çizmek imkânsız.
`Blocked` durumunu da ekledim: gerçek salonlarda ses/ışık masası, engelli erişim
koridoru gibi sebeplerle satışa kapatılan koltuklar olur.

---

## 2. Durum Makineleri (State Machines)

### Rezervasyon durum geçişleri

```
                    POST /reservations
                            │
                            ▼
                        [Locked] ◄──────── ödeme başarısız
                       ╱    │    ╲              ▲
        süre doldu   ╱      │      ╲ kullanıcı  │
                   ╱        │        ╲ iptal    │
                  ▼         │          ▼        │
            [Expired]       │      [Cancelled]  │
                            │                   │
                POST /payments                  │
                            │                   │
                            ▼                   │
                    [PaymentPending] ────────────┘
                            │
                            │ ödeme başarılı
                            ▼
                      [Confirmed] ──── iade ───► [Refunded]
```

**İzin verilen geçişler tablosu:**

| Mevcut durum | Yeni durum | Tetikleyen |
|---|---|---|
| `Locked` | `PaymentPending` | Kullanıcı ödeme başlatır |
| `Locked` | `Expired` | Background job (süre doldu) |
| `Locked` | `Cancelled` | Kullanıcı iptal eder / etkinlik iptal olur |
| `PaymentPending` | `Confirmed` | Ödeme başarılı |
| `PaymentPending` | `Locked` | Ödeme başarısız (süre varsa) |
| `PaymentPending` | `Expired` | Ödeme sürerken süre doldu |
| `Confirmed` | `Refunded` | İade tamamlandı |

Bu tabloda **olmayan her geçiş yasaktır.** Örneğin `Expired → PaymentPending`
geçişi listede yok; bu, PDF'teki *"süresi dolmuş rezervasyon üzerinden ödeme
başlatılamaz"* kuralının koddaki karşılığıdır.

### Bunu koda nasıl döküyorum?

```csharp
namespace Ticketing.Domain.Entities;

public class Reservation
{
    // private set: durum sadece bu sınıfın içinden değiştirilebilir.
    // Dışarıdan reservation.Status = ReservationStatus.Confirmed; yazılamaz.
    public ReservationStatus Status { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }

    // İzin verilen geçişlerin tek kaynağı. Yukarıdaki tablonun birebir karşılığı.
    private static readonly Dictionary<ReservationStatus, ReservationStatus[]> AllowedTransitions = new()
    {
        [ReservationStatus.Locked]         = [ReservationStatus.PaymentPending,
                                              ReservationStatus.Expired,
                                              ReservationStatus.Cancelled],
        [ReservationStatus.PaymentPending] = [ReservationStatus.Confirmed,
                                              ReservationStatus.Locked,
                                              ReservationStatus.Expired],
        [ReservationStatus.Confirmed]      = [ReservationStatus.Refunded]
    };

    private void TransitionTo(ReservationStatus target)
    {
        if (!AllowedTransitions.TryGetValue(Status, out var allowed) || !allowed.Contains(target))
            throw new DomainException($"Rezervasyon {Status} durumundan {target} durumuna geçemez.");

        Status = target;
    }

    public void StartPayment()
    {
        // Süre kontrolünü geçişten ÖNCE yapıyorum.
        // Çünkü bu bir iş kuralı ihlali, geçersiz bir durum geçişi değil.
        // Kullanıcıya "süreniz doldu" demek, "geçiş yapılamaz" demekten daha anlamlı.
        if (DateTimeOffset.UtcNow >= ExpiresAt)
            throw new DomainException("Rezervasyon süresi dolmuş, ödeme başlatılamaz.");

        TransitionTo(ReservationStatus.PaymentPending);
    }

    public void Confirm() => TransitionTo(ReservationStatus.Confirmed);
    public void Expire()  => TransitionTo(ReservationStatus.Expired);
}
```

**Neden `private set` kullanıyorum?**

Eğer `Status` alanı `public set` olsaydı, projenin herhangi bir yerinde birisi
`reservation.Status = ReservationStatus.Confirmed;` yazabilirdi. Ödeme yapılmadan
rezervasyon onaylanmış olurdu ve bunu hiçbir test yakalayamazdı — çünkü kural
kodun hiçbir yerinde yazılı olmazdı.

`private set` + davranış metodları ile kural **tek bir yerde** yaşıyor. Bu, PDF'in
istediği "Domain kuralları" maddesinin gerçek anlamı: kural entity'nin içinde olmalı,
handler'ın içinde `if` olarak değil. Handler'da yazarsan aynı kuralı 5 farklı yerde
tekrarlaman gerekir ve biri mutlaka unutulur.

**Neden `Dictionary` ile yaptım da dev bir `switch` yazmadım?**

`switch` de çalışırdı ama geçiş kuralları koda dağılırdı. Dictionary'de tüm kurallar
tek bir yerde, dokümandaki tabloyla birebir karşılaştırılabilir halde duruyor.
Yeni bir geçiş eklemek gerektiğinde tek satır ekliyorum ve gözden kaçırma ihtimalim
düşük oluyor.

---

## 3. Value Object: Money

PDF'in Sprint 6 kuralı: *"Para değerleri decimal olarak tutulmalıdır. Floating point
kullanılmamalıdır. Currency alanı bulunmalıdır."*

```csharp
namespace Ticketing.Domain.ValueObjects;

public readonly record struct Money
{
    public decimal Amount { get; }
    public string Currency { get; }

    public Money(decimal amount, string currency)
    {
        if (amount < 0)
            throw new DomainException("Tutar negatif olamaz.");
        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3)
            throw new DomainException("Para birimi 3 harfli ISO 4217 kodu olmalıdır (TRY, USD, EUR).");

        // Kuruş hassasiyetinde yuvarlıyorum. Bankacılık standardı olan
        // MidpointRounding.ToEven ("banker's rounding") kullanıyorum:
        // 2.125 -> 2.12, 2.135 -> 2.14. Sürekli yukarı yuvarlamanın aksine
        // çok sayıda işlemde sistematik sapma oluşturmaz.
        Amount = Math.Round(amount, 2, MidpointRounding.ToEven);
        Currency = currency.ToUpperInvariant();
    }

    public static Money Zero(string currency) => new(0m, currency);

    public static Money operator +(Money a, Money b)
    {
        // Farklı para birimlerini toplamaya çalışmak bir programlama hatasıdır,
        // kullanıcı hatası değil. Sessizce dönüştürmek yerine patlatıyorum.
        if (a.Currency != b.Currency)
            throw new DomainException($"Farklı para birimleri toplanamaz: {a.Currency} + {b.Currency}");

        return new Money(a.Amount + b.Amount, a.Currency);
    }

    public static Money operator *(Money money, int quantity)
        => new(money.Amount * quantity, money.Currency);
}
```

**Neden `decimal`, `double` değil?**

`double` ikili (binary) tabanda çalışır ve `0.1` sayısını tam olarak temsil edemez.
`0.1 + 0.2` işlemi `double` ile `0.30000000000000004` verir. 10.000 biletlik bir
etkinlikte bu hatalar birikir ve raporlarda tutmayan kuruşlar ortaya çıkar.
`decimal` ondalık tabanda çalışır, para için tasarlanmıştır.

**Neden `record struct`?**

Para bir kimlik değil, bir değerdir. 100 TL ile 100 TL aynı şeydir — hangi nesne
olduğunun önemi yoktur. `record` bana bu değer bazlı eşitliği bedavaya veriyor.
`struct` olması da her para işleminde heap'te yeni nesne oluşturmayı engelliyor.
`readonly` ise oluşturulduktan sonra değiştirilemez olmasını garantiliyor —
paranın sessizce değişmesi istemeyeceğim son şey.

---

## 4. Ortak Taban Sınıflar

```csharp
namespace Ticketing.Domain.Common;

// Denetim alanları: PDF'in "CreatedAt ve UpdatedAt alanları" + "Audit alanları" maddeleri
public abstract class AuditableEntity
{
    public Guid Id { get; protected set; } = Guid.CreateVersion7();

    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    // Soft delete
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}
```

**Neden `Guid.CreateVersion7()`, klasik `Guid.NewGuid()` değil?**

Klasik GUID (v4) tamamen rastgeledir. Primary key olarak kullanıldığında veritabanı
index'i sürekli parçalanır (fragmentation), çünkü yeni kayıtlar index'in rastgele
yerlerine girer. UUID v7 ise zaman sıralıdır — ilk 48 biti timestamp'tir. Yeni
kayıtlar hep index'in sonuna eklenir, tıpkı auto-increment gibi. GUID'in dağıtık
sistemlerde çakışmama avantajını korurken performans sorununu çözer.
.NET 9 ile geldi, projemiz .NET 9 hedeflediği için kullanabiliriz.

**Neden `int` yerine `Guid`?**

Bilet ID'si URL'de görünüyor. `int` olsaydı kullanıcı `/tickets/1234` adresini
`/tickets/1235` yapıp başkasının biletini denemeye kalkardı. (Yetkilendirme bunu
zaten engelleyecek ama savunma katmanlarını çoğaltmak iyidir.) Ayrıca `Guid` ile
ID'yi veritabanına gitmeden uygulama tarafında üretebiliyorum — Outbox pattern'de
bu çok işime yarayacak.

**Neden `DateTimeOffset`, `DateTime` değil?**

PDF: *"Tarih ve saat bilgilerinin UTC tutulması"*. `DateTime` saat dilimi bilgisi
taşımaz; bir `DateTime`'a bakıp UTC mi yerel saat mi olduğunu anlayamazsın.
`DateTimeOffset` offset bilgisini de saklar, bu belirsizliği ortadan kaldırır.
Rezervasyon süresi hesaplarken bir saatlik bir hata felaket olur.

```csharp
// Eş zamanlılık kontrolü gereken entity'ler bundan türeyecek
public abstract class ConcurrentEntity : AuditableEntity
{
    // PostgreSQL'de bu alanı "xmin" sistem sütununa eşleyeceğiz.
    // EF Core her UPDATE'e otomatik olarak
    //   WHERE Id = @id AND xmin = @okunanDeger
    // koşulunu ekler. Araya başkası girip satırı değiştirmişse
    // etkilenen satır sayısı 0 döner ve EF DbUpdateConcurrencyException fırlatır.
    public uint RowVersion { get; set; }
}
```

Bu sınıftan türeyecekler: `EventSeat` (en kritik olan), `Reservation`, `Payment`, `Event`.

---

## 5. Sprint 2'de Yazacağımız Entity Listesi

PDF'in ER diyagramında 28 tablo var. Bunları bağımlılık sırasına göre yazacağız:

**Seviye 1 — hiçbir şeye bağlı değil:**
`Role`, `City`, `EventCategory`, `User`

**Seviye 2 — Seviye 1'e bağlı:**
`UserRole`, `RefreshToken`, `OrganizerProfile`, `OrganizerApplication`, `Venue`

**Seviye 3:**
`Hall`, `SeatLayout`

**Seviye 4:**
`SeatSection`, `Seat`

**Seviye 5:**
`Event`, `EventSession`, `TicketType`

**Seviye 6 — projenin kalbi:**
`EventSeat`

**Seviye 7:**
`Reservation`, `ReservationItem`

**Seviye 8:**
`Payment`, `PaymentTransaction`, `Ticket`, `TicketQrCode`

**Bağımsız / yatay:**
`Favorite`, `Review`, `Notification`, `AuditLog`, `OutboxMessage`, `UploadedFile`

Bu sırayla yazmamın sebebi: her entity yazıldığında bağlı olduğu her şey zaten
hazır olacak. Böylece derleme hatası almadan ilerleyeceğiz ve tek bir migration'da
tüm şema oluşacak.
