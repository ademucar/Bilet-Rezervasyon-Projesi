# Bildirim ve E-posta Sistemi (Sprint 14)

---

## 1. PDF gereksinimleri

### 9 bildirim noktası

| PDF maddesi | Nerede | Durum |
|---|---|---|
| Rezervasyon oluşturulduğunda | `CreateReservationCommandHandler` | ✅ **bu sprintte eklendi** |
| Rezervasyon süresi dolmak üzereyken | `NotifyExpiringReservations` işi | ✅ **bu sprintte eklendi** |
| Ödeme başarılı olduğunda | `CompletePaymentCommandHandler` | ✅ vardı |
| Ödeme başarısız olduğunda | `FailPaymentCommandHandler` | ✅ vardı |
| Bilet oluşturulduğunda | `CompletePaymentCommandHandler` | ✅ **bu sprintte eklendi** |
| Etkinlik tarihi yaklaştığında | `EventReminderOutboxHandler` | ✅ vardı |
| Etkinlik iptal edildiğinde | `EventCancelledOutboxHandler` | ✅ vardı |
| İade tamamlandığında | `RefundPaymentCommandHandler` | ✅ vardı |
| Rapor hazırlandığında | `ReportExportOutboxHandler` | ✅ vardı |

### 4 uç — hepsi doğrulandı

| PDF ucu | Sonuç |
|---|---|
| `GET /notifications` | **200** |
| `GET /notifications/unread-count` | **200**, sayı 10 |
| `PATCH /notifications/{id}/read` | **204** |
| `PATCH /notifications/read-all` | **200**, sonrasında sayaç 0 |

### 8 e-posta şablonu

Hoş geldiniz, Şifre sıfırlama, Rezervasyon oluşturuldu, Ödeme başarılı,
Bilet bilgileri, Etkinlik hatırlatma, Etkinlik iptali, İade tamamlandı.

---

## 2. Sprint 3'te bıraktığım not — kontrol ettim, durum değişmiş

Sprint 3'te MailKit'i **güvenlik açığı yüzünden almamıştım**:

```
NU1902: 'MailKit' paketinde önem derecesi ORTA olan bilinen bir
        güvenlik açığı var (GHSA-9j88-vvj5-vhgr)
```

Denediğim tüm sürümlerde (4.9.0 – 4.14.0) aynı uyarı çıkmıştı. Bilinen açığı
olan bir paketi projeye almayı reddedip .NET'in yerleşik `SmtpClient`'ını
kullandım — eskimiş (`SYSLIB0014`) ama güvenli. Ve koda şu notu bıraktım:

> *"SPRINT 14 NOTU: ... O gün MailKit advisory'sinin kapanıp kapanmadığı
> TEKRAR kontrol edilmeli."*

**Sprint 14'te kontrol ettim: MailKit 4.17.0 ile tarama temiz döndü.**
8 projenin hiçbirinde güvenlik açığı olan paket yok.

Sprint 3'teki gerekçe artık geçerli değil, MailKit'e geçtim:

- `SYSLIB0014` bastırması **kalktı** — artık eskimiş API kullanmıyoruz
- Microsoft'un kendisi `SmtpClient` yerine MailKit öneriyor
- Modern TLS ve kimlik doğrulama desteği var

> Kodda bırakılan bir *"sonra bak"* notunun neden değerli olduğunun somut
> örneği: karar o günün koşullarına göre verilmişti, koşullar değişti,
> karar güncellendi.

---

## 3. Şablon sistemi: neden gerekliydi?

Sprint 9'a kadar e-posta gövdeleri handler'ların **içine gömülü** HTML
metinleriydi:

```csharp
body.Append($"<p>Merhaba {user.FirstName},</p>");
body.Append("<p>Odemeniz alindi...</p>");
```

Üç somut sorun:

1. **Görünüm tutarsızlığı** — her e-posta farklı görünüyordu. Kullanıcının
   gözünde bunlar aynı şirketten gelmiyor gibi duruyordu.
2. **Değişiklik maliyeti** — alt bilgiye bir satır eklemek **sekiz** dosyayı
   değiştirmek demekti, ve birini unutmak kaçınılmazdı.
3. **İş mantığı kirliliği** — ödeme handler'ının işi para işlemek, HTML
   yazmak değil.

Şablon sistemi üçünü de çözüyor: ortak bir çerçeve var, her şablon yalnızca
kendi içeriğini üretiyor, handler'lar da yalnızca **veri** gönderiyor.

### Satır içi CSS — web'de kötü, e-postada zorunlu

Gmail, Outlook ve çoğu istemci `<style>` bloğunu **siliyor** veya yok sayıyor.
`max-width: 600px` de e-posta istemcilerinde yaygın kabul gören genişlik.

