import type { ReactNode } from 'react'
import { formatCountdown } from '../hooks/useCountdown'

/**
 *
 * ÖDEME GERİ SAYIMI -- ÜÇ AŞAMA
 *
 * Sayaç tek bir görünüme sahip olmamalı. Kalan süre azaldıkça
 * BİÇİMİ de değişmeli: kullanıcı saati okumadan, çevresel görüşüyle
 * aciliyeti sezmeli.
 *
 *   > 3 dk     nötr    -- "vaktin var, acele etme"
 *   1-3 dk     kehribar -- "koltukların birazdan serbest kalacak"
 *   < 1 dk     kırmızı  -- "şimdi tamamla"
 *
 * Önceki hâlde iki aşama vardı (kehribar / son 60 sn kırmızı) ve
 * kehribar ta baştan yanıyordu. Ekranın ilk saniyesinden itibaren
 * uyarı rengi göstermek, uyarıyı anlamsızlaştırıyor: kullanıcı
 * sarıya alışıyor ve gerçekten acil olduğunda fark etmiyor.
 *
 * NEDEN İLERLEME ÇUBUĞU YOK?
 *
 * Tasarım taslağında sayaçın altında bir dolu/boş çubuk vardı.
 * Çizmedim: çubuğun paydası "toplam tutma süresi" olmalı, ama
 * ReservationDto bunu döndürmüyor (yalnızca expiresAt ve
 * remainingSeconds var; rezervasyonun NE ZAMAN oluştuğu yok).
 *
 * Paydayı uydurabilirdim -- "herhalde 10 dakikadır" diye. O zaman
 * backend tutma süresini değiştirdiğinde çubuk sessizce yalan
 * söylemeye başlardı ve kimse fark etmezdi. Renk zaten aynı bilgiyi
 * doğru veriyor.
 *
 */

type Asama = 'normal' | 'uyari' | 'kritik'

function asamaBul(remaining: number): Asama {
  if (remaining <= 60) return 'kritik'
  if (remaining <= 180) return 'uyari'
  return 'normal'
}

const KUTU: Record<Asama, string> = {
  normal: 'border-slate-300 bg-white',
  uyari: 'border-amber-300 bg-amber-50',
  kritik: 'border-red-300 bg-red-50',
}

const BASLIK: Record<Asama, string> = {
  normal: 'border-slate-200 text-slate-400',
  uyari: 'border-amber-200 text-amber-700',
  kritik: 'border-red-200 text-red-700',
}

const RAKAM: Record<Asama, string> = {
  normal: 'text-slate-900 font-semibold',
  uyari: 'text-amber-700 font-bold',
  kritik: 'text-red-700 font-bold',
}

const ACIKLAMA: Record<Asama, string> = {
  normal: 'text-slate-500',
  uyari: 'text-amber-800',
  kritik: 'text-red-800',
}

const METIN: Record<Asama, string> = {
  normal: 'Ödeme için kalan süre',
  uyari: 'Koltuklarınız birazdan serbest kalacak',
  kritik: 'Ödemeyi şimdi tamamlayın',
}

interface ReservationCountdownProps {
  remaining: number
  /** Uzat / vazgeç düğmeleri. Sayaç bunları tanımıyor, sadece yerleştiriyor. */
  actions?: ReactNode
}

export function ReservationCountdown({ remaining, actions }: ReservationCountdownProps) {
  const asama = asamaBul(remaining)

  return (
    <div className={`mt-6 rounded-[4px] border ${KUTU[asama]}`}>
      <div className={`flex items-center gap-2 border-b px-3.5 py-2.5 ${BASLIK[asama]}`}>
        {asama === 'kritik' && (
          /* Nabız atan nokta: hareketi göz, metni okumadan yakalıyor.
             Yalnızca son dakikada -- sürekli yanıp sönen bir nokta
             sayfanın tamamını okunmaz yapardı. */
          <span
            className="size-1.5 shrink-0 animate-pulse rounded-full bg-red-600"
            aria-hidden="true"
          />
        )}

        {asama === 'uyari' && (
          <svg
            className="size-3.5 shrink-0"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            strokeWidth="2"
            strokeLinecap="round"
            aria-hidden="true"
          >
            <path d="M12 9v4M12 17h.01" />
            <path d="M10.3 3.9 2.4 18a2 2 0 0 0 1.7 3h15.8a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0Z" />
          </svg>
        )}

        <span className="label-xs text-current">
          {asama === 'kritik'
            ? 'Son 1 dakika'
            : asama === 'uyari'
              ? 'Süre azalıyor'
              : 'Rezervasyon açık'}
        </span>
      </div>

      <div className="flex flex-wrap items-center justify-between gap-4 p-4">
        <div>
          <p
            className={`num text-[38px] leading-none ${RAKAM[asama]}`}
            /* role="timer" + aria-live="off": ekran okuyucu her saniye
               konuşmasın. Saniyede bir okunan bir sayaç, ekran okuyucu
               kullanıcısı için ekranı kullanılamaz hâle getirir.
               Kritik uyarıyı alttaki metin veriyor. */
            role="timer"
            aria-live="off"
          >
            {formatCountdown(remaining)}
          </p>
          <p className={`mt-2 text-[13px] ${ACIKLAMA[asama]}`}>{METIN[asama]}</p>
        </div>

        {actions && <div className="flex gap-2">{actions}</div>}
      </div>
    </div>
  )
}
