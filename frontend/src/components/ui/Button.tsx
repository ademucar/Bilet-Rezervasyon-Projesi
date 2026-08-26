import type { ButtonHTMLAttributes, ReactNode } from 'react'

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: 'primary' | 'secondary' | 'ghost'
  isLoading?: boolean
  children: ReactNode
}

/**
 * Ortak buton.
 *
 * Neden her yerde ham <button> yazmiyorum? Cunku o zaman 40 farkli
 * ekranda 40 farkli buton gorunumu olusur ve "loading" durumunu
 * gostermeyi birinde mutlaka unuturuz.
 */
export function Button({
  variant = 'primary',
  isLoading = false,
  disabled,
  className = '',
  children,
  ...props
}: ButtonProps) {
  const base =
    'inline-flex items-center justify-center gap-2 rounded-lg px-4 py-2.5 text-sm font-medium ' +
    'transition-colors disabled:cursor-not-allowed disabled:opacity-60'

  const variants = {
    primary: 'bg-brand-600 text-white hover:bg-brand-700',
    secondary: 'bg-white text-slate-700 border border-slate-300 hover:bg-slate-50',
    ghost: 'text-brand-600 hover:bg-brand-50',
  }

  return (
    <button
      // Yukleniyorken butonu DA pasiflestiriyorum.
      // Yoksa kullanici ust uste tiklar ve ayni istek 5 kez gider.
      // (Backend'de idempotency var ama istegi hic gondermemek daha iyi.)
      disabled={disabled || isLoading}
      className={`${base} ${variants[variant]} ${className}`}
      {...props}
    >
      {isLoading && (
        <span
          className="h-4 w-4 animate-spin rounded-full border-2 border-current border-t-transparent"
          // aria-hidden: ekran okuyucu bu gorsel suslemeyi okumasin.
          // Yukleniyor bilgisini asagidaki metin zaten veriyor.
          aria-hidden="true"
        />
      )}
      {children}
    </button>
  )
}
