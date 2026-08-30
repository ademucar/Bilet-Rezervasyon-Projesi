import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useMutation } from '@tanstack/react-query'
import { Button } from '../../../components/ui/Button'
import { Input } from '../../../components/ui/Input'
import { toProblem } from '../../../lib/api/client'
import type { EventDetail } from '../../booking/api/bookingApi'
import { organizerApi } from '../api/organizerApi'

const sema = z
  .object({
    title: z.string().min(1, 'Başlık zorunludur.').max(250),
    description: z.string().min(1, 'Açıklama zorunludur.').max(4000),
    minimumAge: z.coerce.number().int().min(0).max(99),
    eventDate: z.string().min(1, 'Etkinlik tarihi zorunludur.'),
    salesStartDate: z.string().min(1, 'Satış başlangıcı zorunludur.'),
    salesEndDate: z.string().min(1, 'Satış bitişi zorunludur.'),
  })
  .refine((d) => new Date(d.salesStartDate) < new Date(d.salesEndDate), {
    message: 'Satış başlangıcı, satış bitişinden sonra olamaz.',
    path: ['salesEndDate'],
  })
  .refine((d) => new Date(d.salesEndDate) <= new Date(d.eventDate), {
    message: 'Satış bitişi, etkinlik başlangıcından sonra olamaz.',
    path: ['salesEndDate'],
  })

type Form = z.input<typeof sema>

/**
 * ISO tarihi datetime-local girdisinin beklediği biçime çevirir.
 *
 * Girdi "2027-07-15T20:00" istiyor: saniye yok, saat dilimi yok ve
 * YEREL saat. toISOString() UTC verdiği için doğrudan kullanamam --
 * kullanıcı 20:00 kaydettiyse formu açtığında 17:00 görürdü.
 */
function yerelGirdi(iso: string): string {
  const d = new Date(iso)
  const p = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}T${p(d.getHours())}:${p(d.getMinutes())}`
}

interface Props {
  etkinlik: EventDetail
  onKaydedildi: () => void
  onHata: (mesaj: string) => void
}

export function EventEditForm({ etkinlik, onKaydedildi, onHata }: Props) {
  const [kaydedildi, setKaydedildi] = useState(false)

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<Form>({
    resolver: zodResolver(sema),
    defaultValues: {
      title: etkinlik.title,
      description: etkinlik.description,
      minimumAge: etkinlik.minimumAge,
      eventDate: yerelGirdi(etkinlik.eventDate),
      salesStartDate: yerelGirdi(etkinlik.salesStartDate),
      salesEndDate: yerelGirdi(etkinlik.salesEndDate),
    },
  })

  const kaydet = useMutation({
    mutationFn: (d: Form) =>
      organizerApi.updateEvent(etkinlik.id, {
        title: d.title,
        description: d.description,
        minimumAge: Number(d.minimumAge),
        eventDate: new Date(d.eventDate).toISOString(),
        salesStartDate: new Date(d.salesStartDate).toISOString(),
        salesEndDate: new Date(d.salesEndDate).toISOString(),
      }),
    onSuccess: () => {
      setKaydedildi(true)
      onKaydedildi()
      // Onay yazisi kalici olmasin: iki saniye sonra kayboluyor.
      // Kalici olsaydi kullanici bir sonraki degisikligi
      // kaydetmediginde de "kaydedildi" gorurdu.
      setTimeout(() => setKaydedildi(false), 2000)
    },
    onError: (e) => onHata(toProblem(e).detail ?? 'Kaydedilemedi.'),
  })

  const alan =
    'w-full rounded-[4px] border border-slate-300 px-3 py-2.5 text-sm outline-none ' +
    'transition-colors focus:border-brand-500'

  return (
    <section className="mt-6 rounded-[4px] border border-slate-300 bg-white p-5">
      <div className="flex items-center justify-between">
        <h2 className="font-display font-semibold text-slate-900">Bilgiler</h2>
        {kaydedildi && <span className="label-xs text-emerald-700">Kaydedildi</span>}
      </div>

      <form onSubmit={handleSubmit((d) => kaydet.mutate(d))} className="mt-4 space-y-5" noValidate>
        <Input label="Başlık" error={errors.title?.message} {...register('title')} />

        <div className="space-y-1.5">
          <label htmlFor="duzenleAciklama" className="label-xs block text-slate-500">
            Açıklama
          </label>
          <textarea id="duzenleAciklama" rows={4} className={alan} {...register('description')} />
          {errors.description && (
            <p role="alert" className="text-sm text-red-600">
              {errors.description.message}
            </p>
          )}
        </div>

        <div className="grid gap-5 sm:grid-cols-3">
          <TarihAlani
            id="d-etkinlik"
            etiket="Etkinlik tarihi"
            hata={errors.eventDate?.message}
            {...register('eventDate')}
          />
          <TarihAlani
            id="d-satisbas"
            etiket="Satış başlangıcı"
            hata={errors.salesStartDate?.message}
            {...register('salesStartDate')}
          />
          <TarihAlani
            id="d-satisbit"
            etiket="Satış bitişi"
            hata={errors.salesEndDate?.message}
            {...register('salesEndDate')}
          />
        </div>

        <div className="sm:w-1/3">
          <Input
            label="Yaş sınırı (0 = yok)"
            type="number"
            error={errors.minimumAge?.message}
            {...register('minimumAge')}
          />
        </div>

        <div className="border-t border-slate-200 pt-4">
          <Button type="submit" isLoading={isSubmitting || kaydet.isPending}>
            Değişiklikleri kaydet
          </Button>
          <p className="mt-2 text-xs text-slate-500">
            Satışı başlamış etkinliklerde tarih değişikliğini backend reddeder (PDF sayfa 13).
          </p>
        </div>
      </form>
    </section>
  )
}

interface TarihProps extends React.InputHTMLAttributes<HTMLInputElement> {
  id: string
  etiket: string
  hata?: string
}

const TarihAlani = ({ id, etiket, hata, ...props }: TarihProps) => (
  <div className="space-y-1.5">
    <label htmlFor={id} className="label-xs block text-slate-500">
      {etiket}
    </label>
    <input
      id={id}
      type="datetime-local"
      className="w-full rounded-[4px] border border-slate-300 px-3 py-2.5 text-sm outline-none transition-colors focus:border-brand-500"
      aria-invalid={hata ? true : undefined}
      {...props}
    />
    {hata && (
      <p role="alert" className="text-sm text-red-600">
        {hata}
      </p>
    )}
  </div>
)
