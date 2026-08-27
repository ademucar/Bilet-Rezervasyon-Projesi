import { useEffect, useRef, useState } from 'react'

/**
 * ==================================================================
 * GERI SAYIM -- PDF Sprint 7: "Geri sayim gostergesi"
 * ==================================================================
 *
 * ------------------------------------------------------------------
 * 1) NEDEN SUNUCUNUN SANIYESINDEN BASLIYORUZ?
 * ------------------------------------------------------------------
 * Backend `remainingSeconds` gonderiyor, `expiresAt` yerine onu
 * kullaniyoruz.
 *
 * Cunku kullanicinin bilgisayar saati YANLIS olabilir. `expiresAt`
 * mutlak bir zaman; saati 5 dakika geri olan bir kullanici
 * `expiresAt - Date.now()` hesabiyla sureyi 15 dakika sanirdi ve
 * odemeye gectiginde beklenmedik bir "sureniz doldu" hatasi alirdi.
 *
 * Kalan sureyi SANIYE olarak almak saat farkindan tamamen bagimsiz.
 *
 * ------------------------------------------------------------------
 * 2) NEDEN HER TIK'TA 1 AZALTMIYORUZ?
 * ------------------------------------------------------------------
 * En yaygin (ve hatali) yazim sudur:
 *
 *     setInterval(() => setKalan((s) => s - 1), 1000)
 *
 * Bu iki sebeple bozuk:
 *
 *   a) setInterval TAM 1000 ms degildir. Tarayici mesgulse gecikir.
 *      10 dakikada birikimli kayma onlarca saniyeyi bulur.
 *
 *   b) DAHA KOTUSU: kullanici sekmeyi arka plana attiginda tarayici
 *      zamanlayicilari DAKIKADA BIR'e kadar yavaslatir. Kullanici
 *      3 dakika baska sekmede kalip geri donse, sayac yalnizca 3
 *      saniye azalmis gorunurdu. Ekranda "7:00 kaldi" yazarken
 *      rezervasyon coktan silinmis olurdu.
 *
 * COZUM: Baslangicta bir BITIS ANI hesapliyoruz ve her tik'ta
 * "bitise ne kadar kaldi" diye YENIDEN olcuyoruz. Tik gec de gelse,
 * hic de gelmese, gosterilen deger her zaman dogru olur.
 *
 * `performance.now()` kullaniyorum, `Date.now()` degil:
 * performance.now() monotondur -- sistem saati degisse veya yaz
 * saati uygulamasi devreye girse bile geriye gitmez.
 * ==================================================================
 */
export function useCountdown(initialSeconds: number | undefined): number {
  const [remaining, setRemaining] = useState(initialSeconds ?? 0)

  // Bitis ani, render'lar arasinda korunmali ama DEGISIMI bir
  // yeniden cizim tetiklememeli -> useRef, useState degil.
  const deadlineRef = useRef<number>(0)

  // ----------------------------------------------------------------
  // 3) LINT UYARISI VE NEDEN SUSTURDUM
  // ----------------------------------------------------------------
  // Asagidaki effect, icinde setRemaining cagirdigi icin oxlint'in
  // `react(set-state-in-effect)` kuralini tetikliyor. Kural hakli
  // bir sey soyluyor: state'i effect icinde ayarlamak cogu zaman
  // "aslinda bunu render sirasinda hesaplayabilirdin" demektir.
  //
  // Once kuralin dedigini YAPTIM: degisimi render sirasinda fark
  // edip orada sifirladim. Ama o zaman iki YENI uyari cikti --
  // `react(purity)` ve `react(refs)`. Hakliydilar: render sirasinda
  // performance.now() okumak ve ref'e yazmak, React'in saf render
  // sozunu bozuyor.
  //
  // Sebep su: bu kanca React'i bir DIS SISTEME bagliyor -- tarayicinin
  // saatine. React dokumantasyonunun useEffect icin verdigi tanim tam
  // olarak bu. Yani burada effect kullanmak kacamak degil, DOGRU
  // arac.
  //
  // Bu yuzden kurali dar kapsamda, gerekcesiyle susturuyorum.
  // Projede benimsedigim kural: uyariyi susturmak serbest degil,
  // yalnizca "neden" yazildiginda serbest.
  // ----------------------------------------------------------------
  useEffect(() => {
    if (initialSeconds === undefined) {
      return
    }

    // Negatif gelirse (sunucu ile aramizda gecikme varsa) sifira cek.
    const seconds = Math.max(0, initialSeconds)

    deadlineRef.current = performance.now() + seconds * 1000

    // Sunucudan YENI bir sure geldiginde (ornegin kullanici "5 dakika
    // uzat" dedi) sayacin oradan devam etmesi icin gosterimi de
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

    // 250 ms'de bir olcuyorum, 1000 ms'de bir degil.
    //
    // Sebep: 1 saniyelik araliklarla olcersek, gercek saniye gecisi
    // ile bizim olcumumuz arasinda 999 ms'ye kadar fark olusabilir
    // ve sayac bazen bir saniyeyi ATLAR (7:00 -> 6:58 gibi gorunur).
    // 250 ms hem bunu onluyor hem de gozle gorulur bir maliyeti yok:
    // yalnizca bir cikarma islemi yapiyoruz.
    const timer = setInterval(tick, 250)

    // Temizlik SART. Kullanici odeme sayfasindan cikinca zamanlayici
    // calismaya devam etseydi, React "kaldirilmis bilesende state
    // guncellemesi" uyarisi verir ve bellek sizintisi olusurdu.
    return () => clearInterval(timer)
  }, [initialSeconds])

  return remaining
}

/** 425 saniyeyi "07:05" olarak yazar. */
export function formatCountdown(totalSeconds: number): string {
  const safe = Math.max(0, totalSeconds)
  const minutes = Math.floor(safe / 60)
  const seconds = safe % 60

  // padStart olmasaydi "7:5" yazardi; sayaclarda bu okunaksizdir.
  return `${String(minutes).padStart(2, '0')}:${String(seconds).padStart(2, '0')}`
}
