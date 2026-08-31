import { api } from '../../../lib/api/client'
import type { Paged } from './adminApi'

// Kullanici yonetimi ve denetim kayitlari -- PDF sayfa 5:
//   "Admin: Tum kullanicilari yonetebilir."
//   "Admin: Audit log kayitlarini inceleyebilir."
//
// Ikisi de sifirdan yazildi: ne uc vardi ne ekran. AuditLogs tablosu
// Sprint 12'den beri duruyordu ama icine yalnizca bilet turu fiyat
// degisikligi yaziliyordu -- o da hic tetiklenmedigi icin tablo
// tamamen bostu.

export interface UserListItem {
  id: string
  email: string
  firstName: string
  lastName: string
  phoneNumber: string | null
  isActive: boolean
  isEmailConfirmed: boolean
  isLockedOut: boolean
  createdAt: string
  roles: string[]
}

export interface UserFilters {
  search?: string
  role?: string
  isActive?: boolean
  pageNumber?: number
  pageSize?: number
}

export const userAdminApi = {
  list: async (filters: UserFilters): Promise<Paged<UserListItem>> => {
    // Boş ve tanımsız alanları eliyorum: axios boş metni ?search=
    // olarak gönderiyor ve backend'de bool? alanına boş metin
    // bağlanmaya çalışılınca istek 400 dönüyor. Aynı temizliği
    // etkinlik filtrelerinde de yapmıştım.
    const temiz = Object.fromEntries(
      Object.entries(filters).filter(([, v]) => v !== undefined && v !== '' && v !== null),
    )

    const { data } = await api.get<Paged<UserListItem>>('/admin/users', { params: temiz })
    return data
  },

  /**
   * Hesabı aktif/pasif yapar.
   *
   * Silme değil pasifleştirme: kullanıcının geçmiş rezervasyonları,
   * biletleri ve ödemeleri duruyor. Hesabı silseydik bu kayıtlar
   * sahipsiz kalır ve mali geçmiş bozulurdu.
   */
  setActive: async (id: string, isActive: boolean): Promise<void> => {
    await api.put(`/admin/users/${id}/active`, { isActive })
  },

  setRole: async (id: string, roleName: string, assign: boolean): Promise<void> => {
    await api.put(`/admin/users/${id}/roles`, { roleName, assign })
  },
}

export interface AuditLogListItem {
  id: string
  entityName: string
  entityId: string
  action: string
  oldValues: string | null
  newValues: string | null
  userId: string | null
  userEmail: string | null
  ipAddress: string | null
  correlationId: string | null
  createdAt: string
}

export interface AuditFilters {
  entityName?: string
  action?: string
  userId?: string
  pageNumber?: number
  pageSize?: number
}

export const auditApi = {
  list: async (filters: AuditFilters): Promise<Paged<AuditLogListItem>> => {
    const temiz = Object.fromEntries(
      Object.entries(filters).filter(([, v]) => v !== undefined && v !== '' && v !== null),
    )

    const { data } = await api.get<Paged<AuditLogListItem>>('/admin/audit-logs', { params: temiz })
    return data
  },
}

/**
 * İşlem kodunu Türkçe etikete çevirir.
 *
 * Backend işlemi kod olarak dönüyor ("UserDeactivated"), metin olarak
 * değil. Bu doğru: kod sabit ve sorgulanabilir; metin ise arayüzün
 * işi ve dil değişirse yalnızca burası değişir.
 *
 * Bilinmeyen kod gelirse kodun kendisini gösteriyorum. Boş bırakmak
 * ya da "Bilinmiyor" yazmak, ileride yeni bir işlem eklenip burası
 * güncellenmediğinde kaydı okunamaz hale getirirdi.
 */
const ISLEM_ETIKETLERI: Record<string, string> = {
  UserActivated: 'Hesap aktifleştirildi',
  UserDeactivated: 'Hesap pasifleştirildi',
  RoleAssigned: 'Rol verildi',
  RoleRemoved: 'Rol kaldırıldı',
  EventPublished: 'Etkinlik yayına alındı',
  EventSuspended: 'Etkinlik askıya alındı',
  EventReinstated: 'Etkinlik yayına geri alındı',
  OrganizerApplicationApproved: 'Organizatör başvurusu onaylandı',
  OrganizerApplicationRejected: 'Organizatör başvurusu reddedildi',
  PriceChanged: 'Bilet fiyatı değişti',
}

export function islemEtiketi(action: string): string {
  return ISLEM_ETIKETLERI[action] ?? action
}

/**
 * JSON değer metnini okunur hâle getirir.
 *
 * AuditLog eski/yeni değerleri JSON olarak saklıyor
 * ({"IsActive":false}). Ham JSON'u ekrana basmak teknik ve çirkin
 * duruyor; burada "IsActive: false" gibi satırlara açıyorum.
 *
 * Bozuk JSON'da ham metni döndürüyorum -- denetim ekranının bir
 * ayrıştırma hatası yüzünden çökmesi, gösterdiği verinin
 * kendisinden daha kötü olurdu.
 */
export function degerleriOku(json: string | null): string[] {
  if (!json) {
    return []
  }

  try {
    const nesne = JSON.parse(json) as Record<string, unknown>

    return Object.entries(nesne).map(([anahtar, deger]) => `${anahtar}: ${String(deger)}`)
  } catch {
    return [json]
  }
}
