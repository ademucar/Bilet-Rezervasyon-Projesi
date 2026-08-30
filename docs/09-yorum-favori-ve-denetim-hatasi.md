# Yorum, Puanlama, Favori — ve Bulunan Bir Hata (Sprint 12)

---

## 1. PDF gereksinimleri

### Review uçları

| PDF | Durum |
|---|---|
| `POST /api/v1/events/{eventId}/reviews` | ✅ |
| `GET /api/v1/events/{eventId}/reviews` | ✅ |
| `PUT /api/v1/reviews/{id}` | ✅ |
| `DELETE /api/v1/reviews/{id}` | ✅ |

### İş kuralları — altısı da doğrulandı

| PDF kuralı | Test | Sonuç |
|---|---|---|
| Puan 1-5 arasında olmalıdır | `rating=0` / `rating=6` | **400** ✅ |
| Etkinlik tamamlanmadan yorum yapılamaz | Etkinlik `SalesOpen` iken | **422** `review.event_not_completed` ✅ |
| Yalnızca geçerli bilet almış kullanıcı yorum yapabilir | Biletsiz kullanıcı | **403** `review.no_valid_ticket` ✅ |
| Kullanıcı etkinlik başına bir yorum oluşturabilir | Aynı kullanıcı ikinci kez | **422** `review.already_exists` ✅ |
| Kullanıcı yalnızca kendi yorumunu düzenleyebilir | Başkası düzenlemeye çalıştı | **403** `review.not_owner` ✅ |
| Admin uygunsuz yorumu kaldırabilir | Admin sildi | Kayıt **duruyor**, `IsHidden=true` ✅ |

### Favori uçları

| PDF | Durum |
|---|---|
| `POST /api/v1/events/{eventId}/favorite` | ✅ idempotent |
| `DELETE /api/v1/events/{eventId}/favorite` | ✅ idempotent |
| `GET /api/v1/users/me/favorites` | ✅ |

---

## 2. PDF'in söylemediği: "geçerli bilet" ne demek?

PDF *"geçerli bilet almış kullanıcı"* diyor ama hangi bilet durumlarının geçerli
sayılacağını söylemiyor. Karar bana ait:

| Durum | Geçerli mi? | Neden |
|---|---|---|
| `Active` | ✅ | Bileti var; turnikeden geçmemiş olabilir ama parasını ödedi |
| `Used` | ✅ | Girişte okutuldu, kesinlikle katıldı |
| `Refunded` | ❌ | Parasını geri aldı, etkinliğe gitmedi |
| `Cancelled` / `Expired` | ❌ | Bilet hiç geçerli olmadı |

`Refunded`'ı dışlamamın somut sebebi: aksi hâlde **bilet alıp hemen iade eden
biri yorum hakkı kazanırdı.** Sahte yorum üretmenin en ucuz yolu olurdu —
al, iade et, kötü puan ver.

---

## 3. Silme: aynı uç, iki farklı davranış

PDF iki ayrı kural veriyor ve ikisi aynı uçtan yönetiliyor:

| Kim | Ne olur | Neden |
|---|---|---|
| **Kullanıcı** kendi yorumunu siler | Soft delete | Yorum kaybolur; isterse yenisini yazabilir (unique index `IsDeleted=false` filtreli) |
| **Admin** başkasının yorumunu siler | **Gizlenir** (`IsHidden`) | Kayıt durur, denetim izi korunur, kullanıcı yerine yenisini **yazamaz** |

Admin neden silmiyor? Çünkü silinen bir yorumun yerine kullanıcı aynısını
tekrar yazabilirdi ve moderasyon **sonsuz bir kovalamacaya** dönerdi.
Gizlemek kalıcı. Ayrıca *"yorumum neden kayboldu?"* sorusuna cevap verebilmek
için `HiddenReason` saklanıyor.

