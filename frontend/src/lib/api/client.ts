import axios, { AxiosError, type AxiosInstance, type InternalAxiosRequestConfig } from 'axios'
import { useAuthStore } from '../../stores/authStore'
import type { AuthResponse, ProblemDetails } from '../../types/auth'

/**
 * Ortak API istemcisi.
 *
 * PDF Sprint 18: "API istekleri component icinde dagink sekilde
 * yazilmamalidir. Ortak API client olusturulmalidir."
 */
export const api: AxiosInstance = axios.create({
  // Vite proxy sayesinde gorece yol yeterli. Ortam bazli adres yok.
  baseURL: '/api/v1',
  headers: { 'Content-Type': 'application/json' },
  timeout: 15000,
})

/** Her istek icin ayri bir izleme kimligi. Backend loglarinda eslesiyor. */
function newCorrelationId(): string {
  return crypto.randomUUID().replace(/-/g, '')
}

// ===================================================================
// ISTEK INTERCEPTOR'I -- token ekle
// ===================================================================
api.interceptors.request.use((config) => {
  const token = useAuthStore.getState().accessToken

  if (token) {
    config.headers.Authorization = `Bearer ${token}`
  }

  // PDF Sprint 16: Correlation ID. Frontend uretip gonderiyor;
  // backend ayni degeri kullanip loglara isliyor. Boylece bir
  // kullanici sikayetini uctan uca izleyebiliyoruz.
  config.headers['X-Correlation-Id'] = newCorrelationId()

  return config
})

// ===================================================================
// YANIT INTERCEPTOR'I -- 401 alinca sessizce token yenile
// ===================================================================
//
// ==================================================================
// EN ONEMLI PROBLEM: ES ZAMANLI ISTEKLER
// ==================================================================
// Sayfa acilirken 4 istek ayni anda gidiyor ve access token'in suresi
// yeni dolmus. Dordu de 401 aliyor.
//
// Naif bir cozum her 401'de yenileme yapardi -> DORT yenileme istegi.
//
// Bu bizim backend'imizde FELAKET olurdu, cunku refresh token
// ROTATION uyguluyoruz:
//   1. istek token'i yeniler -> eski token IPTAL olur
//   2. istek AYNI eski token'i gonderir -> "iptal edilmis token
//      tekrar kullanildi!" -> backend CALINMA SALDIRISI sanip
//      kullanicinin TUM oturumlarini kapatir
//
// Yani kullanici hicbir sey yapmadan sistemden atilirdi. Ve bu hata
// yalnizca "birden fazla istek ayni anda giderse" olusacagi icin
// tespit edilmesi cok zor olurdu.
//
// COZUM: Ayni anda YALNIZCA BIR yenileme calisir. Digerleri o
// yenilemenin Promise'ini bekler ve sonucunu paylasir.
// ==================================================================

let refreshPromise: Promise<AuthResponse> | null = null

async function refreshAccessToken(): Promise<AuthResponse> {
  const refreshToken = useAuthStore.getState().refreshToken

  if (!refreshToken) {
    throw new Error('Refresh token yok')
  }

  // DIKKAT: Burada `api` DEGIL, ham axios kullaniyorum.
  //
  // `api` ile cagirsaydim ve bu istek de 401 alsaydi, interceptor
  // tekrar devreye girip yine yenileme denerdi -> SONSUZ DONGU.
  // Ham axios interceptor'lardan gecmez.
  const { data } = await axios.post<AuthResponse>(
    '/api/v1/auth/refresh-token',
    { refreshToken },
    { headers: { 'Content-Type': 'application/json' } },
  )

  return data
}

/** Oturum sonlandi; kullaniciyi giris ekranina yolla. */
function endSession(reason: 'expired' | 'revoked') {
  useAuthStore.getState().clearSession()

  // window.location kullaniyorum, react-router'in navigate'ini degil.
  //
  // Sebep: bu kod bir React bileseninin DISINDA calisiyor; hook
  // cagiramam. Ayrica tam sayfa yenilemesi, bellekte kalmis olabilecek
  // eski durumu (onbellege alinmis sorgular, form verileri) da
  // temizliyor -- oturum sonlandiginda istedigimiz tam olarak bu.
  const target = reason === 'revoked' ? '/giris?sebep=guvenlik' : '/giris?sebep=sure-doldu'

  if (window.location.pathname !== '/giris') {
    window.location.href = target
  }
}

