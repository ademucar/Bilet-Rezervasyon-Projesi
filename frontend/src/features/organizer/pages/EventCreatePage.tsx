import { useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate } from 'react-router-dom'
import { SiteHeader } from '../../../components/layout/SiteHeader'
import { Alert } from '../../../components/ui/Alert'
import { Button } from '../../../components/ui/Button'
import { Input } from '../../../components/ui/Input'
import { toProblem } from '../../../lib/api/client'
import { adminApi } from '../../admin/api/adminApi'
import { bookingApi } from '../../booking/api/bookingApi'
import { organizerApi } from '../api/organizerApi'

/**
 * Etkinlik oluşturma formu -- PDF Sprint 5.
 *
 * Doğrulamayı iki katmana böldüm ve ikisi de gerekli.
 *
 * Zod burada, tarayıcıda: kullanıcı yazarken anında geri bildirim
 * versin, boş form sunucuya gitmesin. Sunucudaki FluentValidation
 * ise asıl otorite; buradaki kuralları atlayan bir istek (curl,
 * Postman) yine reddediliyor.
 *
 * Aynı kuralı iki yerde yazmak tekrar gibi görünüyor ama biri
 * "deneyim" diğeri "güvenlik". Yalnızca birini tutsaydım ya form
 * sinir bozucu olurdu ya da API korumasız kalırdı.
 */
const sema = z
  .object({
    title: z.string().min(1, 'Etkinlik başlığı zorunludur.').max(250),
    description: z.string().min(1, 'Açıklama zorunludur.').max(4000),
    categoryId: z.string().min(1, 'Kategori seçilmelidir.'),
    cityId: z.string().min(1, 'Şehir seçilmelidir.'),
    venueId: z.string().min(1, 'Mekan seçilmelidir.'),
    hallId: z.string().min(1, 'Salon seçilmelidir.'),
    eventDate: z.string().min(1, 'Etkinlik tarihi zorunludur.'),
    salesStartDate: z.string().min(1, 'Satış başlangıcı zorunludur.'),
    salesEndDate: z.string().min(1, 'Satış bitişi zorunludur.'),
    durationMinutes: z.coerce.number().int().min(1, 'Süre en az 1 dakika olmalı.').max(1440),
    maxTicketsPerUser: z.coerce.number().int().min(1, 'En az 1 olmalı.').max(50),
    minimumAge: z.coerce.number().int().min(0).max(99),
  })
  // PDF sayfa 13'teki üç tarih kuralı. Bunlar tek tek alanlara
  // yazılamıyor çünkü alanlar BİRBİRİNE bağlı; refine bunun için var.
  .refine((d) => new Date(d.salesStartDate) < new Date(d.salesEndDate), {
    message: 'Satış başlangıcı, satış bitişinden sonra olamaz.',
    path: ['salesEndDate'],
  })
  .refine((d) => new Date(d.salesEndDate) <= new Date(d.eventDate), {
    message: 'Satış bitişi, etkinlik başlangıcından sonra olamaz.',
    path: ['salesEndDate'],
  })

type Form = z.input<typeof sema>

