import { api } from '../../../lib/api/client'
import type { EventListItem, EventDetail } from '../../booking/api/bookingApi'
import type { Paged } from '../../admin/api/adminApi'

// Organizatorun etkinlik yonetimi -- PDF Sprint 5.
//
// Bu dosyayi bookingApi'den AYIRDIM. Ikisi de /events altinda calisiyor
// ama farkli isler: bookingApi bilet ALAN kullanicinin gordugu okuma
// uclari, burasi etkinligi YONETEN organizatorun yazma uclari.
//
// Tek dosyada toplasaydim, bilet alma ekranlarini acan her kullanici
// paketin icinde etkinlik silme kodunu da indirirdi.

/** Etkinlik durumlari -- backend'deki EventStatus ile birebir. */
export const EventStatus = {
  Draft: 1,
  PendingApproval: 2,
  Published: 3,
  SalesOpen: 4,
  SalesClosed: 5,
  Completed: 6,
  Cancelled: 7,
  Suspended: 8,
} as const

export type EventStatusValue = (typeof EventStatus)[keyof typeof EventStatus]

/**
 * Durum -> ekranda gosterilecek ad ve renk.
 *
 * Sekiz durumu tek yerde topluyorum. Liste, detay ve durum rozeti
 * ayni kaynaktan okusun; birinde "Taslak" digerinde "Draft" yazmasin.
 */
export const EVENT_STATUS_LABELS: Record<number, { text: string; tone: string }> = {
  [EventStatus.Draft]: { text: 'Taslak', tone: 'border-slate-300 bg-slate-50 text-slate-600' },
  [EventStatus.PendingApproval]: {
    text: 'Onay bekliyor',
    tone: 'border-amber-300 bg-amber-50 text-amber-700',
  },
  [EventStatus.Published]: {
    text: 'Yayında',
    tone: 'border-sky-300 bg-sky-50 text-sky-700',
  },
  [EventStatus.SalesOpen]: {
    text: 'Satışta',
    tone: 'border-emerald-300 bg-emerald-50 text-emerald-700',
  },
  [EventStatus.SalesClosed]: {
    text: 'Satış kapandı',
    tone: 'border-slate-300 bg-slate-50 text-slate-600',
  },
  [EventStatus.Completed]: {
    text: 'Tamamlandı',
    tone: 'border-slate-300 bg-slate-50 text-slate-500',
  },
  [EventStatus.Cancelled]: { text: 'İptal', tone: 'border-red-300 bg-red-50 text-red-700' },
  [EventStatus.Suspended]: {
    text: 'Askıya alındı',
    tone: 'border-red-300 bg-red-50 text-red-700',
  },
}

export interface CreateEventBody {
  title: string
  description: string
  categoryId: string
  cityId: string
  venueId: string
  hallId: string
  eventDate: string
  salesStartDate: string
  salesEndDate: string
  durationMinutes: number
  maxTicketsPerUser: number
  minimumAge: number
}

export interface UpdateEventBody {
  title: string
  description: string
  minimumAge?: number | null
  eventDate?: string | null
  salesStartDate?: string | null
  salesEndDate?: string | null
}

export interface AddSessionBody {
  startDate: string
  endDate: string
  hallId: string
  seatLayoutId: string
}

