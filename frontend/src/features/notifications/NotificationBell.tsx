import { useEffect, useRef, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useNavigate } from 'react-router-dom'
import { api } from '../../lib/api/client'
import { formatDateTime } from '../../lib/format'
import { NotificationType, type NotificationDto, type Paged } from './types'

const notificationsApi = {
  getUnreadCount: async (): Promise<number> => {
    const { data } = await api.get<number>('/notifications/unread-count')
    return data
  },
  getNotifications: async (): Promise<Paged<NotificationDto>> => {
    const { data } = await api.get<Paged<NotificationDto>>('/notifications', {
      params: { pageNumber: 1, pageSize: 15 },
    })
    return data
  },
  markRead: async (id: string): Promise<void> => {
    await api.patch(`/notifications/${id}/read`)
  },
  markAllRead: async (): Promise<number> => {
    const { data } = await api.patch<number>('/notifications/read-all')
    return data
  },
}

/**
 * Bildirim turune göre ikon ve renk.
 *
 * NEDEN RENK + IKON, SADECE RENK DEĞİL?
 *
 * Renk korlugu olan kullanıcı "kırmızı = kötü haber" ayrimini
 * yapamaz. Ikon ikinci bir isaret veriyor.
 *
 * Aynı ilkeyi Sprint 7'de koltuk haritasında da uygulamıştım:
 * durumu yalnızca renkle değil, metinle de anlatmak.
 *
 */
function gorunum(type: number): { ikon: string; renk: string } {
  switch (type) {
    case NotificationType.PaymentSucceeded:
    case NotificationType.TicketCreated:
      return { ikon: '✓', renk: 'bg-emerald-100 text-emerald-700' }

    case NotificationType.PaymentFailed:
    case NotificationType.EventCancelled:
      return { ikon: '!', renk: 'bg-red-100 text-red-700' }

    case NotificationType.ReservationExpiring:
      return { ikon: '⏱', renk: 'bg-amber-100 text-amber-700' }

    case NotificationType.ReservationExpired:
      return { ikon: '×', renk: 'bg-slate-100 text-slate-600' }

    case NotificationType.EventReminder:
      return { ikon: '★', renk: 'bg-brand-100 text-brand-700' }

    case NotificationType.ReportReady:
      return { ikon: '↓', renk: 'bg-brand-100 text-brand-700' }

    default:
      return { ikon: '•', renk: 'bg-slate-100 text-slate-600' }
  }
}

/**
 * BILDIRIM ZILI -- PDF Sprint 14
 *
 * Ust cubukta duruyor; rozet okunmamış sayisini gosteriyor.
 *
 */
