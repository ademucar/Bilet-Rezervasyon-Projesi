import type { ReactNode } from 'react'
import { Link } from 'react-router-dom'

interface AuthLayoutProps {
  title: string
  subtitle?: string
  children: ReactNode
  footer?: ReactNode
}

/**
 * Kimlik doğrulama ekranlarinin ortak cercevesi.
 *
 * Bes ekran (giriş, kayıt, sifremi unuttum, şifre sıfırla, yetkisiz)
 * aynı düzeni paylasiyor. Her birinde tekrar yazsaydim, birinde
 * baslik boyutunu degistirdigimde digerleri geride kalırdı.
 */
export function AuthLayout({ title, subtitle, children, footer }: AuthLayoutProps) {
  return (
    <div className="flex min-h-screen items-center justify-center px-4 py-12">
      {/* max-w-md + w-full: mobilde tam genislik, masaustunde sinirli.
          PDF Sprint 18: "Responsive tasarım uygulanmalıdır." */}
      <div className="w-full max-w-md">
        <div className="mb-8 text-center">
          <Link to="/" className="inline-block text-2xl font-bold text-brand-600">
            Biletim
          </Link>
        </div>

        {/* main: sayfanin ana icerigi. Ekran okuyucular "ana icerige atla"
            komutuyla doğrudan buraya gelebiliyor. */}
        <main className="rounded-2xl border border-slate-200 bg-white p-8 shadow-sm">
          <h1 className="text-xl font-semibold text-slate-900">{title}</h1>
          {subtitle && <p className="mt-1.5 text-sm text-slate-500">{subtitle}</p>}

          <div className="mt-6">{children}</div>
        </main>

        {footer && <div className="mt-6 text-center text-sm text-slate-600">{footer}</div>}
      </div>
    </div>
  )
}
