import { api } from '../../../lib/api/client'
import type { Paged } from '../../admin/api/adminApi'

/**
 * ==================================================================
 * BILET ALMA API KATMANI -- PDF Sprint 7 ve 8
 * ==================================================================
 * Backend DTO'lariyla birebir esleyen tipler.
 *
 * Sayilarin (enum) TypeScript karsiliklarini `as const` nesnesiyle
 * yaziyorum, TypeScript `enum` anahtar kelimesiyle degil.
 *
 * Neden? TypeScript enum'u derlendiginde ORTADA bir JavaScript
 * nesnesi birakir; `as const` ise tamamen silinir ve pakete tek
 * bayt eklemez. Ayrica `enum` degerleri yapisal olarak degil
 * NOMINAL karsilastirilir; backend'den gelen ham sayi (3) bir
 * TS enum'una dogrudan atanamaz, cast gerekir. `as const` ile
 * bu sorun hic dogmuyor.
 * ==================================================================
 */

export const EventSeatStatus = {
  Available: 1,
  Locked: 2,
  Sold: 3,
  Blocked: 4,
} as const

export const ReservationStatus = {
  Pending: 1,
  Locked: 2,
  PaymentPending: 3,
  Confirmed: 4,
  Expired: 5,
  Cancelled: 6,
  Refunded: 7,
} as const

export const PaymentStatus = {
  Pending: 1,
  Processing: 2,
  Successful: 3,
  Failed: 4,
  Cancelled: 5,
  Refunded: 6,
} as const

export const TicketStatus = {
  Active: 1,
  Used: 2,
  Cancelled: 3,
  Refunded: 4,
  Expired: 5,
} as const

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

// ===================================================================
// ETKINLIK
// ===================================================================

export interface EventListItem {
  id: string
  title: string
  categoryName: string
  cityName: string
  venueName: string
  posterImagePath: string | null
  eventDate: string
  status: number
  minimumAge: number
  sessionCount: number
}

export interface EventSessionDto {
  id: string
  startDate: string
  endDate: string
  hallId: string
  hallName: string
  seatLayoutId: string
  seatLayoutName: string
  status: number
  areSeatsGenerated: boolean
}

export interface EventDetail {
  id: string
  title: string
  description: string
  categoryName: string
  organizerName: string
  cityName: string
  venueName: string
  venueAddress: string
  hallName: string
  posterImagePath: string | null
  minimumAge: number
  durationMinutes: number
  maxTicketsPerUser: number
  salesStartDate: string
  salesEndDate: string
  eventDate: string
  status: number
  cancellationReason: string | null
  sessions: EventSessionDto[]
}

// ===================================================================
// KOLTUK UYGUNLUGU
// ===================================================================

export interface SeatAvailabilityItem {
  eventSeatId: string
  seatId: string
  rowLabel: string
  seatNumber: number
  displayLabel: string
  sectionId: string
  sectionName: string
  sectionColor: string | null
  ticketTypeId: string
  ticketTypeName: string
  price: number
  currency: string
  status: number
}

export interface SeatAvailability {
  sessionId: string
  startDate: string
  totalSeats: number
  availableSeats: number
  seats: SeatAvailabilityItem[]
}

// ===================================================================
// REZERVASYON
// ===================================================================

export interface ReservationItemDto {
  id: string
  eventSeatId: string
  seatLabel: string
  sectionName: string
  ticketTypeName: string
  unitPrice: number
  currency: string
}

export interface ReservationDto {
  id: string
  reservationCode: string
  status: number
  eventSessionId: string
  eventTitle: string
  sessionStartDate: string
  venueName: string
  totalAmount: number
  currency: string
  expiresAt: string
  /** Sunucunun hesapladigi kalan sure. Geri sayim bundan baslar. */
  remainingSeconds: number
  extensionCount: number
  items: ReservationItemDto[]
}

// ===================================================================
// ODEME VE BILET
// ===================================================================

export interface PaymentTransactionDto {
  type: number
  status: number
  message: string | null
  createdAt: string
}

export interface PaymentDto {
  id: string
  reservationId: string
  reservationCode: string
  status: number
  providerName: string
  providerReference: string | null
  amount: number
  refundedAmount: number
  currency: string
  failureReason: string | null
  completedAt: string | null
  transactions: PaymentTransactionDto[]
}

export interface TicketDto {
  id: string
  ticketNumber: string
  status: number
  eventTitle: string
  sessionStartDate: string
  venueName: string
  seatLabel: string
  sectionName: string
  ticketTypeName: string
  price: number
  currency: string
  qrValue: string | null
  usedAt: string | null
}

