import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { Link, useSearchParams, useNavigate } from 'react-router-dom'
import { useMutation } from '@tanstack/react-query'
import { authApi } from '../api/authApi'
import { resetPasswordSchema, type ResetPasswordForm } from '../api/schemas'
import { toProblem } from '../../../lib/api/client'
import { AuthLayout } from '../components/AuthLayout'
import { Button } from '../../../components/ui/Button'
import { Input } from '../../../components/ui/Input'
import { Alert } from '../../../components/ui/Alert'

export function ResetPasswordPage() {
  const [searchParams] = useSearchParams()
  const navigate = useNavigate()

  // Token URL'den geliyor: /sifre-sifirla?token=xxx
  // Backend'in e-postaya koydugu link bu bicimde.
  const token = searchParams.get('token')

  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<ResetPasswordForm>({
    resolver: zodResolver(resetPasswordSchema),
    mode: 'onBlur',
    defaultValues: { password: '', passwordConfirm: '' },
  })

  const mutation = useMutation({
    mutationFn: (data: ResetPasswordForm) => authApi.resetPassword(token!, data.password),
    onSuccess: () => {
      // Sifirlamadan sonra OTOMATIK GIRIS YAPMIYORUZ.
      //
      // Neden? Backend sifirlama sonrasi tum oturumlari kapatiyor ve
      // yeni token dondurmuyor. Ayrica kullanicinin yeni sifresini
      // bir kez girerek dogrulamasi, sifreyi akilda tutmasina yardim
      // eder. Guvenlik acisindan da temiz bir baslangic.
      navigate('/giris?sebep=sifre-sifirlandi', { replace: true })
    },
  })

  // Token yoksa formu HIC gostermiyorum.
  //
  // Formu gosterip kullaniciya sifre yazdirmak, sonra "token yok"
  // demek zaman kaybi ve sinir bozucu olurdu. Engeli en basta
  // bildirmek daha iyi bir deneyim.
  if (!token) {
    return (
      <AuthLayout title="Gecersiz baglanti">
        <Alert variant="error">
          Bu sifre sifirlama baglantisi gecersiz. Lutfen yeni bir talep olusturun.
        </Alert>

        <div className="mt-4">
          <Link to="/sifremi-unuttum" className="font-medium text-brand-600 hover:underline">
            Yeni baglanti iste
          </Link>
        </div>
      </AuthLayout>
    )
  }

  return (
    <AuthLayout
      title="Yeni sifre belirle"
      subtitle="Hesabiniz icin yeni bir sifre olusturun."
      footer={
        <Link to="/giris" className="font-medium text-brand-600 hover:underline">
          Giris ekranina don
        </Link>
      }
    >
      {mutation.isError && (
        <div className="mb-4">
          <Alert variant="error">
            {toProblem(mutation.error).detail ?? 'Sifre sifirlanamadi.'}
          </Alert>
        </div>
      )}

      <form onSubmit={handleSubmit((d) => mutation.mutate(d))} className="space-y-4" noValidate>
        <Input
          label="Yeni sifre"
          type="password"
          autoComplete="new-password"
          error={errors.password?.message}
          {...register('password')}
        />

        <Input
          label="Yeni sifre tekrar"
          type="password"
          autoComplete="new-password"
          error={errors.passwordConfirm?.message}
          {...register('passwordConfirm')}
        />

        <p className="text-xs text-slate-500">
          Sifreniz en az 8 karakter olmali; buyuk harf, kucuk harf ve rakam icermelidir.
        </p>

        <Button type="submit" isLoading={mutation.isPending} className="w-full">
          Sifreyi guncelle
        </Button>
      </form>
    </AuthLayout>
  )
}
