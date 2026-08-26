/**
 * Backend'in dondugu tiplerin TypeScript karsiliklari.
 *
 * PDF Sprint 18: "Swagger dokumanindan TypeScript client uretimi
 * arastirilmalidir (NSwag, Orval)."
 *
 * Su an elle yaziyorum cunku yalnizca 3 tip var ve otomatik uretim
 * kurulumu (kod ureteci, npm script, CI adimi) simdi gereksiz
 * karmasiklik olurdu. Endpoint sayisi 30'u gectiginde -- ki Sprint 5'ten
 * sonra gececek -- Orval kurup bu dosyayi otomatik uretilene
 * degistirecegiz.
 *
 * Bu bilincli bir "simdilik" karari; unutulmamasi icin buraya yaziyorum.
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
 * Frontend hata kontrolunu `detail` metnine gore DEGIL `errorCode`e
 * gore yapmali -- metin degisince kod bozulmasin.
 */
export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  detail?: string
  instance?: string
  errorCode?: string
  correlationId?: string
  /** Alan bazinda dogrulama hatalari: { "Email": ["..."], "Password": ["..."] } */
  errors?: Record<string, string[]>
}

/** Sistemdeki roller. Backend'deki Role.Names ile birebir ayni olmali. */
export const Roles = {
  User: 'User',
  Organizer: 'Organizer',
  Admin: 'Admin',
} as const

export type Role = (typeof Roles)[keyof typeof Roles]
