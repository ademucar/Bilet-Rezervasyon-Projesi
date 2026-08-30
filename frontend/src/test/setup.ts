import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { afterEach, vi } from 'vitest'

// Her testten sonra DOM temizleniyor
//
// Testing Library bileşenleri gerçek bir DOM'a bağlıyor. Temizlemezsek
// önceki testin render ettiği elemanlar sayfada kalır ve
// getByText("Giriş") gibi sorgular "birden fazla eşleşme" hatası verir.
//
// Daha kötüsü: bazen hata vermez, ESKİ elemanı bulur ve test yanlış
// bir şeyi doğrular. Sırasına göre geçip kalan testler, ayıklanması en
// zor test türüdür.
afterEach(() => {
  cleanup()
  vi.clearAllMocks()
})

// jsdom'DA OLMAYAN TARAYICI API'LERI
//
// jsdom tam bir tarayıcı değil; bazı API'ler eksik. Bileşenlerim
// bunları kullanıyor ve mock'lamazsak test "matchMedia is not a
// function" diye patlar -- oysa bileşende hata yok.

// Tailwind'in duyarlı (responsive) davranışını test ederken gerekli.
Object.defineProperty(window, 'matchMedia', {
  writable: true,
  value: (query: string) => ({
    matches: false,
    media: query,
    onchange: null,
    addEventListener: vi.fn(),
    removeEventListener: vi.fn(),
    dispatchEvent: vi.fn(),
  }),
})

// Koltuk haritası ve sanal listeler kullanıyor.
globalThis.ResizeObserver = class {
  observe() {}
  unobserve() {}
  disconnect() {}
} as unknown as typeof ResizeObserver

globalThis.IntersectionObserver = class {
  observe() {}
  unobserve() {}
  disconnect() {}
  takeRecords() {
    return []
  }
  root = null
  rootMargin = ''
  thresholds = []
} as unknown as typeof IntersectionObserver

// scrollIntoView jsdom'da tanımlı değil; koltuk seçiminde kullanılıyor.
Element.prototype.scrollIntoView = vi.fn()
