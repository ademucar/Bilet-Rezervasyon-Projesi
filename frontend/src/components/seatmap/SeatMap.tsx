import { useMemo } from 'react'

/**
 * Paylasilan koltuk haritasi
 *
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
 *
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
  /**
   * Haritanin zemini.
   *
   * Neden iki ton var?
   *
   * Bilet alma ekranında harita KOYU zeminde duruyor: salon
   * karanlıktır, koltuklar ışıklı okunur. Koyu zemin ayrıca
   * koltukları sayfanın geri kalanından ayırıyor -- göz "burası
   * harita" diye anlıyor.
   *
   * Ama admin tarafında bölüm renkleri KULLANICININ seçtiği renkler
   * (colorHex). Biri açık sarı bir bölüm tanımlarsa koyu zeminde
   * gözü alır, biri lacivert seçerse zemine karışır. Orada zemini
   * açık bırakmak zorundayız.
   *
   * Varsayılan 'light' -- yani bu prop'u vermeyen mevcut çağrılar
   * (admin) hiç değişmiyor.
   *
   */
  tone?: 'light' | 'dark'
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
 * NEDEN SVG? Neden div/CSS grid değil?
 *
 * 1) OLCEKLENEBILIRLIK: SVG vektoreldir. viewBox ile harita, kapsayici
 *    genisligine göre kendini olceklendirir -- mobilde de masaustunde
 *    de bozulmadan çalışır.
 *
 * 2) PERFORMANS: 2000 koltuk = 2000 DOM elemani. <div> ile her biri
 *    tam bir CSS kutu modeli hesaplamasi gerektirir. SVG <rect> çok
 *    daha hafiftir.
 *
 * Erişilebilirlik
 *
 * SVG varsayılan olarak ekran okuyuculara KAPALIDIR. Her koltuga
 * <title> ekliyorum ve role="button" veriyorum ki klavyeyle
 * gezilebilsin. PDF Sprint 18: "Keyboard navigation desteklenmelidir."
 *
 */
export function SeatMap({
  sections,
  onSeatClick,
  selectedSeatIds,
  legend,
  emptyMessage = 'Gösterilecek koltuk yok.',
  tone = 'light',
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
    // Sebep: burada bir DONUSUM değil, BIRIKIM yapiyorum -- her bölüm
    // bir oncekinin bittigi yerden başlıyor (currentY) ve en genis
    // bolumu ariyorum (maxWidth). `.map()` içinde disaridaki
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
    // Boş bir alan göstermek yerine ne olduğunu soyluyorum.
    return (
      <div className="rounded-[4px] border border-slate-300 bg-white p-10 text-center">
        <p className="text-sm text-slate-500">{emptyMessage}</p>
      </div>
    )
  }

  const koyu = tone === 'dark'

  return (
    <div
      className={`overflow-x-auto rounded-[4px] border p-5 ${
        koyu ? 'border-slate-800 bg-slate-900' : 'border-slate-300 bg-white'
      }`}
    >
      {/*
          SAHNE -- düz çubuk değil, YAY
          Kullanıcının yönünü bulması için: koltuk haritasında "ön
          taraf neresi?" sorusu cevapsız kalırsa hangi koltuğun
          sahneye yakın olduğu anlaşılmaz.

          Önceki hâl "SAHNE" yazan dolu bir dikdörtgendi ve koltuk
          sıralarıyla aynı görsel ağırlıktaydı -- uzaktan bakınca
          bir koltuk sırası gibi görünüyordu.

          Yay, gerçek bir salonun perspektifini taklit ediyor:
          seyirci sahneyi çevreliyor. Tek bir kenarlık çizgisi
          olduğu için de hiçbir koltukla karışmıyor.
          */}
      <div className="mb-6 flex justify-center">
        <div
          className={`flex h-8 w-[min(300px,70%)] justify-center rounded-t-full border-t-2 pt-1.5 ${
            koyu ? 'border-slate-600' : 'border-slate-400'
          }`}
        >
          <span className={`label-xs ${koyu ? 'text-slate-500' : 'text-slate-400'}`}>Sahne</span>
        </div>
      </div>

      <svg
        viewBox={`0 0 ${layout.width} ${layout.height}`}
        // width="100%" + viewBox = duyarli olcekleme.
        // Sabit piksel genisligi verseydim mobilde tasardi.
        width="100%"
        style={{ height: 'auto', maxHeight: '70vh' }}
        role="group"
        aria-label="Koltuk planı"
      >
        {layout.sections.map(({ section, rows, top }) => (
          <g key={section.id} transform={`translate(0, ${top})`}>
            <text
              x={0}
              y={14}
              className={`text-[13px] font-semibold ${koyu ? 'fill-slate-300' : 'fill-slate-700'}`}
            >
              {section.name}
            </text>

            {rows.map((row, rowIndex) => {
              const y = SECTION_TITLE_HEIGHT + rowIndex * (SEAT_SIZE + SEAT_GAP)

              return (
                <g key={row.label}>
                  <text
                    x={0}
                    y={y + SEAT_SIZE * 0.75}
                    className={`text-[11px] ${koyu ? 'fill-slate-500' : 'fill-slate-400'}`}
                  >
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
                        rx={2}
                        fill={seat.fill}
                        // Seçili koltuğa halka: rengi göremeyen kullanıcı
                        // (ve koyu zeminde renk körü olan) için ikinci
                        // bir işaret. Renk tek başına asla yeterli değil.
                        stroke={isSelected ? (koyu ? '#a5b4fc' : '#15803d') : 'transparent'}
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
                                // standardi. Yalnızca onClick koysaydım
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
        <div
          className={`mt-6 flex flex-wrap gap-4 border-t pt-3.5 text-xs ${
            koyu ? 'border-slate-800 text-slate-300' : 'border-slate-200 text-slate-600'
          }`}
        >
          {legend.map((item) => (
            <span key={item.label} className="inline-flex items-center gap-2">
              <span
                className={`size-3 border ${koyu ? 'border-slate-600' : 'border-slate-300'}`}
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