export function NotificationBell() {
  const [open, setOpen] = useState(false)
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  const panelRef = useRef<HTMLDivElement>(null)

  // SAYAC: DUZENLI YENILEME
  //
  // Bildirimler arka plan islerinden geliyor (süre uyarısı, rapor
  // hazır, etkinlik hatirlatmasi). Kullanıcı hiçbir sey yapmadan
  // yeni bildirim olusabiliyor.
  //
  // 60 saniye: yeterince taze ama sunucuyu yormuyor. Sayac ucu
  // yalnızca bir COUNT calistiriyor.
  //
  // Sprint 10'da koltuk haritası için SignalR kurmustuk; bildirimler
  // için de kurulabilirdi. Kurmadim: koltuk durumu SANIYELER içinde
  // değişiyor ve gecikme doğrudan 409'a yol aciyordu. Bildirimde
  // bir dakikalik gecikmenin somut bir zarari yok.
  const countQuery = useQuery({
    queryKey: ['notifications', 'unread-count'],
    queryFn: notificationsApi.getUnreadCount,
    refetchInterval: 60_000,
  })

  // Liste YALNIZCA panel acikken çekiliyor.
  //
  // enabled: open -- kapaliyken 15 bildirimin tüm metnini boşuna
  // tasimanin anlami yok. Sayac zaten ayrı ve ucuz bir uctan geliyor.
  const listQuery = useQuery({
    queryKey: ['notifications', 'list'],
    queryFn: notificationsApi.getNotifications,
    enabled: open,
  })

  const markRead = useMutation({
    mutationFn: notificationsApi.markRead,
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['notifications'] })
    },
  })

  const markAllRead = useMutation({
    mutationFn: notificationsApi.markAllRead,
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['notifications'] })
    },
  })

  // DISARI TIKLAYINCA KAPAT
  //
  // Olmasaydı panel açık kalır ve kullanıcı sayfayla etkilesemezdi.
  //
  // Temizlik ŞART: bileşen kaldirildiginda dinleyici kalirsa her
  // tiklamada calismaya devam eder (bellek sizintisi).
  useEffect(() => {
    if (!open) {
      return
    }

    const disariTiklama = (e: MouseEvent) => {
      if (panelRef.current && !panelRef.current.contains(e.target as Node)) {
        setOpen(false)
      }
    }

    // Escape ile de kapansin: klavye kullanicilari için standart.
    const escBasimi = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        setOpen(false)
      }
    }

    document.addEventListener('mousedown', disariTiklama)
    document.addEventListener('keydown', escBasimi)

    return () => {
      document.removeEventListener('mousedown', disariTiklama)
      document.removeEventListener('keydown', escBasimi)
    }
  }, [open])

  const count = countQuery.data ?? 0

  const bildirimeTikla = (n: NotificationDto) => {
    if (!n.isRead) {
      markRead.mutate(n.id)
    }

    if (n.actionPath) {
      // Rapor indirme adresleri API'ye gidiyor (/api/v1/...).
      // Bunlari SPA yonlendirmesiyle acamam -- tarayıcıda
      // doğrudan acmak gerekiyor.
      if (n.actionPath.startsWith('/api/')) {
        window.open(n.actionPath, '_blank', 'noopener,noreferrer')
      } else {
        navigate(n.actionPath)
      }

      setOpen(false)
    }
  }

  return (
    <div className="relative" ref={panelRef}>
      <button
        type="button"
        onClick={() => setOpen((o) => !o)}
        aria-expanded={open}
        aria-label={count > 0 ? `Bildirimler, ${count} okunmamış` : 'Bildirimler'}
        className="relative rounded-lg p-2 text-slate-600 transition-colors hover:bg-slate-100"
      >
        <span aria-hidden="true" className="text-lg leading-none">
          🔔
        </span>

        {count > 0 && (
          <span
            className="absolute -right-0.5 -top-0.5 flex h-4 min-w-4 items-center justify-center rounded-full bg-red-600 px-1 text-[10px] font-bold text-white"
            aria-hidden="true"
          >
            {/* 99'dan fazlası rozete sigmaz ve okunmaz. */}
            {count > 99 ? '99+' : count}
          </span>
        )}
      </button>

      {open && (
        <div className="absolute right-0 z-50 mt-2 w-80 overflow-hidden rounded-[4px] border border-slate-300 bg-white shadow-lg sm:w-96">
          <div className="flex items-center justify-between border-b border-slate-100 px-4 py-3">
            <span className="font-display font-semibold text-slate-900">Bildirimler</span>

            {count > 0 && (
              <button
                type="button"
                onClick={() => markAllRead.mutate()}
                className="text-xs font-medium text-brand-600 hover:underline"
              >
                Tümünü okundu işaretle
              </button>
            )}
          </div>

          <div className="max-h-96 overflow-y-auto">
            {listQuery.isPending && (
              <div className="space-y-2 p-4">
                {[1, 2, 3].map((i) => (
                  <div key={i} className="h-14 animate-pulse rounded-lg bg-slate-100" />
                ))}
              </div>
            )}

            {listQuery.data?.items.length === 0 && (
              <p className="p-6 text-center text-sm text-slate-500">Henüz bildiriminiz yok.</p>
            )}

            <ul className="divide-y divide-slate-100">
              {listQuery.data?.items.map((n) => {
                const g = gorunum(n.type)

                return (
                  <li key={n.id}>
                    <button
                      type="button"
                      onClick={() => bildirimeTikla(n)}
                      className={`flex w-full gap-3 px-4 py-3 text-left transition-colors hover:bg-slate-50 ${
                        n.isRead ? '' : 'bg-brand-50/40'
                      }`}
                    >
                      <span
                        className={`flex h-7 w-7 shrink-0 items-center justify-center rounded-full text-sm font-bold ${g.renk}`}
                        aria-hidden="true"
                      >
                        {g.ikon}
                      </span>

                      <span className="min-w-0 flex-1">
                        <span className="flex items-center gap-2">
                          <span
                            className={`text-sm ${
                              n.isRead ? 'text-slate-700' : 'font-semibold text-slate-900'
                            }`}
                          >
                            {n.title}
                          </span>

                          {/* Okunmamis isareti: kalin yazi TEK BASINA
                              yeterli değil -- ekran okuyucu kalinligi
                              okumaz. Nokta da aria-hidden ama metin
                              alternatifini aşağıda veriyoruz. */}
                          {!n.isRead && (
                            <>
                              <span
                                className="h-1.5 w-1.5 shrink-0 rounded-full bg-brand-600"
                                aria-hidden="true"
                              />
                              <span className="sr-only">(okunmadı)</span>
                            </>
                          )}
                        </span>

                        <span className="mt-0.5 block text-xs leading-snug text-slate-500">
                          {n.message}
                        </span>

                        <span className="mt-1 block text-[11px] text-slate-400">
                          {formatDateTime(n.createdAt)}
                        </span>
                      </span>
                    </button>
                  </li>
                )
              })}
            </ul>
          </div>
        </div>
      )}
    </div>
  )
}
