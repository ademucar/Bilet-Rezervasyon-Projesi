# Yayına alma — tek sunucu

Projenin tamamı (API, veritabanı, Redis, arayüz) tek bir Linux
sunucusunda Docker Compose ile çalışır. Bu belge sıfırdan çalışır
hâle getirmenin adımlarını içeriyor.

---

## Neden tek sunucu?

Vercel/Netlify gibi servisler yalnızca statik siteyi ve küçük
serverless fonksiyonları barındırır. Bu projede kalıcı bağlantı
gerektiren üç şey var ve üçü de oraya sığmaz:

| Bileşen | Neden serverless'a uymuyor |
|---|---|
| PostgreSQL | Kalıcı disk ve sürekli açık bağlantı ister |
| Redis | Aynı gerekçe; ayrıca durum tutuyor |
| Hangfire | Arka planda **sürekli çalışan** bir işçi süreç |
| SignalR | WebSocket; edge proxy'ler upgrade isteğini geçirmez |

Arayüzü Vercel'e, API'yi başka bir yere koymak da mümkündü ama iki
ayrı platform, iki ayrı dağıtım hattı ve aralarında CORS ayarı
demek. Compose zaten çalışıyor; tek sunucu hem daha az parça hem
de PDF'in *"tüm proje Docker Compose ile ayağa kaldırılabilmelidir"*
maddesiyle birebir örtüşüyor.

---

## Gereken

- **Sunucu:** 2 vCPU / 4 GB RAM yeterli (Hetzner CX22 ~4 €/ay,
  DigitalOcean 4 GB ~24 $/ay). 2 GB'da da çalışır ama .NET
  derlemesi sunucuda yapılırsa takla atar — aşağıda buna değindim.
- **Alan adı:** A kaydı sunucunun IP'sine bakmalı. Caddy sertifikayı
  buna göre alıyor; kayıt yayılmadan denemek Let's Encrypt kotasını
  boşa harcar.
- **SMTP hesabı:** Brevo / Resend / Mailgun. Ücretsiz katmanlar
  günde birkaç yüz e-posta veriyor, bu proje için fazlasıyla yeter.

---

## Adımlar

### 1. Sunucuyu hazırla

```bash
sudo apt update && sudo apt upgrade -y
curl -fsSL https://get.docker.com | sudo sh
sudo usermod -aG docker $USER   # çıkıp tekrar girmek gerekiyor
```

Güvenlik duvarı — **yalnızca üç port**:

```bash
sudo ufw allow OpenSSH && sudo ufw allow 80 && sudo ufw allow 443 && sudo ufw enable
```

PostgreSQL (5432), Redis (6379) **ve API (8080)** dışarıya
**açılmıyor**; `docker-compose.prod.yml` bunlara zaten `ports`
vermiyor.

> API'nin kapalı kalması yalnızca temizlik değil, **güvenlik
> koşulu**: `Program.cs` içinde `KnownProxies.Clear()` var, yani
> API gelen `X-Forwarded-For` başlığına koşulsuz güveniyor. Bu
> ancak API'ye yalnızca kendi vekilimiz ulaşabildiğinde güvenli.
> API doğrudan internete açılırsa saldırgan başlığı uydurup hız
> sınırını (Sprint 15) atlatabilir.

Veritabanına bakmak gerekirse SSH tüneli kullanın:

```bash
ssh -L 5433:localhost:5433 kullanici@sunucu
```

### 2. Projeyi indir

```bash
git clone <depo-adresi> biletim && cd biletim/docker
```

### 3. Ayarları gir

```bash
cp .env.production.example .env.production
nano .env.production
chmod 600 .env.production
```

Parolaları düşünerek değil **üreterek** yazın:

```bash
openssl rand -base64 32
```

`JWT_SECRET` en kritik değer: sızarsa saldırgan istediği kullanıcı ve
rol için geçerli token üretir, parola bilmesine gerek kalmaz.

