# Git ve Branch Stratejisi

PDF Sprint 19, "Branch Koruması" bölümünün karşılığıdır:

> - Main branch doğrudan değiştirilememelidir.
> - Pull Request zorunlu olmalıdır.
> - Testler başarısızsa merge yapılamamalıdır.
> - En az bir code review alınmalıdır.

---

## 1. Branch Yapısı

| Branch | Amaç | Doğrudan commit? |
|---|---|---|
| `main` | Her zaman çalışır durumda olan kod | ❌ Hayır, sadece PR ile |
| `feature/sprint-N-konu` | Bir sprintin bir parçası | ✅ Evet |
| `fix/kisa-aciklama` | Hata düzeltmesi | ✅ Evet |
| `docs/kisa-aciklama` | Sadece doküman değişikliği | ✅ Evet |

**Örnekler:**

```
feature/sprint-2-domain-entities
feature/sprint-3-jwt-authentication
feature/sprint-7-seat-locking
fix/reservation-expiry-timezone
docs/sprint-7-concurrency-karsilastirmasi
```

**Not:** İlk üç commit (`docs: Sprint 1...`, `feat(backend): Sprint 2...`,
`feat(domain): ...`) doğrudan `main`'e atıldı. Bu, proje iskeleti kurulurken
yapılan bir eksikliktir ve geriye dönük düzeltilmemiştir — PR geçmişini
sonradan uydurmak, PDF'in yasakladığı "sahte commit geçmişi oluşturmak"
maddesine girer. Bu tarihten sonraki tüm değişiklikler PR ile gelmektedir.

---

## 2. Commit Mesajı Kuralı (Conventional Commits)

```
<tip>(<kapsam>): <ne yapıldığı, küçük harfle, emir kipinde>

<gerekçe: neden yapıldı — isteğe bağlı ama önerilir>
```

**Tipler:**

| Tip | Ne zaman |
|---|---|
| `feat` | Yeni özellik |
| `fix` | Hata düzeltmesi |
| `refactor` | Davranış değişmeden kod düzenleme |
| `test` | Sadece test ekleme/düzeltme |
| `docs` | Sadece doküman |
| `chore` | Paket güncelleme, yapılandırma |
| `perf` | Performans iyileştirmesi |

**Kapsam** genelde katman veya modüldür: `domain`, `application`, `webapi`,
`persistence`, `frontend`, `docker`, `ci`.

**İyi örnek:**

```
feat(domain): EventSeat entity ve koltuk kilitleme kurallari

Koltugun oturum bazindaki durumunu EventSeat tasiyor. Seat entity'si
fiziksel koltugu temsil ettigi icin oturum bazli durum orada tutulamaz;
ayni salonda iki farkli konser oldugunda durumlar birbirine karisirdi.
```

**Kötü örnek:**

```
update
fix bug
calisiyor artik
```

---

## 3. Çalışma Akışı

```bash
# 1. main'i guncelle
git checkout main
git pull origin main

# 2. Yeni branch ac
git checkout -b feature/sprint-3-jwt-authentication

# 3. Calis, kucuk parcalar halinde commit et
git add <dosyalar>
git commit -m "feat(application): login command ve handler"

# 4. Testleri calistir -- PR acmadan ONCE
cd backend && dotnet test

# 5. Push et
git push -u origin feature/sprint-3-jwt-authentication

# 6. GitHub'da Pull Request ac
```

---

## 4. Commit Sıklığı

PDF'in yasakladığı: *"Tüm projeyi tek commit ile teslim etmek"*

**Kural:** Bir commit, **tek bir anlamlı adımı** kapsamalı ve o adımdan sonra
proje **derlenmeye devam etmeli**.

| Kötü | İyi |
|---|---|
| Tüm Sprint 3 tek commit | `feat(domain): User ve RefreshToken entityleri` |
| Yarım kalmış, derlenmeyen kod | `feat(application): JWT token uretici servis` |
| 40 dosyalık dev commit | `test(application): login handler testleri` |
| | `feat(webapi): auth controller endpointleri` |

**Pratik ölçüt:** Commit mesajını yazarken "ve" kelimesi kullanmak zorunda
kalıyorsan, muhtemelen iki ayrı commit olmalıydı.

---

## 5. GitHub'da Yapılması Gerekenler

Bunlar arayüzden yapılır, kodla değil. `Settings → Branches → Add rule`:

- Branch name pattern: `main`
- ☑ Require a pull request before merging
- ☑ Require approvals: **1**
- ☑ Require status checks to pass before merging
  - Sprint 19'da CI pipeline kurulunca `build` ve `test` job'ları buraya eklenecek
- ☑ Do not allow bypassing the above settings

**Tek kişilik projede kendi PR'ını onaylayamazsın.** İki seçenek var:

1. "Require approvals" sayısını **0** bırak, ama PR açma zorunluluğunu koru.
   Böylece her değişiklik yine PR olarak görünür ve geçmiş denetlenebilir olur.
2. Ekip arkadaşın varsa karşılıklı review yapın.

Hangisini seçtiğini bu dokümana not et — değerlendiren kişi neden 0 approval
olduğunu merak edecektir.

---

## 6. Pull Request Kontrol Listesi

PR açmadan önce:

- [ ] `dotnet build` — 0 uyarı, 0 hata
- [ ] `dotnet test` — hepsi yeşil
- [ ] Yeni yazılan iş kuralı için test eklendi
- [ ] Yeni test **bir kez bilerek kırılıp** çalıştığı doğrulandı
- [ ] Hassas bilgi (şifre, token, connection string) commit'lenmedi
- [ ] Commit mesajları Conventional Commits kuralına uygun
- [ ] PR açıklamasında **ne** ve **neden** yazıyor
