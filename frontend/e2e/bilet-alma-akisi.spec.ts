import { expect, test, type Page } from '@playwright/test';

/**
 * ==================================================================
 * PDF Sprint 17 -- END-TO-END TEST
 * ==================================================================
 * PDF'in istediği senaryo, adım adım:
 *
 *   1. Kullanıcı kayıt olur
 *   2. Giriş yapar
 *   3. Etkinlik bulur
 *   4. Koltuk seçer
 *   5. Rezervasyon oluşturur
 *   6. Ödeme yapar
 *   7. Biletini görüntüler
 *
 * ------------------------------------------------------------------
 * ÖN KOŞULLAR
 * ------------------------------------------------------------------
 *   docker compose up -d          (PostgreSQL + Redis)
 *   dotnet run                    (API, :5000)
 *   Satışa açık, koltukları üretilmiş bir etkinlik
 *
 * Bu testi çalıştırmadan önce backend'in ayakta olması gerekiyor;
 * Playwright yalnızca frontend'i başlatıyor.
 * ==================================================================
 */

/**
 * ================================================================
 * HER TEST BENZERSİZ BİR E-POSTA KULLANIYOR
 * ================================================================
 * Sabit bir e-posta yazsaydık test yalnızca BİR KEZ geçerdi:
 * ikinci çalıştırmada "bu e-posta zaten kayıtlı" hatası alırdık.
 *
 * Testten önce veritabanını temizlemek de bir seçenekti ama E2E
 * testi gerçek bir ortamda (staging) da çalıştırılabilmeli — orada
 * veritabanını silmek kabul edilemez.
 *
 * Zaman damgası + rastgele ek: aynı milisaniyede başlayan iki test
 * bile çakışmıyor.
 * ================================================================
 */
function benzersizEposta(): string {
  const damga = Date.now();
  const rastgele = Math.random().toString(36).slice(2, 8);

  return `e2e-${damga}-${rastgele}@ornek.test`;
}

const SIFRE = 'E2eTest1234!';

/**
 * Üst menüdeki bağlantı.
 *
 * ================================================================
 * NEDEN header'A DARALTIYORUM?
 * ================================================================
 * Ana sayfada da /etkinlikler'e giden bir kart bağlantısı var.
 * Sayfa geneline sorunca Playwright iki eşleşme buluyor ve
 * "strict mode violation" ile duruyor.
 *
 * .first() yazıp geçebilirdim ama o zaman DOM sırası değiştiğinde
 * test sessizce farklı bir bağlantıya tıklardı. header'a
 * daraltmak niyeti açık ediyor: gezinme menüsündeki bağlantı.
 * ================================================================
 */
function menuBaglantisi(page: Page, ad: string) {
  return page.locator('header').getByRole('link', { name: ad });
}

/** 1. adım: kayıt ol. Kayıt sonrası otomatik giriş yapılıyor. */
async function kayitOl(page: Page, eposta: string) {
  await page.goto('/kayit');

  // exact: true ŞART -- "Ad" varsayılan olarak "Soyad" ile de
  // eşleşiyor (alt dize eşleşmesi) ve Playwright "strict mode
  // violation: resolved to 2 elements" diyor.
  //
  // Bu katı davranış iyi bir şey: belirsiz bir seçici sessizce
  // YANLIŞ alanı doldurmak yerine testi durduruyor.
  await page.getByLabel('Ad', { exact: true }).fill('E2E');
  await page.getByLabel('Soyad', { exact: true }).fill('Test');
  await page.getByLabel('E-posta').fill(eposta);

  // "Sifre" ve "Sifre tekrar" alanları — exact eşleşme şart,
  // yoksa "Sifre" ikisiyle birden eşleşir ve Playwright
  // "strict mode violation" hatası verir.
  await page.getByLabel('Sifre', { exact: true }).fill(SIFRE);
  await page.getByLabel('Sifre tekrar').fill(SIFRE);

  await page.getByRole('button', { name: 'Hesap olustur' }).click();
}

