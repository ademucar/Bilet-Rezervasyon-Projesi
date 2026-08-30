# SignalR ile Gerçek Zamanlı Koltuk Güncelleme (Sprint 10)

Bu belge Sprint 10'da verdiğim kararları ve PDF'in isteklerinin nerede
karşılandığını kayıt altına alır.

---

## 1. Sprint 7'de bıraktığım notun karşılığı

Sprint 7'de koltuk haritasını 10 saniyede bir yokluyordum ve koda şunu
yazmıştım:

> *"Bu bir GEÇİCİ çözüm. PDF Sprint 10'da SignalR gelecek ve sunucu
> değişiklikleri ANINDA itecek. O zaman bu satır kaldırılacak."*

Sprint 10 geldi. Ama satırı **tamamen kaldırmadım** — fikrimi değiştirdim.

### Neden fikrimi değiştirdim?

SignalR bağlantısı **her zaman kurulamıyor**:

- Kurumsal ağlar WebSocket'i engelleyebiliyor
- Vekil sunucular uzun bağlantıları kesebiliyor
- Kullanıcının interneti gidebiliyor

Yoklamayı tamamen silseydim, bu durumlarda koltuk haritası **tamamen
donardı** — Sprint 7'deki halinden bile kötü olurdu.

### Çözüm: yoklama artık asıl yol değil, yedek

```ts
refetchInterval: hubStatus === 'connected' ? false : 10_000
```

| Durum | Davranış |
|---|---|
| Canlı bağlantı **var** | Yoklama **kapalı** — olaylar anında geliyor |
| Canlı bağlantı **yok** | Yoklama **açık** — Sprint 7 davranışı |

En iyi durumda gerçek zamanlı, en kötü durumda eskisi kadar iyi.
Buna **zarif bozulma** (graceful degradation) deniyor.

---

## 2. Bu bildirimler neden Outbox'a yazılmıyor?

Sprint 9'da e-posta ve bildirimleri Outbox'a yazmıştık. Burada **aynısını
yapmıyoruz** ve bu bilinçli bir ayrım.

Fark, *"kaybolursa ne olur?"* sorusunun cevabında:

| | Kaybolursa | Sonuç |
|---|---|---|
| **E-posta** | Kullanıcı biletini aldığından haberi olmaz | Telafisi yok → **kalıcı olmalı** (Outbox) |
| **Koltuk bildirimi** | Ekrandaki harita birkaç saniye eski kalır | Yedeği var → **hız önemli** (SignalR) |

Koltuk bildiriminin iki yedeği var:

1. İstemci yeniden bağlandığında listeyi baştan çekiyor
2. Rezervasyon denemesi sunucuda doğrulanıyor (409)

Yani en kötü ihtimalle kullanıcı bir 409 görür.

Üstelik Outbox'a yazmak **gerçek zamanlılığı bozardı**: mesaj en fazla
30 saniye sonra işlenirdi. *"Gerçek zamanlı"* diye 30 saniye gecikmeli bir
sistem kurmak, amacı tamamen kaçırmak olurdu.

> **Özet: Outbox dayanıklılık için, SignalR hız için.**
> İkisi farklı problemleri çözüyor.

Etkinlik iptalinde **ikisini birden** kullanıyoruz:

- **SignalR** → şu an o oturumun haritasına bakanlar (boşuna koltuk seçmesinler)
- **Outbox** → bileti olan herkes (ekranda olsun olmasın, kalıcı bildirim + e-posta)

---

## 3. Mimari: Application neden SignalR'ı tanımıyor?

`ISeatNotifier` arayüzü Application katmanında, uygulaması WebApi'de.

Application'a doğrudan `IHubContext` enjekte etseydik:

- Application, `Microsoft.AspNetCore.SignalR` paketine bağlanırdı
- Mimari testimiz (`Application_AltyapiKatmanlariniReferansAlmamali`)
  kırmızı yanardı — ve haklı olarak
- Birim testlerinde bir SignalR sunucusu ayağa kaldırmak gerekirdi

Bu arayüz sayesinde Application yalnızca *"koltuk kilitlendi, ilgili herkese
haber ver"* diyor. Nasıl haber verildiği WebApi'nin işi.

