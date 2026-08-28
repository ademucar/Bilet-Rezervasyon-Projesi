import { useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { useForm } from 'react-hook-form'
import { adminApi } from '../api/adminApi'
import { toProblem } from '../../../lib/api/client'
import { Button } from '../../../components/ui/Button'
import { Input } from '../../../components/ui/Input'
import { Alert } from '../../../components/ui/Alert'
import { AdminLayout } from '../components/AdminLayout'

interface HallForm {
  name: string
  capacity: number
}

/** Mekan detayı ve salon yönetimi. PDF Sprint 4: "Salon yönetimi". */
export function VenueDetailPage() {
  const { venueId } = useParams<{ venueId: string }>()
  const queryClient = useQueryClient()
  const [showForm, setShowForm] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)

  const venueQuery = useQuery({
    queryKey: ['venue', venueId],
    queryFn: () => adminApi.getVenue(venueId!),
    // enabled: venueId yoksa sorguyu HİÇ calistirma.
    //
    // Bu olmasaydı `/venues/undefined` gibi anlamsiz bir istek gider
    // ve 404 alırdık. Kullanıcı bunu "sayfa bozuk" olarak gorurdu.
    enabled: Boolean(venueId),
  })

  const {
    register,
    handleSubmit,
    reset,
    formState: { errors },
  } = useForm<HallForm>({
    defaultValues: { name: '', capacity: 500 },
  })

  const createHall = useMutation({
    mutationFn: (data: HallForm) => adminApi.createHall(venueId!, data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['venue', venueId] })
      reset()
      setShowForm(false)
      setFormError(null)
    },
    onError: (error) => setFormError(toProblem(error).detail ?? 'Salon oluşturulamadı.'),
  })

  if (venueQuery.isPending) {
    return (
      <AdminLayout title="Yükleniyor...">
        <div className="h-40 animate-pulse rounded-xl bg-slate-200" aria-busy="true" />
      </AdminLayout>
    )
  }

  if (venueQuery.isError || !venueQuery.data) {
    return (
      <AdminLayout title="Mekan bulunamadı" backTo={{ label: 'Mekanlar', to: '/admin/mekanlar' }}>
        <Alert variant="error">{toProblem(venueQuery.error).detail ?? 'Mekan yüklenemedi.'}</Alert>
      </AdminLayout>
    )
  }

  const venue = venueQuery.data

  return (
    <AdminLayout
      title={venue.name}
      subtitle={`${venue.cityName} - ${venue.address}`}
      backTo={{ label: 'Mekanlar', to: '/admin/mekanlar' }}
    >
      <div className="mb-4 flex items-center justify-between">
        <h2 className="text-lg font-semibold text-slate-900">Salonlar</h2>

        <Button onClick={() => setShowForm((v) => !v)}>{showForm ? 'Vazgeç' : 'Yeni salon'}</Button>
      </div>

      {showForm && (
        <form
          onSubmit={handleSubmit((data) => {
            setFormError(null)
            // valueAsNumber ile capacity zaten sayi geliyor.
            createHall.mutate(data)
          })}
          className="mb-6 space-y-4 rounded-xl border border-slate-200 bg-white p-6"
          noValidate
        >
          {formError && <Alert variant="error">{formError}</Alert>}

          <Input
            label="Salon adı"
            error={errors.name?.message}
            {...register('name', { required: 'Salon adı zorunludur.' })}
          />

          <Input
            label="Kapasite"
            type="number"
            error={errors.capacity?.message}
            {...register('capacity', {
              // valueAsNumber ŞART: HTML input her zaman STRING döner.
              // Bu olmasaydı backend'e "500" (metin) gonderirdik ve
              // model binding hatası alırdık.
              valueAsNumber: true,
              required: 'Kapasite zorunludur.',
              min: { value: 1, message: 'Kapasite sıfırdan büyük olmalıdır.' },
              max: { value: 200000, message: 'Kapasite 200.000 aşamaz.' },
            })}
          />

          <Button type="submit" isLoading={createHall.isPending}>
            Kaydet
          </Button>
        </form>
      )}

      {venue.halls.length === 0 ? (
        <div className="rounded-xl border border-dashed border-slate-300 bg-slate-50 p-12 text-center">
          <p className="text-sm text-slate-500">
            Bu mekanda henüz salon yok. Etkinlik oluşturabilmek için en az bir salon gerekir.
          </p>
        </div>
      ) : (
        <ul className="space-y-2">
          {venue.halls.map((h) => (
            <li key={h.id}>
              <Link
                to={`/admin/salonlar/${h.id}`}
                className="flex items-center justify-between rounded-xl border border-slate-200 bg-white p-4 transition-colors hover:border-brand-300"
              >
                <div>
                  <p className="font-medium text-slate-900">{h.name}</p>
                  <p className="text-sm text-slate-500">{h.capacity} kisilik</p>
                </div>

                <span className="text-sm text-slate-500">{h.seatLayoutCount} oturma plani</span>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </AdminLayout>
  )
}
