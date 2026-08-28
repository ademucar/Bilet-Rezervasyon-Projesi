import { useEffect, useRef, useState } from 'react'

/**
 * ==================================================================
 * GERİ SAYIM -- PDF Sprint 7: "Geri sayım göstergesi"
 * ==================================================================
 *
 * ------------------------------------------------------------------
 * 1) NEDEN SUNUCUNUN SANIYESINDEN BASLIYORUZ?
 * ------------------------------------------------------------------
 * Backend `remainingSeconds` gönderiyor, `expiresAt` yerine önü
 * kullanıyoruz.
 *
 * Çünkü kullanıcının bilgisayar saati YANLIS olabilir. `expiresAt`
 * mutlak bir zaman; saati 5 dakika geri olan bir kullanıcı
 * `expiresAt - Date.now()` hesabiyla süreyi 15 dakika sanirdi ve
 * ödemeye geçtiğinde beklenmedik bir "süreniz doldu" hatası alırdı.
 *
 * Kalan süreyi SANIYE olarak almak saat farkindan tamamen bağımsız.
 *
 * ------------------------------------------------------------------
 * 2) NEDEN HER TIK'TA 1 AZALTMIYORUZ?
 * ------------------------------------------------------------------
 * En yaygin (ve hatalı) yazım sudur:
 *
 *     setInterval(() => setKalan((s) => s - 1), 1000)
 *
 * Bu iki sebeple bozuk:
 *
 *   a) setInterval TAM 1000 ms degildir. Tarayici mesgulse gecikir.
 *      10 dakikada birikimli kayma onlarca saniyeyi bulur.
 *
 *   b) DAHA KOTUSU: kullanıcı sekmeyi arka plana attiginda tarayıcı
 *      zamanlayicilari DAKIKADA BIR'e kadar yavaslatir. Kullanıcı
 *      3 dakika başka sekmede kalip geri donse, sayaç yalnızca 3
 *      saniye azalmis görünürdü. Ekranda "7:00 kaldı" yazarken
 *      rezervasyon coktan silinmis olurdu.
 *
 * COZUM: Baslangicta bir BITIS ANI hesapliyoruz ve her tik'ta
 * "bitise ne kadar kaldı" diye YENIDEN olcuyoruz. Tik geç de gelse,
 * hiç de gelmese, gosterilen deger her zaman doğru olur.
 *
 * `performance.now()` kullanıyorum, `Date.now()` değil:
 * performance.now() monotondur -- sistem saati degisse veya yaz
 * saati uygulamasi devreye girse bile geriye gitmez.
 * ==================================================================
 */
export function useCountdown(initialSeconds: number | undefined): number {
  const [remaining, setRemaining] = useState(initialSeconds ?? 0)

  // Bitiş ani, render'lar arasında korunmali ama DEGISIMI bir
  // yeniden cizim tetiklememeli -> useRef, useState değil.
  const deadlineRef = useRef<number>(0)

  // ----------------------------------------------------------------
  // 3) LINT UYARISI VE NEDEN SUSTURDUM
  // ----------------------------------------------------------------
  // Aşağıdaki effect, içinde setRemaining cagirdigi için oxlint'in
  // `react(set-state-in-effect)` kuralini tetikliyor. Kural haklı
  // bir sey söylüyor: state'i effect içinde ayarlamak çoğu zaman
  // "aslında bunu render sırasında hesaplayabilirdin" demektir.
  //
  // Önce kuralin dedigini YAPTIM: degisimi render sırasında fark
  // edip orada sifirladim. Ama o zaman iki YENI uyarı cikti --
  // `react(purity)` ve `react(refs)`. Hakliydilar: render sırasında
  // performance.now() okumak ve ref'e yazmak, React'in saf render
  // sozunu bozuyor.
  //
  // Sebep su: bu kanca React'i bir DIS SISTEME bagliyor -- tarayıcının
  // saatine. React dokumantasyonunun useEffect için verdiği tanim tam
  // olarak bu. Yani burada effect kullanmak kacamak değil, DOGRU
  // arac.
  //
  // Bu yüzden kuralı dar kapsamda, gerekcesiyle susturuyorum.
  // Projede benimsedigim kural: uyariyi susturmak serbest değil,
  // yalnızca "neden" yazildiginda serbest.
  // ----------------------------------------------------------------
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
    // ile bizim olcumumuz arasında 999 ms'ye kadar fark olusabilir
    // ve sayaç bazen bir saniyeyi ATLAR (7:00 -> 6:58 gibi görünür).
    // 250 ms hem bunu onluyor hem de gozle gorulur bir maliyeti yok:
    // yalnızca bir cikarma islemi yapiyoruz.
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