export function EventCreatePage() {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [sunucuHatasi, setSunucuHatasi] = useState<string | null>(null)

  const {
    register,
    handleSubmit,
    control,
    formState: { errors, isSubmitting },
  } = useForm<Form>({
    resolver: zodResolver(sema),
    defaultValues: { durationMinutes: 120, maxTicketsPerUser: 4, minimumAge: 0 },
  })

  const sehirler = useQuery({
    queryKey: ['cities'],
    queryFn: bookingApi.getCities,
    staleTime: 60 * 60 * 1000,
  })

  const kategoriler = useQuery({
    queryKey: ['categories'],
    queryFn: bookingApi.getCategories,
    staleTime: 60 * 60 * 1000,
  })

  const mekanlar = useQuery({
    queryKey: ['venues', 'hepsi'],
    queryFn: () => adminApi.getVenues({ pageSize: 100 }),
  })

  // Salon listesi seçilen mekana bağlı. Mekan seçilmeden salon
  // seçtirmek anlamsız; kullanıcı 40 salonluk bir listede kendi
  // mekanının salonunu aramak zorunda kalırdı.
  // watch() yerine useWatch: oxlint react(incompatible-library)
  // uyarisi veriyordu. watch() her render'da yeni bir fonksiyon
  // donduruyor ve React Compiler bu bileseni memoize etmekten
  // vazgeciyor. useWatch abone olup yalnizca o alan degisince
  // yeniden ciziyor -- hem uyari gidiyor hem daha az render.
  const secilenMekan = useWatch({ control, name: 'venueId' })
  const mekanDetay = useQuery({
    queryKey: ['venue', secilenMekan],
    queryFn: () => adminApi.getVenue(secilenMekan),
    enabled: Boolean(secilenMekan),
  })

  const olustur = useMutation({
    mutationFn: (d: Form) =>
      organizerApi.createEvent({
        title: d.title,
        description: d.description,
        categoryId: d.categoryId,
        cityId: d.cityId,
        venueId: d.venueId,
        hallId: d.hallId,
        // datetime-local "2026-10-27T20:00" veriyor: saat dilimi yok.
        // Backend DateTimeOffset bekliyor. new Date(...) değeri
        // KULLANICININ diliminde yorumlayıp toISOString ile UTC'ye
        // çeviriyor -- yani "20:00" yazan organizatör kendi saatiyle
        // 20:00 demiş oluyor. Ham dizeyi göndersem sunucu onu UTC
        // sanar ve etkinlik üç saat kayardı.
        eventDate: new Date(d.eventDate).toISOString(),
        salesStartDate: new Date(d.salesStartDate).toISOString(),
        salesEndDate: new Date(d.salesEndDate).toISOString(),
        durationMinutes: Number(d.durationMinutes),
        maxTicketsPerUser: Number(d.maxTicketsPerUser),
        minimumAge: Number(d.minimumAge),
      }),
    onSuccess: (id) => {
      queryClient.invalidateQueries({ queryKey: ['events'] })
      navigate(`/panel/etkinlikler/${id}`)
    },
    onError: (e) => setSunucuHatasi(toProblem(e).detail ?? 'Etkinlik oluşturulamadı.'),
  })

  const alanSinifi =
    'w-full rounded-[4px] border border-slate-300 px-3 py-2.5 text-sm outline-none ' +
    'transition-colors focus:border-brand-500'

  return (
    <div className="min-h-screen bg-slate-100">
      <SiteHeader />

      <main className="mx-auto max-w-3xl px-4 py-8">
        <Link to="/panel/etkinlikler" className="text-sm text-brand-600 hover:underline">
          &larr; Etkinliklerim
        </Link>

        <h1 className="mt-3 font-display text-2xl font-bold tracking-tight text-slate-900">
          Yeni etkinlik
        </h1>
        <p className="mt-1 text-sm text-slate-500">
          Etkinlik taslak olarak oluşturulur. Oturum ve bilet türlerini ekledikten sonra onaya
          gönderebilirsiniz.
        </p>

        {sunucuHatasi && (
          <div className="mt-4">
            <Alert variant="error">{sunucuHatasi}</Alert>
          </div>
        )}

        <form
          onSubmit={handleSubmit((d) => {
            setSunucuHatasi(null)
            olustur.mutate(d)
          })}
          className="mt-6 space-y-5 rounded-[4px] border border-slate-300 bg-white p-6"
          noValidate
        >
          <Input label="Başlık" error={errors.title?.message} {...register('title')} />

          <div className="space-y-1.5">
            <label htmlFor="aciklama" className="label-xs block text-slate-500">
              Açıklama
            </label>
            <textarea
              id="aciklama"
              rows={4}
              className={alanSinifi}
              aria-invalid={errors.description ? true : undefined}
              {...register('description')}
            />
            {errors.description && (
              <p role="alert" className="text-sm text-red-600">
                {errors.description.message}
              </p>
            )}
          </div>

          <div className="grid gap-5 sm:grid-cols-2">
            <Secim
              id="kategori"
              etiket="Kategori"
              hata={errors.categoryId?.message}
              secenekler={(kategoriler.data ?? []).map((k) => ({ id: k.id, ad: k.name }))}
              {...register('categoryId')}
            />
            <Secim
              id="sehir"
              etiket="Şehir"
              hata={errors.cityId?.message}
              secenekler={(sehirler.data ?? []).map((s) => ({ id: s.id, ad: s.name }))}
              {...register('cityId')}
            />
            <Secim
              id="mekan"
              etiket="Mekan"
              hata={errors.venueId?.message}
              secenekler={(mekanlar.data?.items ?? []).map((m) => ({
                id: m.id,
                ad: `${m.name} (${m.cityName})`,
              }))}
              {...register('venueId')}
            />
            <Secim
              id="salon"
              etiket="Salon"
              hata={errors.hallId?.message}
              yardim={!secilenMekan ? 'Önce mekan seçin.' : undefined}
              secenekler={(mekanDetay.data?.halls ?? []).map((h) => ({
                id: h.id,
                ad: `${h.name} (${h.capacity} kişi)`,
              }))}
              {...register('hallId')}
            />
          </div>

          <div className="grid gap-5 sm:grid-cols-3">
            <TarihAlani
              id="etkinlikTarihi"
              etiket="Etkinlik tarihi"
              hata={errors.eventDate?.message}
              {...register('eventDate')}
            />
            <TarihAlani
              id="satisBas"
              etiket="Satış başlangıcı"
              hata={errors.salesStartDate?.message}
              {...register('salesStartDate')}
            />
            <TarihAlani
              id="satisBit"
              etiket="Satış bitişi"
              hata={errors.salesEndDate?.message}
              {...register('salesEndDate')}
            />
          </div>

          <div className="grid gap-5 sm:grid-cols-3">
            <Input
              label="Süre (dakika)"
              type="number"
              error={errors.durationMinutes?.message}
              {...register('durationMinutes')}
            />
            <Input
              label="Kişi başı bilet limiti"
              type="number"
              error={errors.maxTicketsPerUser?.message}
              {...register('maxTicketsPerUser')}
            />
            <Input
              label="Yaş sınırı (0 = yok)"
              type="number"
              error={errors.minimumAge?.message}
              {...register('minimumAge')}
            />
          </div>

          <div className="flex gap-2 border-t border-slate-200 pt-4">
            <Button type="submit" isLoading={isSubmitting || olustur.isPending}>
              Taslak oluştur
            </Button>
            <Button
              type="button"
              variant="secondary"
              onClick={() => navigate('/panel/etkinlikler')}
            >
              Vazgeç
            </Button>
          </div>
        </form>
      </main>
    </div>
  )
}

