import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, type RenderOptions } from '@testing-library/react'
import type { ReactElement, ReactNode } from 'react'
import { MemoryRouter } from 'react-router-dom'

/**
 * TESTLER İÇİN ORTAK SARMALAYICI
 *
 * Bileşenlerimizin çoğu iki bağlama ihtiyaç duyuyor:
 *   - React Router (Link, useNavigate, useLocation)
 *   - TanStack Query (useQuery, useMutation)
 *
 * Bunları her testte elle kurmak 15 satırlık tekrar demek olurdu ve
 * biri unutulduğunda hata mesajı ("useNavigate() may be used only in
 * the context of a Router") bileşende sorun varmış gibi görünürdü.
 *
 */

/**
 * Her test için YENİ bir QueryClient üretir.
 *
 * Tek bir istemciyi paylaşsaydık, bir testte önbelleğe alınan veri
 * sonraki teste sızardı ve "veri yükleniyor" durumunu test etmek
 * imkânsız olurdu — veri zaten önbellekte hazır olurdu.
 *
 * Backend'deki Respawn ile aynı mantık: her test temiz başlamalı.
 *
 */
function testQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: {
        // Testte yeniden deneme KAPALI.
        //
        // Açık kalsaydı, hata durumunu test ederken TanStack Query
        // 3 kez daha denerdi ve test saniyelerce beklerdi. Daha
        // kötüsü: "hata ekranı göründü mü?" kontrolü, denemeler
        // sürerken çalışıp başarısız olurdu.
        retry: false,

        // Ekran odağı değişince yeniden sorgulama: testte gereksiz
        // ve öngörülemez istekler üretir.
        refetchOnWindowFocus: false,
      },
      mutations: { retry: false },
    },
  })
}

interface Seçenekler extends Omit<RenderOptions, 'wrapper'> {
  /** Başlangıç adresi. Yönlendirme testlerinde kullanılıyor. */
  route?: string
}

export function renderWithProviders(ui: ReactElement, secenekler: Seçenekler = {}) {
  const { route = '/', ...rest } = secenekler

  const client = testQueryClient()

  function Sarmalayici({ children }: { children: ReactNode }) {
    return (
      <QueryClientProvider client={client}>
        {/*
          MemoryRouter: gerçek tarayıcı adres çubuğu olmadan
          yönlendirme. jsdom'da history API çalışıyor ama
          MemoryRouter testler arası sızıntıyı tamamen engelliyor.
        */}
        <MemoryRouter initialEntries={[route]}>{children}</MemoryRouter>
      </QueryClientProvider>
    )
  }

  return {
    ...render(ui, { wrapper: Sarmalayici, ...rest }),
    queryClient: client,
  }
}
