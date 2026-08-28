import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { Link, useNavigate } from 'react-router-dom'
import { useMutation } from '@tanstack/react-query'
import { authApi } from '../api/authApi'
import { registerSchema, type RegisterForm } from '../api/schemas'
import { useAuthStore } from '../../../stores/authStore'
import { toProblem } from '../../../lib/api/client'
import { AuthLayout } from '../components/AuthLayout'
import { Button } from '../../../components/ui/Button'
import { Input } from '../../../components/ui/Input'
import { Alert } from '../../../components/ui/Alert'

export function RegisterPage() {
  const navigate = useNavigate()
  const setSession = useAuthStore((s) => s.setSession)
  const [serverError, setServerError] = useState<string | null>(null)

  const {
    register,
    handleSubmit,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<RegisterForm>({
    resolver: zodResolver(registerSchema),
    // mode: 'onBlur' -> alandan cikinca dogrula.
    //
    // 'onChange' olsaydi kullanici daha ilk harfi yazarken "en az 8
    // karakter" hatasi gorurdu -- henuz yazmayi bitirmemisken
    // azarlanmis hissettirir.
    // 'onSubmit' (varsayilan) ise geri bildirimi cok geciktirir.
    // 'onBlur' ikisinin arasindaki dogru denge.
    mode: 'onBlur',
    defaultValues: {
      email: '',
      password: '',
      passwordConfirm: '',
      firstName: '',
      lastName: '',
      phoneNumber: '',
    },
  })

  const mutation = useMutation({
    mutationFn: authApi.register,
    onSuccess: (auth) => {
      // Kayittan sonra otomatik giris: backend zaten token donuyor.
      // Kullaniciyi bir de giris ekranina yollamak gereksiz surtunme.
      setSession(auth)
      navigate('/', { replace: true })
    },
    onError: (error) => {
      const problem = toProblem(error)

      // E-posta cakismasini ILGILI ALANIN altinda gosteriyorum,
      // sayfanin tepesinde genel bir uyari olarak degil.
      // Kullanici hangi alani duzeltecegini aninda goruyor.
      if (problem.errorCode === 'auth.email_in_use') {
        setError('email', { message: 'Bu e-posta adresi zaten kullaniliyor.' })
        return
      }

      // Backend'den gelen alan bazli dogrulama hatalarini formla eslestir.
      // Normalde frontend dogrulamasi bunlari zaten yakalar; buraya
      // dusmesi kurallarin ayristigi anlamina gelir -- yine de
      // kullaniciyi bilgisiz birakmiyoruz.
      if (problem.errors) {
        Object.entries(problem.errors).forEach(([field, messages]) => {
          const key = field.charAt(0).toLowerCase() + field.slice(1)
          setError(key as keyof RegisterForm, { message: messages[0] })
        })
        return
      }

      setServerError(problem.detail ?? 'Kayit olusturulamadi.')
    },
  })

  return (
    <AuthLayout
      title="Kayit ol"
      subtitle="Birkac adimda hesabinizi olusturun"
      footer={
        <>
          Zaten hesabiniz var mi?{' '}
          <Link to="/giris" className="font-medium text-brand-600 hover:underline">
            Giris yapin
          </Link>
        </>
      }
    >
      {serverError && (
        <div className="mb-4">
          <Alert variant="error">{serverError}</Alert>
        </div>
      )}

      <form
        onSubmit={handleSubmit((data) => {
          setServerError(null)

          // passwordConfirm backend'e GONDERILMIYOR.
          // O yalnizca frontend'in yazim hatasi kontrolu; sunucunun
          // bilmesine gerek yok ve gondermek gereksiz veri olurdu.
          mutation.mutate({
            email: data.email,
            password: data.password,
            firstName: data.firstName,
            lastName: data.lastName,
            phoneNumber: data.phoneNumber || undefined,
          })
        })}
        className="space-y-4"
        noValidate
      >
        {/* Mobilde alt alta, masaustunde yan yana. */}
        <div className="grid gap-4 sm:grid-cols-2">
          <Input
            label="Ad"
            autoComplete="given-name"
            error={errors.firstName?.message}
            {...register('firstName')}
          />
          <Input
            label="Soyad"
            autoComplete="family-name"
            error={errors.lastName?.message}
            {...register('lastName')}
          />
        </div>

        <Input
          label="E-posta"
          type="email"
          autoComplete="email"
          placeholder="ornek@eposta.com"
          error={errors.email?.message}
          {...register('email')}
        />

        <Input
          label="Telefon (istege bagli)"
          type="tel"
          autoComplete="tel"
          placeholder="+90 555 000 0000"
          error={errors.phoneNumber?.message}
          {...register('phoneNumber')}
        />

        <Input
          label="Sifre"
          type="password"
          // "new-password": sifre yoneticisine "bu yeni bir sifre,
          // guclu bir tane onerebilirsin" der. "current-password"
          // yazsaydik kayitli sifreyi doldurmaya calisirdi.
          autoComplete="new-password"
          error={errors.password?.message}
          {...register('password')}
        />

        <Input
          label="Sifre tekrar"
          type="password"
          autoComplete="new-password"
          error={errors.passwordConfirm?.message}
          {...register('passwordConfirm')}
        />

        <p className="text-xs text-slate-500">
          Sifreniz en az 8 karakter olmali; buyuk harf, kucuk harf ve rakam icermelidir.
        </p>

        <Button type="submit" isLoading={isSubmitting || mutation.isPending} className="w-full">
          Hesap olustur
        </Button>
      </form>
    </AuthLayout>
  )
}