**Doğrulandı:** admin gizledikten sonra ortalama 2.5 → 4.0, toplam 2 → 1;
veritabanında kayıt `IsHidden=t, HiddenReason='Uygunsuz dil', IsDeleted=f`.

---

## 4. Gizlilik: yorumlarda tam ad gösterilmiyor

Backend `"Adem U."` şeklinde döndürüyor.

Yorumlar herkese açık ve arama motorlarınca indekslenebilir. **Tam ad +
katıldığı etkinlik** birleşince kişinin nerede olduğunu gösteren bir iz oluşur.
Soyadının ilk harfi aynı adlı iki kullanıcıyı ayırmaya yeter ama kimliği açık
etmez. E-posta **asla** dönmüyor.

---

## 5. PDF'in bir sprintteki kuralı, başka bir sprintte olmayan bir işi zorunlu kıldı

*"Etkinlik tamamlanmadan yorum yapılamaz"* kuralını uygulamaya oturunca fark
ettim ki `Event.Complete()` metodu **var ama hiçbir yerden çağrılmıyor.**

Yani hiçbir etkinlik `Completed` durumuna geçmiyordu → **hiç kimse yorum
yapamazdı.** Özellik "yazılmış" olurdu ama hiçbir zaman çalışmazdı.

PDF Sprint 9'un arka plan işleri listesinde bu iş **sayılmıyor**. Ama Sprint
12'nin kuralı onsuz anlamsız. Ekledim: `complete-past-events`, saatte bir.

### Neden "etkinlik tarihi geçti" kontrolü yetmiyor?

Yorum kontrolünü `EventDate < şimdi` diye de yazabilirdim. Yazmadım çünkü
**durum, tarihten daha fazla şey anlatıyor:** bir etkinlik iptal edilmiş veya
askıya alınmış olabilir. Tarihi geçmiş olması "gerçekleşti" demek değil.

### Durum makinesi beni hatadan korudu

İlk yazımımda doğrudan `Complete()` çağırıyordum. İş çalışınca hata aldım:

```
DomainException: Etkinlik SalesOpen durumundan Completed durumuna gecemez.
```

Durum makinesi `SalesOpen → SalesClosed → Completed` diyor. **Ara durum
atlanamıyor** — ve bu doğru bir kısıt: satışı açıkken "tamamlandı" olan bir
etkinlik, geçmiş bir etkinliğe bilet satmaya devam ediyor demektir.

Çözüm ara durumu **atlamak** değil, **geçmek**. Mimari testlerin ve
derleyicinin yaptığı şeyin aynısı: varsayımımı sessizce kabul etmek yerine
reddetti.

### Zamanlama kararı

`GracePeriodHours = 6`: `EventDate` etkinliğin **başlangıç** zamanı. Bir konser
20:00'de başlayıp 23:00'te bitebilir. Tam 20:01'de "tamamlandı" desek, etkinlik
**daha sürerken** yorum yapılabilirdi.

---

## 6. Sprint 2'den beri süren bir hata buldum

Yorum özelliğini tarayıcıda denerken yorum tarihi **"01 Ocak 1"** göründü.
Veritabanına bakınca sebebi çıktı:

```
CreatedAt = -infinity     (DateTimeOffset.MinValue)
```

`AuditableEntity` üzerindeki `CreatedAt` / `CreatedBy` / `UpdatedAt` alanları
**tanımlı ama hiçbir yerde doldurulmuyordu.**

### Etkilenen tablolar

| Tablo | Dolu / Toplam |
|---|---|
| Tickets | **0 / 4** |
| Reservations | **0 / 7** |
| Payments | **0 / 3** |
| Reviews | **0 / 2** |

### Daha önce fark edemedim — belirtisini yanlış yorumladım

Sprint 11'de günlük satış özeti işini test ederken rapor **"0 bilet, 0
rezervasyon"** dönmüştü. O sorgu tam olarak şunu kullanıyor:

```csharp
.Where(t => t.CreatedAt >= start && t.CreatedAt < end)
```

Bunu *"dün hiç satış olmamış, normal"* diye yorumlayıp geçtim. Oysa rapor
`CreatedAt` boş olduğu için **hiçbir zaman** veri bulamayacaktı.

> **Ders:** beklediğim sonucu gören bir test, geçen bir test değildir.
> *"0 döndü ve bu makul"* ile *"0 döndü çünkü sorgu bozuk"* aynı görünüyordu.

### Çözüm: interceptor

Her `Create()` metoduna `CreatedAt = UtcNow` eklemek de mümkündü. Yapmadım:

1. **29 entity var.** Birinde unutmak kaçınılmaz — hatanın tam olarak böyle
   oluştuğunu düşünüyorum.
2. `UpdatedAt`'i entity içinde tutmak imkânsız: hangi metodun "güncelleme"
   sayılacağını her seferinde elle işaretlemek gerekirdi.
3. Domain katmanı **zamanı ve kullanıcıyı bilmemeli**.

`AuditFieldsInterceptor` tek yerde ve otomatik. Yeni entity eklendiğinde
hiçbir şey yapmaya gerek yok.

### Interceptor'da üç ek karar

**a) `CreatedAt` üzerine yazılmasın**
`Modified` durumunda `IsModified = false` yapıyorum. EF, bellekteki değer
yanlışsa veritabanındakinin üzerine yazabilirdi.

**b) `Remove()` çağrısı soft delete'e çevriliyor**
`AuditableEntity` soft delete destekliyor ama biri `context.Remove(entity)`
çağırırsa EF **gerçek** bir `DELETE` üretir ve kayıt kaybolur — soft delete
altyapısı hiçbir işe yaramaz. Interceptor durumu `Modified`'a çevirip
`IsDeleted` bayrağını set ediyor.

> `Favorite` bir `AuditableEntity` **değil**, bu yüzden gerçekten siliniyor —
> Sprint 12'de bilinçli olarak böyle tasarlandı. Bir bağlantı kaydının denetim
> değeri yok ve kullanıcı *"favorilerimi temizledim"* dediğinde verinin
> gerçekten gitmesini bekler.

**c) Senkron `SavingChanges` de yazıldı**
Uygulamada senkron `SaveChanges` kullanmıyoruz. Yine de yazdım: biri ileride
(test, seed script) senkron çağırırsa denetim alanları **sessizce** boş
kalırdı — yani düzelttiğim hatanın aynısı geri gelirdi.

### Doğrulama

```
Eski yorum (interceptor öncesi) : -infinity        | CreatedBy yok
Yeni yorum (interceptor sonrası): 2026-08-28 06:55 | CreatedBy dolu
```

Geliştirme veritabanındaki 17 bozuk kayıt tek seferlik bir `UPDATE` ile
düzeltildi. **Gerçek bir üretim ortamında** bunun bir düzeltme migration'ı
olarak yazılması ve gerçek oluşturulma zamanlarının (varsa) başka bir
kaynaktan türetilmesi gerekirdi.

---

## 7. Güvenlik: favoride de aynı IDOR kapısı vardı

Favori eklerken yalnızca *"etkinlik var mı"* diye bakmak **yetmez**. Görünürlük
filtresi de şart: aksi hâlde kullanıcı bir Id tahmin edip **taslak** bir
etkinliği favorileyebilirdi.

Tek başına zararsız görünüyor — ama "favorilerim" listesi o etkinliğin
**başlığını** gösteriyor. Yani yayınlanmamış bir etkinliğin adını sızdırmış
olurduk.

Bu, Sprint 11'de etkinlik detayında kapattığımız IDOR açığına giden **başka
bir kapı**. Aynı kontrol burada da uygulandı.

**Doğrulandı:** taslak/iptal etkinlik favorilenmeye çalışıldı → **404**.

