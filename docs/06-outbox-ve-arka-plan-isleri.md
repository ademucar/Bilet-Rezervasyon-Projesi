# Outbox Pattern ve Arka Plan İşleri (Sprint 9)

Bu belge Sprint 9'da verdiğim kararları ve gerekçelerini kayıt altına alır.
PDF'in bu sprintte istediklerinin nerede karşılandığını da gösterir.

---

## 1. Problem: İki sistem, tek transaction yok

Ödeme başarılı olduğunda iki şey yapmamız gerekiyor:

1. Veritabanına yaz (rezervasyon onayla, bilet üret)
2. E-posta gönder

Bunlar **iki farklı sistem** ve aralarında ortak bir transaction yok.
İki olası sıralamanın ikisi de bozuk:

| Sıra | Ne olur? |
|---|---|
| Önce DB, sonra e-posta | DB yazıldı, e-posta servisi çökmüş → kullanıcı biletini aldı ama haberi yok |
| Önce e-posta, sonra DB | E-posta gitti, DB transaction'ı geri alındı → **kullanıcıda bilet yok ama "biletiniz hazır" maili var** |

İkincisi daha kötü çünkü **geri alınamaz**. Gönderilmiş bir e-postayı geri çağıramazsınız.

### Çözüm

E-postayı göndermek yerine, **"e-posta gönderilecek" niyetini** aynı
veritabanına, aynı transaction içinde yazıyoruz:

```sql
BEGIN;
  UPDATE "Reservations" SET "Status" = 4;         -- Confirmed
  INSERT INTO "Tickets" ...;
  INSERT INTO "OutboxMessages" ('TicketsIssued', '{...}');
COMMIT;
```

Artık tek transaction var: ya hepsi olur, ya hiçbiri.
Arkada çalışan bir job `OutboxMessages` tablosunu okur ve e-postayı gönderir.

---

## 2. En az bir kez teslim (at-least-once)

Outbox **"tam olarak bir kez"** garantisi vermez, **"en az bir kez"** verir.

Somut senaryo: işleyici e-postayı gönderdi, tam o anda sunucu çöktü ve
`ProcessedAt` yazılamadı. Sistem ayağa kalkınca mesaj hâlâ işlenmemiş
görünür ve tekrar denenir.

**Exactly-once, dış servislerle teorik olarak imkânsızdır.** "Mesajı gönder"
ve "gönderdim diye işaretle" iki ayrı işlemdir; atomik olamazlar.

Bu yüzden çözümü tarafı değiştiriyoruz: **mesajı iki kez işlemek zararsız
olmalı.** Bütün işleyiciler idempotent:

```csharp
var exists = await OutboxPayload.NotificationExistsAsync(...);
if (exists) return;   // zaten yapılmış
```

**Doğrulandı:** `event-reminders` job'ı iki kez tetiklendi →
2 outbox mesajı oluştu ama **1 bildirim** yazıldı.

---

## 3. PDF'in iki maddesi çakıştı — verdiğim karar

PDF **Sprint 9**, Outbox senaryoları arasında **"QR bilet oluşturma işlemi"**ni
sayıyor.

Ama aynı PDF'in **Sprint 8** bölümü, ödeme başarılı olduğunda şu altı işin
**tek bir süreç içinde** çalışmasını istiyor ve listede **"Bilet oluşturma"** da var.

İkisini birden yapmak mümkün değil: bilet oluşturma tek transaction içindeyse,
QR üretimi de oradadır.

### Kararım

**QR, bilet ile birlikte transaction içinde üretiliyor. Outbox'a bırakılan şey
QR'in ÜRETİMİ değil, TESLİMİ** — yani QR'i içeren e-postanın gönderilmesi.

**Gerekçe:**

- QR'siz bilet **yarım bir kayıttır**. Kullanıcı ödemeyi yapıp "Biletlerim"e
  gittiğinde QR'i görmek zorunda; arka plan job'ının çalışmasını beklemesi
  kabul edilemez.
- QR üretimi bir **dış servise çıkmıyor** — birkaç mikrosaniyelik yerel bir
  hesap. Outbox'in varlık sebebi olan "dış sistem çağrısı" burada yok.

Yani sapma bilinçli: PDF'in **amacı** (kullanıcı isteği dış servis beklemesin)
korunuyor, e-posta gönderimi Outbox'a alınıyor.

