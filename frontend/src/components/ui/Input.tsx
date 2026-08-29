import { forwardRef, useId } from 'react'
import type { InputHTMLAttributes } from 'react'

interface InputProps extends InputHTMLAttributes<HTMLInputElement> {
  label: string
  error?: string
}

/**
 * Etiketli ve hata gosterebilen metin girişi.
 *
 * forwardRef ŞART: React Hook Form, alanı register() ile bagladiginda
 * ref üzerinden DOM elemanina erisir. forwardRef olmasaydı RHF alanı
 * hiç goremezdi.
 */
export const Input = forwardRef<HTMLInputElement, InputProps>(
  ({ label, error, id, className = '', ...props }, ref) => {
    // useId: sunucu ve istemcide aynı, benzersiz kimlik üretir.
    // Math.random() kullansaydım her render'da degisir ve
    // label-input bağlantısı bozulurdu.
    const generatedId = useId()
    const inputId = id ?? generatedId
    const errorId = `${inputId}-error`

    return (
      <div className="space-y-1.5">
        {/* htmlFor + id eslesmesi: etikete tıklayınca alan odaklanir.
            Ekran okuyucular da alanin ne olduğunu bu sayede söyler. */}
        {/* ============================================================
            ETIKET KUCUK BUYUK HARF
            ============================================================
            14px normal metin yerine 10px büyük harf.

            Sebep: etiket bir BASLIK değil, alanin ADI. Govde
            metniyle aynı boyutta olunca formdaki her satır esit
            agirlikta gorunuyordu ve göz nereye bakacagini
            bilmiyordu.

            Küçük ve harf aralığı açık olunca etiket geri çekiliyor,
            KULLANICININ YAZDIGI deger one cikiyor.
            ============================================================ */}
        <label htmlFor={inputId} className="label-xs block text-slate-500">
          {label}
        </label>

        <input
          ref={ref}
          id={inputId}
          // aria-invalid: ekran okuyucuya "bu alanda hata var" der.
          // Yalnızca kırmızı kenarlik koysaydım görmeyen kullanıcı
          // hatanin varligini anlayamazdi.
          aria-invalid={error ? true : undefined}
          // aria-describedby: hata metnini alana BAGLAR. Ekran okuyucu
          // alana odaklandiginda hatayi da okur.
          aria-describedby={error ? errorId : undefined}
          className={
            'w-full rounded-[4px] border px-3 py-2.5 text-sm outline-none transition-colors ' +
            'placeholder:text-slate-400 ' +
            (error
              ? 'border-red-400 focus:border-red-500'
              : 'border-slate-300 focus:border-brand-500') +
            ' ' +
            className
          }
          {...props}
        />

        {error && (
          // role="alert": hata belirdiginde ekran okuyucu ANINDA okur.
          <p id={errorId} role="alert" className="text-sm text-red-600">
            {error}
          </p>
        )}
      </div>
    )
  },
)

Input.displayName = 'Input'
