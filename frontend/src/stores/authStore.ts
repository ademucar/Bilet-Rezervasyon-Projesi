import { create } from 'zustand'
import { persist } from 'zustand/middleware'
import type { AuthResponse, UserSummary } from '../types/auth'

/**
 * TOKEN NEREDE SAKLANMALI? -- DURUSTCE ANLATIYORUM
 *
 * Uc seçenek var ve HICBIRI kusursuz değil:
 *
 * 1) httpOnly çerez  -- EN GUVENLI
 *    JavaScript okuyamaz, yani XSS ile calinamaz.
 *    AMA: backend'in token'i çerez olarak yazmasi gerekir, CSRF
 *    korumasi eklenmesi gerekir ve mobil istemciler için ayrı bir
 *    akis lazim olur.
 *
 * 2) Yalnızca BELLEK (React state)
 *    XSS'e karsi en dayanikli ikinci seçenek.
 *    AMA: sayfa yenilenince oturum kapanir. Kullanıcı F5'e basinca
 *    her seferinde giriş yapmak zorunda kalır.
 *
 * 3) localStorage  <-- SECILEN
 *    Sayfa yenilenmesinde oturum korunur, uygulanmasi basittir.
 *    RISK: XSS acigi olan bir sayfada saldirgan token'i okuyabilir.
 *
 * NEDEN 3'U SECTIM?
 * Backend'im token'i YANIT GOVDESINDE dönüyor, çerez olarak değil
 * (PDF'in ongordugu klasik JWT akışı). Cerez yaklasimina gecmek
 * backend'i değiştirmeyi gerektirirdi.
 *
 * RISKI NASIL AZALTIYORUM?
 *   - Access token yalnızca 15 DAKIKA geçerli -> calinsa bile pencere dar
 *   - Refresh token rotation var -> calinma tespit edilince tüm
 *     oturumlar kapaniyor (backend'de dogrulandi)
 *   - React JSX'i varsayılan olarak kacisla yazar (XSS'in en yaygin
 *     kaynagini kapatır)
 *   - dangerouslySetInnerHTML KULLANMIYORUM
 *   - Content-Security-Policy Sprint 15'te eklendi
 *     (SecurityHeadersMiddleware)
 *
 * Yani riski kabul edip AZALTIYORUM, gormezden gelmiyorum.
 * Uretime cikan gerçek bir urunde httpOnly çerez tercih edilmeliydi.
 *
 */

interface AuthState {
  accessToken: string | null
  refreshToken: string | null
  user: UserSummary | null

  /** Kayıt/giriş sonrası cagrilir. */
  setSession: (auth: AuthResponse) => void

  /** Token yenileme sonrası cagrilir; kullanıcı bilgisi de guncellenir. */
  updateTokens: (auth: AuthResponse) => void

  /** Çıkış veya oturum sonlanmasi. */
  clearSession: () => void

  isAuthenticated: () => boolean
  hasRole: (...roles: string[]) => boolean
}

export const useAuthStore = create<AuthState>()(
  persist(
    (set, get) => ({
      accessToken: null,
      refreshToken: null,
      user: null,

      setSession: (auth) =>
        set({
          accessToken: auth.accessToken,
          refreshToken: auth.refreshToken,
          user: auth.user,
        }),

      updateTokens: (auth) =>
        set({
          accessToken: auth.accessToken,
          refreshToken: auth.refreshToken,
          // Kullanıcı bilgisini de guncelliyorum: backend her yenilemede
          // GUNCEL rolleri dönüyor. Admin bu arada rol vermisse
          // kullanıcı sayfayı yenilemeden fark edebiliyor.
          user: auth.user,
        }),

      clearSession: () => set({ accessToken: null, refreshToken: null, user: null }),

      isAuthenticated: () => Boolean(get().accessToken),

      /**
       * Kullanıcının verilen rollerden EN AZ BIRINE sahip olup olmadığı.
       *
       * NOT: Bu YALNIZCA arayüz icindir -- menuyu gizlemek, butonu
       * pasiflestirmek gibi. GÜVENLİK DEĞİLDİR.
       *
       * Kullanıcı tarayıcı konsolundan bu store'u değiştirip kendini
       * Admin yapabilir. Ama backend token'daki ROLLERE bakar ve token
       * imzalidir; sahte rol ise yaramaz.
       *
       * Kural: frontend yetkilendirmesi KULLANICI DENEYİMİ icindir,
       * gerçek kontrol her zaman sunucudadır.
       */
      hasRole: (...roles) => {
        const userRoles = get().user?.roles ?? []
        return roles.some((r) => userRoles.includes(r))
      },
    }),
    {
      name: 'ticketing-auth',

      /**
       * Hangi alanlar localStorage'a yazilacak?
       *
       * Metotlari (setSession, hasRole...) DISARIDA birakiyorum.
       * Fonksiyonlar JSON'a serilestirilemez; yazmaya calisirsak
       * sessizce kaybolur ve sayfa yenilendiginde "hasRole is not a
       * function" hatası alırdım.
       */
      partialize: (state) => ({
        accessToken: state.accessToken,
        refreshToken: state.refreshToken,
        user: state.user,
      }),
    },
  ),
)
