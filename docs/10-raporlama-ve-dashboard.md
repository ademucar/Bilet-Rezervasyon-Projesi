# Organizatör Paneli, Raporlama ve Dışa Aktarma (Sprint 13)

---

## 1. PDF gereksinimleri — hepsi karşılandı

### Organizatör Dashboard (10 metrik)

| PDF metriği | Gerçek değer (test verisi) |
|---|---|
| Toplam etkinlik sayısı | 4 |
| Yayındaki etkinlikler | 2 |
| Toplam bilet satışı | 4 |
| Toplam gelir | ₺1.400,00 |
| İade edilen biletler | 0 |
| Doluluk oranı | %5.9 |
| En çok satan bilet türü | Öğrenci (2 adet) |
| Günlük satış grafiği | 30 nokta |
| Etkinlik bazlı gelir | Çubuk grafik |
| Bölüm bazlı doluluk | Balkon %10, Orta %4.2 |

### Admin Dashboard (10 metrik)

| PDF metriği | Gerçek değer |
|---|---|
| Toplam kullanıcı | 3 |
| Toplam organizatör | 1 |
| Toplam etkinlik | 4 |
| Aktif satışlar | 2 |
| Toplam işlem hacmi | ₺1.400,00 |
| İptal edilen etkinlikler | 1 |
| Başarısız ödeme oranı | %33.3 |
| En popüler şehirler | Adana=4 |
| En popüler kategoriler | Konser=4 |
| Sistem hata sayısı | 2 |

### 6 rapor ucu + 3 format

Beşi de **HTTP 200**; `POST /reports/export` **202 Accepted**.
CSV, Excel ve PDF üçü de üretildi ve doğru dosya imzasıyla indi.

---

## 2. PDF'in tanımlamadığı metrik: "Sistem hata sayısı"

PDF bu metriği istiyor ama neyin "sistem hatası" sayılacağını söylemiyor.
Tanımı ben verdim ve kodda açıkça yazdım:

> **Dead letter olmuş Outbox mesajları.**

Neden bu? Çünkü dead letter, sistemde gerçekten yanlış giden ve **insan
müdahalesi bekleyen** tek kalıcı kayıt. Beş kez denenmiş ve hâlâ başarısız
bir mesaj = gönderilmemiş bir e-posta veya oluşmamış bir bildirim.

**Elediklerim ve sebepleri:**

| Aday | Neden elendi |
|---|---|
| HTTP 500 sayısı | Loglarda, veritabanında değil. Saymak için log toplama altyapısı gerekir (Sprint 16) |
| Başarısız ödemeler | Bunlar **sistem** hatası değil, **iş** sonucu. Kart limiti yetmemesi bizim hatamız değil — ayrıca zaten ayrı bir metrik |
| Eşzamanlılık çakışmaları (409) | Bunlar sistemin **doğru çalıştığının kanıtı**. Hata saymak yanıltıcı olurdu |

Yani bu sayı *"operatörün bakması gereken iş sayısı"*. Sıfırdan büyükse
Hangfire panelinde işlenecek bir şey var — panelde kırmızı gösteriliyor.

---

## 3. Aynı ada sahip iki farklı metrik

Admin panelindeki **"toplam işlem hacmi"** ile satış özetindeki
**"net gelir"** karıştırılmamalı:

| Metrik | İade düşülür mü? | Neden |
|---|---|---|
| İşlem hacmi (admin) | **Hayır** | Finansal bir terim: sistemden **geçen** paranın toplamı |
| Net gelir (rapor) | **Evet** | Organizatörün eline geçen para |

İkisini aynı şekilde hesaplasaydık, biri mutlaka yanlış olurdu. Kartlarda
"İade düşülmemiş" notu var.

---

## 4. Güvenlik omurgası: `ReportScope`

Beş raporun **hepsi** aynı soruyu sormak zorunda: *"bu kullanıcı hangi
etkinliklerin verisini görebilir?"*

