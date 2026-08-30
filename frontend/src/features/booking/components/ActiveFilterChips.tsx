import { useQuery } from '@tanstack/react-query'
import { bookingApi, EventStatus, type EventFilters } from '../api/bookingApi'
import { formatDate } from '../../../lib/format'

/**
 * Aktif filtre rozetleri
 *
 * Yan paneldeki filtreler sayfayı aşağı doğru uzatıyor ve kullanıcı
 * sonuçlara baktığında hangi filtrelerin açık olduğunu göremiyor --
 * özellikle mobilde, panel yukarıda kaldığında.
 *
 * Sonuç: "neden bu kadar az etkinlik var?" sorusunun cevabı ekranda
 * duruyor ama görünmüyor. Boş sonuç ekranında bunu yazmıştım; ama
 * asıl gerekli olan yer SONUÇ VARKEN.
 *
 * Rozetler iki iş yapıyor:
 *   1. Ne filtrelendiğini görünür kılıyor
 *   2. Her filtreyi TEK TIKLA kaldırılabilir yapıyor -- yan panelde
 *      doğru açılır listeyi bulup "Farketmez"e çekmek gerekmiyor
 *
 * Neden şehir/kategori isimlerini ayrica çekiyorum?
 *
 * Filtrede yalnızca cityId var (bir GUID). Rozette "Şehir:
 * 3fa85f64-..." yazamam. Listeler zaten EventFilterPanel tarafından
 * çekiliyor ve TanStack Query aynı queryKey ile ONBELLEKTEN
 * dönüyor -- yani bu iki useQuery ek ağ isteği yapmıyor.
 *
 * staleTime'ı da orayla aynı tuttum; farklı verseydim iki bileşen
 * sırayla birbirinin önbelleğini bayatlatırdı.
 *
 */

interface ActiveFilterChipsProps {
  filters: EventFilters
  onChange: (patch: Partial<EventFilters>) => void
  onReset: () => void
  /** Arama kutusundaki metin -- filtrelerin dışında tutuluyor. */
  search: string
  onClearSearch: () => void
  /** Sonuç sayısı. Sağ uçta, rozetlerle aynı satırda. */
  totalCount?: number
}

interface Rozet {
  anahtar: string
  metin: string
  kaldir: () => void
}

export function ActiveFilterChips({
  filters,
  onChange,
  onReset,
  search,
  onClearSearch,
  totalCount,
}: ActiveFilterChipsProps) {
  const citiesQuery = useQuery({
    queryKey: ['cities'],
    queryFn: bookingApi.getCities,
    staleTime: 60 * 60 * 1000,
  })

  const categoriesQuery = useQuery({
    queryKey: ['categories'],
    queryFn: bookingApi.getCategories,
    staleTime: 60 * 60 * 1000,
  })

  const rozetler: Rozet[] = []

  if (search) {
    rozetler.push({ anahtar: 'search', metin: `"${search}"`, kaldir: onClearSearch })
  }

  if (filters.cityId) {
    const ad = citiesQuery.data?.find((c) => c.id === filters.cityId)?.name
    rozetler.push({
      anahtar: 'cityId',
      // Ad henüz gelmediyse rozeti gizlemiyorum: "Şehir" yazıp
      // kaldırma düğmesini veriyorum. Rozetin yokmuş gibi görünüp
      // sonra aniden belirmesi, sayfayı zıplatırdı.
      metin: ad ? `Şehir: ${ad}` : 'Şehir',
      kaldir: () => onChange({ cityId: undefined }),
    })
  }

  if (filters.categoryId) {
    const ad = categoriesQuery.data?.find((c) => c.id === filters.categoryId)?.name
    rozetler.push({
      anahtar: 'categoryId',
      metin: ad ? `Kategori: ${ad}` : 'Kategori',
      kaldir: () => onChange({ categoryId: undefined }),
    })
  }

  if (filters.dateFrom || filters.dateTo) {
    // İki tarih tek rozette: "1 Eki - 30 Eki". Ayrı ayrı iki rozet
    // koysaydım kullanıcı birini kaldırıp diğerini unutur ve hâlâ
    // filtrelenmiş bir liste görürdü.
    const bas = filters.dateFrom ? formatDate(filters.dateFrom) : ''
    const bit = filters.dateTo ? formatDate(filters.dateTo) : ''
    rozetler.push({
      anahtar: 'date',
      metin: bas && bit ? `${bas} – ${bit}` : bas ? `${bas} sonrası` : `${bit} öncesi`,
      kaldir: () => onChange({ dateFrom: undefined, dateTo: undefined }),
    })
  }

  if (filters.minPrice !== undefined || filters.maxPrice !== undefined) {
    const alt = filters.minPrice ?? 0
    const ust = filters.maxPrice
    rozetler.push({
      anahtar: 'price',
      metin: ust !== undefined ? `${alt}–${ust} ₺` : `${alt} ₺ ve üzeri`,
      kaldir: () => onChange({ minPrice: undefined, maxPrice: undefined }),
    })
  }

  if (filters.maxMinimumAge !== undefined) {
    rozetler.push({
      anahtar: 'maxMinimumAge',
      metin: `${filters.maxMinimumAge} yaş ve altı`,
      kaldir: () => onChange({ maxMinimumAge: undefined }),
    })
  }

  if (filters.status !== undefined) {
    rozetler.push({
      anahtar: 'status',
      metin: filters.status === EventStatus.SalesOpen ? 'Yalnızca satışta' : 'Duruma göre',
      kaldir: () => onChange({ status: undefined }),
    })
  }

  if (filters.venueId) {
    rozetler.push({
      anahtar: 'venueId',
      metin: 'Mekan seçili',
      kaldir: () => onChange({ venueId: undefined }),
    })
  }

  if (rozetler.length === 0) {
    return null
  }

  return (
    <div className="mb-3 flex flex-wrap items-center gap-2 rounded-[4px] border border-slate-300 bg-white px-3 py-2.5">
      <span className="label-xs">
        <span className="num">{rozetler.length}</span> filtre aktif
      </span>

      {rozetler.map((r) => (
        <button
          key={r.anahtar}
          type="button"
          onClick={r.kaldir}
          className="inline-flex items-center gap-1.5 rounded-[4px] border border-brand-200 bg-brand-50 px-2 py-1 text-xs text-brand-700 hover:border-brand-500"
        >
          {r.metin}
          <svg
            className="size-3"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            strokeWidth="3"
            strokeLinecap="round"
            aria-hidden="true"
          >
            <path d="M18 6 6 18M6 6l12 12" />
          </svg>
          {/* Ekran okuyucu "İstanbul" yazan bir düğme duyduğunda ne
              olacağını bilemez. Görsel olarak gizli bu metin, düğmenin
              ne yaptığını söylüyor. */}
          <span className="sr-only">filtresini kaldır</span>
        </button>
      ))}

      <button
        type="button"
        onClick={() => {
          onClearSearch()
          onReset()
        }}
        className="px-1 text-xs text-slate-500 underline hover:text-slate-900"
      >
        Tümünü temizle
      </button>

      {totalCount !== undefined && (
        <span className="ml-auto text-xs text-slate-500">
          <span className="num text-slate-900">{totalCount}</span> etkinlik
        </span>
      )}
    </div>
  )
}
