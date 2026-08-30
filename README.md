# Biletim — Etkinlik, Biletleme ve Koltuk Rezervasyon Sistemi

Konser, tiyatro ve etkinlikler için biletleme sistemi. Kullanıcı salon
planından koltuğunu seçiyor, koltuk 10 dakika kendisine kilitleniyor,
ödeme tamamlanınca QR kodlu bileti üretiliyor.

Staj projesi olarak 19 sprintte geliştirildi.

```
.NET 9  ·  React 19 + TypeScript  ·  PostgreSQL 17  ·  Redis  ·  Docker
```

## Neyi çözüyor

Bir biletleme sisteminin asıl zor kısmı bilet satmak değil, **aynı
koltuğu iki kişiye satmamak**. Kullanıcı koltuğa tıkladığı an ile
ödemeyi bitirdiği an arasında dakikalar geçiyor ve o sırada başkası da
aynı koltuğa bakıyor.

Bu projede o sorunu üç katmanda çözdüm:

| Katman | Ne yapıyor |
|---|---|
| Veritabanı | `(EventSessionId, SeatId)` üzerinde partial unique index — son savunma hattı |
| Uygulama | `SELECT ... FOR UPDATE` ile satır kilidi, `ORDER BY Id` ile deadlock önleme |
| Arayüz | SignalR ile anlık koltuk güncellemesi, yoklama yedeğiyle birlikte |

Ayrıntılı analiz: [docs/05-concurrency-analizi.md](docs/05-concurrency-analizi.md)

## Hızlı başlangıç

Tek gereksinim Docker.

```bash
cd docker
cp .env.example .env
docker compose up -d --build
```

| Servis | Adres |
|---|---|
| Arayüz | http://localhost:5173 |
| API (Scalar) | http://localhost:5000/scalar |
| Sağlık kontrolü | http://localhost:5000/health |
| Hangfire panosu | http://localhost:5000/hangfire |
| pgAdmin | http://localhost:5050 |
| Redis Insight | http://localhost:5540 |
| Mailpit (giden e-posta) | http://localhost:8025 |

Veritabanı ilk açılışta migration'ları uyguluyor ve referans verisini
(20 şehir, 8 kategori) yüklüyor. Kayıt olup hemen kullanmaya
başlayabilirsiniz.

## Mimari

Onion Architecture. Bağımlılıklar yalnızca içeri doğru; `Domain`
hiçbir şeye bağımlı değil.

```
Ticketing.Domain          30 entity, iş kuralları, hiçbir bağımlılık yok
Ticketing.Application     CQRS handler'ları, FluentValidation, soyutlamalar
Ticketing.Infrastructure  Redis, SMTP, Hangfire, JWT, dosya depolama
Ticketing.Persistence     EF Core, migration'lar, interceptor'lar
Ticketing.WebApi          Controller'lar, middleware, SignalR hub'ı
```

Bu kural yoruma bırakılmadı — **16 mimari testi** derleme zamanında
kontrol ediyor. `Domain`'e `Microsoft.EntityFrameworkCore` eklemeye
kalkarsanız test kırmızı yanıyor.

### Öne çıkan çözümler

- **Outbox Pattern** — bilet üretimi ile e-posta gönderimi aynı
  transaction'da; en az bir kez teslim, üstel geri çekilme, dead-letter
- **Idempotency** — `Idempotency-Key` başlığı ile çift ödeme ve çift
  rezervasyon engelleniyor
- **Optimistic concurrency** — PostgreSQL `xmin` sistem sütunu, ek kolon
  ve ek yazma maliyeti olmadan
- **Cache'siz çalışabilme** — Redis kapalıyken sistem ayakta kalıyor
  (Null Object Pattern), yalnızca yavaşlıyor
- **Correlation ID** — her istek uçtan uca izlenebiliyor; arka plan
  işlerine ve outbox kayıtlarına da taşınıyor

## Testler

```bash
cd backend  && dotnet test          # 258 birim + 16 mimari + 23 entegrasyon
cd frontend && npm test             # 36 bileşen testi
cd frontend && npm run test:coverage
```

