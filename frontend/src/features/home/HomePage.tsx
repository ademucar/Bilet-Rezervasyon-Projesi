import { Link } from 'react-router-dom'
import { useAuthStore } from '../../stores/authStore'
import { SiteHeader } from '../../components/layout/SiteHeader'

/**
 * Ana sayfa.
 *
 * Sprint 11'de one cikan etkinlikler, kategoriler ve arama ile
 * degistirilecek. Su an akisa giriş kapisi ve hesap özeti.
 *
 * Çıkış butonu artık burada DEĞİL, SiteHeader'da: her sayfada
 * ulasilabilir olması gerekiyordu.
 */
export function HomePage() {
  const user = useAuthStore((s) => s.user)

  const cards = [
    {
      to: '/etkinlikler',
      title: 'Etkinlikler',
      description: 'Konser, tiyatro ve daha fazlası. Koltuğunu seç, ayırt.',
    },
    {
      to: '/rezervasyonlarim',
      title: 'Rezervasyonlarim',
      description: 'Ödemesi tamamlanmamış rezervasyonlarına buradan devam et.',
    },
    {
      to: '/biletlerim',
      title: 'Biletlerim',
      description: 'Geçerli biletlerin ve girişte okutacağın QR kodların.',
    },
  ]

  return (
    <div className="min-h-screen bg-slate-50">
      <SiteHeader />

      <main className="mx-auto max-w-4xl px-4 py-10">
        <h1 className="text-2xl font-bold text-slate-900">
          Hos geldiniz{user ? `, ${user.firstName}` : ''}
        </h1>
        <p className="mt-1 text-sm text-slate-500">Nereden devam etmek istersiniz?</p>

        <div className="mt-6 grid gap-4 sm:grid-cols-3">
          {cards.map((card) => (
            <Link
              key={card.to}
              to={card.to}
              className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm transition-shadow hover:shadow-md"
            >
              <h2 className="font-semibold text-slate-900">{card.title}</h2>
              <p className="mt-2 text-sm text-slate-500">{card.description}</p>
            </Link>
          ))}
        </div>

        {user && (
          <section className="mt-8 rounded-2xl border border-slate-200 bg-white p-6 shadow-sm">
            <h2 className="text-lg font-semibold text-slate-900">Hesabım</h2>

            <dl className="mt-4 space-y-2 text-sm">
              <div className="flex gap-2">
                <dt className="w-32 text-slate-500">Ad Soyad</dt>
                <dd className="font-medium text-slate-900">
                  {user.firstName} {user.lastName}
                </dd>
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
                <dt className="w-32 text-slate-500">E-posta onayı</dt>
                <dd className="font-medium text-slate-900">
                  {user.isEmailConfirmed ? 'Onaylandı' : 'Bekliyor'}
                </dd>
              </div>
            </dl>
          </section>
        )}
      </main>
    </div>
  )
}
