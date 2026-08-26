import { z } from 'zod'

/**
 * Form dogrulama semalari.
 *
 * ==================================================================
 * NEDEN HEM FRONTEND HEM BACKEND'DE DOGRULAMA VAR?
 * ==================================================================
 * Bu bir TEKRAR degil, iki FARKLI amaca hizmet ediyor:
 *
 *   Frontend -> KULLANICI DENEYIMI. Kullanici yazarken aninda geri
 *               bildirim alir, sunucuya gidip gelmez.
 *
 *   Backend  -> GUVENLIK. Frontend'i tamamen atlayip curl ile istek
 *               gonderilebilir. Gercek kontrol her zaman sunucudadir.
 *
 * Frontend dogrulamasini KALDIRSAK sistem yine guvenli calisir, sadece
 * kullanici deneyimi kotulesir. Backend dogrulamasini kaldirsak
 * sistem SAVUNMASIZ kalir.
 *
 * Kurallari backend'deki FluentValidation ile AYNI tutuyorum. Farkli
 * olsalardi kullanici formu doldurur, "tamam" der, sonra sunucudan
 * hata alirdi -- en sinir bozucu deneyimlerden biri.
 * ==================================================================
 */

/** Backend'deki RegisterCommandValidator ile birebir ayni kurallar. */
const passwordSchema = z
  .string()
  .min(8, 'Sifre en az 8 karakter olmalidir.')
  // 72 siniri BCrypt'ten geliyor: BCrypt yalnizca ilk 72 byte'i dikkate
  // alir, gerisini sessizce yok sayar. Sinir koymasaydik kullanici
  // 100 karakterlik sifre girip aslinda 72 ile korunuyor olurdu.
  .max(72, 'Sifre en fazla 72 karakter olabilir.')
  .regex(/[A-Z]/, 'Sifre en az bir buyuk harf icermelidir.')
  .regex(/[a-z]/, 'Sifre en az bir kucuk harf icermelidir.')
  .regex(/[0-9]/, 'Sifre en az bir rakam icermelidir.')

export const loginSchema = z.object({
  email: z.string().min(1, 'E-posta adresi zorunludur.').email('Gecerli bir e-posta adresi giriniz.'),
  // Girise sifre KURALLARI uygulamiyorum -- backend'de de uygulamiyoruz.
  // Sebep: eski kullanicilarin sifresi yeni kurallara uymayabilir ve
  // kendi hesaplarina giremez hale gelirler.
  password: z.string().min(1, 'Sifre zorunludur.'),
})

export const registerSchema = z
  .object({
    email: z.string().min(1, 'E-posta adresi zorunludur.').email('Gecerli bir e-posta adresi giriniz.'),
    password: passwordSchema,
    passwordConfirm: z.string().min(1, 'Sifre tekrari zorunludur.'),
    firstName: z.string().min(1, 'Ad zorunludur.').max(100, 'Ad en fazla 100 karakter olabilir.'),
    lastName: z.string().min(1, 'Soyad zorunludur.').max(100, 'Soyad en fazla 100 karakter olabilir.'),
    phoneNumber: z.string().max(20).optional().or(z.literal('')),
  })
  // Sifre tekrari yalnizca FRONTEND kurali -- backend'e hic gonderilmiyor.
  // Amaci yazim hatasini yakalamak, guvenlik degil.
  .refine((d) => d.password === d.passwordConfirm, {
    message: 'Sifreler eslesmiyor.',
    path: ['passwordConfirm'],
  })

export const forgotPasswordSchema = z.object({
  email: z.string().min(1, 'E-posta adresi zorunludur.').email('Gecerli bir e-posta adresi giriniz.'),
})

export const resetPasswordSchema = z
  .object({
    password: passwordSchema,
    passwordConfirm: z.string().min(1, 'Sifre tekrari zorunludur.'),
  })
  .refine((d) => d.password === d.passwordConfirm, {
    message: 'Sifreler eslesmiyor.',
    path: ['passwordConfirm'],
  })

export type LoginForm = z.infer<typeof loginSchema>
export type RegisterForm = z.infer<typeof registerSchema>
export type ForgotPasswordForm = z.infer<typeof forgotPasswordSchema>
export type ResetPasswordForm = z.infer<typeof resetPasswordSchema>
