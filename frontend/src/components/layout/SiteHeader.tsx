import { useMutation } from '@tanstack/react-query'
import { NavLink, useNavigate } from 'react-router-dom'
import { useAuthStore } from '../../stores/authStore'
import { authApi } from '../../features/auth/api/authApi'
import { Button } from '../ui/Button'
import { NotificationBell } from '../../features/notifications/NotificationBell'
import { Roles } from '../../types/auth'

/**
 * Uygulamanin ust cubugu.
 *
 * Sprint 7'ye kadar her sayfa kendi basligini ciziyordu. Artık
 * bilet alma akisinda 5 sayfa var ve kullanıcının "biletlerim"e
 * her yerden ulasabilmesi gerekiyor -- ortak bir cubuk sart oldu.
 */
export function SiteHeader() {
  const navigate = useNavigate()
  const user = useAuthStore((s) => s.user)
  const refreshToken = useAuthStore((s) => s.refreshToken)
  const clearSession = useAuthStore((s) => s.clearSession)

  const logout = useMutation({
    mutationFn: () => authApi.logout(refreshToken),

    // onSettled: başarılı da olsa başarısız da olsa çalışır.
    // Sunucuya ulasilamasa bile kullanıcıyı cikarmaliyiz;
    // "çıkış yapamadiniz" demek sacma olurdu.
    onSettled: () => {
      clearSession()
      navigate('/giris', { replace: true })
    },
  })

  // NavLink, aktif rotada `isActive` veriyor. Bunu kullanmak
  // kullanıcıya "hangi sayfadayim" bilgisini veriyor -- basit ama
  // olmadiginda kaybolmus hissettiren bir detay.
  const linkClass = ({ isActive }: { isActive: boolean }) =>
    `rounded-lg px-3 py-2 text-sm font-medium transition-colors ${
      isActive ? 'bg-brand-50 text-brand-700' : 'text-slate-600 hover:bg-slate-100'
    }`

  return (
    <header className="border-b border-slate-300 bg-white">
      <div className="mx-auto flex max-w-6xl flex-wrap items-center gap-4 px-4 py-3">
        <NavLink to="/" className="font-display text-lg font-bold tracking-tight text-slate-900">
          Biletim
        </NavLink>

        <nav className="flex flex-1 flex-wrap items-center gap-1">
          <NavLink to="/etkinlikler" className={linkClass}>
            Etkinlikler
          </NavLink>
          <NavLink to="/rezervasyonlarim" className={linkClass}>
            Rezervasyonlarım
          </NavLink>
          <NavLink to="/biletlerim" className={linkClass}>
            Biletlerim
          </NavLink>
          <NavLink to="/favorilerim" className={linkClass}>
            Favorilerim
          </NavLink>

          {/* Admin baglantisini yalnızca admin GORUR.
              UNUTMA: bu bir güvenlik önlemi DEĞİL, kullanıcı deneyimi.
              Baglantiyi gizlemek adresi elle yazmayi engellemez --
              gerçek kontrol backend'deki AdminOnly policy'sinde.

              Rolu `user` üzerinden okuyorum, store.getState() ile
              DEĞİL: getState() abonelik kurmaz, kullanıcı değişince
              bu satır yeniden hesaplanmazdi. */}
          {/* Panel: organizatör VEYA admin görebilir.
              Normal kullanıcı gorse de backend 403 döner. */}
          {(user?.roles.includes(Roles.Admin) || user?.roles.includes(Roles.Organizer)) && (
            <>
              <NavLink to="/panel" className={linkClass}>
                Panel
              </NavLink>

              {/* Etkinlik yonetimi Panel'den AYRI bir baglanti.
                  Panel rapor ve metrik gosteriyor, burasi is
                  yapiliyor. Ikisini tek baglantida toplasaydim
                  organizator "etkinlik olusturmak icin nereye
                  basacagim" diye aramak zorunda kalirdi. */}
              <NavLink to="/panel/etkinlikler" className={linkClass}>
                Etkinliklerim
              </NavLink>
            </>
          )}

          {user?.roles.includes(Roles.Admin) && (
            // "Yonetim" artik Mekanlar'a degil Etkinlikler'e gidiyor.
            //
            // Onceden mekan listesine dusuyordu ve bu yanlisti: menude
            // "Yonetim" yaziyor, acilan sayfada "Mekanlar" -- tiklayan
            // kisi yanlis yere geldigini saniyor. Ustelik adminin bu
            // panele girme sebebi neredeyse her zaman onay bekleyen bir
            // etkinlik; mekan duzenlemek nadir bir is.
            <NavLink to="/admin/etkinlikler" className={linkClass}>
              Yönetim
            </NavLink>
          )}
        </nav>

        {/* PDF Sprint 14: bildirim zili. Her sayfada erişilebilir. */}
        <NotificationBell />

        {user && (
          <span className="hidden text-sm text-slate-500 sm:inline">
            {user.firstName} {user.lastName}
          </span>
        )}

        <Button variant="secondary" onClick={() => logout.mutate()} isLoading={logout.isPending}>
          Çıkış
        </Button>
      </div>
    </header>
  )
}
