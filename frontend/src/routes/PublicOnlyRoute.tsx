import { Navigate, Outlet } from 'react-router-dom'
import { useAuthStore } from '../stores/authStore'

/**
 * Yalnızca giris yapmamis kullanicilarin gorebilecegi sayfalar.
 *
 * Neden gerekli? Giriş yapmış bir kullanıcı /giriş adresine
 * gittiginde giriş formunu gormesi anlamsizdir -- kafa karistirir
 * ve "acaba oturumum mu kapandı?" dusundurur.
 *
 * Bu bileşen önü ana sayfaya yonlendiriyor.
 */
export function PublicOnlyRoute() {
  const isAuthenticated = useAuthStore((s) => s.isAuthenticated())

  return isAuthenticated ? <Navigate to="/" replace /> : <Outlet />
}