### HTML + düz metin alternatifi

`multipart/alternative` ile ikisi birden gönderiliyor:

1. Bazı istemciler HTML'i kapatıyor — düz metin olmasaydı kullanıcı **boş**
   bir e-posta görürdü
2. Spam filtreleri yalnızca HTML içeren mesajları daha şüpheli buluyor

Düz metni HTML'den **türetiyorum** — ayrı şablon yazmaya gerek kalmıyor ve
ikisi birbirinden ayrışamıyor.

**Doğrulandı:** Mailpit'te hem `HTML` hem `Text` alanı dolu.

---

## 4. Güvenlik: e-posta üzerinden içerik enjeksiyonu

Şablon verilerinin çoğu **kullanıcıdan** geliyor: ad, soyad, etkinlik
başlığı, iptal sebebi.

Kullanıcı adını şöyle kaydederse ne olur?

```html
<a href="http://kotu-site">Hesabinizi dogrulayin</a>
```

Kaçış olmadan bu HTML e-postaya **olduğu gibi** girerdi. Çoğu istemci script
çalıştırmıyor ama **bağlantı çalışıyor** — yani saldırgan, **bizim alan
adımızdan** gönderilen bir e-postaya kendi kimlik avı bağlantısını
koyabilirdi. Alıcının gözünde tamamen güvenilir görünen bir mesaj.

Tek bir `H()` yardımcısı bütün şablon değerlerini kaçırıyor.

**Gerçekten test ettim** — kullanıcı adını yukarıdaki HTML ile değiştirip
e-posta ürettim:

```
Çalışan kötü bağlantı HTML olarak girdi mi? -> HAYIR
Kaçırılmış metin olarak mı görünüyor?       -> EVET
Gövde: Merhaba &lt;a href=&quot;http://kotu-site&quot;&gt;...
```

---

## 5. Bildirim mi, e-posta mı? — ikisi farklı yerlerde

Rezervasyon oluşturulduğunda **ikisi de** gidiyor ama farklı yollardan:

| | Nerede | Neden |
|---|---|---|
| Uygulama içi bildirim | Rezervasyonla **aynı transaction** | Kendi veritabanımıza yazılıyor — atomik olabilir ve olmalı |
| E-posta | **Outbox** üzerinden | Dış bir servise çıkıyor ve yavaş olabilir |

Sonuç: koltuklar anında ayrılıyor, e-posta birkaç saniye sonra geliyor.
Doğru öncelik.

> Outbox'ın varlık sebebi *"iki sistem arasında atomiklik sağlamak"*.
> Uygulama içi bildirimde tek sistem var — Outbox'a koymak gereksiz
> gecikme olurdu.

### "Bilet oluşturuldu" neden ayrı bir bildirim?

Ödeme başarılı bildirimi zaten var. Ama kullanıcı iki farklı soru soruyor:
*"param gitti mi?"* ve *"biletim hazır mı?"*

Tek bildirimde birleştirseydik, biletlerini görmek isteyen kullanıcı ödeme
bildirimini aramak zorunda kalırdı.

---

## 6. "Süresi dolmak üzere" uyarısı

### Neden 3 dakika?

Kilit süresi toplam 10 dakika.

- **5 dakika kala** çok erken: kullanıcı zaten ödeme ekranında ve sayacı
  görüyor olabilir
- **1 dakika kala** çok geç: ödemeyi tamamlamaya vakit kalmaz, uyarı yalnızca
  kaybı bildirmiş olur

3 dakika, kullanıcının başka sekmedeyse geri dönüp ödemeyi bitirebileceği
bir süre.

### Neden dakikada bir çalışıyor?

Uyarı penceresi 3 dakika. Beş dakikada bir çalışsaydı pencereyi **tamamen
kaçırabilirdi** — yani bildirim hiç gitmezdi ve **hata da vermezdi**.

### İdempotency şart

İş dakikada bir çalışıyor ve pencere 3 dakika — yani aynı rezervasyon
**üç kez** seçilir. Kontrol olmasaydı kullanıcı üst üste üç uyarı alırdı ve
uyarının amacı (dikkat çekmek) tam tersine dönerdi: kullanıcı bildirimleri
kapatırdı.

**Doğrulandı:** iş iki kez tetiklendi → uyarı sayısı **2'de kaldı**.

### Kalan süre yukarı yuvarlanıyor

2.1 dakika kalmışken *"2 dakika"* demek, kullanıcının sandığı kadar vakti
olmaması demek. `Ceiling` ile yuvarlıyorum — ama asla olduğundan **fazla**
göstermiyor.

---

## 7. `read-all` neden `ExecuteUpdateAsync` kullanmıyor?

EF Core 7+ ile tek SQL cümlesinde toplu güncelleme yapılabilirdi. Kullanmadım:

