import { screen } from '@testing-library/react'
import { beforeEach, describe, expect, it } from 'vitest'
import { Route, Routes } from 'react-router-dom'
import { ProtectedRoute } from './ProtectedRoute'
import { useAuthStore } from '../stores/authStore'
import { renderWithProviders } from '../test/testUtils'

/**
 * PDF Sprint 17 frontend testi: "Yetkisiz route".
 */

function rotalariCiz(baslangic: string, roller?: string[]) {
  return renderWithProviders(
    <Routes>
      <Route path="/giris" element={<div>Giriş Sayfası</div>} />
      <Route path="/yetkisiz" element={<div>Yetkisiz Sayfası</div>} />

      <Route element={<ProtectedRoute roles={roller} />}>
        <Route path="/panel" element={<div>Korumalı Panel</div>} />
      </Route>
    </Routes>,
    { route: baslangic },
  )
}

/** Oturum durumunu doğrudan store üzerinden kuruyor. */
function oturumAc(roller: string[]) {
  useAuthStore.setState({
    accessToken: 'sahte-token',
    user: {
      id: 'kullanici-1',
      email: 'test@ornek.com',
      firstName: 'Test',
      lastName: 'Kullanıcı',
      isEmailConfirmed: true,
      roles: roller,
    },
  })
}

describe('ProtectedRoute', () => {
  beforeEach(() => {
    // ==============================================================
    // STORE HER TESTTE SIFIRLANIYOR
    // ==============================================================
    // Zustand store'u modül seviyesinde yaşıyor, yani testler
    // arasında PAYLAŞILIYOR. Sıfırlamasaydık bir testte açılan
    // oturum sonrakine sızardı ve "giriş yapmamış kullanıcı
    // yönlendirilmeli" testi YANLIŞLIKLA geçerdi.
    //
    // Yani en kritik güvenlik testimiz, hiçbir şey doğrulamayan bir
    // teste dönüşürdü.
    // ==============================================================
    useAuthStore.setState({ accessToken: null, refreshToken: null, user: null })
  })

  it('giriş yapmamış kullanıcıyı giriş sayfasına yönlendirir', () => {
    rotalariCiz('/panel')

    expect(screen.getByText('Giriş Sayfası')).toBeInTheDocument()
    expect(screen.queryByText('Korumalı Panel')).not.toBeInTheDocument()
  })

  it('giriş yapmış kullanıcıyı içeri alır', () => {
    oturumAc(['User'])

    rotalariCiz('/panel')

    expect(screen.getByText('Korumalı Panel')).toBeInTheDocument()
  })

  /**
   * ================================================================
   * 401 VE 403 AYRIMI ARAYÜZDE DE VAR
   * ================================================================
   * Backend'de 401 "kim olduğunu bilmiyorum", 403 "biliyorum ama
   * yetkin yok" demek (Sprint 17 entegrasyon testinde doğrulandı).
   *
   * Arayuzde karşılığı:
   *   giriş yok      -> /giriş        (giriş yap, sonra dön)
   *   rol yetersiz   -> /yetkisiz     (giriş yapmak işe yaramaz)
   *
   * İkisini de /giriş'e yönlendirseydik, yetkisiz bir kullanıcı
   * giriş sayfasına atılır, zaten girişli olduğu için tekrar panele
   * yönlendirilir ve sonsuz bir döngüye girerdi.
   * ================================================================
   */
  it('rolü yetersiz kullanıcıyı yetkisiz sayfasına yönlendirir', () => {
    oturumAc(['User'])

    rotalariCiz('/panel', ['Admin'])

    expect(screen.getByText('Yetkisiz Sayfası')).toBeInTheDocument()
    expect(screen.queryByText('Korumalı Panel')).not.toBeInTheDocument()
  })

  it('doğru role sahip kullanıcıyı içeri alır', () => {
    oturumAc(['Admin'])

    rotalariCiz('/panel', ['Admin'])

    expect(screen.getByText('Korumalı Panel')).toBeInTheDocument()
  })

  /**
   * Birden fazla rol kabul edilen sayfalar var (örneğin raporlar
   * hem Admin hem Organizer'a açık). Kullanıcının BİR tanesine
   * sahip olması yeterli olmalı.
   */
  it('kabul edilen rollerden birine sahip olmak yeterli', () => {
    oturumAc(['Organizer'])

    rotalariCiz('/panel', ['Admin', 'Organizer'])

    expect(screen.getByText('Korumalı Panel')).toBeInTheDocument()
  })
})
