# ==================================================================
# FRONTEND IMAJI -- PDF Sprint 2: Docker Compose'da "Frontend" servisi
# ==================================================================
# Sprint 19 denetiminde bu dosyanin HIC OLMADIGINI buldum.
# docker-compose.yml postgres, redis, api, pgadmin, redis-insight ve
# mailpit'i ayaga kaldiriyordu ama FRONTEND yoktu.
#
# PDF iki yerde istiyor:
#   Sprint 2  -> compose servisleri arasinda "Frontend"
#   Zorunlu   -> "Tum proje Docker Compose ile ayaga
#                 kaldirilabilmelidir."
#
# Yani "tum proje" maddesi karsilanmiyordu: arayuzu calistirmak icin
# ayrica `npm run dev` demek gerekiyordu.
# ==================================================================

# ------------------------------------------------------------------
# 1. ASAMA: DERLEME
# ------------------------------------------------------------------
FROM node:22-alpine AS build

WORKDIR /app

# ==================================================================
# ONCE package DOSYALARI, SONRA KAYNAK KOD
# ==================================================================
# Docker her satiri onbellege aliyor. Kaynak kodu once kopyalasaydik,
# tek bir .tsx degisikliginde npm ci BASTAN calisirdi (dakikalar).
#
# Bu sirayla: bagimliliklar degismedikce o katman onbellekten
# geliyor ve yalnizca derleme yeniden yapiliyor.
# ==================================================================
COPY package.json package-lock.json ./

# npm ci, npm install DEGIL: kilit dosyasina birebir uyuyor.
# install kilit dosyasini guncelleyebilir ve imaj, gelistiricinin
# test ettiginden FARKLI surumlerle derlenebilirdi.
RUN npm ci

COPY . .

# ==================================================================
# API ADRESI DERLEME ZAMANINDA GOMULUYOR
# ==================================================================
# Vite ortam degiskenlerini derleme aninda kodun icine yaziyor;
# calisma zamaninda degistirilemiyor.
#
# Varsayilan "/api": nginx ayni kaynaktan API'ye vekillik ediyor
# (asagida). Boylece tarayici acisindan CORS hic devreye girmiyor --
# gelistirmedeki Vite proxy'siyle ayni yapi.
# ==================================================================
ARG VITE_API_URL=/api
ENV VITE_API_URL=${VITE_API_URL}

RUN npm run build

# ------------------------------------------------------------------
# 2. ASAMA: SUNUM
# ------------------------------------------------------------------
# ==================================================================
# NEDEN COK ASAMALI (multi-stage)?
# ==================================================================
# Node imaji ~200 MB ve icinde npm, derleyici, tum node_modules var.
# Uretimde bunlarin HICBIRINE ihtiyac yok: elimizde yalnizca statik
# dosyalar var.
#
# nginx-alpine ~25 MB. Hem cok daha kucuk hem de saldiri yuzeyi
# kucuk: imajda npm yok, derleyici yok, kaynak kod yok.
# ==================================================================
FROM nginx:1.27-alpine AS runtime

COPY --from=build /app/dist /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 80

# 127.0.0.1, "localhost" DEGIL.
#
# Container icinde localhost once ::1 (IPv6) olarak cozuluyor ama
# nginx yalnizca 0.0.0.0:80 dinliyor. Sonuc: disaridan gelen her
# istek 200 donerken saglik kontrolu "connection refused" aliyor ve
# container sonsuza kadar "unhealthy" gorunuyordu.
#
# Servisin kendisinde sorun yoktu -- kontrol yanlis kapiyi
# caliyordu. Ama compose'da baska bir servis buna service_healthy
# ile baglanmis olsaydi hicbir zaman baslamazdi.
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD wget --quiet --tries=1 --spider http://127.0.0.1/ || exit 1

CMD ["nginx", "-g", "daemon off;"]