---

## 4. Bildirim sırası: commit'ten SONRA

Bütün `_seatNotifier` çağrıları `SaveChangesAsync`'ten **sonra**.

Bu sıra zorunlu. Önce bildirseydik ve kayıt `DbUpdateConcurrencyException`
ile başarısız olsaydı:

- Oturumu izleyen herkes koltuğu **kilitli** görürdü
- Oysa koltuk boşta
- Kimse alamazdı ve kimse nedenini anlayamazdı

*"Gördüğünü söyle"* ilkesi: yalnızca **gerçekleşmiş** bir şeyi duyuruyoruz.

---

## 5. Bildirim hatası iş akışını asla bozmamalı

`SafeSendAsync` bütün gönderimleri sarıyor ve hatayı yutup logluyor.

Bu dosyadaki en önemli karar. Eğer SignalR hatası yukarı sızsaydı:

1. Kullanıcı **500** hatası alırdı
2. **Ama rezervasyonu başarıyla oluşmuş olurdu**
3. Kullanıcı *"olmadı"* deyip tekrar denerdi
4. Koltuklar zaten kendisinde olduğu için **409** alırdı
5. Yani **kendi rezervasyonu yüzünden engellenirdi**

Teşhis edilmesi en zor hata türlerinden biri olurdu.

Kaybedilen şey ise küçük: bir kullanıcının ekranı birkaç saniye eski kalır.

`CA1031` (genel `catch`) bilinçli olarak susturuldu; gerekçe kodda yazılı.

---

## 6. Hub neden `[AllowAnonymous]`?

Bu kararı uzun düşündüm, çünkü ilk refleks *"her şeyi kilitle"* oluyor.

### Tutarlılık

`GET /event-sessions/{id}/seat-availability` zaten `[AllowAnonymous]`.
Gerekçesi Sprint 7'de yazılmıştı: kullanıcı bilet almadan önce hangi
koltukların boş olduğunu görebilmeli.

Hub'ın yaydığı olaylar o uç noktanın döndüğü bilginin **aynısını** taşıyor —
koltuk kimliği ve durumu, başka hiçbir şey. Sorguya açık olan bilgiyi canlı
yayında kapatmak tutarsız olurdu ve hiçbir şey korumazdı.

### Token'ı adrese koymak istemedim

SignalR WebSocket kullanınca tarayıcı `Authorization` **başlığı gönderemez**.
Standart çözüm token'ı sorgu dizesine koymaktır:

```
/hubs/seats?access_token=eyJhbGciOi...
```

Ama URL'ler her yere yazılır: sunucu erişim logları, ters vekil sunucu
logları, tarayıcı geçmişi, `Referer` başlığı. Yani token onlarca yerde düz
metin olarak birikir.

**Korunacak bir şey olsaydı bu bedeli öderdik. Burada yok.**

> Sprint 15'te bildirim hub'ı eklendiğinde (kullanıcıya **özel** veri
> taşıyacak) orada kimlik şart olacak ve token sorgu dizesi çözümünü,
> loglardan token'ı maskeleyen bir yapılandırmayla birlikte kuracağız.

### Kimin kilitlediği yayınlanmıyor

Olaylar yalnızca **hangi koltuk** bilgisini taşıyor, **kim** bilgisini değil.
Kullanıcı kimliğini yayınlasaydık, oturumu izleyen herkes *"şu kişi şu
koltuğu aldı"* bilgisini görürdü. Gizlilik ihlali olurdu ve ekranda hiçbir
işe yaramazdı.

---

## 7. Grup kuralı — PDF: *"Kullanıcı yalnızca görüntülediği oturumun grubuna katılmalıdır"*

Grup olmasaydı tek seçenek tüm istemcilere yayın yapmak olurdu (`Clients.All`).

**Somut sonucu:** 50 farklı etkinlik satışta ve 10.000 kişi bağlıyken, bir
koltuğun kilitlenmesi 10.000 kişiye mesaj gönderirdi. Bunların 9.800'ü o
etkinliğe bakmıyor bile.