/**
 * ------------------------------------------------------------------
 * IDEMPOTENCY ANAHTARI
 * ------------------------------------------------------------------
 * Backend hem rezervasyon hem odeme olustururken "Idempotency-Key"
 * header'ini kabul ediyor: ayni anahtarla gelen ikinci istek YENI
 * kayit olusturmuyor, ilkini donduruyor.
 *
 * Anahtari ISTEMCI uretmek ZORUNDA. Sunucu uretseydi hicbir ise
 * yaramazdi: ag kopmasi yuzunden tekrarlanan istek sunucuya
 * ulastiginda "yeni istek" gorunurdu.
 *
 * crypto.randomUUID() tarayicida yerlesik ve kriptografik olarak
 * guclu. Kutuphane eklemeye gerek yok.
 * ------------------------------------------------------------------
 */
export function newIdempotencyKey(): string {
  return crypto.randomUUID()
}

/**
 * Etkinlik listeleme filtreleri. PDF Sprint 11.
 *
 * Backend'deki GetEventsQuery ile birebir esleyen alanlar. Alan adi
 * uyusmazsa filtre SESSIZCE calismaz -- ASP.NET taninmayan sorgu
 * parametresini yok sayar, hata dondurmez. Bu yuzden adlari
 * kopyalayarak aliyorum.
 */
export interface EventFilters {
  search?: string
  cityId?: string
  categoryId?: string
  venueId?: string
  organizerId?: string
  dateFrom?: string
  dateTo?: string
  minPrice?: number
  maxPrice?: number
  maxMinimumAge?: number
  status?: number
  sortBy?: 'date' | 'title' | 'created'
  sortDirection?: 'asc' | 'desc'
  pageNumber?: number
  pageSize?: number
}

export interface CategoryDto {
  id: string
  name: string
  slug: string
  iconName: string | null
}

export interface CityDto {
  id: string
  name: string
  plateCode: number
}

// ===================================================================
// YORUM VE FAVORI -- PDF Sprint 12
// ===================================================================

export interface ReviewDto {
  id: string
  userId: string
  /** "Adem U." -- backend soyadi KISALTARAK donuyor (gizlilik). */
  userDisplayName: string
  rating: number
  comment: string
  createdAt: string
  updatedAt: string | null
  /** Bu yorum bana mi ait? Duzenle/Sil dugmeleri icin. */
  isMine: boolean
}

export interface ReviewSummary {
  averageRating: number
  totalCount: number
  /** Puan -> adet. Backend 1-5 arasi TUM anahtarlari dolduruyor. */
  ratingCounts: Record<string, number>
}

export interface EventReviewsResult {
  summary: ReviewSummary
  reviews: Paged<ReviewDto>
}

