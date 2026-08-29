# Sprint 19 — CI/CD ve Kod Kalitesi

## Bu sprintte ne yaptım?

PDF üç başlık istiyor: 10 aşamalı pipeline, altı kod kalitesi aracı ve
branch koruması.

---

## 🔴 En önemli bulgu: Docker imajı hiç derlenmiyordu

CI'a "Docker Image Build" aşamasını eklemeden önce imajı gerçekten
derlemeyi denedim. **24 hatayla kırıldı:**

```
error CA1861: Prefer 'static readonly' fields over constant array
arguments  [Migrations/20260826171245_InitialCreate.cs]
```

Oysa `.editorconfig`'te bu kural Migrations klasörü için **zaten
kapatılmıştı** — Sprint 2'de EF'in ürettiği kodu analizden muaf
tutmuştuk.

### Sebep

Sorun kuralda değil, dosyanın imaja **hiç girmemesinde** idi:

```dockerfile
COPY Directory.Build.props Directory.Packages.props ./   # .editorconfig YOK
COPY src/ src/                                           # kökteki dosyayı kapsamıyor
```

`.editorconfig` derleme bağlamının kökünde duruyor. İmaj içinde
derlenen kod, **yerel derlemeden farklı analizör ayarlarına** tabiydi.

> Yerelde 0 hata, imajda 24 hata.

PDF diyor ki: *"Tüm proje Docker Compose ile ayağa kaldırılabilmelidir."*
Bu commit'ten önce o madde **karşılanmıyordu** — ve bunu ancak imajı
derlemeye çalışınca gördüm. 18 sprint boyunca kimse denememişti.

### Yan bulgu: derleme SDK sürümüne bağlıydı

Yerelde SDK 10 ile Release derlemesi geçiyordu; Docker'da SDK 9-alpine
ile kırılıyordu. Yeni SDK yeni analizör kuralları getiriyor.

Bu yüzden CI'da SDK sürümünü **açıkça sabitledim** (`9.0.x`), `latest`
değil. Aksi halde hiçbir kod değişmeden bir gün derleme kırılabilirdi.

---

## Pipeline: 10 aşama, 5 iş

| PDF aşaması | Nerede |
|---|---|
| Restore | `backend` işi |
| Build | `backend` (Release, TreatWarningsAsErrors) |
| Lint | Derlemenin parçası — aşağıda açıklandı |
| Unit Test | `backend` |
| Integration Test | `backend` (Testcontainers) |
| Architecture Test | `backend` |
| Frontend Test | `frontend` (Vitest) |
| Docker Image Build | `docker` |
| Security Scan | `security` + Trivy (`docker` işinde) |
| Artifact | `artifact` + her işte yüklemeler |

### Neden tek iş değil, beş?

1. **Paralellik** — backend ve frontend birbirini beklemiyor
2. **Teşhis** — *"CI kırıldı"* yerine *"Frontend Test kırıldı"* görmek,
   hangi logu açacağını baştan söylüyor
3. **Yeniden deneme** — yalnızca kırılan iş tekrar çalıştırılabiliyor

### Lint neden ayrı bir adım değil?

PDF "Lint" ve ".NET Analyzer" maddelerini ayrı sayıyor ama .NET'te
bunlar **derlemenin parçası**. `TreatWarningsAsErrors` açık; bir
analizör uyarısı derlemeyi durduruyor. Ayrı bir lint adımı, aynı
analizi ikinci kez çalıştırmak olurdu.

Frontend'de ise gerçekten ayrı: `oxlint` + `prettier --check` + `tsc`.

### Kararlar ve gerekçeleri

| Karar | Neden |
|---|---|
| `npm ci`, `npm install` değil | `install` kilit dosyasını güncelleyebiliyor — CI, geliştiricinin test ettiğinden **farklı** sürümler kurabilir |
| `prettier --check`, `--write` değil | CI'da `--write` kimsenin görmediği bir değişiklik yapıp sessizce geçerdi |
| `tsc --noEmit` ayrı adım | Vite tipleri **kontrol etmiyor** (esbuild yalnızca siliyor) |
| `if: always()` ile sonuç yükleme | Test sonuçlarına en çok **testler kırıldığında** ihtiyaç var |
| Testcontainers, `services:` bloğu değil | `services:` kullansaydık bağlantı dizeleri CI'da ve yerelde farklı olurdu |
| `concurrency: cancel-in-progress` | Üst üste commit'te eski çalıştırmanın sonucu artık kimseyi ilgilendirmiyor |

### Güvenlik taraması üç katmanlı

```
NuGet  →  dotnet list package --vulnerable --include-transitive
npm    →  npm audit --audit-level=high
İmaj   →  Trivy (CRITICAL, HIGH)
```

