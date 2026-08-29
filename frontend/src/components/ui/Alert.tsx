import type { ReactNode } from 'react'

interface AlertProps {
  variant: 'error' | 'success' | 'info'
  children: ReactNode
}

export function Alert({ variant, children }: AlertProps) {
  // SOL KENAR ÇİZGİSİ
  //
  // Üç varyantın da zemini hafif renkliydi ve köşeleri 8px
  // yuvarlaktı. Sayfada iki uyarı yan yana geldiğinde (örneğin
  // "oturum süreniz doldu" + "e-posta hatalı") ekran renk lekesine
  // dönüyordu.
  //
  // Zemin yine renkli ama daha soluk; asıl işareti 2px'lik sol
  // kenar veriyor. Bu, gazetede kenar çizgili kutu geleneğinden
  // geliyor: metin akışını kesmeden "buraya bak" diyor.
  const styles = {
    error: 'border-slate-200 border-l-red-600 bg-red-50/70 text-red-800',
    success: 'border-slate-200 border-l-emerald-600 bg-emerald-50/70 text-emerald-800',
    info: 'border-slate-200 border-l-brand-600 bg-brand-50/70 text-brand-800',
  }

  return (
    <div
      // Hata mesajlari için "alert", digerleri için "status".
      // Fark: alert ekran okuyucuyu ANINDA boler, status ise
      // kullanıcı mola verdiginde okunur. Hata acil, bilgi değil.
      role={variant === 'error' ? 'alert' : 'status'}
      className={`rounded-[4px] border border-l-2 px-4 py-3 text-sm ${styles[variant]}`}
    >
      {children}
    </div>
  )
}
