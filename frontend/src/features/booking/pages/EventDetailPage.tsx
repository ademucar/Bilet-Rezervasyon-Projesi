import { useQuery } from '@tanstack/react-query'
import { Link, useParams } from 'react-router-dom'
import { SiteHeader } from '../../../components/layout/SiteHeader'
import { Alert } from '../../../components/ui/Alert'
import { EventReviews } from '../components/EventReviews'
import { FavoriteButton } from '../components/FavoriteButton'
import { toProblem } from '../../../lib/api/client'
import { formatDateTime } from '../../../lib/format'
import { bookingApi, EventStatus } from '../api/bookingApi'

/**
 * Etkinlik detayı ve OTURUM seçimi.
 *
 * Bir etkinliğin birden fazla oturumu olabilir (aynı tiyatro oyunu
 * Cuma 20:00 ve Cumartesi 15:00). Koltuk her OTURUM için ayrı
 * uretildiginden, kullanıcının önce oturumu secmesi gerekiyor.
 */
export function EventDetailPage() {
  const { eventId = '' } = useParams()

  const eventQuery = useQuery({
    queryKey: ['event', eventId],
    queryFn: () => bookingApi.getEvent(eventId),
    // eventId boş gelirse (bozuk adres) isteği hiç gonderme.
    enabled: eventId.length > 0,
  })

  if (eventQuery.isPending) {
    return (
      <div className="min-h-screen bg-slate-100">
        <SiteHeader />
        <div className="mx-auto max-w-4xl px-4 py-8">
          <div className="h-64 animate-pulse rounded-[4px] bg-slate-200" />
        </div>
      </div>
    )
  }

  if (eventQuery.isError || !eventQuery.data) {
    return (
      <div className="min-h-screen bg-slate-100">
        <SiteHeader />
        <div className="mx-auto max-w-4xl px-4 py-8">
          <Alert variant="error">{toProblem(eventQuery.error).detail}</Alert>
        </div>
      </div>
    )
  }

  const ev = eventQuery.data

  // SATIS ACIK MI?
  //
  // Bu kontrol yalnızca KULLANICI DENEYİMİ için.
  //
  // Satış kapaliyken koltuk seçim baglantisini gizlemek, kullanıcının
  // 10 koltuk seçip en sonda "satış kapalı" hatası almasini onluyor.
  //
  // GÜVENLİK değil: adresi elle yazan biri yine koltuk seçim
  // sayfasına girebilir. Gerçek kontrol backend'de
  // CreateReservationCommand içinde -- oradaki kontrol kaldirilirsa
  // sistem açık olur, buradaki kaldirilirsa yalnızca deneyim bozulur.
  const isOnSale = ev.status === EventStatus.SalesOpen
  const isCancelled = ev.status === EventStatus.Cancelled

  return (
    <div className="min-h-screen bg-slate-100">
      <SiteHeader />

      <main className="mx-auto max-w-4xl px-4 py-8">
        <Link to="/etkinlikler" className="text-sm text-brand-600 hover:underline">
          &larr; Etkinlikler
        </Link>

        <div className="mt-4 flex flex-wrap items-start justify-between gap-3">
          <div>
            <h1 className="font-display text-2xl font-bold tracking-tight text-slate-900">
              {ev.title}
            </h1>
            <p className="mt-1 text-sm text-slate-500">
              {ev.venueName} - {ev.venueAddress}, {ev.cityName}
            </p>
          </div>

          {/* PDF Sprint 12: favori dugmesi */}
          <FavoriteButton eventId={ev.id} />
        </div>

        {isCancelled && (
          <div className="mt-4">
            <Alert variant="error">
              Bu etkinlik iptal edildi.
              {ev.cancellationReason ? ` Sebep: ${ev.cancellationReason}` : ''}
            </Alert>
          </div>
        )}

        {!isOnSale && !isCancelled && (
          <div className="mt-4">
            <Alert variant="info">
              Bu etkinlik için bilet satışı su anda kapalı. Satış{' '}
              {formatDateTime(ev.salesStartDate)} tarihinde açılıyor.
            </Alert>
          </div>
        )}

        <section className="mt-6 rounded-[4px] border border-slate-300 bg-white p-6">
          <p className="whitespace-pre-line text-sm leading-relaxed text-slate-700">
            {ev.description}
          </p>

          <dl className="mt-6 grid gap-4 text-sm sm:grid-cols-2">
            <div>
              <dt className="text-slate-500">Kategori</dt>
              <dd className="font-medium text-slate-900">{ev.categoryName}</dd>
            </div>
            <div>
              <dt className="text-slate-500">Organizatör</dt>
              <dd className="font-medium text-slate-900">{ev.organizerName}</dd>
            </div>
            <div>
              <dt className="text-slate-500">Süre</dt>
              <dd className="font-medium text-slate-900">{ev.durationMinutes} dakika</dd>
            </div>
            <div>
              <dt className="text-slate-500">Yaş sınırı</dt>
              <dd className="font-medium text-slate-900">
                {ev.minimumAge > 0 ? `${ev.minimumAge} yaş ve üzeri` : 'Yok'}
              </dd>
            </div>
            <div>
              <dt className="text-slate-500">Kişi başı bilet limiti</dt>
              <dd className="font-medium text-slate-900">{ev.maxTicketsPerUser} bilet</dd>
            </div>
            <div>
              <dt className="text-slate-500">Satış bitiş</dt>
              <dd className="font-medium text-slate-900">{formatDateTime(ev.salesEndDate)}</dd>
            </div>
          </dl>
        </section>

        <section className="mt-6">
          <h2 className="font-display text-lg font-semibold text-slate-900">Oturumlar</h2>

          {ev.sessions.length === 0 ? (
            <div className="mt-3 rounded-[4px] border border-slate-300 bg-white p-8 text-center text-sm text-slate-500">
              Bu etkinlik için henüz oturum tanımlanmamış.
            </div>
          ) : (
            <ul className="mt-3 space-y-3">
              {ev.sessions.map((session) => {
                // Koltuklar URETILMEMISSE rezervasyon imkansizdir:
                // secilecek EventSeat kaydı yok. Organizatorun
                // "koltukları üret" adimini atlamis olmasını
                // kullanıcının karsisina boş bir harita olarak
                // cikmasin diye burada aciklikla soyluyorum.
                const canBook = isOnSale && session.areSeatsGenerated

                return (
                  <li
                    key={session.id}
                    className="flex flex-wrap items-center justify-between gap-3 rounded-[4px] border border-slate-300 bg-white p-4"
                  >
                    <div>
                      <p className="font-medium text-slate-900">
                        {formatDateTime(session.startDate)}
                      </p>
                      <p className="text-sm text-slate-500">{session.hallName}</p>
                    </div>

                    {canBook ? (
                      <Link
                        to={`/oturumlar/${session.id}/koltuklar`}
                        className="rounded-lg bg-brand-600 px-4 py-2.5 text-sm font-medium text-white transition-colors hover:bg-brand-700"
                      >
                        Koltuk seç
                      </Link>
                    ) : (
                      <span className="text-sm text-slate-400">
                        {session.areSeatsGenerated ? 'Satış kapalı' : 'Koltuklar hazırlanmadı'}
                      </span>
                    )}
                  </li>
                )
              })}
            </ul>
          )}
        </section>

        {/* PDF Sprint 12: yorumlar ve puanlama */}
        <EventReviews eventId={ev.id} eventStatus={ev.status} />
      </main>
    </div>
  )
}
