/**
 * ==================================================================
 * BICIMLENDIRME YARDIMCILARI
 * ==================================================================
 * Tarih ve para bicimlendirmesini TEK YERDE topluyorum.
 *
 * Neden? Cunku bunlari her bilesende elle yazmak iki somut hataya
 * yol aciyor:
 *
 *   1) Tutarlilik: bir ekranda "1.250,00 TL", digerinde "1250 TRY"
 *      gorunur. Kullanici ayni uygulamada oldugundan suphe eder.
 *
 *   2) Performans: Intl.NumberFormat NESNESI olusturmak pahalidir.
 *      Her render'da `new Intl.NumberFormat(...)` yazmak, 100
 *      biletlik listede 100 nesne uretir. Asagida bir kez olusturup
 *      yeniden kullaniyoruz.
 * ==================================================================
 */

// Modul yuklenirken BIR KEZ olusturuluyor.
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
 * Para bicimlendirici onbellegi.
 *
 * Para birimi degisken (TRY, USD, EUR) oldugu icin tek bir nesne
 * yetmiyor. Gorulen her para birimi icin bir kez olusturup
 * sakliyoruz -- Map, tekrar tekrar olusturmayi engelliyor.
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
 * Backend her zaman UTC ve ofsetli gonderiyor ("2026-09-14T20:00:00+00:00").
 * `new Date(...)` bunu KULLANICININ yerel saatine cevirir -- istedigimiz
 * tam olarak bu: Istanbul'daki kullanici 23:00 gorsun.
 */
export function formatDateTime(isoString: string): string {
  return dateTimeFormatter.format(new Date(isoString))
}

export function formatDate(isoString: string): string {
  return dateFormatter.format(new Date(isoString))
}
