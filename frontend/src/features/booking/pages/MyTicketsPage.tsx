import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link, useSearchParams } from 'react-router-dom'
import { QRCodeSVG } from 'qrcode.react'
import { SiteHeader } from '../../../components/layout/SiteHeader'
import { Alert } from '../../../components/ui/Alert'
import { toProblem } from '../../../lib/api/client'
import { formatDateTime, formatMoney } from '../../../lib/format'
import { bookingApi, TicketStatus, type TicketDto } from '../api/bookingApi'

const STATUS_LABELS: Record<number, { text: string; className: string }> = {
  [TicketStatus.Active]: { text: 'Gecerli', className: 'bg-emerald-50 text-emerald-700' },
  [TicketStatus.Used]: { text: 'Kullanildi', className: 'bg-slate-100 text-slate-600' },
  [TicketStatus.Cancelled]: { text: 'Iptal', className: 'bg-red-50 text-red-700' },
  [TicketStatus.Refunded]: { text: 'Iade edildi', className: 'bg-amber-50 text-amber-700' },
  [TicketStatus.Expired]: { text: 'Suresi doldu', className: 'bg-slate-100 text-slate-600' },
}

/**
 * Biletlerim -- PDF sayfa 4: "Kullanici kendi biletlerini gorebilmelidir."
 */
export function MyTicketsPage() {
  const [searchParams] = useSearchParams()
  const [filter, setFilter] = useState<number | undefined>(undefined)

  // Odeme sonrasi buraya "?yeni=1" ile geliyoruz.
  // Kullanicinin "odemem gecti mi?" tereddudunu ortadan kaldiriyor.
  const isFreshPurchase = searchParams.get('yeni') === '1'

  const ticketsQuery = useQuery({
    queryKey: ['my-tickets', filter],
    queryFn: () => bookingApi.getMyTickets(filter),
  })

  return (
    <div className="min-h-screen bg-slate-50">
      <SiteHeader />

      <main className="mx-auto max-w-4xl px-4 py-8">
        <h1 className="text-2xl font-bold text-slate-900">Biletlerim</h1>

        {isFreshPurchase && (
          <div className="mt-4">
            <Alert variant="success">
              Odemeniz alindi. Biletleriniz asagida; girise QR kodunuzu okutmaniz yeterli.
            </Alert>
          </div>
        )}

        <div className="mt-6 flex flex-wrap gap-2">
          {[
            { label: 'Tumu', value: undefined },
            { label: 'Gecerli', value: TicketStatus.Active as number },
            { label: 'Kullanilmis', value: TicketStatus.Used as number },
            { label: 'Iade', value: TicketStatus.Refunded as number },
          ].map((tab) => (
            <button
              key={tab.label}
              type="button"
              onClick={() => setFilter(tab.value)}
              // aria-pressed: ekran okuyucuya hangi filtrenin acik
              // oldugunu soyler. Yalnizca renk degistirseydik
              // gormeyen kullanici hangi sekmede oldugunu bilemezdi.
              aria-pressed={filter === tab.value}
              className={`rounded-lg px-3 py-1.5 text-sm font-medium transition-colors ${
                filter === tab.value
                  ? 'bg-brand-600 text-white'
                  : 'bg-white text-slate-600 hover:bg-slate-100'
              }`}
            >
              {tab.label}
            </button>
          ))}
        </div>

        {ticketsQuery.isError && (
          <div className="mt-6">
            <Alert variant="error">{toProblem(ticketsQuery.error).detail}</Alert>
          </div>
        )}

        {ticketsQuery.isPending && (
          <div className="mt-6 space-y-4">
            {[1, 2].map((i) => (
              <div key={i} className="h-44 animate-pulse rounded-2xl bg-slate-200" />
            ))}
          </div>
        )}

        {ticketsQuery.data?.length === 0 && (
          <div className="mt-6 rounded-2xl border border-dashed border-slate-300 bg-white p-12 text-center">
            <p className="text-sm text-slate-500">Bu filtrede bilet yok.</p>
            <Link
              to="/etkinlikler"
              className="mt-3 inline-block text-sm font-medium text-brand-600 hover:underline"
            >
              Etkinliklere goz at
            </Link>
          </div>
        )}

        <ul className="mt-6 space-y-4">
          {ticketsQuery.data?.map((ticket) => (
            <li key={ticket.id}>
              <TicketCard ticket={ticket} />
            </li>
          ))}
        </ul>
      </main>
    </div>
  )
}

