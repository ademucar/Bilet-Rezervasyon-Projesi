# Sprint 17 — Testing

## Bu sprintte ne yaptım?

PDF beş test türü istiyor: birim, entegrasyon (Testcontainers ile),
mimari, frontend ve E2E. Sprint sonunda tablo şöyle:

| Katman | Önce | Sonra |
|---|---:|---:|
| Birim (backend) | 237 | **258** |
| Mimari | 14 | **16** |
| Entegrasyon | **0** | **23** |
| Frontend (Vitest) | **0** | **36** |
| E2E (Playwright) | **0** | **4** |
| **Toplam** | 251 | **337** |

Ama asıl sonuç sayı değil: **testler gerçek bir mali hatayı ve dört
yanlış varsayımımı buldu.**

---

## 🔴 Entegrasyon testinin bulduğu hata: iade idempotency'si hiç çalışmıyordu

Sprint 15'te iade için `Idempotency-Key` desteği yazmıştım. Kod doğru
görünüyordu:

```csharp
var zatenIslendi = payment.Transactions.Any(
    t => t.Type == Refund && t.ProviderReference == request.IdempotencyKey);
```

Entegrasyon testi yazdım: aynı anahtarla iki kısmi iade gönder,
veritabanında **bir** iade kaydı olmalı.

```
Expected iadeSayisi to be 1, but found 2.
```

### Sebep

`payment.Transactions` sorguda **hiç yüklenmiyordu.** Lazy loading
kapalı olduğu için koleksiyon her zaman boş geliyordu — kontrol her
seferinde "daha önce işlenmemiş" diyordu.

Yani idempotency'nin tek satırı bile çalışmıyordu.

### Neden bu kadar zor fark edildi?

**Tam** iadede domain koruması (toplam iade ödenen tutarı aşamaz)
ikinci isteği zaten reddediyordu. Hata yalnızca **kısmi** iadede
görünür oluyordu.

Ve şanslıydım: `GetRefundableAmount()` kalıcı bir sütun
(`RefundedAmount`) kullanıyor, `Transactions` koleksiyonunu değil.
Kullansaydı aşırı iade de mümkün olurdu.

Düzeltme tek satır: `.Include(p => p.Transactions)`.

### Desen tanıdık

| Sprint | Bulgu |
|---|---|
| 12 | Denetim alanları tanımlıydı, hiç doldurulmuyordu |
| 15 | `SensitiveDataMasker` yazılmıştı, hiçbir yerden çağrılmıyordu |
| 16 | Correlation ID alanı ve indeksi vardı, hep NULL'du |
| **17** | **İdempotency kontrolü vardı, baktığı koleksiyon hep boştu** |

Dördü de "yazılmış ama beslenmemiş kod". Bu kez farkı bir **entegrasyon
testi** yaptı — birim testi bulamazdı, çünkü sorunun kaynağı EF'in
yükleme davranışıydı.

---

## Testcontainers: neden gerçek veritabanı?

PDF açıkça istiyor ama gerekçesi kendi projemizde çok somut.

EF Core'un InMemory sağlayıcısı bir veritabanı **değil**, bir sözlük.
Şunların hiçbiri orada yok:

- **`xmin` tabanlı iyimser eşzamanlılık** ← projemizin kalbi
- Gerçek transaction ve izolasyon seviyeleri
- UNIQUE / FOREIGN KEY kısıtları
- **Sorgu çevirisi (LINQ → SQL)**

Sonuncusu özellikle sinsi: Sprint 13'te `GroupBy` + record constructor
kombinasyonunun EF tarafından çevrilemediğini ancak çalışma zamanında
500 alarak öğrenmiştim. InMemory sağlayıcı LINQ'u **bellekte**
çalıştırdığı için o hataların hiçbirini yakalamaz.

> *"Aynı koltuğu iki kullanıcı alamaz"* testini InMemory ile
> yazsaydık **yeşil olurdu ve hiçbir şey kanıtlamazdı.**

