import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { SiteHeader } from '../../../components/layout/SiteHeader'
import { Alert } from '../../../components/ui/Alert'
import { BUTON_TEMEL, BUTON_VARYANT } from '../../../components/ui/buttonStyles'
import { toProblem } from '../../../lib/api/client'
import { formatDateParts } from '../../../lib/format'
import { organizerApi, EVENT_STATUS_LABELS, EventStatus } from '../api/organizerApi'

// Durum suzgeci. "Tumu" icin undefined gonderiyorum -- backend'de
// Status nullable ve bos gecilince filtre uygulanmiyor.
const DURUM_SECENEKLERI: { etiket: string; deger?: number }[] = [
  { etiket: 'Tümü' },
  { etiket: 'Taslak', deger: EventStatus.Draft },
  { etiket: 'Onay bekleyen', deger: EventStatus.PendingApproval },
  { etiket: 'Satışta', deger: EventStatus.SalesOpen },
  { etiket: 'İptal', deger: EventStatus.Cancelled },
]

/**
 * Organizatörün kendi etkinlikleri -- PDF Sprint 5 "Etkinlik listesi".
 *
 * Bu ekran Sprint 5'te hiç yazılmamıştı. Backend'de uçlar duruyordu
 * ama organizatör arayüzden etkinlik oluşturamıyor, göremiyor,
 * yayına alamıyordu. PDF sayfa 5 organizatöre yedi yetki veriyor ve
 * dördü bu ekranla açılıyor.
 */
export function MyEventsPage() {
  const [durum, setDurum] = useState<number | undefined>(undefined)

  const sorgu = useQuery({
    queryKey: ['events', 'mine', durum],
    queryFn: () => organizerApi.getMyEvents({ status: durum, pageSize: 50 }),
  })

  return (
    <div className="min-h-screen bg-slate-100">
      <SiteHeader />

      <main className="mx-auto max-w-5xl px-4 py-8">
        <div className="mb-6 flex flex-wrap items-start justify-between gap-3">
          <div>
            <h1 className="font-display text-2xl font-bold tracking-tight text-kagit">
              Etkinliklerim
            </h1>
            <p className="mt-1 text-sm text-kagit-soluk">
              Etkinlik oluştur, oturum ve bilet türü tanımla, yayına gönder.
            </p>
          </div>

          <Link to="/panel/etkinlikler/yeni" className={`${BUTON_TEMEL} ${BUTON_VARYANT.primary}`}>
            Yeni etkinlik
          </Link>
        </div>

        {/* Durum suzgeci: rozetlerin aynisi, tiklanabilir hali.
            Ayri bir acilir liste koymadim -- bes secenek icin
            acilir liste, bir tiklamayi iki tiklamaya cevirir. */}
        <div className="mb-4 flex flex-wrap gap-2">
          {DURUM_SECENEKLERI.map((s) => (
            <button
              key={s.etiket}
              type="button"
              onClick={() => setDurum(s.deger)}
              className={`rounded-[4px] border px-2.5 py-1 text-xs font-medium transition-colors ${
                durum === s.deger
                  ? 'border-slate-900 bg-slate-900 text-white'
                  : 'border-slate-300 bg-white text-slate-600 hover:border-slate-900'
              }`}
            >
              {s.etiket}
            </button>
          ))}
        </div>

        {sorgu.isError && (
          <Alert variant="error">
            {toProblem(sorgu.error).detail ?? 'Etkinlikler yüklenemedi.'}
          </Alert>
        )}

        {sorgu.isPending && (
          <ul className="space-y-2" aria-busy="true" aria-label="Etkinlikler yükleniyor">
            {[1, 2, 3].map((i) => (
              <li key={i} className="h-[72px] animate-pulse rounded-[4px] bg-slate-200" />
            ))}
          </ul>
        )}

        {sorgu.data && sorgu.data.items.length === 0 && (
          <div className="rounded-[4px] border border-slate-300 bg-white px-5 py-10 text-center">
            <h2 className="font-display text-[15px] font-semibold text-slate-900">
              {durum === undefined ? 'Henüz etkinlik oluşturmadınız' : 'Bu durumda etkinlik yok'}
            </h2>
            <p className="mt-1 text-[13px] text-slate-500">
              {durum === undefined
                ? 'İlk etkinliğinizi oluşturun; oturum ve bilet türlerini sonra ekleyebilirsiniz.'
                : 'Başka bir durum seçin.'}
            </p>
            {durum === undefined && (
              <Link
                to="/panel/etkinlikler/yeni"
                className={`mt-4 ${BUTON_TEMEL} ${BUTON_VARYANT.primary}`}
              >
                Yeni etkinlik
              </Link>
            )}
          </div>
        )}

        {sorgu.data && sorgu.data.items.length > 0 && (
          <ul className="space-y-2">
            {sorgu.data.items.map((e) => {
              const tarih = formatDateParts(e.eventDate)
              const rozet = EVENT_STATUS_LABELS[e.status] ?? {
                text: 'Bilinmiyor',
                tone: 'border-slate-300 bg-slate-50 text-slate-500',
              }

              return (
                <li key={e.id}>
                  <Link
                    to={`/panel/etkinlikler/${e.id}`}
                    className="flex items-center gap-4 rounded-[4px] border border-slate-300 bg-white p-3.5 transition-colors hover:border-slate-900"
                  >
                    {/* Etkinlik kartindaki takvim yirtmacinin kucugu.
                        Ayni gorsel dili kullanmak, iki ekranin ayni
                        uygulamaya ait oldugunu hissettiriyor. */}
                    <div className="flex w-14 shrink-0 flex-col items-center rounded-[4px] bg-slate-900 py-1.5 text-white">
                      <span className="label-xs text-slate-400">{tarih.ay}</span>
                      <span className="num text-lg font-semibold leading-none">{tarih.gun}</span>
                    </div>

                    <div className="min-w-0 flex-grow">
                      <p className="truncate font-medium text-slate-900">{e.title}</p>
                      <p className="truncate text-[13px] text-slate-500">
                        {e.venueName}, {e.cityName} &middot;{' '}
                        <span className="num">{e.sessionCount}</span> oturum
                      </p>
                    </div>

                    <span className={`label-xs shrink-0 border px-1.5 py-[3px] ${rozet.tone}`}>
                      {rozet.text}
                    </span>
                  </Link>
                </li>
              )
            })}
          </ul>
        )}
      </main>
    </div>
  )
}
