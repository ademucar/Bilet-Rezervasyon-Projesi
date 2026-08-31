import { AdminLayout } from '../components/AdminLayout'
import { CategoryPanel } from '../components/CategoryPanel'
import { CityPanel } from '../components/CityPanel'

/**
 * Kategori ve şehir yönetimi -- PDF sayfa 5:
 * "Admin: Kategori, şehir ve salon yönetimi."
 *
 * Üçünden salon Sprint 4'te yapılmıştı (/admin/mekanlar). Kategori ve
 * şehirde yalnızca okuma ucu vardı: listeler veritabanına elle
 * girilen tohum veriyle doluydu ve arayüzden yeni kategori eklemenin
 * hiçbir yolu yoktu.
 *
 * İkisini AYNI sayfaya koydum, ayrı iki menü maddesi açmadım. İkisi
 * de küçük referans listesi ve admin genellikle ikisine de aynı
 * sebeple giriyor: "filtre listesinde eksik bir şey var". Menüyü beş
 * maddeye çıkarmak, asıl işlerin (etkinlik, başvuru) görünürlüğünü
 * düşürürdü.
 */
export function ReferenceDataPage() {
  return (
    <AdminLayout title="Tanımlar" subtitle="Etkinlik kategorileri ve şehirler">
      {/* Tek sütunda alt alta değil, iki sütun: ikisi de kısa liste
          ve yan yana durunca admin hangisini düzenlediğini
          karıştırmıyor. Dar ekranda alt alta düşüyor. */}
      <div className="grid gap-5 lg:grid-cols-2">
        <CategoryPanel />
        <CityPanel />
      </div>
    </AdminLayout>
  )
}
