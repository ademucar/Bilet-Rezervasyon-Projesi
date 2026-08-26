import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { useAuthStore } from '../stores/authStore'

interface ProtectedRouteProps {
  /**
   * Erisim icin gereken roller. Bos ise yalnizca giris yapmis olmak yeterli.
   * Kullanicinin bunlardan EN AZ BIRINE sahip olmasi gerekir.
   */
  roles?: string[]
}

/**
 * Korumali route sarmalayicisi.
 *
 * ==================================================================
 * BU BIR GUVENLIK ONLEMI DEGILDIR -- KULLANICI DENEYIMIDIR
 * ==================================================================
 * Kullanici tarayici konsolunu acip localStorage'daki rolu "Admin"
 * yapabilir ve bu bilesen onu admin paneline sokar.
 *
 * PEKI SORUN OLMAZ MI? Hayir. Cunku o panelde gosterilecek her veri
 * API'den geliyor ve API, JWT'nin ICINDEKI rollere bakiyor. Token
 * imzali oldugu icin rol degistirilemez -- degistirilirse imza bozulur.
 *
 * Yani sahte Admin, bos bir panel gorur; tum istekleri 403 doner.
 *
 * Bu bilesenin isi, MESRU kullanicilari erisemeyecekleri sayfalara
 * gitmekten alikoymak ve onlara anlamli bir mesaj gostermek.
 * Gercek kapi her zaman sunucudadir.
 * ==================================================================
 */
export function ProtectedRoute({ roles }: ProtectedRouteProps) {
  const location = useLocation()
  const isAuthenticated = useAuthStore((s) => s.isAuthenticated())
  const hasRole = useAuthStore((s) => s.hasRole)

  if (!isAuthenticated) {
    return (
      <Navigate
        to="/giris"
        replace
        // Kullanicinin gitmek istedigi adresi tasiyorum.
        // Giristen sonra LoginPage onu buraya geri gonderiyor.
        //
        // Bu olmasaydi kullanici derin bir linke tiklar, giris yapar
        // ve ana sayfada bulurdu kendini -- nereye gitmek istedigini
        // hatirlayip tekrar bulmasi gerekirdi.
        state={{ from: location.pathname + location.search }}
      />
    )
  }

  if (roles && roles.length > 0 && !hasRole(...roles)) {
    return <Navigate to="/yetkisiz" replace />
  }

  // Outlet: ic ice route'larin render edilecegi yer.
  return <Outlet />
}
