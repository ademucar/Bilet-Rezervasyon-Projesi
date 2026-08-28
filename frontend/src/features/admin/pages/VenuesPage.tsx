import { useState } from 'react'
import { Link } from 'react-router-dom'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { useForm } from 'react-hook-form'
import { adminApi } from '../api/adminApi'
import { toProblem } from '../../../lib/api/client'
import { Button } from '../../../components/ui/Button'
import { Input } from '../../../components/ui/Input'
import { Alert } from '../../../components/ui/Alert'
import { AdminLayout } from '../components/AdminLayout'

interface VenueForm {
  name: string
  address: string
  cityId: string
}

/** Mekan listesi ve oluşturma. PDF Sprint 4: "Mekan listeleme". */
export function VenuesPage() {
  const queryClient = useQueryClient()
  const [search, setSearch] = useState('')
  const [showForm, setShowForm] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)

  const citiesQuery = useQuery({
    queryKey: ['cities'],
    queryFn: adminApi.getCities,
    // Şehirler neredeyse hiç degismez. 24 saat taze say.
    // Varsayılan staleTime 1 dakika olsaydı kullanıcı her sayfa
    // gecisinde aynı 20 şehri tekrar indirirdi.
    staleTime: 24 * 60 * 60 * 1000,
  })

  const venuesQuery = useQuery({
    // queryKey'e `search` DAHIL: arama degistiginde TanStack Query
    // bunu yeni bir sorgu sayar ve otomatik yeniden ceker.
    // Dahil etmeseydik arama yazdikca sonuclar guncellenmezdi.
    queryKey: ['venues', search],
    queryFn: () => adminApi.getVenues({ search: search || undefined }),
  })

  const {
    register,
    handleSubmit,
    reset,
    formState: { errors },
  } = useForm<VenueForm>({
    defaultValues: { name: '', address: '', cityId: '' },
  })

  const createMutation = useMutation({
    mutationFn: adminApi.createVenue,
    onSuccess: () => {
      // Listeyi geçersiz kil -> TanStack Query otomatik yeniden ceker.
      //
      // Alternatif, donen Id ile listeyi ELLE guncellemekti. Onu
      // yapmadim çünkü liste sunucuda siralaniyor ve sayfalaniyor;
      // elle ekleme yanlış sırada gosterebilir veya sayfa sinirlarini
      // bozabilir. Yeniden cekmek daha basit ve her zaman doğru.
      queryClient.invalidateQueries({ queryKey: ['venues'] })
      reset()
      setShowForm(false)
      setFormError(null)
    },
    onError: (error) => setFormError(toProblem(error).detail ?? 'Mekan oluşturulamadı.'),
  })

  return (
    <AdminLayout title="Mekanlar" subtitle="Etkinlik mekanlarını ve salonlarını yönetin">
      <div className="mb-6 flex flex-wrap items-center justify-between gap-3">
        <input
          type="search"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="Mekan ara..."
          aria-label="Mekan ara"
          className="w-full max-w-xs rounded-lg border border-slate-300 px-3 py-2 text-sm outline-none focus:border-brand-500"
        />

        <Button onClick={() => setShowForm((v) => !v)}>{showForm ? 'Vazgeç' : 'Yeni mekan'}</Button>
      </div>

      {showForm && (
        <form
          onSubmit={handleSubmit((data) => {
            setFormError(null)
            createMutation.mutate(data)
          })}
          className="mb-6 space-y-4 rounded-xl border border-slate-200 bg-white p-6"
          noValidate
        >
          <h2 className="text-sm font-semibold text-slate-900">Yeni mekan ekle</h2>

          {formError && <Alert variant="error">{formError}</Alert>}

          <Input
            label="Mekan adı"
            error={errors.name?.message}
            {...register('name', { required: 'Mekan adı zorunludur.' })}
          />

          <Input
            label="Adres"
            error={errors.address?.message}
            {...register('address', { required: 'Adres zorunludur.' })}
          />

          <div className="space-y-1.5">
            <label htmlFor="cityId" className="block text-sm font-medium text-slate-700">
              Şehir
            </label>

            <select
              id="cityId"
              className="w-full rounded-lg border border-slate-300 px-3 py-2.5 text-sm outline-none focus:border-brand-500"
              {...register('cityId', { required: 'Şehir seçilmelidir.' })}
            >
              <option value="">Şehir seçin</option>
              {citiesQuery.data?.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name}
                </option>
              ))}
            </select>

            {errors.cityId && (
              <p role="alert" className="text-sm text-red-600">
                {errors.cityId.message}
              </p>
            )}
          </div>

          <Button type="submit" isLoading={createMutation.isPending}>
            Kaydet
          </Button>
        </form>
      )}

      {/* PDF Sprint 18: loading, empty ve error state'lerinin UCU DE olmalı. */}
      {venuesQuery.isPending && <SkeletonList />}

      {venuesQuery.isError && (
        <Alert variant="error">
          {toProblem(venuesQuery.error).detail ?? 'Mekanlar yüklenemedi.'}
        </Alert>
      )}

      {venuesQuery.data?.items.length === 0 && (
        <div className="rounded-xl border border-dashed border-slate-300 bg-slate-50 p-12 text-center">
          <p className="text-sm text-slate-500">
            {search ? `"${search}" için sonuç bulunamadı.` : 'Henüz mekan eklenmemiş.'}
          </p>
        </div>
      )}

      {venuesQuery.data && venuesQuery.data.items.length > 0 && (
        <ul className="space-y-2">
          {venuesQuery.data.items.map((v) => (
            <li key={v.id}>
              <Link
                to={`/admin/mekanlar/${v.id}`}
                className="flex items-center justify-between rounded-xl border border-slate-200 bg-white p-4 transition-colors hover:border-brand-300"
              >
                <div>
                  <p className="font-medium text-slate-900">{v.name}</p>
                  <p className="text-sm text-slate-500">{v.cityName}</p>
                </div>

                <span className="text-sm text-slate-500">{v.hallCount} salon</span>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </AdminLayout>
  )
}

/** PDF Sprint 18: "Skeleton loading hazırlanmalıdır." */
function SkeletonList() {
  return (
    <ul className="space-y-2" aria-busy="true" aria-label="Yükleniyor">
      {[1, 2, 3].map((i) => (
        <li key={i} className="h-[74px] animate-pulse rounded-xl bg-slate-200" />
      ))}
    </ul>
  )
}
