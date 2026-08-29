import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { Link } from 'react-router-dom'
import { useMutation } from '@tanstack/react-query'
import { authApi } from '../api/authApi'
import { forgotPasswordSchema, type ForgotPasswordForm } from '../api/schemas'
import { AuthLayout } from '../components/AuthLayout'
import { Button } from '../../../components/ui/Button'
import { Input } from '../../../components/ui/Input'
import { Alert } from '../../../components/ui/Alert'

export function ForgotPasswordPage() {
  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<ForgotPasswordForm>({
    resolver: zodResolver(forgotPasswordSchema),
    defaultValues: { email: '' },
  })

  const mutation = useMutation({
    mutationFn: (data: ForgotPasswordForm) => authApi.forgotPassword(data.email),
  })

  // ==================================================================
  // BASARILI EKRANI -- GÜVENLİK ACISINDAN KRITIK METIN
  // ==================================================================
  // "E-posta gönderildi" DEMIYORUZ.
  // "EGER bu adres kayıtlıysa gönderildi" diyoruz.
  //
  // Fark neden önemli? Backend, adres kayıtlı olsun olmasın AYNI
  // cevabi döner (kullanıcı numaralandirmayi engellemek için).
  // Frontend "gönderildi" deseydi, backend'deki tüm o ozeni bosa
  // cikarmis olurduk -- kullanıcı mesaja bakip adresin kayıtlı
  // olduğunu varsayardi.
  //
  // Güvenlik zinciri en zayif halkasi kadar güçlü. Arayuz metni de
  // o zincirin bir halkasi.
  // ==================================================================
  if (mutation.isSuccess) {
    return (
      <AuthLayout
        title="E-postanızı kontrol edin"
        footer={
          <Link to="/giris" className="font-medium text-brand-600 hover:underline">
            Giriş ekranına dön
          </Link>
        }
      >
        <Alert variant="success">
          Girdiğiniz adres sistemimizde kayıtlıysa, şifre sıfırlama bağlantısı gönderildi. Bağlantı{' '}
          <strong>1 saat</strong> geçerlidir.
        </Alert>

        <p className="mt-4 text-sm text-slate-500">
          E-posta gelmediyse spam klasörünü kontrol edin.
        </p>
      </AuthLayout>
    )
  }

  return (
    <AuthLayout
      title="Şifremi unuttum"
      subtitle="E-posta adresinizi girin, size bir sıfırlama bağlantısı gönderelim."
      footer={
        <Link to="/giris" className="font-medium text-brand-600 hover:underline">
          Giriş ekranına dön
        </Link>
      }
    >
      <form onSubmit={handleSubmit((d) => mutation.mutate(d))} className="space-y-4" noValidate>
        <Input
          label="E-posta"
          type="email"
          autoComplete="email"
          placeholder="örnek@eposta.com"
          error={errors.email?.message}
          {...register('email')}
        />

        <Button type="submit" isLoading={mutation.isPending} className="w-full">
          Sıfırlama bağlantısı gönder
        </Button>
      </form>
    </AuthLayout>
  )
}