Entegrasyon testleri **Testcontainers** ile gerçek PostgreSQL ve Redis
kaldırıyor; sahte veritabanı yok. Her test öncesi Respawn ile şema
temizleniyor.

## Dokümantasyon

Her sprintin kararları ve karşılaşılan hatalar `docs/` altında:

| | |
|---|---|
| [01-is-analizi.md](docs/01-is-analizi.md) | 15 soru ve cevapları |
| [05-concurrency-analizi.md](docs/05-concurrency-analizi.md) | Eşzamanlılık: üç katmanlı savunma |
| [06-outbox-ve-arka-plan-isleri.md](docs/06-outbox-ve-arka-plan-isleri.md) | Outbox ve Hangfire |
| [12-api-guvenligi.md](docs/12-api-guvenligi.md) | Hız sınırı, maskeleme, güvenlik başlıkları |
| [14-test-stratejisi.md](docs/14-test-stratejisi.md) | Neyi test ettim, neyi etmedim |
| [16-ci-cd-ve-kod-kalitesi.md](docs/16-ci-cd-ve-kod-kalitesi.md) | Pipeline, SonarQube sonuçları |
| [LIGHTHOUSE.md](docs/LIGHTHOUSE.md) | Performans ve erişilebilirlik ölçümü |
| [YAYINA-ALMA.md](docs/YAYINA-ALMA.md) | Tek sunucuya kurulum |

## Kod kalitesi

| | |
|---|---|
| Derleme | 0 uyarı (`TreatWarningsAsErrors`) |
| Analizörler | .NET Analyzers + StyleCop |
| SonarQube | Kalite kapısı **OK** — 0 açık, güvenlik A, sürdürülebilirlik A |
| Lighthouse | Performans 96, Erişilebilirlik 100, Best Practices 100 |
| Güvenlik taraması | Bağımlılıklar: `dotnet list package --vulnerable` + `npm audit`; imaj: Trivy |

## Bilinen eksikler

Dürüst olmak gerekirse tamamlanmayanlar:

- **Etkinlik listesi giriş istiyor.** Anonim gezinme için ayrı bir üst
  çubuk gerekiyordu, yapmadım. Bedeli: etkinlik sayfaları arama
  motoruna kapalı.
- **Frontend test kapsamı %13.** Kritik akışlar (giriş, filtre, koltuk
  haritası, korumalı rota) test edildi; sayfa bileşenlerinin çoğu
  edilmedi.
- **SonarQube CI'da sürekli çalışmıyor.** Yerelde Docker ile
  çalıştırıldı; sürekli çalışması için sunucu veya SonarCloud hesabı
  gerekiyor.
- **Askıya alma sebebi organizatöre gösterilmiyor.** Admin bir
  etkinliği askıya alırken sebep yazmak zorunda ve bu sebep denetim
  kaydına (log 1106) yazılıyor; ama `Event` üzerinde sebep sütunu yok,
  bu yüzden organizatör panelinde yalnızca "askıya alındı" görünüyor.
  Göstermek için migration gerekiyor.
- **Ödeme simülasyon.** PDF gerçek sağlayıcı istemiyor; `IPaymentService`
  arkasında soyutlandı, gerçek entegrasyon tek sınıf değiştirmekle
  gelir.

## Yapay zekâ kullanımı

Bu projede yapay zekâ; kavram araştırma, hata mesajı çözümleme, kod
gözden geçirme ve dokümantasyon taslağı için kullanıldı. Katkının
görünür olması için ilgili commit'lerde `Co-Authored-By` satırı
bırakıldı — projenin kuralları kod kaynağını gizlemeyi yasaklıyor.

Mimari kararlar, hata ayıklama ve iş kuralları kendi çalışmamın
sonucu; kodun tamamını açıklayabilirim. Karşılaştığım hataların ve
fikir değiştirdiğim yerlerin kaydı hem commit mesajlarında hem kod
içindeki açıklamalarda duruyor.
