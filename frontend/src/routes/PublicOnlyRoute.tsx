import { Navigate, Outlet } from 'react-router-dom'
import { useAuthStore } from '../stores/authStore'

/**
 * Yalnizca GIRIS YAPMAMIS kullanicilarin gorebilecegi sayfalar.
 *
 * Neden gerekli? Giris yapmis bir kullanici /giris adresine
 * gittiginde giris formunu gormesi anlamsizdir -- kafa karistirir
 * ve "acaba oturumum mu kapandi?" dusundurur.
 *
 * Bu bilesen onu ana sayfaya yonlendiriyor.
 */
export function PublicOnlyRoute() {
  const isAuthenticated = useAuthStore((s) => s.isAuthenticated())

  return isAuthenticated ? <Navigate to="/" replace /> : <Outlet />
}
