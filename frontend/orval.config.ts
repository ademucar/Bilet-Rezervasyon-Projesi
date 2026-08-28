import { defineConfig } from 'orval';

/**
 * ==================================================================
 * ORVAL -- OpenAPI'den TypeScript istemci uretimi
 * PDF Sprint 18: "OpenAPI Client ... arastirilmalidir"
 * ==================================================================
 * PDF uc arac oneriyor: NSwag, OpenAPI Generator, Orval.
 * Orval'i sectim; gerekcesi docs/15-api-dokumantasyonu.md'de.
 *
 * ------------------------------------------------------------------
 * NEDEN YALNIZCA TIPLER URETILIYOR?
 * ------------------------------------------------------------------
 * Orval, TanStack Query kancalarini da uretebiliyor
 * (client: 'react-query'). Bu cazip ama BENIMSEMEDIM:
 *
 *   - Elimizde 1100+ satirlik, calisan ve TEST EDILMIS bir istemci
 *     katmani var (bookingApi, authApi, adminApi...). Onlari toptan
 *     degistirmek Sprint 18'in kapsamini asar ve Sprint 17'de yazdigim
 *     testleri gecersiz kilardi.
 *
 *   - Uretilen kancalar bizim ozel davranislarimizi BILMIYOR: token
 *     yenileme, 409 sonrasi koltuk haritasini tazeleme, correlation
 *     ID basligi. Bunlarin hepsi lib/api/client.ts icinde.
 *
 * TIPLER ise net bir kazanc: elle yazdigimiz arayuzler backend
 * degistiginde SESSIZCE eskiyor. Uretilen tipler her derlemede
 * gercekle karsilastirilabiliyor.
 *
 * Bu, "arastirdim ve su kadarini benimsedim" demektir -- hepsini
 * ya da hicbirini degil.
 * ==================================================================
 */
export default defineConfig({
  biletim: {
    input: {
      // ==============================================================
      // KAYNAK: CALISAN API'NIN URETTIGI BELGE
      // ==============================================================
      // Elle tutulan bir openapi.json dosyasi kullanabilirdik ama o
      // dosya guncellenmeyi unutulurdu ve uretilen tipler gercekle
      // ayrisirdi -- tam olarak cozmeye calistigimiz sorun.
      //
      // Uretim icin API'nin ayakta olmasi gerekiyor. Bu bir bedel;
      // karsiliginda tiplerin GERCEK belgeden geldigine emin oluyoruz.
      // ==============================================================
      target: 'http://localhost:5000/openapi/v1.json',
    },

    output: {
      // Yalnizca semalar (tipler). HTTP cagri kodu uretilmiyor.
      mode: 'single',
      target: './src/lib/api/generated/schemas.ts',

      // ==============================================================
      // client: 'fetch' YERINE HICBIR SEY
      // ==============================================================
      // Orval'in varsayilani cagri fonksiyonlari da uretmek.
      // Kapatiyoruz: yalnizca "model" (tip) uretimi istiyoruz.
      // ==============================================================
      client: 'fetch',
      httpClient: 'fetch',

      override: {
        // Uretilen dosyalarda mutasyon/kanca istemiyoruz.
        mock: false,
      },
    },

    hooks: {
      // Uretilen kod bizim lint kurallarimizdan gecmeli.
      afterAllFilesWrite: 'npx oxlint --fix',
    },
  },
});
