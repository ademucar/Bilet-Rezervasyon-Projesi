/**
 * Bildirim tipleri ve DTO'lari.
 *
 * ==================================================================
 * NEDEN AYRI DOSYA?
 * ==================================================================
 * Önce bunlar NotificationBell.tsx icindeydi. Lint uyardi:
 *
 *   react(only-export-components): Fast refresh only works when a
 *   file only exports components.
 *
 * Kural haklı: Vite'in hizli yenileme (HMR) mekanizmasi, bir dosya
 * hem bileşen hem başka sey disa aktardiginda o dosyayı guvenle
 * degistiremiyor ve TAM SAYFA yenilemesi yapiyor.
 *
 * Gelistirme sırasında fark edilir bir yavaslama -- ve cozumu bir
 * dosya ayirmak kadar basit.
 * ==================================================================
 */

/** Backend'deki NotificationType ile birebir aynı. */
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
