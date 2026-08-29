import { lazy, Suspense } from 'react'
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { ReactQueryDevtools } from '@tanstack/react-query-devtools'
import { ProtectedRoute } from './routes/ProtectedRoute'
import { PublicOnlyRoute } from './routes/PublicOnlyRoute'
import { ErrorBoundary } from './components/layout/ErrorBoundary'
import { Roles } from './types/auth'

// ROUTE BAZLI KOD BOLME (code splitting)
//
// PDF Sprint 18: "Route bazlı code splitting uygulanmalıdır."
//
// lazy() ile her sayfa AYRI bir JS parcasina derleniyor ve yalnızca
// o sayfaya gidildiginde indiriliyor.
//
// Neden önemli? Su an 6 sayfa var, fark küçük. Ama Sprint 5-13'te
// organizatör paneli, admin paneli, koltuk seçim ekrani, raporlama
// grafikleri eklenecek. Hepsi tek pakette olsaydı, sadece giriş
// yapmak isteyen kullanıcı Recharts kutuphanesini de indirmek
// zorunda kalırdı.
//
// Simdiden kurmak, sonradan eklemekten kolay: yapiyi bastan doğru
// kurunca yeni sayfa eklerken dusunmeye bile gerek kalmiyor.
const LoginPage = lazy(() =>
  import('./features/auth/pages/LoginPage').then((m) => ({ default: m.LoginPage })),
)
const RegisterPage = lazy(() =>
  import('./features/auth/pages/RegisterPage').then((m) => ({ default: m.RegisterPage })),
)
const ForgotPasswordPage = lazy(() =>
  import('./features/auth/pages/ForgotPasswordPage').then((m) => ({
    default: m.ForgotPasswordPage,
  })),
)
const ResetPasswordPage = lazy(() =>
  import('./features/auth/pages/ResetPasswordPage').then((m) => ({ default: m.ResetPasswordPage })),
)
const HomePage = lazy(() =>
  import('./features/home/HomePage').then((m) => ({ default: m.HomePage })),
)
const UnauthorizedPage = lazy(() =>
  import('./features/misc/UnauthorizedPage').then((m) => ({ default: m.UnauthorizedPage })),
)
const NotFoundPage = lazy(() =>
  import('./features/misc/NotFoundPage').then((m) => ({ default: m.NotFoundPage })),
)

// --- Bilet alma akışı (Sprint 7-8) ---
// Bu bes sayfa ayrı parcalar ama AYNI akışın adimlari. Vite,
// paylastiklari kodu (bookingApi, SeatMap, format) ortak bir parcaya
// koyup her ikisine de bagliyor -- yani tekrar indirilmiyor.
const EventsPage = lazy(() =>
  import('./features/booking/pages/EventsPage').then((m) => ({ default: m.EventsPage })),
)
const EventDetailPage = lazy(() =>
  import('./features/booking/pages/EventDetailPage').then((m) => ({ default: m.EventDetailPage })),
)
const SeatSelectionPage = lazy(() =>
  import('./features/booking/pages/SeatSelectionPage').then((m) => ({
    default: m.SeatSelectionPage,
  })),
)
const ReservationPage = lazy(() =>
  import('./features/booking/pages/ReservationPage').then((m) => ({ default: m.ReservationPage })),
)
const MyReservationsPage = lazy(() =>
  import('./features/booking/pages/MyReservationsPage').then((m) => ({
    default: m.MyReservationsPage,
  })),
)
const MyTicketsPage = lazy(() =>
  import('./features/booking/pages/MyTicketsPage').then((m) => ({ default: m.MyTicketsPage })),
)
const MyFavoritesPage = lazy(() =>
  import('./features/booking/pages/MyFavoritesPage').then((m) => ({ default: m.MyFavoritesPage })),
)

// --- Raporlama paneli (Sprint 13) ---
// Recharts agir bir kutuphane (~100 KB). Ayrı parcada tutmak ŞART:
// bilet alan normal kullanıcı bu kodu HİÇ indirmiyor.
const DashboardPage = lazy(() =>
  import('./features/reports/pages/DashboardPage').then((m) => ({ default: m.DashboardPage })),
)

// --- Admin paneli ---
// Ayrı parcalara boluyorum: normal kullanıcı bu ekranlari HİÇ
// indirmeyecek. Koltuk haritası ve form kutuphaneleri bu sayfalarda
// yogun; hepsini ana pakete koysaydım giriş yapan herkes bedelini oderdi.
const VenuesPage = lazy(() =>
  import('./features/admin/pages/VenuesPage').then((m) => ({ default: m.VenuesPage })),
)
const VenueDetailPage = lazy(() =>
  import('./features/admin/pages/VenueDetailPage').then((m) => ({ default: m.VenueDetailPage })),
)
const HallDetailPage = lazy(() =>
  import('./features/admin/pages/HallDetailPage').then((m) => ({ default: m.HallDetailPage })),
)
const SeatLayoutPage = lazy(() =>
  import('./features/admin/pages/SeatLayoutPage').then((m) => ({ default: m.SeatLayoutPage })),
)

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      // 1 dakika boyunca veriyi "taze" say, tekrar isteme.
      // Varsayılan 0'dir; yani her bileşen bagladiginda yeni istek gider.
      staleTime: 60_000,

      // 401 ve 403'te YENIDEN DENEME
      //
      // Varsayılan davranis başarısız isteği 3 kez tekrarlar.
      //
      // Bu benim için ZARARLI olurdu: 401 alan bir istek zaten
      // interceptor tarafından token yenilenip tekrarlaniyor.
      // TanStack Query bir de kendi başına 3 kez denerse, tek bir
      // basarisizlik 4 gereksiz isteğe donusur.
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

      // Sekme değiştirip geri geldiğinde otomatik yenileme.
      // Koltuk uygunlugu gibi hizli degisen veriler için degerli.
      refetchOnWindowFocus: true,
    },

    mutations: {
      // Mutation'lar (POST/PUT/DELETE) ASLA otomatik tekrarlanmamali.
      //
      // "Rezervasyon oluştur" isteği başarısız gorunup aslında
      // başarılı olduysa, tekrar gondermek IKINCI bir rezervasyon
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
        <div className="h-64 animate-pulse rounded-[4px] bg-slate-200" />
      </div>
    </div>
  )
}