### Kurulum kararları

| Karar | Gerekçe |
|---|---|
| Konteynerler **tek kez** başlıyor (`ICollectionFixture`) | Her sınıf kendi konteynerini başlatsa paket dakikalarca sürerdi — çalıştırılmayan test, olmayan testtir |
| Şema `Migrate()` ile, `EnsureCreated()` ile **değil** | Aksi halde bozuk bir migration'ı testler asla yakalamaz |
| Respawn her testten önce tabloları boşaltıyor | Testlerin **sıralarına** göre geçip kalması, ayıklanması en zor test türü |
| İmaj sürümleri sabit (`postgres:17-alpine`) | `latest` olsaydı bugün geçen test, PostgreSQL 18 çıktığı gün hiçbir kod değişmeden kırılırdı |

### İlk çalıştırmada 8 testin 8'i de patladı

```
DataAnnotation validation failed for 'JwtOptions'
members: 'Secret' with the error: 'The Secret field is required.'
```

Bu bir test hatası değil, **Sprint 1'deki `ValidateOnStart`
korumasının çalıştığının kanıtı.** Ayarlar opsiyonel olsaydı uygulama
sessizce açılır ve JWT boş bir anahtarla imzalanırdı — yani herkesin
üretebileceği token'larla.

---

## Testlerin düzelttiği dört yanlış varsayımım

Bu sprintte dört kez test kırıldı ve **dördünde de kod doğruydu.**

### 1. Başarısız ödemede koltuk ne oluyor?

Beklentim: koltuk kilitli kalır, kullanıcı tekrar dener. Gerekçesini
de yazmıştım — kart hatası yaygın, kullanıcı başka kartla hemen dener.

Gerçek: rezervasyon **iptal ediliyor**, koltuklar **serbest
bırakılıyor**.

Kodu düzeltmedim çünkü PDF Sprint 8 açıkça diyor: *"Ödeme başarısız
olduğunda koltuklar serbest bırakılmalıdır."* Handler'ın içindeki
yorumda bu tartışma zaten yazılıydı: iş analizinde ben de tersini
önermişim, **şartname benim tercihimin önüne geçiyor.**

Yani testim kendi eski görüşümü doğruluyordu, şartnameyi değil.

### 2 ve 3. İki bilet metodu neden hata fırlatmıyor?

İptal edilmiş bir bileti tekrar iptal etmenin ve kullanılmış bir bileti
`Expired` işaretlemenin hata vermesini bekliyordum. İkisi de sessizce
geçiyor — yanlarında `// idempotent` yazıyordu.

**Ve bu doğru tasarım:** bu metotları arka plan işleri çağırıyor ve
Outbox'ta en az bir kez teslim garantisi var. Hata fırlatsalardı iş
başarısız sayılır, tekrar denenir, sonunda dead letter'a düşerdi —
oysa yapılması gereken iş **zaten yapılmıştı.**

> *"Zaten istenen durumdaysa sessizce geç"* ile *"geçersiz bir geçiş
> denendi, reddet"* farklı şeyler. Kullanılmış bir bileti iptal etmek
> gerçekten yanlış (o test hata bekliyor ve geçiyor); iptal edilmiş bir
> bileti tekrar iptal etmek yalnızca gereksiz.

### 4. Uydurma ödeme referansı

`complete` ucuna `"TEST-REF-1"` diye kendi uydurduğum bir referans
gönderdim, 422 aldım. Sebep Sprint 8'deki güvenlik kontrolü: referans
**sağlayıcıya doğrulatılıyor** ve `MockPaymentProvider` yalnızca kendi
ürettiklerini tanıyor.

O kontrol olmasaydı saldırgan doğrudan bu adrese istek atıp **bedava
bilet** alabilirdi.

---

## Domain kapsüllemesi test kodunu da bağlıyor

Entegrasyon testinde kullanıcıya rol atamak için şunu yazdım:

```csharp
db.UserRoles.Add(new UserRole(kullanici.Id, rol.Id));   // DERLENMEDİ
```

