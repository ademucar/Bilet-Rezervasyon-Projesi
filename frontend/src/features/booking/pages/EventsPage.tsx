import { useCallback, useMemo, useState } from 'react'
import { useQuery, keepPreviousData } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { SiteHeader } from '../../../components/layout/SiteHeader'
import { Alert } from '../../../components/ui/Alert'
import { Button } from '../../../components/ui/Button'
import { Input } from '../../../components/ui/Input'
import { toProblem } from '../../../lib/api/client'
import { formatDateTime } from '../../../lib/format'
import { EventFilterPanel } from '../components/EventFilterPanel'
import { bookingApi, EventStatus, type EventFilters, type EventListItem } from '../api/bookingApi'

/**
 * ==================================================================
 * ETKINLIK LISTESI -- PDF Sprint 11
 * ==================================================================
 * Sprint 7'de bu sayfa yalnizca metin aramasi ve sayfalama
 * yapiyordu ve su notu birakmistim:
 *
 *   "PDF Sprint 11 gelismis arama ve filtreleri getirecek.
 *    Ekrani simdiden asiri tasarlamiyorum."
 *
 * Sprint 11 geldi: sekiz filtre, siralama ve populer etkinlikler.
 * ==================================================================
 */
export function EventsPage() {
  const [search, setSearch] = useState('')

  /**
   * ----------------------------------------------------------------
   * NEDEN TEK BIR `filters` NESNESI?
   * ----------------------------------------------------------------
   * Her filtre icin ayri useState acabilirdim: cityId, categoryId,
   * minPrice, maxPrice, dateFrom, dateTo, sortBy... on tane state.
   *
   * Sorun sayfalama ile ortaya cikardi: filtre DEGISTIGINDE sayfayi
   * 1'e dondurmek zorundayiz. Ayri state'lerde bunu ON AYRI onChange
   * icinde tekrarlamak gerekirdi ve birinde unutmak kacinilmazdi --
   * kullanici 5. sayfadayken filtre degistirir, bos sonuc gorur ve
   * "arama bozuk" der.
   *
   * Tek nesne + tek guncelleme fonksiyonu ile bu kurali TEK YERDE
   * uyguluyorum (bkz. updateFilters).
   * ----------------------------------------------------------------
   */
  const [filters, setFilters] = useState<EventFilters>({
    pageNumber: 1,
    pageSize: 9,
    sortBy: 'date',
    sortDirection: 'asc',
  })

  const updateFilters = useCallback((degisiklik: Partial<EventFilters>) => {
    setFilters((onceki) => ({
      ...onceki,
      ...degisiklik,

      // Filtre degisince HER ZAMAN 1. sayfaya don.
      //
      // Tek istisna: degisiklik zaten sayfa numarasi ise (kullanici
      // "Sonraki"ye basmis). O zaman gelen degeri koruyoruz.
      pageNumber: degisiklik.pageNumber ?? 1,
    }))
  }, [])

  const resetFilters = useCallback(() => {
    setSearch('')
    setFilters({ pageNumber: 1, pageSize: 9, sortBy: 'date', sortDirection: 'asc' })
  }, [])

  /**
   * Kac filtre aktif?
   *
   * Sayfalama ve siralama alanlarini SAYMIYORUM: onlar her zaman
   * dolu. Saysaydik rozet hicbir filtre secilmemisken bile
   * "4 aktif" yazardi ve anlamsizlasirdi.
   */
  const activeCount = useMemo(() => {
    const sayilmayanlar = new Set(['pageNumber', 'pageSize', 'sortBy', 'sortDirection'])

    return Object.entries(filters).filter(
      ([anahtar, deger]) => !sayilmayanlar.has(anahtar) && deger !== undefined && deger !== '',
    ).length
  }, [filters])

  const eventsQuery = useQuery({
    // queryKey'e TUM filtreler dahil.
    //
    // Bu sadece bir isim degil, ONBELLEK ANAHTARI: filtre degisince
    // yeni veri cekiliyor, ayni filtreye geri donuldugunde onbellekten
    // aninda gosteriliyor.
    queryKey: ['events', filters],
    queryFn: () => bookingApi.getEvents(filters),

    // Sayfa/filtre degisirken eski veriyi ekranda TUT.
    // Olmasaydi her degisiklikte liste bosalip iskelete donerdi.
    placeholderData: keepPreviousData,
  })

  // ================================================================
  // POPULER ETKINLIKLER -- PDF Sprint 11 (Redis'te 10 dakika)
  // ================================================================
  // YALNIZCA filtresiz gorunumde gosteriliyor.
  //
  // Kullanici filtre uyguladiginda ne aradigini biliyor; ustune
  // alakasiz "populer" onerileri koymak ekrani kalabaliklastirir ve
  // gercek sonuclari asagi iter.
  // ================================================================
  const filtresizMi = activeCount === 0 && !search

  const popularQuery = useQuery({
    queryKey: ['events', 'popular'],
    queryFn: () => bookingApi.getPopularEvents(4),
    enabled: filtresizMi,

    // 5 dakika: sunucudaki Redis suresi (10 dk) ile uyumlu.
    // Daha kisa vermek, sunucunun zaten onbellekledigi veriyi
    // gereksiz yere tekrar istemek olurdu.
    staleTime: 5 * 60 * 1000,
  })

  const onSearch = (e: React.FormEvent) => {
    e.preventDefault()
    updateFilters({ search: search.trim() || undefined })
  }

  const data = eventsQuery.data

  return (
    <div className="min-h-screen bg-slate-50">
      <SiteHeader />

      <main className="mx-auto max-w-6xl px-4 py-8">
        <h1 className="text-2xl font-bold text-slate-900">Etkinlikler</h1>
        <p className="mt-1 text-sm text-slate-500">
          Bir etkinlik secin, oturumunu belirleyin ve koltugunuzu ayirtin.
        </p>

        <form onSubmit={onSearch} className="mt-6 flex flex-wrap gap-3">
          <div className="min-w-64 flex-1">
            <Input
              label="Ara"
              placeholder="Etkinlik adi veya aciklamasi"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
            />
          </div>

          <div className="flex items-end">
            <Button type="submit">Ara</Button>
          </div>
        </form>

        {/* ---- POPULER ETKINLIKLER ---- */}
        {filtresizMi && popularQuery.data && popularQuery.data.length > 0 && (
          <section className="mt-8">
            <h2 className="text-lg font-semibold text-slate-900">Populer etkinlikler</h2>
            <p className="mt-0.5 text-xs text-slate-500">En cok bilet satilanlar</p>

            <ul className="mt-3 grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
              {popularQuery.data.map((ev, sira) => (
                <li key={ev.id}>
                  <Link
                    to={`/etkinlikler/${ev.id}`}
                    className="flex h-full items-start gap-3 rounded-xl border border-amber-200 bg-amber-50/60 p-4 transition-shadow hover:shadow-md"
                  >
                    <span
                      className="flex h-7 w-7 shrink-0 items-center justify-center rounded-full bg-amber-500 text-sm font-bold text-white"
                      aria-hidden="true"
                    >
                      {sira + 1}
                    </span>
                    <div className="min-w-0">
                      <p className="truncate font-medium text-slate-900">{ev.title}</p>
                      <p className="mt-1 truncate text-xs text-slate-500">{ev.venueName}</p>
                      <p className="text-xs text-slate-500">{formatDateTime(ev.eventDate)}</p>
                    </div>
                  </Link>
                </li>
              ))}
            </ul>
          </section>
        )}

        <div className="mt-8 grid gap-6 lg:grid-cols-[260px_1fr]">
          {/* ---- FILTRE PANELI ---- */}
          <div className="lg:sticky lg:top-6 lg:self-start">
            <EventFilterPanel
              filters={filters}
              onChange={updateFilters}
              onReset={resetFilters}
              activeCount={activeCount}
            />
          </div>

          {/* ---- SONUCLAR ---- */}
          <div>
            {eventsQuery.isError && (
              <Alert variant="error">{toProblem(eventsQuery.error).detail}</Alert>
            )}

            {eventsQuery.isPending && (
              // PDF Sprint 18: "Skeleton loading".
              <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
                {[1, 2, 3, 4, 5, 6].map((i) => (
                  <div key={i} className="h-40 animate-pulse rounded-2xl bg-slate-200" />
                ))}
              </div>
            )}

            {data && data.items.length === 0 && (
              <div className="rounded-2xl border border-dashed border-slate-300 bg-white p-12 text-center">
                <p className="text-sm text-slate-500">Bu kriterlere uyan etkinlik bulunamadi.</p>

                {/* Bos sonucta CIKIS YOLU gosteriyorum.
                    Yoksa kullanici hangi filtrenin sonucu bosalttigini
                    aramak zorunda kalir. */}
                {(activeCount > 0 || search) && (
                  <button
                    type="button"
                    onClick={resetFilters}
                    className="mt-3 text-sm font-medium text-brand-600 hover:underline"
                  >
                    Filtreleri temizle
                  </button>
                )}
              </div>
            )}

            {data && data.items.length > 0 && (
              <>
                <p className="mb-3 text-sm text-slate-500">{data.totalCount} etkinlik bulundu</p>

                <ul className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
                  {data.items.map((ev) => (
                    <li key={ev.id}>
                      <EventCard event={ev} />
                    </li>
                  ))}
                </ul>

                <div className="mt-8 flex items-center justify-between gap-3">
                  <Button
                    variant="secondary"
                    disabled={!data.hasPreviousPage}
                    onClick={() => updateFilters({ pageNumber: data.pageNumber - 1 })}
                  >
                    Onceki
                  </Button>

                  <span className="text-sm text-slate-500">
                    Sayfa {data.pageNumber} / {data.totalPages}
                  </span>

                  <Button
                    variant="secondary"
                    disabled={!data.hasNextPage}
                    onClick={() => updateFilters({ pageNumber: data.pageNumber + 1 })}
                  >
                    Sonraki
                  </Button>
                </div>
              </>
            )}
          </div>
        </div>
      </main>
    </div>
  )
}

