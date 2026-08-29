import { useMemo } from 'react'
import { SeatMap as BaseSeatMap, type SeatMapSection } from '../../../components/seatmap/SeatMap'
import type { SectionDetail } from '../api/adminApi'

interface SeatMapProps {
  sections: SectionDetail[]
  /** Koltuga tiklandiginda. Verilmezse harita salt okunur olur. */
  onSeatClick?: (seatId: string) => void
  /** Seçili koltuklarin Id'leri. */
  selectedSeatIds?: ReadonlySet<string>
}

const DEFAULT_SECTION_COLOR = '#94a3b8'
const SELECTED_COLOR = '#16a34a'
const INACTIVE_COLOR = '#e2e8f0'

/**
 * ==================================================================
 * ADMIN KOLTUK PLANI
 * ==================================================================
 * Sprint 4'te bu dosya koltuk haritasinin KENDISIYDI. Sprint 7'de
 * bilet alma ekrani da bir harita isteyince cizim mantigini
 * components/seatmap/SeatMap.tsx'e tasidim.
 *
 * Geriye kalan bu dosya artık yalnızca bir CEVIRICI: adminApi'nin
 * SectionDetail tipini, paylasilan bilesenin anladigi sade modele
 * doksturuyor.
 *
 * Admin sayfalarinin import satirlarina DOKUNMADIM. Onlar hâlâ
 * `import { SeatMap } from '../components/SeatMap'` diyor ve
 * calisiyorlar. Refactor'un doğru yapilmis olmasinin olcusu budur:
 * cagiran taraf degisikligi fark etmez.
 *
 * ------------------------------------------------------------------
 * ADMIN'DE RENK NE ANLAMA GELIR?
 * ------------------------------------------------------------------
 * Burada koltuğun SATIS durumu yok -- oturum bile secilmemis.
 * Renk yalnızca BOLUMU anlatiyor. Bilet alma ekraninda ise renk
 * "boş mu, kilitli mi, satilmis mi" demek.
 *
 * Iki ekranin renk kuralı farklı olduğu için renk secimini
 * paylasilan bilesene KOYMADIM; her ekran kendi kuralini yazıyor.
 * ==================================================================
 */
export function SeatMap({ sections, onSeatClick, selectedSeatIds }: SeatMapProps) {
  const mapped = useMemo<SeatMapSection[]>(
    () =>
      sections.map((section) => ({
        id: section.id,
        name: section.name,
        displayOrder: section.displayOrder,
        seats: section.seats.map((seat) => ({
          id: seat.id,
          rowLabel: seat.rowLabel,
          seatNumber: seat.seatNumber,
          label: seat.displayLabel,

          // Renk oncelik sırası: seçili > pasif > bölüm rengi
          fill: selectedSeatIds?.has(seat.id)
            ? SELECTED_COLOR
            : seat.isActive
              ? (section.colorHex ?? DEFAULT_SECTION_COLOR)
              : INACTIVE_COLOR,

          selectable: seat.isActive,
          description: seat.isActive ? undefined : 'devre dışı',
        })),
      })),
    [sections, selectedSeatIds],
  )

  const legend = useMemo(
    () => [
      ...sections.map((s) => ({
        label: `${s.name} (${s.seatCount})`,
        color: s.colorHex ?? DEFAULT_SECTION_COLOR,
      })),
      { label: 'Devre dışı', color: INACTIVE_COLOR },
    ],
    [sections],
  )

  return (
    <BaseSeatMap
      sections={mapped}
      onSeatClick={onSeatClick}
      selectedSeatIds={selectedSeatIds}
      legend={legend}
      emptyMessage="Henüz bölüm eklenmemiş. Önce bir bölüm oluşturun, sonra koltuk üretin."
    />
  )
}
