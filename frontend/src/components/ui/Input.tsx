import { forwardRef, useId } from 'react'
import type { InputHTMLAttributes } from 'react'

interface InputProps extends InputHTMLAttributes<HTMLInputElement> {
  label: string
  error?: string
}

/**
 * Etiketli ve hata gosterebilen metin girisi.
 *
 * forwardRef SART: React Hook Form, alani register() ile bagladiginda
 * ref uzerinden DOM elemanina erisir. forwardRef olmasaydi RHF alani
 * hic goremezdi.
 */
export const Input = forwardRef<HTMLInputElement, InputProps>(
  ({ label, error, id, className = '', ...props }, ref) => {
    // useId: sunucu ve istemcide ayni, benzersiz kimlik uretir.
    // Math.random() kullansaydik her render'da degisir ve
    // label-input baglantisi bozulurdu.
    const generatedId = useId()
    const inputId = id ?? generatedId
    const errorId = `${inputId}-error`

    return (
      <div className="space-y-1.5">
        {/* htmlFor + id eslesmesi: etikete tiklayinca alan odaklanir.
            Ekran okuyucular da alanin ne oldugunu bu sayede soyler. */}
        <label htmlFor={inputId} className="block text-sm font-medium text-slate-700">
          {label}
        </label>

        <input
          ref={ref}
          id={inputId}
          // aria-invalid: ekran okuyucuya "bu alanda hata var" der.
          // Yalnizca kirmizi kenarlik koysaydik gormeyen kullanici
          // hatanin varligini anlayamazdi.
          aria-invalid={error ? true : undefined}
          // aria-describedby: hata metnini alana BAGLAR. Ekran okuyucu
          // alana odaklandiginda hatayi da okur.
          aria-describedby={error ? errorId : undefined}
          className={
            'w-full rounded-lg border px-3 py-2.5 text-sm outline-none transition-colors ' +
            'placeholder:text-slate-400 ' +
            (error
              ? 'border-red-400 focus:border-red-500'
              : 'border-slate-300 focus:border-brand-500') +
            ' ' + className
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