> Bu, `01-is-analizi.md` soru 8'deki güncellemeyle aynı yaklaşım: şartname ile
> tasarım çakıştığında kararı gizlemiyorum, yazıyorum.

---

## 4. Outbox tablosu — PDF'in istediği alanlar

PDF: *"En az aşağıdaki alanları içermelidir."*

| PDF alanı | Sütun | Durum |
|---|---|---|
| Id | `Id` (uuid v7) | ✅ |
| Type | `Type` (text) | ✅ |
| Payload | `Payload` (jsonb) | ✅ |
| CreatedAt | `CreatedAt` | ✅ |
| ProcessedAt | `ProcessedAt` (null = işlenmedi) | ✅ |
| RetryCount | `RetryCount` | ✅ |
| ErrorMessage | `ErrorMessage` | ✅ |

**Eklediğim iki sütun:**

- `NextRetryAt` — üstel geri çekilme için. Olmasaydı başarısız mesaj her
  turda tekrar denenir, çökmüş servisi daha da yorardı.
- `IsDeadLettered` — kalıcı başarısızlık işareti. Sonsuza kadar denemek
  kuyruğu tıkar ve gerçek sorunu gizler.
- `CorrelationId` — PDF Sprint 16: *"Correlation ID Outbox kaydı içerisinde
  kullanılmalıdır."*

### `Payload` neden `jsonb`?

Test sırasında beklemediğim bir fayda ortaya çıktı: kasten bozuk bir payload
eklemeye çalıştığımda **PostgreSQL isteği reddetti**:

```
ERROR:  invalid input syntax for type json
```

Yani bozuk JSON tabloya hiç giremiyor. `text` olsaydı girer ve ancak
işleyicide patlardı.

---

## 5. Type neden `enum` değil de sabit metin?

Bu değer veritabanında **metin olarak** saklanıyor ve yıllarca orada duracak.

| Yaklaşım | Risk |
|---|---|
| Enum → sayı | Enum sıralaması değişirse **tablodaki eski kayıtların anlamı değişir**. "3 = EventCancelled" idi, araya değer eklendi, artık "3 = PaymentSucceeded" |
| Enum → metin | Bir üyeyi yeniden adlandırmak derleyici hatası **vermez**, ama eski kayıtlar hiçbir işleyiciyle eşleşmez — sessizce ölü mesaja dönerler |
| **Sabit metin** | Değeri değiştirmek bilinçli bir karar gerektirir ve migration yazılması gerektiği bellidir |

---

## 6. İş kuralları — PDF maddesi maddesine

### "Aynı Outbox kaydı iki kez işlenmemelidir."

Üç katmanlı:

1. `MarkAsProcessed` ikinci çağrıda ilk zamanı korur
2. Sorgu `ProcessedAt IS NULL` filtreliyor
3. **Asıl koruma:** işleyiciler idempotent

### "Başarısız işlem yeniden denenmelidir."

Üstel geri çekilme: **2 → 4 → 8 → 16 → 32 dakika**, üst sınır 60 dakika.

Neden üstel? E-posta servisi çökmüşse her 10 saniyede bir denemek onu daha
da yorar ve logları doldurur. Araları açmak hem servise nefes aldırır hem de
geçici sorunların kendiliğinden düzelmesine zaman tanır.

Neden 60 dakika üst sınır? 10 denemeden sonra 2¹⁰ = 1024 dakika (17 saat)
beklerdik. Servis düzelse bile bildirimler saatlerce gitmezdi.

### "Belirli deneme sayısından sonra hata kaydı oluşturulmalıdır."

**5 deneme** sonrası `IsDeadLettered = true`, `NextRetryAt = null`,
`ErrorMessage` korunur.

Neden 5? Üstel geri çekilme ile 2+4+8+16 = **30 dakikaya** karşılık geliyor.
Geçici bir kesinti (servis yeniden başlatma, ağ sorunu) bu süre içinde
neredeyse her zaman düzelir. Düzelmiyorsa sorun geçici değildir.

### "Job sonuçları loglanmalıdır."

`[LoggerMessage]` kaynak üreteci ile, yapılandırılmış olay kimlikleriyle
(9001–9005, 9101–9104).