api.interceptors.response.use(
  (response) => response,

  async (error: AxiosError<ProblemDetails>) => {
    const original = error.config as InternalAxiosRequestConfig & { _retried?: boolean }

    // 401 degilse veya zaten bir kez denendiyse: hatayi oldugu gibi ilet.
    //
    // `_retried` bayragi SART: olmasaydi, yenileme sonrasi tekrarlanan
    // istek de 401 alirsa (ornegin kullanici gercekten yetkisiz)
    // sonsuz dongu olusurdu.
    if (error.response?.status !== 401 || original?._retried) {
      return Promise.reject(error)
    }

    // Giris/kayit endpointlerinde 401 NORMALDIR ("sifre yanlis").
    // Bunlari yenilemeye calismak anlamsiz olur.
    const url = original?.url ?? ''
    if (
      url.includes('/auth/login') ||
      url.includes('/auth/register') ||
      url.includes('/auth/refresh-token')
    ) {
      return Promise.reject(error)
    }

    // ---- Kilitleme: yalnizca ilk istek yenilemeyi baslatir ----
    if (!refreshPromise) {
      refreshPromise = refreshAccessToken().finally(() => {
        // Basarili da olsa basarisiz da olsa kilidi birak.
        // finally kullanmasaydim, basarisiz bir yenilemeden sonra
        // refreshPromise dolu kalir ve bir daha HIC yenileme
        // yapilamazdi.
        refreshPromise = null
      })
    }

    try {
      // Es zamanli tum istekler AYNI Promise'i bekliyor.
      const auth = await refreshPromise

      useAuthStore.getState().updateTokens(auth)

      original._retried = true
      original.headers.Authorization = `Bearer ${auth.accessToken}`

      // Basarisiz olan istegi yeni token'la tekrar dene.
      // Kullanici hicbir sey fark etmez.
      return api(original)
    } catch {
      // Yenileme basarisiz: refresh token da gecersiz.
      //
      // Backend "refresh_token_reused" dondurduyse bu bir GUVENLIK
      // olayidir -- kullaniciya farkli bir mesaj gosteriyoruz.
      const code = error.response?.data?.errorCode
      endSession(code === 'auth.refresh_token_reused' ? 'revoked' : 'expired')

      return Promise.reject(error)
    }
  },
)

/**
 * Axios hatasindan Problem Details cikarir.
 *
 * Bunu tek yerde yapmamin sebebi: her bilesende
 * `error.response?.data?.detail ?? 'Bir hata olustu'` yazmak
 * hem tekrar hem de hataya acik. Ag hatasinda (sunucu kapali)
 * `response` hic olmaz ve o zincir undefined doner.
 */
export function toProblem(error: unknown): ProblemDetails {
  if (axios.isAxiosError<ProblemDetails>(error)) {
    if (error.response?.data) {
      return error.response.data
    }

    // Sunucuya hic ulasilamadi (ag hatasi, sunucu kapali, timeout).
    // Kullaniciya "500 hatasi" demek yanlis olur; sunucu cevap bile vermedi.
    return {
      status: 0,
      title: 'Baglanti hatasi',
      detail: 'Sunucuya ulasilamiyor. Internet baglantinizi kontrol edin.',
      errorCode: 'network.unreachable',
    }
  }

  return {
    status: 500,
    title: 'Beklenmeyen hata',
    detail: 'Beklenmeyen bir hata olustu.',
    errorCode: 'client.unexpected',
  }
}

// ===================================================================
// GELISTIRME YARDIMCISI
// ===================================================================
// API istemcisini YALNIZCA gelistirme modunda window'a bagliyorum.
//
// Ne ise yariyor? Tarayici konsolundan
//     await window.__api.get('/auth/me')
// yazip interceptor'in davranisini (token yenileme, hata isleme)
// dogrudan deneyebiliyoruz. Ozellikle "es zamanli 401" senaryosunu
// test etmenin en pratik yolu bu.
//
// import.meta.env.DEV, Vite tarafindan uretim derlemesinde false'a
// sabitlenir ve bu blok paketten TAMAMEN silinir (tree shaking).
// Yani uretimde window.__api diye bir sey OLMAZ -- boyle bir kapiyi
// acik birakmak istemeyiz.
// ===================================================================
if (import.meta.env.DEV) {
  ;(window as unknown as { __api: AxiosInstance }).__api = api
}
