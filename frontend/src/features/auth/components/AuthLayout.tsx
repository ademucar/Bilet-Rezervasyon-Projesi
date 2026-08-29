import type { ReactNode } from 'react'
import { Link } from 'react-router-dom'

interface AuthLayoutProps {
  title: string
  subtitle?: string
  children: ReactNode
  footer?: ReactNode
  /**
   * Sol sütunda gösterilecek yardımcı not.
   *
   * Her ekranın kullanıcıya söyleyecek farklı bir şeyi var:
   * girişte hesap kilidi, kayıtta e-posta onayı, şifre
   * sıfırlamada bağlantının ömrü. Varsayılanı yok -- ekran
   * söyleyecek bir şey bulamıyorsa sütun sadece markayı taşır.
   */
  aside?: ReactNode
}

/**
 *
 * KİMLİK DOĞRULAMA EKRANLARININ ORTAK ÇERÇEVESİ
 *
 * Beş ekran (giriş, kayıt, şifremi unuttum, şifre sıfırla,
 * yetkisiz) aynı düzeni paylaşıyor. Her birinde tekrar yazsaydım,
 * birinde başlık boyutunu değiştirdiğimde diğerleri geride kalırdı.
 *
 * NEDEN ORTALANMIŞ KART DEĞİL DE İKİ SÜTUN?
 *
 * Önceki hâl ekranın ortasında yüzen dar beyaz bir karttı. Sorun
 * şu: kullanıcı buraya çoğu zaman bir AKIŞIN ORTASINDA düşüyor --
 * koltuk seçerken, rezervasyona devam ederken. Ortalanmış kart o
 * bağlamı tamamen siliyor; kullanıcı nereden geldiğini ve neden
 * giriş yaptığını göremiyor.
 *
 * Koyu sol sütun iki iş yapıyor:
 *   1. Markayı taşıyor (logo artık formun üstünde yer yemiyor)
 *   2. Ekrana özgü bağlamı taşıyor (hesap kilidi kuralı gibi)
 *
 * Mobilde sütun gizleniyor (hidden md:flex): 375px genişlikte iki
 * sütun ikisini de okunmaz yapardı; orada zaten tek iş var, form.
 *
 */
export function AuthLayout({ title, subtitle, children, footer, aside }: AuthLayoutProps) {
  return (
    <div className="flex min-h-screen items-center justify-center px-4 py-10">
      <div className="w-full max-w-3xl">
        <div className="flex overflow-hidden rounded-[4px] border border-slate-300 bg-white">
          {/* ============================================================
              SOL SÜTUN -- bağlam
              ============================================================ */}
          <aside className="hidden w-[280px] shrink-0 flex-col justify-between bg-slate-900 p-6 md:flex">
            <div>
              <Link to="/" className="mb-7 flex items-center gap-2">
                {/* Bilet ikonu: sol üstte metinsiz de tanınabilir bir
                    işaret. Marka moru burada -- koyu zeminde tek renk
                    lekesi olarak -- hâlâ işini görüyor. */}
                <svg
                  className="size-[18px] text-brand-500"
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  strokeWidth="2"
                  strokeLinecap="round"
                  strokeLinejoin="round"
                  aria-hidden="true"
                >
                  <path d="M3 9a3 3 0 0 1 0 6v3a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-3a3 3 0 0 1 0-6V6a2 2 0 0 0-2-2H5a2 2 0 0 0-2 2Z" />
                  <path d="M13 5v2M13 11v2M13 17v2" />
                </svg>
                <span className="font-display text-[15px] font-bold tracking-tight text-white">
                  Biletim
                </span>
              </Link>

              {aside}
            </div>

            <p className="text-xs leading-relaxed text-slate-500">
              Konser, tiyatro ve daha fazlası. Koltuğunu seç, ayırt.
            </p>
          </aside>

          {/* ============================================================
              SAĞ SÜTUN -- form
              ============================================================
              main: sayfanın ana içeriği. Ekran okuyucular "ana içeriğe
              atla" komutuyla doğrudan buraya gelebiliyor.
              ============================================================ */}
          <main className="min-w-0 flex-grow p-7">
            {/* Mobilde sol sütun gizli olduğu için marka buraya taşınıyor.
                Yoksa telefonda ekranda hiçbir yerde ad görünmezdi. */}
            <Link
              to="/"
              className="mb-6 inline-block font-display text-lg font-bold text-slate-900 md:hidden"
            >
              Biletim
            </Link>

            <h1 className="font-display text-xl font-semibold tracking-tight text-slate-900">
              {title}
            </h1>
            {subtitle && <p className="mt-1 text-[13px] text-slate-500">{subtitle}</p>}

            <div className="mt-6">{children}</div>

            {footer && (
              <div className="mt-6 border-t border-slate-200 pt-4 text-sm text-slate-600">
                {footer}
              </div>
            )}
          </main>
        </div>
      </div>
    </div>
  )
}

/**
 * Sol sütunda kullanılan bilgilendirme satırı.
 *
 * Kilit, saat, zarf gibi bir ikon + tek cümle. Ayrı bir bileşen
 * yaptım çünkü beş ekranda da aynı hizalama ve renk gerekiyor;
 * elle yazsaydım biri kaçınılmaz olarak yamuk olurdu.
 */
export function AuthAsideNote({ icon, children }: { icon: ReactNode; children: ReactNode }) {
  return (
    <div className="flex items-start gap-2.5">
      <span className="mt-px shrink-0 text-slate-500" aria-hidden="true">
        {icon}
      </span>
      <span className="text-xs leading-relaxed text-slate-400">{children}</span>
    </div>
  )
}