**Sıfır mesaj işlendiğinde log yazmıyoruz.** Outbox job'ı 30 saniyede bir
çalışıyor: günde 2880 kez. Her seferinde "0 mesaj" yazsaydık loglar günde
2880 anlamsız satırla dolar ve gerçek hatalar arasında kaybolurdu.

### "Job işlemleri kullanıcı isteğini gereksiz yere bekletmemelidir."

En net örnek **etkinlik iptali**: 2000 kişilik bir konser iptal edildiğinde
2000 bildirim yazılacak. Bunu iptal isteğinin içinde yapsaydık admin
dakikalarca beklerdi — ve zaman aşımına uğrarsa iptal geri alınırdı.

Outbox'a **tek satır** yazmak ise anında.

---

## 7. Hangfire mi, Quartz.NET mi?

PDF ikisini de kabul ediyor. **Hangfire** seçtim:

| Sebep | Açıklama |
|---|---|
| **İzleme ekranı hazır** | `/hangfire` — hangi iş ne zaman çalıştı, ne kadar sürdü, hangi hatayla başarısız oldu. Arka plan işlerinde en büyük risk *"çalışmadığını fark etmemek"* olduğu için bu bir konfor değil, ihtiyaç |
| **İş durumu veritabanında** | Uygulama yeniden başlatıldığında yarım kalan işler kaybolmuyor |
| **Ek altyapı gerekmiyor** | Zaten PostgreSQL kullanıyoruz |

Quartz daha hafif ve daha esnek zamanlama sunuyor; bizim ihtiyacımız olan
zamanlama basit olduğu için bu avantajı kullanamazdık.

### Dashboard güvenliği — dikkat edilmesi gereken tuzak

Hangfire'in `/hangfire` ekranı **varsayılan olarak yalnızca localhost'tan**
erişilebilir. Ama **bir yetkilendirme filtresi tanımladığınız anda bu kısıt
kalkar.** Yani filtreyi yanlış yazmak, ekranı tüm internete açmak demektir.

Bu ekran salt okunur bir gösterge paneli değil, bir **yönetim arayüzü**:
işleri siler, yeniden çalıştırır, iş parametrelerini (rezervasyon ve kullanıcı
kimlikleri) gösterir.

`HangfireDashboardAuthorizationFilter` iki koşulu da açıkça arıyor:
kimlik doğrulanmış **ve** Admin rolünde.

**Doğrulandı:**

| Kim | Sonuç |
|---|---|
| Kimliksiz | `401` |
| Normal kullanıcı | `403` |
| Admin | `200` |

---

## 8. Zamanlama kararları

| İş | Sıklık | Neden bu sıklık? |
|---|---|---|
| Süresi dolan rezervasyonlar | Dakikada bir | **Doğrudan geliri etkiliyor.** Süresi dolmuş bir rezervasyonun koltuğu, iş çalışana kadar kimseye satılamaz. 10 dakikada bir çalışsaydı her koltuk ortalama 5 dakika boşuna beklerdi |
| Outbox işleme | 30 saniyede bir | Kullanıcı ödemeden sonra "biletiniz hazır" e-postasını bekliyor. Dakikalarca beklemek ödemenin geçip geçmediğinden şüphe ettirir |
| Etkinlik hatırlatması | Her gün 10:00 UTC | Hatırlatma bir **bildirimdir**; gece 03:00'te telefon titretmek kullanıcıyı kızdırır. TR saatiyle 13:00 — öğle arası |
| Günlük satış özeti | Her gün 00:30 UTC | Gece yarısından **yarım saat sonra**. 23:59:59'da tamamlanan bir ödemenin transaction'ı kapanması birkaç yüz milisaniye sürebilir; tam 00:00'da çalışırsak onu kaçırırdık |

### "Başarısız mesajları yeniden deneme" neden ayrı bir iş değil?

PDF bunu ayrı bir madde olarak sayıyor. Ayrı iş yazmayı düşündüm ve **vazgeçtim**.

Yeniden denenecek mesaj, bekleyen bir mesajdan yalnızca `RetryCount > 0`
olmasıyla ayrılıyor. Processor'ın sorgusu zaten şunu diyor:

```csharp
.Where(m => m.ProcessedAt == null
         && !m.IsDeadLettered
         && (m.NextRetryAt == null || m.NextRetryAt <= now))
```