`--include-transitive` **şart**: Sprint 9'da Hangfire üzerinden gelen
açıklı `Newtonsoft.Json 11.0.1`'i tam olarak böyle bulmuştum —
doğrudan bağımlılıklarımızda yoktu.

Bir tuzak var: `dotnet list package --vulnerable` açık bulsa bile
**çıkış kodu 0 döner**. Rapor üretip sessizce geçerdi. `grep` ile
açıkça kırıyoruz.

Trivy yalnızca CRITICAL/HIGH'da kırıyor. Orta seviyeleri de
kırsaydık her hafta başka bir sebeple kırılan bir CI olurdu — ve
sürekli kırmızı bir CI, görmezden gelinen bir CI'dır.

---

## Kod kalitesi araçları

| PDF maddesi | Durum |
|---|---|
| ESLint | ✅ oxlint (ESLint uyumlu, çok daha hızlı) |
| Prettier | ✅ eklendi, 48 dosya biçimlendirildi |
| .NET Analyzer | ✅ Sprint 1'den beri, `TreatWarningsAsErrors` |
| StyleCop | ✅ eklendi — aşağıda |
| SonarQube | ✅ yerelde çalıştırıldı — kalite kapısı OK, 1 bug bulundu ve düzeltildi |
| Test coverage | ✅ coverlet + ReportGenerator |

### StyleCop: kararlı sürüm çalışmadı

`StyleCop.Analyzers 1.1.118` (Şubat 2018) ile derleme **12 adet
`AD0001`** verdi — analizörün **kendisi** çöküyordu:

```
'SA1500BracesForMultiLineStatementsMustNotShareLine' çözümleyicisi
'Object reference not set to an instance of an object.' iletisiyle
NullReferenceException oluşturdu
```

Koleksiyon ifadeleri, hedef tipli `new()`, file-scoped namespace,
desen eşleştirme — kullandığımız sözdiziminin çoğu o sürümden **sonra**
geldi.

`1.2.0-beta.556`'ya geçtim. Sprint 16'daki Redis instrumentation
kararının aynısı: **kararlı sürüm yoksa ve etki alanı sınırlıysa beta
kabul edilebilir.** Burada etki gerçekten sınırlı — StyleCop yalnızca
derleme zamanında çalışıyor, üretime hiçbir şey gitmiyor.

### ~2400 ihlalden 0'a

**~300'ünü elle düzelttim:** sondaki virgüller (53 yer), parametre
yerleşimi, tek satıra sıkıştırılmış `if/else`, kapanış parantezi
öncesi boş satırlar, iç içe sözlük başlatıcısı.

**Kalanını gerekçesiyle kapattım.** Dört aile oluşuyor:

| Aile | Örnek | Gerekçe |
|---|---|---|
| Modern C# ile çelişen | SA1101 (`this.` öneki), SA1200 (using yerleşimi), SA1309 (alt çizgi yasağı) | .NET'in kendi kod tabanı da böyle yazmıyor |
| Analizörün tanımadığı sözdizimi | SA1008 (desen parantezleri), SA1000/1009/1010 | Araç 2018'den, sözdizimi daha yeni |
| İngilizce kalıp varsayan | SA1623 (*"Gets or sets"*), SA1642 (*"Initializes a new instance"*) | Yorumlarımız Türkçe |
| Dikey dilim düzeniyle çelişen | SA1402 (dosyada tek tip), SA1649 (dosya adı = tip adı) | CQRS dosyalarımız bir **özelliği** bir arada tutuyor |

Son aile en önemlisi. `PaymentQueries.cs` içinde DTO + sorgu + handler
+ projeksiyon birlikte duruyor. Kuralı uygulasaydık dört ayrı dosyaya
dağılırdı; bir özelliği değiştirmek için dört dosya açmak gerekirdi ve
hiçbiri tek başına anlaşılır olmazdı.

### Neden hepsini düzeltmedim?

Kalan kurallar **15.000 satırlık kodun biçimini toptan değiştirmeyi**
gerektiriyordu.

PDF, projenin *"tek commit ile teslim edilmesini"* ve *"anlaşılmayan
kodun eklenmesini"* yasaklıyor. Binlerce satırlık otomatik bir biçim
değişikliği o yasağın ruhuyla çelişir: kimse inceleyemez, kimse
savunamaz.

> StyleCop'un değeri geçmişi yeniden yazmak değil, **bundan sonra
> yazılanı tutarlı tutmak.**

Kapatılmayan tüm SA kuralları açık ve derlemeyi kırıyor: SA1028 (satır
sonu boşluk), SA1508, SA1501, SA1116, SA1115. Bunlar bu sprintte
**gerçekten düzeltildi** ve bundan sonra korunuyor.

### Testlerde StyleCop kapalı — politika kararı

