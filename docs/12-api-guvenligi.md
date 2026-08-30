# Sprint 15 — API Güvenliği

## Bu sprintte ne yaptım?

PDF'in istediği 15 güvenlik önlemi, 6 hız sınırlı uç ve 4 idempotent
işlem. Ama asıl öğrendiğim şey listenin kendisi değildi: **bir korumayı
eklemek ile o korumanın doğru davrandığını doğrulamak ayrı işler.**

Bu sprintte üç ayrı yerde "ayar doğruydu, davranış yanlıştı" durumuyla
karşılaştım. Üçünü de yalnızca çalıştırıp yanıtı okuyarak buldum.

---

## 1. Hız sınırlama (rate limiting)

### Üç politika

| Politika | Sınır | Uçlar | Neden bu sınır? |
|---|---|---|---|
| `auth` | 5 dk / 10 istek | login, register, şifre sıfırlama | Brute force hedefi |
| `transaction` | 1 dk / 20 istek | rezervasyon, ödeme, dosya yükleme | Bot ile koltuk kapatma |
| `search` | 1 dk / 60 istek | etkinlik listeleme, dosya indirme | Kazıma (scraping) |
| *(genel)* | 1 dk / 300 istek | politikası olmayan **her** uç | Varsayılan olarak güvenli |

### Neden hesap kilidi yetmiyordu?

Sprint 3'te zaten hesap bazlı kilitleme vardı: 5 yanlış denemede hesap
kilitleniyor. Bu **tek bir hesabı** koruyor.

Saldırgan 10.000 farklı e-posta ile aynı şifreyi (`sifre123`) denerse
hiçbir hesap kilitlenmez — her hesaba yalnızca bir deneme yapılıyor.
Buna *credential stuffing* deniyor.

IP bazlı hız sınırı bu saldırıyı durduruyor. İkisi birlikte çalışıyor:
hesap kilidi tek hesabı, hız sınırı tüm saldırıyı.

### İstemci anahtarı: kullanıcı > IP

Yalnızca IP kullansaydık, aynı şirket ağından (tek NAT IP) bağlanan
yüzlerce çalışan **tek** kotayı paylaşırdı — biri diğerlerini
engellerdi.

Bu yüzden giriş yapmış kullanıcılarda kota kullanıcı bazlı:

```csharp
var userId = context.User?.FindFirst("sub")?.Value;
if (!string.IsNullOrEmpty(userId)) return $"user:{userId}";
return $"ip:{context.Connection.RemoteIpAddress}";
```

Bu, `UseRateLimiter()`'ın `UseAuthentication()`'dan **sonra** gelmesini
zorunlu kılıyor. Önce olsaydı `context.User` boş olurdu ve herkes IP
bazlı sayılırdı.

### Kuyruk yok (`QueueLimit = 0`)

Kuyruk açsaydık, sınıra takılan istek beklerdi ve sunucu kaynağını
tutardı. Saldırgan binlerce isteği kuyrukta bekletip gerçek
kullanıcılara kaynak bırakmayabilirdi. Hemen reddetmek daha güvenli.

### Doğrulama

```
=== RATE LIMIT: login (5 dakikada 10 istek) ===
401 401 401 401 401 401 401 401 401 401 429 429 429

HTTP/1.1 429 Too Many Requests
Retry-After: 300
{
  "title": "Cok fazla istek",
  "status": 429,
  "errorCode": "rate_limit.exceeded"
}
```

`Retry-After` başlığı şart: olmasaydı istemci körlemesine tekrar dener
ve durumu kötüleştirirdi.

---

## 2. Güvenlik başlıkları

`SecurityHeadersMiddleware`, **tüm** yanıtlara ekliyor — controller,
hata sayfası, Swagger, Hangfire paneli. Başlıkları controller'larda
eklemek, birini unutmak demektir ve unutulan uç tam olarak korumasız
olandır.

| Başlık | Ne engelliyor? |
|---|---|
| `X-Content-Type-Options: nosniff` | MIME sniffing saldırısı |
| `X-Frame-Options: DENY` | Clickjacking |
| `Referrer-Policy` | Adresimizdeki GUID'lerin dışarı sızması |
| `Permissions-Policy` | Kullanmadığımız tarayıcı özellikleri |
| `Content-Security-Policy` | XSS (son savunma hattı) |
| `Strict-Transport-Security` | Protokol düşürme (yalnızca üretimde) |

