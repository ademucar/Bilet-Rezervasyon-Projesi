import { api } from '../../../lib/api/client'

/**
 * Raporlama API katmani. PDF Sprint 13.
 */

export interface DailySalesPoint {
  date: string
  ticketCount: number
  revenue: number
}

export interface NamedCount {
  name: string
  count: number
}

export interface EventRevenue {
  eventId: string
  title: string
  ticketCount: number
  revenue: number
}

export interface SectionOccupancy {
  sectionName: string
  totalSeats: number
  soldSeats: number
  occupancyRate: number
}

/** PDF Sprint 13'un saydığı 10 organizatör metriği. */
export interface OrganizerDashboard {
  totalEvents: number
  publishedEvents: number
  totalTicketsSold: number
  totalRevenue: number
  refundedTickets: number
  occupancyRate: number
  topTicketTypeName: string | null
  topTicketTypeCount: number
  dailySales: DailySalesPoint[]
  revenueByEvent: EventRevenue[]
  sectionOccupancies: SectionOccupancy[]
  currency: string
}

/** PDF Sprint 13'un saydığı 10 admin metriği. */
export interface AdminDashboard {
  totalUsers: number
  totalOrganizers: number
  totalEvents: number
  activeSales: number
  totalTransactionVolume: number
  cancelledEvents: number
  failedPaymentRate: number
  topCities: NamedCount[]
  topCategories: NamedCount[]
  systemErrorCount: number
  currency: string
}

export interface SalesSummaryReport {
  ticketCount: number
  grossRevenue: number
  refundedAmount: number
  netRevenue: number
  refundedTicketCount: number
  reservationCount: number
  expiredReservationCount: number
  currency: string
}

export interface EventOccupancyRow {
  eventId: string
  title: string
  eventDate: string
  totalSeats: number
  soldSeats: number
  lockedSeats: number
  availableSeats: number
  occupancyRate: number
}

export interface TicketTypeSalesRow {
  ticketTypeName: string
  soldCount: number
  refundedCount: number
  revenue: number
  averagePrice: number
}

export interface PaymentStatusRow {
  status: number
  statusName: string
  count: number
  totalAmount: number
  percentage: number
}

/**
 * Rapor türleri. Backend'deki ReportType enum'u ile BIREBIR.
 *
 * Sayilar backend'de acikca 1'den başlıyor (asla 0 değil) -- Sprint 2'de
 * benimsedigimiz kural. Burada da aynı değerleri yazıyorum.
 */
export const ReportType = {
  SalesSummary: 1,
  EventOccupancy: 2,
  RevenueByEvent: 3,
  TicketTypeSales: 4,
  PaymentStatuses: 5,
} as const

/** PDF Sprint 13: Excel, CSV, PDF. */
export const ReportFormat = {
  Csv: 1,
  Excel: 2,
  Pdf: 3,
} as const

export const reportsApi = {
  getOrganizerDashboard: async (days = 30): Promise<OrganizerDashboard> => {
    const { data } = await api.get<OrganizerDashboard>('/dashboard/organizer', {
      params: { days },
    })
    return data
  },

  getAdminDashboard: async (): Promise<AdminDashboard> => {
    const { data } = await api.get<AdminDashboard>('/dashboard/admin')
    return data
  },

  getSalesSummary: async (): Promise<SalesSummaryReport> => {
    const { data } = await api.get<SalesSummaryReport>('/reports/sales-summary')
    return data
  },

  getEventOccupancy: async (): Promise<EventOccupancyRow[]> => {
    const { data } = await api.get<EventOccupancyRow[]>('/reports/event-occupancy')
    return data
  },

  getRevenueByEvent: async (): Promise<EventRevenue[]> => {
    const { data } = await api.get<EventRevenue[]>('/reports/revenue-by-event')
    return data
  },

  getTicketTypeSales: async (): Promise<TicketTypeSalesRow[]> => {
    const { data } = await api.get<TicketTypeSalesRow[]>('/reports/ticket-type-sales')
    return data
  },

  getPaymentStatuses: async (): Promise<PaymentStatusRow[]> => {
    const { data } = await api.get<PaymentStatusRow[]>('/reports/payment-statuses')
    return data
  },

  /**
   * Rapor disa aktarimi TALEP EDER.
   *
   * BU CAGRI DOSYAYI DONDURMEZ
   *
   * PDF: "Rapor üretimi background job olarak calistirilmali ve
   * tamamlandiginda kullanıcıya bildirim gonderilmelidir."
   *
   * Sunucu 202 Accepted ve bir exportId dönüyor. Dosya hazır
   * olunca kullanıcıya bildirim gidiyor.
   *
   * Arayuzde bunu ACIKCA söylemek zorundayız: kullanıcı "indir"
   * dedikten sonra dosya inmezse ve hiçbir açıklama gormezse
   * dugmenin bozuk olduğunu dusunur.
   *
   */
  requestExport: async (type: number, format: number): Promise<string> => {
    const { data } = await api.post<{ exportId: string }>('/reports/export', { type, format })
    return data.exportId
  },
}
