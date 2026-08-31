import { useState } from 'react'
import { Link } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { AdminLayout } from '../components/AdminLayout'
import { Alert } from '../../../components/ui/Alert'
import { Button } from '../../../components/ui/Button'
import { Input } from '../../../components/ui/Input'
import { toProblem } from '../../../lib/api/client'
import { formatDate } from '../../../lib/format'
import { bookingApi, type EventListItem } from '../../booking/api/bookingApi'
import { EventStatus, EVENT_STATUS_LABELS, organizerApi } from '../../organizer/api/organizerApi'

const SUZGECLER: { etiket: string; deger?: number }[] = [
  { etiket: 'Onay bekleyenler', deger: EventStatus.PendingApproval },
  { etiket: 'Yayında', deger: EventStatus.Published },
  { etiket: 'Satışta', deger: EventStatus.SalesOpen },
  { etiket: 'Askıdakiler', deger: EventStatus.Suspended },
  { etiket: 'Tümü' },
]

/**
 * Admin etkinlik yönetimi -- PDF sayfa 5:
 * "Admin: Tüm etkinlikleri görüntüleyebilir."
 * "Admin: Uygunsuz etkinlikleri pasifleştirebilir."
 *
 * Ayrı bir "tüm etkinlikler" ucu yazmadım, genel GET /events'i
 * kullanıyorum. Sebebi controller'da duruyor: o uç, isteği yapan
 * kişi admin ise IncludeUnpublished'ı kendisi true yapıyor. Yani
 * aynı adres, admine taslakları ve askıdakileri de döndürüyor.
 *
 * Aynı işi yapan ikinci bir uç açsaydım, iki ucun filtre mantığı
 * zamanla birbirinden ayrılırdı.
 */
