# Arama, Filtreleme, Pagination ve Redis Cache (Sprint 11)

Bu belge Sprint 11'de verdiğim kararları ve PDF'in isteklerinin nerede
karşılandığını kayıt altına alır.

---

## 1. PDF'in istediği 8 filtre

| PDF filtresi | Sorgu parametresi | Panelde var mı? |
|---|---|---|
| Şehir | `cityId` | ✅ |
| Kategori | `categoryId` | ✅ |
| Tarih | `dateFrom` / `dateTo` | ✅ |
| Fiyat aralığı | `minPrice` / `maxPrice` | ✅ |
| Mekân | `venueId` | API'de var, panelde yok |
| Organizatör | `organizerId` | API'de var, panelde yok |
| Yaş sınırı | `maxMinimumAge` | ✅ |
| Satış durumu | `status` | ✅ |

**Sekizi de API'de destekleniyor.** Panelde altısı var.

Mekân ve organizatörü bilerek koymadım: kullanıcı mekân adını genelde
bilmez ("Demo Sahne" mi "Zorlu PSM" mi?) ve etkinliği seçince zaten görüyor.
Her filtreyi ekrana koymak *"eksiksiz"* değil, **kullanılamaz** bir arayüz
üretirdi. İkisi de organizatör paneli ve admin ekranlarında kullanılıyor.

### Fiyat filtresinde verdiğim karar

Bir etkinliğin **birden fazla bilet türü** var (Tam, Öğrenci, VIP) ve her
birinin fiyatı farklı. Kullanıcı *"en fazla 300 TL"* dediğinde ne bekler?

**"300 TL'ye girebileceğim etkinlikler"** — yani en ucuz bileti 300'ün
altında olanlar. VIP bileti 1000 TL olsa bile.

Bu yüzden *"herhangi bir bilet türü aralığa giriyorsa"* şeklinde filtreliyorum.
*"Tüm bilet türleri aralığa girmeli"* deseydik kullanıcı pahalı bir VIP
seçeneği yüzünden uygun fiyatlı etkinliği hiç göremezdi.

### Yaş filtresinde isimlendirme

Alan adı `maxMinimumAge` — kulağa garip geliyor ama doğru olan bu: etkinliğin
`MinimumAge` alanı var ve biz onun **en fazla** kaç olabileceğini soruyoruz.

Arayüzde kullanıcıya *"yaş sınırı"* değil **"yaşım"** soruyorum. *"Yaş sınırı 18"*
seçeneği belirsiz olurdu: 18 sınırlı etkinlikleri mi, 18 yaşındakinin
girebileceklerini mi? *"18 yaşındayım"* hiçbir yoruma yer bırakmıyor.

---

## 2. Sıralama — beyaz liste neden şart?

PDF örneği: `GET /api/v1/events?sortBy=startDate&sortDirection=asc`

Bazı kütüphaneler `OrderBy("Title")` gibi **metin alarak** sıralama yapmayı
mümkün kılıyor. Cazip ama tehlikeli:

1. **Güvenlik:** Ham SQL üreten bir yapıda bu, sıralama üzerinden SQL
   enjeksiyonuna kapı açar.
2. **Veri sızıntısı:** İstemci `sortBy=PasswordHash` yazarsa sonuçlar o alana
   göre **sıralanır**. Alan yanıtta görünmese bile sıralamanın kendisi bilgi
   verir — birden fazla sorguyla değerler ikili aramayla çıkarılabilir.

`switch` ile beyaz liste kullanıyorum. Tanınmayan değer **sessizce varsayılana**
düşüyor: hata dönmek yerine mantıklı bir sonuç vermek, listeleme uçlarında
daha iyi bir davranış.

**Doğrulandı:**

```
sortBy=PasswordHash             -> Demo Konser | Alfa Tiyatro | Zeta Konser
sortBy=Id;DROP TABLE Events--   -> Demo Konser | Alfa Tiyatro | Zeta Konser
```

İkisi de varsayılana düştü, hata yok, tablo duruyor.

---

## 3. Cache key standardı — PDF kuralı 1

```
{alan}:{varlık}:{ayırt-edici}
```

| Anahtar | İçerik |
|---|---|
| `ref:cities` | Şehir listesi |
| `ref:categories` | Etkinlik kategorileri |
| `event:detail:{id}` | Etkinlik detayı |
| `event:popular:{adet}` | Popüler etkinlikler |
| `layout:{id}` | Salon oturma planı |

