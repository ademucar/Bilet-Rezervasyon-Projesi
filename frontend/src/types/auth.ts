/**
 * Backend'in dondugu tiplerin TypeScript karsiliklari.
 *
 * PDF Sprint 18: "Swagger dokumanindan TypeScript client üretimi
 * arastirilmalidir (NSwag, Orval)."
 *
 * Su an elle yazıyorum çünkü yalnızca 3 tip var ve otomatik üretim
 * kurulumu (kod ureteci, npm script, CI adimi) simdi gereksiz
 * karmasiklik olurdu. Endpoint sayısı 30'u geçtiğinde -- ki Sprint 5'ten
 * sonra gececek -- Orval kurup bu dosyayı otomatik uretilene
 * değiştirecegiz.
 *
 * Bu bilinçli bir "şimdilik" karari; unutulmamasi için buraya yazıyorum.
 */

export interface UserSummary {
  id: string
  email: string
  firstName: string
  lastName: string
  isEmailConfirmed: boolean
  roles: string[]
}

export interface AuthResponse {
  accessToken: string
  accessTokenExpiresAt: string
  refreshToken: string
  refreshTokenExpiresAt: string
  user: UserSummary
}

/**
 * Backend'in RFC 7807 Problem Details yaniti.
 *
 * `errorCode` ve `errors` alanlarini BIZ ekledik (RFC uzantisi).
 * Frontend hata kontrolunu `detail` metnine göre DEĞİL `errorCode`e
 * göre yapmali -- metin değişince kod bozulmasin.
 */
export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  detail?: string
  instance?: string
  errorCode?: string
  correlationId?: string
  /** Alan bazinda doğrulama hatalari: { "Email": ["..."], "Password": ["..."] } */
  errors?: Record<string, string[]>
}

/** Sistemdeki roller. Backend'deki Role.Names ile birebir aynı olmalı. */
export const Roles = {
  User: 'User',
  Organizer: 'Organizer',
  Admin: 'Admin',
} as const

export type Role = (typeof Roles)[keyof typeof Roles]
