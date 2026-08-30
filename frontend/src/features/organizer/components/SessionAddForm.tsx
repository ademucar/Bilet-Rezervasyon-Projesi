import { useState } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { Button } from '../../../components/ui/Button'
import { toProblem } from '../../../lib/api/client'
import { formatDateTime } from '../../../lib/format'
import type { EventDetail } from '../../booking/api/bookingApi'
import { adminApi } from '../../admin/api/adminApi'
import { organizerApi } from '../api/organizerApi'

interface Props {
  etkinlik: EventDetail
  onEklendi: () => void
  onHata: (mesaj: string) => void
  duzenlenebilir: boolean
}

/**
 * Oturum ekleme -- PDF Sprint 5 "Oturum ekleme" ve
 * "Salon ve oturma planı seçebilir".
 *
 * Bir etkinliğin birden fazla oturumu olabilir: aynı konser iki
 * gece, ya da matine + akşam seansı. Koltuklar oturum başına
 * üretiliyor (EventSeat), yani oturum eklemeden kimse bilet
 * alamıyor.
 */
export function SessionAddForm({ etkinlik, onEklendi, onHata, duzenlenebilir }: Props) {
  const [acik, setAcik] = useState(false)
  const [baslangic, setBaslangic] = useState('')
  const [bitis, setBitis] = useState('')
  const [salonId, setSalonId] = useState('')
  const [planId, setPlanId] = useState('')

  // Mekanin salonlari. Etkinligin mekani sabit oldugu icin
  // salon listesi de sabit; her acilista yeniden cekmiyorum.
  const mekanlar = useQuery({
    queryKey: ['venues', 'hepsi'],
    queryFn: () => adminApi.getVenues({ pageSize: 100 }),
    enabled: acik,
  })

  const mekan = mekanlar.data?.items.find((m) => m.name === etkinlik.venueName)

  const mekanDetay = useQuery({
    queryKey: ['venue', mekan?.id],
    queryFn: () => adminApi.getVenue(mekan?.id ?? ''),
    enabled: Boolean(mekan?.id),
  })

  // Oturma planlari SALONA bagli. Salon secilmeden plan listesi
  // gostermek anlamsiz -- baska salonun plani secilirse backend
  // zaten reddeder ama kullanici sebebini anlamaz.
  const planlar = useQuery({
    queryKey: ['seatLayouts', salonId],
    queryFn: () => adminApi.getSeatLayouts(salonId),
    enabled: Boolean(salonId),
  })

  const ekle = useMutation({
    mutationFn: () =>
      organizerApi.addSession(etkinlik.id, {
        startDate: new Date(baslangic).toISOString(),
        endDate: new Date(bitis).toISOString(),
        hallId: salonId,
        seatLayoutId: planId,
      }),
    onSuccess: () => {
      setAcik(false)
      setBaslangic('')
      setBitis('')
      setSalonId('')
      setPlanId('')
      onEklendi()
    },
    onError: (e) => onHata(toProblem(e).detail ?? 'Oturum eklenemedi.'),
  })

  const alan =
    'w-full rounded-[4px] border border-slate-300 px-3 py-2.5 text-sm outline-none ' +
    'transition-colors focus:border-brand-500'

  const gecerli = baslangic && bitis && salonId && planId

  return (
    <section className="mt-6 rounded-[4px] border border-slate-300 bg-white p-5">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 className="font-display font-semibold text-slate-900">
          Oturumlar <span className="num text-slate-400">({etkinlik.sessions.length})</span>
        </h2>
        {duzenlenebilir && (
          <Button variant="secondary" onClick={() => setAcik((v) => !v)}>
            {acik ? 'Vazgeç' : 'Oturum ekle'}
          </Button>
        )}
      </div>

      {etkinlik.sessions.length === 0 && !acik && (
        <p className="mt-3 text-[13px] text-slate-500">
          Henüz oturum yok. Oturum eklemeden etkinlik onaya gönderilemez; koltuklar oturum başına
          üretiliyor.
        </p>
      )}

      {etkinlik.sessions.length > 0 && (
        <ul className="mt-4 divide-y divide-slate-100">
          {etkinlik.sessions.map((o) => (
            <li key={o.id} className="flex flex-wrap items-center justify-between gap-2 py-2.5">
              <div className="min-w-0">
                <p className="text-sm font-medium text-slate-900">{formatDateTime(o.startDate)}</p>
                <p className="text-xs text-slate-500">
                  {o.hallName} &middot; {o.seatLayoutName}
                </p>
              </div>

              <span
                className={`label-xs border px-1.5 py-[3px] ${
                  o.areSeatsGenerated
                    ? 'border-emerald-300 bg-emerald-50 text-emerald-700'
                    : 'border-amber-300 bg-amber-50 text-amber-700'
                }`}
              >
                {o.areSeatsGenerated ? 'Koltuklar hazır' : 'Koltuk üretilmedi'}
              </span>
            </li>
          ))}
        </ul>
      )}

      {acik && (
        <div className="mt-4 space-y-4 border-t border-slate-200 pt-4">
          <div className="grid gap-4 sm:grid-cols-2">
            <div className="space-y-1.5">
              <label htmlFor="o-bas" className="label-xs block text-slate-500">
                Başlangıç
              </label>
              <input
                id="o-bas"
                type="datetime-local"
                className={alan}
                value={baslangic}
                onChange={(e) => setBaslangic(e.target.value)}
              />
            </div>
            <div className="space-y-1.5">
              <label htmlFor="o-bit" className="label-xs block text-slate-500">
                Bitiş
              </label>
              <input
                id="o-bit"
                type="datetime-local"
                className={alan}
                value={bitis}
                onChange={(e) => setBitis(e.target.value)}
              />
            </div>

            <div className="space-y-1.5">
              <label htmlFor="o-salon" className="label-xs block text-slate-500">
                Salon
              </label>
              <select
                id="o-salon"
                className={alan}
                value={salonId}
                onChange={(e) => {
                  setSalonId(e.target.value)
                  // Salon degisince eski planin secili kalmasi
                  // sessiz bir hata olurdu: baska salonun plani.
                  setPlanId('')
                }}
              >
                <option value="">Seçin</option>
                {(mekanDetay.data?.halls ?? []).map((h) => (
                  <option key={h.id} value={h.id}>
                    {h.name} ({h.capacity} kişi)
                  </option>
                ))}
              </select>
            </div>

            <div className="space-y-1.5">
              <label htmlFor="o-plan" className="label-xs block text-slate-500">
                Oturma planı
              </label>
              <select
                id="o-plan"
                className={alan}
                value={planId}
                onChange={(e) => setPlanId(e.target.value)}
                disabled={!salonId}
              >
                <option value="">{salonId ? 'Seçin' : 'Önce salon seçin'}</option>
                {(planlar.data ?? []).map((p) => (
                  <option key={p.id} value={p.id}>
                    {p.name} ({p.seatCount} koltuk)
                  </option>
                ))}
              </select>
              {salonId && planlar.data?.length === 0 && (
                <p className="text-xs text-amber-700">
                  Bu salonda oturma planı yok. Yönetim &rarr; Mekanlar bölümünden ekleyin.
                </p>
              )}
            </div>
          </div>

          <Button onClick={() => ekle.mutate()} isLoading={ekle.isPending} disabled={!gecerli}>
            Oturumu ekle
          </Button>
        </div>
      )}
    </section>
  )
}
