# Sprint 16 — Logging, Monitoring ve Observability

## Bu sprintte ne yaptım?

PDF dört başlık istiyor: Serilog ile 12 olayın loglanması, Correlation
ID'nin beş yerde birden kullanılması, OpenTelemetry ile beş şeyin
izlenmesi ve dört sağlık kontrolü.

Ama bu sprintin asıl hikâyesi listedeki maddeler değil. Correlation
ID'yi doğrulamak için veritabanına baktığımda **sistemin en temel
izlenebilirlik özelliğinin baştan beri hiç çalışmadığını** buldum.

---

## 🔴 Sprint 16'nın ana bulgusu: Correlation ID hiç yazılmıyordu

### Nasıl fark ettim?

PDF diyor ki: *"Correlation ID … Outbox kaydı içerisinde
kullanılmalıdır."* Bu zaten yapılmış gibi görünüyordu:

- `OutboxMessage.CorrelationId` alanı **vardı** (Sprint 9'dan beri)
- `Create()` metodunda parametresi **vardı**
- Veritabanında sütunu ve hatta **indeksi** vardı
- XML yorumunda "PDF Sprint 16" diye işaretliydi

Yine de ölçtüm:

```sql
SELECT "Type", COUNT(*), COUNT("CorrelationId") FROM "OutboxMessages" GROUP BY "Type";
```

| Type | adet | correlation_dolu |
|---|---:|---:|
| ReportExport | 5 | **0** |
| ReservationExpired | 5 | **0** |
| PaymentSucceeded | 2 | **0** |
| TicketsIssued | 2 | **0** |
| ReservationCreated | 2 | **0** |
| … | | |
| **TOPLAM** | **22** | **0** |

`AuditLogs` tablosunda da aynı: 0 dolu.

### İlk teşhisim yanlıştı

Sekiz `OutboxMessage.Create()` çağrı yerinden yedisi parametreyi hiç
geçmiyordu. "Tamam, 7 satır düzeltirim" dedim — ama düzeltmedim, çünkü
aynı hata dokuzuncu çağrı yerinde yine olurdu.

Bunun yerine merkezi bir `OutboxCorrelationInterceptor` yazdım:
kaydetme anında, unutulması mümkün olmayan yerde. Sprint 12'deki
`AuditFieldsInterceptor` kararının aynısı.

Sonra test ettim. **Hâlâ boştu.**

### Asıl sebep: bir zamanlama hatası

`ICurrentUser.CorrelationId` değeri **response header'ından** okuyordu:

```csharp
return context.Response.Headers.TryGetValue(
    CorrelationIdMiddleware.HeaderName, out var value) ? value.ToString() : null;
```

Ama `CorrelationIdMiddleware` o header'ı `OnStarting` içinde yazıyor —
yani **yanıtın ilk baytı yazılmadan hemen önce**, handler çalıştıktan
*sonra*:

```
Middleware   -> OnStarting KAYDEDILDI (henüz çalışmadı)
Handler      -> _currentUser.CorrelationId  =>  null      ← burada
SaveChanges  -> Outbox.CorrelationId = null
OnStarting   -> header nihayet yazılıyor (çok geç)
```

Yani **istek işlenirken bu değer her zaman null'du.** Parametreyi
geçen tek doğru çağrı yeri (`TicketTypeCommands`) bile null yazıyordu.
Kaç çağrı yeri düzeltilse fark etmezdi — kaynağın kendisi boştu.

### Düzeltme

Değeri middleware'in ilk satırında `HttpContext.Items`'a koydum;
`ICurrentUser` önce oradan okuyor, response header'ı fallback olarak
kaldı.

### Doğrulama — uçtan uca zincir

`X-Correlation-Id: s16-duzeltme-…` başlığıyla bir rezervasyon yaptım:

```
                        CorrelationId | ProcessedAt | RetryCount
ReservationCreated | s16-duzeltme-1787927778 | t | 0
```

Sonra zincirin son halkasını ayrıca kanıtladım — bilinen bir ID ile
işleyicisi olmayan bir outbox mesajı ekleyip arka plan işinin onu
devralmasını izledim:

```
seviye          : Error
kaynak          : ProcessOutboxMessagesCommandHandler
mesaj           : '{Type}' turu icin kayitli Outbox isleyicisi yok.
CorrelationId   : ZINCIR-KANITI-16          ← HTTP'den geldi
OutboxMessageId : dc5d74a7
```

Yani zincir artık tam:

```
HTTP isteği      CorrelationId = abc
  → Outbox kaydı CorrelationId = abc
     → Arka plan işi CorrelationId = abc
```

Üç adım, farklı zamanlarda ve farklı process'lerde — tek sorguyla
bağlanabiliyor.

> **Dürüst not:** başarılı outbox işlemenin log satırı `Debug`
> seviyesinde, yani varsayılan ayarda görünmüyor. Bu bilinçli:
> başarıda günde binlerce satır üretirdi. Zincire gerçekten ihtiyaç
> duyduğun an olan **başarısızlık** ise `Warning`/`Error` seviyesinde
> ve aynı kapsamda — yukarıdaki kanıt tam olarak o yoldan geldi.

### Bu neden fark edilmemişti?

Çünkü **belirtisi yoktu.** Kod derleniyordu, testler geçiyordu, sistem
çalışıyordu. Boş bir sütun hiçbir şeyi kırmıyor. Yalnızca üretimde bir
sorunu araştırırken "bu e-postayı hangi istek tetikledi?" diye
sorduğunda cevapsız kalıyorsun — yani tam ihtiyacın olduğu anda.

Sprint 12'deki denetim alanları hatası, Sprint 15'teki bağlanmamış
maskeleyici ve bu — üçü aynı desen: **tanımlanmış ama beslenmeyen bir
alan.**

---

## 1. Serilog ve 12 olay

### Serilog neyi değiştirdi, neyi değiştirmedi?

**Değiştirmediği:** kodumuzdaki tek bir log satırı bile. Her yerde
`ILogger` ve `[LoggerMessage]` kullanmaya devam ediyoruz.

**Değiştirdiği:** o logların nereye ve hangi biçimde yazıldığı.

| | Düz metin | Yapılandırılmış (JSON) |
|---|---|---|
| Biçim | `"Rezervasyon olusturuldu. Koltuk: 4"` | `{"@mt":"...", "SeatCount":4, "CorrelationId":"9f2c"}` |
| Arama | grep + regex | **sorgu**: `SeatCount > 3` |

Mesaj şablonundaki `{ReservationId}` gibi yer tutucular otomatik olarak
alan adına dönüşüyor. Yani bu biçimi kazanmak için ekstra hiçbir şey
yazmadık — zaten doğru şekilde logluyorduk.

Üretilen gerçek satır:

```json
{"@t":"2026-08-28T14:37:38Z","@mt":"{RequestMethod} {RequestPath} -> {StatusCode}...",
 "CorrelationId":"01a048ce0ea078e7...","Application":"Ticketing.Api","Environment":"Development"}
```

### PDF'in 12 olayı

| # | Olay | Durum | EventId |
|---|---|---|---|
| 1 | Login | ✅ eklendi | 1001 |
| 2 | Başarısız login | ✅ eklendi | 1002 |
| 3 | Etkinlik oluşturma | ✅ eklendi | 1101 |
| 4 | Etkinlik yayınlama | ✅ eklendi | 1102 |
| 5 | Rezervasyon oluşturma | ✅ eklendi | 1201 |
| 6 | Koltuk kilitleme | ✅ eklendi | 1202 |
| 7 | Ödeme | ✅ eklendi | 1301/1302 |
| 8 | İade | ✅ eklendi | 1304 |
| 9 | Background job | ✔ zaten vardı | 9101-9106 |
| 10 | Cache hatası | ✔ zaten vardı | 9301-9307 |
| 11 | SignalR bağlantı hatası | ✔ zaten vardı | 9201 |
| 12 | Beklenmeyen exception | ✔ zaten vardı | 4000/5000 |

Sekizi bu sprintte eklendi. `LogEvents.cs` ile merkezi bir numara
haritası oluşturdum — çakışmayı ve "hangi numara boş?" sorusunu
ortadan kaldırıyor.

### Seviye seçimleri — hepsi gerekçeli

**Başarısız login → Warning, Information değil.** Üretimde Information
çoğu zaman filtreleniyor. Information yapsaydık *"son 5 dakikada 100
başarısız giriş"* alarmı hiç tetiklenmezdi — kural doğru olurdu ama
onu besleyen veri hiç gelmezdi.

**İade → Warning.** Hata olduğu için değil, **görülmesi** gerektiği
için. Sistemdeki tek para çıkışı; hacminde ani artış ya bir yazılım
hatası ya da kötüye kullanım işaretidir.

**Koltuk çakışması → Warning, Error değil.** Sistem tam olarak doğru
çalıştı ve veri bütünlüğünü korudu. Error yapsaydık pano sürekli alarm
çalar, gerçek hatalar bu gürültüde kaybolurdu.

### Loglamada gizlilik

Sprint 15'te kurduğumuz ilkeleri burada uyguladım:

- Başarısız girişte **e-posta maskeli** (`ade***@ornek.com`) — açık
  yazsaydık, saldırganın denediği adresler log dosyasında toplu bir
  liste oluştururdu. Yani saldırgan başarısız olsa bile bizim
  loglarımız onun işine yarardı.
- Başarılı girişte **kullanıcı Guid'i**, e-posta değil.
- Token, şifre, kart bilgisi hiçbir yerde loglanmıyor.

Bir ayrım bilinçli: başarısız giriş logunda sebebi ayrı bir alan
olarak veriyorum (`kullanici_yok` / `sifre_yanlis`). **Bu ayrım
yalnızca logda var** — kullanıcıya dönen yanıt ikisinde de aynı, aksi
halde hesap sayımı (user enumeration) yapılabilirdi.

### Log ne zaman atılmalı? — SaveChanges'ten *sonra*

Her iş olayında logu `SaveChangesAsync`'ten sonraya koydum. Önce
loglasaydık ve kaydetme başarısız olsaydı, logda "yayınlandı" yazardı
ama veritabanında yayınlanmamış olurdu.

**Logların gerçekle çelişmesi, hiç log olmamasından daha kötüdür:**
sorun araştıran kişi yanlış yöne gider.

---

## 2. OpenTelemetry

### Log ve trace aynı şey değil

Log *"ne oldu"*, trace *"nerede ne kadar sürdü"* sorusunu cevaplıyor:

```
POST /reservations                        820 ms
  ├─ CreateReservationCommand             815 ms
  │   ├─ SELECT EventSeats (FOR UPDATE)   640 ms   ← suçlu
  │   ├─ INSERT Reservations               18 ms
  │   └─ Redis DEL event:123                2 ms
```

Loglar bu isteğin 820 ms sürdüğünü söyler ama **neden** uzun sürdüğünü
söylemez.

### Paket kararı: Npgsql.OpenTelemetry vs EF instrumentation

İki seçenek vardı:

| | Sürüm | Ölçtüğü |
|---|---|---|
| `OpenTelemetry.Instrumentation.EntityFrameworkCore` | yalnızca `1.18.0-beta.1` | EF katmanı |
| `Npgsql.OpenTelemetry` | **kararlı** `9.0.5` | sürücü katmanı |

İkincisini seçtim: beta pakete gerek kalmadığı gibi, sürücü seviyesinde
ölçtüğü için daha doğru — EF'in ürettiği SQL'in veritabanında
*gerçekte* ne kadar sürdüğünü görüyoruz.

**Sürüm tuzağı:** `Npgsql.OpenTelemetry 10.0.3` de vardı ama Npgsql 10'u
hedefliyor; bizimki 9.x. Karıştırmak sessizce yanlış sürüm yükleyen bir
yapı üretirdi.

### Redis için beta kabul ettim — gerekçesiyle

`OpenTelemetry.Instrumentation.StackExchangeRedis` yalnızca
`1.18.0-beta.1` olarak yayınlanıyor. Bu, önceki sprintlerdeki paket
kararlarımdan farklı bir uç:

| Sprint | Paket | Karar |
|---|---|---|
| 9 | Newtonsoft.Json 11 (açık) | yama var → **zorla 13.0.3** |
| 14 | (MailKit alternatifi) | yama yok → **paketi reddet** |
| 16 | Redis instrumentation | **beta'yı kabul et** |

Gerekçe: paket OpenTelemetry'nin kendi kuruluşundan geliyor; "beta"
etiketi güvenilmezlikten değil, anlamsal sözleşmelerin henüz
sabitlenmemiş olmasından (alan *adları* değişebilir, kod çalışmaz
değil); ve etkisi izleme ile sınırlı — bozulursa Redis çalışmaz hale
gelmez, yalnızca trace kaybederiz.

### Katman sorunu: ActivitySource nereye ait?

`ActivitySource`'u önce WebApi altına koymuştum. Arka plan işleri
(Infrastructure) ona ihtiyaç duyunca sorun çıktı: **Infrastructure,
WebApi'ye referans veremez** — mimari testimiz zaten reddediyor.

Her iki katmanın da gördüğü tek yer Application. Oraya taşıdım. Ortak
ihtiyaç, ortak bağımlılık olan katmana çıkıyor.

Ayrıca Application katmanı `System.Diagnostics.ActivitySource`
kullanıyor — OpenTelemetry paketine bağımlı değil. Yarın sağlayıcı
değişirse buradaki kodun tek satırı değişmez.

### Doğrulama — gerçekten üretiliyor mu?

Konsol exporter'ı açıp trace'leri kaynak adına göre saydım:

| PDF maddesi | Kaynak | Üretilen iz |
|---|---|---|
| HTTP request trace | `Microsoft.AspNetCore` | ✅ 7 |
| Database sorguları | `Npgsql` | ✅ 691 |
| Redis işlemleri | `…StackExchangeRedis` | ✅ 3 |
| Background job işlemleri | `Ticketing` | ✅ 12 |
| Harici servis çağrıları | `System.Net.Http` | ⚠️ 0 |

> **Dürüst not — beşincisi ölçülemedi.** Instrumentation kayıtlı ve
> çalışır durumda, ama bu oturumda hiç giden HTTP çağrısı olmadı:
> ödeme sağlayıcımız simülasyon ve in-process çalışıyor, MailKit ise
> HTTP değil **SMTP** protokolü kullanıyor — yani `HttpClient`
> instrumentation'ı onu zaten yakalamaz.
>
> Gerçek bir dış entegrasyon eklendiğinde `HttpClient` üzerinden
> geçeceği için çalışacaktır, ama bunu **doğrulamadım**. "Kayıtlı"
> ile "çalıştığını gördüm" arasındaki farkı belirtmem gerekiyor.

Sağlık kontrollerini izlemeden **hariç tuttum**: saniyede bir
çağrılıyorlar ve trace deposunun %90'ı bu gürültü olurdu.

---

## 3. Sağlık kontrolleri — üç uç, üç farklı soru

| Uç | Soru | Kontrol edilen | Başarısızsa Kubernetes |
|---|---|---|---|
| `/health/live` | Process ayakta mı? | **hiçbir şey** | kapsayıcıyı **öldürür** |
| `/health/ready` | Trafik alabilir miyim? | DB, Redis, Hangfire, disk | yük dengeleyiciden **çıkarır** |
| `/health` | Özet (insan için) | hepsi | — |

### Bu ayrım neden hayati?

Diyelim `/health/live`'a da veritabanı kontrolü koyduk. PostgreSQL 30
saniye yanıt vermez oldu:

```
live probe düşer → Kubernetes TÜM kapsayıcıları öldürür
→ yeniden başlarlar → veritabanı hâlâ yok → yine ölürler → ...
```

**Geçici bir veritabanı sorunu, kalıcı bir uygulama çöküşüne dönüşür.**
Uygulama, kendi yeniden başlatmasıyla çözemeyeceği bir şey için sürekli
yeniden başlatılır.

Bu yüzden `Predicate = _ => false` satırı o dosyadaki en kritik satır.

### Doğrulama — Postgres'i gerçekten durdurdum

```
docker stop ticketing-postgres

/health/live  -> HTTP 200   ✅ process sağlıklı, öldürülme
/health/ready -> HTTP 503   ✅ trafik kesildi
```

### Redis → Degraded, Unhealthy değil

Sprint 11'de önbelleği bilinçli olarak opsiyonel yaptık (Null Object
Pattern): Redis yoksa sistem **yavaş** çalışır, **bozuk** çalışmaz.

