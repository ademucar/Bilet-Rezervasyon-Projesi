/// <reference types="vitest/config" />
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

export default defineConfig({
  plugins: [
    react(),
    // Tailwind v4 artik PostCSS yerine kendi Vite eklentisiyle calisiyor.
    // tailwind.config.js dosyasina gerek yok; yapilandirma CSS icinde.
    tailwindcss(),
  ],

  // ==================================================================
  // TESTLER -- PDF Sprint 17
  // ==================================================================
  // Vitest'i AYRI bir yapilandirma dosyasina koymadim: boylece
  // testler, uygulamanin GERCEK derleme ayarlariyla (Tailwind eklentisi,
  // React eklentisi, yol takma adlari) calisiyor.
  //
  // Ayri dosya olsaydi ikisi zamanla birbirinden ayrisir ve
  // "testte calisiyor, uygulamada calismiyor" durumu ortaya cikardi.
  // ==================================================================
  test: {
    // jsdom: tarayici DOM'unu Node icinde taklit ediyor.
    // Gercek tarayici (Playwright) E2E testlerinde kullaniliyor;
    // birim testleri icin jsdom cok daha hizli.
    environment: 'jsdom',

    // Her test dosyasindan once calisan kurulum (jest-dom eslesmeleri,
    // temizlik).
    setupFiles: ['./src/test/setup.ts'],

    // describe/it/expect'i her dosyada import etmeye gerek kalmasin.
    globals: true,

    css: false,
  },

  server: {
    port: 5173,

    proxy: {
      // ==============================================================
      // NEDEN PROXY? Dogrudan http://localhost:5000 cagirsak olmaz miydi?
      // ==============================================================
      // Olurdu ama iki sorun cikardi:
      //
      // 1) CORS. Tarayici 5173'ten 5000'e giden istekleri "farkli
      //    kaynak" sayar ve backend'in CORS izni vermesi gerekir.
      //    Proxy ile istek tarayici acisindan AYNI kaynaga (5173)
      //    gidiyor; Vite arka planda backend'e iletiyor. CORS devreye
      //    hic girmiyor.
      //
      // 2) Ortam farki. Uretimde frontend ve API genelde ayni alan adi
      //    altinda olur (/api yolu reverse proxy ile yonlendirilir).
      //    Gelistirmede de ayni yapiyi taklit edersek, kodda ortam
      //    bazli adres ayrimi yapmamiza gerek kalmaz -- her yerde
      //    sadece "/api/..." yazariz.
      // ==============================================================
      '/api': {
        target: 'http://localhost:5000',
        changeOrigin: true,
      },

      // ==============================================================
      // SIGNALR HUB'I -- PDF Sprint 10
      // ==============================================================
      // Bu girdiyi EKLEMEYI UNUTTUM ve gostergemiz sayesinde hemen
      // yakalandi: ekranda "Canli baglanti yok" yazdi.
      //
      // Gosterge olmasaydi harita yine calisirdi (yoklama yedegi
      // devrede) ve SignalR'in hic baglanmadigini fark etmezdim.
      // Sprint 10'u "bitti" sanip devam ederdim. Kucuk bir arayuz
      // parcasinin gercek degeri tam olarak bu.
      //
      // ws: true SART -- varsayilan proxy yalnizca HTTP'yi iletir.
      // SignalR once HTTP ile el sikisip sonra WebSocket'e
      // YUKSELTIYOR (Upgrade). Bu bayrak olmadan el sikisma
      // basarili olur, yukseltme sessizce basarisiz olur ve
      // SignalR daha yavas olan "long polling" moduna duser --
      // ya da hic baglanamaz.
      // ==============================================================
      '/hubs': {
        target: 'http://localhost:5000',
        changeOrigin: true,
        ws: true,
      },
    },
  },
})
