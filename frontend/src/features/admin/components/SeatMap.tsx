import { useMemo } from 'react'
import type { SectionDetail } from '../api/adminApi'

interface SeatMapProps {
  sections: SectionDetail[]
  /** Koltuga tiklandiginda. Verilmezse harita salt okunur olur. */
  onSeatClick?: (seatId: string) => void
  /** Secili koltuklarin Id'leri. */
  selectedSeatIds?: ReadonlySet<string>
}

// Koltuk gorsel sabitleri (SVG birimi).
const SEAT_SIZE = 18
const SEAT_GAP = 4
const ROW_LABEL_WIDTH = 28
const SECTION_GAP = 32
const SECTION_TITLE_HEIGHT = 24

/**
 * ==================================================================
 * GORSEL KOLTUK HARITASI
 * ==================================================================
 * PDF Sprint 4: "Gorsel koltuk plani"
 * PDF Sprint 7: "Gorsel koltuk secimi"
 *
 * Bu bileseni Sprint 7'de de kullanacagiz. O yuzden bastan
 * genisletilebilir tasarliyorum: onSeatClick ve selectedSeatIds
 * opsiyonel; verilmezse salt okunur bir onizleme olur.
 *
 * ------------------------------------------------------------------
 * NEDEN SVG? Neden div/CSS grid degil?
 * ------------------------------------------------------------------
 * 1) OLCEKLENEBILIRLIK: SVG vektoreldir. viewBox ile harita, kapsayici
 *    genisligine gore kendini olceklendirir -- mobilde de masaustunde
 *    de bozulmadan calisir. CSS grid ile bunu yapmak icin karmasik
 *    medya sorgulari gerekirdi.
 *
 * 2) PERFORMANS: 2000 koltuk = 2000 DOM elemani. <div> ile her biri
 *    tam bir CSS kutu modeli hesaplamasi (layout, paint, composite)
 *    gerektirir. SVG <rect> cok daha hafiftir; tarayici bunlari tek
 *    bir cizim katmaninda isler.
 *
 * 3) ILERIDE: Sprint 7'de yakinlastirma (zoom) ve kaydirma (pan)
 *    ekleyecegiz. SVG'de bu tek bir transform niteligiyle olur.
 *
 * ------------------------------------------------------------------
 * ERISILEBILIRLIK
 * ------------------------------------------------------------------
 * SVG varsayilan olarak ekran okuyuculara KAPALIDIR. Her koltuga
 * <title> ekliyoruz ve role="button" veriyoruz ki klavyeyle
 * gezilebilsin. PDF Sprint 18: "Keyboard navigation desteklenmelidir."
 * ==================================================================
 */
