# 5000 portunu dinleyen sureci durdurur.
#
# Neden gerekli? "dotnet run" ile calisan API, derleme ciktisi
# DLL'lerini kilitler. Kod degistirip yeniden derlemek istediginde
# MSB3021 hatasi alirsin ve -- dikkat -- bu hata "error CS" desenine
# UYMAZ. Grep'i dar tutarsan derlemenin basarisiz oldugunu fark etmez,
# ESKI binary'yi test edersin. (Bu tuzaga Sprint 4'te dustuk.)
Get-NetTCPConnection -LocalPort 5000 -State Listen -ErrorAction SilentlyContinue |
    ForEach-Object { Stop-Process -Id $_.OwningProcess -Force -ErrorAction SilentlyContinue }
Start-Sleep -Seconds 2
Write-Output "API durduruldu (varsa)."
