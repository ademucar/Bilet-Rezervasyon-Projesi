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
| Biletini iptal edebilir | ⚠️ Rezervasyon iptali var, **bilet** iptali yok |
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

## Admin — 2/7 ❌

| PDF yetkisi | Backend | Ekran |
|---|---|---|
| **Tüm kullanıcıları yönetebilir** | ❌ uç yok | ❌ |
| **Organizatör başvurularını onaylayabilir** | ✅ 3 uç hazır | ❌ **yok** |
| **Tüm etkinlikleri görüntüleyebilir** | ⚠️ genel liste var, durum filtresi yok | ❌ |
| **Uygunsuz etkinlikleri pasif hâle getirebilir** | ✅ `cancel` | ❌ |
| Kategori, şehir ve salon yönetimi | salon ✅ / kategori-şehir ❌ | salon ✅ |
| Sistem raporlarını görüntüleyebilir | ✅ | ✅ `/panel` → Yönetici |
| **Audit log kayıtlarını inceleyebilir** | ❌ uç yok | ❌ |

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

**Başlangıçta 24 rol yetkisinin 12'sinde ekran yoktu. Organizatörün
dördü tamamlandı; 8 madde kaldı.**

Boşluklar ikiye ayrılıyor:

**Yalnızca arayüz eksik** (backend hazır, iş sadece ekran):
- Etkinlik oluşturma / düzenleme / oturum ekleme / görsel yükleme /
  önizleme / yayına alma / iptal
- Bilet türü ve fiyat tanımlama
- Organizatör başvuru onayı
- Admin etkinlik listesi ve pasifleştirme

**Hem uç hem ekran eksik** (tam yığın iş):
- Bilet iptali
- Kategori ve şehir yönetimi
- Audit log görüntüleme
- Kullanıcı yönetimi

## Neden ilk denetimde kaçtı

Uçları saydım, ekranları saymadım. `POST /api/v1/events` OpenAPI'de
duruyordu ve ben tik attım — ama o uca basacak hiçbir düğme yok.
Organizatör bu sistemde arayüzden **etkinlik oluşturamıyor**.

Aynı şeyi Docker'da ve CI'da da yaşadım: dosya yazılmış ama hiç
çalıştırılmamış. Burada da uç yazılmış ama hiç çağrılmamış. Ders
aynı: **var olmak ile çalışmak farklı şeyler.**