Yani yeni mesajlar ile yeniden denenecekleri **aynı sorgu topluyor**.
Ayrı bir iş yazsaydık aynı tabloyu aynı koşulla tarayan iki iş olurdu ve
ikisi aynı anda aynı mesajı işlemeye çalışırdı.

PDF'in istediği **davranış** tam olarak karşılanıyor; ayrı bir zamanlayıcı
gerekmiyor.

---

## 9. Bilinen sınır: tek işlemci varsayımı

Şu an aynı anda **tek bir Outbox işlemcisi** çalışıyor —
`[DisableConcurrentExecution]` bunu garanti ediyor.

Birden fazla API sunucusuna ölçeklenirse her sunucunun kendi Hangfire
sunucusu olur ve `DisableConcurrentExecution` **dağıtık kilit** kullandığı
için (Hangfire'ın kendi `lock` tablosu) bu yine çalışır.

Ama daha yüksek verim istenirse doğru çözüm şudur:

```sql
SELECT ... FROM "OutboxMessages"
WHERE ...
ORDER BY "CreatedAt"
LIMIT 20
FOR UPDATE SKIP LOCKED;
```

`SKIP LOCKED` ile her işçi farklı satırları alır ve paralel çalışabilirler.

**Şimdi yapmadım** çünkü bu ham SQL PostgreSQL'e özgü ve Application
katmanını veritabanı sağlayıcısına bağlardı — Sprint 7'de `EF.Functions.ILike`
için verdiğim kararla aynı gerekçe. Tek işlemci mevcut ölçek için fazlasıyla
yeterli.

---

## 10. Uçtan uca doğrulama

Gerçek PostgreSQL + çalışan API + Mailpit ile:

| Test | Sonuç |
|---|---|
| Hangfire şeması oluştu | 12 tablo, `hangfire` şeması altında |
| 4 tekrarlanan iş kaydedildi | Cron ifadeleri doğru |
| Bekleyen `TicketsIssued` mesajı | İşlendi → **e-posta Mailpit'e düştü** |
| Süresi dolan rezervasyonlar | Job **kendiliğinden** 2 rezervasyonu temizledi |
| Koltuklar | 63 → **65 boş** (2 koltuk satışa döndü) |
| `ReservationExpired` outbox | 2 mesaj → 2 bildirim + 2 e-posta |
| Günlük satış özeti | Dünün raporu üretildi → admin bildirimi |
| Etkinlik hatırlatması | 3 bileti olan kullanıcıya **1** hatırlatma (Distinct çalışıyor) |
| **İdempotency** | 2 outbox mesajı → **1** bildirim |
| **Yeniden deneme** | İşleyicisi olmayan tür → `RetryCount=2`, `NextRetryAt` planlandı |
| **Dead letter** | 5. denemede `IsDeadLettered=true`, `NextRetryAt=null`, hata korundu |
| Dashboard erişimi | Kimliksiz `401`, kullanıcı `403`, admin `200` |

---

## 11. Güvenlik notu: Newtonsoft.Json

Hangfire eklerken `dotnet list package -vulnerable -include-transitive`
şunu yakaladı:

```
> Newtonsoft.Json  11.0.1  High  GHSA-5crp-9r3c-p9vr
```

Hangfire.Core geçişli olarak bu sürüme bağlı ve o sürümde **yüksek önem
dereceli** bir açık var (derin iç içe JSON ile StackOverflow → servis dışı
bırakma).

**Çözüm:** `CentralPackageTransitivePinningEnabled` açıldı ve
`Newtonsoft.Json` **13.0.3**'e sabitlendi. Artık Hangfire 11.0.1 istese bile
yamalı sürüm kullanılıyor.

> MailKit'te (Sprint 14 notu) durum farklıydı: orada **hiçbir sürümde yama
> yoktu**, bu yüzden paketi hiç almadım. Burada yama **var**; doğru çözüm
> paketi reddetmek değil, yamayı zorlamak.

Sonuç: **8 projenin hiçbirinde güvenlik açığı olan paket yok.**

### Yan etki

Geçişli sabitleme açılınca zaten var olan bir tutarsızlık ortaya çıktı:
`Microsoft.Extensions.Options` 10.0.11 iken
`Microsoft.Extensions.DependencyInjection.Abstractions` 9.0.11'di.

Önceden NuGet bunu sessizce yükseltiyordu. `net9.0` hedeflediğimiz için
Microsoft.Extensions ailesini **9.0.11'de hizaladım**.
