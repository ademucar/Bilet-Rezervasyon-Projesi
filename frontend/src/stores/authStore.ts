import { create } from 'zustand'
import { persist } from 'zustand/middleware'
import type { AuthResponse, UserSummary } from '../types/auth'

// Token'i nereye koyacagima karar vermek bu dosyanin en uzun
// suren kismiydi. Uc secenek var ve hicbiri temiz degil.
//
// httpOnly cerez en guvenlisi: JavaScript okuyamiyor, yani XSS ile
// calinamiyor. Ama backend'in token'i cerez olarak yazmasi, ustune
// CSRF korumasi gelmesi ve mobil istemciler icin ayri bir akis
// kurulmasi gerekiyor.
//
// Sadece bellekte tutmak (React state) XSS'e karsi ikinci en
// dayanikli yol. Ama sayfa yenilenince oturum kapaniyor; kullanici
// her F5'te yeniden giris yapiyor.
//
// localStorage'i sectim. Sebebi guvenlik degil, backend: token'i
// yanit govdesinde donuyor, cerez olarak degil -- PDF'in tarif
// ettigi klasik JWT akisi bu. Cereze gecmek backend'i degistirmek
// demekti.
//
// Riski gormezden gelmiyorum, daraltiyorum: access token 15 dakika
// gecerli, refresh token rotation var (calinma tespit edilince tum
// oturumlar kapaniyor), JSX zaten kacisla yaziyor,
// dangerouslySetInnerHTML hic kullanmadim ve Sprint 15'te CSP
// eklendi.
//
// Yine de durustce: uretime cikan gercek bir urunde httpOnly cerez
// dogru secim olurdu.

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
