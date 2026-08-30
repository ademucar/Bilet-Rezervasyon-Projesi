import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { AdminLayout } from '../components/AdminLayout'
import { Alert } from '../../../components/ui/Alert'
import { Button } from '../../../components/ui/Button'
import { Input } from '../../../components/ui/Input'
import { toProblem } from '../../../lib/api/client'
import { formatDateTime } from '../../../lib/format'
import {
  applicationApi,
  ApplicationStatus,
  APPLICATION_STATUS_LABELS,
} from '../../organizer/api/applicationApi'

const SUZGECLER: { etiket: string; deger?: number }[] = [
  { etiket: 'Bekleyenler', deger: ApplicationStatus.Pending },
  { etiket: 'Onaylananlar', deger: ApplicationStatus.Approved },
  { etiket: 'Reddedilenler', deger: ApplicationStatus.Rejected },
  { etiket: 'Tümü' },
]

/**
 * Organizatör başvuruları -- PDF sayfa 5:
 * "Admin: Organizatör başvurularını onaylayabilir."
 *
 * Varsayılan süzgeç "Bekleyenler" çünkü adminin bu ekrana gelme
 * sebebi neredeyse her zaman bekleyen bir başvuru. Tüm listeyi
 * varsayılan yapsaydım, onaylanmış yüzlerce kaydın arasında iş
 * aramak gerekirdi.
 */
