# Sprint 1 — İş Analizi

Bu doküman, PDF'te Sprint 1 altında sorulan 16 analiz sorusunun cevaplarını içerir.
Buradaki her karar, ileriki sprintlerde yazacağımız kodun gerekçesidir.
Bir kod satırı yazmadan önce bu kararların netleşmesi gerekir; çünkü Sprint 7'deki
eş zamanlılık problemi doğrudan buradaki modellemeye bağlıdır.

---

## 1. Kullanıcı hangi işlemleri yapabilir?

| İşlem | Kimlik doğrulama gerekir mi? |
|---|---|
| Yayındaki etkinlikleri listeleme / arama / filtreleme | Hayır (anonim) |
| Etkinlik detayını görüntüleme | Hayır (anonim) |
| Koltuk uygunluk haritasını görüntüleme | Hayır (anonim) |
| Koltuk seçip rezervasyon oluşturma | **Evet** |
| Ödeme başlatma / tamamlama | **Evet** (+ rezervasyon sahibi olmalı) |
| Kendi biletlerini görüntüleme | **Evet** |
| Bilet iptal / iade talebi | **Evet** (+ bilet sahibi olmalı) |
| Favori ekleme / çıkarma / listeleme | **Evet** |
| Yorum ve puan verme | **Evet** (+ etkinliğe geçerli bilet almış + etkinlik tamamlanmış) |
| Bildirimlerini görüntüleme / okundu işaretleme | **Evet** |
| Organizatör olmak için başvuru gönderme | **Evet** |

**Karar:** Listeleme ve detay endpointleri anonim erişime açık olacak. Bu bir bilet
satış sitesi; kullanıcı üye olmadan önce ne satın alacağını görebilmeli. Ama koltuk
haritasında "kim kilitledi" bilgisi anonime dönmeyecek — sadece durum (boş/dolu/kilitli)
dönecek. Aksi halde kullanıcı gizliliği ihlal edilir.

---

## 2. Organizatör hangi işlemleri yapabilir?