Bu mantığı her raporda tekrar yazsaydım, birinde unutmak kaçınılmazdı — ve
sonucu bir organizatörün **rakiplerinin gelir rakamlarını** görmesi olurdu.
Arayüzde hiçbir hata görünmezdi, sadece "çok fazla veri".

Tek bir `ReportScope` kaydı bunu çözüyor. Yeni bir rapor eklendiğinde bu
metodu çağırmamak derleme hatası vermez ama kod incelemesinde hemen göze
çarpar: *"scope nerede?"*

**Doğrulandı:**

| Test | Sonuç |
|---|---|
| Normal kullanıcı → rapor ucu | **403** |
| Normal kullanıcı → admin paneli | **403** |
| Kimliksiz → rapor ucu | **401** |
| Başkasının raporunu indirme | **404** |
| Geçersiz rapor türü (99) | **400** |

---

## 5. Arka planda çalışan bir işte yetki nasıl korunur?

> PDF: *"Rapor üretimi background job olarak çalıştırılmalı ve
> tamamlandığında kullanıcıya bildirim gönderilmelidir."*

Bu kural somut bir tasarım sorunu doğurdu: **arka planda HTTP bağlamı yok**,
yani `ICurrentUser` boş döner.

Handler'ı doğrudan çağırsaydık ya rapor *"yetkisiz"* hatası verirdi ya da
(çok daha kötüsü) kapsam boş kalıp **tüm veriyi** döndürürdü.

### Çözüm: sorguyu kapsamdan ayırmak

Her rapor sorgusunun gövdesini, kapsamı **dışarıdan alan** bir `static`
metoda taşıdım:

```csharp
internal static async Task<SalesSummaryReport> RunAsync(
    IApplicationDbContext context,
    ReportScope scope,        // <- dışarıdan
    ...)
```

Böylece aynı sorgu iki yerden çalışıyor:

| Yol | Kapsam nereden? |
|---|---|
| HTTP ucu | `ICurrentUser` |
| Arka plan işi | Outbox payload'ındaki `userId` |

**Kod tekrarı yok** ve yetki kuralları her iki yolda da **aynen**
uygulanıyor. Arka planda *"her şeyi gör"* gibi bir ayrıcalık yok.

### Rolü payload'a yazmadım

`ResolveForUserAsync` rolü **veritabanından okuyor**. Sebep: kullanıcının
rolü talep ile işleme arasında değişmiş olabilir (admin yetkisi alınmış
olabilir). Güncel rol her zaman doğru olandır.

### Yetki kontrolü talep anında

Yetki **talep anında** doğrulanıyor, işleme anında değil. Kontrolü
işleyiciye bıraksaydık ya yetkisiz rapor üretilirdi ya da hiçbir rapor
üretilemezdi.

---

## 6. Neden Hangfire.Enqueue değil, Outbox?

`BackgroundJob.Enqueue` da kullanılabilirdi. Sprint 9'da kurduğumuz Outbox'ın
üç üstünlüğü var:

1. Talep **veritabanı transaction'ı içinde** kaydediliyor — sunucu tam o anda
   çökse bile kaybolmuyor
2. Başarısız üretim üstel geri çekilme ile yeniden deneniyor
3. Beş denemeden sonra dead letter oluyor ve izleme ekranında görünüyor

Hangfire ile bunların hepsini ayrıca kurmak gerekirdi.

---

## 7. Dosya indirmede: tahmin edilemez kimlik yetki değildir

`exportId` bir Guid v7 ve tahmin edilmesi pratikte imkânsız. Ama buna güvenip
yetki kontrolünü atlamak **"gizlilik yoluyla güvenlik"** olurdu.

Kimlik sızabilir: sunucu erişim logları, tarayıcı geçmişi, paylaşılan bir
ekran görüntüsü, `Referer` başlığı. Sızan kimlikle **başkasının gelir raporu**
indirilebilirdi.

### Sahipliği nereden biliyoruz?