### `OnStarting` neden gerekli?

Başlıkları `_next(context)` sonrasında eklemeye çalışsaydık, yanıt
gövdesi çoktan yazılmaya başlamış olabilirdi ve
*"headers are read-only"* istisnası alırdık. `OnStarting`, ilk bayt
yazılmadan hemen önce çalışıyor — başlıkları değiştirmek için son
güvenli an.

### HSTS neden yalnızca üretimde?

Geliştirmede localhost HTTP kullanıyor. HSTS gönderirsek tarayıcı
localhost'u **kalıcı olarak** HTTPS'e zorlar. Bunu geri almak tarayıcı
ayarlarından elle silmeyi gerektiriyor.

### 🔴 Hata 1: `Server: Kestrel` başlığı silinmiyordu

Middleware içinde `headers.Remove("Server")` yazdım. Test ettim —
başlık hâlâ oradaydı.

Sebep: **Kestrel, `Server` başlığını bizim geri çağrımızdan sonra
ekliyor.** Sildiğimiz şey henüz var olmayan bir başlıktı.

Doğru yer Program.cs:

```csharp
options.AddServerHeader = false;
```

Ders: bir başlığı "kaldırmak", onu ekleyen kodun **öncesinde** değil
**sonrasında** çalışmayı gerektiriyor. Ya da hiç eklenmemesini
sağlamak — ki bu daha temiz.

---

## 3. CORS

```csharp
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
```

Kaynak tanımlanmamışsa **hiçbir şey** açılmıyor. `AllowAnyOrigin()`
yazmak cazip ama tehlikeli: herhangi bir site tarayıcıdan API'mize
istek atabilirdi.

İlke: *eksik ayar, açık kapı anlamına gelmemeli.*

CORS `UseAuthentication`'dan **önce** olmalı — tarayıcının gönderdiği
ön kontrol (preflight `OPTIONS`) isteği kimlik bilgisi taşımaz. Sonra
olsaydı preflight 401 alır ve gerçek istek hiç gönderilmezdi.

### Doğrulama

```
izinli kaynak (localhost:5173): Access-Control-Allow-Origin: http://localhost:5173
izinsiz kaynak (kotu-site.com): IZIN VERILMEDI (dogru)
```

---

## 4. İstek boyutu sınırı

Varsayılan Kestrel sınırı ~30 MB; bizim en büyük isteğimiz birkaç
kilobayt. Genel sınırı 1 MB yaptım, dosya yükleme ucunda 5 MB'a
yükselttim.

İlke: **sınırlar ihtiyacı olan yerde genişletilir, her yerde birden
değil.** Genel sınırı 5 MB yapıp herkese açmak gereksiz bir saldırı
yüzeyi olurdu.

### 🔴 Hata 2: Sınır çalışıyordu ama yanıt 500 dönüyordu

2 MB'lık bir istek gönderdim. Sınır devreye girdi — ama yanıt
**500 Internal Server Error**'du.

Bu iki açıdan zararlı:

1. İstemciye "sunucu bozuk" diyor. Oysa sunucu tam olarak doğru
   çalıştı. İstemci "sonra tekrar denerim" diye düşünüp aynı büyük
   isteği tekrar gönderiyor ve sonsuza kadar başarısız oluyor.
2. 500'ler izleme panosunda alarm üretiyor. Saldırgan büyük istekler
   göndererek sahte alarm yağmuru oluşturabilirdi.

Sebep: Kestrel `BadHttpRequestException` fırlatıyor ve içinde doğru
durum kodunu (413) taşıyor — ama `GlobalExceptionHandler`'da bu tür
için bir dal yoktu, `_ => 500` dalına düşüyordu.

Düzeltme: istisnanın **kendi** durum kodunu kullanan bir dal ekledim.
Kendim tahmin etmiyorum, çünkü aynı istisna bozuk gövde için 400 ile
de geliyor.

### 🔴 Hata 3: `[RequestSizeLimit]` doğru sınırlıyor, yanlış konuşuyor

Dosya yükleme ucuna `[RequestSizeLimit(5 MB)]` koydum. 6 MB gönderdim:

```json
{
  "status": 400,
  "errors": { "": ["Failed to read the request form.
    Request body too large. The max request body size is 5242880 bytes."] }
}
```

İki sorun:

