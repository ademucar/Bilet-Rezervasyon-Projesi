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

/** Salonun oturma planlari. PDF Sprint 4: "Oturma plani tasarlama ekrani". */
export function HallDetailPage() {
  const { hallId } = useParams<{ hallId: string }>()
  const queryClient = useQueryClient()
  const [formError, setFormError] = useState<string | null>(null)

  const layoutsQuery = useQuery({
    queryKey: ['seat-layouts', hallId],
    queryFn: () => adminApi.getSeatLayouts(hallId!),
    enabled: Boolean(hallId),
  })

  const { register, handleSubmit, reset, formState: { errors } } =
    useForm<{ name: string; description?: string }>({
      defaultValues: { name: '', description: '' },
    })

  const createLayout = useMutation({
    mutationFn: (data: { name: string; description?: string }) =>
      adminApi.createSeatLayout(hallId!, data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['seat-layouts', hallId] })
      reset()
      setFormError(null)
    },
    onError: (error) => setFormError(toProblem(error).detail ?? 'Plan olusturulamadi.'),
  })

  return (
    <AdminLayout
      title="Oturma planlari"
      subtitle="Bir salonun birden fazla duzeni olabilir (konser, tiyatro...)"
      backTo={{ label: 'Mekanlar', to: '/admin/mekanlar' }}
    >
      <form
        onSubmit={handleSubmit((data) => {
          setFormError(null)
          createLayout.mutate({ name: data.name, description: data.description || undefined })
        })}
        className="mb-6 space-y-4 rounded-xl border border-slate-200 bg-white p-6"
        noValidate
      >
        <h2 className="text-sm font-semibold text-slate-900">Yeni oturma plani</h2>

        {formError && <Alert variant="error">{formError}</Alert>}

        <Input
          label="Plan adi"
          placeholder="Konser Duzeni"
          error={errors.name?.message}
          {...register('name', { required: 'Plan adi zorunludur.' })}
        />

        <Input
          label="Aciklama (istege bagli)"
          placeholder="Sahne onu ayakta, arkasi koltuklu"
          {...register('description')}
        />

        <Button type="submit" isLoading={createLayout.isPending}>Olustur</Button>
      </form>

      {layoutsQuery.isPending && (
        <div className="h-32 animate-pulse rounded-xl bg-slate-200" aria-busy="true" />
      )}

      {layoutsQuery.data?.length === 0 && (
        <div className="rounded-xl border border-dashed border-slate-300 bg-slate-50 p-12 text-center">
          <p className="text-sm text-slate-500">Bu salonda henuz oturma plani yok.</p>
        </div>
      )}

      {layoutsQuery.data && layoutsQuery.data.length > 0 && (
        <ul className="space-y-2">
          {layoutsQuery.data.map((l) => (
            <li key={l.id}>
              <Link
                to={`/admin/oturma-planlari/${l.id}`}
                className="flex items-center justify-between rounded-xl border border-slate-200 bg-white p-4 transition-colors hover:border-brand-300"
              >
                <div>
                  <p className="font-medium text-slate-900">{l.name}</p>
                  <p className="text-sm text-slate-500">
                    {l.sectionCount} bolum &middot; {l.seatCount} koltuk
                  </p>
                </div>

                {/* isInUse bilgisi KULLANICIYA GOSTERILIYOR.
                    Backend kullanilan plani degistirmeye izin vermiyor;
                    kullanici bunu ancak hata alinca ogrenecek olsaydi
                    sinirlenirdi. Onceden bildirmek daha iyi. */}
                {l.isInUse && (
                  <span className="rounded-full bg-amber-100 px-2.5 py-1 text-xs font-medium text-amber-800">
                    Kullanimda &middot; degistirilemez
                  </span>
                )}
              </Link>
            </li>
          ))}
        </ul>
      )}
    </AdminLayout>
  )
}
