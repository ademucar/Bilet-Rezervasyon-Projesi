import { useEffect, useRef, useState } from 'react'

// Geri sayım -- PDF Sprint 7 "geri sayım göstergesi".
//
// Backend hem expiresAt hem remainingSeconds gönderiyor;
// remainingSeconds'i kullanıyorum. expiresAt mutlak bir zaman ve
// kullanıcının bilgisayar saatine güveniyor. Saati beş dakika geri
// olan biri süreyi on beş dakika sanır, sonra ödemeye geçtiğinde
// hiç beklemediği bir "süreniz doldu" yer. Kalan süreyi saniye
// olarak almak bu farkı tamamen dışarıda bırakıyor.

export function useCountdown(initialSeconds: number | undefined): number {
  const [remaining, setRemaining] = useState(initialSeconds ?? 0)

  // Bitiş anı render'lar arasında korunmalı ama değişmesi yeniden
  // çizim tetiklememeli; o yüzden useRef, useState değil.
  //
  // Baştan yazacağım ilk şey şuydu:
  //
  //     setInterval(() => setKalan((s) => s - 1), 1000)
  //
  // İki yerden bozuk. Birincisi setInterval tam 1000 ms değil,
  // tarayıcı meşgulse gecikiyor; on dakikada birikimli kayma
  // onlarca saniyeyi buluyor. İkincisi ve daha kötüsü, kullanıcı
  // sekmeyi arka plana attığında tarayıcı zamanlayıcıları dakikada
  // bire kadar yavaşlatıyor. Üç dakika başka sekmede kalıp dönse
  // sayaç üç saniye inmiş görünürdü -- ekranda "7:00 kaldı"
  // yazarken rezervasyon çoktan silinmiş olurdu.
  //
  // Onun yerine başta bir bitiş anı hesaplayıp her tikte "bitişe ne
  // kaldı" diye yeniden ölçüyorum. Tik geç gelse de, hiç gelmese de
  // ekrandaki değer doğru.
  //
  // performance.now(), Date.now() değil: monoton, yani sistem saati
  // değişse veya yaz saati devreye girse bile geriye gitmiyor.
  const deadlineRef = useRef<number>(0)

  // Bu effect içinde setRemaining çağırdığım için oxlint
  // react(set-state-in-effect) kuralına takılıyor. Kural boşuna
  // konmamış: state'i effect içinde ayarlamak çoğu zaman "bunu
  // render sırasında hesaplayabilirdin" demek.
  //
  // Denedim. Değişimi render sırasında yakalayıp orada sıfırladım
  // ve bu sefer react(purity) ile react(refs) uyardı -- onlar da
  // haklıydı, render sırasında performance.now() okuyup ref'e
  // yazmak React'in saf render sözünü bozuyor.
  //
  // Sonunda şuraya vardım: bu kanca React'i dışarıdaki bir sisteme,
  // tarayıcının saatine bağlıyor. useEffect'in tarifi tam olarak bu.
  // Yani effect burada kaçamak değil, doğru araç. Kuralı tek satırda
  // ve gerekçesiyle susturuyorum -- gerekçesiz susturmayı kendime
  // yasakladım.
  useEffect(() => {
    if (initialSeconds === undefined) {
      return
    }

    // Negatif gelirse (sunucu ile aramizda gecikme varsa) sıfıra cek.
    const seconds = Math.max(0, initialSeconds)

    deadlineRef.current = performance.now() + seconds * 1000

    // Sunucudan YENI bir süre geldiğinde (örneğin kullanıcı "5 dakika
    // uzat" dedi) sayacin oradan devam etmesi için gosterimi de
    // hemen guncelliyoruz.
    // oxlint-disable-next-line react/set-state-in-effect
    setRemaining(seconds)

    if (seconds === 0) {
      return
    }

    const tick = () => {
      const left = Math.max(0, Math.round((deadlineRef.current - performance.now()) / 1000))

      setRemaining(left)

      if (left === 0) {
        clearInterval(timer)
      }
    }

    // 250 ms'de bir olcuyorum, 1000 ms'de bir değil.
    //
    // Sebep: 1 saniyelik araliklarla olcersek, gerçek saniye gecisi
    // ile benim olcumum arasında 999 ms'ye kadar fark olusabilir
    // ve sayaç bazen bir saniyeyi ATLAR (7:00 -> 6:58 gibi görünür).
    // 250 ms hem bunu onluyor hem de gozle gorulur bir maliyeti yok:
    // yalnızca bir cikarma islemi yapiyorum.
    const timer = setInterval(tick, 250)

    // Temizlik ŞART. Kullanıcı ödeme sayfasindan cikinca zamanlayici
    // calismaya devam etseydi, React "kaldirilmis bileşende state
    // guncellemesi" uyarısı verir ve bellek sizintisi olusurdu.
    return () => clearInterval(timer)
  }, [initialSeconds])

  return remaining
}

/** 425 saniyeyi "07:05" olarak yazar. */
export function formatCountdown(totalSeconds: number): string {
  const safe = Math.max(0, totalSeconds)
  const minutes = Math.floor(safe / 60)
  const seconds = safe % 60

  // padStart olmasaydı "7:5" yazardi; sayaclarda bu okunaksizdir.
  return `${String(minutes).padStart(2, '0')}:${String(seconds).padStart(2, '0')}`
}