**İzolasyon doğrulandı:** rakip kullanıcı 1 favori, demo kullanıcı 0 favori —
birbirine sızmıyor.

---

## 8. Frontend kararları

### Bilet kontrolünü istemcide yapmıyorum

Yapabilirdim: "biletlerim" listesini çekip bu etkinlik var mı diye bakardım.
Yapmadım çünkü:

1. Fazladan bir istek, **herkes için**, yalnızca bir düğmeyi gizlemek uğruna
2. Sunucu **zaten** kontrol ediyor — istemcideki kontrol yalnızca kolaylık
   olurdu, güvenlik değil
3. Yanlış pozitif riski: bilet listesi bayatsa düğmeyi haksız yere gizlerdim

Hatayı açıkça göstermek, sessizce düğme gizlemekten daha dürüst.

### Favori düğmesinde iyimser güncelleme

Kalp ikonu sunucu cevabını **beklemeden** doluyor — favorileme "anlık"
hissetmesi gereken bir eylem.

Riski: istek başarısız olursa ekran **yalan** söylemiş olur. Bu yüzden
`onError`'da eski duruma **geri alınıyor**. Geri alma olmadan yapılan iyimser
güncelleme, arayüz ile sunucunun sessizce ayrışması demektir.

`onMutate`'te devam eden çekimi **iptal ediyorum** — etmeseydim o çekim
iyimser güncellememizden sonra tamamlanıp eski veriyi geri yazabilirdi
(kalp bir dolup bir boşalırdı).

### Yıldızlar sadece görsel değil

Salt okunur hâlde tek bir `aria-label` ("5 üzerinden 4 puan") var ve yıldızlar
`aria-hidden`. Seçilebilir hâlde ise **gerçek radio düğmeleri** kullanılıyor —
klavyeyle ok tuşlarıyla gezilebiliyor.

Yalnızca sembol olarak çizseydik ekran okuyucu *"yıldız yıldız yıldız"* derdi.

### Düzenlenmiş yorumlar işaretleniyor

`(duzenlendi)` etiketi — okuyan kişi metnin sonradan değiştirilmiş
olabileceğini bilmeli.

---

## 9. CA1845: kuralın önerdiği düzeltme burada uygulanamıyordu

Ad kısaltmasını önce sorguya yazmıştım:

```csharp
r.User.FirstName + " " + r.User.LastName.Substring(0, 1) + "."
```

Derleyici CA1845 ile uyardı: *`Substring` yerine `AsSpan` kullanın.* Ama
`AsSpan` bir **ifade ağacında** çalışmaz — EF onu SQL'e çeviremez.

Yani kuralın önerdiği düzeltme burada **uygulanamaz**. İki seçenek kaldı:
kuralı bastırmak, ya da kısaltmayı sorgudan çıkarmak.

**İkincisini seçtim.** Yalnızca uyarıyı susturmakla kalmıyor, daha da doğru:
string birleştirme SQL'de değil C#'ta yapılıyor ve veritabanı yalnızca ham
sütunları döndürüyor. Sayfa başına en fazla 20 satır olduğu için bellekte
işlemenin maliyeti yok.

---

## 10. Test sonuçları

Gerçek PostgreSQL + çalışan API + tarayıcı ile:

- 6 iş kuralının **hepsi** doğrulandı (yukarıdaki tablo)
- Favori uçları idempotent: aynı isteği iki kez → **204, 204**
- Kullanıcı izolasyonu: favoriler birbirine sızmıyor
- Yorum listesi anonim erişime açık, gizlenen yorumlar dönmüyor
- Ortalama ve dağılım tutarlı (tek `GroupBy`'dan hesaplanıyor)
- Tarayıcıda: yıldız dağılım çubuğu, favori düğmesi, düzenle/sil düğmeleri
  yalnızca kendi yorumunda

**214 test yeşil, 0 uyarı, 0 hata.**