`Unhealthy` deseydik, Redis düştüğünde `/health/ready` başarısız olur
ve site **tamamen erişilemez** hale gelirdi. Çalışabilecek bir sistemi,
çalışmayan bir önbellek yüzünden kapatmış olurduk.

Doğruladım:

```
docker stop ticketing-redis

/health/ready -> HTTP 200   (genel: Degraded)
  postgresql Healthy | redis Degraded | hangfire Healthy | storage Healthy
```

Aynı gerekçe Hangfire için de geçerli: arka plan işleri durduğunda
kullanıcı hâlâ bilet alabilir, yalnızca e-posta gecikir.

### Storage kontrolü: gerçekten yazmayı deniyor

`Directory.Exists` yeterli **değil** — klasör var olabilir ama salt
okunur bağlanmış olabilir (Docker volume), izinler değişmiş olabilir
veya disk dolmuş olabilir. Bunların hiçbirini "klasör var mı" sorusu
yakalamaz.

Ayrıca disk dolarsa **Serilog da log yazamaz** — yani sorunu anlatacak
mekanizma da susar. 500 MB eşiğinde uyarıyorum.

---

## Bu sprintin dersi

Sprint 15'te üç kez "ayar doğruydu, davranış yanlıştı" demiştim ve
kural olarak *"bir korumayı ekledikten sonra tetikleyip yanıtı
okuyorum"* diye yazmıştım.