Tümü `ticketing:` öneki ile yazılıyor — Redis başka uygulamalarla
paylaşılabilir; önek olmasaydı `ref:cities` anahtarları çakışırdı.

### Standart neden şart?

1. **Çakışma:** Anahtarlar elle yazılsaydı birinin `event:123`, diğerinin
   `events:123` yazması kaçınılmazdı. İkisi **farklı** anahtar olur; biri
   temizlenir diğeri bayat kalır.
2. **Temizleme:** Önek olmadan *"bu etkinliğe ait tüm anahtarları sil"*
   demek imkânsızdır.

**Doğrulandı:**

```
ticketing:ref:cities
ticketing:event:detail:01a0436e-7065-757e-8d38-ada797b90295
ticketing:event:popular:10
```

---

## 4. Expiration — PDF kuralı 2

Tek soru: *"Bu veri değiştikten sonra kullanıcının eski halini görmesi ne
kadar süre kabul edilebilir?"*

| Veri | Süre | Neden |
|---|---|---|
| Şehirler, kategoriler | **24 saat** | Türkiye'de 81 il var ve bu sayı yıllardır değişmedi |
| Etkinlik detayı | **5 dakika** | Yanıt etkinliğin **durumunu** taşıyor. Satış kapanır veya etkinlik askıya alınırsa saatlerce geç öğrenilmesi kabul edilemez |
| Popüler etkinlikler | **10 dakika** | Bir **sıralama** — anında güncel olması gerekmiyor, hesabı pahalı |
| Oturma planı | **1 saat** | Salon yapısı neredeyse hiç değişmez |

**Süre yalnızca emniyet ağı.** Her veri için açık temizleme de var; bir
temizleme çağrısı unutulursa veya başarısız olursa, veri en geç bu süre
sonunda kendini yeniler. Yani *"sonsuza kadar bayat kalma"* ihtimali yok.

**Doğrulandı (kalan TTL):**

```
ticketing:ref:cities        -> 86398 sn (24 saat)
ticketing:event:popular:10  ->   598 sn (10 dk)
ticketing:event:detail:...  ->   298 sn (5 dk)
```

---

## 5. Invalidation — PDF kuralı 3

> *"Veri güncellendiğinde ilgili cache temizlenmelidir."*

Etkinlik değiştiğinde `event:` **öneki** ile siliyorum, tek anahtarla değil.

Sebep: bir etkinliğin durumu değiştiğinde birden fazla anahtar bayatlıyor —
`event:detail:{id}`, `event:popular:10`, `event:popular:20`... `popular:{n}`
anahtarlarının hangi `n` değerleriyle üretildiğini **önceden bilemeyiz**.

### Fazla silmek, eksik silmekten iyidir

Bu yaklaşım başka etkinliklerin detay anahtarlarını da siliyor. İsraf gibi
görünüyor ama doğru tercih:

| | Bedeli |
|---|---|
| Fazla silmek | Birkaç sorgu tekrar veritabanına gider (milisaniyeler) |
| Eksik silmek | Kullanıcı **iptal edilmiş** etkinliğe bilet almaya çalışır |

İkisi kıyaslanamaz. Önbellekte *"bayat veri"* her zaman *"gereksiz sorgu"*dan
pahalıdır.

**Doğrulandı:**

```
ÖNCE  -> event:detail:... | event:popular:10 | ref:cities
[etkinlik iptal edildi]
SONRA -> ref:cities
Log: "Onbellek temizlendi. Onek: event:, silinen anahtar: 2"
```

Referans verisi (şehirler) etkilenmedi — doğru kapsam.

### Başarısız işlemde temizlik yapılmıyor

Test sırasında bir `submit` isteği 422 döndü ve cache **temizlenmedi**.
Bu doğru davranış: işlem başarısız olduysa veri değişmemiştir, temizlemenin
anlamı yok.

---

## 6. Hassas veri — PDF kuralı 4 (en kritik olan)

> *"Kullanıcıya özel hassas veriler ortak cache içinde tutulmamalıdır."*

Etkinlik detay sorgusu **ilk bakışta** kullanıcıdan bağımsız görünüyor — aynı
etkinlik herkese aynı döner. Ama bir alan var: `IncludeUnpublished`.

**Admin için `true`, herkes için `false`. Yani aynı Id, role göre farklı sonuç.**

### İlk aklıma gelen çözüm ve neden vazgeçtim

Anahtara rolü eklemek:

```
event:detail:{id}:admin
event:detail:{id}:public
```

