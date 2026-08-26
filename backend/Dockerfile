# ===================================================================
# Backend API Dockerfile -- COK ASAMALI (multi-stage) YAPI
# ===================================================================
#
# Neden cok asamali?
#
# Tek asamali olsaydi son imaj icinde .NET SDK (~800 MB), NuGet onbellegi,
# kaynak kodumuz ve derleme ara ciktilari bulunurdu. Uc sorun:
#
#   1) BOYUT: ~1.2 GB yerine ~220 MB. Her deploy'da bu fark aga biner.
#   2) GUVENLIK: Uretim imajinda derleyici bulunmasi, saldirganin
#      container icinde kod derlemesine izin verir. Kaynak kodumuz da
#      imajda olmaz.
#   3) HIZ: Katman onbellegi sayesinde yalnizca degisen kisim yeniden
#      derlenir.
#
# Son imaj yalnizca "runtime" asamasindan olusur; oncekiler atilir.
# ===================================================================

# -------------------------------------------------------------------
# ASAMA 1: Derleme
# -------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS build
WORKDIR /src

# ---- Katman onbellegi optimizasyonu ----
#
# Once YALNIZCA proje dosyalarini kopyaliyorum, kaynak kodu degil.
#
# Neden? Docker her komutu bir katman olarak onbellege alir ve bir
# katman degisirse SONRAKI TUM katmanlar yeniden calisir.
#
# Kaynak kodu her commit'te degisir ama paket listesi nadiren degisir.
# Bu sirayla, kod degisikliginde "dotnet restore" adimi ONBELLEKTEN
# gelir ve tum paketleri yeniden indirmeyiz.
#
# Ters sirada yapsaydik her kucuk kod degisikligi 2-3 dakikalik
# paket indirmesi anlamina gelirdi.
COPY Directory.Build.props Directory.Packages.props ./
COPY src/Ticketing.Domain/Ticketing.Domain.csproj              src/Ticketing.Domain/
COPY src/Ticketing.Application/Ticketing.Application.csproj    src/Ticketing.Application/
COPY src/Ticketing.Infrastructure/Ticketing.Infrastructure.csproj src/Ticketing.Infrastructure/
COPY src/Ticketing.Persistence/Ticketing.Persistence.csproj    src/Ticketing.Persistence/
COPY src/Ticketing.WebApi/Ticketing.WebApi.csproj              src/Ticketing.WebApi/

RUN dotnet restore src/Ticketing.WebApi/Ticketing.WebApi.csproj

# Simdi kaynak kodu kopyala ve derle
COPY src/ src/

RUN dotnet publish src/Ticketing.WebApi/Ticketing.WebApi.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    # Uygulama dosyalarini tek klasorde toplarken sembolleri disarida birak.
    /p:UseAppHost=false

# -------------------------------------------------------------------
# ASAMA 2: Calistirma
# -------------------------------------------------------------------
# aspnet imaji SDK degil yalnizca CALISMA ZAMANI icerir.
# alpine varyanti en kucuk olani.
FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine AS runtime
WORKDIR /app

# ---- ROOT OLMAYAN KULLANICI ----
#
# Container'lar varsayilan olarak root ile calisir. Bu tehlikelidir:
# uygulamada bir guvenlik acigi bulunursa saldirgan container icinde
# root yetkisiyle hareket eder ve container kacisi (escape) girisimleri
# cok daha kolay hale gelir.
#
# Kendi kullanicimizi olusturup ona geciyoruz. Bu, en dusuk maliyetli
# ve en cok atlanan guvenlik onlemlerinden biridir.
RUN addgroup -g 1001 -S appgroup && \
    adduser -u 1001 -S appuser -G appgroup

# Yuklenen dosyalar icin dizin (Sprint 5: afis gorselleri)
RUN mkdir -p /app/uploads && chown -R appuser:appgroup /app

COPY --from=build --chown=appuser:appgroup /app/publish .

USER appuser

# ---- Ortam degiskenleri ----
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    # Alpine'da globalization desteginin acik kalmasi SART:
    # Turkce karakterler ve kultur bazli tarih/sayi bicimlendirmesi
    # icin ICU kutuphanesi gerekiyor. Bunu true yapsaydik
    # ToUpperInvariant disindaki tum kultur islemleri bozulurdu.
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

EXPOSE 8080

# ---- Saglik kontrolu ----
#
# Docker'in container'in gercekten HIZMET VERDIGINI anlamasi icin.
# Process'in ayakta olmasi yetmez; uygulama kilitlenmis olabilir.
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD wget --quiet --tries=1 --spider http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "Ticketing.WebApi.dll"]