Grup ile yalnızca o oturumu izleyen 200 kişiye gidiyor. **Fark 50 kat.**

Kullanıcı oturumlar arasında gezinirse önce **eski gruptan çıkıyoruz** —
yoksa artık bakmadığı oturumun mesajlarını almaya devam ederdi.

---

## 8. Olay gelince listeyi tekrar çekmiyoruz, önbelleği yamalıyoruz

Popüler bir konserde saniyede birkaç olay gelir. Her olayda tam listeyi
çekseydik (2000 koltuk, ~200 KB) sunucuyu yoklamadan bile beter yorardık —
SignalR'ın bütün kazancı giderdi.

`patchSeatStatus` yalnızca ilgili koltuğun durumunu değiştiriyor ve React
yalnızca o `<rect>`'i yeniden çiziyor.

**Dikkat edilen iki nokta:**

1. **Yeni nesneler üretiliyor** (yayma operatörü). Yerinde değiştirseydik
   React referansın aynı olduğunu görüp ekranı hiç güncellemezdi — sessizce
   çalışmayan bir arayüz.
2. **Boş koltuk sayacı da güncelleniyor.** Unutsaydık başlıkta *"65 / 68
   koltuk boş"* yazarken haritada 60 boş koltuk görünürdü.

Hiçbir şey değişmediyse **eski nesne aynen dönüyor** — yoksa React 2000
koltuğu boşuna yeniden hesaplardı.

---

## 9. Yeniden bağlanma — PDF frontend görevlerinin en kritiği

### Varsayılan strateji yetersizdi

`withAutomaticReconnect()` yalnızca **dört kez** dener (0, 2, 10, 30 sn) ve
sonra **pes eder**.

Kullanıcı koltuk seçim ekranında 10 dakika kalabilir. Wi-Fi'si iki dakika
kesilse bağlantı kalıcı olarak ölürdü ve kullanıcı bunu **fark etmeden**
eski bir haritaya bakmaya devam ederdi.

Kendi stratejim: artan aralıklarla (0, 2, 5, 10, 30 sn) ama **sonsuza kadar**.

### Yeniden bağlanınca listeyi baştan çekmek

```ts
connection.onreconnected(() => {
  void connection.invoke('JoinSession', eventSessionId)
  handlersRef.current.onReconnected()   // → invalidateQueries
})
```

Bu satır kancanın en kritik yeri. Bağlantı kopukken sunucu onlarca olay
göndermiş olabilir ve **hiçbiri bize ulaşmadı**. SignalR kaçırılan mesajları
biriktirmez.

Yamalama ile telafi edemeyiz — **neyi kaçırdığımızı bilmiyoruz.** Tek doğru
yol tam listeyi baştan çekmek.

Bu aynı zamanda SignalR'a neden *"kaybolursa olur"* diyebildiğimizin sebebi:
her zaman güvenilir bir toparlanma yolumuz var.

---

## 10. Bağlantı durumu göstergesi — küçük bileşen, büyük değer

Gösterge olmasaydı bağlantı koptuğunda ekranda **hiçbir şey değişmezdi**.
Kullanıcı donmuş bir haritaya bakıp *"kimse bilet almıyor, acele etmeme
gerek yok"* diye düşünürdü. Sonra bir koltuk seçip 409 alırdı — hiçbir şey
anlamadan.

### Gösterge kendi değerini test sırasında kanıtladı

SignalR'ı yazdım, backend'i doğruladım, sayfayı açtım ve ekranda
**"Canlı bağlantı yok"** yazdı.

Sebep: `vite.config.ts` yalnızca `/api` yolunu backend'e yönlendiriyordu,
`/hubs` yolunu **eklemeyi unutmuştum**.

Gösterge olmasaydı harita yine çalışırdı (yoklama yedeği devrede) ve
SignalR'ın hiç bağlanmadığını **fark etmezdim**. Sprint 10'u *"bitti"* sanıp
devam ederdim.

Ayrıca `ws: true` şart: varsayılan proxy yalnızca HTTP'yi iletir. SignalR
önce HTTP ile el sıkışıp sonra WebSocket'e **yükseltiyor**.