export const bookingApi = {
  getEvents: async (params: EventFilters) => {
    // ==============================================================
    // BOS ALANLARI TEMIZLE
    // ==============================================================
    // Axios, undefined degerleri zaten atliyor ama BOS METIN ('')
    // gonderiyor: ?cityId=&categoryId=
    //
    // Backend tarafinda Guid? alanina bos metin baglanmaya calisilinca
    // model binding hatasi olusur ve istek 400 doner. Yani kullanici
    // filtreyi "Tumu"ne cevirdiginde liste tamamen bozulurdu.
    //
    // Temizligi TEK YERDE yapiyorum ki her cagirim yerinde
    // tekrarlanmasin.
    // ==============================================================
    const temiz = Object.fromEntries(
      Object.entries(params).filter(
        ([, deger]) => deger !== undefined && deger !== '' && deger !== null,
      ),
    )

    const { data } = await api.get<Paged<EventListItem>>('/events', { params: temiz })
    return data
  },

  /** PDF Sprint 11: populer etkinlikler (Redis'te 10 dakika). */
  getPopularEvents: async (count = 8): Promise<EventListItem[]> => {
    const { data } = await api.get<EventListItem[]>('/events/popular', { params: { count } })
    return data
  },

  getCategories: async (): Promise<CategoryDto[]> => {
    const { data } = await api.get<CategoryDto[]>('/events/categories')
    return data
  },

  getCities: async (): Promise<CityDto[]> => {
    const { data } = await api.get<CityDto[]>('/cities')
    return data
  },

  getEvent: async (id: string): Promise<EventDetail> => {
    const { data } = await api.get<EventDetail>(`/events/${id}`)
    return data
  },

  getSeatAvailability: async (sessionId: string): Promise<SeatAvailability> => {
    const { data } = await api.get<SeatAvailability>(
      `/event-sessions/${sessionId}/seat-availability`,
    )
    return data
  },

  createReservation: async (
    body: { eventSessionId: string; eventSeatIds: string[] },
    idempotencyKey: string,
  ): Promise<ReservationDto> => {
    const { data } = await api.post<ReservationDto>('/reservations', body, {
      headers: { 'Idempotency-Key': idempotencyKey },
    })
    return data
  },

  getReservation: async (id: string): Promise<ReservationDto> => {
    const { data } = await api.get<ReservationDto>(`/reservations/${id}`)
    return data
  },

  cancelReservation: async (id: string, reason?: string): Promise<void> => {
    await api.post(`/reservations/${id}/cancel`, { reason: reason ?? null })
  },

  extendReservation: async (id: string): Promise<ReservationDto> => {
    const { data } = await api.post<ReservationDto>(`/reservations/${id}/extend`)
    return data
  },

  getMyReservations: async (status?: number): Promise<ReservationDto[]> => {
    const { data } = await api.get<ReservationDto[]>('/users/me/reservations', {
      params: status ? { status } : undefined,
    })
    return data
  },

  createPayment: async (reservationId: string, idempotencyKey: string): Promise<PaymentDto> => {
    // TUTAR GONDERMIYORUZ.
    //
    // PDF Sprint 6: "Frontend tarafindan gonderilen toplam tutara
    // guvenilmemelidir." Backend tutari rezervasyondan okuyor.
    // Buraya bir `amount` alani eklemek, kullanicinin tarayici
    // konsolundan 1000 TL'lik bileti 1 TL'ye almasina kapi acardi.
    const { data } = await api.post<PaymentDto>(
      '/payments',
      { reservationId },
      { headers: { 'Idempotency-Key': idempotencyKey } },
    )
    return data
  },

  getPayment: async (id: string): Promise<PaymentDto> => {
    const { data } = await api.get<PaymentDto>(`/payments/${id}`)
    return data
  },

  /**
   * Odemeyi tamamlar.
   *
   * GOVDE BOS GONDERILIYOR -- bilincli bir karar.
   *
   * Backend, govdede referans gelmezse KENDI kaydettigi referansi
   * kullaniyor (CompletePaymentCommand: `request.ProviderReference
   * ?? payment.ProviderReference`).
   *
   * Referansi buradan gondermek sacma olurdu: zaten sunucu uretip
   * sunucu sakladi. Istemciye gonderip geri almak, dogrulanmasi
   * gereken fazladan bir yuzey acmak demek.
   */
  completePayment: async (id: string): Promise<PaymentDto> => {
    const { data } = await api.post<PaymentDto>(`/payments/${id}/complete`, {})
    return data
  },

  failPayment: async (id: string, reason: string): Promise<void> => {
    await api.post(`/payments/${id}/fail`, { reason })
  },

  // ---- Yorumlar (PDF Sprint 12) ----

  getEventReviews: async (eventId: string, pageNumber = 1): Promise<EventReviewsResult> => {
    const { data } = await api.get<EventReviewsResult>(`/events/${eventId}/reviews`, {
      params: { pageNumber, pageSize: 10 },
    })
    return data
  },

  createReview: async (eventId: string, body: { rating: number; comment: string }) => {
    const { data } = await api.post<string>(`/events/${eventId}/reviews`, body)
    return data
  },

  updateReview: async (id: string, body: { rating: number; comment: string }): Promise<void> => {
    await api.put(`/reviews/${id}`, body)
  },

  deleteReview: async (id: string): Promise<void> => {
    await api.delete(`/reviews/${id}`)
  },

  // ---- Favoriler (PDF Sprint 12) ----

  addFavorite: async (eventId: string): Promise<void> => {
    await api.post(`/events/${eventId}/favorite`)
  },

  removeFavorite: async (eventId: string): Promise<void> => {
    await api.delete(`/events/${eventId}/favorite`)
  },

  getMyFavorites: async (): Promise<EventListItem[]> => {
    const { data } = await api.get<EventListItem[]>('/users/me/favorites')
    return data
  },

  getMyTickets: async (status?: number): Promise<TicketDto[]> => {
    const { data } = await api.get<TicketDto[]>('/users/me/tickets', {
      params: status ? { status } : undefined,
    })
    return data
  },
}