### 4. Başlat

```bash
docker compose -f docker-compose.prod.yml --env-file .env.production up -d --build
```

İlk çalıştırmada:

- .NET imajı derlenir (2–5 dakika)
- **EF Core migration'ları otomatik uygulanır** — API açılırken
  bekleyen migration varsa çalıştırıyor, yoksa dokunmuyor
- **Referans verisi yüklenir** — 20 il ve etkinlik kategorileri.
  Seeder idempotent: tablo doluysa hiçbir şey yapmıyor
- Caddy sertifikayı alır (birkaç saniye)

> Bu iki adım da başta **yoktu**. Yayın yığını ilk kez temiz bir
> veritabanıyla ayağa kaldırılınca çıktılar: şema hiç
> oluşturulmuyordu (`42P01: relation "Cities" does not exist`) ve
> şema elle oluşturulsa bile şehir/kategori tabloları boş kaldığı
> için mekân ve etkinlik açılamıyordu.

```bash
docker compose -f docker-compose.prod.yml ps
```

Beş servisin de `healthy` olması gerekiyor.

### 5. Yönetici hesabı

Kayıt ekranından kendinize bir hesap açın, sonra rolü verin:

```bash
docker exec -it ticketing-postgres psql -U ticketing -d ticketing -c "
INSERT INTO \"UserRoles\" (\"UserId\",\"RoleId\",\"AssignedAt\")
SELECT u.\"Id\", '33333333-3333-3333-3333-333333333333', now()
FROM \"Users\" u WHERE u.\"Email\"='sizin@adresiniz.com';"
```

Organizatör paneli ayrıca bir `OrganizerProfiles` kaydı istiyor —
rol tek başına yetmiyor, panel bunu açıkça söylüyor.

---

## Bakım

### Yedek

```bash
docker exec ticketing-postgres pg_dump -U ticketing ticketing \
  | gzip > backups/$(date +%F).sql.gz
```

Günlük almak için crontab'a:

```
0 3 * * * cd /home/kullanici/biletim/docker && docker exec ticketing-postgres pg_dump -U ticketing ticketing | gzip > backups/$(date +\%F).sql.gz
```

> Yedeği **sunucunun dışına** da kopyalayın. Diski bozulan bir
> sunucuda, aynı diskteki yedeğin hiçbir kıymeti yok.

### Güncelleme

```bash
git pull
docker compose -f docker-compose.prod.yml --env-file .env.production up -d --build
```

Compose yalnızca değişen servisleri yeniden oluşturur.

### Loglar

```bash
docker compose -f docker-compose.prod.yml logs -f api
docker compose -f docker-compose.prod.yml logs -f caddy
```

API logları Serilog ile JSON; `jq` ile süzülebilir.

---

## Sunucunun RAM'i azsa

2 GB'lık bir makinede `dotnet publish` bellek yetersizliğinden
öldürülebilir. İki çıkış yolu var:

1. **İmajı kendi bilgisayarında derle**, registry'ye gönder, sunucuda
   yalnızca çek. `build:` yerine `image:` yazman yeterli.
2. Geçici swap aç:

```bash
sudo fallocate -l 2G /swapfile && sudo chmod 600 /swapfile
sudo mkswap /swapfile && sudo swapon /swapfile
```

---

## Yayına almadan önce son kontrol

- [ ] `.env.production` içindeki **tüm** `DEGISTIR` değerleri değişti
- [ ] `chmod 600 .env.production`
- [ ] Alan adının A kaydı yayıldı (`dig +short alanadi.com`)
- [ ] `ufw status` → yalnızca 22, 80, 443
- [ ] SMTP gerçek bir sağlayıcıya bakıyor, mailpit **değil**
- [ ] Kayıt olup onay e-postasının geldiği görüldü
- [ ] Yedek cron'u kuruldu ve **bir kez elle çalıştırılıp** dosyanın
      oluştuğu doğrulandı