function EventCard({ event }: { event: EventListItem }) {
  return (
    <Link
      to={`/etkinlikler/${event.id}`}
      className="block h-full rounded-2xl border border-slate-200 bg-white p-5 shadow-sm transition-shadow hover:shadow-md"
    >
      <div className="flex items-start justify-between gap-2">
        <h3 className="font-semibold text-slate-900">{event.title}</h3>

        {event.status === EventStatus.SalesOpen && (
          <span className="shrink-0 rounded-full bg-emerald-50 px-2 py-0.5 text-xs font-medium text-emerald-700">
            Satista
          </span>
        )}
      </div>

      <p className="mt-2 text-sm text-slate-500">
        {event.venueName} - {event.cityName}
      </p>
      <p className="mt-1 text-sm text-slate-500">{formatDateTime(event.eventDate)}</p>

      <div className="mt-3 flex flex-wrap gap-2 text-xs text-slate-500">
        <span className="rounded bg-slate-100 px-2 py-0.5">{event.categoryName}</span>
        <span className="rounded bg-slate-100 px-2 py-0.5">{event.sessionCount} oturum</span>
        {event.minimumAge > 0 && (
          <span className="rounded bg-amber-50 px-2 py-0.5 text-amber-700">
            {event.minimumAge}+
          </span>
        )}
      </div>
    </Link>
  )
}
