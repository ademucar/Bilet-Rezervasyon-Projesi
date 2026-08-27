import { lazy, Suspense } from 'react'
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { ReactQueryDevtools } from '@tanstack/react-query-devtools'
import { ProtectedRoute } from './routes/ProtectedRoute'
import { PublicOnlyRoute } from './routes/PublicOnlyRoute'
import { ErrorBoundary } from './components/layout/ErrorBoundary'
import { Roles } from './types/auth'

// ===================================================================
// ROUTE BAZLI KOD BOLME (code splitting)
// ===================================================================
// PDF Sprint 18: "Route bazli code splitting uygulanmalidir."
//
// lazy() ile her sayfa AYRI bir JS parcasina derleniyor ve yalnizca
// o sayfaya gidildiginde indiriliyor.
//
// Neden onemli? Su an 6 sayfa var, fark kucuk. Ama Sprint 5-13'te
// organizator paneli, admin paneli, koltuk secim ekrani, raporlama
// grafikleri eklenecek. Hepsi tek pakette olsaydi, sadece giris
// yapmak isteyen kullanici Recharts kutuphanesini de indirmek
// zorunda kalirdi.
//
// Simdiden kurmak, sonradan eklemekten kolay: yapiyi bastan dogru
// kurunca yeni sayfa eklerken dusunmeye bile gerek kalmiyor.
// ===================================================================
const LoginPage = lazy(() => import('./features/auth/pages/LoginPage').then((m) => ({ default: m.LoginPage })))
const RegisterPage = lazy(() => import('./features/auth/pages/RegisterPage').then((m) => ({ default: m.RegisterPage })))
const ForgotPasswordPage = lazy(() => import('./features/auth/pages/ForgotPasswordPage').then((m) => ({ default: m.ForgotPasswordPage })))
const ResetPasswordPage = lazy(() => import('./features/auth/pages/ResetPasswordPage').then((m) => ({ default: m.ResetPasswordPage })))
const HomePage = lazy(() => import('./features/home/HomePage').then((m) => ({ default: m.HomePage })))
const UnauthorizedPage = lazy(() => import('./features/misc/UnauthorizedPage').then((m) => ({ default: m.UnauthorizedPage })))
const NotFoundPage = lazy(() => import('./features/misc/NotFoundPage').then((m) => ({ default: m.NotFoundPage })))

// --- Bilet alma akisi (Sprint 7-8) ---
// Bu bes sayfa ayri parcalar ama AYNI akisin adimlari. Vite,
// paylastiklari kodu (bookingApi, SeatMap, format) ortak bir parcaya
// koyup her ikisine de bagliyor -- yani tekrar indirilmiyor.
const EventsPage = lazy(() => import('./features/booking/pages/EventsPage').then((m) => ({ default: m.EventsPage })))
const EventDetailPage = lazy(() => import('./features/booking/pages/EventDetailPage').then((m) => ({ default: m.EventDetailPage })))
const SeatSelectionPage = lazy(() => import('./features/booking/pages/SeatSelectionPage').then((m) => ({ default: m.SeatSelectionPage })))
const ReservationPage = lazy(() => import('./features/booking/pages/ReservationPage').then((m) => ({ default: m.ReservationPage })))
const MyReservationsPage = lazy(() => import('./features/booking/pages/MyReservationsPage').then((m) => ({ default: m.MyReservationsPage })))
const MyTicketsPage = lazy(() => import('./features/booking/pages/MyTicketsPage').then((m) => ({ default: m.MyTicketsPage })))

// --- Admin paneli ---
// Ayri parcalara boluyorum: normal kullanici bu ekranlari HIC
// indirmeyecek. Koltuk haritasi ve form kutuphaneleri bu sayfalarda
// yogun; hepsini ana pakete koysaydik giris yapan herkes bedelini oderdi.
const VenuesPage = lazy(() => import('./features/admin/pages/VenuesPage').then((m) => ({ default: m.VenuesPage })))
const VenueDetailPage = lazy(() => import('./features/admin/pages/VenueDetailPage').then((m) => ({ default: m.VenueDetailPage })))
const HallDetailPage = lazy(() => import('./features/admin/pages/HallDetailPage').then((m) => ({ default: m.HallDetailPage })))
const SeatLayoutPage = lazy(() => import('./features/admin/pages/SeatLayoutPage').then((m) => ({ default: m.SeatLayoutPage })))

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      // 1 dakika boyunca veriyi "taze" say, tekrar isteme.
      // Varsayilan 0'dir; yani her bilesen bagladiginda yeni istek gider.
      staleTime: 60_000,

      // ==============================================================
      // 401 ve 403'te YENIDEN DENEME
      // ==============================================================
      // Varsayilan davranis basarisiz istegi 3 kez tekrarlar.
      //
      // Bu bizim icin ZARARLI olurdu: 401 alan bir istek zaten
      // interceptor tarafindan token yenilenip tekrarlaniyor.
      // TanStack Query bir de kendi basina 3 kez denerse, tek bir
      // basarisizlik 4 gereksiz istege donusur.
      //
      // 403'te tekrar denemek ise tamamen anlamsiz: yetki yoksa
      // 100 kez de denesen yine yok.
      retry: (failureCount, error) => {
        const status = (error as { response?: { status?: number } })?.response?.status

        if (status === 401 || status === 403 || status === 404) {
          return false
        }

        return failureCount < 2
      },

      // Sekme degistirip geri geldiginde otomatik yenileme.
      // Koltuk uygunlugu gibi hizli degisen veriler icin degerli.
      refetchOnWindowFocus: true,
    },

    mutations: {
      // Mutation'lar (POST/PUT/DELETE) ASLA otomatik tekrarlanmamali.
      //
      // "Rezervasyon olustur" istegi basarisiz gorunup aslinda
      // basarili olduysa, tekrar gondermek IKINCI bir rezervasyon
      // olusturabilir. Backend'de idempotency var ama ona guvenip
      // gereksiz istek gondermenin anlami yok.
      retry: false,
    },
  },
})