`UserRole`'un kurucusu `internal`. Sprint 2'de ara tabloyu bilerek
kapsüllemiştik ki kimse rol atamasını kuralları atlayarak yapmasın.

**Test kodu bile bu kurala uymak zorunda** — ve iyi ki öyle:
`AssignRole` ayrıca "aynı rol iki kez atanmasın" kontrolünü yapıyor.
Tabloya doğrudan yazsaydım, testim üretimde olmayan bir durumu
doğrulardı.

---

## Mimari testler: 5. kural eklendi

Dört kural zaten vardı; *"Domain Entity doğrudan DTO döndürmemelidir"*
eksikti.

İki açıdan kontrol ediyorum ve **ikisi de gerekli**:

| Kural | Yakaladığı | Kaçırdığı |
|---|---|---|
| Ad tabanlı (`Dto`/`Response`/`ViewModel` ile biten dönüş tipleri) | Domain içinde tanımlı `SeatMapDto` | `Model` diye adlandırılmış bir DTO |
| Yapısal (Domain → Application bağımlılığı yok) | Application'daki her DTO | Domain'in kendi içindeki DTO |

Ad kuralı generic argümanlara da bakıyor — yoksa `IReadOnlyList<EventDto>`
döndüren bir metotla kolayca atlatılırdı.

---

## Frontend testleri

Frontend'de **hiçbir test altyapısı yoktu.** Vitest + Testing Library
kurdum.

Vitest yapılandırmasını ayrı bir dosyaya koymadım: testler uygulamanın
**gerçek** derleme ayarlarıyla (Tailwind eklentisi, React eklentisi)
çalışsın diye. Ayrı dosya zamanla ayrışır ve *"testte çalışıyor,
uygulamada çalışmıyor"* durumu üretirdi.

### PDF'in 9 maddesi

| Madde | Nerede |
|---|---|
| Login formu | `LoginPage.test.tsx` |
| Etkinlik filtreleme | `EventFilterPanel.test.tsx` |
| Koltuk seçimi | `SeatMap.test.tsx` |
| Rezervasyon sayacı | `useCountdown.test.ts` |
| API hata ekranı | `LoginPage.test.tsx` |
| Yetkisiz route | `ProtectedRoute.test.tsx` |
| Ödeme sonucu | E2E (`bilet-alma-akisi.spec.ts`) |
| SignalR güncellemesi | `ConnectionIndicator.test.tsx` |
| Responsive görünüm | E2E, `mobil` projesi |

### SignalR: hub'ı değil göstergeyi test ediyorum — dürüst not

`useSeatHub` gerçek bir WebSocket bağlantısı kuruyor; jsdom'da
WebSocket yok. Mock'lasaydım `@microsoft/signalr`'ın iç durum
makinesini taklit etmem gerekirdi ve o taklit gerçek kütüphaneyle
uyuşmadığında test yeşil kalır ama **hiçbir şey kanıtlamazdı.**

Sprint 10'da bu dersi zaten almıştım: `window.WebSocket` sarmalayıcısı
SignalR'ın taşıma katmanı görüşmesini bozmuştu.

Burada test ettiğim şey **kullanıcının gördüğü kısım**: bağlantı durumu
değiştiğinde ekranda doğru bilgi çıkıyor mu. Gerçek bağlantı davranışı
hâlâ yalnızca elle ve E2E'de doğrulanıyor.

### Üç seçici sorunu, üç ders

**1. Salt okunur koltuk haritasında `getByLabelText` çalışmadı.**
Bileşen etkileşimli modda `aria-label` + `role="button"`, salt okunur
modda yalnızca SVG `<title>` veriyor. Salt okunur haritaya
`role="button"` vermek **yanlış** olurdu: ekran okuyucuya tıklanabilir
bir şey vaat edip hiçbir şey yapmamak.

