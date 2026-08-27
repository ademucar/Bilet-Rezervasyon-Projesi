import { useState } from 'react'
import { useQuery, keepPreviousData } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { SiteHeader } from '../../../components/layout/SiteHeader'
import { Alert } from '../../../components/ui/Alert'
import { Button } from '../../../components/ui/Button'
import { Input } from '../../../components/ui/Input'
import { toProblem } from '../../../lib/api/client'
import { formatDateTime } from '../../../lib/format'
import { bookingApi, EventStatus } from '../api/bookingApi'

/**
 * Etkinlik listesi.
 *
 * PDF Sprint 11 gelismis arama ve filtreleri (kategori, tarih araligi,
 * fiyat, Redis onbellegi) getirecek. Burada YALNIZCA metin aramasi ve
 * sayfalama var -- bilet alma akisina girebilmek icin gereken kadari.
 *
 * Ekrani simdiden asiri tasarlamiyorum; Sprint 11'de zaten
 * degisecek.
 */
export function EventsPage() {
  const [search, setSearch] = useState('')
  const [appliedSearch, setAppliedSearch] = useState('')
  const [page, setPage] = useState(1)

  const eventsQuery = useQuery({
    // queryKey'e appliedSearch ve page DAHIL.
    //
    // Bu sadece bir "isim" degil, ONBELLEK ANAHTARI: anahtar
    // degisince TanStack Query yeni veri cekiyor, ayni anahtara
    // geri donuldugunde onbellekten aninda gosteriyor.
    // Anahtara koymasaydik 2. sayfaya gecince 1. sayfanin verisi
    // ekranda kalirdi.
    queryKey: ['events', appliedSearch, page],
    queryFn: () =>
      bookingApi.getEvents({
        search: appliedSearch || undefined,
        pageNumber: page,
      }),

    // Sayfa degisirken eski veriyi ekranda TUT.
    //
    // Olmasaydi her sayfa gecisinde liste bosalip yukleniyor
    // iskeletine donerdi; ekran "ziplardi". keepPreviousData ile
    // eski liste solgunlasarak yerinde kaliyor.
    placeholderData: keepPreviousData,
  })

  const onSearch = (e: React.FormEvent) => {
    e.preventDefault()

    // Arama degisince 1. sayfaya DONMEK zorundayiz.
    // Yoksa 5. sayfadayken arama yapan kullanici bos sonuc gorur
    // ve "arama bozuk" saniir.
    setPage(1)
    setAppliedSearch(search.trim())
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
              placeholder="Etkinlik adi"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
            />
          </div>

          <div className="flex items-end">
            <Button type="submit">Ara</Button>
          </div>
        </form>

        {eventsQuery.isError && (
          <div className="mt-6">
            <Alert variant="error">{toProblem(eventsQuery.error).detail}</Alert>
          </div>
        )}

        {eventsQuery.isPending && (
          // PDF Sprint 18: "Skeleton loading".
          // Donen bir carktan daha iyi: sayfanin gelecek yapisini
          // gosterdigi icin bekleme daha kisa hissettiriyor.
          <div className="mt-6 grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
            {[1, 2, 3, 4, 5, 6].map((i) => (
              <div key={i} className="h-40 animate-pulse rounded-2xl bg-slate-200" />
            ))}
          </div>
        )}

        {data && data.items.length === 0 && (
          <div className="mt-6 rounded-2xl border border-dashed border-slate-300 bg-white p-12 text-center">
            <p className="text-sm text-slate-500">
              {appliedSearch
                ? `"${appliedSearch}" icin sonuc bulunamadi.`
                : 'Henuz yayinlanmis etkinlik yok.'}
            </p>
          </div>
        )}

        {data && data.items.length > 0 && (
          <>
            <ul className="mt-6 grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
              {data.items.map((ev) => (
                <li key={ev.id}>
                  <Link
                    to={`/etkinlikler/${ev.id}`}
                    className="block h-full rounded-2xl border border-slate-200 bg-white p-5 shadow-sm transition-shadow hover:shadow-md"
                  >
                    <div className="flex items-start justify-between gap-2">
                      <h2 className="font-semibold text-slate-900">{ev.title}</h2>

                      {ev.status === EventStatus.SalesOpen && (
                        <span className="shrink-0 rounded-full bg-emerald-50 px-2 py-0.5 text-xs font-medium text-emerald-700">
                          Satista
                        </span>
                      )}
                    </div>

                    <p className="mt-2 text-sm text-slate-500">
                      {ev.venueName} - {ev.cityName}
                    </p>
                    <p className="mt-1 text-sm text-slate-500">{formatDateTime(ev.eventDate)}</p>

                    <div className="mt-3 flex flex-wrap gap-2 text-xs text-slate-500">
                      <span className="rounded bg-slate-100 px-2 py-0.5">{ev.categoryName}</span>
                      <span className="rounded bg-slate-100 px-2 py-0.5">
                        {ev.sessionCount} oturum
                      </span>
                      {ev.minimumAge > 0 && (
                        <span className="rounded bg-amber-50 px-2 py-0.5 text-amber-700">
                          {ev.minimumAge}+
                        </span>
                      )}
                    </div>
                  </Link>
                </li>
              ))}
            </ul>

            <div className="mt-8 flex items-center justify-between">
              <Button
                variant="secondary"
                disabled={!data.hasPreviousPage}
                onClick={() => setPage((p) => p - 1)}
              >
                Onceki
              </Button>

              <span className="text-sm text-slate-500">
                Sayfa {data.pageNumber} / {data.totalPages} ({data.totalCount} etkinlik)
              </span>

              <Button
                variant="secondary"
                disabled={!data.hasNextPage}
                onClick={() => setPage((p) => p + 1)}
              >
                Sonraki
              </Button>
            </div>
          </>
        )}
      </main>
    </div>
  )
}
