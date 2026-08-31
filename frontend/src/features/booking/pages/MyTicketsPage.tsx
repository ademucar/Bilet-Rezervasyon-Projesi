import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link, useSearchParams } from 'react-router-dom'
import { QRCodeSVG } from 'qrcode.react'
import { SiteHeader } from '../../../components/layout/SiteHeader'
import { Alert } from '../../../components/ui/Alert'
import { toProblem } from '../../../lib/api/client'
import { formatDateParts, formatDateTime, formatMoney } from '../../../lib/format'
import { bookingApi, TicketStatus, type TicketDto } from '../api/bookingApi'
import { TicketCancelPanel } from '../components/TicketCancelPanel'

// Durum -> etiket metni + NOKTA rengi.
//
// Rozet artık dolgu değil: koçanın sol üstünde küçük bir nokta ve
// büyük harf etiket duruyor. Dolgu rozet, biletin kendi zemini
// (beyaz kâğıt) üzerinde ikinci bir kart gibi duruyordu.
const STATUS_LABELS: Record<number, { text: string; dot: string; tone: string }> = {
  [TicketStatus.Active]: { text: 'Geçerli', dot: 'bg-emerald-600', tone: 'text-emerald-700' },
  [TicketStatus.Used]: { text: 'Kullanıldı', dot: 'bg-slate-400', tone: 'text-slate-500' },
  [TicketStatus.Cancelled]: { text: 'İptal', dot: 'bg-red-600', tone: 'text-red-700' },
  [TicketStatus.Refunded]: { text: 'İade edildi', dot: 'bg-amber-600', tone: 'text-amber-700' },
  [TicketStatus.Expired]: { text: 'Süresi doldu', dot: 'bg-slate-400', tone: 'text-slate-500' },
}

/**
 * Biletlerim -- PDF sayfa 4: "Kullanıcı kendi biletlerini görebilmelidir."
 */
export function MyTicketsPage() {
  const [searchParams] = useSearchParams()
  const [filter, setFilter] = useState<number | undefined>(undefined)

  // Ayni anda tek bilet icin iptal paneli acik olsun. Hepsini birden
  // acabilseydim ekran, kullanicinin hangi bileti iptal ettigini
  // karistirmasi cok kolay bir liste olurdu.
  const [iptalEdilen, setIptalEdilen] = useState<string | null>(null)

  // Ödeme sonrası buraya "?yeni=1" ile geliyorum.
  // Kullanıcının "odemem gecti mi?" tereddudunu ortadan kaldiriyor.
  const isFreshPurchase = searchParams.get('yeni') === '1'

  const ticketsQuery = useQuery({
    queryKey: ['my-tickets', filter],
    queryFn: () => bookingApi.getMyTickets(filter),
  })

  return (
    <div className="min-h-screen bg-slate-100">
      <SiteHeader />

      <main className="mx-auto max-w-4xl px-4 py-8">
        <h1 className="font-display text-2xl font-bold tracking-tight text-kagit">Biletlerim</h1>

        {isFreshPurchase && (
          <div className="mt-4">
            <Alert variant="success">
              Ödemeniz alındı. Biletleriniz aşağıda; girişe QR kodunuzu okutmanız yeterli.
            </Alert>
          </div>
        )}

        <div className="mt-6 flex flex-wrap gap-2">
          {[
            { label: 'Tümü', value: undefined },
            { label: 'Geçerli', value: TicketStatus.Active as number },
            { label: 'Kullanılmış', value: TicketStatus.Used as number },
            { label: 'İade', value: TicketStatus.Refunded as number },
          ].map((tab) => (
            <button
              key={tab.label}
              type="button"
              onClick={() => setFilter(tab.value)}
              // aria-pressed: ekran okuyucuya hangi filtrenin açık
              // olduğunu söyler. Yalnızca renk değiştirseydim
              // görmeyen kullanıcı hangi sekmede olduğunu bilemezdi.
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
              <div key={i} className="h-44 animate-pulse rounded-[4px] bg-slate-200" />
            ))}
          </div>
        )}

        {ticketsQuery.data?.length === 0 && (
          <div className="mt-6 rounded-[4px] border border-slate-300 bg-white p-12 text-center">
            <p className="text-sm text-slate-500">Bu filtrede bilet yok.</p>
            <Link
              to="/etkinlikler"
              className="mt-3 inline-block text-sm font-medium text-brand-600 hover:underline"
            >
              Etkinliklere göz at
            </Link>
          </div>
        )}

        <ul className="mt-6 space-y-4">
          {ticketsQuery.data?.map((ticket) => (
            <li key={ticket.id}>
              <TicketCard
                ticket={ticket}
                iptalAcik={iptalEdilen === ticket.id}
                onIptalAc={() => setIptalEdilen(iptalEdilen === ticket.id ? null : ticket.id)}
              />
            </li>
          ))}
        </ul>
      </main>
    </div>
  )
}

