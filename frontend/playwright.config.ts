import { defineConfig, devices } from '@playwright/test';

/**
 * ==================================================================
 * PLAYWRIGHT YAPILANDIRMASI -- PDF Sprint 17: End-to-End Test
 * ==================================================================
 * E2E testleri, birim ve entegrasyon testlerinin GÖREMEDİĞİ tek
 * şeyi doğruluyor: sistemin PARÇALARI BİRLİKTE çalışıyor mu.
 *
 * Backend entegrasyon testleri API'nin doğru davrandığını,
 * Vitest testleri bileşenlerin doğru çizildiğini kanıtlıyor. Ama
 * ikisi de şunu kaçırır:
 *   - Vite proxy'si yanlış yapılandırılmış
 *   - Frontend, API'nin döndürdüğü alan adını yanlış okuyor
 *   - Yönlendirme zinciri kopuk
 *
 * Sprint 10'da tam olarak birinci türden bir hata yaşamıştım
 * (/hubs proxy girdisi eksikti). E2E testi olsaydı onu ilk
 * çalıştırmada yakalardı.
 * ==================================================================
 */
export default defineConfig({
  testDir: './e2e',

  // ================================================================
  // TEK İŞÇİ (worker) -- BİLİNÇLİ
  // ================================================================
  // Testler AYNI backend ve AYNI veritabanını paylaşıyor. Paralel
  // çalışsalardı aynı koltukları kapmaya çalışır ve birbirlerini
  // 409 ile düşürürlerdi.
  //
  // İronik olarak bu, sistemin doğru çalıştığının kanıtı olurdu ama
  // testleri güvenilmez yapardı.
  // ================================================================
  workers: 1,
  fullyParallel: false,

  // Yeniden deneme YOK.
  //
  // Açık olsaydı ara sıra kırılan (flaky) bir test, ikinci denemede
  // geçip sorunu gizlerdi. E2E'de kırılganlık genellikle GERÇEK bir
  // yarış koşulunun belirtisi -- gizlenmesi gereken değil,
  // araştırılması gereken bir şey.
  retries: 0,

  reporter: [['list']],

  use: {
    baseURL: 'http://localhost:5173',

    // Başarısız testte iz kaydı: hangi adımda ne olduğunu adım adım
    // görmeyi sağlıyor. E2E hatalarını yalnızca hata mesajından
    // teşhis etmek neredeyse imkânsız.
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',

    // Varsayılan 30 sn; koltuk haritası ve ödeme akışı için yeterli.
    actionTimeout: 15_000,
  },

  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },

    // ============================================================
    // PDF Sprint 17: "Responsive görünüm"
    // ============================================================
    // Aynı testleri mobil bir görünüm alanında da çalıştırıyoruz.
    //
    // Duyarlı tasarımı yalnızca ekran genişliğini değiştirerek test
    // etmek yetmez: mobil profil aynı zamanda dokunmatik olayları
    // ve mobil kullanıcı aracısını da taklit ediyor. Bir düğme
    // masaüstünde hover ile görünüp mobilde hiç görünmüyorsa,
    // yalnızca genişlik değiştiren bir test bunu kaçırırdı.
    // ============================================================
    {
      name: 'mobil',
      use: { ...devices['Pixel 7'] },
    },
  ],

  /**
   * ================================================================
   * SUNUCULARI TEST BAŞLATIYOR
   * ================================================================
   * Elle başlatmayı gerektirseydi, testler "unutulduğu için" hiç
   * çalıştırılmazdı. CI'da da ayrı bir adım yazmak gerekirdi.
   *
   * reuseExistingServer: geliştirici zaten `npm run dev`
   * çalıştırıyorsa yeniden başlatmıyor.
   *
   * NOT: Backend'i buradan başlatmıyorum çünkü PostgreSQL ve
   * Redis'in ayakta olmasını gerektiriyor. E2E testleri
   * çalıştırmadan önce `docker compose up -d` ve API'nin çalışıyor
   * olması gerekiyor -- README'de yazılı.
   * ================================================================
   */
  webServer: {
    command: 'npm run dev',
    url: 'http://localhost:5173',
    reuseExistingServer: true,
    timeout: 120_000,
  },
});