test.describe('Bilet alma akışı', () => {
  test('kullanıcı kayıt olup bilet satın alabilir', async ({ page }) => {
    const eposta = benzersizEposta();

    // ==============================================================
    // 1 + 2) KAYIT VE GİRİŞ
    // ==============================================================
    await kayitOl(page, eposta);

    // Kayıt başarılıysa ana sayfaya yönlendiriliyor ve üst menü
    // görünüyor. "Cikis" düğmesinin varlığı, oturumun gerçekten
    // açıldığının en net kanıtı.
    await expect(page.getByRole('button', { name: 'Cikis' })).toBeVisible({
      timeout: 15_000,
    });

    // ==============================================================
    // 3) ETKİNLİK BUL
    // ==============================================================
    await menuBaglantisi(page, 'Etkinlikler').click();

    await expect(page.getByRole('heading', { name: 'Etkinlikler' })).toBeVisible();

    // ==============================================================
    // SATIŞTA OLAN BİR ETKİNLİK SEÇİLİYOR, İLKİ DEĞİL
    // ==============================================================
    // İlk kartı tıklasaydık test, seed verisinin SIRASINA bağımlı
    // olurdu. Bugün ilk sıradaki etkinliğin oturumu varsa geçer,
    // yarın sıra değişince "Koltuk sec" düğmesi bulunamaz ve test
    // kodda hiçbir değişiklik olmadan kırılırdı.
    //
    // Bunun yerine: oturumu OLAN bir etkinlik arıyoruz.
    // ==============================================================
    const etkinlikBaglantilari = page.locator('a[href^="/etkinlikler/"]');

    await expect(etkinlikBaglantilari.first()).toBeVisible();

    // ==============================================================
    // ADRESLERI ONCE TOPLUYORUZ, SONRA GEZIYORUZ
    // ==============================================================
    // Ilk denememde baglantilara sirayla tiklayip page.goBack()
    // yapiyordum. Test "etkinlik bulunamadi" dedi.
    //
    // Sebep: her gezinmeden sonra DOM yeniden olusuyor ve elimdeki
    // locator listesi BAYAT kaliyor; ayrica detay sayfasi tam
    // yuklenmeden "Koltuk sec" aranip bulunamiyordu.
    //
    // Adresleri bir kez toplayip goto() ile gitmek hem daha
    // guvenilir hem de daha hizli. Ayrica ayni etkinlige giden
    // birden fazla kart oldugu icin (populer listesi + ana liste)
    // tekrarlari eliyorum.
    // ==============================================================
    const adresler = [
      ...new Set(await etkinlikBaglantilari.evaluateAll(
        (baglantilar) => baglantilar.map((a) => a.getAttribute('href') ?? ''),
      )),
    ].filter(Boolean);

    let koltukSecAcildi = false;

    for (const adres of adresler) {
      await page.goto(adres);

      const koltukSec = page.getByRole('link', { name: 'Koltuk sec' }).first();

      // isVisible() ANLIK bakiyor; sayfa yuklenmemisse false doner.
      // Kisa bir bekleme veriyoruz ama testi bloklamadan: oturumu
      // olmayan etkinliklerde bu beklemenin dolmasi NORMAL.
      const gorunur = await koltukSec
        .waitFor({ state: 'visible', timeout: 5000 })
        .then(() => true)
        .catch(() => false);

      if (gorunur) {
        await koltukSec.click();
        koltukSecAcildi = true;
        break;
      }
    }

    expect(
      koltukSecAcildi,
      'Satisa acik ve oturumu olan bir etkinlik bulunamadi. ' +
        'Seed verisini kontrol edin.',
    ).toBe(true);

    // ==============================================================
    // 4) KOLTUK SEÇ
    // ==============================================================
    // ==============================================================
    // MÜSAİT KOLTUK DİNAMİK BULUNUYOR
    // ==============================================================
    // "A-1'e tıkla" diye sabit yazsaydık, o koltuk bir önceki test
    // çalıştırmasında satıldığında test kırılırdı -- ve sebebi
    // kodda değil, veritabanında olurdu.
    //
    // Seçilebilir koltukların tabindex="0" olması, bileşenin
    // yalnızca müsait koltukları klavyeye açmasından geliyor
    // (SeatMap birim testinde de doğrulanıyor).
    // ==============================================================
    const musaitKoltuk = page.locator('rect[role="button"][tabindex="0"]').first();

    await expect(musaitKoltuk).toBeVisible({ timeout: 15_000 });

    const koltukAdi = await musaitKoltuk.getAttribute('aria-label');

    await musaitKoltuk.click();

    // Seçim, kullanıcıya geri bildirilmeli: seçilen koltuk için bir
    // "çıkar" düğmesi beliriyor.
    await expect(
      page.getByRole('button', { name: /koltugunu secimden cikar/i }),
    ).toBeVisible();

    // ==============================================================
    // 5) REZERVASYON OLUŞTUR
    // ==============================================================
    await page.getByRole('button', { name: 'Koltuklari ayirt' }).click();

    // Rezervasyon kodu görünmeli.
    await expect(page.getByText(/RSV-/)).toBeVisible({ timeout: 15_000 });

    // ==============================================================
    // GERİ SAYIM ÇALIŞIYOR OLMALI
    // ==============================================================
    // Sayaç, kullanıcının koltuğunu kaybetmeden önce elindeki tek
    // uyarı. Görünmüyorsa kullanıcı süreyi bilmiyor demektir.
    //
    // Biçim mm:ss -- birim testinde (useCountdown) mantığı,
    // burada ekranda GERÇEKTEN göründüğü doğrulanıyor.
    // ==============================================================
    await expect(page.getByText(/^\d{2}:\d{2}$/)).toBeVisible();

    // ==============================================================
    // 6) ÖDEME
    // ==============================================================
    await page.getByRole('button', { name: /ode$/i }).click();

    // Simülasyon sağlayıcısının ekranı açılıyor.
    await expect(page.getByText('ODEME SIMULASYONU')).toBeVisible({
      timeout: 15_000,
    });

    await page.getByRole('button', { name: 'Odeme basarili' }).click();

    // ==============================================================
    // 7) BİLETİ GÖRÜNTÜLE
    // ==============================================================
    await expect(
      page.getByRole('heading', { name: 'Biletlerim' }),
    ).toBeVisible({ timeout: 20_000 });

    await expect(page.getByText(/Odemeniz alindi/i)).toBeVisible();

    // Bilet numarası üretilmiş olmalı.
    await expect(page.getByText(/TKT-/)).toBeVisible();

    // ==============================================================
    // EN ÖNEMLİ DOĞRULAMA: SEÇTİĞİMİZ KOLTUĞUN BİLETİ
    // ==============================================================
    // Yalnızca "bir bilet var" demek yetmez: yanlış koltuğun bileti
    // üretilseydi bu kontrol yine geçerdi.
    //
    // Koltuk etiketi "Orta A-3" biçiminde geliyor; bilet ekranında
    // "A-3" yazıyor. Son parçayı karşılaştırıyoruz.
    // ==============================================================
    const koltukEtiketi = koltukAdi?.split(' ').pop() ?? '';

    expect(koltukEtiketi).not.toBe('');
    await expect(page.getByText(koltukEtiketi, { exact: false }).first()).toBeVisible();
  });

  /**
   * ================================================================
   * PDF Sprint 17 frontend maddesi: "Responsive görünüm"
   * ================================================================
   * Bu test, playwright.config.ts'teki "mobil" projesinde Pixel 7
   * görünüm alanıyla da çalışıyor. Yani aynı akış hem masaüstünde
   * hem mobilde doğrulanıyor.
   *
   * Burada ayrıca menünün mobilde erişilebilir kaldığını
   * kontrol ediyorum: masaüstünde yatay duran menü, dar ekranda
   * taşarsa bağlantılar tıklanamaz hale gelir ve kullanıcı bilet
   * alma akışına hiç giremez.
   * ================================================================
   */
  test('gezinme menüsü dar ekranda kullanılabilir', async ({ page }) => {
    const eposta = benzersizEposta();

    await kayitOl(page, eposta);

    await expect(page.getByRole('button', { name: 'Cikis' })).toBeVisible({
      timeout: 15_000,
    });

    // Etkinlikler bağlantısı görünür VE tıklanabilir olmalı.
    //
    // toBeVisible() yetmez: bir eleman görünür olup başka bir
    // elemanın altında kalabilir. Tıklama denemesi bunu yakalıyor.
    const etkinlikler = menuBaglantisi(page, 'Etkinlikler');

    await expect(etkinlikler).toBeVisible();
    await etkinlikler.click();

    await expect(page.getByRole('heading', { name: 'Etkinlikler' })).toBeVisible();

    // ==============================================================
    // YATAY KAYDIRMA OLMAMALI
    // ==============================================================
    // Sayfa görünüm alanından genişse kullanıcı sağa kaydırmak
    // zorunda kalır ve içeriğin bir kısmını hiç görmez. Duyarlı
    // tasarımın en somut ölçütü budur.
    // ==============================================================
    const tasma = await page.evaluate(
      () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
    );

    // 1 piksellik yuvarlama farkına tolerans.
    expect(tasma).toBeLessThanOrEqual(1);
  });
});