Test kodunun okunurluk öncelikleri farklı: uzun açıklayıcı adlar,
gözle takip edilen kurulum, kurucu zincirleri.

**Önemli:** bu, testlerde kalite denetimi olmadığı anlamına gelmiyor.
CA kuralları ve `TreatWarningsAsErrors` testlerde de tam gücüyle
çalışıyor. Kapatılan yalnızca **biçim** kuralları.

### Bir gözlem: hata sayısı düzelttikçe arttı

`TreatWarningsAsErrors` ile derleme **ilk hatada duruyor**. Bir katman
düzelmeden sonrakiler hiç analiz edilmiyor.

Domain'i düzelttim → Application'ın ihlalleri göründü → onu düzelttim →
Infrastructure göründü... Yaklaşık **1 → 106 → 43 → 260 → 32 → 0**
şeklinde bir yol izledi. Yanıltıcı ama normal; bunu bilmeden "her
düzelttiğimde daha çok bozuluyor" diye paniklenebilirdim.

---

## SonarQube

Önce "yapılandırdım ama çalıştırmadım" diye yazmıştım — hesap ve token
gerektirdiği için. Sonra fark ettim ki SonarCloud'a hiç gerek yok:
sunucunun kendisi bir Docker imajı.

```bash
docker run -d --name sonarqube-local -p 9000:9000 \
  -e SONAR_ES_BOOTSTRAP_CHECKS_DISABLE=true sonarqube:community
```

Sonrası standart: token üret, `dotnet sonarscanner begin`, çözümü
derle, `end`.

> Git Bash kullanıyorsanız komutun başına `MSYS_NO_PATHCONV=1` koyun.
> Yoksa `/k:biletim` argümanını dosya yolu sanıp `C:/Program
> Files/Git/k:biletim`'e çeviriyor ve scanner "proje anahtarı eksik"
> diyor. Bunu anlamak on dakikamı aldı.

### Sonuçlar

| Ölçüm | Değer |
|---|---|
| Kalite kapısı | **OK** |
| Bug | 1 (düzeltildi, aşağıda) |
| Güvenlik açığı | 0 |
| Security hotspot | 0 |
| Kod kokusu | 77 |
| Kod tekrarı | %1.9 |
| Teknik borç | 358 dakika (~6 saat) |
| Analiz edilen satır | 16.792 |
| Güvenlik / bakılabilirlik notu | A / A |
| Güvenilirlik notu | C (tek bug yüzünden) |

### Bulunan tek bug

`OwnershipNotFoundMiddleware.cs`, 404 cevabını yazan satır:

```csharp
await context.Response.WriteAsJsonAsync(problem);
```

Sonar `CancellationToken` geçilmediğini söyledi ve haklıydı: istemci
bağlantıyı kapattığında (sekmeyi kapattı, ağı gitti) yazma işlemi
boşuna devam ediyordu. Yük altında bu, hiçbir yere gitmeyen cevapları
yazmakla uğraşan thread'ler demek.

`context.RequestAborted` eklendi.

### Kapatmadığım bulgular

**18 × S6964** — *"Value type property used as input in a controller
action should be nullable"*. Sonar, `int Page { get; set; }` gibi bir
alanın istekte hiç gönderilmediğinde sessizce `0` olacağını söylüyor.
Bizde bu alanların hepsi FluentValidation'dan geçiyor ve varsayılanı
kasıtlı; `int?` yapmak her handler'a bir null kontrolü daha eklerdi.

**10 × S125** — *"Remove this commented out code"*. Hepsi **yanlış
pozitif**: açıklama yorumlarının içinde örnek kod parçaları var, ör.

```
// Alternatif, her konfigürasyonu tek tek çağırmaktı:
//     modelBuilder.ApplyConfiguration(new UserConfiguration());
```

Bu satır ölü kod değil, *"bunu neden yapmadım"*ın kanıtı. Silmek
açıklamayı anlamsız bırakırdı.

**4 × S3776** — bilinçli olarak duruyor. Karmaşıklığı yüksek çıkan
metotlar rezervasyon ve ödeme akışları; parçalara bölmek okumayı
kolaylaştırmıyor, akışı üç dosyaya dağıtıyor.

> Sunucu yerelde çalıştı ve sonuçlar alındıktan sonra kapatıldı;
> CI'da sürekli çalışan bir SonarQube yok. Bunun için ya bir sunucu ya
> da SonarCloud hesabı gerekiyor.

---

## Test kapsamı

CI her çalıştırmada Cobertura raporu üretiyor ve özeti iş sayfasına
yazıyor. Yerel ölçüm (yalnızca birim testleri):

```
Line coverage:   23.7%  (2003 / 8438)
Branch coverage: 33.3%  (509 / 1524)
```

