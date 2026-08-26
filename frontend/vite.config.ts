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
    },
  },
})