export function OrganizerApplicationsPage() {
  const queryClient = useQueryClient()
  const [durum, setDurum] = useState<number | undefined>(ApplicationStatus.Pending)
  const [hata, setHata] = useState<string | null>(null)
  const [redEdilen, setRedEdilen] = useState<string | null>(null)
  const [redSebebi, setRedSebebi] = useState('')

  const sorgu = useQuery({
    queryKey: ['organizerApplications', durum],
    queryFn: () => applicationApi.list(durum),
  })

  const tazele = () => {
    queryClient.invalidateQueries({ queryKey: ['organizerApplications'] })
  }

  const onayla = useMutation({
    mutationFn: (id: string) => applicationApi.approve(id),
    onSuccess: tazele,
    onError: (e) => setHata(toProblem(e).detail ?? 'Onaylanamadı.'),
  })

  const reddet = useMutation({
    mutationFn: (id: string) => applicationApi.reject(id, redSebebi),
    onSuccess: () => {
      setRedEdilen(null)
      setRedSebebi('')
      tazele()
    },
    onError: (e) => setHata(toProblem(e).detail ?? 'Reddedilemedi.'),
  })

  return (
    <AdminLayout
      title="Organizatör başvuruları"
      subtitle="Başvuruları inceleyin, onaylayın veya reddedin"
    >
      <div className="mb-4 flex flex-wrap gap-2">
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

      {hata && (
        <div className="mb-4">
          <Alert variant="error">{hata}</Alert>
        </div>
      )}

      {sorgu.isError && (
        <Alert variant="error">{toProblem(sorgu.error).detail ?? 'Başvurular yüklenemedi.'}</Alert>
      )}

      {sorgu.isPending && (
        <ul className="space-y-2" aria-busy="true" aria-label="Başvurular yükleniyor">
          {[1, 2].map((i) => (
            <li key={i} className="h-24 animate-pulse rounded-[4px] bg-slate-200" />
          ))}
        </ul>
      )}

      {sorgu.data && sorgu.data.length === 0 && (
        <div className="rounded-[4px] border border-slate-300 bg-white px-5 py-10 text-center">
          <p className="text-sm text-slate-500">
            {durum === ApplicationStatus.Pending
              ? 'Bekleyen başvuru yok.'
              : 'Bu durumda başvuru yok.'}
          </p>
        </div>
      )}

      {sorgu.data && sorgu.data.length > 0 && (
        <ul className="space-y-3">
          {sorgu.data.map((b) => {
            const rozet = APPLICATION_STATUS_LABELS[b.status] ?? {
              text: 'Bilinmiyor',
              tone: 'border-slate-300 bg-slate-50 text-slate-500',
            }
            const bekliyor = b.status === ApplicationStatus.Pending

            return (
              <li key={b.id} className="rounded-[4px] border border-slate-300 bg-white p-4">
                <div className="flex flex-wrap items-start justify-between gap-3">
                  <div className="min-w-0">
                    <p className="font-display font-semibold text-slate-900">{b.companyName}</p>
                    <p className="mt-0.5 text-[13px] text-slate-500">
                      {b.userEmail} &middot; başvuru {formatDateTime(b.createdAt)}
                    </p>
                  </div>
                  <span className={`label-xs shrink-0 border px-1.5 py-[3px] ${rozet.tone}`}>
                    {rozet.text}
                  </span>
                </div>

                {/* Basvuru bilgileri: admin karar verirken bunlara
                    bakiyor. Vergi numarasi ve aciklama istege bagli
                    alanlar; olmayani hic gostermiyorum ki bos satir
                    kalabaligi olusmasin. */}
                <dl className="mt-3 grid gap-2 text-[13px] sm:grid-cols-2">
                  <div>
                    <dt className="label-xs">İletişim e-postası</dt>
                    <dd className="text-slate-700">{b.contactEmail}</dd>
                  </div>
                  {b.taxNumber && (
                    <div>
                      <dt className="label-xs">Vergi numarası</dt>
                      <dd className="num text-slate-700">{b.taxNumber}</dd>
                    </div>
                  )}
                </dl>

                {b.description && (
                  <p className="mt-3 border-t border-slate-100 pt-3 text-[13px] leading-relaxed text-slate-600">
                    {b.description}
                  </p>
                )}

                {b.rejectionReason && (
                  <p className="mt-3 border-t border-slate-100 pt-3 text-[13px] text-red-700">
                    <span className="font-medium">Ret sebebi:</span> {b.rejectionReason}
                  </p>
                )}

                {bekliyor && (
                  <div className="mt-4 flex flex-wrap gap-2 border-t border-slate-200 pt-3">
                    <Button
                      onClick={() => {
                        setHata(null)
                        onayla.mutate(b.id)
                      }}
                      isLoading={onayla.isPending && onayla.variables === b.id}
                    >
                      Onayla
                    </Button>
                    <Button
                      variant="secondary"
                      onClick={() => {
                        setRedEdilen(redEdilen === b.id ? null : b.id)
                        setRedSebebi('')
                      }}
                    >
                      Reddet
                    </Button>
                  </div>
                )}

                {redEdilen === b.id && (
                  <div className="mt-3 border-t border-slate-200 pt-3">
                    <Input
                      label="Ret sebebi"
                      placeholder="Vergi numarası doğrulanamadı..."
                      value={redSebebi}
                      onChange={(e) => setRedSebebi(e.target.value)}
                    />
                    {/* Sebep ZORUNLU. Backend de istiyor ama asil
                        gerekce su: reddedilen kisi neyi duzeltip
                        yeniden basvuracagini bilmeli. Sebepsiz ret,
                        ayni basvurunun tekrar gelmesine yol acar. */}
                    <p className="mt-1 text-xs text-slate-500">
                      Bu metin başvuru sahibine gösterilir.
                    </p>
                    <div className="mt-3 flex gap-2">
                      <Button
                        onClick={() => {
                          setHata(null)
                          reddet.mutate(b.id)
                        }}
                        isLoading={reddet.isPending}
                        disabled={redSebebi.trim().length === 0}
                      >
                        Reddi onayla
                      </Button>
                      <Button variant="secondary" onClick={() => setRedEdilen(null)}>
                        Vazgeç
                      </Button>
                    </div>
                  </div>
                )}
              </li>
            )
          })}
        </ul>
      )}
    </AdminLayout>
  )
}
