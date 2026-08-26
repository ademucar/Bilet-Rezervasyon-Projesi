import { create } from 'zustand'
import { persist } from 'zustand/middleware'
import type { AuthResponse, UserSummary } from '../types/auth'

/**
 * ==================================================================
 * TOKEN NEREDE SAKLANMALI? -- DURUSTCE ANLATIYORUM
 * ==================================================================
 * Uc secenek var ve HICBIRI kusursuz degil:
 *
 * 1) httpOnly cerez  -- EN GUVENLI
 *    JavaScript okuyamaz, yani XSS ile calinamaz.
 *    AMA: backend'in token'i cerez olarak yazmasi gerekir, CSRF
 *    korumasi eklenmesi gerekir ve mobil istemciler icin ayri bir
 *    akis lazim olur.
 *
 * 2) Yalnizca BELLEK (React state)
 *    XSS'e karsi en dayanikli ikinci secenek.
 *    AMA: sayfa yenilenince oturum kapanir. Kullanici F5'e basinca
 *    her seferinde giris yapmak zorunda kalir.
 *
 * 3) localStorage  <-- SECILEN
 *    Sayfa yenilenmesinde oturum korunur, uygulanmasi basittir.
 *    RISK: XSS acigi olan bir sayfada saldirgan token'i okuyabilir.
 *
 * NEDEN 3'U SECTIM?
 * Backend'imiz token'i YANIT GOVDESINDE donuyor, cerez olarak degil
 * (PDF'in ongordugu klasik JWT akisi). Cerez yaklasimina gecmek
 * backend'i degistirmeyi gerektirirdi.
 *
 * RISKI NASIL AZALTIYORUZ?
 *   - Access token yalnizca 15 DAKIKA gecerli -> calinsa bile pencere dar
 *   - Refresh token rotation var -> calinma tespit edilince tum
 *     oturumlar kapaniyor (backend'de dogrulandi)
 *   - React JSX'i varsayilan olarak kacisla yazar (XSS'in en yaygin
 *     kaynagini kapatir)
 *   - dangerouslySetInnerHTML KULLANMIYORUZ
 *   - Sprint 15'te Content-Security-Policy eklenecek
 *
 * Yani riski kabul edip AZALTIYORUZ, gormezden gelmiyoruz.
 * Uretime cikan gercek bir urunde httpOnly cerez tercih edilmeliydi.
 * ==================================================================
 */

interface AuthState {
  accessToken: string | null
  refreshToken: string | null
  user: UserSummary | null

  /** Kayit/giris sonrasi cagrilir. */
  setSession: (auth: AuthResponse) => void

  /** Token yenileme sonrasi cagrilir; kullanici bilgisi de guncellenir. */
  updateTokens: (auth: AuthResponse) => void

  /** Cikis veya oturum sonlanmasi. */
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
          // Kullanici bilgisini de guncelliyorum: backend her yenilemede
          // GUNCEL rolleri donuyor. Admin bu arada rol vermisse
          // kullanici sayfayi yenilemeden fark edebiliyor.
          user: auth.user,
        }),

      clearSession: () =>
        set({ accessToken: null, refreshToken: null, user: null }),

      isAuthenticated: () => Boolean(get().accessToken),

      /**
       * Kullanicinin verilen rollerden EN AZ BIRINE sahip olup olmadigi.
       *
       * NOT: Bu YALNIZCA arayuz icindir -- menuyu gizlemek, butonu
       * pasiflestirmek gibi. GUVENLIK DEGILDIR.
       *
       * Kullanici tarayici konsolundan bu store'u degistirip kendini
       * Admin yapabilir. Ama backend token'daki ROLLERE bakar ve token
       * imzalidir; sahte rol ise yaramaz.
       *
       * Kural: frontend yetkilendirmesi KULLANICI DENEYIMI icindir,
       * gercek kontrol her zaman sunucudadir.
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
       * function" hatasi alirdik.
       */
      partialize: (state) => ({
        accessToken: state.accessToken,
        refreshToken: state.refreshToken,
        user: state.user,
      }),
    },
  ),
)
