import type { ReactNode } from 'react'

interface AlertProps {
  variant: 'error' | 'success' | 'info'
  children: ReactNode
}

export function Alert({ variant, children }: AlertProps) {
  const styles = {
    error: 'bg-red-50 text-red-800 border-red-200',
    success: 'bg-emerald-50 text-emerald-800 border-emerald-200',
    info: 'bg-brand-50 text-brand-700 border-brand-200',
  }

  return (
    <div
      // Hata mesajlari için "alert", digerleri için "status".
      // Fark: alert ekran okuyucuyu ANINDA boler, status ise
      // kullanıcı mola verdiginde okunur. Hata acil, bilgi değil.
      role={variant === 'error' ? 'alert' : 'status'}
      className={`rounded-lg border px-4 py-3 text-sm ${styles[variant]}`}
    >
      {children}
    </div>
  )
}
