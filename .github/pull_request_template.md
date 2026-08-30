## Ne yapildi?

<!-- Degisikligi bir paragrafta anlat. "Neden" kismi "ne" kismindan
     daha onemli: kodun kendisi "ne" oldugunu zaten soyluyor. -->

## Neden bu sekilde?

<!-- Degerlendirdigin alternatifler ve neden bunu sectigin.
     Bir odun (trade-off) verdiysen acikca yaz. -->

## Nasil dogrulandi?

<!-- "Test yazdim" yetmez: NE dogruladigini yaz.
     Ornek: "Postgres'i durdurup /health/live'in 200, /health/ready'nin
     503 dondugunu gordum." -->

- [ ] Birim testleri
- [ ] Entegrasyon testleri
- [ ] Elle denendi (nasil: ...)

## Kontrol listesi

- [ ] `dotnet build` -- 0 uyari, 0 hata (Debug **ve** Release)
- [ ] `dotnet test` -- tum testler yesil
- [ ] `npm run lint` ve `npm run format:check` temiz
- [ ] `npx tsc --noEmit` temiz
- [ ] Hassas deger (baglanti dizesi, token, sifre) EKLENMEDI
- [ ] Migration eklendiyse Git'e dahil edildi
- [ ] Davranis degistiyse ilgili dokuman guncellendi

## Notlar

<!-- Bilincli olarak ERTELENEN seyler, bilinen sinirlamalar,
     inceleyenin ozellikle bakmasini istedigin yerler. -->
