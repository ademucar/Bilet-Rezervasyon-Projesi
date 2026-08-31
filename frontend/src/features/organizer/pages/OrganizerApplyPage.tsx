import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useMutation } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { SiteHeader } from '../../../components/layout/SiteHeader'
import { Alert } from '../../../components/ui/Alert'
import { Button } from '../../../components/ui/Button'
import { Input } from '../../../components/ui/Input'
import { toProblem } from '../../../lib/api/client'
import { useAuthStore } from '../../../stores/authStore'
import { applicationApi } from '../api/applicationApi'

const sema = z.object({
  companyName: z.string().min(1, 'Firma adı zorunludur.').max(200),
  contactEmail: z
    .string()
    .min(1, 'İletişim e-postası zorunludur.')
    .email('Geçerli bir e-posta girin.'),
  taxNumber: z.string().max(20).optional(),
  contactPhone: z.string().max(20).optional(),
  description: z.string().max(2000).optional(),
})

type Form = z.input<typeof sema>

/**
 * Organizatör başvurusu.
 *
 * Bu ekranı admin onay ekranıyla birlikte yazdım: onaylanacak bir
 * başvuru olmadan onay ekranının test edilecek hâli yok. Backend'de
 * POST /organizer-applications zaten duruyordu ama çağıran kimse
 * yoktu -- yani sistemde organizatör olmanın arayüzden hiçbir yolu
 * bulunmuyordu.
 */
export function OrganizerApplyPage() {
  const kullanici = useAuthStore((s) => s.user)
  const [gonderildi, setGonderildi] = useState(false)
  const [hata, setHata] = useState<string | null>(null)

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<Form>({
    resolver: zodResolver(sema),
    // Iletisim e-postasini kullanicinin kendi adresiyle
    // dolduruyorum: cogu basvuruda ayni olacak ve yazdirmak
    // gereksiz surtunme.
    defaultValues: { contactEmail: kullanici?.email ?? '' },
  })

  const basvur = useMutation({
    mutationFn: (d: Form) =>
      applicationApi.apply({
        companyName: d.companyName,
        contactEmail: d.contactEmail,
        taxNumber: d.taxNumber?.trim() || null,
        contactPhone: d.contactPhone?.trim() || null,
        description: d.description?.trim() || null,
      }),
    onSuccess: () => setGonderildi(true),
    onError: (e) => setHata(toProblem(e).detail ?? 'Başvuru gönderilemedi.'),
  })

  const alan =
    'w-full rounded-[4px] border border-slate-300 px-3 py-2.5 text-sm outline-none ' +
    'transition-colors focus:border-brand-500'

  if (gonderildi) {
    return (
      <div className="min-h-screen bg-slate-100">
        <SiteHeader />
        <main className="mx-auto max-w-2xl px-4 py-8">
          <div className="rounded-[4px] border border-slate-300 bg-white p-8 text-center">
            <h1 className="font-display text-xl font-semibold text-slate-900">Başvurunuz alındı</h1>
            <p className="mt-2 text-sm text-slate-600">
              Yönetici incelemesinden sonra sonucu e-posta ile bildireceğiz. Onaylanırsa
              &ldquo;Etkinliklerim&rdquo; bölümü hesabınızda açılır.
            </p>
            <Link to="/" className="mt-4 inline-block text-sm text-brand-600 hover:underline">
              Ana sayfaya dön
            </Link>
          </div>
        </main>
      </div>
    )
  }

  return (
    <div className="min-h-screen bg-slate-100">
      <SiteHeader />

      <main className="mx-auto max-w-2xl px-4 py-8">
        <h1 className="font-display text-2xl font-bold tracking-tight text-kagit">
          Organizatör başvurusu
        </h1>
        <p className="mt-1 text-sm text-kagit-soluk">
          Etkinlik oluşturup bilet satmak için organizatör olmanız gerekiyor. Başvurunuz yönetici
          onayından sonra aktifleşir.
        </p>

        {hata && (
          <div className="mt-4">
            <Alert variant="error">{hata}</Alert>
          </div>
        )}

        <form
          onSubmit={handleSubmit((d) => {
            setHata(null)
            basvur.mutate(d)
          })}
          className="mt-6 space-y-5 rounded-[4px] border border-slate-300 bg-white p-6"
          noValidate
        >
          <Input
            label="Firma / kurum adı"
            error={errors.companyName?.message}
            {...register('companyName')}
          />

          <div className="grid gap-5 sm:grid-cols-2">
            <Input
              label="İletişim e-postası"
              type="email"
              error={errors.contactEmail?.message}
              {...register('contactEmail')}
            />
            <Input
              label="Telefon (isteğe bağlı)"
              placeholder="+90 555 000 0000"
              error={errors.contactPhone?.message}
              {...register('contactPhone')}
            />
          </div>

          <div className="sm:w-1/2">
            <Input
              label="Vergi numarası (isteğe bağlı)"
              error={errors.taxNumber?.message}
              {...register('taxNumber')}
            />
          </div>

          <div className="space-y-1.5">
            <label htmlFor="basvuruAciklama" className="label-xs block text-slate-500">
              Ne tür etkinlikler düzenliyorsunuz? (isteğe bağlı)
            </label>
            <textarea
              id="basvuruAciklama"
              rows={4}
              className={alan}
              placeholder="Konser ve festival organizasyonu, 2019'dan beri..."
              {...register('description')}
            />
            <p className="text-xs text-slate-500">
              Bu metin başvurunuzu inceleyen yöneticiye gösterilir.
            </p>
          </div>

          <div className="border-t border-slate-200 pt-4">
            <Button type="submit" isLoading={isSubmitting || basvur.isPending}>
              Başvuruyu gönder
            </Button>
          </div>
        </form>
      </main>
    </div>
  )
}
