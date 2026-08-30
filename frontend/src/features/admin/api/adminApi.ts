import { api } from '../../../lib/api/client'

/**
 * Admin paneli API cagrilari.
 *
 * Backend'in DTO'lariyla birebir eşleyen tipler. Sprint 18'de bunlar
 * Swagger'dan otomatik uretilecek (Orval); su an elle yazıyorum.
 */

export interface City {
  id: string
  name: string
  plateCode: number
}

export interface VenueListItem {
  id: string
  name: string
  cityName: string
  hallCount: number
}

export interface HallSummary {
  id: string
  name: string
  capacity: number
  seatLayoutCount: number
}

export interface VenueDetail {
  id: string
  name: string
  address: string
  cityId: string
  cityName: string
  latitude: number | null
  longitude: number | null
  halls: HallSummary[]
}

export interface SeatLayoutListItem {
  id: string
  name: string
  isActive: boolean
  isInUse: boolean
  sectionCount: number
  seatCount: number
}

export interface SeatDto {
  id: string
  rowLabel: string
  seatNumber: number
  displayLabel: string
  isActive: boolean
  positionX: number | null
  positionY: number | null
}

export interface SectionDetail {
  id: string
  name: string
  displayOrder: number
  colorHex: string | null
  seatCount: number
  seats: SeatDto[]
}

export interface SeatLayoutDetail {
  id: string
  hallId: string
  hallName: string
  hallCapacity: number
  name: string
  description: string | null
  isActive: boolean
  isInUse: boolean
  totalSeatCount: number
  sections: SectionDetail[]
}

/** Backend'in PagedResult<T> karşılığı. */
export interface Paged<T> {
  items: T[]
  pageNumber: number
  pageSize: number
  totalCount: number
  totalPages: number
  hasPreviousPage: boolean
  hasNextPage: boolean
}

export const adminApi = {
  getCities: async (): Promise<City[]> => {
    const { data } = await api.get<City[]>('/cities')
    return data
  },

  getVenues: async (params: {
    search?: string
    cityId?: string
    pageNumber?: number
    // pageSize'i etkinlik olusturma formu icin ekledim: mekan
    // listesi orada acilir liste ve varsayilan 20 kayit, 21. mekani
    // olan organizator kendi mekanini secemezdi.
    pageSize?: number
  }) => {
    const { data } = await api.get<Paged<VenueListItem>>('/venues', { params })
    return data
  },

  getVenue: async (id: string): Promise<VenueDetail> => {
    const { data } = await api.get<VenueDetail>(`/venues/${id}`)
    return data
  },

  createVenue: async (body: {
    name: string
    address: string
    cityId: string
    latitude?: number
    longitude?: number
  }): Promise<string> => {
    const { data } = await api.post<string>('/venues', body)
    return data
  },

  createHall: async (
    venueId: string,
    body: { name: string; capacity: number },
  ): Promise<string> => {
    const { data } = await api.post<string>(`/venues/${venueId}/halls`, body)
    return data
  },

  getSeatLayouts: async (hallId: string): Promise<SeatLayoutListItem[]> => {
    const { data } = await api.get<SeatLayoutListItem[]>(`/halls/${hallId}/seat-layouts`)
    return data
  },

  createSeatLayout: async (
    hallId: string,
    body: { name: string; description?: string },
  ): Promise<string> => {
    const { data } = await api.post<string>(`/halls/${hallId}/seat-layouts`, body)
    return data
  },

  getSeatLayout: async (id: string): Promise<SeatLayoutDetail> => {
    const { data } = await api.get<SeatLayoutDetail>(`/seat-layouts/${id}`)
    return data
  },

  addSection: async (
    layoutId: string,
    body: { name: string; displayOrder: number; colorHex?: string },
  ): Promise<string> => {
    const { data } = await api.post<string>(`/seat-layouts/${layoutId}/sections`, body)
    return data
  },

  generateSeats: async (
    layoutId: string,
    body: { sectionId: string; rowCount: number; seatsPerRow: number; rowLabels?: string[] },
  ): Promise<number> => {
    const { data } = await api.post<number>(`/seat-layouts/${layoutId}/generate-seats`, body)
    return data
  },
}