interface KartProps {
  ticket: TicketDto
  iptalAcik: boolean
  onIptalAc: () => void
}

function TicketCard({ ticket, iptalAcik, onIptalAc }: KartProps) {
  const badge = STATUS_LABELS[ticket.status] ?? {
    text: 'Bilinmiyor',
    dot: 'bg-slate-400',
    tone: 'text-slate-500',
  }

  // Kullanılmış / iptal / iade biletler SOLUK. Kullanıcının
  // listesinde asıl aradığı şey geçerli bilet; diğerleri arşiv.
  const olu = ticket.status !== TicketStatus.Active
  const tarih = formatDateParts(ticket.sessionStartDate)

  return (
    /*
       BİLET, KART GİBİ DEĞİL BİLET GİBİ GÖRÜNMELİ
       Önceki hâl köşesi 16px yuvarlatılmış, gölgeli bir kutuydu --
       sitedeki diğer her kutuyla aynı. Oysa bu ekrandaki nesne
       gerçek dünyada bir KOÇAN: kapıda uzatılan şey.

       O hissi veren iki ayrıntı var ve ikisi de bedava:
         1. Sağda koparma çizgisi (dashed) ile ayrılmış QR koçanı
         2. Keskin köşe + tek çizgi çerçeve, gölge yok

       Gradyan, emoji veya süs ikonu koymadım; yapı zaten söylüyor.
       */
    <article
      className={`overflow-hidden rounded-[4px] border ${
        olu ? 'border-slate-200 bg-slate-50 opacity-75' : 'border-slate-300 bg-white'
      }`}
    >
      <div className="flex">
        {/* ---- SOL GÖVDE ---- */}
        <div className="min-w-0 flex-grow p-4">
          <div className="mb-2.5 flex items-center gap-2">
            <span className={`size-1.5 shrink-0 rounded-full ${badge.dot}`} aria-hidden="true" />
            <span className={`label-xs ${badge.tone}`}>
              {badge.text}
              {ticket.usedAt && ` \u00b7 ${formatDateTime(ticket.usedAt)}`}
            </span>
          </div>

          <h2 className="font-display text-lg font-bold leading-tight tracking-tight text-slate-900">
            {ticket.eventTitle}
          </h2>
          <p className="num mt-1 text-xs font-medium text-brand-600">
            {tarih.gun} {tarih.ay.toLocaleUpperCase('tr-TR')} {tarih.yil} &middot; {tarih.saat}
          </p>

          {/* Koltuk / blok / tür: üç sütun, hepsi aynı hizada.
            Eski hâlde bunlar iki sütunlu bir dl idi ve "Koltuk"
            satırı "Bilet no" ile aynı görsel ağırlıktaydı.
            Kapıda sorulan şey KOLTUK; en iri rakam o olmalı. */}
          <dl className="mt-3.5 grid grid-cols-3 gap-3">
            <div>
              <dt className="label-xs">Koltuk</dt>
              <dd className="num mt-1 text-[17px] font-semibold text-slate-900">
                {ticket.seatLabel}
              </dd>
            </div>
            <div className="min-w-0">
              <dt className="label-xs">Blok</dt>
              <dd className="mt-1 truncate text-sm font-semibold text-slate-900">
                {ticket.sectionName}
              </dd>
            </div>
            <div className="min-w-0">
              <dt className="label-xs">Tür</dt>
              <dd className="mt-1 truncate text-sm font-semibold text-slate-900">
                {ticket.ticketTypeName}
              </dd>
            </div>
          </dl>

          <div className="mt-3.5 flex items-end justify-between gap-3 border-t border-dashed border-slate-300 pt-3">
            <div className="min-w-0">
              <p className="label-xs">Mekan</p>
              <p className="truncate text-[13px] text-slate-700">{ticket.venueName}</p>
            </div>
            <span className="num shrink-0 text-base font-semibold text-slate-900">
              {formatMoney(ticket.price, ticket.currency)}
            </span>
          </div>

          {/* Iptal, yalnizca gecerli biletlerde.
            Kullanilmis veya zaten iptal edilmis bir bilette dugmeyi
            gostermek, sunucunun reddedecegi bir islemi teklif etmek
            olurdu. */}
          {ticket.status === TicketStatus.Active && (
            <button
              type="button"
              onClick={onIptalAc}
              aria-expanded={iptalAcik}
              className="mt-3 text-[13px] font-medium text-slate-500 underline-offset-2 transition-colors hover:text-red-700 hover:underline"
            >
              {iptalAcik ? 'Vazgeç' : 'Bileti iptal et'}
            </button>
          )}
        </div>

        {/*
          SAĞ: QR KOÇANI
          Koparma çizgisi (border-l-dashed) biletin tamamını "koçan"
          yapan tek ayrıntı.

          Backend qrValue'yu YALNIZCA aktif biletlerde dönüyor
          (GetMyTicketsQueryHandler). İptal edilmiş biletin QR
          değerini göndermenin faydası yok ve hassas bir değeri
          gereksiz yere yaymak olurdu. Bu yüzden `qrValue` null
          olabilir ve bunu bir HATA gibi değil, beklenen bir durum
          gibi ele alıyorum.

          QRCodeSVG kullanıyorum, QRCodeCanvas değil:
            - SVG vektörel; yakınlaştırınca veya yazdırınca bulanmaz.
              Turnikedeki okuyucunun keskin kenarlara ihtiyacı var.
            - Canvas ise sabit piksel; büyük ekranda kareli görünür.

          level="M": hata düzeltme seviyesi. Karekodun bir kısmı
          zarar görse bile (ekran çiziği, parmak izi) okunabilir.
          "H" daha dayanıklı ama kodu yoğunlaştırır; telefon
          ekranından okutmada M yeterli.
          */}
        <div className="flex w-[140px] shrink-0 flex-col items-center justify-center gap-2.5 border-l border-dashed border-slate-400 bg-slate-50 p-4">
          {ticket.qrValue ? (
            <>
              <div className="border border-slate-300 bg-white p-1.5">
                <QRCodeSVG value={ticket.qrValue} size={96} level="M" />
              </div>
              <div className="text-center">
                <p className="label-xs">Bilet no</p>
                <p className="num mt-1 break-all text-[10px] text-slate-600">
                  {ticket.ticketNumber}
                </p>
              </div>
            </>
          ) : (
            <p className="text-center text-[11px] leading-relaxed text-slate-500">
              Bu bilet artık geçerli olmadığı için QR kodu gösterilmiyor.
            </p>
          )}
        </div>
      </div>

      {iptalAcik && <TicketCancelPanel ticketId={ticket.id} onKapat={onIptalAc} />}
    </article>
  )
}