export const organizerApi = {
  /**
   * Organizatorun kendi etkinlikleri -- TASLAKLAR DAHIL.
   *
   * Genel /events ucu yalnizca yayindakileri donuyor; kendi
   * taslagini goremeyen organizator onu duzenleyemezdi de.
   * Bunun icin backend'e /events/mine ucunu ekledim: organizator
   * kimligini sunucu kendisi cozuyor, istemciden almiyor.
   */
  getMyEvents: async (params?: {
    status?: number
    pageNumber?: number
    pageSize?: number
  }): Promise<Paged<EventListItem>> => {
    const { data } = await api.get<Paged<EventListItem>>('/events/mine', { params })
    return data
  },

  getEvent: async (id: string): Promise<EventDetail> => {
    const { data } = await api.get<EventDetail>(`/events/${id}`)
    return data
  },

  createEvent: async (body: CreateEventBody): Promise<string> => {
    const { data } = await api.post<string>('/events', body)
    return data
  },

  updateEvent: async (id: string, body: UpdateEventBody): Promise<void> => {
    await api.put(`/events/${id}`, body)
  },

  deleteEvent: async (id: string): Promise<void> => {
    await api.delete(`/events/${id}`)
  },

  addSession: async (eventId: string, body: AddSessionBody): Promise<string> => {
    const { data } = await api.post<string>(`/events/${eventId}/sessions`, body)
    return data
  },

  /** Taslagi admin onayina gonderir. */
  submitForApproval: async (id: string): Promise<void> => {
    await api.post(`/events/${id}/submit`)
  },

  /**
   * Etkinligi yayina alir -- YALNIZCA ADMIN.
   *
   * Backend'de POST /events/{id}/publish AdminOnly politikasinda.
   * Organizatorun kendi etkinligini onaylamasi, onay surecini
   * anlamsiz kilardi.
   */
  publishEvent: async (id: string): Promise<void> => {
    await api.post(`/events/${id}/publish`)
  },

  /**
   * Etkinligi iptal eder.
   *
   * Sebep zorunlu degil ama arayuzde istiyorum: iptal, bilet almis
   * kullanicilara bildirim olarak gidiyor ve "iptal edildi" tek
   * basina kullaniciyi kizdiriyor. Sebep yazilinca bildirim
   * anlamli oluyor.
   */
  cancelEvent: async (id: string, reason: string): Promise<void> => {
    await api.post(`/events/${id}/cancel`, { reason })
  },

  /**
   * Uygunsuz etkinligi askiya alir -- YALNIZCA ADMIN.
   *
   * Iptalden farki: Cancelled bir son durum, geri donusu yok ve para
   * iadesi zincirini baslatiyor. Suspended geri alinabilir ve hicbir
   * zincir tetiklemiyor. Admin "bu afis uygunsuz" dedigi zaman
   * istedigi sey etkinligi yok etmek degil, satisi durdurup
   * organizatorden duzeltme beklemek.
   *
   * Sebep burada ZORUNLU (iptalde degildi): askiya almayi her zaman
   * bir baskasi yapiyor ve organizator neyi duzeltecegini bilmeli.
   */
  suspendEvent: async (id: string, reason: string): Promise<void> => {
    await api.post(`/events/${id}/suspend`, { reason })
  },

  /** Askidaki etkinligi yayina geri alir -- yalnizca admin. */
  reinstateEvent: async (id: string): Promise<void> => {
    await api.post(`/events/${id}/reinstate`)
  },
}

export interface TicketTypeDto {
  id: string
  name: string
  price: number
  currency: string
  priceDisplay: string
  quota: number | null
  isActive: boolean
  requiresStudentVerification: boolean
  salesStartDate: string | null
  salesEndDate: string | null
  assignedSectionIds: string[]
}

export interface CreateTicketTypeBody {
  name: string
  price: number
  currency: string
  quota?: number | null
  requiresStudentVerification: boolean
  salesStartDate?: string | null
  salesEndDate?: string | null
}

export interface UploadedFile {
  id: string
  fileName: string
  contentType: string
  sizeInBytes: number
  downloadUrl: string
}

export const ticketTypeApi = {
  list: async (eventId: string): Promise<TicketTypeDto[]> => {
    const { data } = await api.get<TicketTypeDto[]>(`/events/${eventId}/ticket-types`)
    return data
  },

  create: async (eventId: string, body: CreateTicketTypeBody): Promise<string> => {
    const { data } = await api.post<string>(`/events/${eventId}/ticket-types`, body)
    return data
  },

  remove: async (id: string): Promise<void> => {
    await api.delete(`/ticket-types/${id}`)
  },
}

export const fileApi = {
  /**
   * Dosya yukler ve indirme adresini doner.
   *
   * Content-Type'i ELLE AYARLAMIYORUM. axios'a FormData verince
   * sinirlayiciyi (boundary) kendisi uretip basliga koyuyor.
   * "multipart/form-data" yazsaydim sinirlayici kaybolur ve sunucu
   * govdeyi ayristiramazdi -- sessizce 400 donerdi.
   */
  upload: async (file: File): Promise<UploadedFile> => {
    const govde = new FormData()
    govde.append('file', file)
    const { data } = await api.post<UploadedFile>('/files', govde)
    return data
  },
}

export const posterApi = {
  set: async (eventId: string, posterPath: string | null): Promise<void> => {
    await api.put(`/events/${eventId}/poster`, { posterPath })
  },
}