Ayrı bir "raporlar" tablosu açmadım. Bilgi **zaten duruyor**: rapor hazır
olduğunda sahibine bir bildirim yazılıyor ve o bildirimin `RelatedEntityId`
alanı `exportId`.

Yani *"bu raporun bildirimi bu kullanıcıya mı yazılmış?"* sorusu, sahiplik
sorusunun ta kendisi.

---

## 8. Dışa aktarma mimarisi: 15 metot yerine 8 parça

Her rapor tipi için ayrı Excel/CSV/PDF yazıcı yazmak **5 × 3 = 15** metot
demekti.

Bunun yerine her rapor kendini bir **tabloya** (başlık + satırlar) çeviriyor;
üç yazıcı da yalnızca bu tabloyu biliyor. **5 + 3 = 8** parça, ve yeni bir
rapor eklemek yeni bir yazıcı gerektirmiyor.

### Biçimlendirme neden tablo üreticisinde?

Para/tarih/yüzde biçimlendirmesini yazıcıya bıraksaydık üçü de aynı işi
tekrar yazardı — ve birinde farklı yaparsak aynı rapor **Excel'de başka,
PDF'te başka** görünürdü.

### Neden `InvariantCulture`?

Rapor dosyaları başka sistemlere aktarılıyor. Türkçe kültürde ondalık ayırıcı
**virgül**; `"1.234,56"` yazan bir CSV, virgülle ayrılmış bir dosyada **sütun
kaymasına** yol açar.

Ekranda Türkçe göstermek arayüzün işi; dışa aktarılan dosyanın işi değil.

### CSV için kütüphane almadım

CSV **yazma** kuralları toplam üç satır (RFC 4180). Üçüncü bir bağımlılık
eklemek; güvenlik taraması, sürüm takibi ve geçişli bağımlılık maliyeti
getirir.

*(CSV **okumak** farklı olurdu — çok daha zor ve orada kütüphane kullanırdım.)*

---

## 9. Üç gerçek hata buldum ve düzelttim

### a) EF `GroupBy` + record kurucusu çevrilemiyor

`revenue-by-event` ucu **HTTP 500** döndü:

```
InvalidOperationException: The LINQ expression ... could not be translated
```

EF Core, `GroupBy` sonucunu bir **record kurucusuna** projelendiremiyor
(anonim tipe ise sorunsuz çeviriyor).

**Aynı desen dashboard'ın 4 yerinde daha vardı.** Uçları tek tek test
etmeseydim, organizatör ve admin panellerinin ikisi de üretimde 500
verecekti.

Çözüm: anonim tiple gruplayıp record'a **bellekte** geçmek. Gruplama ve
toplama hâlâ SQL'de — yalnızca tipe dönüşüm bellekte.

### b) `Event.Complete()` çağrılmıyordu *(Sprint 12'de bulundu)*

Bu sprint onun devamı: `complete-past-events` işi artık saatte bir çalışıyor
ve durum makinesinin `SalesOpen → SalesClosed → Completed` yolunu izliyor.

### c) CSV'de UTF-8 BOM eklenmiyordu

Yorumda *"BOM ekliyoruz, Excel için şart"* yazıyordu. **Eklenmiyordu.**

```csharp
new UTF8Encoding(true).GetBytes(...)   // BOM EKLEMEZ
```

O bayrak yalnızca `GetPreamble()`'ın ne döndüreceğini belirliyor; `GetBytes`
onu **kullanmıyor**. BOM ancak `StreamWriter` gibi preamble'ı kendisi yazan
sınıflarla eklenir.

Üretilen dosyanın ilk baytlarına bakarak buldum:

```
Beklenen : EF BB BF
Gerçek   : 45 74 6B 69   ("Etki")
```

Türkçe karakterler Excel'de bozuk çıkacaktı.

> **Ders:** kodun **niyetini** değil, **ürettiği çıktıyı** kontrol etmek
> gerekiyor. Yorum doğru şeyi anlatıyordu; kod yapmıyordu.