function TicketCard({ ticket }: { ticket: TicketDto }) {
  const badge = STATUS_LABELS[ticket.status] ?? {
    text: 'Bilinmiyor',
    className: 'bg-slate-100 text-slate-600',
  }

  return (
    <article className="flex flex-wrap gap-6 rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
      <div className="min-w-56 flex-1">
        <div className="flex items-start justify-between gap-2">
          <h2 className="font-semibold text-slate-900">{ticket.eventTitle}</h2>
          <span
            className={`shrink-0 rounded-full px-2 py-0.5 text-xs font-medium ${badge.className}`}
          >
            {badge.text}
          </span>
        </div>

        <p className="mt-1 text-sm text-slate-500">
          {formatDateTime(ticket.sessionStartDate)} &middot; {ticket.venueName}
        </p>

        <dl className="mt-4 grid gap-2 text-sm sm:grid-cols-2">
          <div>
            <dt className="text-slate-500">Koltuk</dt>
            <dd className="font-medium text-slate-900">
              {ticket.seatLabel} ({ticket.sectionName})
            </dd>
          </div>
          <div>
            <dt className="text-slate-500">Bilet turu</dt>
            <dd className="font-medium text-slate-900">{ticket.ticketTypeName}</dd>
          </div>
          <div>
            <dt className="text-slate-500">Tutar</dt>
            <dd className="font-medium text-slate-900">
              {formatMoney(ticket.price, ticket.currency)}
            </dd>
          </div>
          <div>
            <dt className="text-slate-500">Bilet no</dt>
            <dd className="font-mono text-xs text-slate-700">{ticket.ticketNumber}</dd>
          </div>
        </dl>

        {ticket.usedAt && (
          <p className="mt-3 text-xs text-slate-500">
            {formatDateTime(ticket.usedAt)} tarihinde giriste okutuldu.
          </p>
        )}
      </div>

      {/* ============================================================
          QR KODU
          ============================================================
          Backend qrValue'yu YALNIZCA aktif biletlerde donuyor
          (GetMyTicketsQueryHandler). Iptal edilmis biletin QR'ini
          gondermenin faydasi yok ve hassas bir degeri gereksiz yere
          yaymak olurdu.

          Bu yuzden burada `qrValue` null olabilir ve bunu bir HATA
          gibi degil, beklenen bir durum gibi ele aliyorum.

          QRCodeSVG kullaniyorum, QRCodeCanvas degil:
            - SVG vektorel; yakinlastirinca veya yazdirinca bulanmaz.
              Turnikedeki okuyucunun keskin kenarlara ihtiyaci var.
            - Canvas ise sabit piksel; buyuk ekranda kareli gorunur.

          level="M": hata duzeltme seviyesi. Karekodun bir kismi
          zarar gorse bile (ekran cizigi, parmak izi) okunabilir.
          "H" daha dayanikli ama kodu yogunlastirir; telefon
          ekranindan okutmada M yeterli.
          ============================================================ */}
      <div className="flex flex-col items-center justify-center">
        {ticket.qrValue ? (
          <>
            <div className="rounded-xl border border-slate-200 bg-white p-3">
              <QRCodeSVG value={ticket.qrValue} size={132} level="M" />
            </div>
            <p className="mt-2 text-xs text-slate-500">Giriste okutun</p>
          </>
        ) : (
          <div className="flex h-[156px] w-[156px] items-center justify-center rounded-xl border border-dashed border-slate-300 bg-slate-50 p-4 text-center">
            <p className="text-xs text-slate-500">
              Bu bilet artik gecerli olmadigi icin QR kodu gosterilmiyor.
            </p>
          </div>
        )}
      </div>
    </article>
  )
}
