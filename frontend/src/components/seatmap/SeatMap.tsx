import { useMemo } from 'react'

/**
 * ==================================================================
 * PAYLASILAN KOLTUK HARITASI
 * ==================================================================
 * Bu bileşen ONCE admin panelinde (Sprint 4) yazildi ve yalnızca
 * adminApi'nin SectionDetail tipini taniyordu.
 *
 * Sprint 7'de bilet alma ekranini yazarken sorun çıktı: oradaki veri
 * SeatAvailabilityItem, alanlari bambaska ve koltuklarin 4 farklı
 * durumu var (boş / kilitli / satilmis / bloke).
 *
 * Onumde iki seçenek vardi:
 *
 *   A) Ikinci bir koltuk haritası yazmak
 *      -> 200 satirlik yerlesim hesabi KOPYALANIRDI. Bir hizalama
 *         hatasini duzeltince digerinde duzelmezdi. Klasik teknik borc.
 *
 *   B) Bileseni GENELLESTIRMEK  <-- SECILEN
 *      -> Bilesen artık "hangi API'den geldigini" bilmiyor. Yalnızca
 *         "sıra, numara, renk, tıklanabilir mi" biliyor.
 *
 * Cagiran taraflar kendi verilerini bu sade modele CEVIRIYOR. Boylece
 * renk kurallari (admin: bölüm rengi / bilet alma: durum rengi) her
 * ekranin kendi isi oluyor -- ki zaten oyle olmalı.
 * ==================================================================
 */

export interface SeatMapSeat {
  id: string
  rowLabel: string
  seatNumber: number
  /** Kullanıcıya gösterilecek etiket. Ornek: "A-12". */
  label: string
  /** Dolgu rengi. Renk kararini CAGIRAN verir; bileşen kural bilmez. */
  fill: string
  /** Tıklanabilir mi? Satilmis koltuk için false. */
  selectable: boolean
  /** Ekran okuyucu ve fare ipucu için ek açıklama. Ornek: "satıldı". */
  description?: string
}

export interface SeatMapSection {
  id: string
  name: string
  displayOrder: number
  seats: SeatMapSeat[]
}

export interface SeatMapLegendItem {
  label: string
  color: string
}

interface SeatMapProps {
  sections: SeatMapSection[]
  /** Koltuga tiklandiginda. Verilmezse harita salt okunur olur. */
  onSeatClick?: (seatId: string) => void
  /** Seçili koltuklar. Cerceve ve aria-pressed için kullanilir. */
  selectedSeatIds?: ReadonlySet<string>
  legend?: SeatMapLegendItem[]
  emptyMessage?: string
}

interface PositionedRow {
  label: string
  seats: SeatMapSeat[]
}

// Koltuk görsel sabitleri (SVG birimi).
const SEAT_SIZE = 18
const SEAT_GAP = 4
const ROW_LABEL_WIDTH = 28
const SECTION_GAP = 32
const SECTION_TITLE_HEIGHT = 24

/**
 * ------------------------------------------------------------------
 * NEDEN SVG? Neden div/CSS grid değil?
 * ------------------------------------------------------------------
 * 1) OLCEKLENEBILIRLIK: SVG vektoreldir. viewBox ile harita, kapsayici
 *    genisligine göre kendini olceklendirir -- mobilde de masaustunde
 *    de bozulmadan çalışır.
 *
 * 2) PERFORMANS: 2000 koltuk = 2000 DOM elemani. <div> ile her biri
 *    tam bir CSS kutu modeli hesaplamasi gerektirir. SVG <rect> çok
 *    daha hafiftir.
 *
 * ------------------------------------------------------------------
 * ERİŞİLEBİLİRLİK
 * ------------------------------------------------------------------
 * SVG varsayılan olarak ekran okuyuculara KAPALIDIR. Her koltuga
 * <title> ekliyoruz ve role="button" veriyoruz ki klavyeyle
 * gezilebilsin. PDF Sprint 18: "Keyboard navigation desteklenmelidir."
 * ------------------------------------------------------------------
 */
