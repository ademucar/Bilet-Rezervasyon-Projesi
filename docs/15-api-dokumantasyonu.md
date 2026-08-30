# Sprint 18 — Swagger ve API Dokümantasyonu

## Bu sprintte ne yaptım?

PDF üç başlık istiyor: Swagger'da 10 madde, API versioning ve OpenAPI
client üretimi araştırması.

Başlangıçta elimde yalnızca `AddOpenApi()` + `MapOpenApi()` vardı — ham
bir JSON belgesi, arayüz yok, açıklama yok.

---

## Swagger'ın 10 maddesi

| # | Madde | Nasıl karşılandı | Sonuç |
|---|---|---|---|
| 1 | Endpoint açıklamaları | `XmlDocumentationTransformer` | **78/78** |
| 2 | Request örnekleri | `RequestExampleTransformer` | 21/34 gövdeli uç |
| 3 | Response örnekleri | XML `<response>` etiketleri | 373 özel açıklama |
| 4 | Validation hataları | `ProblemDetailsTransformer` | 69 uçta 400 + örnek |
| 5 | Authentication | `SecuritySchemeTransformer` | Bearer şeması |
| 6 | Yetkili roller | `AuthorizationTransformer` | 60 uçta yetki notu |
| 7 | Pagination açıklaması | Belge açıklaması | ✅ |
| 8 | Problem Details modeli | Belge + örnek gövdeler | ✅ |
| 9 | Idempotency-Key | `IdempotencyHeaderTransformer` | 3 uçta başlık |
| 10 | API version bilgisi | `DocumentInfoTransformer` | `v1` |

### Neden transformer, neden her uca öznitelik değil?

60'tan fazla ucumuz var. Her birine `[ProducesResponseType(401)]`
eklemek yüzlerce satır tekrar demek — ve birini unutunca belgeyle
gerçek arasında **sessiz bir fark** oluşur.

Transformer kuralı tek yerden uyguluyor: *"kimlik doğrulaması
gerektiren her uca 401 ekle"*. Yeni bir uç eklendiğinde hiçbir şey
yapmak gerekmiyor.

Yetki bilgisi de koddan okunuyor — `[Authorize]` öznitelikleri
yansımayla taranıyor. Elle yazsaydık bir ucun yetkisi değiştiğinde
belgeyi güncellemeyi unuturduk ve Swagger "herkese açık" derken uç 403
dönerdi. **Yanlış dokümantasyon, hiç dokümantasyon olmamasından
kötüdür — çünkü ona güveniliyor.**

### Scalar, Swashbuckle değil

.NET 9 ile OpenAPI **belgesi** üretimi çerçeve içine girdi. Eksik olan
tek şey arayüzdü.

Swashbuckle da kullanılabilirdi ama o hem belge üretimini hem arayüzü
birlikte getiriyor — yerleşik üreticiyle **çakışırdı** ve iki farklı
OpenAPI belgesi üretilirdi. Scalar yalnızca arayüz: yerleşik
üreticinin ürettiği belgeyi okuyup gösteriyor.

Arayüz sadece geliştirmede açık. Üretimde açık bırakmak, tüm uçların
ve hata kodlarının haritasını saldırgana hazır sunmak olurdu.

---

## 🔴 Sorun 1: .NET 9 XML yorumlarını okumuyor

`GenerateDocumentationFile`'ı açtım, Swagger'a baktım: **78 ucun
hiçbirinde açıklama yoktu.**

Sebep: .NET 9'un yerleşik OpenAPI üreticisi XML yorumlarını okumuyor —
o özellik .NET 10 ile geldi.

Kendi okuyucumu yazdım (`XmlDocumentationTransformer`): uygulama
klasöründeki tüm `Ticketing.*.xml` dosyalarını okuyup `<summary>`,
`<remarks>`, `<response code="...">` ve `<param>` etiketlerini belgeye
bağlıyor.

Bilinçli bir basitleştirme var: XML üye kimlikleri parametre türlerini
de içeriyor ve o imzayı yansımadan birebir üretmek generic tipler ve
ref parametreler yüzünden hataya çok açık. "Tip adı + metot adı" ile
eşliyorum. Bedeli aşırı yüklenmiş metotlarda ilk eşleşmenin
kullanılması — controller'larımızda aşırı yükleme yok.

Sonuç: **0/78 → 78/78.**

### Yan bulgu: 8 bozuk XML yorumu

`GenerateDocumentationFile` açılınca derleyici mevcut yorumları
doğrulamaya başladı ve 8 gerçek hata çıktı:

```csharp
/// 1) DbSet<T> ZATEN bir repository'dir        // < ve > kaçırılmamış
/// "EventDate < simdi" diye de yazabilirdim
/// .Where(t => t.CreatedAt >= start && ...)    // & de kaçırılmamış
```