/**
 * Açılır liste.
 *
 * forwardRef gerekiyor: React Hook Form register() bir ref
 * döndürüyor ve onu gerçek <select>'e ulaştırmazsam form alanın
 * değerini hiç görmüyor.
 */
interface SecimProps extends React.SelectHTMLAttributes<HTMLSelectElement> {
  id: string
  etiket: string
  hata?: string
  yardim?: string
  secenekler: { id: string; ad: string }[]
}

const Secim = ({ id, etiket, hata, yardim, secenekler, ...props }: SecimProps) => (
  <div className="space-y-1.5">
    <label htmlFor={id} className="label-xs block text-slate-500">
      {etiket}
    </label>
    <select
      id={id}
      className="w-full rounded-[4px] border border-slate-300 px-3 py-2.5 text-sm outline-none transition-colors focus:border-brand-500"
      aria-invalid={hata ? true : undefined}
      {...props}
    >
      <option value="">Seçin</option>
      {secenekler.map((s) => (
        <option key={s.id} value={s.id}>
          {s.ad}
        </option>
      ))}
    </select>
    {yardim && <p className="text-xs text-slate-400">{yardim}</p>}
    {hata && (
      <p role="alert" className="text-sm text-red-600">
        {hata}
      </p>
    )}
  </div>
)

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