Çalışırdı. Ama **yayınlanmamış etkinliğin tüm içeriği Redis'e yazılmış olurdu.**
Redis'e erişen herhangi biri (yanlış yapılandırılmış bir port, başka bir
uygulama, bir yedek dosyası) organizatörün henüz yayınlamadığı etkinlikleri
okuyabilirdi.

### Seçtiğim çözüm: yayınlanmamış içerik hiç önbelleklenmez

Admin görünümü önbelleği **tamamen atlıyor**. Önbelleğe giren sorgu ise
yalnızca yayınlanmış etkinlikleri döndüren sürüm.

**Maliyeti:** admin istekleri önbellekten yararlanmıyor. Kabul edilebilir —
admin trafiği toplamın binde biri bile değil ve önbellek zaten ölçek için var.

**Yan fayda:** anahtar sayısı ikiye katlanmıyor.

**Doğrulandı:**

```
1) ADMIN taslak detayı istedi  -> HTTP 200, "GIZLI TASLAK ETKINLIK"
   Redis'teki anahtarlar       -> (BOŞ)

2) ANONİM aynı etkinliği istedi -> HTTP 404
   Redis'teki anahtarlar        -> (BOŞ)
```

Taslak içerik Redis'e **hiç yazılmadı**. `null` sonuçlar da önbelleklenmiyor —
yoksa admin bir etkinlik yayınladığında kullanıcılar 5 dakika boyunca 404
görmeye devam ederdi.

### IDOR koruması sorgunun içine taşındı

Önceden görünürlük kontrolü veriyi **çektikten sonra** yapılıyordu. Önbellek
eklerken sorgunun içine taşıdım ve daha da güvenli oldu: yetkisiz kullanıcı
için veritabanından **hiç veri gelmiyor**, dolayısıyla önbelleğe de yazılamıyor.

### Hiç önbelleklenmeyenler

Rezervasyon, bilet, ödeme ve bildirim sorguları **hiç** önbelleklenmiyor.
Sebep sadece gizlilik değil, **doğruluk** da: rezervasyon durumu saniyeler
içinde değişiyor. Bir saniye bile bayat veri, kullanıcının süresi dolmuş bir
rezervasyona ödeme yapmaya çalışması demek.

---

## 7. Cache kapalıyken sistem çalışmalı — PDF kuralı 5

Bu kural tasarımın en önemli kısıtı ve **üç yerde** uygulandı:

### a) Bağlantı dizesi yoksa

`NullCacheService` devreye giriyor — her sorgu doğrudan veritabanına.

Bunu `if (cache != null)` kontrolü yerine **boş bir sınıf** ile yaptım
(Null Object Pattern). Alternatifi her handler'da **iki kod yolu** olurdu ve
o iki yoldan yalnızca biri test edilirdi; diğeri üretimde ilk kez çalışırdı.

### b) Başlangıçta Redis'e ulaşılamıyorsa

`ConnectionMultiplexer.Connect()` istisna fırlatırsa yakalanıyor ve
`NullCacheService`'e düşülüyor. **Uygulama yine açılıyor.**

Bu, JWT doğrulamasındaki *"fail fast"* yaklaşımının **tersi** — ve bilinçli:
eksik JWT bir **güvenlik açığı**, eksik Redis yalnızca bir **performans kaybı**.

### c) Çalışma sırasında Redis çökerse

Okuma ve yazma ayrı ayrı `try/catch` ile sarılı. Hata loglanıyor ama yukarı
sızmıyor.

> **Önbellek bir hızlandırıcıdır, veri kaynağı değil.**
> Redis çöktüğünde site yavaşlamalı ama çökmemeli.

İstisnayı yukarı bıraksaydık, Redis'in bir dakikalık kesintisi **tüm siteyi**
500 hatasına boğardı — oysa veritabanı gayet sağlıklı çalışıyor olurdu.
Önbellek eklemek, sistemi daha **kırılgan** yapmış olurdu ki bu tam tersi bir
sonuç.

**Doğrulandı (Redis durduruldu):**

| Uç | Sonuç |
|---|---|
| `GET /cities` | HTTP 200 |
| `GET /events` | HTTP 200 |
| `GET /events/popular` | HTTP 200 |
| `GET /events/{id}` | HTTP 200 |

20 şehir doğru döndü, loglara 8 uyarı düştü (sessiz kalmadı).

**Redis geri açıldığında** önbellek uygulamayı yeniden başlatmadan
kendiliğinden devreye girdi — `AbortOnConnectFail = false` sayesinde.