export default function App() {
  return (
    <ErrorBoundary>
      <QueryClientProvider client={queryClient}>
        <BrowserRouter>
          {/* Suspense: lazy() ile yuklenen sayfa hazır olana kadar
              fallback gosterilir. Olmasaydı React hata firlatirdi. */}
          <Suspense fallback={<PageFallback />}>
            <Routes>
              {/* ---- Yalnızca giriş YAPMAMIS kullanıcılar ---- */}
              <Route element={<PublicOnlyRoute />}>
                <Route path="/giris" element={<LoginPage />} />
                <Route path="/kayit" element={<RegisterPage />} />
                <Route path="/sifremi-unuttum" element={<ForgotPasswordPage />} />
                <Route path="/sifre-sifirla" element={<ResetPasswordPage />} />
              </Route>

              {/* ---- Giriş gerektiren sayfalar ---- */}
              <Route element={<ProtectedRoute />}>
                <Route path="/" element={<HomePage />} />

                {/* ---- Bilet alma akışı ----
                    Etkinlik listesi ve detayı backend'de ANONIM erisime
                    açık. Yine de ProtectedRoute icine koyuyorum: bu
                    akışın sonu rezervasyon ve ödeme, ikisi de giriş
                    gerektiriyor.

                    Kullaniciyi 4 sayfa gezdirip koltuğu sectirdikten
                    SONRA "önce giriş yapın" demek, en can sıkıcı
                    deneyimlerden biridir. Kapiyi bastan gösteriyorum.

                    Listelemeyi herkese acmayi Sprint 11'de
                    planlamistim ve ACMADIM. Sebep: SiteHeader
                    oturum acmis kullaniciya gore yazilmis (Cikis
                    dugmesi, Biletlerim baglantisi). Anonim ziyaretci
                    icin ayri bir ust cubuk gerekiyor ve bu, listeleme
                    ekraninin isi degil.

                    Bedeli su: etkinlik sayfalari arama motoruna
                    kapali. Gercek bir bilet sitesinde bu kabul
                    edilemez; burada bilincli bir eksik olarak
                    duruyor. */}
                <Route path="/etkinlikler" element={<EventsPage />} />
                <Route path="/etkinlikler/:eventId" element={<EventDetailPage />} />
                <Route path="/oturumlar/:sessionId/koltuklar" element={<SeatSelectionPage />} />
                <Route path="/rezervasyonlar/:reservationId" element={<ReservationPage />} />
                <Route path="/rezervasyonlarim" element={<MyReservationsPage />} />
                <Route path="/biletlerim" element={<MyTicketsPage />} />
                <Route path="/favorilerim" element={<MyFavoritesPage />} />
                <Route path="/panel" element={<DashboardPage />} />
              </Route>

              {/* ---- Admin paneli ----
                  ProtectedRoute roles={['Admin']} -> yalnızca admin görür.
                  UNUTMA: bu bir GÜVENLİK önlemi değil, kullanıcı deneyimi.
                  Gerçek kontrol backend'de AdminOnly policy'sinde. */}
              <Route element={<ProtectedRoute roles={[Roles.Admin]} />}>
                <Route path="/admin/mekanlar" element={<VenuesPage />} />
                <Route path="/admin/mekanlar/:venueId" element={<VenueDetailPage />} />
                <Route path="/admin/salonlar/:hallId" element={<HallDetailPage />} />
                <Route path="/admin/oturma-planlari/:layoutId" element={<SeatLayoutPage />} />
              </Route>

              <Route path="/yetkisiz" element={<UnauthorizedPage />} />

              {/* Turkce yollari kullanıyorum ama Ingilizce deneyenler
                  için de yönlendirme koyuyorum -- kırık link olmasın. */}
              <Route path="/login" element={<Navigate to="/giris" replace />} />
              <Route path="/register" element={<Navigate to="/kayit" replace />} />

              <Route path="*" element={<NotFoundPage />} />
            </Routes>
          </Suspense>
        </BrowserRouter>

        {/* Devtools yalnızca gelistirmede paketlenir.
            import.meta.env.DEV, Vite tarafından üretim derlemesinde
            false'a sabitlenir ve bu blok tamamen silinir (tree shaking). */}
        {import.meta.env.DEV && <ReactQueryDevtools initialIsOpen={false} />}
      </QueryClientProvider>
    </ErrorBoundary>
  )
}