Bu **düşük görünüyor ve dürüst olmak gerekirse öyle** — ama sayının
bağlamı var:

- Yalnızca birim testlerinden; entegrasyon testleri (23 senaryo,
  gerçek HTTP + PostgreSQL) bu ölçüme dahil değil
- `Program.cs`, `*Setup.cs`, migration'lar ve DTO'lar payda içinde
- Kapsam **hangi satırın çalıştığını** ölçüyor, **ne kadar doğru
  çalıştığını** değil

Sprint 17'de bulunan iade hatası bunun kanıtı: o satırlar **kapsam
içindeydi** (idempotency kontrolü çalışıyordu) ama koleksiyon boş
olduğu için hiçbir şey yapmıyordu. Kapsam %100 olsaydı bile o hata
görünmezdi.

---

## Branch koruması

PDF dört madde istiyor. Bunlar **kodla değil, GitHub ayarlarından**
yapılıyor — bu yüzden burada adımları yazıyorum.

`Settings → Branches → Add branch protection rule`, dal: `main`

| PDF maddesi | GitHub ayarı |
|---|---|
| Main doğrudan değiştirilememeli | ☑ *Restrict who can push* + ☑ *Do not allow bypassing* |
| Pull Request zorunlu | ☑ *Require a pull request before merging* |
| Testler başarısızsa merge yok | ☑ *Require status checks to pass* → `Backend (build + test)`, `Frontend (lint + test + build)`, `Docker imaji`, `Guvenlik taramasi` |
| En az bir code review | ☑ *Require approvals: 1* |

Ek olarak önerilenler:

- ☑ **Require branches to be up to date** — yoksa iki PR ayrı ayrı
  yeşil olup birleştiklerinde kırılabilir (*semantic conflict*)
- ☑ **Require conversation resolution** — inceleme yorumları
  cevaplanmadan merge edilmesin

> **Not:** Tek kişilik bir projede "en az bir onay" kuralı kendini
> onaylayamayacağın için **seni de kilitler**. Staj değerlendirmesinde
> kuralın **kurulu olması** isteniyor; gerçek bir ekipte zaten ikinci
> bir kişi var.

Ayrıca `.github/pull_request_template.md` ekledim: her PR açıldığında
"ne / neden / nasıl doğrulandı" soruları otomatik geliyor.

---

## Bu projenin dersi

19 sprint boyunca tekrar eden tek bir desen vardı:

| Sprint | Bulgu |
|---|---|
| 12 | Denetim alanları tanımlıydı, hiç doldurulmuyordu |
| 15 | `SensitiveDataMasker` yazılmıştı, hiçbir yerden çağrılmıyordu |
| 16 | Correlation ID alanı ve indeksi vardı, hep NULL'du |
| 17 | İdempotency kontrolü vardı, baktığı koleksiyon hep boştu |
| 18 | XML yorumları vardı, hiçbiri Swagger'a ulaşmıyordu |
| **19** | **Dockerfile vardı, imaj hiç derlenmiyordu** |

Altısı da aynı cümleyle özetlenebilir:

> **Kod yazılmış olması, çalıştığı anlamına gelmiyor.**

Ve altısı da aynı yöntemle bulundu: **çalıştırıp sonucu ölçerek.**
Kodu okuyarak hiçbirini bulamazdım — hepsi doğru görünüyordu.

Sprint 15'te *"bir korumayı ekledikten sonra tetikleyip yanıtı
okuyorum"* demiştim. Sprint 16 *"üretilen veriyi sorgula"* ekledi.
Sprint 17 *"bir test kırıldığında önce testi sorgula"* dedi. Sprint 19
sonuncusunu ekliyor:

> **CI'ın asıl değeri, "yaptım" ile "çalışıyor" arasındaki farkı her
> commit'te ölçmesi.**

Bu sprintteki Docker bulgusu tam olarak bunun kanıtı: pipeline'a bir
aşama eklemek için o aşamayı bir kez çalıştırmam gerekti ve 18
sprinttir kırık olan bir şeyi ortaya çıkardı.

---

## Bilinçli olarak ertelenenler

- **SonarQube'un CI'da sürekli çalışması** — yerelde çalıştırıldı;
  CI'ya bağlamak için sunucu veya SonarCloud hesabı gerekiyor
- **CD (deployment)** — PDF yalnızca CI istiyor; imaj derleniyor ama
  bir kayıt defterine gönderilmiyor
- **E2E testleri CI'da** — ayrı bir test veritabanı ve API'nin ayağa
  kalkması gerekiyor; şu an yerelde çalışıyor
- **`npm run api:check`** — Sprint 18'de hazırlandı, CI'a bağlanmadı
- **SA1210 (using sıralaması) ve SA1414 (adlandırılmış tuple)** —
  katıldığım ama şimdi düzeltmediğim iki kural
