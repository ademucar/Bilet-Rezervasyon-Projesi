import { useMutation } from '@tanstack/react-query'
import { NavLink, useNavigate } from 'react-router-dom'
import { useAuthStore } from '../../stores/authStore'
import { authApi } from '../../features/auth/api/authApi'
import { Button } from '../ui/Button'
import { Roles } from '../../types/auth'

/**
 * Uygulamanin ust cubugu.
 *
 * Sprint 7'ye kadar her sayfa kendi basligini ciziyordu. Artik
 * bilet alma akisinda 5 sayfa var ve kullanicinin "biletlerim"e
 * her yerden ulasabilmesi gerekiyor -- ortak bir cubuk sart oldu.
 */
export function SiteHeader() {
  const navigate = useNavigate()
  const user = useAuthStore((s) => s.user)
  const refreshToken = useAuthStore((s) => s.refreshToken)
  const clearSession = useAuthStore((s) => s.clearSession)

  const logout = useMutation({
    mutationFn: () => authApi.logout(refreshToken),

    // onSettled: basarili da olsa basarisiz da olsa calisir.
    // Sunucuya ulasilamasa bile kullaniciyi cikarmaliyiz;
    // "cikis yapamadiniz" demek sacma olurdu.
    onSettled: () => {
      clearSession()
      navigate('/giris', { replace: true })
    },
  })

  // NavLink, aktif rotada `isActive` veriyor. Bunu kullanmak
  // kullaniciya "hangi sayfadayim" bilgisini veriyor -- basit ama
  // olmadiginda kaybolmus hissettiren bir detay.
  const linkClass = ({ isActive }: { isActive: boolean }) =>
    `rounded-lg px-3 py-2 text-sm font-medium transition-colors ${
      isActive ? 'bg-brand-50 text-brand-700' : 'text-slate-600 hover:bg-slate-100'
    }`

  return (
    <header className="border-b border-slate-200 bg-white">
      <div className="mx-auto flex max-w-6xl flex-wrap items-center gap-4 px-4 py-3">
        <NavLink to="/" className="text-lg font-bold text-slate-900">
          Biletim
        </NavLink>

        <nav className="flex flex-1 flex-wrap items-center gap-1">
          <NavLink to="/etkinlikler" className={linkClass}>
            Etkinlikler
          </NavLink>
          <NavLink to="/rezervasyonlarim" className={linkClass}>
            Rezervasyonlarim
          </NavLink>
          <NavLink to="/biletlerim" className={linkClass}>
            Biletlerim
          </NavLink>

          {/* Admin baglantisini yalnizca admin GORUR.
              UNUTMA: bu bir guvenlik onlemi DEGIL, kullanici deneyimi.
              Baglantiyi gizlemek adresi elle yazmayi engellemez --
              gercek kontrol backend'deki AdminOnly policy'sinde.

              Rolu `user` uzerinden okuyorum, store.getState() ile
              DEGIL: getState() abonelik kurmaz, kullanici degisince
              bu satir yeniden hesaplanmazdi. */}
          {user?.roles.includes(Roles.Admin) && (
            <NavLink to="/admin/mekanlar" className={linkClass}>
              Yonetim
            </NavLink>
          )}
        </nav>

        {user && (
          <span className="hidden text-sm text-slate-500 sm:inline">
            {user.firstName} {user.lastName}
          </span>
        )}

        <Button variant="secondary" onClick={() => logout.mutate()} isLoading={logout.isPending}>
          Cikis
        </Button>
      </div>
    </header>
  )
}