> Aynı ilkeyi Sprint 9'da Hangfire izleme ekranı için de savunmuştum:
> **en tehlikeli durum, çalışmadığı halde çalışıyor görünmektir.**

---

## 11. Uçtan uca doğrulama

Gerçek PostgreSQL + çalışan API + tarayıcı ile.
**Not: bağlantı canlıyken yoklama kapalı**, yani aşağıdaki güncellemelerin
tek kaynağı SignalR.

| PDF kuralı | Test | Sonuç |
|---|---|---|
| *"Bir koltuk başka kullanıcı tarafından seçildiğinde ekran güncellenmelidir"* | Rakip kullanıcı D-1'i kilitledi | Amber'a döndü, `tabindex` kalktı, sayaç 65→64 ✅ |
| **SeatReleased** | Rakip rezervasyonu iptal etti | Griye döndü, `tabindex="0"`, sayaç 63→64 ✅ |
| *"Satılan koltuk yeniden seçilememelidir"* | Rakip ödemeyi tamamladı | Koyu gri, `tabindex` yok — **tıkladım, seçime eklenmedi** ✅ |
| *"Rezervasyon süresi dolduğunda koltuk serbest görünmelidir"* | Süre geçmişe çekildi, Hangfire job'ı temizledi | F-1 anında boşaldı ✅ |
| **Çakışma bildirimi** | Seçtiğim E-1'i rakip aldı | Uyarı anında çıktı, koltuk seçimden düştü ✅ |
| *"Bağlantı kesildiğinde yeniden bağlanmalıdır"* | API durduruldu | Gösterge **"Yeniden bağlanıyor"** ✅ |
| *"Güncel koltuk listesini yeniden çekme"* | **API kapalıyken** A-5 doğrudan veritabanında bloke edildi | API dönünce **"Canlı"** + A-5 bloke göründü ✅ |

Son satır en güçlü kanıt: **A-5 değişikliğini SignalR taşıyamazdı** (sunucu
kapalıydı). Ekranın bunu bilmesinin tek yolu yeniden bağlanma sonrası
yapılan tam liste çekimi.

### Grup izolasyonu

Bunu tarayıcıdan ölçmeyi denedim: `window.WebSocket`'i sararak gelen
mesajları saymak istedim ama sarmalayıcı SignalR'ın taşıma katmanı
seçimini bozdu ve bağlantı hiç kurulamadı. İki denemeden sonra bu yolu
bıraktım.

Bunun yerine **sunucu tarafında** doğruladım — garantinin asıl yaşadığı yer:

```
Clients.All / AllExcept kullanımı : YOK
Toplam gönderim yolu              : 3
Hepsi                             : .Group(...) / .Groups(...)
```

Üç gönderim yolunun üçü de `SeatHub.GroupNameFor(sessionId)`'den türeyen
gruplara gidiyor. Yayın (broadcast) yapan tek bir kod yolu yok.

**Dürüst olmak gerekirse:** pozitif durum (olayın doğru gruba ulaşması)
işlevsel olarak doğrulandı; negatif durum (yanlış gruba **ulaşmaması**)
kod incelemesiyle doğrulandı, tarayıcıda ölçülmedi.

---

## 12. Bilinen sınır: tek sunucu varsayımı

Şu an tek API sunucusu var ve SignalR grupları **o sunucunun belleğinde**
tutuluyor.

Birden fazla sunucuya ölçeklenirse sorun çıkar: kullanıcı A sunucu-1'e,
kullanıcı B sunucu-2'ye bağlıysa, sunucu-1'in gönderdiği mesaj B'ye ulaşmaz.

Çözüm **Redis backplane**:

```csharp
builder.Services.AddSignalR().AddStackExchangeRedis(connectionString);
```

Redis zaten `docker-compose.yml`'de ayakta (Sprint 11'de önbellek için
kullanılacak). **Şimdi eklemedim** çünkü tek sunucuda hiçbir şey
kazandırmaz, sadece bir bağımlılık daha ekler. Sprint 17'de yatay
ölçekleme gündeme geldiğinde tek satırla açılacak.
