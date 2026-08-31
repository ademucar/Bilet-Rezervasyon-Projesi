# PDF uyum denetimi — rol yetkileri ve ekranlar

> Bu belge Sprint 19 sonrası yapılan **ikinci** denetimin sonucudur.
>
> Birinci denetimi uç (endpoint) listesi üzerinden yapmıştım: PDF'in
> saydığı uçları OpenAPI dokümanıyla karşılaştırdım, hepsi vardı ve
> "uyumlu" dedim. **Yöntem yanlıştı.** PDF rol yetkilerini sayıyor,
> uçları değil. Bir yetki için uç var ama o uca basacak bir ekran
> yoksa, kullanıcı o işi yapamıyor demektir.
>
> Bu denetim her yetkiyi *hangi ekranda karşılanıyor* diye sordu.

## Kullanıcı — 10/10 ✅

| PDF yetkisi | Ekran |
|---|---|
| Etkinlikleri listeleyebilir | `/etkinlikler` |
| Etkinlik detaylarını görüntüleyebilir | `/etkinlikler/:id` |
| Koltuk seçebilir | `/oturumlar/:id/koltuklar` |
| Rezervasyon oluşturabilir | Koltuk seçim ekranı |
| Ödeme simülasyonu gerçekleştirebilir | `/rezervasyonlar/:id` |
| Satın aldığı biletleri görüntüleyebilir | `/biletlerim` |
| Biletini iptal edebilir | `/biletlerim` → İptal et (iade oranı önizlemeli) |
| Favori etkinliklerini yönetebilir | `/favorilerim` + kalp düğmesi |
| Etkinliklere yorum ve puan verebilir | Etkinlik detayında |
| Bildirimlerini görüntüleyebilir | Üst çubuktaki zil |

## Organizatör — 7/7 ✅

| PDF yetkisi | Backend | Ekran |
|---|---|---|
| Etkinlik oluşturabilir | ✅ `POST /events` | ✅ `/panel/etkinlikler/yeni` |
| Kendi etkinliklerini güncelleyebilir | ✅ `PUT /events/{id}` | ✅ yönetim ekranı |
| Salon ve oturma planı seçebilir | ✅ `POST /events/{id}/sessions` | ✅ yönetim ekranı |
| Bilet kategorileri ve fiyatları tanımlayabilir | ✅ `TicketTypesController` | ✅ yönetim ekranı |
| Etkinlik satış durumunu görüntüleyebilir | ✅ | ✅ `/panel` |
| Etkinlik raporlarını inceleyebilir | ✅ | ✅ `/panel` |
| Etkinliği yayına alabilir veya iptal edebilir | ✅ `submit` / `publish` / `cancel` | ✅ yönetim ekranı |

## Admin — 7/7 ✅

| PDF yetkisi | Backend | Ekran |
|---|---|---|
| Tüm kullanıcıları yönetebilir | ✅ liste + aktif/pasif + rol | ✅ `/admin/kullanicilar` |
| Organizatör başvurularını onaylayabilir | ✅ 3 uç | ✅ `/admin/basvurular` |
| Tüm etkinlikleri görüntüleyebilir | ✅ `GET /events` admine taslakları da döner | ✅ `/admin/etkinlikler` |
| Uygunsuz etkinlikleri pasif hâle getirebilir | ✅ `suspend` / `reinstate` (yeni) | ✅ `/admin/etkinlikler` |
| Kategori, şehir ve salon yönetimi | ✅ üçü de | ✅ `/admin/tanimlar` + `/admin/mekanlar` |
| Sistem raporlarını görüntüleyebilir | ✅ | ✅ `/panel` → Yönetici |
| Audit log kayıtlarını inceleyebilir | ✅ `GET /admin/audit-logs` | ✅ `/admin/denetim` |

## Sprint 5'in "Frontend Görevleri" listesi

PDF sayfa 13'te on madde sayıyor. **Onu da tamamlandı:**

| Madde | Durum |
|---|---|
| Etkinlik oluşturma formu | ✅ |
| Etkinlik düzenleme | ✅ |
| Etkinlik listesi | ✅ |
| Etkinlik detay sayfası | ✅ |
| Oturum ekleme | ✅ |
| Görsel yükleme | ✅ (`PUT /events/{id}/poster` eklendi) |
| Önizleme | ✅ (gerçek detay sayfasına bağlantı) |
| Yayına alma | ✅ (admin) |
| İptal etme | ✅ (sebep zorunlu) |
| Form doğrulama | ✅ (Zod + FluentValidation) |

Diğer sprintlerin frontend listeleri (3, 4, 7, 10) **tam**.

## Özet

**Başlangıçta 24 rol yetkisinin 12'sinde ekran yoktu. Üç rolün de
tüm yetkileri tamamlandı: 24/24.**

Boşluklar ikiye ayrılıyor:

**Yalnızca arayüz eksikti** (backend hazırdı, iş sadece ekrandı) —
**hepsi tamamlandı:**
- Etkinlik oluşturma / düzenleme / oturum ekleme / görsel yükleme /
  önizleme / yayına alma / iptal
- Bilet türü ve fiyat tanımlama
- Organizatör başvuru onayı
- Admin etkinlik listesi

Pasifleştirme bu listede değildi: `Event.Suspend()` ve `Reinstate()`
domain'de duruyordu ama onları çağıran komut da, uç da, ekran da
yoktu. Üçünü birden yazdım.

**Hem uç hem ekran eksikti** (tam yığın iş) — **hepsi tamamlandı:**
- ~~Bilet iptali~~ ✅ yapıldı: `POST /users/me/tickets/{id}/cancel` +
  `cancellation-preview`. Bu iş sırasında `CancellationPolicy`nin
  (PDF Sprint 1, soru 10) hiçbir yerden çağrılmadığı ortaya çıktı —
  var olan iade ucu tutarı çağırandan alıyordu, yani iade politikası
  19 sprint boyunca hiç uygulanmamış.
- ~~Kategori ve şehir yönetimi~~ ✅ yapıldı: `/admin/tanimlar`.
  Kullanımdaki kategori/şehir silinemiyor; ikisinin de 24 saatlik
  Redis önbelleği her değişiklikte temizleniyor.
- ~~Audit log görüntüleme~~ ✅ `/admin/denetim`. Bu işi yaparken
  `AuditLogs` tablosunun TAMAMEN BOŞ olduğu ortaya çıktı: tek yazım
  noktası bilet türü fiyat değişikliğiydi ve o da hiç tetiklenmemişti.
  Ekranı yapmadan önce denetim kaydı yazımını kritik işlemlere
  yaydım (hesap aç/kapa, rol ver/al, etkinlik yayınla/askıya al/geri
  al, başvuru onayla/reddet).
- ~~Kullanıcı yönetimi~~ ✅ `/admin/kullanicilar`. Silme YOK,
  pasifleştirme var: hesabı silmek geçmiş rezervasyon, bilet ve
  ödemeleri sahipsiz bırakırdı.

## Neden ilk denetimde kaçtı

Uçları saydım, ekranları saymadım. `POST /api/v1/events` OpenAPI'de
duruyordu ve ben tik attım — ama o uca basacak hiçbir düğme yok.
Organizatör bu sistemde arayüzden **etkinlik oluşturamıyor**.

Aynı şeyi Docker'da ve CI'da da yaşadım: dosya yazılmış ama hiç
çalıştırılmamış. Burada da uç yazılmış ama hiç çağrılmamış. Ders
aynı: **var olmak ile çalışmak farklı şeyler.**