export function AdminEventsPage() {
  const queryClient = useQueryClient()
  const [durum, setDurum] = useState<number | undefined>(EventStatus.PendingApproval)
  const [arama, setArama] = useState('')
  const [hata, setHata] = useState<string | null>(null)
  const [askiyaAlinan, setAskiyaAlinan] = useState<string | null>(null)
  const [sebep, setSebep] = useState('')

  const sorgu = useQuery({
    queryKey: ['adminEvents', durum, arama],
    queryFn: () =>
      bookingApi.getEvents({
        status: durum,
        search: arama.trim() || undefined,
        pageSize: 50,
        sortBy: 'created',
        sortDirection: 'desc',
      }),
  })

  const tazele = () => {
    queryClient.invalidateQueries({ queryKey: ['adminEvents'] })
  }

  const yayinla = useMutation({
    mutationFn: (id: string) => organizerApi.publishEvent(id),
    onSuccess: tazele,
    onError: (e) => setHata(toProblem(e).detail ?? 'Yayına alınamadı.'),
  })

  const askiyaAl = useMutation({
    mutationFn: (id: string) => organizerApi.suspendEvent(id, sebep),
    onSuccess: () => {
      setAskiyaAlinan(null)
      setSebep('')
      tazele()
    },
    onError: (e) => setHata(toProblem(e).detail ?? 'Askıya alınamadı.'),
  })

  const geriAl = useMutation({
    mutationFn: (id: string) => organizerApi.reinstateEvent(id),
    onSuccess: tazele,
    onError: (e) => setHata(toProblem(e).detail ?? 'Yayına geri alınamadı.'),
  })

  return (
    <AdminLayout title="Etkinlikler" subtitle="Tüm etkinlikleri görüntüleyin ve denetleyin">
      <div className="mb-4 flex flex-wrap items-end gap-3 rounded-[4px] border border-slate-300 bg-white p-4">
        <div className="flex flex-wrap gap-2">
          {SUZGECLER.map((s) => (
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

        <div className="ml-auto w-full sm:w-64">
          <Input
            label="Ara"
            placeholder="Etkinlik adı..."
            value={arama}
            onChange={(e) => setArama(e.target.value)}
          />
        </div>
      </div>

      {hata && (
        <div className="mb-4">
          <Alert variant="error">{hata}</Alert>
        </div>
      )}

      {sorgu.isError && (
        <Alert variant="error">{toProblem(sorgu.error).detail ?? 'Etkinlikler yüklenemedi.'}</Alert>
      )}

      {sorgu.isPending && (
        <ul className="space-y-2" aria-busy="true" aria-label="Etkinlikler yükleniyor">
          {[1, 2, 3].map((i) => (
            <li key={i} className="h-20 animate-pulse rounded-[4px] bg-slate-200" />
          ))}
        </ul>
      )}

      {sorgu.data && sorgu.data.items.length === 0 && (
        <div className="rounded-[4px] border border-slate-300 bg-white px-5 py-10 text-center">
          <p className="text-sm text-slate-500">
            {durum === EventStatus.PendingApproval
              ? 'Onay bekleyen etkinlik yok.'
              : 'Bu filtreye uyan etkinlik yok.'}
          </p>
        </div>
      )}

      {sorgu.data && sorgu.data.items.length > 0 && (
        <>
          <p className="label-xs mb-2 text-slate-500">
            <span className="num">{sorgu.data.totalCount}</span> etkinlik
          </p>

          <ul className="space-y-2">
            {sorgu.data.items.map((e) => (
              <EtkinlikSatiri
                key={e.id}
                etkinlik={e}
                sebepAcik={askiyaAlinan === e.id}
                sebep={sebep}
                onSebep={setSebep}
                onSebepAc={() => {
                  setAskiyaAlinan(askiyaAlinan === e.id ? null : e.id)
                  setSebep('')
                }}
                onYayinla={() => {
                  setHata(null)
                  yayinla.mutate(e.id)
                }}
                onAskiyaAl={() => {
                  setHata(null)
                  askiyaAl.mutate(e.id)
                }}
                onGeriAl={() => {
                  setHata(null)
                  geriAl.mutate(e.id)
                }}
                yayinlaBekliyor={yayinla.isPending && yayinla.variables === e.id}
                askiyaAlBekliyor={askiyaAl.isPending && askiyaAl.variables === e.id}
                geriAlBekliyor={geriAl.isPending && geriAl.variables === e.id}
              />
            ))}
          </ul>
        </>
      )}
    </AdminLayout>
  )
}

interface SatirProps {
  etkinlik: EventListItem
  sebepAcik: boolean
  sebep: string
  onSebep: (v: string) => void
  onSebepAc: () => void
  onYayinla: () => void
  onAskiyaAl: () => void
  onGeriAl: () => void
  yayinlaBekliyor: boolean
  askiyaAlBekliyor: boolean
  geriAlBekliyor: boolean
}

function EtkinlikSatiri({
  etkinlik: e,
  sebepAcik,
  sebep,
  onSebep,
  onSebepAc,
  onYayinla,
  onAskiyaAl,
  onGeriAl,
  yayinlaBekliyor,
  askiyaAlBekliyor,
  geriAlBekliyor,
}: SatirProps) {
  const rozet = EVENT_STATUS_LABELS[e.status] ?? {
    text: 'Bilinmiyor',
    tone: 'border-slate-300 bg-slate-50 text-slate-500',
  }

  // Hangi eylemin gorunecegini durum belirliyor.
  //
  // Butun butonlari her zaman gosterip backend'in reddetmesine
  // birakabilirdim. Birakmadim: "Askiya al" butonuna basip 422
  // yiyen admin, hatanin kendi hatasi mi sistemin hatasi mi
  // oldugunu anlamaz. Gecis tablosu Event.cs'te; buradaki kosullar
  // onun ekrandaki karsiligi.
  const yayinlanabilir = e.status === EventStatus.PendingApproval
  const askiyaAlinabilir = e.status === EventStatus.Published || e.status === EventStatus.SalesOpen
  const geriAlinabilir = e.status === EventStatus.Suspended

  return (
    <li className="rounded-[4px] border border-slate-300 bg-white p-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <Link
            to={`/etkinlikler/${e.id}`}
            className="font-display font-semibold text-slate-900 hover:text-brand-600"
          >
            {e.title}
          </Link>
          <p className="mt-0.5 text-[13px] text-slate-500">
            {e.categoryName} &middot; {e.venueName}, {e.cityName}
          </p>
          <p className="mt-0.5 text-[13px] text-slate-500">
            <span className="num">{formatDate(e.eventDate)}</span> &middot;{' '}
            <span className="num">{e.sessionCount}</span> oturum
          </p>
        </div>
        <span className={`label-xs shrink-0 border px-1.5 py-[3px] ${rozet.tone}`}>
          {rozet.text}
        </span>
      </div>

      {(yayinlanabilir || askiyaAlinabilir || geriAlinabilir) && (
        <div className="mt-3 flex flex-wrap gap-2 border-t border-slate-200 pt-3">
          {yayinlanabilir && (
            <Button onClick={onYayinla} isLoading={yayinlaBekliyor}>
              Yayına al
            </Button>
          )}
          {askiyaAlinabilir && (
            <Button variant="secondary" onClick={onSebepAc}>
              {sebepAcik ? 'Vazgeç' : 'Askıya al'}
            </Button>
          )}
          {geriAlinabilir && (
            <Button onClick={onGeriAl} isLoading={geriAlBekliyor}>
              Yayına geri al
            </Button>
          )}
        </div>
      )}

      {sebepAcik && (
        <div className="mt-3 border-t border-slate-200 pt-3">
          <Input
            label="Askıya alma sebebi"
            placeholder="Afiş uygunsuz içerik barındırıyor..."
            value={sebep}
            onChange={(ev) => onSebep(ev.target.value)}
          />
          {/* Sebep su an yalnizca sunucu loglarina yaziliyor,
              organizatorun ekraninda gorunmuyor -- Event uzerinde
              boyle bir sutun yok. Bunu README'nin "bilinen
              eksikler" bolumune yazdim; buradaki metni de ona gore
              kurdum, kullaniciya yalan soylemesin. */}
          <p className="mt-1 text-xs text-slate-500">
            Sebep denetim kaydına yazılır. Organizatöre ayrıca iletmeniz gerekir.
          </p>
          <div className="mt-3">
            <Button
              onClick={onAskiyaAl}
              isLoading={askiyaAlBekliyor}
              disabled={sebep.trim().length === 0}
            >
              Askıya al
            </Button>
          </div>
        </div>
      )}
    </li>
  )
}
