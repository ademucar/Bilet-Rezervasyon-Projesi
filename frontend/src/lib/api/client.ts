import axios, { AxiosError, type AxiosInstance, type InternalAxiosRequestConfig } from 'axios'
import { useAuthStore } from '../../stores/authStore'
import type { AuthResponse, ProblemDetails } from '../../types/auth'

/**
 * Ortak API istemcisi.
 *
 * PDF Sprint 18: "API istekleri component içinde dagink şekilde
 * yazilmamalidir. Ortak API client olusturulmalidir."
 */
export const api: AxiosInstance = axios.create({
  // Vite proxy sayesinde gorece yol yeterli. Ortam bazlı adres yok.
  baseURL: '/api/v1',
  headers: { 'Content-Type': 'application/json' },
  timeout: 15000,
})

/** Her istek için ayrı bir izleme kimliği. Backend loglarinda eslesiyor. */
function newCorrelationId(): string {
  return crypto.randomUUID().replace(/-/g, '')
}

// ISTEK INTERCEPTOR'I -- token ekle
api.interceptors.request.use((config) => {
  const token = useAuthStore.getState().accessToken

  if (token) {
    config.headers.Authorization = `Bearer ${token}`
  }

  // PDF Sprint 16: Correlation ID. Frontend uretip gönderiyor;
  // backend aynı değeri kullanip loglara isliyor. Boylece bir
  // kullanıcı sikayetini uctan uca izleyebiliyoruz.
  config.headers['X-Correlation-Id'] = newCorrelationId()

  return config
})

// YANIT INTERCEPTOR'I -- 401 alınca sessizce token yenile
//
// En onemli problem: es zamanli istekler
//
// Sayfa acilirken 4 istek aynı anda gidiyor ve access token'in süresi
// yeni dolmuş. Dordu de 401 aliyor.
//
// Naif bir çözüm her 401'de yenileme yapardi -> DORT yenileme isteği.
//
// Bu benim backend'imde FELAKET olurdu, çünkü refresh token
// ROTATION uyguluyorum:
//   1. istek token'i yeniler -> eski token İPTAL olur
//   2. istek AYNI eski token'i gönderir -> "iptal edilmiş token
//      tekrar kullanıldı!" -> backend CALINMA SALDIRISI sanip
//      kullanıcının TÜM oturumlarini kapatır
//
// Yani kullanıcı hiçbir sey yapmadan sistemden atilirdi. Ve bu hata
// yalnızca "birden fazla istek aynı anda giderse" olusacagi için
// tespit edilmesi çok zor olurdu.
//
// COZUM: Aynı anda YALNIZCA BIR yenileme çalışır. Digerleri o
// yenilemenin Promise'ini bekler ve sonucunu paylasir.

let refreshPromise: Promise<AuthResponse> | null = null