1. **Entity metodunu atlar.** Bugün basit ama ileride bir kural eklenirse
   (örneğin *"arşivlenmiş bildirim okundu işaretlenemez"*) toplu güncelleme
   onu görmez ve iki farklı davranış oluşur.
2. **Denetim interceptor'ını atlar** — `UpdatedAt`/`UpdatedBy` dolmaz.
   Sprint 12'de tam bu tür bir boşluk yüzünden `CreatedAt`'in hiç dolmadığını
   bulmuştum.

Okunmamış bildirim sayısı kullanıcı başına onlarla ölçülür; tek tek
yüklemenin maliyeti kabul edilebilir. **Binlerce satıra çıksaydı karar
değişirdi.**

---

## 8. Zil: iki ayrı uç, iki ayrı sıklık

| Uç | Ne zaman | Neden |
|---|---|---|
| `unread-count` | Her 60 saniye | Yalnızca bir `COUNT` — satırlar hiç okunmuyor |
| `notifications` | **Sadece panel açıkken** | Kapalıyken 15 bildirimin tüm metnini taşımanın anlamı yok |

Sayıyı liste ucundan da alabilirdik (`totalCount`). Ama zil rozeti **her
sayfada** ve düzenli aralıklarla yenileniyor — her yenilemede 15 bildirimin
başlığını, mesajını ve adresini boşuna taşırdık.

### Neden SignalR değil?

Sprint 10'da koltuk haritası için SignalR kurmuştuk. Bildirimler için de
kurulabilirdi ama kurmadım:

- Koltuk durumu **saniyeler** içinde değişiyor ve gecikme doğrudan 409'a
  yol açıyordu
- Bildirimde bir dakikalık gecikmenin somut bir zararı yok

---

## 9. Erişilebilirlik

- Zil düğmesi: `aria-label="Bildirimler, 4 okunmamis"` — rozet sayısı ekran
  okuyucuya da ulaşıyor
- Okunmamış işareti: kalın yazı **tek başına** yeterli değil (ekran okuyucu
  kalınlığı okumaz) → `<span class="sr-only">(okunmadı)</span>`
- Tür göstergesi: **renk + ikon**. Renk körü kullanıcı *"kırmızı = kötü
  haber"* ayrımını yapamaz
- `Escape` ile panel kapanıyor

**Doğrulandı:** tarayıcıda zil `"Bildirimler, 4 okunmamis"` etiketiyle
göründü, panelde `(okunmadi)` metni yer aldı.

---

## 10. Kod analizinden gelen üç düzeltme

| Kural | Ne dedi | Ne yaptım |
|---|---|---|
| `CA1716` | Arayüzde `template` parametresi — C++'ta ayrılmış kelime | `emailTemplate` olarak adlandırdım |
| `CA1305` | `StringBuilder.Append` interpolasyonu kültüre bağlı | `InvariantCulture` verdim |
| `CA1822` | `RefundCompleted` örnek verisine erişmiyor | `static` yaptım |
| `react(only-export-components)` | Bileşen dosyası sabit de dışa aktarıyor → HMR bozuluyor | Tipleri `types.ts`'e taşıdım |

Dördü de **bastırılmadı, uyuldu**. Bastırmak yerine uymak daha ucuzdu.

---

## 11. PDF'ten bilinçli bir sapma

*"Rezervasyon süresi doldu"* e-postası için PDF'in listesinde **şablon yok**
(dokuz bildirim var ama sekiz şablon).

*"Rezervasyon oluşturuldu"* şablonunu kullanmak yanlış olurdu — metni tam
tersini söylüyor. Dokuzuncu bir şablon eklemek yerine, o e-posta için kısa ve
doğrudan bir mesaj ürettim ve kararı koda yazdım. PDF'in listesine sadık
kaldım.

---

## 12. Uçtan uca doğrulama

Gerçek PostgreSQL + Hangfire + Mailpit + tarayıcı ile:

| Test | Sonuç |
|---|---|
| 4 bildirim ucu | 200 / 200 / 204 / 200 |
| `read-all` sonrası sayaç | **0** |
| Yeni rezervasyon | Bildirim **1**, e-posta Mailpit'e düştü |
| E-posta içeriği | Konu + HTML + **düz metin alternatifi** |
| HTML enjeksiyonu | **Engellendi** (kaçırılmış metin olarak göründü) |
| Süre uyarısı | 2 rezervasyon uyarıldı |
| Uyarı idempotency | İkinci tetikleme → sayı **artmadı** |
| Zil (tarayıcı) | Rozet **4**, panel açıldı, ikonlar ve tarihler doğru |

**214 test yeşil, 0 uyarı, 0 hata. 8 projede güvenlik açığı olan paket yok.**
