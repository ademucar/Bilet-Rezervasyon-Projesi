# Sprint 7 — Eş Zamanlılık Analizi

PDF Sprint 7:

> Aşağıdaki yöntemlerden en az biri araştırılıp uygulanmalıdır:
> - Optimistic concurrency
> - PostgreSQL row-level locking
> - Redis distributed lock
> - Unique constraint ile yarış durumu engelleme
>
> **Stajyerler seçtikleri yöntemin avantajlarını ve dezavantajlarını
> yazılı olarak açıklamalıdır.**

Bu doküman o açıklamadır.

---

## Problem

İki kullanıcı **aynı anda** aynı koltuğu satın almaya çalışıyor.

```
t0   Ayşe:   B-12 koltuğunu seçti, "Devam et" dedi
t0   Mehmet: B-12 koltuğunu seçti, "Devam et" dedi
t1   Ayşe'nin isteği:   koltuk müsait mi? -> EVET
t1   Mehmet'in isteği:  koltuk müsait mi? -> EVET   (Ayşe henüz kaydetmedi)
t2   Ayşe'nin isteği:   koltuğu kilitle
t2   Mehmet'in isteği:  koltuğu kilitle
```

Hiçbir önlem alınmazsa **aynı koltuk iki kişiye satılır**. Kapıda kavga
çıkar, biri geri gönderilir, itibar zarar görür.

Bu, "nadiren olur" denip geçilebilecek bir durum değil: popüler bir
konserin satış açılışında saniyede yüzlerce kişi aynı koltukları dener.

---

## Dört Yöntemin Karşılaştırması

### 1. Optimistic Concurrency (iyimser eş zamanlılık)

Her satırda bir sürüm numarası tutulur. Güncelleme yaparken
"okuduğum sürüm hâlâ aynı mı?" diye kontrol edilir.

```sql
UPDATE "EventSeats" SET "Status" = 2
WHERE "Id" = @id AND xmin = @okunanSürüm
```

Araya biri girmişse `xmin` değişmiştir, 0 satır etkilenir, EF Core
`DbUpdateConcurrencyException` fırlatır.

| Avantaj | Dezavantaj |
|---|---|
| Kilit **yok** — okuma işlemleri hiç bloke olmaz | Çakışma **sonradan** anlaşılır; iş boşa gider |
| Ölçeklenir: iki farklı koltuk birbirini hiç beklemez | Yüksek çakışmada çok sayıda başarısız istek |
| PostgreSQL'de `xmin` ile **maliyeti sıfır** (ek sütun/index yok) | İstemci yeniden deneme mantığı yazmalı |
| Kilidi serbest bırakmayı unutma riski yok | Çok adımlı işlemlerde yönetimi zorlaşır |

### 2. PostgreSQL Row-Level Locking (`SELECT ... FOR UPDATE`)

Satır okunurken kilitlenir; transaction bitene kadar başkası
o satırı güncelleyemez.

| Avantaj | Dezavantaj |
|---|---|
| Çakışma **anında** engellenir, boşa iş yok | Kilitler transaction boyunca **tutulur** |
| Anlaması ve doğruluğunu ispatlaması kolay | **Deadlock riski**: iki istek koltukları farklı sırada kilitlerse |
| Ek altyapı gerektirmez | Uzun transaction bağlantı havuzunu tüketir |
| | Yalnızca tek veritabanı içinde çalışır |

