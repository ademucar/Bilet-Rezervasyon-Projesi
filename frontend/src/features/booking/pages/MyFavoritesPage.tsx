import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { SiteHeader } from '../../../components/layout/SiteHeader'
import { Alert } from '../../../components/ui/Alert'
import { toProblem } from '../../../lib/api/client'
import { formatDateTime } from '../../../lib/format'
import { bookingApi, EventStatus } from '../api/bookingApi'

/**
 * Favorilerim. PDF Sprint 12: GET /api/v1/users/me/favorites
 */
export function MyFavoritesPage() {
  const favoritesQuery = useQuery({
    queryKey: ['favorites'],
    queryFn: bookingApi.getMyFavorites,
  })

  return (
    <div className="min-h-screen bg-slate-50">
      <SiteHeader />

      <main className="mx-auto max-w-4xl px-4 py-8">
        <h1 className="text-2xl font-bold text-slate-900">Favorilerim</h1>

        {favoritesQuery.isError && (
          <div className="mt-6">
            <Alert variant="error">{toProblem(favoritesQuery.error).detail}</Alert>
          </div>
        )}

        {favoritesQuery.isPending && (
          <div className="mt-6 grid gap-4 sm:grid-cols-2">
            {[1, 2].map((i) => (
              <div key={i} className="h-36 animate-pulse rounded-2xl bg-slate-200" />
            ))}
          </div>
        )}

        {favoritesQuery.data?.length === 0 && (
          <div className="mt-6 rounded-2xl border border-dashed border-slate-300 bg-white p-12 text-center">
            <p className="text-sm text-slate-500">
              Henüz favori etkinliğiniz yok. Etkinlik sayfasındaki kalp ikonuna dokunarak
              ekleyebilirsiniz.
            </p>
            <Link
              to="/etkinlikler"
              className="mt-3 inline-block text-sm font-medium text-brand-600 hover:underline"
            >
              Etkinliklere göz at
            </Link>
          </div>
        )}

        <ul className="mt-6 grid gap-4 sm:grid-cols-2">
          {favoritesQuery.data?.map((ev) => {
            // ==========================================================
            // İPTAL EDILMIS ETKİNLİK LISTEDEN CIKARILMIYOR
            // ==========================================================
            // Backend bunlari da döndürüyor (bilinçli). Kullanıcı
            // favoriledigi etkinliğin iptal edildigini GORMELI.
            //
            // Sessizce kaldirsaydik "favorim nereye gitti?" derdi ve
            // cevabini hiçbir yerde bulamazdi.
            // ==========================================================
            const iptalEdildi = ev.status === EventStatus.Cancelled
            const tamamlandi = ev.status === EventStatus.Completed

            return (
              <li key={ev.id}>
                <Link
                  to={`/etkinlikler/${ev.id}`}
                  className={`block h-full rounded-2xl border bg-white p-5 shadow-sm transition-shadow hover:shadow-md ${
                    iptalEdildi ? 'border-red-200' : 'border-slate-200'
                  }`}
                >
                  <div className="flex items-start justify-between gap-2">
                    <h2 className="font-semibold text-slate-900">{ev.title}</h2>

                    {iptalEdildi && (
                      <span className="shrink-0 rounded-full bg-red-50 px-2 py-0.5 text-xs font-medium text-red-700">
                        İptal edildi
                      </span>
                    )}

                    {tamamlandi && (
                      <span className="shrink-0 rounded-full bg-slate-100 px-2 py-0.5 text-xs font-medium text-slate-600">
                        Tamamlandı
                      </span>
                    )}

                    {ev.status === EventStatus.SalesOpen && (
                      <span className="shrink-0 rounded-full bg-emerald-50 px-2 py-0.5 text-xs font-medium text-emerald-700">
                        Satışta
                      </span>
                    )}
                  </div>

                  <p className="mt-2 text-sm text-slate-500">
                    {ev.venueName} - {ev.cityName}
                  </p>
                  <p className="mt-1 text-sm text-slate-500">{formatDateTime(ev.eventDate)}</p>

                  <div className="mt-3 flex flex-wrap gap-2 text-xs text-slate-500">
                    <span className="rounded bg-slate-100 px-2 py-0.5">{ev.categoryName}</span>
                  </div>
                </Link>
              </li>
            )
          })}
        </ul>
      </main>
    </div>
  )
}
