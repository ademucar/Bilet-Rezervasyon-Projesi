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
  // BASARILI EKRANI -- GUVENLIK ACISINDAN KRITIK METIN
  // ==================================================================
  // "E-posta gonderildi" DEMIYORUZ.
  // "EGER bu adres kayitliysa gonderildi" diyoruz.
  //
  // Fark neden onemli? Backend, adres kayitli olsun olmasin AYNI
  // cevabi doner (kullanici numaralandirmayi engellemek icin).
  // Frontend "gonderildi" deseydi, backend'deki tum o ozeni bosa
  // cikarmis olurduk -- kullanici mesaja bakip adresin kayitli
  // oldugunu varsayardi.
  //
  // Guvenlik zinciri en zayif halkasi kadar guclu. Arayuz metni de
  // o zincirin bir halkasi.
  // ==================================================================
  if (mutation.isSuccess) {
    return (
      <AuthLayout
        title="E-postanizi kontrol edin"
        footer={
          <Link to="/giris" className="font-medium text-brand-600 hover:underline">
            Giris ekranina don
          </Link>
        }
      >
        <Alert variant="success">
          Girdiginiz adres sistemimizde kayitliysa, sifre sifirlama baglantisi gonderildi. Baglanti{' '}
          <strong>1 saat</strong> gecerlidir.
        </Alert>

        <p className="mt-4 text-sm text-slate-500">
          E-posta gelmediyse spam klasorunu kontrol edin.
        </p>
      </AuthLayout>
    )
  }

  return (
    <AuthLayout
      title="Sifremi unuttum"
      subtitle="E-posta adresinizi girin, size bir sifirlama baglantisi gonderelim."
      footer={
        <Link to="/giris" className="font-medium text-brand-600 hover:underline">
          Giris ekranina don
        </Link>
      }
    >
      <form onSubmit={handleSubmit((d) => mutation.mutate(d))} className="space-y-4" noValidate>
        <Input
          label="E-posta"
          type="email"
          autoComplete="email"
          placeholder="ornek@eposta.com"
          error={errors.email?.message}
          {...register('email')}
        />

        <Button type="submit" isLoading={mutation.isPending} className="w-full">
          Sifirlama baglantisi gonder
        </Button>
      </form>
    </AuthLayout>
  )
}
