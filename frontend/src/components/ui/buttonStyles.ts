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

// Metin BUYUK HARF ve harf araligi acik.
//
// Referans tasarimda butonlar afis dilinde: kisa, buyuk harf,
// harfler arasi nefes var. Kucuk harfle denedim, ayni renkte bile
// olsa "dugme" gibi degil "etiket" gibi duruyordu.
//
// Harf araligini eklemek sart: buyuk harfler sikisik dizildiginde
// okunmuyor. label-xs yardimcisinda da ayni karari vermistim.
export const BUTON_TEMEL =
  'inline-flex items-center justify-center gap-2 rounded-[4px] px-5 py-2.5 ' +
  'text-xs font-semibold uppercase tracking-[0.06em] transition-colors ' +
  'disabled:cursor-not-allowed disabled:opacity-60'

export const BUTON_VARYANT = {
  // Birincil ARTIK TURUNCU, koyu degil.
  //
  // Eski halde primary bg-slate-900 idi: koyu zeminde koyu dugme,
  // yani gorunmez. Yeni temada zemin zaten koyu yesil; eylemi one
  // cikaran tek sey turuncu.
  //
  // Sayfada tek bir turuncu olmali. Ikinci bir turuncu dugme,
  // ilkini degersizlestirir ve kullanici "hangisi asil islem?"
  // diye durur.
  primary: 'bg-brand-600 text-brand-50 hover:bg-brand-700',

  // Ikincil: krem kart uzerinde cerceve. Kart disinda (koyu zeminde)
  // de calisiyor cunku bg-white krem.
  secondary: 'border border-slate-300 bg-white text-slate-700 hover:bg-slate-50',

  ghost: 'text-brand-600 hover:bg-brand-50',
} as const