Bunlar Sprint 2'den beri koddaydı ve **hiç fark edilmemişti** — çünkü
XML üretimi kapalıyken derleyici yorumlara bakmıyor. Hepsi `&lt;`
`&gt;` `&amp;` ile düzeltildi.

Ayrıca üç dosyada `///` yorumları **kayıt parametre listesinin içine**
yazılmıştı (`CS1587: geçerli bir dil öğesine koyulmamış`). Doğru yer
kayıt bildiriminin üstündeki `<param>` etiketi — Swagger şema alan
açıklamalarını oradan okuyor.

### CS1591 ve CS1573 neden susturuldu?

`TreatWarningsAsErrors` açık olduğu için bu iki uyarı derlemeyi
durduruyordu:

- **CS1591** (eksik XML yorumu): her public tipe ve üyeye yorum
  yazmayı zorunlu kılardı — yüzlerce DTO alanı dahil. Bu faydalı
  dokümantasyon *üretmez*; `"Gets or sets the Id."` türü anlamsız
  satırlar üretir ve gerçek açıklamaları içinde kaybederdi.
- **CS1573** (bazı parametrelerin `<param>` etiketi yok): bir metodun
  *bir* parametresini belgelediğinde *hepsini* belgelemeyi zorunlu
  kılıyor. Yazım tarzımızla çelişiyor — yalnızca açıklama gerektiren
  parametreyi belgeliyoruz.

Yaklaşım: açıklamayı **anlamlı olduğu yere** yazıyoruz, derleyiciden
zorunluluk beklemiyoruz.

---

## API Versioning

PDF'in istediği yapı zaten Sprint 1'den beri yerinde:

```
/api/v1/events
/api/v1/reservations
/api/v1/payments
```

**Strateji dokümantasyonu** eksikti; artık Swagger'ın ilk ekranında:

> URL segmenti tabanlı sürümleme seçildi. Tarayıcıdan denemesi kolay,
> önbellek anahtarları doğal olarak ayrışır ve loglarda hangi sürümün
> çağrıldığı açıktır. Header tabanlı sürümleme bu üçünü de
> zorlaştırırdı.

Yanıt başlıklarında `api-supported-versions` ile desteklenen sürümler
bildiriliyor.

---

## OpenAPI Client araştırması

PDF üç araç öneriyor: NSwag, OpenAPI Generator, Orval. Okuyup geçmek
yerine **gerçekten kurup çalıştırdım.**

### Neden Orval?

| Araç | Değerlendirme |
|---|---|
| **NSwag** | .NET dünyasında güçlü ama TypeScript çıktısı eski tarz (class tabanlı). React/TanStack Query ile uyumu zayıf. |
| **OpenAPI Generator** | Çok dilli ve olgun ama **Java** gerektiriyor. Frontend araç zincirimize Java bağımlılığı eklemek orantısız. |
| **Orval** | Node tabanlı (zaten var), TanStack Query'yi doğrudan destekliyor, yapılandırması tek dosya. |

### Ne ürettim, neyi benimsedim?

Orval `6423 satır` tip üretti. Ama **kancaları benimsemedim**:

- Elimizde 1100+ satırlık, çalışan ve Sprint 17'de **test edilmiş** bir
  istemci katmanı var. Toptan değiştirmek Sprint 18'in kapsamını aşar
  ve o testleri geçersiz kılardı.
- Üretilen kancalar bizim özel davranışlarımızı bilmiyor: token
  yenileme, 409 sonrası koltuk haritasını tazeleme, correlation ID
  başlığı.

Yani: *araştırdım ve şu kadarını benimsedim* — hepsi ya da hiçbiri
değil.

### 🔴 Sorun 2: Araştırma gerçek bir boşluk buldu

Üretilen tip şuydu:

```typescript
export type ReservationStatus = number;
```

Yani hiçbir şey. Sebep belgede görüldü:

```json
"ReservationStatus": { "type": "integer" }
```

**Enum'un hangi sayının ne anlama geldiği belgede hiç yoktu.**

Bunun sonucu kod üretimiyle sınırlı değil: Swagger'ı açan bir istemci
geliştiricisi `status: 3` gördüğünde ne yapacağını bilemiyordu. Kaynak
koda erişimi olmayan biri için bu alan tamamen anlamsızdı.

#### Neden string'e çevirmedim?

`JsonStringEnumConverter` ekleyip `"Confirmed"` gönderebilirdim. Daha
okunaklı olurdu ama bu **kırıcı** bir değişiklik: frontend sayılarla
karşılaştırma yapıyor ve Sprint 17 testleri de öyle.

Dokümantasyonu iyileştirmek için çalışan bir sözleşmeyi bozmak yanlış
takas. Bunun yerine sayıları koruyup anlamlarını belgeye ekledim:

```json
"ReservationStatus": {
  "type": "integer",
  "enum": [1, 2, 3, 4, 5, 6, 7],
  "description": "`1` = Pending, `2` = Locked, `3` = PaymentPending, ...",
  "x-enum-varnames": ["Pending", "Locked", "PaymentPending", ...]
}
```

`x-enum-varnames` OpenAPI Generator ve NSwag'in tanıdığı yaygın bir
uzantı; `description` ise her araçta ve insan gözünde çalışıyor.
Tanımayan araçlar için zararsız — bilinmeyen `x-` alanları yok
sayılıyor.

### Araştırmanın asıl kazancı: sapma kontrolü

Enum isimleri belgeye girince, frontend'in elle yazdığı tipleri
backend gerçeğiyle **karşılaştırabildim**:

```
=== FRONTEND ENUM'LARI BACKEND ILE UYUSUYOR MU? ===

ReservationStatus    UYUSUYOR  (7 deger)
EventStatus          UYUSUYOR  (8 deger)
TicketStatus         UYUSUYOR  (5 deger)
PaymentStatus        UYUSUYOR  (6 deger)
EventSeatStatus      UYUSUYOR  (4 deger)

Uyusmayan enum sayisi: 0
```

Arayüz tipleri de karşılaştırıldı: `ReservationDto`, `EventListItem`,
`TicketDto`, `PaymentDto` — **alan kümeleri birebir aynı**, tek fark
`status` alanının `number` yerine adlandırılmış tip olması.

Yani bugün sapma **yok**. Ama bunu ölçmeden bilemezdik ve altı ay
sonra bilemeyeceğiz. Sprint 19'da CI'a bir sapma kontrolü eklenecek:

```bash
npm run api:check    # orval && git diff --exit-code
```

### Üretilen dosya depoya konmuyor

`frontend/src/lib/api/generated/` gitignore'da:

- 6400+ satır ve **hiçbir yerden import edilmiyor** (şu an yalnızca
  karşılaştırma amaçlı). Kullanılmayan üretilmiş kodu depoda tutmak
  incelemelerde gürültü yaratır.
- Üretmek için API'nin ayakta olması gerekiyor; depodaki kopya kolayca
  bayatlar ve "gerçek" sanılır.

Yeniden üretmek: `npm run api:generate`

---

## Yan bulgu: Vitest, Playwright dosyalarını çalıştırıyordu

Sprint 17'de E2E eklendikten sonra `npm test` şu hatayı veriyordu:

```
Playwright Test did not expect test.describe() to be called here
```

36 birim testi yine geçiyordu ama paket "1 failed" raporluyordu.
**Sürekli kırmızı görünen bir test paketi, bir süre sonra hiç
bakılmayan bir test paketine dönüşür.**

Vitest varsayılan olarak tüm `*.spec.ts` dosyalarını topluyor. İki
araç ayrı sorumluluklara sahip; dosya düzeyinde de ayırmak
gerekiyordu:

```ts
exclude: ['node_modules/**', 'dist/**', 'e2e/**']
```

---

## Bu sprintin dersi

Önceki üç sprintte tekrar eden bir desen vardı: *"yazılmış ama
beslenmemiş kod"* (denetim alanları, maskeleyici, correlation ID,
iade idempotency'si).

Sprint 18 aynı desenin dokümantasyon hâli:

> **Kodun içinde duran bir açıklama, kimse okuyamıyorsa yok
> hükmündedir.**

XML yorumları Sprint 1'den beri yazılıyordu. Özenle, uzun uzun. Ama
`GenerateDocumentationFile` kapalıydı ve .NET 9 onları okumuyordu —
yani **hiçbiri kimseye ulaşmıyordu.**

Aynı şekilde enum'ların anlamı kodda apaçık duruyordu; belgede
`{"type": "integer"}` yazıyordu.

Ders: dokümantasyonun var olması yetmiyor, **ulaştığını doğrulamak
gerekiyor.** Bu sprintte bunu üretilen OpenAPI belgesini programla
sorgulayarak yaptım — Swagger'a bakıp "güzel görünüyor" demek yerine.

---

## Sonraki adımlar (bilinçli olarak ertelenenler)

- **Request örnekleri 21/34:** kalan 13'ü admin/CRUD uçları. Şeması
  zaten açıklayıcı; kullanıcı akışındaki uçların hepsi kapsandı.
- **Response örnek gövdeleri:** şu an açıklama var, örnek JSON yok
  (hata yanıtları hariç). Şema + açıklama çoğu durumda yeterli.
- **CI'da sapma kontrolü:** `npm run api:check` hazır, CI'a bağlanması
  Sprint 19.
- **Üretilen tiplerin benimsenmesi:** bugün sapma yok; sapma çıkarsa
  kademeli geçiş değerlendirilecek.
- **.NET 10 geçişi:** `XmlDocumentationTransformer` o gün gereksiz
  hale gelecek ve silinebilecek.
