import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { Link, useNavigate, useLocation, useSearchParams } from 'react-router-dom'
import { useMutation } from '@tanstack/react-query'
import { authApi } from '../api/authApi'
import { loginSchema, type LoginForm } from '../api/schemas'
import { useAuthStore } from '../../../stores/authStore'
import { toProblem } from '../../../lib/api/client'
import { AuthLayout } from '../components/AuthLayout'
import { Button } from '../../../components/ui/Button'
import { Input } from '../../../components/ui/Input'
import { Alert } from '../../../components/ui/Alert'

export function LoginPage() {
  const navigate = useNavigate()
  const location = useLocation()
  const [searchParams] = useSearchParams()
  const setSession = useAuthStore((s) => s.setSession)
  const [serverError, setServerError] = useState<string | null>(null)

  // Interceptor oturumu sonlandirirken sebebi URL'e yaziyor.
  // Kullaniciya "neden cikis yaptim?" sorusunun cevabini veriyoruz --
  // sessizce giris ekranina atmak cok kotu bir deneyimdir.
  const sessionReason = searchParams.get('sebep')

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<LoginForm>({
    resolver: zodResolver(loginSchema),
    defaultValues: { email: '', password: '' },
  })

  const mutation = useMutation({
    mutationFn: authApi.login,
    onSuccess: (auth) => {
      setSession(auth)

      // Kullanici korumali bir sayfaya gitmeye calistiysa ProtectedRoute
      // onu buraya yonlendirirken hedefi state icinde tasidi.
      // Giristen sonra oraya donuyoruz -- basa donmek yerine.
      const from = (location.state as { from?: string } | null)?.from ?? '/'

      // replace: true -> tarayici gecmisinde giris sayfasini BIRAKMA.
      // Yoksa kullanici geri tusuna bastiginda giris ekranina doner
      // ki zaten giris yapmis durumda. Kafa karistirici olur.
      navigate(from, { replace: true })
    },
    onError: (error) => {
      const problem = toProblem(error)

      // Hata kontrolunu METNE gore degil KODA gore yapiyorum.
      // Backend mesaji degistirdiginde bu kod bozulmasin.
      const message =
        problem.errorCode === 'auth.account_locked'
          ? 'Cok fazla basarisiz deneme yapildi. Lutfen 15 dakika sonra tekrar deneyin.'
          : problem.detail ?? 'Giris yapilamadi.'

      setServerError(message)
    },
  })

  return (
    <AuthLayout
      title="Giris yap"
      subtitle="Hesabiniza erisin"
      footer={
        <>
          Hesabiniz yok mu?{' '}
          <Link to="/kayit" className="font-medium text-brand-600 hover:underline">
            Kayit olun
          </Link>
        </>
      }
    >
      {sessionReason === 'sure-doldu' && (
        <div className="mb-4">
          <Alert variant="info">Oturum sureniz doldu. Lutfen tekrar giris yapin.</Alert>
        </div>
      )}

      {sessionReason === 'guvenlik' && (
        <div className="mb-4">
          <Alert variant="error">
            Guvenlik nedeniyle tum oturumlariniz sonlandirildi. Lutfen tekrar giris yapin.
          </Alert>
        </div>
      )}

      {serverError && (
        <div className="mb-4">
          <Alert variant="error">{serverError}</Alert>
        </div>
      )}

      <form
        onSubmit={handleSubmit((data) => {
          setServerError(null)
          mutation.mutate(data)
        })}
        className="space-y-4"
        // noValidate: tarayicinin kendi dogrulama balonlarini kapat.
        // Zod ile tutarli, Turkce ve erisilebilir hatalar gosteriyoruz;
        // tarayicinin Ingilizce balonlari bunu bozardi.
        noValidate
      >
        <Input
          label="E-posta"
          type="email"
          // autoComplete: sifre yoneticilerinin alani tanimasini saglar.
          // Yazmazsak kullanicilar kayitli sifrelerini kullanamaz.
          autoComplete="email"
          placeholder="ornek@eposta.com"
          error={errors.email?.message}
          {...register('email')}
        />

        <Input
          label="Sifre"
          type="password"
          autoComplete="current-password"
          placeholder="••••••••"
          error={errors.password?.message}
          {...register('password')}
        />

        <div className="flex justify-end">
          <Link to="/sifremi-unuttum" className="text-sm text-brand-600 hover:underline">
            Sifremi unuttum
          </Link>
        </div>

        <Button type="submit" isLoading={isSubmitting || mutation.isPending} className="w-full">
          Giris yap
        </Button>
      </form>
    </AuthLayout>
  )
}