1. **400, 413 değil.** Kestrel doğru istisnayı fırlatıyor ama MVC bunu
   *model bağlama sırasında* yakalayıp sıradan bir doğrulama hatasına
   çeviriyor — bizim `GlobalExceptionHandler`'a hiç ulaşmıyor.
2. **Yapılandırdığımız sınır aynen sızıyor** (`5242880 bytes`) ve hata
   biçimi uygulamanın geri kalanıyla tutarsız.

Düzeltme: `RequestSizeGuardAttribute` — bir **resource filter**.

Neden action filter değil? Action filter model bağlamadan **sonra**
çalışıyor; gövde çoktan okunmuş, hata çoktan oluşmuş oluyor. Resource
filter, model bağlamadan önceki ilk noktadır. `Content-Length` başlığı
o an zaten elimizde.

Yan fayda: 6 MB'lık bir isteği tel üzerinden okumak zorunda
kalmıyoruz. Reddedeceğimiz veriyi almak için bant genişliği harcamak,
tam olarak saldırganın istediği şeydir.

İki attribute birlikte duruyor ve **ikisi de gerekli**:

- `[RequestSizeLimit]` → gerçek sınırlayıcı, chunked isteklerde bile
  çalışıyor
- `[RequestSizeGuard]` → doğru yanıtı veriyor

Biri korumayı, diğeri iletişimi üstleniyor.

### Doğrulama

```json
{
  "title": "Istek cok buyuk",
  "status": 413,
  "detail": "Dosya boyutu en fazla 5 MB olabilir.",
  "errorCode": "request.too_large"
}
```

---

## 5. Dosya yükleme güvenliği

Dosya yükleme, bir web uygulamasındaki **en tehlikeli** özelliktir:
kullanıcının sunucumuza veri değil **dosya** yazmasına izin veriyoruz.

### Üç kontrol var ve üçü de gerekli

| Kontrol | Kim sağlıyor? | Yalan söyleyebilir mi? |
|---|---|---|
| Uzantı (`file type`) | kullanıcı | **evet, bir saniyede** |
| MIME type | istemci | **evet, `curl -H` ile** |
| İmza (magic number) | dosyanın içeriği | hayır |

İlk ikisini de kullanıcı gönderiyor. Yalnızca imza kontrolü
güvenilir — çünkü onu değiştirmek dosyayı **bozmak** demektir.

Üçü birden aynı türü göstermeli. Meşru kullanıcıda bu üç bilgi zaten
uyuşur; uyuşmuyorsa ya bozuk ya kötü niyetli.

### Beyaz liste, kara liste değil

```csharp
[".jpg"] = ["image/jpeg"],
[".png"] = ["image/png"],
[".webp"] = ["image/webp"],
[".pdf"] = ["application/pdf"],
```

"Şunlar yasak" yazmak cazip ama yanlış: unuttuğun her uzantı bir
açıktır. `.exe` engellersin, `.bat` unutursun.

Beyaz listede unutmanın bedeli yalnızca *"bu dosya türü
desteklenmiyor"* hatasıdır — güvenlik açığı değil.

**SVG bilinçli olarak yok:** SVG bir XML belgesidir ve içine `<script>`
gömülebilir. Tarayıcıda açıldığında o script **bizim** alan adımızda
çalışır (saklanmış XSS). "Resim" gibi görünmesi aldatıcıdır.

### WebP özel durumu

WebP `RIFF` ile başlıyor — ama WAV ve AVI de. Yalnızca ilk 4 bayta
bakan bir kontrol, `.webp` adıyla yüklenen bir WAV dosyasını kabul
ederdi. 8. bayttan itibaren `WEBP` yazısını da doğruluyorum.

### Güvenli dosya adı: temizlemiyoruz, atıyoruz

Yaygın yaklaşım adı *temizlemektir* (tehlikeli karakterleri silmek).
Bu bir kedi-fare oyunu; her zaman kaçırılan bir durum vardır:

- `afis.jpg.exe` — çift uzantı
- `CON`, `PRN`, `NUL` — Windows ayrılmış adları
- üst üste URL kodlaması
- görsel olarak aynı görünen Unicode karakterler

`Guid` üretmek bu **sınıfın tamamını** ortadan kaldırıyor:
kullanıcıdan gelen metin dosya yolunda hiç kullanılmıyor. Yani
"acaba her durumu yakaladım mı?" sorusunu sormaya gerek kalmıyor.

