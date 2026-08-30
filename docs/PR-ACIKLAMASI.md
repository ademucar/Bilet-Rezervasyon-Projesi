# Etkinlik, Biletleme ve Koltuk Rezervasyon Sistemi — Sprint 2–19

> Bu dosya, `main` dalına açılacak Pull Request'in açıklamasıdır.
> İçeriği GitHub'daki PR kutusuna kopyalanır.

## Ne yapıldı?

PDF'in 19 sprintlik kapsamı: .NET 9 Web API (Onion Architecture, CQRS)
+ React/TypeScript arayüz. 69 commit, 363 dosya.

Sprint 1 `main` üzerinde tamamlanmıştı; bu PR Sprint 2–19 arasını
getiriyor.

## Kapsam

**Backend** — Onion Architecture, CQRS + MediatR, FluentValidation, EF
Core + PostgreSQL, JWT kimlik doğrulama, rol/politika yetkilendirme,
`xmin` ile iyimser eşzamanlılık, Redis önbellek, SignalR, Hangfire
arka plan işleri, Outbox Pattern, ödeme soyutlaması, Serilog +
OpenTelemetry, sağlık kontrolleri, hız sınırlama, dosya yükleme
güvenliği, Swagger (Scalar).

**Frontend** — React 19 + TypeScript, TanStack Query, Zustand, React
Hook Form + Zod, görsel koltuk seçimi, geri sayım, ödeme akışı,
organizatör ve admin panelleri, Error Boundary, rota bazlı code
splitting.

**Test** — 337 test: 258 birim, 16 mimari, 23 entegrasyon
(Testcontainers ile gerçek PostgreSQL + Redis), 36 frontend (Vitest),
4 E2E (Playwright, masaüstü + mobil).

**CI/CD** — 10 aşamalı GitHub Actions pipeline, StyleCop, Prettier,
güvenlik taraması (NuGet + npm + Trivy), coverage raporu.

## Neden bu şekilde?

Her sprintin kararları ve gerekçeleri `docs/` altında 16 belgede
yazılı. Öne çıkan birkaçı:

- **Koltuk yarışı** iyimser eşzamanlılıkla çözüldü (kötümser kilit
  değil): PostgreSQL `xmin` sistem sütunu üzerinden. Kaybeden istek
  409 alıyor, veri bozulmuyor.
- **Outbox Pattern** en az bir kez teslim garantisiyle: e-posta
  gönderimi ile veritabanı işlemi aynı transaction'a giremediği için.
- **Önbellek opsiyonel** (Null Object Pattern): Redis yoksa sistem
  yavaş çalışır, bozuk çalışmaz. Sağlık kontrolünde `Degraded`,
  `Unhealthy` değil.
- **`/health/live` hiçbir bağımlılığı kontrol etmiyor**: etseydi
  geçici bir veritabanı sorunu tüm kapsayıcıların öldürülmesine yol
  açardı.

## Nasıl doğrulandı?

Her sprint sonunda **çalıştırılarak** — kod okunarak değil. Bu yöntem
altı gerçek hata buldu:

| Sprint | Bulgu |
|---|---|
| 12 | Denetim alanları tanımlıydı, hiç doldurulmuyordu (`CreatedAt = -infinity`) |
| 15 | `SensitiveDataMasker` yazılmıştı, hiçbir yerden çağrılmıyordu |
| 16 | Correlation ID alanı ve indeksi vardı, 22 kaydın 22'sinde NULL'du |
| 17 | İade idempotency'si vardı, baktığı koleksiyon hiç yüklenmiyordu — **aynı para iki kez iade ediliyordu** |
| 18 | XML yorumları vardı, hiçbiri Swagger'a ulaşmıyordu (78 uçta 0 açıklama) |
| 19 | Dockerfile vardı, **imaj hiç derlenmiyordu** (`.editorconfig` kopyalanmıyordu) |

Altısı da doğru görünüyordu. Hiçbiri kod okunarak bulunamazdı.

Ayrıca dört kez test kırıldı ve **dördünde de kod doğruydu** — benim
varsayımlarım yanlıştı (Sprint 17, `docs/14`).

### Bu PR'da çalıştırılan kontroller

```
dotnet build -c Release   →  0 uyarı, 0 hata
dotnet test               →  258 + 16 + 23 = 297 test yeşil
npm run lint              →  temiz
npx prettier --check .    →  temiz
npx tsc --noEmit          →  temiz
npx vitest run            →  36 test yeşil
docker build              →  başarılı (364 MB)
```

## Notlar

**Dal adı yanıltıcı.** `feature/sprint-2-domain-entities` olarak
başladı ama Sprint 2–19 arasını içeriyor.
`docs/04-git-stratejisi.md` her sprint için ayrı dal yazıyor ve buna
uymadım. Sebebi sprintler arası bağımlılıklar oldu: Sprint 7 koltuk
kilidi Sprint 4 oturma planına, Sprint 8 ödeme Sprint 7 rezervasyona
dayanıyor. Her birini ayrı dala alsaydım sürekli birbirini bekleyen
PR'lar çıkacaktı.

Doğrusu yine de ayrı dal + ayrı PR'dı. Geriye dönük bölmek geçmişi
yeniden yazmayı gerektirir; PDF *"sahte commit geçmişi oluşturmak"*
diyerek bunu yasaklıyor. Olduğu gibi bırakıp burada yazmayı tercih
ettim. Commit geçmişi sprint sprint ilerliyor, incelemede o sıra
takip edilebilir.

**Bilinçli olarak ertelenenler** (her biri ilgili belgede yazılı):

- SonarQube CI'da sürekli çalışmıyor — yerelde Docker ile
  çalıştırıldı (kalite kapısı OK, 1 bug bulunup düzeltildi); sürekli
  çalışması için sunucu veya SonarCloud hesabı gerekiyor (`docs/16`)
- E2E testleri CI'da değil — ayrı test veritabanı gerekiyor
- Etkinlik listesi giriş istiyor; anonim gezinme için ayrı bir üst
  çubuk gerekiyordu, yapmadım. Bedeli: etkinlik sayfaları arama
  motoruna kapalı
- Harici HTTP çağrısı izlemesi kayıtlı ama ölçülemedi — mevcut kod
  yollarında giden HTTP çağrısı yok (`docs/13`)
- SignalR gerçek bağlantısı elle doğrulanıyor; jsdom'da WebSocket yok

**İncelerken özellikle bakılması istenenler:**

1. `CreateReservationCommand` — eşzamanlılık çözümü ve `catch
   (DbUpdateConcurrencyException)` dalı
2. `ProcessOutboxMessagesCommand` — üstel geri çekilme ve dead-letter
3. `backend/.editorconfig` — StyleCop kural seçimleri ve gerekçeleri
4. `docs/16` — kod kalitesi kararlarının tamamı

## Yapay zekâ kullanımı

PDF'in izin verdiği kapsamda kullanıldı: kavram araştırma, hata mesajı
anlama, alternatif yaklaşımlar, test senaryosu fikirleri, kod
inceleme, doküman taslağı.

Katkının görünür olması için ilgili commit'lerde `Co-Authored-By`
satırı bırakıldı. PDF'in yasak listesinde *"kod kaynağını gizlemek"*
maddesi var; o satırı silmek tam olarak bu olurdu.

Kod birinci ağızdan açıklamalı yazıldı, her sprintte çalıştırılarak
doğrulandı ve 69 ayrı commit'te teslim edildi. Karşılaştığım
hataların ve fikir değiştirdiğim yerlerin kaydı hem commit
mesajlarında hem kod içindeki açıklamalarda duruyor.
