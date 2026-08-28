/**
 * Bildirim tipleri ve DTO'lari.
 *
 * ==================================================================
 * NEDEN AYRI DOSYA?
 * ==================================================================
 * Once bunlar NotificationBell.tsx icindeydi. Lint uyardi:
 *
 *   react(only-export-components): Fast refresh only works when a
 *   file only exports components.
 *
 * Kural hakli: Vite'in hizli yenileme (HMR) mekanizmasi, bir dosya
 * hem bilesen hem baska sey disa aktardiginda o dosyayi guvenle
 * degistiremiyor ve TAM SAYFA yenilemesi yapiyor.
 *
 * Gelistirme sirasinda fark edilir bir yavaslama -- ve cozumu bir
 * dosya ayirmak kadar basit.
 * ==================================================================
 */

/** Backend'deki NotificationType ile birebir ayni. */
export const NotificationType = {
  Welcome: 1,
  ReservationCreated: 2,
  ReservationExpiring: 3,
  ReservationExpired: 4,
  PaymentSucceeded: 5,
  PaymentFailed: 6,
  TicketCreated: 7,
  EventReminder: 8,
  EventCancelled: 9,
  RefundCompleted: 10,
  ReportReady: 11,
} as const

export interface NotificationDto {
  id: string
  type: number
  title: string
  message: string
  actionPath: string | null
  relatedEntityId: string | null
  isRead: boolean
  createdAt: string
}

export interface Paged<T> {
  items: T[]
  totalCount: number
}
