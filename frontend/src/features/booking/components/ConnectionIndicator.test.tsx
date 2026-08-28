import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { ConnectionIndicator } from './ConnectionIndicator'
import type { ConnectionStatus } from '../hooks/useSeatHub'

/**
 * PDF Sprint 17 frontend testi: "SignalR güncellemesi".
 *
 * ==================================================================
 * NEDEN HUB'IN KENDİSİNİ DEĞİL, GÖSTERGEYİ TEST EDİYORUM?
 * ==================================================================
 * useSeatHub gerçek bir WebSocket bağlantısı kuruyor. jsdom'da
 * WebSocket yok; mock'lasaydım @microsoft/signalr'ın iç durum
 * makinesini (negotiate, handshake, reconnect) taklit etmem
 * gerekirdi. O taklit gerçek kütüphaneyle uyuşmadığında test yeşil
 * kalır ama hiçbir şey kanıtlamaz.
 *
 * Sprint 10'da bu dersi zaten almıştım: SignalR grup izolasyonunu
 * tarayıcıda ölçmeye çalıştım, window.WebSocket sarmalayıcısı
 * taşıma katmanı görüşmesini bozdu ve iki denemeden sonra vazgeçip
 * kod incelemesiyle doğruladım.
 *
 * Burada test ettiğim şey KULLANICININ GÖRDÜĞÜ kısım: bağlantı
 * durumu değiştiğinde ekranda doğru bilgi çıkıyor mu. Gerçek
 * bağlantı davranışı E2E testinde (Playwright) doğrulanıyor.
 * ==================================================================
 */
describe('ConnectionIndicator', () => {
  const durumlar: ConnectionStatus[] = ['connecting', 'connected', 'reconnecting', 'disconnected']

  it.each(durumlar)('%s durumu için bir metin gösterir', (durum) => {
    render(<ConnectionIndicator status={durum} />)

    const gosterge = screen.getByRole('status')

    expect(gosterge).toBeInTheDocument()
    expect(gosterge.textContent?.trim()).not.toBe('')
  })

  it('bağlıyken canlı olduğunu bildirir', () => {
    render(<ConnectionIndicator status="connected" />)

    expect(screen.getByRole('status')).toHaveTextContent(/canlı/i)
  })

  /**
   * ================================================================
   * BAĞLANTI YOKKEN KULLANICI BUNU BİLMELİ
   * ================================================================
   * Bu göstergenin var olma sebebi Sprint 10'da somut bir hatayı
   * yakalamış olması: Vite proxy'sine /hubs girdisini eklemeyi
   * unutmuştum.
   *
   * Gösterge olmasaydı harita yine çalışırdı (yoklama yedeği
   * devrede) ve SignalR'ın hiç bağlanmadığını fark etmezdim —
   * Sprint 10'u "bitti" sanıp devam ederdim.
   *
   * Kullanıcı açısından da önemli: bağlantı yoksa koltuk haritası
   * bayat olabilir. Bunu bilmeden koltuk seçen kullanıcı, "az önce
   * boştu" dediği koltuk için 409 alır ve sistemi hatalı sanır.
   * ================================================================
   */
  it('bağlantı yokken kullanıcıyı uyarır', () => {
    render(<ConnectionIndicator status="disconnected" />)

    expect(screen.getByRole('status')).toHaveTextContent(/yok/i)
  })

  /**
   * ================================================================
   * DURUM DEĞİŞİKLİĞİ EKRAN OKUYUCUYA DA BİLDİRİLMELİ
   * ================================================================
   * role="status" + aria-live="polite" ikilisi, ekran okuyucunun
   * bu bölgeyi izlemesini ve içerik değiştiğinde OKUMASINI sağlıyor.
   *
   * "polite" bilinçli: "assertive" olsaydı ekran okuyucu
   * kullanıcının o an okuduğu şeyi keserdi. Bağlantı durumu önemli
   * ama bir cümlenin ortasında araya girecek kadar acil değil.
   * ================================================================
   */
  it('durum bölgesi ekran okuyucuya bildiriliyor', () => {
    render(<ConnectionIndicator status="reconnecting" />)

    expect(screen.getByRole('status')).toHaveAttribute('aria-live', 'polite')
  })
})
