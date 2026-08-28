import { useState } from 'react'
import { useParams } from 'react-router-dom'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { useForm } from 'react-hook-form'
import { adminApi } from '../api/adminApi'
import { toProblem } from '../../../lib/api/client'
import { Button } from '../../../components/ui/Button'
import { Input } from '../../../components/ui/Input'
import { Alert } from '../../../components/ui/Alert'
import { AdminLayout } from '../components/AdminLayout'
import { SeatMap } from '../components/SeatMap'

/**
 * Oturma plani tasarlama ekrani.
 * PDF Sprint 4: "Bolum ekleme", "Sira ve koltuk olusturma",
 * "Gorsel koltuk plani", "Plan onizleme".
 */
export function SeatLayoutPage() {
  const { layoutId } = useParams<{ layoutId: string }>()
  const queryClient = useQueryClient()
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)

  const layoutQuery = useQuery({
    queryKey: ['seat-layout', layoutId],
    queryFn: () => adminApi.getSeatLayout(layoutId!),
    enabled: Boolean(layoutId),
  })

  const sectionForm = useForm<{ name: string; colorHex: string }>({
    defaultValues: { name: '', colorHex: '#4f46e5' },
  })

  const seatsForm = useForm<{
    sectionId: string
    rowCount: number
    seatsPerRow: number
    useLetters: boolean
  }>({
    defaultValues: { sectionId: '', rowCount: 10, seatsPerRow: 20, useLetters: true },
  })

  const addSection = useMutation({
    mutationFn: (data: { name: string; colorHex: string }) =>
      adminApi.addSection(layoutId!, {
        name: data.name,
        // Yeni bolum en sona eklensin.
        displayOrder: (layoutQuery.data?.sections.length ?? 0) + 1,
        colorHex: data.colorHex,
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['seat-layout', layoutId] })
      sectionForm.reset()
      setError(null)
    },
    onError: (e) => setError(toProblem(e).detail ?? 'Bolum eklenemedi.'),
  })

  const generateSeats = useMutation({
    mutationFn: (data: {
      sectionId: string
      rowCount: number
      seatsPerRow: number
      useLetters: boolean
    }) =>
      adminApi.generateSeats(layoutId!, {
        sectionId: data.sectionId,
        rowCount: data.rowCount,
        seatsPerRow: data.seatsPerRow,
        rowLabels: data.useLetters ? buildRowLabels(data.rowCount) : undefined,
      }),
    onSuccess: (count) => {
      queryClient.invalidateQueries({ queryKey: ['seat-layout', layoutId] })
      setNotice(`${count} koltuk uretildi.`)
      setError(null)
    },
    onError: (e) => {
      const problem = toProblem(e)

      // Alan bazli dogrulama hatalarini da gosteriyorum.
      // Yalnizca `detail` gosterseydik "Gonderilen veriler gecerli
      // degil" gibi hicbir sey anlatmayan bir mesaj cikardi.
      const fieldErrors = problem.errors ? Object.values(problem.errors).flat().join(' ') : null

      setError(fieldErrors ?? problem.detail ?? 'Koltuklar uretilemedi.')
      setNotice(null)
    },
  })

  if (layoutQuery.isPending) {
    return (
      <AdminLayout title="Yukleniyor...">
        <div className="h-64 animate-pulse rounded-xl bg-slate-200" aria-busy="true" />
      </AdminLayout>
    )
  }

  if (layoutQuery.isError || !layoutQuery.data) {
    return (
      <AdminLayout title="Plan bulunamadi">
        <Alert variant="error">
          {toProblem(layoutQuery.error).detail ?? 'Oturma plani yuklenemedi.'}
        </Alert>
      </AdminLayout>
    )
  }

  const layout = layoutQuery.data
  const remainingCapacity = layout.hallCapacity - layout.totalSeatCount

  return (
    <AdminLayout
      title={layout.name}
      subtitle={`${layout.hallName} - ${layout.totalSeatCount} / ${layout.hallCapacity} koltuk`}
      backTo={{ label: 'Oturma planlari', to: `/admin/salonlar/${layout.hallId}` }}
    >
      {/* Plan kullanimdaysa DUZENLEME FORMLARINI HIC GOSTERMIYORUM.
          Gosterip sonra hata vermek kullaniciyi bosuna ugrastirir. */}
      {layout.isInUse && (
        <div className="mb-6">
          <Alert variant="info">
            Bu plan bir etkinlik oturumunda kullaniliyor. Yapisi degistirilemez; yalnizca
            goruntuleyebilirsiniz.
          </Alert>
        </div>
      )}

      {error && (
        <div className="mb-4">
          <Alert variant="error">{error}</Alert>
        </div>
      )}
      {notice && (
        <div className="mb-4">
          <Alert variant="success">{notice}</Alert>
        </div>
      )}

      {!layout.isInUse && (
        <div className="mb-8 grid gap-6 lg:grid-cols-2">
          {/* ---- Bolum ekleme ---- */}
          <form
            onSubmit={sectionForm.handleSubmit((d) => addSection.mutate(d))}
            className="space-y-4 rounded-xl border border-slate-200 bg-white p-6"
            noValidate
          >
            <h2 className="text-sm font-semibold text-slate-900">1. Bolum ekle</h2>

            <Input
              label="Bolum adi"
              placeholder="Orta Blok"
              error={sectionForm.formState.errors.name?.message}
              {...sectionForm.register('name', { required: 'Bolum adi zorunludur.' })}
            />

            <div className="space-y-1.5">
              <label htmlFor="colorHex" className="block text-sm font-medium text-slate-700">
                Renk
              </label>

              <div className="flex items-center gap-3">
                {/* type="color" tarayicinin renk secicisini acar ve
                    HER ZAMAN gecerli #RRGGBB uretir. Metin girisi
                    kullansaydik backend'in regex dogrulamasina takilan
                    girdiler olusabilirdi. */}
                <input
                  id="colorHex"
                  type="color"
                  className="h-10 w-16 cursor-pointer rounded border border-slate-300"
                  {...sectionForm.register('colorHex')}
                />
                <span className="text-sm text-slate-500">Koltuk haritasinda bu bolumun rengi</span>
              </div>
            </div>

            <Button type="submit" isLoading={addSection.isPending}>
              Bolum ekle
            </Button>
          </form>

          {/* ---- Koltuk uretimi ---- */}
          <form
            onSubmit={seatsForm.handleSubmit((d) => generateSeats.mutate(d))}
            className="space-y-4 rounded-xl border border-slate-200 bg-white p-6"
            noValidate
          >
            <h2 className="text-sm font-semibold text-slate-900">2. Koltuk uret</h2>

            {layout.sections.length === 0 ? (
              <p className="text-sm text-slate-500">Once bir bolum eklemelisiniz.</p>
            ) : (
              <>
                <div className="space-y-1.5">
                  <label htmlFor="sectionId" className="block text-sm font-medium text-slate-700">
                    Bolum
                  </label>

                  <select
                    id="sectionId"
                    className="w-full rounded-lg border border-slate-300 px-3 py-2.5 text-sm outline-none focus:border-brand-500"
                    {...seatsForm.register('sectionId', { required: true })}
                  >
                    <option value="">Bolum secin</option>
                    {layout.sections.map((s) => (
                      <option key={s.id} value={s.id} disabled={s.seatCount > 0}>
                        {s.name}
                        {s.seatCount > 0 ? ` (${s.seatCount} koltuk mevcut)` : ''}
                      </option>
                    ))}
                  </select>
                </div>

                <div className="grid grid-cols-2 gap-4">
                  <Input
                    label="Sira sayisi"
                    type="number"
                    {...seatsForm.register('rowCount', { valueAsNumber: true, min: 1, max: 500 })}
                  />
                  <Input
                    label="Sira basina koltuk"
                    type="number"
                    {...seatsForm.register('seatsPerRow', {
                      valueAsNumber: true,
                      min: 1,
                      max: 500,
                    })}
                  />
                </div>

                <label className="flex items-center gap-2 text-sm text-slate-700">
                  <input
                    type="checkbox"
                    className="rounded"
                    {...seatsForm.register('useLetters')}
                  />
                  Siralari harfle adlandir (A, B, C...)
                </label>

                {/* Kalan kapasiteyi ONCEDEN gosteriyorum.
                    Backend zaten reddedecek ama kullanici 900 koltuk
                    girip "kapasite asildi" hatasi almadan once
                    sinirini bilmeli. */}
                <p className="text-xs text-slate-500">
                  Kalan kapasite: <strong>{remainingCapacity}</strong> koltuk
                </p>

                <Button type="submit" isLoading={generateSeats.isPending}>
                  Koltuklari uret
                </Button>
              </>
            )}
          </form>
        </div>
      )}

      <h2 className="mb-3 text-lg font-semibold text-slate-900">Onizleme</h2>

      <SeatMap sections={layout.sections} />
    </AdminLayout>
  )
}

/**
 * Sira etiketlerini uretir: A, B, C ... Z, AA, AB ...
 *
 * KARISABILECEK HARFLERI ATLIYORUM: I, O, Q.
 * Sebep: gercek salonlarda gorevli "I sirasi mi 1 sirasi mi?" diye
 * sorar; kullanici da bilette "O" mu "0" mi ayirt edemez. Bilet
 * numarasi uretiminde de ayni mantigi uygulamistik.
 */
function buildRowLabels(count: number): string[] {
  const alphabet = 'ABCDEFGHJKLMNPRSTUVYZ'
  const labels: string[] = []

  for (let i = 0; i < count; i++) {
    if (i < alphabet.length) {
      labels.push(alphabet[i])
    } else {
      // 21'den sonra iki harfli: AA, AB, AC...
      const first = alphabet[Math.floor(i / alphabet.length) - 1]
      const second = alphabet[i % alphabet.length]
      labels.push(`${first}${second}`)
    }
  }

  return labels
}