**2. `toHaveBeenCalledWith` kırıldı.** TanStack Query, `mutationFn`'i
ikinci bir bağlam parametresiyle çağırıyor. O parametre bizim
sözleşmemizin değil, **kütüphanenin iç ayrıntısı** — ona bağlanan bir
test, kütüphane sürümü değiştiğinde kodda hiçbir hata olmadan
kırılırdı.

**3. `getByText('2')` eşleşmedi.** Metin JSX'te `{activeCount} aktif`
diye parçalanmış.

---

## E2E: Playwright

PDF'in yedi adımlık senaryosu tek testte:

```
kayıt → giriş → etkinlik bul → koltuk seç
      → rezervasyon → ödeme → bilet
```

Masaüstü (Chromium) ve mobil (Pixel 7) profillerinde çalışıyor. Mobil
profil yalnızca genişliği değiştirmiyor; dokunmatik olayları ve mobil
kullanıcı aracısını da taklit ediyor.

### Test verisine bağımlı değil

| Sabit yazsaydım | Bunun yerine |
|---|---|
| `test@ornek.com` | Zaman damgalı benzersiz e-posta — ikinci çalıştırmada "zaten kayıtlı" hatası almasın |
| `A-1'e tıkla` | `rect[role="button"][tabindex="0"]` — o koltuk satıldığında test kırılmasın |
| İlk etkinlik kartı | Oturumu **olan** etkinliği arayarak seçiyor |

Bu üçünü de sabit yazsaydım test **yalnızca bir kez** geçerdi.

### Bir seçici hatası ve düzeltmesi

İlk yazımda bağlantılara tıklayıp `page.goBack()` yapan bir döngü
vardı; "etkinlik bulunamadı" dedi. Her gezinmeden sonra DOM yeniden
oluşuyor ve elimdeki locator listesi **bayat** kalıyordu.

Adresleri bir kez toplayıp `goto()` ile gezmek hem daha güvenilir hem
daha hızlı.

### Doğrulandı

Test çalıştıktan sonra veritabanında gerçek bir bilet kaydı buldum:

```
e2e-1787930907107-5b0vj7@ornek.test | TKT-20260828-F82EF2D4 | Active
```

Testin 4 saniyede bitmesi beni şüphelendirmişti — sessizce atlıyor
olabilirdi. Veritabanına bakmak bunu kesinleştirdi. Sprint 16'daki
dersin uygulaması: **"geçti" ile "gerçekten yaptı" ayrı iki şey.**

---

## Bu sprintin dersi

Sprint 15 bana *"bir korumayı ekledikten sonra tetikleyip yanıtı oku"*
dedirtmişti. Sprint 16 bunu genişletti: *"bir alan eklediğimde üretilen
veriyi sorgula."*

Sprint 17 üçüncü katmanı ekliyor:

> **Bir test kırıldığında ilk soru "kod mu yanlış, test mi?" olmalı.**

Bu sprintte dört kez test kırıldı ve dördünde de cevap **testti** —
benim yanlış varsayımlarımdı. Beşinci kırılma ise gerçek bir hataydı ve
para kaybettirecek türdendi.

Testlerin asıl değeri "hata yakalamak" değil; **varsayımlarını yazılı
hale getirip yanlışlarını sana göstermek.**

---

## Sonraki adımlar (bilinçli olarak ertelenenler)

- **Handler seviyesinde birim testler:** PDF *"use case veya servisler"*
  dediği için domain testleri + entegrasyon testleri ile karşıladım.
  Handler'ları izole test etmek `IApplicationDbContext` mock'lamayı
  gerektiriyor ve EF ile bu kırılgan olur.
- **E2E için ayrı test veritabanı:** şu an geliştirme veritabanını
  kullanıyor. CI'da Testcontainers ile ayrı bir örnek gerekecek
  (Sprint 19).
- **Kapsam (coverage) raporu:** `coverlet.collector` kurulu ama eşik
  tanımlı değil.
- **SignalR gerçek bağlantı testi:** hâlâ elle doğrulanıyor.
