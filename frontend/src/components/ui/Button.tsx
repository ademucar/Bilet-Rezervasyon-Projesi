import type { ButtonHTMLAttributes, ReactNode } from 'react'
import { BUTON_TEMEL, BUTON_VARYANT } from './buttonStyles'

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: 'primary' | 'secondary' | 'ghost'
  isLoading?: boolean
  children: ReactNode
}

/**
 * Ortak buton.
 *
 * Neden her yerde ham <button> yazmiyorum? Çünkü o zaman 40 farklı
 * ekranda 40 farklı buton gorunumu olusur ve "loading" durumunu
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
  // rounded-lg YERINE rounded-[4px]
  //
  // Eski halde her dugme 8px yuvarlaktı. Kartlar 16px, girdiler
  // 8px... uc farklı yaricap vardi ve hicbiri digerine
  // benzemiyordu.
  //
  // Artık tek deger: 4px. Dugme, girdi ve kart aynı ailedenmis gibi
  // duruyor.
  const base = BUTON_TEMEL

  // Birincil dugme slate-900, marka rengi değil
  //
  // Marka moru her dugmede kullanildiginda "hangisi asil eylem?"
  // sorusu cevapsiz kaliyordu: sayfada bes mor dugme vardi.
  //
  // Artık mor YALNIZCA baglantilarda ve seçili durumda. Eylem
  // dugmesi koyu lacivert — sayfada tek bir tane olduğunda gozu
  // doğrudan oraya cekiyor.
  const variants = BUTON_VARYANT

  return (
    <button
      // Yukleniyorken butonu DA pasiflestiriyorum.
      // Yoksa kullanıcı ust uste tiklar ve aynı istek 5 kez gider.
      // (Backend'de idempotency var ama isteği hiç gondermemek daha iyi.)
      disabled={disabled || isLoading}
      className={`${base} ${variants[variant]} ${className}`}
      {...props}
    >
      {isLoading && (
        <span
          className="h-4 w-4 animate-spin rounded-full border-2 border-current border-t-transparent"
          // aria-hidden: ekran okuyucu bu görsel suslemeyi okumasin.
          // Yükleniyor bilgisini aşağıdaki metin zaten veriyor.
          aria-hidden="true"
        />
      )}
      {children}
    </button>
  )
}
