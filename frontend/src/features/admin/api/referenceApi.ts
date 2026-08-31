import { api } from '../../../lib/api/client'
import type { CategoryDto, CityDto } from '../../booking/api/bookingApi'

// Kategori ve sehir yonetimi -- PDF sayfa 5:
// "Admin: Kategori, sehir ve salon yonetimi."
//
// Salon tarafi Sprint 4'te yapilmisti (/admin/mekanlar); kategori ve
// sehirde yalnizca OKUMA ucu vardi. Yani listeler veritabanina elle
// girilen tohum veriyle doluydu ve arayuzden yeni kategori eklemenin
// hicbir yolu yoktu.

export interface SaveCategoryBody {
  name: string
  slug: string
  iconName: string | null
  displayOrder: number
}

export const categoryApi = {
  list: async (): Promise<CategoryDto[]> => {
    const { data } = await api.get<CategoryDto[]>('/events/categories')
    return data
  },

  create: async (body: SaveCategoryBody): Promise<string> => {
    const { data } = await api.post<string>('/events/categories', body)
    return data
  },

  update: async (id: string, body: SaveCategoryBody): Promise<void> => {
    await api.put(`/events/categories/${id}`, body)
  },

  remove: async (id: string): Promise<void> => {
    await api.delete(`/events/categories/${id}`)
  },
}

export const cityApi = {
  list: async (): Promise<CityDto[]> => {
    const { data } = await api.get<CityDto[]>('/cities')
    return data
  },

  create: async (name: string, plateCode: number): Promise<string> => {
    const { data } = await api.post<string>('/cities', { name, plateCode })
    return data
  },

  /**
   * Yalnızca ad değişiyor.
   *
   * Plaka kodu backend'de bilerek değiştirilemez: plaka şehrin
   * kimliği gibi, 34 her zaman İstanbul. Yanlış girilmişse doğru
   * işlem düzeltmek değil, kaydı silip yeniden oluşturmak.
   */
  rename: async (id: string, name: string): Promise<void> => {
    await api.put(`/cities/${id}`, { name })
  },

  remove: async (id: string): Promise<void> => {
    await api.delete(`/cities/${id}`)
  },
}

/**
 * Ada bakarak slug önerir: "Rock Konseri" -> "rock-konseri".
 *
 * Backend slug'ı kendisi üretmiyor, istemciden alıyor -- çünkü admin
 * öneriyi beğenmezse değiştirebilmeli. Ama her seferinde elle
 * yazdırmak da gereksiz sürtünme; öneriyi burada üretip alana
 * dolduruyorum.
 *
 * Türkçe karakterleri elle eşliyorum. normalize('NFD') ile aksan
 * ayırma yöntemi ş ve ğ için çalışmıyor: onlar aksanlı harf değil,
 * Latin alfabesinde ayrı harfler. Denedim, "şarkı" -> "sarki" değil
 * "şarki" veriyordu.
 */
const TURKCE_ESLEME: Record<string, string> = {
  ç: 'c',
  ğ: 'g',
  ı: 'i',
  i: 'i',
  ö: 'o',
  ş: 's',
  ü: 'u',
}

export function slugOner(ad: string): string {
  return (
    ad
      // toLocaleLowerCase('tr') şart: normal toLowerCase, 'I' harfini
      // 'i' yapıyor ama Türkçede 'I'nın küçüğü 'ı'. "IŞIK" kelimesi
      // "isik" yerine "işik" olurdu.
      .toLocaleLowerCase('tr')
      .split('')
      .map((h) => TURKCE_ESLEME[h] ?? h)
      .join('')
      // Harf ve rakam dışındaki her şey tire olur.
      .replace(/[^a-z0-9]+/g, '-')
      // Baştaki/sondaki tireleri at: backend "-rock-" biçimini
      // reddediyor.
      .replace(/^-+|-+$/g, '')
  )
}