async function refreshAccessToken(): Promise<AuthResponse> {
  const refreshToken = useAuthStore.getState().refreshToken

  if (!refreshToken) {
    throw new Error('Refresh token yok')
  }

  // DIKKAT: Burada `api` DEĞİL, ham axios kullanıyorum.
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

/** Oturum sonlandi; kullanıcıyı giriş ekranına yolla. */
function endSession(reason: 'expired' | 'revoked') {
  useAuthStore.getState().clearSession()

  // window.location kullanıyorum, react-router'in navigate'ini değil.
  //
  // Sebep: bu kod bir React bileseninin DISINDA çalışıyor; hook
  // cagiramam. Ayrıca tam sayfa yenilemesi, bellekte kalmis olabilecek
  // eski durumu (onbellege alinmis sorgular, form verileri) da
  // temizliyor -- oturum sonlandiginda istedigim tam olarak bu.
  const target = reason === 'revoked' ? '/giris?sebep=guvenlik' : '/giris?sebep=sure-doldu'

  if (window.location.pathname !== '/giris') {
    window.location.href = target
  }
}

api.interceptors.response.use(
  (response) => response,

  async (error: AxiosError<ProblemDetails>) => {
    const original = error.config as InternalAxiosRequestConfig & { _retried?: boolean }

    // 401 degilse veya zaten bir kez denendiyse: hatayi olduğu gibi ilet.
    //
    // `_retried` bayragi ŞART: olmasaydı, yenileme sonrası tekrarlanan
    // istek de 401 alirsa (örneğin kullanıcı gerçekten yetkisiz)
    // sonsuz dongu olusurdu.
    if (error.response?.status !== 401 || original?._retried) {
      return Promise.reject(error)
    }

    // Giriş/kayıt endpointlerinde 401 NORMALDIR ("şifre yanlış").
    // Bunlari yenilemeye calismak anlamsiz olur.
    const url = original?.url ?? ''
    if (
      url.includes('/auth/login') ||
      url.includes('/auth/register') ||
      url.includes('/auth/refresh-token')
    ) {
      return Promise.reject(error)
    }

    // ---- Kilitleme: yalnızca ilk istek yenilemeyi baslatir ----
    if (!refreshPromise) {
      refreshPromise = refreshAccessToken().finally(() => {
        // Başarılı da olsa başarısız da olsa kilidi birak.
        // finally kullanmasaydim, başarısız bir yenilemeden sonra
        // refreshPromise dolu kalır ve bir daha HİÇ yenileme
        // yapilamazdi.
        refreshPromise = null
      })
    }

    try {
      // Es zamanlı tüm istekler AYNI Promise'i bekliyor.
      const auth = await refreshPromise

      useAuthStore.getState().updateTokens(auth)

      original._retried = true
      original.headers.Authorization = `Bearer ${auth.accessToken}`

      // Başarısız olan isteği yeni token'la tekrar dene.
      // Kullanıcı hiçbir sey fark etmez.
      return api(original)
    } catch {
      // Yenileme başarısız: refresh token da geçersiz.
      //
      // Backend "refresh_token_reused" dondurduyse bu bir GÜVENLİK
      // olayidir -- kullanıcıya farklı bir mesaj gosteriyorum.
      const code = error.response?.data?.errorCode
      endSession(code === 'auth.refresh_token_reused' ? 'revoked' : 'expired')

      return Promise.reject(error)
    }
  },
)

/**
 * Axios hatasindan Problem Details cikarir.
 *
 * Bunu tek yerde yapmamin sebebi: her bileşende
 * `error.response?.data?.detail ?? 'Bir hata oluştu'` yazmak
 * hem tekrar hem de hataya açık. Ag hatasinda (sunucu kapalı)
 * `response` hiç olmaz ve o zincir undefined döner.
 */
export function toProblem(error: unknown): ProblemDetails {
  if (axios.isAxiosError<ProblemDetails>(error)) {
    if (error.response?.data) {
      return error.response.data
    }

    // Sunucuya hiç ulasilamadi (ag hatası, sunucu kapalı, timeout).
    // Kullanıcıya "500 hatası" demek yanlış olur; sunucu cevap bile vermedi.
    return {
      status: 0,
      title: 'Bağlantı hatası',
      detail: 'Sunucuya ulaşılamıyor. Internet bağlantınızı kontrol edin.',
      errorCode: 'network.unreachable',
    }
  }

  return {
    status: 500,
    title: 'Beklenmeyen hata',
    detail: 'Beklenmeyen bir hata oluştu.',
    errorCode: 'client.unexpected',
  }
}

// Gelistirme yardimcisi
//
// API istemcisini YALNIZCA gelistirme modunda window'a bagliyorum.
//
// Ne ise yariyor? Tarayici konsolundan
//     await window.__api.get('/auth/me')
// yazip interceptor'in davranisini (token yenileme, hata isleme)
// doğrudan deneyebiliyorum. Ozellikle "es zamanlı 401" senaryosunu
// test etmenin en pratik yolu bu.
//
// import.meta.env.DEV, Vite tarafından üretim derlemesinde false'a
// sabitlenir ve bu blok paketten TAMAMEN silinir (tree shaking).
// Yani uretimde window.__api diye bir sey OLMAZ -- boyle bir kapiyi
// açık birakmak istemem.
if (import.meta.env.DEV) {
  ;(window as unknown as { __api: AxiosInstance }).__api = api
}