---

## 8. `KEYS` değil `SCAN`

Redis'in `KEYS` komutu tüm anahtar alanını tek seferde tarar ve **sunucuyu
tamamen bloke eder**. Redis tek iş parçacıklı olduğu için o sırada gelen her
istek bekler.

Milyonlarca anahtarlı bir Redis'te `KEYS` saniyelerce sürebilir — yani tek bir
etkinlik güncellemesi tüm siteyi saniyelerce durdururdu.

`SCAN` imleçli çalışır: küçük parçalar halinde tarar ve aralarda diğer
isteklere sıra verir.

---

## 9. Karşılaştığım iki yapılandırma hatası

### a) Redis portu yanlıştı

`appsettings.Development.json` `localhost:6379` diyordu ama container
**6380**'de (Sprint 3'te başka bir projeyle çakışmasın diye değiştirmiştim).

### b) Redis parolası eksikti

`docker-compose.yml` `--requirepass` ile parola koyuyor; bağlantı dizesinde
parola yoktu.

### İkisinin de ortak dersi

Bu iki hata yüzünden **cache hiç çalışmıyordu** — ama PDF kuralı 5 sayesinde
site sorunsuz çalışmaya devam ediyordu. Yani *"her şey yolunda"* görünüyordu.

Fark etmemin tek sebebi Redis'e **doğrudan bakıp anahtarların boş olduğunu
görmem** oldu. Bu, Sprint 9'da Hangfire paneli ve Sprint 10'da bağlantı
göstergesi için savunduğum ilkenin üçüncü tekrarı:

> **En tehlikeli durum, çalışmadığı hâlde çalışıyor görünmektir.**

Bu yüzden Redis bağlantısı kurulamadığında artık **konsola uyarı düşüyor**.

*(`appsettings.Development.json` `.gitignore`'da — parola depoya girmiyor.)*

---

## 10. Paket sürümü kararı

`StackExchange.Redis` **3.1.31** en güncel sürüm ama
`Microsoft.Extensions.Logging.Abstractions >= 10.0.5` istiyor.

Sprint 9'da `Microsoft.Extensions.*` ailesini bilinçli olarak **9.0.11**'de
hizalamıştım (`net9.0` hedefliyoruz). 3.x'i almak o hizalamayı bozardı.

**2.8.58** seçtim — kararlı, yaygın ve 9.x uyumlu.

**8 projenin hiçbirinde güvenlik açığı olan paket yok.**

---

## 11. İki katmanlı önbellek

Şehir ve kategori listeleri **iki yerde** önbellekleniyor:

| Katman | Kazanç |
|---|---|
| Redis (sunucu) | Tüm kullanıcılar için veritabanı yükünü kaldırıyor |
| TanStack Query (istemci) | Bu kullanıcının sayfa geçişlerinde **ağ isteğini bile** ortadan kaldırıyor |

İkisi farklı problemi çözüyor, ikisi de gerekli.

---

## 12. Ölçüm

```
istek 1: 0.0092 sn  (veritabanı - soğuk)
istek 2: 0.0059 sn  (önbellek)
istek 3: 0.0028 sn  (önbellek)
istek 4: 0.0030 sn  (önbellek)
istek 5: 0.0028 sn  (önbellek)
```

Yaklaşık **3 kat** hızlanma — üstelik 3 etkinlikli bir veri setinde.
Gerçek veride popüler sorgusunun maliyeti bilet sayısıyla büyüyeceği için
fark çok daha belirgin olur.

---

## 13. Bilinen sınır: liste sorgusu önbelleklenmiyor

`GET /events` (filtreli liste) **bilerek** önbelleklenmiyor.

Sebep **anahtar patlaması**: şehir × kategori × tarih aralığı × fiyat ×
yaş × durum × sıralama × sayfa = binlerce kombinasyon. Her biri ayrı bir
anahtar olurdu ve çoğu **bir kez** kullanılıp süresi dolana kadar Redis
belleğinde beklerdi.

Önbellek isabet oranı çok düşük, bellek maliyeti çok yüksek olurdu.

Bunun yerine **popüler etkinlikler** ayrı bir uç olarak önbellekleniyor:
tek ve sabit bir anahtar, en sık çağrılan sorgu.

Liste sorgusu için doğru çözüm önbellek değil **indeks**. Sprint 17'de
sorgu planları incelenip gerekli bileşik indeksler eklenecek.
