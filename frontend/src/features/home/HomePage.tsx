import { useMutation } from '@tanstack/react-query'
import { useNavigate } from 'react-router-dom'
import { useAuthStore } from '../../stores/authStore'
import { authApi } from '../auth/api/authApi'
import { Button } from '../../components/ui/Button'

/**
 * Gecici ana sayfa.
 * Sprint 11'de gercek etkinlik listesiyle degistirilecek.
 * Su an auth akisinin calistigini gormek icin var.
 */
export function HomePage() {
  const navigate = useNavigate()
  const user = useAuthStore((s) => s.user)
  const refreshToken = useAuthStore((s) => s.refreshToken)
  const clearSession = useAuthStore((s) => s.clearSession)

  const logout = useMutation({
    mutationFn: () => authApi.logout(refreshToken),

    // onSettled: basarili da olsa basarisiz da olsa calisir.
    //
    // Neden onSuccess degil? Cunku sunucuya ulasilamasa bile
    // kullaniciyi cikarmaliyiz. "Cikis yapamadiniz" demek sacma
    // olurdu -- kullanicinin niyeti oturumu kapatmak.
    //
    // Sunucudaki token yine de suresi dolunca gecersizlesecek.
    onSettled: () => {
      clearSession()
      navigate('/giris', { replace: true })
    },
  })

  return (
    <div className="mx-auto max-w-3xl px-4 py-12">
      <header className="mb-8 flex flex-wrap items-center justify-between gap-4">
        <div>
          <h1 className="text-2xl font-bold text-slate-900">Biletim</h1>
          <p className="text-sm text-slate-500">Etkinlik ve bilet rezervasyon sistemi</p>
        </div>

        <Button variant="secondary" onClick={() => logout.mutate()} isLoading={logout.isPending}>
          Cikis yap
        </Button>
      </header>

      <section className="rounded-2xl border border-slate-200 bg-white p-6 shadow-sm">
        <h2 className="text-lg font-semibold text-slate-900">Hos geldiniz</h2>

        {user && (
          <dl className="mt-4 space-y-2 text-sm">
            <div className="flex gap-2">
              <dt className="w-32 text-slate-500">Ad Soyad</dt>
              <dd className="font-medium text-slate-900">{user.firstName} {user.lastName}</dd>
            </div>
            <div className="flex gap-2">
              <dt className="w-32 text-slate-500">E-posta</dt>
              <dd className="font-medium text-slate-900">{user.email}</dd>
            </div>
            <div className="flex gap-2">
              <dt className="w-32 text-slate-500">Roller</dt>
              <dd className="font-medium text-slate-900">{user.roles.join(', ')}</dd>
            </div>
            <div className="flex gap-2">
              <dt className="w-32 text-slate-500">E-posta onayi</dt>
              <dd className="font-medium text-slate-900">
                {user.isEmailConfirmed ? 'Onaylandi' : 'Bekliyor'}
              </dd>
            </div>
          </dl>
        )}
      </section>

      <p className="mt-6 text-sm text-slate-500">
        Etkinlik listesi Sprint 11'de eklenecek.
      </p>
    </div>
  )
}
