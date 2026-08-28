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

  // Interceptor oturumu sonlandirirken sebebi URL'e yazıyor.
  // Kullanıcıya "neden çıkış yaptım?" sorusunun cevabini veriyoruz --
  // sessizce giriş ekranına atmak çok kötü bir deneyimdir.
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

      // Kullanıcı korumali bir sayfaya gitmeye calistiysa ProtectedRoute
      // önü buraya yonlendirirken hedefi state içinde tasidi.
      // Giristen sonra oraya donuyoruz -- basa donmek yerine.
      const from = (location.state as { from?: string } | null)?.from ?? '/'

      // replace: true -> tarayıcı gecmisinde giriş sayfasini BIRAKMA.
      // Yoksa kullanıcı geri tusuna bastiginda giriş ekranına döner
      // ki zaten giriş yapmış durumda. Kafa karistirici olur.
      navigate(from, { replace: true })
    },
    onError: (error) => {
      const problem = toProblem(error)

      // Hata kontrolunu METNE göre değil KODA göre yapıyorum.
      // Backend mesaji degistirdiginde bu kod bozulmasin.
      const message =
        problem.errorCode === 'auth.account_locked'
          ? 'Çok fazla başarısız deneme yapıldı. Lütfen 15 dakika sonra tekrar deneyin.'
          : (problem.detail ?? 'Giriş yapılamadı.')

      setServerError(message)
    },
  })

  return (
    <AuthLayout
      title="Giriş yap"
      subtitle="Hesabınıza erişin"
      footer={
        <>
          Hesabiniz yok mu?{' '}
          <Link to="/kayit" className="font-medium text-brand-600 hover:underline">
            Kayıt olun
          </Link>
        </>
      }
    >
      {sessionReason === 'sure-doldu' && (
        <div className="mb-4">
          <Alert variant="info">Oturum süreniz doldu. Lütfen tekrar giriş yapın.</Alert>
        </div>
      )}

      {sessionReason === 'guvenlik' && (
        <div className="mb-4">
          <Alert variant="error">
            Güvenlik nedeniyle tüm oturumlarınız sonlandırıldı. Lütfen tekrar giriş yapın.
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
        // noValidate: tarayıcının kendi doğrulama balonlarini kapat.
        // Zod ile tutarli, Turkce ve erişilebilir hatalar gosteriyoruz;
        // tarayıcının Ingilizce balonlari bunu bozardi.
        noValidate
      >
        <Input
          label="E-posta"
          type="email"
          // autoComplete: şifre yoneticilerinin alanı tanimasini saglar.
          // Yazmazsak kullanıcılar kayıtlı sifrelerini kullanamaz.
          autoComplete="email"
          placeholder="örnek@eposta.com"
          error={errors.email?.message}
          {...register('email')}
        />

        <Input
          label="Şifre"
          type="password"
          autoComplete="current-password"
          placeholder="••••••••"
          error={errors.password?.message}
          {...register('password')}
        />

        <div className="flex justify-end">
          <Link to="/sifremi-unuttum" className="text-sm text-brand-600 hover:underline">
            Şifremi unuttum
          </Link>
        </div>

        <Button type="submit" isLoading={isSubmitting || mutation.isPending} className="w-full">
          Giriş yap
        </Button>
      </form>
    </AuthLayout>
  )
}