Orijinal ad yine de veritabanında saklanıyor (indirirken kullanıcıya
göstermek için) ama **diske hiç yazılmıyor**.

### İndirme: her zaman `attachment`

`Content-Disposition: inline` olsaydı tarayıcı dosyayı bizim alan
adımızda açardı. Doğrulamayı geçmiş ama içinde script barındıran bir
dosya o zaman bizim alan adımızda çalışır ve kullanıcıların oturum
çerezlerine erişebilirdi.

> **Not:** Gerçek bir üretim sisteminde yüklenen dosyalar **ayrı bir
> alan adından** sunulur. Bunu şimdi yapmıyorum çünkü tek alan adıyla
> çalışıyoruz — ama ölçeklenirken ilk yapılacak şey bu.

### Uçtan uca doğrulama

| Senaryo | Beklenen | Sonuç |
|---|---|---|
| Kimlik doğrulamasız yükleme | 401 | ✅ 401 |
| Geçerli JPEG | 201 | ✅ 201 |
| `.exe` içeriği, `.jpg` adı, `image/jpeg` başlığı | red | ✅ `file.content_mismatch` |
| SVG | red | ✅ `file.type_not_allowed` |
| `../../appsettings.json.jpg` | etkisiz | ✅ diske Guid yazıldı |
| 6 MB dosya | 413 | ✅ 413 |
| İndirme | içerik aynı | ✅ + `attachment` |

Diskteki gerçek durum:

```
uploads/
  89a6fe4c4b734022880b59b1369b8953.jpg
  fa93d63399b341a38698a7164ad9bb63.jpg
```

Veritabanı:

| FileName | StoredFileName | sahibi_var |
|---|---|---|
| `afis.jpg` | `fa93d633...jpg` | t |
| `appsettings.json.jpg` | `89a6fe4c...jpg` | t |

Dizin geçişi denemesi **reddedilerek değil, etkisizleştirilerek**
çözüldü: kullanıcının gönderdiği yol parçası hiçbir yere yazılmadı.

### Anonim yüklemeye asla izin yok

Anonim dosya yükleme, sunucumuzu herkese açık bir depolama alanına
çevirir. Saldırgan diski doldurabilir veya **bizim alan adımızı
kullanarak** zararlı dosya dağıtabilir — ve iz sürecek bir kimlik
olmaz.

Kimlik zorunlu olunca her dosyanın bir sahibi oluyor
(`AuditFieldsInterceptor` `CreatedBy` alanını dolduruyor) ve kötüye
kullanım geriye dönük izlenebiliyor.

---

## 6. İdempotency

PDF'in listelediği 4 işlem:

| İşlem | Nasıl sağlanıyor? |
|---|---|
| Rezervasyon oluşturma | `Idempotency-Key` başlığı (Sprint 7) |
| Ödeme başlatma | `Idempotency-Key` başlığı (Sprint 8) |
| Ödeme callback | **ödemenin kendi durumu** |
| İade başlatma | `Idempotency-Key` başlığı (bu sprint) |

### Ödeme callback'ine neden anahtar eklemedim?

İdempotency zaten sağlanıyor, ama farklı bir yoldan:
`Payment.Complete()`, ödeme zaten `Successful` ise `false` dönüyor ve
handler bilet üretmiyor.

Anahtar bazlı idempotency burada **yanlış** olurdu: anahtarı
**sağlayıcı** üretecekti ve sağlayıcılar her denemede aynı anahtarı
göndereceğini garanti etmiyor. Anahtar değişirse "yeni istek" sanıp
ikinci kez bilet üretirdik.

**Ödemenin kendi durumu en güvenilir idempotency anahtarıdır.**

### İade neden anahtar gerektiriyor?

İade, çift çalıştırılması en tehlikeli işlem: aynı parayı iki kez geri
göndermek doğrudan mali kayıp.

Domain katmanı zaten koruyor — `Payment.Refund()`, toplam iadenin
ödenen tutarı aşmasını reddediyor. Ama bu, **ağ kopması yüzünden
tekrarlanan** bir isteği de hata yapıyor; oysa admin tek bir iade
yapmak istemişti ve isteğin ulaşıp ulaşmadığını bilmiyor.

Anahtar bu ikisini ayırıyor:

- aynı anahtar → "bu isteği zaten işledim", başarı döner
- farklı anahtar → gerçekten ikinci iade, kurallar işler

---

## 7. Hassas veri maskeleme

### Loglar "güvenli" değildir

