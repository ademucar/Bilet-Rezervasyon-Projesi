import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { SiteHeader } from '../../../components/layout/SiteHeader'
import { Alert } from '../../../components/ui/Alert'
import { toProblem } from '../../../lib/api/client'
import { formatCountdown } from '../hooks/useCountdown'
import { formatDateTime, formatMoney } from '../../../lib/format'
import { bookingApi, ReservationStatus } from '../api/bookingApi'

const STATUS_LABELS: Record<number, { text: string; className: string }> = {
  [ReservationStatus.Pending]: { text: 'Oluşturuluyor', className: 'bg-slate-100 text-slate-600' },
  [ReservationStatus.Locked]: { text: 'Ödeme bekliyor', className: 'bg-amber-50 text-amber-700' },
  [ReservationStatus.PaymentPending]: {
    text: 'Ödeme sürüyor',
    className: 'bg-amber-50 text-amber-700',
  },
  [ReservationStatus.Confirmed]: { text: 'Onaylandı', className: 'bg-emerald-50 text-emerald-700' },
  [ReservationStatus.Expired]: { text: 'Süresi doldu', className: 'bg-slate-100 text-slate-600' },
  [ReservationStatus.Cancelled]: { text: 'İptal edildi', className: 'bg-red-50 text-red-700' },
  [ReservationStatus.Refunded]: { text: 'İade edildi', className: 'bg-amber-50 text-amber-700' },
}

/**
 * Rezervasyonlarim.
 *
 * Bu ekranin varlik sebebi somut: kullanıcı ödeme sayfasindayken
 * sekmeyi kapatirsa, koltukları HALA kilitli ama sayfaya donecek
 * bir bağlantısı kalmiyor. Buradan devam edebiliyor.
 */
export function MyReservationsPage() {
  const reservationsQuery = useQuery({
    queryKey: ['my-reservations'],
    queryFn: () => bookingApi.getMyReservations(),

    // Kalan süre burada da gösteriliyor; bayat veri yanıltıcı olur.
    staleTime: 0,
  })

  return (
    <div className="min-h-screen bg-slate-100">
      <SiteHeader />

      <main className="mx-auto max-w-4xl px-4 py-8">
        <h1 className="font-display text-2xl font-bold tracking-tight text-slate-900">
          Rezervasyonlarim
        </h1>

        {reservationsQuery.isError && (
          <div className="mt-6">
            <Alert variant="error">{toProblem(reservationsQuery.error).detail}</Alert>
          </div>
        )}

        {reservationsQuery.isPending && (
          <div className="mt-6 space-y-4">
            {[1, 2].map((i) => (
              <div key={i} className="h-32 animate-pulse rounded-[4px] bg-slate-200" />
            ))}
          </div>
        )}

        {reservationsQuery.data?.length === 0 && (
          <div className="mt-6 rounded-[4px] border border-slate-300 bg-white p-12 text-center">
            <p className="text-sm text-slate-500">Henüz rezervasyonunuz yok.</p>
            <Link
              to="/etkinlikler"
              className="mt-3 inline-block text-sm font-medium text-brand-600 hover:underline"
            >
              Etkinliklere göz at
            </Link>
          </div>
        )}

        <ul className="mt-6 space-y-4">
          {reservationsQuery.data?.map((r) => {
            const badge = STATUS_LABELS[r.status] ?? {
              text: 'Bilinmiyor',
              className: 'bg-slate-100 text-slate-600',
            }

            // Ödemeye devam edilebilir mi?
            //
            // Hem durum uygun olmalı hem de süre bitmemis olmalı.
            // Yalnızca duruma baksaydik, süresi dolmuş ama arka plan
            // isi (Sprint 9) henüz temizlememis bir rezervasyon için
            // "Ödemeye devam et" gosterirdik ve kullanıcı tıklayınca
            // hata alırdı.
            const canContinue =
              (r.status === ReservationStatus.Locked ||
                r.status === ReservationStatus.PaymentPending) &&
              r.remainingSeconds > 0

            return (
              <li key={r.id} className="rounded-[4px] border border-slate-300 bg-white p-5">
                <div className="flex flex-wrap items-start justify-between gap-3">
                  <div>
                    <h2 className="font-display font-semibold text-slate-900">{r.eventTitle}</h2>
                    <p className="mt-1 text-sm text-slate-500">
                      {formatDateTime(r.sessionStartDate)} &middot; {r.venueName}
                    </p>
                    <p className="mt-1 text-xs text-slate-400">
                      Kod: <span className="font-mono">{r.reservationCode}</span> &middot;{' '}
                      {r.items.length} koltuk
                    </p>
                  </div>

                  <span
                    className={`shrink-0 rounded-full px-2 py-0.5 text-xs font-medium ${badge.className}`}
                  >
                    {badge.text}
                  </span>
                </div>

                <div className="mt-4 flex flex-wrap items-center justify-between gap-3 border-t border-slate-100 pt-4">
                  <span className="font-display font-semibold text-slate-900">
                    {formatMoney(r.totalAmount, r.currency)}
                  </span>

                  {canContinue ? (
                    <div className="flex items-center gap-3">
                      <span className="font-mono text-sm text-amber-700">
                        {formatCountdown(r.remainingSeconds)}
                      </span>

                      <Link
                        to={`/rezervasyonlar/${r.id}`}
                        className="rounded-lg bg-brand-600 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-brand-700"
                      >
                        Ödemeye devam et
                      </Link>
                    </div>
                  ) : (
                    <Link
                      to={`/rezervasyonlar/${r.id}`}
                      className="text-sm font-medium text-brand-600 hover:underline"
                    >
                      Detay
                    </Link>
                  )}
                </div>
              </li>
            )
          })}
        </ul>
      </main>
    </div>
  )
}
