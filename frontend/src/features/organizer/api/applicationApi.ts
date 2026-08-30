import { api } from '../../../lib/api/client'

// Organizator basvurulari -- PDF sayfa 5:
//   Kullanici tarafi: organizator olmak icin basvurur
//   Admin tarafi:     "Organizator basvurularini onaylayabilir"
//
// Backend'de uc uc de Sprint 5'ten beri hazirdi ama arayuzu yoktu:
// basvuruyu onaylamanin tek yolu Scalar'dan elle istek atmakti.
// Nitekim bu projeyi denerken kendi organizator profilimi SQL ile
// olusturmustum -- arayuzden yapilamadigi icin.

export const ApplicationStatus = {
  Pending: 1,
  Approved: 2,
  Rejected: 3,
} as const

export const APPLICATION_STATUS_LABELS: Record<number, { text: string; tone: string }> = {
  [ApplicationStatus.Pending]: {
    text: 'Bekliyor',
    tone: 'border-amber-300 bg-amber-50 text-amber-700',
  },
  [ApplicationStatus.Approved]: {
    text: 'Onaylandı',
    tone: 'border-emerald-300 bg-emerald-50 text-emerald-700',
  },
  [ApplicationStatus.Rejected]: {
    text: 'Reddedildi',
    tone: 'border-red-300 bg-red-50 text-red-700',
  },
}

export interface OrganizerApplicationDto {
  id: string
  userId: string
  userEmail: string
  companyName: string
  contactEmail: string
  taxNumber: string | null
  description: string | null
  status: number
  rejectionReason: string | null
  createdAt: string
}

export interface ApplyBody {
  companyName: string
  contactEmail: string
  taxNumber?: string | null
  contactPhone?: string | null
  description?: string | null
}

export const applicationApi = {
  /** Kullanici basvurusu. Giris yapmis herkes cagirabilir. */
  apply: async (body: ApplyBody): Promise<string> => {
    const { data } = await api.post<string>('/organizer-applications', body)
    return data
  },

  /** Basvuru listesi -- YALNIZCA ADMIN. status bos ise hepsi. */
  list: async (status?: number): Promise<OrganizerApplicationDto[]> => {
    const { data } = await api.get<OrganizerApplicationDto[]>('/organizer-applications', {
      params: { status },
    })
    return data
  },

  /**
   * Basvuruyu onaylar: organizator profili olusturulur ve rol atanir.
   *
   * Kullanicinin MEVCUT access token'inda hala eski roller var;
   * yeni rolu gorebilmesi icin token'in yenilenmesi gerekiyor
   * (en gec 15 dakika). Bunu OrganizerCommands.cs'te de not
   * dusmustum.
   */
  approve: async (id: string): Promise<void> => {
    await api.post(`/organizer-applications/${id}/approve`)
  },

  reject: async (id: string, reason: string): Promise<void> => {
    await api.post(`/organizer-applications/${id}/reject`, { reason })
  },
}