/** Sayfa yuklenirken gosterilen iskelet. PDF: "Skeleton loading". */
function PageFallback() {
  return (
    <div className="flex min-h-screen items-center justify-center">
      <div className="w-full max-w-md space-y-4 px-4">
        <div className="h-8 w-32 animate-pulse rounded bg-slate-200" />
        <div className="h-64 animate-pulse rounded-2xl bg-slate-200" />
      </div>
    </div>
  )
}

export default function App() {
  return (
    <ErrorBoundary>
      <QueryClientProvider client={queryClient}>
        <BrowserRouter>
          {/* Suspense: lazy() ile yuklenen sayfa hazir olana kadar
              fallback gosterilir. Olmasaydi React hata firlatirdi. */}
          <Suspense fallback={<PageFallback />}>
            <Routes>
              {/* ---- Yalnizca giris YAPMAMIS kullanicilar ---- */}
              <Route element={<PublicOnlyRoute />}>
                <Route path="/giris" element={<LoginPage />} />
                <Route path="/kayit" element={<RegisterPage />} />
                <Route path="/sifremi-unuttum" element={<ForgotPasswordPage />} />
                <Route path="/sifre-sifirla" element={<ResetPasswordPage />} />
              </Route>

              {/* ---- Giris gerektiren sayfalar ---- */}
              <Route element={<ProtectedRoute />}>
                <Route path="/" element={<HomePage />} />

                {/* ---- Bilet alma akisi ----
                    Etkinlik listesi ve detayi backend'de ANONIM erisime
                    acik. Yine de ProtectedRoute icine koyuyorum: bu
                    akisin sonu rezervasyon ve odeme, ikisi de giris
                    gerektiriyor.

                    Kullaniciyi 4 sayfa gezdirip koltugu sectirdikten
                    SONRA "once giris yapin" demek, en can sikici
                    deneyimlerden biridir. Kapiyi bastan gosteriyorum.

                    Sprint 11'de arama ve listeleme herkese acilacak
                    (SEO icin de gerekli); o zaman bu iki rota
                    disari alinacak. */}
                <Route path="/etkinlikler" element={<EventsPage />} />
                <Route path="/etkinlikler/:eventId" element={<EventDetailPage />} />
                <Route path="/oturumlar/:sessionId/koltuklar" element={<SeatSelectionPage />} />
                <Route path="/rezervasyonlar/:reservationId" element={<ReservationPage />} />
                <Route path="/rezervasyonlarim" element={<MyReservationsPage />} />
                <Route path="/biletlerim" element={<MyTicketsPage />} />
              </Route>

              {/* ---- Admin paneli ----
                  ProtectedRoute roles={['Admin']} -> yalnizca admin gorur.
                  UNUTMA: bu bir GUVENLIK onlemi degil, kullanici deneyimi.
                  Gercek kontrol backend'de AdminOnly policy'sinde. */}
              <Route element={<ProtectedRoute roles={[Roles.Admin]} />}>
                <Route path="/admin/mekanlar" element={<VenuesPage />} />
                <Route path="/admin/mekanlar/:venueId" element={<VenueDetailPage />} />
                <Route path="/admin/salonlar/:hallId" element={<HallDetailPage />} />
                <Route path="/admin/oturma-planlari/:layoutId" element={<SeatLayoutPage />} />
              </Route>

              <Route path="/yetkisiz" element={<UnauthorizedPage />} />

              {/* Turkce yollari kullaniyorum ama Ingilizce deneyenler
                  icin de yonlendirme koyuyorum -- kirik link olmasin. */}
              <Route path="/login" element={<Navigate to="/giris" replace />} />
              <Route path="/register" element={<Navigate to="/kayit" replace />} />

              <Route path="*" element={<NotFoundPage />} />
            </Routes>
          </Suspense>
        </BrowserRouter>

        {/* Devtools yalnizca gelistirmede paketlenir.
            import.meta.env.DEV, Vite tarafindan uretim derlemesinde
            false'a sabitlenir ve bu blok tamamen silinir (tree shaking). */}
        {import.meta.env.DEV && <ReactQueryDevtools initialIsOpen={false} />}
      </QueryClientProvider>
    </ErrorBoundary>
  )
}