- Yedeklenir ve yedekler başka yerde durur
- Merkezi log sistemlerine gönderilir, oraya geliştirici dışındaki
  kişiler de erişir
- Hata ayıklama sırasında ekran görüntüsü alınıp paylaşılır
- Destek talebine eklenir

Loga düşen bir JWT, süresi dolana kadar o kullanıcının hesabına giriş
yetkisidir.

### Nerelere bağladım?

| Yer | Ne sızabilirdi? |
|---|---|
| `GlobalExceptionHandler.LogClientError` | `exception.Message` içindeki gövde parçası |
| Geliştirme yanıtındaki `stackTrace` | iç içe istisnaların mesajları |
| `SmtpEmailService` alıcı logu | e-posta adresi (KVKK/GDPR) |

### E-posta neden tamamen gizlenmiyor?

`adem@ornek.com` → `ade***@ornek.com`

Tamamen maskeleseydik loglar destek için kullanılamaz hale gelirdi ve
geliştiriciler **maskelemeyi kapatmanın yolunu ararlardı** — ki bu
daha kötü bir sonuç. İlk üç harf + alan adı, destek için yeterli ipucu
veriyor ama adresleri toplu olarak toplamayı engelliyor.

### CA1873 ile karşılaşma

Analizör uyardı: log `Debug` seviyesinde ve üretimde genellikle
kapalı — maskeleme her e-postada boşuna çalışırdı.

Bastırmak yerine uydum: `IsEnabled(LogLevel.Debug)` kontrolü ekledim
ve maskeleme sonucunu bir yerel değişkene aldım.

### 🟡 Dürüst not: canlı pozitif kanıt üretemedim

Maskeleyicinin **çalıştığını** 13 birim testiyle doğruladım. Ama
çalışan sistemde "bakın, şu sır maskelendi" diyebileceğim bir senaryo
üretemedim.

Sebep şu ve aslında **iyi haber**: kendi doğrulama ve hata
mesajlarımız hiçbir kullanıcı değerini yansıtmıyor. Bozuk JSON
gönderdim, JWT'yi alan değeri olarak gönderdim — hiçbiri yanıta veya
loga düşmedi.

Yani maskeleyici bugün bizim kodumuz için değil, **kontrol etmediğimiz
kütüphanelerin** (Npgsql, System.Text.Json) mesajları için duruyor:
derinlemesine savunma. Bu, olmamasının gerekçesi değil — ama
"doğruladım" demekle "test ettim" demek arasındaki farkı belirtmem
gerekiyor.

---

## Bu sprintin asıl dersi

Üç hatanın üçü de aynı desendeydi:

| # | Ayar | Gerçek davranış |
|---|---|---|
| 1 | `headers.Remove("Server")` | başlık yerinde duruyor |
| 2 | `MaxRequestBodySize = 1 MB` | sınır çalışıyor, yanıt 500 |
| 3 | `[RequestSizeLimit(5 MB)]` | sınır çalışıyor, yanıt 400 + sızıntı |

Üçünde de derleme temizdi, testler yeşildi ve kod **doğru görünüyordu.**

Güvenlik kodunda "yazdım" ile "çalışıyor" arasındaki mesafe, normal
koddan daha uzun — çünkü bir güvenlik önlemi sessizce çalışmadığında
hiçbir şey kırılmıyor. Sadece koruma yok oluyor.

Sprint 11'deki Redis hatası da (yanlış port yüzünden önbellek hiç
çalışmadı) aynı desendeydi. Artık kural olarak benimsiyorum:
**bir korumayı ekledikten sonra onu tetikleyip yanıtı okuyorum.**

---

## Sonraki adımlar (bilinçli olarak ertelenenler)

- **Üretim `KnownProxies`:** `KnownNetworks.Clear()` "her vekile güven"
  demek. Bu yalnızca uygulama doğrudan internete açık değilse güvenli.
  Üretim dağıtımında vekil sunucunun gerçek adresi yazılmalı.
- **Sahipsiz dosya temizliği:** `UploadedFile.IsOrphan()` hazır, ama
  onu çağıran arka plan işi henüz yok.
- **Dosyalar için ayrı alan adı:** ölçeklenirken ilk yapılacak iş.
- **`TicketOwner` / `ReservationOwner` politikaları:** hâlâ yalnızca
  kimlik doğrulaması istiyor (Sprint 2'den kalan TODO).
