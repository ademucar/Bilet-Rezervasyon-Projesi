import type { ReactNode } from 'react'
import { Link, NavLink } from 'react-router-dom'
import { useAuthStore } from '../../../stores/authStore'

interface AdminLayoutProps {
  title: string
  subtitle?: string
  children: ReactNode
  /** Sayfa basligi ustunde gösterilecek geri bağlantısı. */
  backTo?: { label: string; to: string }
}

/** Admin panelinin ortak cercevesi. */
export function AdminLayout({ title, subtitle, children, backTo }: AdminLayoutProps) {
  const user = useAuthStore((s) => s.user)

  return (
    <div className="min-h-screen">
      <header className="border-b border-slate-200 bg-white">
        <div className="mx-auto flex max-w-6xl flex-wrap items-center justify-between gap-4 px-4 py-4">
          <Link to="/" className="text-lg font-bold text-brand-600">
            Biletim
          </Link>

          {/* nav + aria-label: ekran okuyucular birden fazla nav
              olduğunda hangisinin ne olduğunu bu etiketle ayırt eder. */}
          <nav aria-label="Yönetim menüsü" className="flex gap-1 text-sm">
            <AdminLink to="/admin/etkinlikler">Etkinlikler</AdminLink>
            <AdminLink to="/admin/basvurular">Başvurular</AdminLink>
            <AdminLink to="/admin/mekanlar">Mekanlar</AdminLink>
          </nav>

          <span className="text-sm text-slate-500">{user?.email}</span>
        </div>
      </header>

      <main className="mx-auto max-w-6xl px-4 py-8">
        {backTo && (
          <Link to={backTo.to} className="mb-4 inline-block text-sm text-brand-600 hover:underline">
            &larr; {backTo.label}
          </Link>
        )}

        <div className="mb-6">
          <h1 className="font-display text-2xl font-bold tracking-tight text-slate-900">{title}</h1>
          {subtitle && <p className="mt-1 text-sm text-slate-500">{subtitle}</p>}
        </div>

        {children}
      </main>
    </div>
  )
}

function AdminLink({ to, children }: { to: string; children: ReactNode }) {
  return (
    <NavLink
      to={to}
      // NavLink, aktif route'u kendisi tespit eder ve className'e
      // isActive gönderir. Bunu elle useLocation ile yapsaydim her
      // menu ogesinde tekrar yazmamiz gerekirdi.
      className={({ isActive }) =>
        `rounded-lg px-3 py-2 transition-colors ${
          isActive ? 'bg-brand-50 font-medium text-brand-700' : 'text-slate-600 hover:bg-slate-100'
        }`
      }
    >
      {children}
    </NavLink>
  )
}
