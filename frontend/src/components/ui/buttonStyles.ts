// Dugme sinif dizeleri, Button.tsx'ten AYRI bir dosyada.
//
// Once Button.tsx'ten disa acmistim ve oxlint hakli olarak uyardi:
//
//   react(only-export-components): Fast refresh only works when a
//   file only exports components.
//
// Vite'in hizli yenilemesi, bir dosya hem bilesen hem baska sey disa
// aciyorsa o dosyayi tam yenilemeye zorluyor -- yani Button.tsx'te
// tek satir degistirince tum sayfa yeniden yukleniyor ve form
// icerigi ucuyor.
//
// Bu sabitlere ihtiyacim var cunku bazi yerlerde dugme gibi GORUNEN
// ama aslinda BAGLANTI olan ogeler var ("Yeni etkinlik" gibi).
// <button onClick={navigate}> yapsaydim orta tikla yeni sekmede
// acma, adresi kopyalama ve ekran okuyucunun "baglanti" demesi
// kaybolurdu.

export const BUTON_TEMEL =
  'inline-flex items-center justify-center gap-2 rounded-[4px] px-4 py-2.5 ' +
  'text-sm font-medium transition-colors ' +
  'disabled:cursor-not-allowed disabled:opacity-60'

export const BUTON_VARYANT = {
  primary: 'bg-slate-900 text-white hover:bg-slate-800',
  secondary: 'border border-slate-300 bg-white text-slate-700 hover:bg-slate-50',
  ghost: 'text-brand-600 hover:bg-brand-50',
} as const