export function SeatMap({ sections, onSeatClick, selectedSeatIds }: SeatMapProps) {
  const isInteractive = Boolean(onSeatClick)

  /**
   * Yerlesim hesabi.
   *
   * useMemo ile SARILI cunku bu hesap 2000 koltuk icin binlerce
   * nesne uretiyor. Her render'da tekrar calissaydi (ornegin
   * kullanici bir koltuk sectiginde) arayuz gozle gorulur sekilde
   * takilirdi.
   *
   * Bagimlilik yalnizca `sections`: secim degistiginde yerlesim
   * DEGISMEZ, yalnizca renkler degisir -- onlari da asagida render
   * sirasinda hesapliyoruz.
   */
  const layout = useMemo(() => {
    let currentY = 0
    let maxWidth = 0

    const positioned = sections
      .slice()
      .sort((a, b) => a.displayOrder - b.displayOrder)
      .map((section) => {
        // Koltuklari SIRALARA grupla.
        //
        // Map kullaniyorum, duz nesne degil: Map ekleme sirasini KORUR.
        // Duz nesnede sayisal gorunumlu anahtarlar ("1", "2") otomatik
        // olarak siralanir ve "A, B, C" ile "1, 2, 10" karisik
        // davranislar gosterir.
        const rowMap = new Map<string, typeof section.seats>()

        for (const seat of section.seats) {
          const existing = rowMap.get(seat.rowLabel)

          if (existing) {
            existing.push(seat)
          } else {
            rowMap.set(seat.rowLabel, [seat])
          }
        }

        const rows = [...rowMap.entries()].map(([label, seats]) => ({
          label,
          seats: seats.slice().sort((a, b) => a.seatNumber - b.seatNumber),
        }))

        const sectionTop = currentY
        const rowCount = rows.length
        const maxSeatsInRow = Math.max(0, ...rows.map((r) => r.seats.length))

        const sectionWidth = ROW_LABEL_WIDTH + maxSeatsInRow * (SEAT_SIZE + SEAT_GAP)
        const sectionHeight = SECTION_TITLE_HEIGHT + rowCount * (SEAT_SIZE + SEAT_GAP)

        maxWidth = Math.max(maxWidth, sectionWidth)
        currentY += sectionHeight + SECTION_GAP

        return { section, rows, top: sectionTop }
      })

    return {
      sections: positioned,
      width: Math.max(maxWidth, 320),
      height: Math.max(currentY, 100),
    }
  }, [sections])

  if (sections.length === 0) {
    // PDF Sprint 18: "Empty state" zorunlu.
    // Bos bir alan gostermek yerine ne yapilmasi gerektigini soyluyoruz.
    return (
      <div className="rounded-xl border border-dashed border-slate-300 bg-slate-50 p-12 text-center">
        <p className="text-sm text-slate-500">
          Henuz bolum eklenmemis. Once bir bolum olusturun, sonra koltuk uretin.
        </p>
      </div>
    )
  }

  return (
    <div className="overflow-x-auto rounded-xl border border-slate-200 bg-white p-4">
      {/* SAHNE gostergesi: kullanicinin yonunu bulmasi icin.
          Koltuk haritasinda "on taraf neresi?" sorusu cevapsiz kalirsa
          kullanici hangi koltugun sahneye yakin oldugunu anlayamaz. */}
      <div className="mb-6 rounded-lg bg-slate-800 py-2 text-center text-xs font-medium tracking-widest text-white">
        SAHNE
      </div>

      <svg
        viewBox={`0 0 ${layout.width} ${layout.height}`}
        // width="100%" + viewBox = duyarli olcekleme.
        // Sabit piksel genisligi verseydik mobilde tasardi.
        width="100%"
        // height="auto" viewBox oranini korur.
        style={{ height: 'auto', maxHeight: '70vh' }}
        role="group"
        aria-label="Koltuk plani"
      >
        {layout.sections.map(({ section, rows, top }) => (
          <g key={section.id} transform={`translate(0, ${top})`}>
            <text
              x={0}
              y={14}
              className="fill-slate-700 text-[13px] font-semibold"
            >
              {section.name}
            </text>

            {rows.map((row, rowIndex) => {
              const y = SECTION_TITLE_HEIGHT + rowIndex * (SEAT_SIZE + SEAT_GAP)

              return (
                <g key={row.label}>
                  {/* Sira etiketi */}
                  <text
                    x={0}
                    y={y + SEAT_SIZE * 0.75}
                    className="fill-slate-400 text-[11px]"
                  >
                    {row.label}
                  </text>

                  {row.seats.map((seat, seatIndex) => {
                    const x = ROW_LABEL_WIDTH + seatIndex * (SEAT_SIZE + SEAT_GAP)
                    const isSelected = selectedSeatIds?.has(seat.id) ?? false

                    // Renk oncelik sirasi: secili > pasif > bolum rengi
                    const fill = isSelected
                      ? '#16a34a'
                      : seat.isActive
                        ? (section.colorHex ?? '#94a3b8')
                        : '#e2e8f0'

                    return (
                      <rect
                        key={seat.id}
                        x={x}
                        y={y}
                        width={SEAT_SIZE}
                        height={SEAT_SIZE}
                        rx={3}
                        fill={fill}
                        stroke={isSelected ? '#15803d' : 'transparent'}
                        strokeWidth={2}
                        className={
                          isInteractive && seat.isActive
                            ? 'cursor-pointer transition-opacity hover:opacity-70'
                            : ''
                        }
                        onClick={
                          isInteractive && seat.isActive
                            ? () => onSeatClick?.(seat.id)
                            : undefined
                        }
                        // Klavye erisimi: yalnizca tiklanabilir koltuklar
                        // odaklanabilir olmali. Salt okunur haritada
                        // 2000 koltugun arasinda Tab ile gezinmek
                        // iskence olurdu.
                        tabIndex={isInteractive && seat.isActive ? 0 : undefined}
                        role={isInteractive ? 'button' : undefined}
                        aria-pressed={isInteractive ? isSelected : undefined}
                        onKeyDown={
                          isInteractive && seat.isActive
                            ? (e) => {
                                // Enter ve Space, buton davranisinin
                                // standardi. Yalnizca onClick koysaydik
                                // klavye kullanicisi koltuk secemezdi.
                                if (e.key === 'Enter' || e.key === ' ') {
                                  e.preventDefault()
                                  onSeatClick?.(seat.id)
                                }
                              }
                            : undefined
                        }
                      >
                        {/* Ekran okuyucu bunu okur. SVG icindeki <title>
                            ayni zamanda fare uzerine gelince ipucu
                            (tooltip) olarak da gorunur. */}
                        <title>
                          {section.name} - {seat.displayLabel}
                          {seat.isActive ? '' : ' (devre disi)'}
                        </title>
                      </rect>
                    )
                  })}
                </g>
              )
            })}
          </g>
        ))}
      </svg>

      {/* Gosterge (legend) */}
      <div className="mt-4 flex flex-wrap gap-4 text-xs text-slate-600">
        {sections.map((s) => (
          <span key={s.id} className="inline-flex items-center gap-1.5">
            <span
              className="h-3 w-3 rounded-sm"
              style={{ backgroundColor: s.colorHex ?? '#94a3b8' }}
              aria-hidden="true"
            />
            {s.name} ({s.seatCount})
          </span>
        ))}

        <span className="inline-flex items-center gap-1.5">
          <span className="h-3 w-3 rounded-sm bg-slate-200" aria-hidden="true" />
          Devre disi
        </span>
      </div>
    </div>
  )
}