**Deadlock örneği:** Ayşe B-1 ve B-2'yi ister, Mehmet B-2 ve B-1'i.
Ayşe B-1'i, Mehmet B-2'yi kilitler; ikisi de diğerini bekler.
*Çözüm: koltukları her zaman aynı sırada (Id'ye göre) kilitlemek.*

### 3. Redis Distributed Lock

Redis'te `SET key value NX PX 10000` ile kilit alınır.

| Avantaj | Dezavantaj |
|---|---|
| Çok **sunucu/servis** arasında çalışır | **Redis çökerse kilitler kaybolur** |
| Çok hızlı (bellek içi) | Ağ gecikmesi eklenir |
| TTL ile otomatik serbest bırakma | Kilit sahibi yavaşlarsa TTL dolar, **iki sahip** olur |
| Veritabanı yükünü azaltır | Doğru uygulaması zordur (Redlock tartışmalıdır) |

**Kritik sorun:** Redis kilidi ile veritabanı yazımı **atomik değildir**.
Kilit alınıp veritabanı yazımı başarısız olursa tutarsızlık doğar.

### 4. Unique Constraint

Veritabanı kısıtı ile aynı kaydın iki kez oluşması engellenir.

| Avantaj | Dezavantaj |
|---|---|
| **Mutlak garanti** — atlatılamaz | Yalnızca *ekleme* için çalışır, *güncelleme* için değil |
| Uygulama kodundan bağımsız | Hata mesajı ham; kullanıcıya çevrilmeli |
| Sıfır ek maliyet | Tek başına yeterli değil |

---

## Seçimimiz: Katmanlı Savunma

Tek bir yöntem seçmedik. **Üç katman** kullanıyoruz, çünkü her biri
farklı bir hata sınıfını yakalıyor.

```
┌─ 1. Uygulama kontrolü (EventSeat.Lock)
│    Amaç: kullanıcıya anlamlı hata mesajı
│    Yakaladığı: yaygın durum (koltuk zaten dolu)
│
├─ 2. Optimistic concurrency (xmin)
│    Amaç: eş zamanlı iki isteği ayırmak
│    Yakaladığı: yarış durumu
│
└─ 3. Unique index (EventSessionId, SeatId)
     Amaç: son savunma hattı
     Yakaladığı: kodda gözden kaçan her şey
```

### Neden bu üçü?

**1. katman olmadan:** Kullanıcı ham bir veritabanı hatası görür.
"duplicate key value violates unique constraint" mesajı hem anlaşılmaz
hem de şema bilgisi sızdırır.

**2. katman olmadan:** İki eş zamanlı istek de "müsait" görür, ikisi de
yazar. **Son yazan kazanır** ve ilk kullanıcının kilidi sessizce silinir.
Bu en tehlikelisidir: hata *yoktur*, veri *yanlıştır*.

**3. katman olmadan:** Kodda bir hata (yeni bir endpoint, bir migration
scripti, bir toplu işlem) kuralı atlarsa hiçbir şey engellemez.

### Neden `SELECT ... FOR UPDATE` kullanmadık?

Kullanabilirdik ve doğru çalışırdı. Optimistic'i tercih etme sebebimiz:

1. **Koltuk seçimi çoğunlukla çakışmaz.** 1000 koltuklu bir salonda iki
   kullanıcının tam olarak aynı koltuğu seçme olasılığı düşüktür.
   İyimser yaklaşım, çakışmanın *nadir* olduğu durumlar için tasarlanmıştır.

2. **Kilit tutmuyoruz.** Koltuk haritası sorgusu (saniyede yüzlerce kez
   çalışacak) hiçbir kilide takılmıyor.

3. **Deadlock riski yok.** Kilit almadığımız için kilitlenme de olamaz.

4. **Maliyeti sıfır.** `xmin` PostgreSQL'de zaten var.

### Redis'i nerede kullanıyoruz?

Kilitleme için **kullanmıyoruz** — doğruluk kaynağı veritabanı.

Redis'i Sprint 11'de şunlar için kullanacağız:
- Koltuk uygunluk **cache**'i (okuma hızlandırma)
- Rate limiting
- SignalR backplane

**Kural:** Redis çökerse sistem **yavaşlar**, ama **yanlış çalışmaz**.

---

## Süre Aşımı: Neden Hem Veritabanı Hem Redis Değil?

`EventSeat.LockedUntil` alanı **veritabanında**. Redis TTL'ine
güvenmiyoruz çünkü:

- Redis restart edilirse tüm kilitler kaybolur → aynı koltuk iki kez satılır
- Redis bir *cache*'tir; kalıcılık garantisi vermez

Süresi dolan kilitleri background job temizler (dakikada bir).
Ama `IsAvailableAt()` metodu, job gelmeden önce de "kilidi dolmuş
koltuk aslında müsait" diyor — böylece o bir dakikalık pencerede
koltuk gereksiz yere dolu görünmüyor.

---

## Idempotency

PDF Sprint 15: rezervasyon oluşturma idempotent olmalı.

Kullanıcı butona iki kez basarsa iki rezervasyon oluşmamalı.
Çözüm: `Reservations.IdempotencyKey` üzerinde **partial unique index**.

```sql
CREATE UNIQUE INDEX ix_reservations_idempotency_key
    ON "Reservations" ("IdempotencyKey")
    WHERE "IdempotencyKey" IS NOT NULL;
```

Ayrı bir `IdempotencyKeys` tablosu **kullanmadık**: o zaman "anahtarı
kaydet" ve "rezervasyon oluştur" iki ayrı işlem olur ve aralarında
yine yarış durumu doğardı. Aynı satırda tutmak, unique constraint'in
**atomik** garantisinden faydalanmamızı sağlıyor.

---

## Doğrulama

Bu tasarımın gerçekten çalıştığı Sprint 17'de Testcontainers ile
kanıtlanacak: gerçek PostgreSQL'e karşı N eş zamanlı istek gönderilip
**tam olarak birinin** başarılı olduğu doğrulanacak.

Bunu birim testle kanıtlamak **mümkün değildir** — mock'lanmış bir
DbContext ne `xmin` davranışını ne de unique index'i taklit eder.
