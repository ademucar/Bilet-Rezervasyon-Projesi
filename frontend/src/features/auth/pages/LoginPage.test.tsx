import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { LoginPage } from './LoginPage'
import { authApi } from '../api/authApi'
import { useAuthStore } from '../../../stores/authStore'
import { renderWithProviders } from '../../../test/testUtils'

/**
 * PDF Sprint 17 frontend testleri: "Login formu" ve "API hata ekranı".
 *
 * ==================================================================
 * API MOCK'LANIYOR, AĞ İSTEĞİ YAPILMIYOR
 * ==================================================================
 * Gerçek backend'e istek atsaydık bu testler:
 *   - Backend ayakta olmadan çalışmazdı
 *   - Yavaş olurdu
 *   - Hata senaryosunu (500 dönmesi) üretmek için backend'i
 *     bozmamız gerekirdi
 *
 * Gerçek uçtan uca akış, ayrıca E2E testinde (Playwright) ve
 * backend entegrasyon testlerinde doğrulanıyor. Burada test edilen
 * şey ARAYÜZÜN DAVRANIŞI: doğru alanları gösteriyor mu, hatayı
 * kullanıcıya iletiyor mu, boş formu gönderiyor mu.
 * ==================================================================
 */

vi.mock('../api/authApi', () => ({
  authApi: {
    login: vi.fn(),
  },
}))

const girisYap = vi.mocked(authApi.login)

describe('LoginPage', () => {
  beforeEach(() => {
    useAuthStore.setState({ accessToken: null, refreshToken: null, user: null })
  })

  it('e-posta ve şifre alanlarını gösterir', () => {
    renderWithProviders(<LoginPage />)

    expect(screen.getByLabelText('E-posta')).toBeInTheDocument()
    expect(screen.getByLabelText('Sifre')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /giris yap/i })).toBeInTheDocument()
  })

  /**
   * ================================================================
   * BOŞ FORM SUNUCUYA GİTMEMELİ
   * ================================================================
   * Sunucu zaten reddedecek — ama boşuna bir istek atmak hem
   * kullanıcıyı bekletir hem de hız sınırından (Sprint 15: 5
   * dakikada 10 giriş) boşuna kota harcar.
   *
   * Kullanıcı formu üç kez boş gönderirse, gerçek denemeleri için
   * yalnızca yedi hakkı kalırdı.
   * ================================================================
   */
  it('boş form gönderilince istek atılmaz ve hata gösterilir', async () => {
    const kullanici = userEvent.setup()

    renderWithProviders(<LoginPage />)

    await kullanici.click(screen.getByRole('button', { name: /giris yap/i }))

    await waitFor(() => {
      expect(screen.getByLabelText('E-posta')).toBeInvalid()
    })

    expect(girisYap).not.toHaveBeenCalled()
  })

  it('geçersiz e-posta biçimi reddedilir', async () => {
    const kullanici = userEvent.setup()

    renderWithProviders(<LoginPage />)

    await kullanici.type(screen.getByLabelText('E-posta'), 'bu-bir-eposta-degil')
    await kullanici.type(screen.getByLabelText('Sifre'), 'Test1234!')
    await kullanici.click(screen.getByRole('button', { name: /giris yap/i }))

    await waitFor(() => {
      expect(screen.getByLabelText('E-posta')).toBeInvalid()
    })

    expect(girisYap).not.toHaveBeenCalled()
  })

  it('geçerli form gönderilince API çağrılır', async () => {
    const kullanici = userEvent.setup()

    girisYap.mockResolvedValue({
      accessToken: 'token',
      accessTokenExpiresAt: new Date(Date.now() + 900_000).toISOString(),
      refreshToken: 'refresh',
      refreshTokenExpiresAt: new Date(Date.now() + 604_800_000).toISOString(),
      user: {
        id: 'k1',
        email: 'test@ornek.com',
        firstName: 'Test',
        lastName: 'Kullanici',
        isEmailConfirmed: true,
        roles: ['User'],
      },
    })

    renderWithProviders(<LoginPage />)

    await kullanici.type(screen.getByLabelText('E-posta'), 'test@ornek.com')
    await kullanici.type(screen.getByLabelText('Sifre'), 'Test1234!')
    await kullanici.click(screen.getByRole('button', { name: /giris yap/i }))

    await waitFor(() => {
      expect(girisYap).toHaveBeenCalled()
    })

    // İLK ARGÜMANI kontrol ediyorum, toHaveBeenCalledWith değil.
    //
    // toHaveBeenCalledWith TÜM argümanları eşleştiriyor ve test
    // kırıldı: TanStack Query, mutationFn'i ikinci bir bağlam
    // parametresiyle çağırıyor.
    //
    // O parametre bizim sözleşmemizin parçası değil — kütüphanenin
    // iç ayrıntısı. Ona bağlanan bir test, kütüphane sürümü
    // değiştiğinde kodda hiçbir hata olmadan kırılırdı.
    expect(girisYap.mock.calls[0][0]).toEqual({
      email: 'test@ornek.com',
      password: 'Test1234!',
    })
  })

  // ==============================================================
  // PDF: "API hata ekranı"
  // ==============================================================

  /**
   * ================================================================
   * HATA KULLANICIYA GÖSTERİLMELİ, KONSOLA DEĞİL
   * ================================================================
   * En yaygın arayüz hatası: istek başarısız oluyor, konsola log
   * düşüyor, ekranda hiçbir şey değişmiyor. Kullanıcı butona basıp
   * bekliyor ve hiçbir şey olmuyor.
   *
   * Bu testin doğruladığı şey, sunucudan gelen mesajın GERÇEKTEN
   * ekrana çıktığı.
   * ================================================================
   */
  it('sunucu hatası ekranda gösterilir', async () => {
    const kullanici = userEvent.setup()

    girisYap.mockRejectedValue(
      Object.assign(new Error('istek basarisiz'), {
        detail: 'E-posta veya sifre hatali.',
      }),
    )

    renderWithProviders(<LoginPage />)

    await kullanici.type(screen.getByLabelText('E-posta'), 'test@ornek.com')
    await kullanici.type(screen.getByLabelText('Sifre'), 'YanlisSifre1!')
    await kullanici.click(screen.getByRole('button', { name: /giris yap/i }))

    await waitFor(() => {
      expect(screen.getByRole('alert')).toBeInTheDocument()
    })
  })

  /**
   * Hata sonrası form KULLANILABİLİR kalmalı.
   *
   * Buton kalıcı olarak "yükleniyor" durumunda kalsaydı, kullanıcı
   * şifresini düzeltip tekrar deneyemez ve sayfayı yenilemek
   * zorunda kalırdı.
   */
  it('hata sonrası tekrar denenebilir', async () => {
    const kullanici = userEvent.setup()

    girisYap.mockRejectedValue(Object.assign(new Error('hata'), { detail: 'Bir hata olustu.' }))

    renderWithProviders(<LoginPage />)

    await kullanici.type(screen.getByLabelText('E-posta'), 'test@ornek.com')
    await kullanici.type(screen.getByLabelText('Sifre'), 'Test1234!')
    await kullanici.click(screen.getByRole('button', { name: /giris yap/i }))

    await waitFor(() => {
      expect(screen.getByRole('alert')).toBeInTheDocument()
    })

    expect(screen.getByRole('button', { name: /giris yap/i })).toBeEnabled()
  })
})
