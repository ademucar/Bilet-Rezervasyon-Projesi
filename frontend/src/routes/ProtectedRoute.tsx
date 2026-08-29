import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { useAuthStore } from '../stores/authStore'

interface ProtectedRouteProps {
  /**
   * Erişim için gereken roller. Boş ise yalnızca giriş yapmış olmak yeterli.
   * Kullanıcının bunlardan EN AZ BIRINE sahip olması gerekir.
   */
  roles?: string[]
}

/**
 * Korumalı route sarmalayicisi.
 *
 * ==================================================================
 * BU BIR GÜVENLİK ONLEMI DEĞİLDİR -- KULLANICI DENEYIMIDIR
 * ==================================================================
 * Kullanıcı tarayıcı konsolunu acip localStorage'daki rolü "Admin"
 * yapabilir ve bu bileşen önü admin paneline sokar.
 *
 * PEKI SORUN OLMAZ MI? Hayir. Çünkü o panelde gösterilecek her veri
 * API'den geliyor ve API, JWT'nin ICINDEKI rollere bakiyor. Token
 * imzali olduğu için rol değiştirilemez -- değiştirilirse imza bozulur.
 *
 * Yani sahte Admin, boş bir panel görür; tüm istekleri 403 döner.
 *
 * Bu bilesenin isi, MESRU kullanicilari erisemeyecekleri sayfalara
 * gitmekten alikoymak ve onlara anlamlı bir mesaj göstermek.
 * Gerçek kapi her zaman sunucudadır.
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
        // Kullanıcının gitmek istedigi adresi tasiyorum.
        // Giristen sonra LoginPage önü buraya geri gönderiyor.
        //
        // Bu olmasaydı kullanıcı derin bir linke tiklar, giriş yapar
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
