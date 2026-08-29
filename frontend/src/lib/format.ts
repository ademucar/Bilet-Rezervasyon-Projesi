/**
 *
 * BICIMLENDIRME YARDIMCILARI
 *
 * Tarih ve para bicimlendirmesini TEK YERDE topluyorum.
 *
 * Neden? Çünkü bunlari her bileşende elle yazmak iki somut hataya
 * yol aciyor:
 *
 *   1) Tutarlilik: bir ekranda "1.250,00 TL", digerinde "1250 TRY"
 *      görünür. Kullanıcı aynı uygulamada oldugundan suphe eder.
 *
 *   2) Performans: Intl.NumberFormat NESNESI olusturmak pahalidir.
 *      Her render'da `new Intl.NumberFormat(...)` yazmak, 100
 *      biletlik listede 100 nesne üretir. Asagida bir kez olusturup
 *      yeniden kullanıyorum.
 *
 */

// Modul yuklenirken BIR KEZ oluşturuluyor.
const dateTimeFormatter = new Intl.DateTimeFormat('tr-TR', {
  day: '2-digit',
  month: 'long',
  year: 'numeric',
  hour: '2-digit',
  minute: '2-digit',
})

const dateFormatter = new Intl.DateTimeFormat('tr-TR', {
  day: '2-digit',
  month: 'long',
  year: 'numeric',
})

/**
 * Para bicimlendirici önbelleği.
 *
 * Para birimi degisken (TRY, USD, EUR) olduğu için tek bir nesne
 * yetmiyor. Gorulen her para birimi için bir kez olusturup
 * sakliyorum -- Map, tekrar tekrar olusturmayi engelliyor.
 */
const currencyFormatters = new Map<string, Intl.NumberFormat>()

export function formatMoney(amount: number, currency: string): string {
  let formatter = currencyFormatters.get(currency)

  if (!formatter) {
    formatter = new Intl.NumberFormat('tr-TR', {
      style: 'currency',
      currency,
      minimumFractionDigits: 2,
    })

    currencyFormatters.set(currency, formatter)
  }

  return formatter.format(amount)
}

/**
 * ISO 8601 metnini okunabilir tarih-saate cevirir.
 *
 * Backend her zaman UTC ve ofsetli gönderiyor ("2026-09-14T20:00:00+00:00").
 * `new Date(...)` bunu KULLANICININ yerel saatine cevirir -- istedigim
 * tam olarak bu: İstanbul'daki kullanıcı 23:00 gorsun.
 */
export function formatDateTime(isoString: string): string {
  return dateTimeFormatter.format(new Date(isoString))
}

export function formatDate(isoString: string): string {
  return dateFormatter.format(new Date(isoString))
}

/**
 * Tarihi PARCALARINA ayirir: takvim yirtmaci bileseni için.
 *
 * NEDEN AYRI BIR ISLEV?
 *
 * formatDateTime "27 Ekim 2026 20:00" gibi TEK bir cumle dönüyor.
 * Kart tasariminda tarihi bir NESNE gibi göstermek istiyorum:
 * ay ustte küçük, gün ortada iri, saat altta.
 *
 * Bunu tek metni parcalayarak yapsaydim ("27 Ekim 2026 20:00"
 * dizesini bosluktan bolerek) biçim değişince kirilirdi. Intl'e
 * her parcayi ayrı ayrı sordurmak daha saglam.
 *
 * Ay adını KISALTIYORUM (Eki, Kas) çünkü yirtmac 72px genis --
 * "Aralik" oraya sigmiyor, "Ara" siğiyor.
 *
 */
const ayKisaFormatter = new Intl.DateTimeFormat('tr-TR', { month: 'short' })
const gunFormatter = new Intl.DateTimeFormat('tr-TR', { day: 'numeric' })
const saatFormatter = new Intl.DateTimeFormat('tr-TR', {
  hour: '2-digit',
  minute: '2-digit',
})

export interface TarihParcalari {
  ay: string
  gun: string
  saat: string
  yil: string
}

export function formatDateParts(isoString: string): TarihParcalari {
  const tarih = new Date(isoString)

  return {
    ay: ayKisaFormatter.format(tarih).replace('.', ''),
    gun: gunFormatter.format(tarih),
    saat: saatFormatter.format(tarih),
    yil: String(tarih.getFullYear()),
  }
}