- Etkinlik oluşturma (Draft durumunda başlar)
- **Sadece kendi** etkinliklerini güncelleme/silme → `EventOwner` policy'si ile korunur
- Salon ve oturma planı seçme (salonu kendisi oluşturmaz, Admin'in tanımladıklarından seçer)
- Bilet türü ve fiyat tanımlama, bölüm-bilet türü eşleştirmesi
- Etkinliği yayına alma (`publish`) ve iptal etme (`cancel`)
- Kendi etkinliklerinin satış durumunu ve raporlarını görüntüleme

**Karar:** Organizatör salon/mekân oluşturamaz. Salonlar fiziksel gerçekliktir ve
Admin tarafından yönetilir. Organizatör sadece var olan salonu belirli bir tarih
aralığı için rezerve eder. Bu, "aynı salon aynı saatte iki etkinliğe atanamaz"
kuralını uygulanabilir kılar.

---

## 3. Admin hangi işlemleri yapabilir?

- Tüm kullanıcıları listeleme, rol atama, hesap aktif/pasif etme
- Organizatör başvurularını onaylama / reddetme
- Tüm etkinlikleri görme (Draft dahil), uygunsuz etkinliği `Suspended` yapma
- Kategori, şehir, mekân, salon, oturma planı yönetimi (CRUD)
- Sistem geneli raporlar
- Audit log inceleme
- Uygunsuz yorum kaldırma

---

## 4. Bir etkinliğin yaşam döngüsü nasıl ilerler?

```
Draft ──submit──► PendingApproval ──approve──► Published ──satış tarihi──► SalesOpen
  │                     │                          │                            │
  │                     │ reject                   │                            │ satış bitişi
  │                     ▼                          │                            ▼
  │                   Draft                        │                       SalesClosed
  │                                                │                            │
  │                                                │                            │ etkinlik geçti
  └──────────────── cancel ────────────────────────┴──────────┐                 ▼
                                                              ▼            Completed
                                                          Cancelled

Herhangi bir yayın durumundan Admin müdahalesiyle ──► Suspended ──► (Published'a dönebilir)
```

**Geçiş kuralları:**

| Durum | Kim değiştirebilir | İzin verilen sonraki durumlar |
|---|---|---|
| `Draft` | Organizatör | `PendingApproval`, `Cancelled` |
| `PendingApproval` | Admin | `Published`, `Draft` (red) |
| `Published` | Sistem (zamanlı) / Organizatör | `SalesOpen`, `Cancelled`, `Suspended` |
| `SalesOpen` | Sistem (zamanlı) / Organizatör | `SalesClosed`, `Cancelled`, `Suspended` |
| `SalesClosed` | Sistem (zamanlı) | `Completed`, `Cancelled` |
| `Completed` | — | (son durum) |
| `Cancelled` | — | (son durum) |
| `Suspended` | Admin | `Published`, `Cancelled` |

**Karar:** `Published → SalesOpen → SalesClosed` geçişlerini **background job** yapacak.
Kullanıcı isteği sırasında "acaba satış açıldı mı" diye hesaplamak yerine, dakikada bir
çalışan bir job durumu günceller. Böylece koltuk sorgusu basit bir `WHERE Status = SalesOpen`
olur ve index'ten faydalanır.

---

## 5. Koltuk rezervasyonu nasıl yapılır?

Akış (mutlu yol):

1. Kullanıcı oturum (`EventSession`) sayfasını açar → `GET /api/v1/event-sessions/{id}/seat-availability`
2. Frontend koltuk haritasını çizer, SignalR grubuna katılır
3. Kullanıcı N koltuk seçer, "Devam et" der
4. `POST /api/v1/reservations` çağrılır (body: sessionId + seatId listesi, header: `Idempotency-Key`)
5. **Backend, tek transaction içinde:**
   - Etkinlik `SalesOpen` mu? Değilse hata
   - Kullanıcının bu oturum için aktif bilet sayısı + N ≤ maksimum mu?
   - Seçilen koltuklar bu oturumda gerçekten var mı, aktif mi?
   - Koltukları **kilitle** (`SELECT ... FOR UPDATE` / Redis lock — Sprint 7'de karar verilecek)
   - Koltuk durumları hâlâ `Available` mı? Değilse → 409 Conflict
   - `Reservation` (Status = `Locked`, `ExpiresAt = now + 10 dk`) + `ReservationItem` kayıtları
   - `EventSeat.Status = Locked`, `LockedByReservationId` set edilir
   - Toplam tutar **backend'de** hesaplanır
   - Outbox'a `ReservationCreated` mesajı yazılır
6. Transaction commit → SignalR ile `SeatLocked` yayınlanır
7. Frontend 10 dakikalık geri sayımı başlatır

---

## 6. Koltuk kaç dakika kilitli tutulmalıdır?

**10 dakika.** Bu değer yapılandırma üzerinden okunacak
(`Reservation:LockDurationMinutes`), koda gömülmeyecek.

Kullanıcı `POST /reservations/{id}/extend` ile **bir kez** ve **en fazla 5 dakika**
uzatabilir. Sınırsız uzatma olsaydı, bir kullanıcı popüler bir etkinlikte koltukları
süresiz bloke edip satışı sabote edebilirdi.

---

## 7. Aynı koltuğu iki kullanıcı aynı anda seçerse ne olmalıdır?

**Bu projenin en kritik sorusu.**

- Frontend'de koltuk seçmek hiçbir şey kilitlemez — sadece görsel bir işarettir.
- Kilit, `POST /api/v1/reservations` çağrıldığı anda backend'de oluşur.
- **İlk transaction'ı commit eden kazanır.**
- Kaybeden kullanıcı `409 Conflict` + Problem Details alır; hangi koltukların
  kapıldığı `extensions` alanında listelenir.
- Frontend bu hatayı yakalar, koltuk haritasını yeniler, kullanıcıya
  "Seçtiğiniz X koltuğu az önce başkası tarafından alındı" der.
- SignalR ile diğer tüm izleyicilerin ekranı da anında güncellenir.

**Son savunma hattı:** Veritabanı seviyesinde unique index.
`EventSeats` tablosunda `(EventSessionId, SeatId)` unique olacak ve koltuk durumu
bu satır üzerinde `RowVersion` (concurrency token) ile korunacak. Uygulama katmanındaki
tüm kontroller atlansa bile veritabanı iki aktif rezervasyona izin vermeyecek.

Yöntem karşılaştırması ve nihai seçim Sprint 7'de yapılacak (PDF bunu yazılı olarak istiyor).

---

## 8. Ödeme başarısız olduğunda rezervasyon ne olmalıdır?

- `Payment.Status = Failed` yazılır (kayıt silinmez — denetim izi gerekir)
- `Reservation.Status` → `PaymentPending`'den geri `Locked`'a döner
- Kilit süresi **uzatılmaz**; kalan süre neyse odur
- Kullanıcı kalan süre içinde tekrar ödeme deneyebilir
- Süre dolarsa rezervasyon `Expired` olur, koltuklar serbest bırakılır
- Kullanıcıya "Ödeme başarısız" bildirimi + e-posta gider

**Karar (Sprint 8'de GÜNCELLENDİ):**

İlk analizimde koltukları kilitli tutup kullanıcıya ikinci şans vermeyi
önermiştim. Ancak PDF Sprint 8 şu kuralı **açıkça** belirtiyor:

> "Ödeme başarısız olduğunda koltuklar serbest bırakılmalıdır."

Şartname benim tercihimin önüne geçer. Sprint 8'de kuralı PDF'e göre uyguladım
ve iki durumu ayırdım:

| Durum | Davranış | Neden |
|---|---|---|
| Sağlayıcı isteği **başlangıçta** reddetti | Rezervasyon `Locked`'a döner, koltuklar kalır | Ödeme hiç başlamadı; geçici hata olabilir. Kilit 10 dakikada zaten dolar. |
| Ödeme başladı, **başarısız sonuçlandı** | Rezervasyon iptal, koltuklar **serbest** | Kesin sonuç; koltuğu tutmanın anlamı yok. |

Ödün: kart hatası alan kullanıcı koltuklarını kaybediyor.
Kazanç: koltuklar hemen satışa dönüyor, popüler etkinliklerde boş yere bloke kalmıyor.

---

## 9. Etkinlik iptal edildiğinde biletler ne olmalıdır?

Etkinlik `Cancelled` olduğunda, tek bir transaction içinde:

1. Aktif rezervasyonlar (`Locked`, `PaymentPending`) → `Cancelled`
2. Satılmış biletler (`Active`) → `Refunded`
3. İlgili ödemeler için `RefundPayment` çağrılır → `Payment.Status = Refunded`
4. `EventSeat` kayıtları → `Available`
5. Etkilenen her kullanıcı için `Notification` kaydı
6. Outbox'a `EventCancelled` mesajı → e-posta gönderimi

**Karar:** İade **tam tutar** üzerinden yapılır. Kullanıcı hatası değil, organizatör
kararıyla iptal olmuştur; kesinti uygulanamaz.

---

## 10. Bilet iade politikası nasıl uygulanmalıdır?

Kullanıcı kaynaklı iptalde, etkinlik başlangıcına kalan süreye göre:

| Etkinliğe kalan süre | İade oranı |
|---|---|
| 7 günden fazla | %100 |
| 48 saat – 7 gün | %50 |
| 48 saatten az | İade yok |

- Bu oranlar `Event.CancellationPolicy` alanında etkinlik bazında saklanır
  (organizatör kendi politikasını belirleyebilir), üstteki tablo varsayılandır.
- Kullanılmış (`Used`) bilet iade edilemez.
- İade işlemi **idempotent** olmalıdır (PDF Sprint 15 gereği).

---

## 11. Hangi işlemlerde transaction gerekir?

| İşlem | Neden |
|---|---|
| Rezervasyon oluşturma | Reservation + Items + EventSeat güncellemesi + Outbox — hepsi ya olur ya olmaz |
| Ödeme tamamlama | Payment + Reservation + Ticket + QR + EventSeat + Notification + Outbox |
| İade | Payment + Ticket + EventSeat + Notification |
| Etkinlik iptali | Toplu rezervasyon/bilet/koltuk güncellemesi |
| Rezervasyon süre aşımı (job) | Reservation + EventSeat serbest bırakma |
| Koltuk üretimi (generate-seats) | Yüzlerce Seat kaydı tek seferde |

**Karar:** Transaction sınırını `IUnitOfWork` arayüzü ile Application katmanında
yöneteceğiz. Handler'lar `SaveChangesAsync` çağırmayacak; MediatR pipeline'ına
koyacağımız bir `TransactionBehavior` bunu otomatik yapacak.

---

## 12. Hangi işlemlerde cache kullanılmalıdır?

| Veri | TTL | Neden |
|---|---|---|
| Şehir listesi | 24 saat | Neredeyse hiç değişmez |
| Kategori listesi | 24 saat | Neredeyse hiç değişmez |
| Salon oturma planı | 12 saat | Değişmez; ama her koltuk sorgusunda okunur |
| Etkinlik detayı | 5 dakika | Sık okunur, seyrek değişir |
| Popüler etkinlikler | 10 dakika | Ağır bir sorgu, anlık doğruluk gerekmez |

**Cache'lenmeyecekler:** koltuk uygunluğu (saniyede değişir), kullanıcı biletleri,
rezervasyon durumu, ödeme bilgisi, bildirimler.

**Karar:** Veri güncellendiğinde ilgili key silinecek (write-through değil,
invalidation). Ve **Redis çökerse sistem çalışmaya devam edecek** — cache erişimi
try/catch içinde olacak, hata loglanıp doğrudan veritabanına düşülecek.

---

## 13. Hangi işlemler background job ile yapılmalıdır?

| Job | Sıklık |
|---|---|
| Süresi dolan rezervasyonları iptal etme | 1 dakika |
| Outbox mesajlarını işleme | 10 saniye |
| Başarısız outbox mesajlarını yeniden deneme | 5 dakika |
| Etkinlik durum geçişleri (Published→SalesOpen→SalesClosed→Completed) | 1 dakika |
| Yaklaşan etkinlik hatırlatması (24 saat kala) | Saatlik |
| Günlük satış özeti | Her gün 00:05 |
| Rapor üretimi (Excel/CSV/PDF) | Talep üzerine |

---

## 14. Hangi işlemler loglanmalıdır?

Başarılı login, **başarısız login** (brute force tespiti için), etkinlik oluşturma,
etkinlik yayınlama, rezervasyon oluşturma, koltuk kilitleme, **kilit çakışması**,
ödeme başlatma/tamamlama/başarısızlık, iade, background job başlangıç ve sonucu,
cache hatası, SignalR bağlantı hatası, tüm beklenmeyen exception'lar.

**Kesinlikle loglanmayacaklar:** şifre (düz metin veya hash), JWT token, refresh token,
şifre sıfırlama tokenı, kart bilgisi, TC kimlik no. Bunlar için Serilog'a maskeleme
enricher'ı yazacağız.

---

## 15. Hangi senaryolarda kullanıcıya bildirim gönderilmelidir?

| Olay | Uygulama içi | E-posta |
|---|---|---|
| Kayıt (hoş geldiniz) | ✓ | ✓ |
| Şifre sıfırlama | — | ✓ |
| Rezervasyon oluşturuldu | ✓ | ✓ |
| Rezervasyon süresi dolmak üzere (2 dk kala) | ✓ | — |
| Rezervasyon süresi doldu | ✓ | ✓ |
| Ödeme başarılı | ✓ | ✓ |
| Ödeme başarısız | ✓ | ✓ |
| Bilet oluşturuldu (QR ile) | ✓ | ✓ |
| Etkinliğe 24 saat kaldı | ✓ | ✓ |
| Etkinlik iptal edildi | ✓ | ✓ |
| İade tamamlandı | ✓ | ✓ |
| Rapor hazır | ✓ | — |

---

## 16. Hangi alanlara index eklenmelidir?

| Tablo | Index | Tip | Neden |
|---|---|---|---|
| Users | Email | Unique | Login'de her istekte aranır |
| Events | (Status, EventDate) | Composite | Ana listeleme sorgusunun WHERE'i |
| Events | (CityId, CategoryId, EventDate) | Composite | Filtreleme kombinasyonu |
| Events | OrganizerId | Normal | Organizatör paneli |
| Events | Title | GIN (full-text) | Arama endpointi |
| EventSessions | (EventId, StartDate) | Composite | Oturum listesi |
| EventSessions | (HallId, StartDate, EndDate) | Composite | Salon çakışma kontrolü |
| **EventSeats** | **(EventSessionId, SeatId)** | **Unique** | **Çift rezervasyonu DB'de engeller** |
| EventSeats | (EventSessionId, Status) | Composite | Koltuk haritası sorgusu |
| Reservations | (UserId, Status) | Composite | Kullanıcının rezervasyonları |
| Reservations | (Status, ExpiresAt) | Composite | Süre aşımı job'ının sorgusu |
| Tickets | TicketNumber | Unique | PDF gereği benzersiz |
| TicketQrCodes | QrValue | Unique | PDF gereği benzersiz |
| Favorites | (UserId, EventId) | Unique | PDF gereği: bir kez favorileme |
| Reviews | (UserId, EventId) | Unique | PDF gereği: bir kez yorum |
| RefreshTokens | TokenHash | Unique | Token doğrulaması |
| OutboxMessages | (ProcessedAt, CreatedAt) | Composite | Job'ın "işlenmemişleri getir" sorgusu |
| AuditLogs | (EntityName, EntityId, CreatedAt) | Composite | Denetim sorgusu |
| Notifications | (UserId, IsRead) | Composite | Okunmamış sayısı |

---

## Özet: Bu Dokümanın Kod Üzerindeki Etkisi

1. **Unique index'ler yarış durumunun son savunma hattıdır** → Sprint 2'de EF konfigürasyonunda tanımlanacak
2. **Durum geçişleri entity metodlarında olacak** → `evt.Publish()` gibi; dışarıdan `evt.Status = X` şeklinde atama yapılamayacak (encapsulation)
3. **Transaction sınırı MediatR pipeline'ında** → handler'lar transaction bilmeyecek
4. **Cache erişimi hataya dayanıklı** → Redis çökse bile sistem ayakta
5. **Para her zaman decimal + Currency** → Sprint 6'da `Money` value object yazacağız
6. **Tüm tarihler UTC** → `DateTimeOffset` kullanacağız, `DateTime` değil