Düzeltildikten sonra: `efbbbf` ✅

---

## 10. Lisans ve paket kararları

| Paket | Lisans | Not |
|---|---|---|
| ClosedXML 0.105.1 | MIT | Excel üretimi |
| QuestPDF 2026.8.0 | **Community** | Yıllık geliri 1M USD altındaki kuruluşlar için ücretsiz |

QuestPDF lisans türünün **kodda açıkça belirtilmesini** şart koşuyor;
belirtilmezse ilk PDF üretiminde istisna fırlatıyor. `static` kurucuda bir kez
ayarlanıyor.

> Bu proje ticari bir ürüne dönüşürse lisans yeniden değerlendirilmeli.
> Kararın görünür kalması için kodda not düşüldü.

**8 projenin hiçbirinde güvenlik açığı olan paket yok.**

### XML yorum tuzağına üçüncü kez düştüm

`Directory.Packages.props`'a yorum eklerken yine `--` kullandım ve dosya
geçersiz XML oldu (CPM tamamen devre dışı kaldı). Bu sefer düzelttikten sonra
**XML doğrulaması** çalıştırdım. Kural basit: XML yorumlarında `--` yasak.

---

## 11. Dosya deposu: neden veritabanı değil disk?

- Rapor dosyaları megabaytlarca olabilir; veritabanının **her yedeği**
  bunları da taşır
- PostgreSQL büyük ikili veriyi TOAST tablolarına taşır, sorgular yavaşlar
- Rapor dosyası **geçici** bir çıktı — kaybolsa yeniden üretilebilir.
  Veritabanı ise doğruluk kaynağımız

Dosya adı olarak **Guid** kullanılıyor, rapor başlığı değil: başlık ileride
özelleştirilebilir olsaydı `"../../appsettings.json"` gibi bir **dizin geçişi**
(path traversal) açığı doğardı.

Birden fazla sunucuya ölçeklenirse disk paylaşılmaz; o zaman `IReportFileStore`
arayüzünün S3/Azure Blob uygulaması gelir ve **Application katmanında tek satır
değişmez**.

---

## 12. Frontend kararları

**Recharts ayrı parçada** (402 KB): bilet alan normal kullanıcı bu kodu **hiç**
indirmiyor.

**İki panel tek sayfada, sekmeli:** admin olan bir kullanıcı çoğu zaman aynı
zamanda organizatör ve iki panel arasında gidip geliyor. Ayrı adresler olsaydı
her geçiş tam sayfa yüklemesi olurdu.

**Beklentiyi açıkça söylüyorum:** "Rapor oluştur" düğmesi dosya **indirmiyor**.
Bunu yazmasaydık kullanıcı bir şey olmadığını düşünüp düğmeye tekrar tekrar
basardı — ve her basış yeni bir rapor üretirdi.

**Recharts tooltip tipi:** kütüphane `ValueType | undefined` kullanıyor. Cast
ile susturmak yerine gelen değeri **kontrol ediyorum** — cast, gerçekten
`undefined` geldiğinde çalışma zamanında patlamak demekti.

---

## 13. Uçtan uca doğrulama

Gerçek PostgreSQL + Redis + Hangfire + tarayıcı ile:

| Adım | Sonuç |
|---|---|
| 5 rapor ucu | Hepsi **200** |
| 2 panel | Hepsi **200**, 20 metrik dolu |
| `POST /reports/export` ×3 | **202 Accepted** |
| Outbox'a yazıldı | 3 mesaj |
| Arka planda üretildi | 3 dosya |
| **Bildirim gönderildi** | 3 bildirim, indirme linkiyle |
| Dosya imzaları | `%PDF`, `PK` (xlsx), `EF BB BF` (csv) |
| CSV içeriği | Başlık + 4 satır, doğru |
| Arayüzden talep | Bildirim: *"Satis Ozeti raporu Excel biciminde olusturuldu. 7 satir"* |

**214 test yeşil, 0 uyarı, 0 hata.**
