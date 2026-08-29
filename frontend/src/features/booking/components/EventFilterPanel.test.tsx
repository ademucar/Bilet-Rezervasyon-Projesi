import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { EventFilterPanel } from './EventFilterPanel'
import { bookingApi, type EventFilters } from '../api/bookingApi'
import { renderWithProviders } from '../../../test/testUtils'

/**
 * PDF Sprint 17 frontend testi: "Etkinlik filtreleme".
 */

vi.mock('../api/bookingApi', async (orijinal) => {
  const gercek = await orijinal<typeof import('../api/bookingApi')>()

  return {
    ...gercek,
    bookingApi: {
      getCities: vi.fn(),
      getCategories: vi.fn(),
    },
  }
})

const sehirler = vi.mocked(bookingApi.getCities)
const kategoriler = vi.mocked(bookingApi.getCategories)

function paneliCiz(filters: EventFilters = {}, onChange = vi.fn(), onReset = vi.fn()) {
  sehirler.mockResolvedValue([
    { id: 'sehir-1', name: 'İstanbul', plateCode: 34 },
    { id: 'sehir-2', name: 'Ankara', plateCode: 6 },
  ])

  kategoriler.mockResolvedValue([
    { id: 'kat-1', name: 'Konser', slug: 'konser', iconName: null },
    { id: 'kat-2', name: 'Tiyatro', slug: 'tiyatro', iconName: null },
  ])

  const aktifSayi = Object.values(filters).filter((v) => v !== undefined).length

  renderWithProviders(
    <EventFilterPanel
      filters={filters}
      onChange={onChange}
      onReset={onReset}
      activeCount={aktifSayi}
    />,
  )

  return { onChange, onReset }
}

describe('EventFilterPanel', () => {
  it('şehir ve kategori listelerini yükler', async () => {
    paneliCiz()

    await waitFor(() => {
      expect(screen.getByRole('option', { name: 'İstanbul' })).toBeInTheDocument()
    })

    expect(screen.getByRole('option', { name: 'Konser' })).toBeInTheDocument()
  })

  /**
   * ================================================================
   * FİLTRE DEĞİŞİKLİĞİ YALNIZCA DEĞİŞEN ALANI BİLDİRMELİ
   * ================================================================
   * onChange, Partial<EventFilters> alıyor — yani "yalnızca bunu
   * değiştir" diyor.
   *
   * Tüm filtre nesnesini gönderseydi, iki filtreyi hızlıca
   * değiştiren kullanıcıda ikinci çağrı birincinin sonucunu EZERDİ:
   * şehir seçilir, hemen ardından kategori seçilir ve kategori
   * çağrısı eski (şehirsiz) durumu taşıdığı için şehir seçimi
   * kaybolurdu.
   * ================================================================
   */
  it('şehir seçilince yalnızca cityId bildirilir', async () => {
    const kullanici = userEvent.setup()
    const { onChange } = paneliCiz()

    await waitFor(() => {
      expect(screen.getByRole('option', { name: 'İstanbul' })).toBeInTheDocument()
    })

    await kullanici.selectOptions(screen.getByLabelText(/şehir/i), 'sehir-1')

    expect(onChange).toHaveBeenCalledWith({ cityId: 'sehir-1' })
  })

  /**
   * "Tümü" seçeneği filtreyi KALDIRMALI, boş dize göndermemeli.
   *
   * Boş dize gönderseydik API'ye "cityId=" diye bir parametre
   * giderdi. Backend bunu geçerli bir kimlik sanıp hiçbir sonuç
   * döndürmeyebilir — ve kullanıcı "filtreyi kaldırdım ama liste
   * boş" derdi.
   */
  it('tümü seçilince filtre kaldırılır', async () => {
    const kullanici = userEvent.setup()
    const { onChange } = paneliCiz({ cityId: 'sehir-1' })

    await waitFor(() => {
      expect(screen.getByRole('option', { name: 'İstanbul' })).toBeInTheDocument()
    })

    await kullanici.selectOptions(screen.getByLabelText(/şehir/i), '')

    expect(onChange).toHaveBeenCalledWith({ cityId: undefined })
  })

  it('sıfırlama düğmesi onReset çağırır', async () => {
    const kullanici = userEvent.setup()
    const { onReset } = paneliCiz({ cityId: 'sehir-1', categoryId: 'kat-1' })

    await waitFor(() => {
      expect(screen.getByRole('option', { name: 'İstanbul' })).toBeInTheDocument()
    })

    const sifirla = screen.getByRole('button', { name: /temizle|sıfırla/i })
    await kullanici.click(sifirla)

    expect(onReset).toHaveBeenCalled()
  })

  /**
   * Aktif filtre sayısı görünmeli.
   *
   * Panel kapalıyken kaç filtre uygulandığını göstermezsek,
   * kullanıcı "neden az sonuç var?" sorusunun cevabını bulamaz ve
   * listeyi bozuk sanır.
   */
  it('aktif filtre sayısını gösterir', async () => {
    paneliCiz({ cityId: 'sehir-1', categoryId: 'kat-1' })

    await waitFor(() => {
      expect(screen.getByRole('option', { name: 'İstanbul' })).toBeInTheDocument()
    })

    // Metin "2 aktif" seklinde ve JSX'te {activeCount} + " aktif"
    // diye PARCALARA ayrilmis; getByText('2') eşleşmiyor.
    //
    // Fonksiyon eslestirici kullanıyorum: elemanin TOPLAM metnini
    // okuyor. Tam metni ("2 aktif") yazmak da calisirdi ama o zaman
    // kelime değiştiginde test kirilirdi -- oysa test edilen sey
    // SAYININ görünmesi.
    expect(
      screen.getByText((_, element) => element?.textContent?.trim() === '2 aktif'),
    ).toBeInTheDocument()
  })
})
