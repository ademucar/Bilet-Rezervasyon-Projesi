# Lighthouse analizi

PDF Sprint 19 / Frontend Teknik Gereksinimleri:
*"Kritik sayfalar Lighthouse ile analiz edilmelidir."*

Ölçüm, **Docker'da çalışan üretim derlemesine** karşı yapıldı
(`http://localhost:5173`, nginx). Vite dev sunucusunda ölçmedim:
orada kaynak haritaları, HMR soketi ve sıkıştırılmamış modüller var;
çıkan puan kullanıcının gördüğü sayfayla ilgisiz olurdu.

## Sonuçlar

| Sayfa | Performans | Erişilebilirlik | Best Practices | SEO |
|---|---|---|---|---|
| `/giris` | 96 | 100 | 100 | 63 \* |
| `/kayit` | 96 | 100 | 100 | 63 \* |
| `/etkinlikler` | 96 | 100 | 100 | 100 |

\* Açıklaması aşağıda — kasıtlı.

## Bulunanlar ve yapılanlar

### 1. Sayfa başlığı `frontend` idi

Vite şablonundan gelen başlık hiç değiştirilmemişti. Sekme adı, yer
imi adı ve arama sonucundaki başlık hep buydu — yani kullanıcının
uygulamayı tanıdığı ilk metin. Bunu Lighthouse denetimine kadar fark
etmemiştim; her ekran görüntüsünde gözümün önündeydi.

### 2. `meta description` yoktu

Arama motoru açıklama bulamazsa sayfadan rastgele bir parça kesip
gösterir. Giriş ekranında bu "E-posta Şifre Giriş yap" gibi bir şey
olurdu.

### 3. `robots.txt is not valid — 24 errors found`

İlginç olan bu. Dosya **yok değildi, yanlış dosya vardı**: nginx'te
SPA için `try_files` ile index.html'e düşme var (derin bağlantılar
çalışsın diye) ve bu, `/robots.txt` isteğine de index.html
döndürüyordu. Arama motoru gelen HTML'i robots.txt sanıp satır satır
ayrıştırıyor ve her satırda hata veriyordu.

`public/robots.txt` eklenince nginx önce onu buluyor, fallback
devreye girmiyor.

### 4. SEO 63 — bu bir hata değil

`/giris` ve `/kayit` sayfalarında tek başarısız denetim
**"Page is blocked from indexing"**. Sebebi robots.txt'te bu iki
yolu `Disallow` etmem. Giriş ekranının Google'da çıkmasını istemiyoruz;
yani Lighthouse'un "hatası" bizim kasıtlı kararımız.

Puanı 100 yapmak için `Disallow` satırlarını silebilirdim ama o zaman
rakam iyileşir, site kötüleşirdi. **Puan hedef değil, ölçü.**

`/etkinlikler` (Disallow'da olmayan bir yol) 100 alıyor — yani SEO
altyapısının kendisi sağlam.

## Kapatılmayan bulgular

**First Contentful Paint 1.9 sn (0.86 puan).** Ana paket 218 kB
(gzip 68 kB) ve panel sayfası Recharts yüzünden 402 kB. Route bazlı
kod bölme zaten var, yani panel paketi ilk açılışta inmiyor.
Daha fazlası için Recharts'ı daha hafif bir grafik kütüphanesiyle
değiştirmek gerekir; bu ayrı bir iş ve şu an 96 performans puanını
düşüren bir sorun değil.

**Network dependency tree (0 puan).** Zincirin başında Google Fonts
var: `index.css` → `fonts.googleapis.com` → font dosyaları. Fontları
kendi sunucumuzdan servis etmek bu zinciri kısaltır. Şimdilik
bilinçli olarak bırakıyorum; ölçüyü not ediyorum.

## Tekrar çalıştırmak için

Uygulama Docker'da ayaktayken:

```bash
CHROME_PATH="C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe" npx lighthouse http://localhost:5173/etkinlikler --output=html --output-path=docs/lighthouse/etkinlikler --chrome-flags="--headless=new"
```

> Ham rapor dosyaları (`docs/lighthouse/*.report.*`) gitignore'da:
> her çalıştırmada yeniden üretiliyorlar ve tanesi ~480 kB.
