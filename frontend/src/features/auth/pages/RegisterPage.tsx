import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { Link, useNavigate } from 'react-router-dom'
import { useMutation } from '@tanstack/react-query'
import { authApi } from '../api/authApi'
import { registerSchema, type RegisterForm } from '../api/schemas'
import { useAuthStore } from '../../../stores/authStore'
import { toProblem } from '../../../lib/api/client'
import { AuthAsideNote, AuthLayout } from '../components/AuthLayout'
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
    // 'onChange' olsaydı kullanıcı daha ilk harfi yazarken "en az 8
    // karakter" hatası gorurdu -- henüz yazmayi bitirmemisken
    // azarlanmis hissettirir.
    // 'onSubmit' (varsayılan) ise geri bildirimi çok geciktirir.
    // 'onBlur' ikisinin arasindaki doğru denge.
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
      // Kayittan sonra otomatik giriş: backend zaten token dönüyor.
      // Kullaniciyi bir de giriş ekranına yollamak gereksiz surtunme.
      setSession(auth)
      navigate('/', { replace: true })
    },
    onError: (error) => {
      const problem = toProblem(error)

      // E-posta cakismasini ILGILI ALANIN altinda gösteriyorum,
      // sayfanin tepesinde genel bir uyarı olarak değil.
      // Kullanıcı hangi alanı duzeltecegini anında görüyor.
      if (problem.errorCode === 'auth.email_in_use') {
        setError('email', { message: 'Bu e-posta adresi zaten kullanılıyor.' })
        return
      }

      // Backend'den gelen alan bazlı doğrulama hatalarini formla eslestir.
      // Normalde frontend dogrulamasi bunlari zaten yakalar; buraya
      // dusmesi kurallarin ayristigi anlamina gelir -- yine de
      // kullanıcıyı bilgisiz birakmiyoruz.
      if (problem.errors) {
        Object.entries(problem.errors).forEach(([field, messages]) => {
          const key = field.charAt(0).toLowerCase() + field.slice(1)
          setError(key as keyof RegisterForm, { message: messages[0] })
        })
        return
      }

      setServerError(problem.detail ?? 'Kayıt oluşturulamadı.')
    },
  })

  return (
    <AuthLayout
      title="Kayıt ol"
      subtitle="Birkaç adımda hesabınızı oluşturun"
      aside={
        /* Onay adımını ÖNCEDEN söylüyorum. Kullanıcı kayıt olup
           "neden bilet alamıyorum" diye takılmasın -- o noktada gelen
           kutusuna bakması gerektiğini bilmiyor. */
        <AuthAsideNote
          icon={
            <svg
              className="size-3.5"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2"
              strokeLinecap="round"
              strokeLinejoin="round"
            >
              <rect x="3" y="5" width="18" height="14" rx="1" />
              <path d="m3 7 9 6 9-6" />
            </svg>
          }
        >
          Kayıttan sonra e-posta adresinize bir onay bağlantısı gönderilir. Onaylamadan bilet
          alamazsınız.
        </AuthAsideNote>
      }
      footer={
        <>
          Zaten hesabınız var mı?{' '}
          <Link to="/giris" className="font-medium text-brand-600 hover:underline">
            Giriş yapın
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
          // O yalnızca frontend'in yazım hatası kontrolü; sunucunun
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
          placeholder="örnek@eposta.com"
          error={errors.email?.message}
          {...register('email')}
        />

        <Input
          label="Telefon (isteğe bağlı)"
          type="tel"
          autoComplete="tel"
          placeholder="+90 555 000 0000"
          error={errors.phoneNumber?.message}
          {...register('phoneNumber')}
        />

        <Input
          label="Şifre"
          type="password"
          // "new-password": şifre yoneticisine "bu yeni bir şifre,
          // güçlü bir tane onerebilirsin" der. "current-password"
          // yazsaydık kayıtlı sifreyi doldurmaya calisirdi.
          autoComplete="new-password"
          error={errors.password?.message}
          {...register('password')}
        />

        <Input
          label="Şifre tekrar"
          type="password"
          autoComplete="new-password"
          error={errors.passwordConfirm?.message}
          {...register('passwordConfirm')}
        />

        <p className="text-xs text-slate-500">
          Şifreniz en az 8 karakter olmalı; büyük harf, küçük harf ve rakam içermelidir.
        </p>

        <Button type="submit" isLoading={isSubmitting || mutation.isPending} className="w-full">
          Hesap oluştur
        </Button>
      </form>
    </AuthLayout>
  )
}
