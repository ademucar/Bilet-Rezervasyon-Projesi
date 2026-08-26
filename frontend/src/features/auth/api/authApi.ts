import { api } from '../../../lib/api/client'
import type { AuthResponse, UserSummary } from '../../../types/auth'

/**
 * Auth endpointlerinin tek noktadan cagrildigi katman.
 *
 * PDF Sprint 18: "API istekleri component icinde dagink sekilde
 * yazilmamalidir."
 *
 * Bilesenler axios'u hic gormuyor; yalnizca bu fonksiyonlari cagiriyor.
 * Yarin bir endpoint'in yolu degisirse tek dosyada duzeltiyoruz.
 */

export interface RegisterRequest {
  email: string
  password: string
  firstName: string
  lastName: string
  phoneNumber?: string
}

export interface LoginRequest {
  email: string
  password: string
}

export const authApi = {
  register: async (body: RegisterRequest): Promise<AuthResponse> => {
    const { data } = await api.post<AuthResponse>('/auth/register', body)
    return data
  },

  login: async (body: LoginRequest): Promise<AuthResponse> => {
    const { data } = await api.post<AuthResponse>('/auth/login', body)
    return data
  },

  me: async (): Promise<UserSummary> => {
    const { data } = await api.get<UserSummary>('/auth/me')
    return data
  },

  logout: async (refreshToken: string | null): Promise<void> => {
    await api.post('/auth/logout', { refreshToken })
  },

  forgotPassword: async (email: string): Promise<void> => {
    await api.post('/auth/forgot-password', { email })
  },

  resetPassword: async (token: string, newPassword: string): Promise<void> => {
    await api.post('/auth/reset-password', { token, newPassword })
  },

  changePassword: async (currentPassword: string, newPassword: string): Promise<void> => {
    await api.post('/auth/change-password', { currentPassword, newPassword })
  },
}