Bu sprint aynı dersin bir üst seviyesi: **bir alanın var olması,
dolduğu anlamına gelmiyor.**

Correlation ID'de her şey doğru görünüyordu — alan, indeks, parametre,
hatta yorum satırı. Kodu okuyarak bu hatayı bulmak mümkün değildi;
yalnızca `SELECT COUNT("CorrelationId")` yazarak bulundu.

Sprint 12 (denetim alanları), Sprint 15 (bağlanmamış maskeleyici) ve
Sprint 16 (correlation ID) — üçü de aynı desen. Artık kontrol listeme
ekliyorum: **bir izlenebilirlik alanı eklediğimde, üretilen veriyi
sorgulayıp gerçekten dolduğunu görüyorum.**

---

## Sonraki adımlar (bilinçli olarak ertelenenler)

- **OTLP toplayıcı:** yapılandırma hazır (`OpenTelemetry:OtlpEndpoint`),
  ama Jaeger/Tempo kurmadım. Şu an geliştirmede konsola yazıyor.
- **Örnekleme (sampling):** şu an %100 iz üretiliyor. Üretimde bu hem
  pahalı hem gereksiz; oran tabanlı örnekleme gerekecek.
- **Metrikler:** PDF yalnızca trace istiyor. İstek sayısı/gecikme
  histogramları için `WithMetrics` eklenebilir.
- **Harici çağrı izlemesi doğrulanmadı** (yukarıdaki dürüst not).
- **Log alarmları:** EventId blokları hazır (1002 = başarısız giriş,
  1304 = iade) ama alarm kuralları henüz kurulmadı — bir log sistemi
  gerektiriyor.
