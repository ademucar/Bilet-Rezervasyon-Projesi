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
 * Oturma planı tasarlama ekrani.
 * PDF Sprint 4: "Bölüm ekleme", "Sıra ve koltuk oluşturma",
 * "Görsel koltuk planı", "Plan önizleme".
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
        // Yeni bölüm en sona eklensin.
        displayOrder: (layoutQuery.data?.sections.length ?? 0) + 1,
        colorHex: data.colorHex,
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['seat-layout', layoutId] })
      sectionForm.reset()
      setError(null)
    },
    onError: (e) => setError(toProblem(e).detail ?? 'Bölüm eklenemedi.'),
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
      setNotice(`${count} koltuk üretildi.`)
      setError(null)
    },
    onError: (e) => {
      const problem = toProblem(e)

      // Alan bazlı doğrulama hatalarini da gösteriyorum.
      // Yalnızca `detail` gosterseydim "Gonderilen veriler geçerli
      // değil" gibi hiçbir sey anlatmayan bir mesaj çıkardı.
      const fieldErrors = problem.errors ? Object.values(problem.errors).flat().join(' ') : null

      setError(fieldErrors ?? problem.detail ?? 'Koltuklar üretilemedi.')
      setNotice(null)
    },
  })

  if (layoutQuery.isPending) {
    return (
      <AdminLayout title="Yükleniyor...">
        <div className="h-64 animate-pulse rounded-[4px] bg-slate-200" aria-busy="true" />
      </AdminLayout>
    )
  }

  if (layoutQuery.isError || !layoutQuery.data) {
    return (
      <AdminLayout title="Plan bulunamadı">
        <Alert variant="error">
          {toProblem(layoutQuery.error).detail ?? 'Oturma planı yüklenemedi.'}
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
      backTo={{ label: 'Oturma planları', to: `/admin/salonlar/${layout.hallId}` }}
    >
      {/* Plan kullanimdaysa DUZENLEME FORMLARINI HİÇ GOSTERMIYORUM.
          Gosterip sonra hata vermek kullanıcıyı boşuna ugrastirir. */}
      {layout.isInUse && (
        <div className="mb-6">
          <Alert variant="info">
            Bu plan bir etkinlik oturumunda kullanılıyor. Yapısı değiştirilemez; yalnızca
            görüntüleyebilirsiniz.
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
          {/* ---- Bölüm ekleme ---- */}
          <form
            onSubmit={sectionForm.handleSubmit((d) => addSection.mutate(d))}
            className="space-y-4 rounded-[4px] border border-slate-300 bg-white p-6"
            noValidate
          >
            <h2 className="text-sm font-semibold text-slate-900">1. Bölüm ekle</h2>

            <Input
              label="Bölüm adı"
              placeholder="Orta Blok"
              error={sectionForm.formState.errors.name?.message}
              {...sectionForm.register('name', { required: 'Bölüm adı zorunludur.' })}
            />

            <div className="space-y-1.5">
              <label htmlFor="colorHex" className="block text-sm font-medium text-slate-700">
                Renk
              </label>

              <div className="flex items-center gap-3">
                {/* type="color" tarayıcının renk secicisini acar ve
                    HER ZAMAN geçerli #RRGGBB üretir. Metin girişi
                    kullansaydık backend'in regex dogrulamasina takilan
                    girdiler olusabilirdi. */}
                <input
                  id="colorHex"
                  type="color"
                  className="h-10 w-16 cursor-pointer rounded border border-slate-300"
                  {...sectionForm.register('colorHex')}
                />
                <span className="text-sm text-slate-500">Koltuk haritasında bu bölümün rengi</span>
              </div>
            </div>

            <Button type="submit" isLoading={addSection.isPending}>
              Bölüm ekle
            </Button>
          </form>

          {/* ---- Koltuk üretimi ---- */}
          <form
            onSubmit={seatsForm.handleSubmit((d) => generateSeats.mutate(d))}
            className="space-y-4 rounded-[4px] border border-slate-300 bg-white p-6"
            noValidate
          >
            <h2 className="text-sm font-semibold text-slate-900">2. Koltuk üret</h2>

            {layout.sections.length === 0 ? (
              <p className="text-sm text-slate-500">Önce bir bölüm eklemelisiniz.</p>
            ) : (
              <>
                <div className="space-y-1.5">
                  <label htmlFor="sectionId" className="block text-sm font-medium text-slate-700">
                    Bölüm
                  </label>

                  <select
                    id="sectionId"
                    className="w-full rounded-lg border border-slate-300 px-3 py-2.5 text-sm outline-none focus:border-brand-500"
                    {...seatsForm.register('sectionId', { required: true })}
                  >
                    <option value="">Bölüm seçin</option>
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
                    label="Sıra sayısı"
                    type="number"
                    {...seatsForm.register('rowCount', { valueAsNumber: true, min: 1, max: 500 })}
                  />
                  <Input
                    label="Sıra başına koltuk"
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
                  Sıraları harfle adlandır (A, B, C...)
                </label>

                {/* Kalan kapasiteyi ONCEDEN gösteriyorum.
                    Backend zaten reddedecek ama kullanıcı 900 koltuk
                    girip "kapasite aşıldı" hatası almadan önce
                    sinirini bilmeli. */}
                <p className="text-xs text-slate-500">
                  Kalan kapasite: <strong>{remainingCapacity}</strong> koltuk
                </p>

                <Button type="submit" isLoading={generateSeats.isPending}>
                  Koltukları üret
                </Button>
              </>
            )}
          </form>
        </div>
      )}

      <h2 className="mb-3 text-lg font-semibold text-slate-900">Önizleme</h2>

      <SeatMap sections={layout.sections} />
    </AdminLayout>
  )
}

/**
 * Sıra etiketlerini üretir: A, B, C ... Z, AA, AB ...
 *
 * KARISABILECEK HARFLERI ATLIYORUM: I, O, Q.
 * Sebep: gerçek salonlarda gorevli "I sırası mi 1 sırası mi?" diye
 * sorar; kullanıcı da bilette "O" mu "0" mi ayırt edemez. Bilet
 * numarasi uretiminde de aynı mantığı uygulamıştım.
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
