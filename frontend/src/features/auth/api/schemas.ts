import { z } from 'zod'

/**
 * Form doğrulama semalari.
 *
 * NEDEN HEM FRONTEND HEM BACKEND'DE DOGRULAMA VAR?
 *
 * Bu bir TEKRAR değil, iki FARKLI amaca hizmet ediyor:
 *
 *   Frontend -> KULLANICI DENEYİMİ. Kullanıcı yazarken anında geri
 *               bildirim alır, sunucuya gidip gelmez.
 *
 *   Backend  -> GÜVENLİK. Frontend'i tamamen atlayip curl ile istek
 *               gonderilebilir. Gerçek kontrol her zaman sunucudadır.
 *
 * Frontend dogrulamasini KALDIRSAK sistem yine güvenli çalışır, sadece
 * kullanıcı deneyimi kotulesir. Backend dogrulamasini kaldirsak
 * sistem SAVUNMASIZ kalır.
 *
 * Kurallari backend'deki FluentValidation ile AYNI tutuyorum. Farklı
 * olsalardi kullanıcı formu doldurur, "tamam" der, sonra sunucudan
 * hata alırdı -- en sinir bozucu deneyimlerden biri.
 *
 */

/** Backend'deki RegisterCommandValidator ile birebir aynı kurallar. */
const passwordSchema = z
  .string()
  .min(8, 'Şifre en az 8 karakter olmalıdır.')
  // 72 sınırı BCrypt'ten geliyor: BCrypt yalnızca ilk 72 byte'i dikkate
  // alır, gerisini sessizce yok sayar. Sinir koymasaydim kullanıcı
  // 100 karakterlik şifre girip aslında 72 ile korunuyor olurdu.
  .max(72, 'Şifre en fazla 72 karakter olabilir.')
  .regex(/[A-Z]/, 'Şifre en az bir büyük harf içermelidir.')
  .regex(/[a-z]/, 'Şifre en az bir küçük harf içermelidir.')
  .regex(/[0-9]/, 'Şifre en az bir rakam içermelidir.')

export const loginSchema = z.object({
  email: z
    .string()
    .min(1, 'E-posta adresi zorunludur.')
    .email('Geçerli bir e-posta adresi giriniz.'),
  // Girise şifre KURALLARI uygulamiyorum -- backend'de de uygulamiyorum.
  // Sebep: eski kullanicilarin sifresi yeni kurallara uymayabilir ve
  // kendi hesaplarina giremez hale gelirler.
  password: z.string().min(1, 'Şifre zorunludur.'),
})

export const registerSchema = z
  .object({
    email: z
      .string()
      .min(1, 'E-posta adresi zorunludur.')
      .email('Geçerli bir e-posta adresi giriniz.'),
    password: passwordSchema,
    passwordConfirm: z.string().min(1, 'Şifre tekrarı zorunludur.'),
    firstName: z.string().min(1, 'Ad zorunludur.').max(100, 'Ad en fazla 100 karakter olabilir.'),
    lastName: z
      .string()
      .min(1, 'Soyad zorunludur.')
      .max(100, 'Soyad en fazla 100 karakter olabilir.'),
    phoneNumber: z.string().max(20).optional().or(z.literal('')),
  })
  // Şifre tekrarı yalnızca FRONTEND kuralı -- backend'e hiç gonderilmiyor.
  // Amaci yazım hatasini yakalamak, güvenlik değil.
  .refine((d) => d.password === d.passwordConfirm, {
    message: 'Şifreler eşleşmiyor.',
    path: ['passwordConfirm'],
  })

export const forgotPasswordSchema = z.object({
  email: z
    .string()
    .min(1, 'E-posta adresi zorunludur.')
    .email('Geçerli bir e-posta adresi giriniz.'),
})

export const resetPasswordSchema = z
  .object({
    password: passwordSchema,
    passwordConfirm: z.string().min(1, 'Şifre tekrarı zorunludur.'),
  })
  .refine((d) => d.password === d.passwordConfirm, {
    message: 'Şifreler eşleşmiyor.',
    path: ['passwordConfirm'],
  })

export type LoginForm = z.infer<typeof loginSchema>
export type RegisterForm = z.infer<typeof registerSchema>
export type ForgotPasswordForm = z.infer<typeof forgotPasswordSchema>
export type ResetPasswordForm = z.infer<typeof resetPasswordSchema>
