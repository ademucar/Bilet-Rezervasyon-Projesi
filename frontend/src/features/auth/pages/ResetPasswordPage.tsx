import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { Link, useSearchParams, useNavigate } from 'react-router-dom'
import { useMutation } from '@tanstack/react-query'
import { authApi } from '../api/authApi'
import { resetPasswordSchema, type ResetPasswordForm } from '../api/schemas'
import { toProblem } from '../../../lib/api/client'
import { AuthAsideNote, AuthLayout } from '../components/AuthLayout'
import { Button } from '../../../components/ui/Button'
import { Input } from '../../../components/ui/Input'
import { Alert } from '../../../components/ui/Alert'

export function ResetPasswordPage() {
  const [searchParams] = useSearchParams()
  const navigate = useNavigate()

  // Token URL'den geliyor: /şifre-sıfırla?token=xxx
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
      // Sifirlamadan sonra otomatik giris yapmiyorum.
      //
      // Neden? Backend sıfırlama sonrası tüm oturumlari kapatiyor ve
      // yeni token dondurmuyor. Ayrıca kullanıcının yeni sifresini
      // bir kez girerek dogrulamasi, sifreyi akilda tutmasina yardim
      // eder. Güvenlik acisindan da temiz bir başlangıç.
      navigate('/giris?sebep=sifre-sifirlandi', { replace: true })
    },
  })

  // Token yoksa formu HİÇ gostermiyorum.
  //
  // Formu gosterip kullanıcıya şifre yazdirmak, sonra "token yok"
  // demek zaman kaybi ve sinir bozucu olurdu. Engeli en basta
  // bildirmek daha iyi bir deneyim.
  if (!token) {
    return (
      <AuthLayout title="Geçersiz bağlantı">
        <Alert variant="error">
          Bu şifre sıfırlama bağlantısı geçersiz. Lütfen yeni bir talep oluşturun.
        </Alert>

        <div className="mt-4">
          <Link to="/sifremi-unuttum" className="font-medium text-brand-600 hover:underline">
            Yeni bağlantı iste
          </Link>
        </div>
      </AuthLayout>
    )
  }

  return (
    <AuthLayout
      title="Yeni şifre belirle"
      subtitle="Hesabınız için yeni bir şifre oluşturun."
      aside={
        /* Sprint 16: şifre değişince refresh token'lar iptal
           ediliyor. Kullanıcı bunu telefonu elinden bırakmadan önce
           bilmeli. */
        <AuthAsideNote
          icon={
            <svg
              className="size-3.5"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2"
              strokeLinecap="round"
            >
              <rect x="4" y="10" width="16" height="10" rx="1" />
              <path d="M8 10V7a4 4 0 0 1 8 0v3" />
            </svg>
          }
        >
          Şifre değiştiğinde açık olan tüm oturumlar kapanır. Diğer cihazlarda tekrar giriş yapmanız
          gerekir.
        </AuthAsideNote>
      }
      footer={
        <Link to="/giris" className="font-medium text-brand-600 hover:underline">
          Giriş ekranına dön
        </Link>
      }
    >
      {mutation.isError && (
        <div className="mb-4">
          <Alert variant="error">
            {toProblem(mutation.error).detail ?? 'Şifre sıfırlanamadı.'}
          </Alert>
        </div>
      )}

      <form onSubmit={handleSubmit((d) => mutation.mutate(d))} className="space-y-4" noValidate>
        <Input
          label="Yeni şifre"
          type="password"
          autoComplete="new-password"
          error={errors.password?.message}
          {...register('password')}
        />

        <Input
          label="Yeni şifre tekrar"
          type="password"
          autoComplete="new-password"
          error={errors.passwordConfirm?.message}
          {...register('passwordConfirm')}
        />

        <p className="text-xs text-slate-500">
          Şifreniz en az 8 karakter olmalı; büyük harf, küçük harf ve rakam içermelidir.
        </p>

        <Button type="submit" isLoading={mutation.isPending} className="w-full">
          Şifreyi güncelle
        </Button>
      </form>
    </AuthLayout>
  )
}