export function SeatMap({
  sections,
  onSeatClick,
  selectedSeatIds,
  legend,
  emptyMessage = 'Gösterilecek koltuk yok.',
}: SeatMapProps) {
  const isInteractive = Boolean(onSeatClick)

  /**
   * Yerlesim hesabi.
   *
   * useMemo ile SARILI çünkü bu hesap 2000 koltuk için binlerce
   * nesne uretiyor. Her render'da tekrar calissaydi (örneğin
   * kullanıcı bir koltuk sectiginde) arayüz gozle gorulur şekilde
   * takilirdi.
   *
   * Bagimlilik yalnızca `sections`: seçim değiştiginde yerlesim
   * DEGISMEZ, yalnızca renkler degisir.
   */
  const layout = useMemo(() => {
    let currentY = 0
    let maxWidth = 0

    const positioned: { section: SeatMapSection; rows: PositionedRow[]; top: number }[] = []

    const ordered = sections.slice().sort((a, b) => a.displayOrder - b.displayOrder)

    // Duz `for` dongusu kullanıyorum, `.map()` değil.
    //
    // Sebep: burada bir DONUSUM değil, BIRIKIM yapiyoruz -- her bölüm
    // bir oncekinin bittigi yerden başlıyor (currentY) ve en genis
    // bolumu ariyoruz (maxWidth). `.map()` içinde disaridaki
    // degiskenleri değiştirmek hem okuyucuyu yaniltir hem de
    // "render sırasında degisken atamasi" olarak lint uyarısı alır.
    for (const section of ordered) {
      // Koltukları SIRALARA grupla.
      //
      // Map kullanıyorum, duz nesne değil: Map ekleme sırasını KORUR.
      // Duz nesnede sayisal gorunumlu anahtarlar ("1", "2") otomatik
      // olarak siralanir ve "A, B, C" ile "1, 2, 10" karisik
      // davranislar gosterir.
      const rowMap = new Map<string, SeatMapSeat[]>()

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

      const maxSeatsInRow = Math.max(0, ...rows.map((r) => r.seats.length))

      const sectionWidth = ROW_LABEL_WIDTH + maxSeatsInRow * (SEAT_SIZE + SEAT_GAP)
      const sectionHeight = SECTION_TITLE_HEIGHT + rows.length * (SEAT_SIZE + SEAT_GAP)

      positioned.push({ section, rows, top: currentY })

      maxWidth = Math.max(maxWidth, sectionWidth)
      currentY += sectionHeight + SECTION_GAP
    }

    return {
      sections: positioned,
      width: Math.max(maxWidth, 320),
      height: Math.max(currentY, 100),
    }
  }, [sections])

  if (sections.length === 0) {
    // PDF Sprint 18: "Empty state" zorunlu.
    // Boş bir alan göstermek yerine ne olduğunu soyluyoruz.
    return (
      <div className="rounded-xl border border-dashed border-slate-300 bg-slate-50 p-12 text-center">
        <p className="text-sm text-slate-500">{emptyMessage}</p>
      </div>
    )
  }

  return (
    <div className="overflow-x-auto rounded-xl border border-slate-200 bg-white p-4">
      {/* SAHNE göstergesi: kullanıcının yönünü bulmasi için.
          Koltuk haritasında "on taraf neresi?" sorusu cevapsiz kalirsa
          kullanıcı hangi koltuğun sahneye yakın olduğunu anlayamaz. */}
      <div className="mb-6 rounded-lg bg-slate-800 py-2 text-center text-xs font-medium tracking-widest text-white">
        SAHNE
      </div>

      <svg
        viewBox={`0 0 ${layout.width} ${layout.height}`}
        // width="100%" + viewBox = duyarli olcekleme.
        // Sabit piksel genisligi verseydik mobilde tasardi.
        width="100%"
        style={{ height: 'auto', maxHeight: '70vh' }}
        role="group"
        aria-label="Koltuk planı"
      >
        {layout.sections.map(({ section, rows, top }) => (
          <g key={section.id} transform={`translate(0, ${top})`}>
            <text x={0} y={14} className="fill-slate-700 text-[13px] font-semibold">
              {section.name}
            </text>

            {rows.map((row, rowIndex) => {
              const y = SECTION_TITLE_HEIGHT + rowIndex * (SEAT_SIZE + SEAT_GAP)

              return (
                <g key={row.label}>
                  <text x={0} y={y + SEAT_SIZE * 0.75} className="fill-slate-400 text-[11px]">
                    {row.label}
                  </text>

                  {row.seats.map((seat, seatIndex) => {
                    const x = ROW_LABEL_WIDTH + seatIndex * (SEAT_SIZE + SEAT_GAP)
                    const isSelected = selectedSeatIds?.has(seat.id) ?? false
                    const clickable = isInteractive && seat.selectable

                    return (
                      <rect
                        key={seat.id}
                        x={x}
                        y={y}
                        width={SEAT_SIZE}
                        height={SEAT_SIZE}
                        rx={3}
                        fill={seat.fill}
                        stroke={isSelected ? '#15803d' : 'transparent'}
                        strokeWidth={2}
                        className={
                          clickable ? 'cursor-pointer transition-opacity hover:opacity-70' : ''
                        }
                        onClick={clickable ? () => onSeatClick?.(seat.id) : undefined}
                        // Klavye erişimi: yalnızca tıklanabilir koltuklar
                        // odaklanabilir olmalı. Salt okunur haritada
                        // 2000 koltuğun arasında Tab ile gezinmek
                        // iskence olurdu.
                        tabIndex={clickable ? 0 : undefined}
                        role={isInteractive ? 'button' : undefined}
                        aria-pressed={isInteractive ? isSelected : undefined}
                        aria-label={isInteractive ? `${section.name} ${seat.label}` : undefined}
                        onKeyDown={
                          clickable
                            ? (e) => {
                                // Enter ve Space, buton davranisinin
                                // standardi. Yalnızca onClick koysaydık
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
                            aynı zamanda fare ipucu olarak da görünür. */}
                        <title>
                          {section.name} - {seat.label}
                          {seat.description ? ` (${seat.description})` : ''}
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

      {legend && legend.length > 0 && (
        <div className="mt-4 flex flex-wrap gap-4 text-xs text-slate-600">
          {legend.map((item) => (
            <span key={item.label} className="inline-flex items-center gap-1.5">
              <span
                className="h-3 w-3 rounded-sm border border-slate-300"
                style={{ backgroundColor: item.color }}
                aria-hidden="true"
              />
              {item.label}
            </span>
          ))}
        </div>
      )}
    </div>
  )
}
