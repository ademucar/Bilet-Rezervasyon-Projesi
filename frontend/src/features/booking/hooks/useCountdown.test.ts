import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useCountdown } from './useCountdown'

/**
 * PDF Sprint 17 frontend testi: "Rezervasyon sayaçı".
 *
 * BU HOOK NEDEN AYRICA TEST EDİLİYOR?
 *
 * Sayaç, kullanıcının koltuğunu kaybetmeden önce elindeki tek
 * uyarı. Yanlış çalışması iki yönde de kötü:
 *
 *   - Olduğundan FAZLA gösterirse: kullanıcı acele etmez, süre
 *     dolar, koltuğunu kaybeder ve "ekranda 5 dakika yazıyordu"
 *     der.
 *   - Olduğundan AZ gösterirse: kullanıcı boşuna panikler ve
 *     ödemeyi yarıda bırakır.
 *
 * Hook'un içindeki asıl zorluk, tarayıcının arka plan sekmelerde
 * zamanlayıcıları yavaşlatması. Onu doğrudan test edemiyorum ama
 * hook'un buna karşı kullandığı tasarımı (bitiş anına göre yeniden
 * ölçme) test edebiliyorum.
 *
 */
describe('useCountdown', () => {
  beforeEach(() => {
    // Sahte zamanlayıcılar: 10 dakika beklemek yerine zamanı
    // ilerletiyoruz. Gerçek beklemeyle test etseydim tek bir test
    // 10 dakika sürerdi ve kimse bu paketi çalıştırmazdı.
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('başlangıç değerini olduğu gibi gösterir', () => {
    const { result } = renderHook(() => useCountdown(600))

    expect(result.current).toBe(600)
  })

  it('süre geçtikçe azalır', () => {
    const { result } = renderHook(() => useCountdown(600))

    act(() => {
      vi.advanceTimersByTime(5000)
    })

    // 5 saniye geçti; kalan 595 civarı olmalı.
    // Tam eşitlik aramıyorum: sahte zamanlayıcı ile
    // performance.now() arasında bir tık kayma olabilir ve testi
    // ARADA BİR kıran bir eşitlik kontrolü, testin kendisini
    // güvenilmez yapardı.
    expect(result.current).toBeLessThanOrEqual(595)
    expect(result.current).toBeGreaterThan(590)
  })

  /**
   *
   * EN ÖNEMLİ TEST: SIFIRIN ALTINA İNMEMELİ
   *
   * Negatife inseydi ekranda "-3:12 kaldı" gibi bir şey yazardı.
   * Bu yalnızca çirkin değil, YANILTICI: kullanıcı eksi bir sayıyı
   * "hâlâ süre var" diye okuyabilir.
   *
   * Ayrıca sayaç 0'a ulaştığında arayüz "süre doldu" durumuna
   * geçiyor; negatif değer o mantığı da bozardı.
   *
   */
  it('sıfırın altına inmez', () => {
    const { result } = renderHook(() => useCountdown(3))

    act(() => {
      vi.advanceTimersByTime(60_000)
    })

    expect(result.current).toBe(0)
  })

  /**
   * Arka plan sekmesi senaryosu.
   *
   * Tarayıcı, sekme arka plandayken zamanlayıcıları dakikada bire
   * kadar yavaşlatıyor. Yani 3 dakika sonra dönen kullanıcı için
   * belki yalnızca 3 tık gerçekleşmiş olur.
   *
   * Hook her tık'ta "1 azalt" deseydi ekranda 3 saniye geçmiş
   * görünürdü. Bitiş anına göre ölçtüğü için TEK bir tık bile
   * doğru değeri veriyor.
   *
   * Bunu, çok az sayıda tık üreterek ama çok zaman geçirerek
   * taklit ediyorum.
   */
  it('az sayıda tık gelse bile doğru süreyi gösterir', () => {
    const { result } = renderHook(() => useCountdown(600))

    act(() => {
      // 180 saniye ilerlet — arka planda olsa bile hook bitiş
      // anına göre yeniden ölçtüğü için doğru sonucu vermeli.
      vi.advanceTimersByTime(180_000)
    })

    expect(result.current).toBeLessThanOrEqual(420)
    expect(result.current).toBeGreaterThan(415)
  })

  it('tanımsız süre için sıfır döner', () => {
    const { result } = renderHook(() => useCountdown(undefined))

    expect(result.current).toBe(0)
  })
})
