import { useEffect, useRef, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { SiteHeader } from '../../../components/layout/SiteHeader'
import { Alert } from '../../../components/ui/Alert'
import { Button } from '../../../components/ui/Button'
import { toProblem } from '../../../lib/api/client'
import { formatDateTime, formatMoney } from '../../../lib/format'
import { formatCountdown, useCountdown } from '../hooks/useCountdown'
import {
  bookingApi,
  newIdempotencyKey,
  PaymentStatus,
  ReservationStatus,
  type PaymentDto,
} from '../api/bookingApi'

/**
 * ==================================================================
 * REZERVASYON VE ODEME EKRANI -- PDF Sprint 7 + Sprint 8
 * ==================================================================
 * Iki sprintin frontend'i tek sayfada bulusuyor cunku kullanici
 * acisindan bunlar tek bir an: "koltugum tutuldu, sureyi kaybetmeden
 * odeyeyim". Ayri sayfalara bolseydik geri sayim sifirdan baslar,
 * kullanici da her geciste bir tur yukleme beklerdi.
 * ==================================================================
 */
export function ReservationPage() {
  const { reservationId = '' } = useParams()
  const navigate = useNavigate()
  const queryClient = useQueryClient()

  const [payment, setPayment] = useState<PaymentDto | null>(null)

  const reservationQuery = useQuery({
    queryKey: ['reservation', reservationId],
    queryFn: () => bookingApi.getReservation(reservationId),
    enabled: reservationId.length > 0,

    // Bu veri HIC bayatlamamali.
    //
    // App.tsx'teki 60 saniyelik varsayilan burada tehlikeli olurdu:
    // kullanici sekmeye geri dondugunde bir dakikalik eski
    // "remainingSeconds" degeriyle sayaci baslatirdik ve sure
    // oldugundan uzun gorunurdu.
    staleTime: 0,
  })

  const reservation = reservationQuery.data

  // Geri sayim SUNUCUNUN verdigi saniyeden basliyor.
  // Detayli gerekce useCountdown icinde.
  const remaining = useCountdown(reservation?.remainingSeconds)

  // ================================================================
  // "OLU" REZERVASYON UC HALDE OLUR -- VE UCU AYNI SEY DEGIL
  // ================================================================
  // Ilk yazimimda ucunu tek bir `isExpired` degiskeninde toplamistim.
  // Tarayicida denerken hatayi gordum: odemeyi "basarisiz" olarak
  // isaretleyince ekranda "Rezervasyon suresi doldu" yaziyor ve
  // sayac hala 09:42'den geri sayiyordu.
  //
  // Oysa rezervasyon IPTAL olmustu, suresi dolmamisti. Kullaniciya
  // yanlis sebebi soylemek, "neden bilet alamadim?" sorusunu
  // cevapsiz birakir -- hatta destek talebine yol acar.
  //
  // Ayirt etmek zorundayiz.
  // ================================================================
  const isCancelled = reservation?.status === ReservationStatus.Cancelled

  const isTimedOut =
    reservation?.status === ReservationStatus.Expired ||
    (reservation !== undefined && !isCancelled && remaining === 0)

  /** Bu rezervasyonla artik hicbir sey yapilamaz. */
  const isDead = isCancelled || isTimedOut

  const isConfirmed = reservation?.status === ReservationStatus.Confirmed

  // ----------------------------------------------------------------
  // SURE DOLDUGUNDA SUNUCUYLA TEYITLES
  // ----------------------------------------------------------------
  // Sayac sifira dustugunde ekranda "sureniz doldu" yaziyoruz ama
  // bu YALNIZCA istemcinin tahmini. Gercek karari veren sunucu.
  //
  // Bu yuzden sifira dusunce bir kez daha soruyoruz. Ornegin
  // kullanici baska bir sekmede sureyi uzatmis olabilir; o zaman
  // sunucu yeni sureyi doner ve sayac devam eder.
  //
  // useRef ile "bir kez" garantisi: olmasaydi her render'da yeni
  // istek gider, sonsuz donguye girerdik.
  // ----------------------------------------------------------------
  const expiryCheckedRef = useRef(false)

  useEffect(() => {
    if (remaining === 0 && reservation && !expiryCheckedRef.current) {
      expiryCheckedRef.current = true
      void reservationQuery.refetch()
    }

    if (remaining > 0) {
      expiryCheckedRef.current = false
    }
  }, [remaining, reservation, reservationQuery])

  // ================================================================
  // ODEME BASLAT
  // ================================================================
  const paymentKeyRef = useRef<string | null>(null)

  const startPayment = useMutation({
    mutationFn: () => {
      // Anahtar rezervasyona bagli ve SABIT kaliyor.
      //
      // Kullanici "Odemeye gec"e iki kez basarsa ikinci istek ayni
      // anahtarla gider ve backend yeni bir odeme kaydi olusturmaz --
      // ilkini doner. Cift cekim riski boylece istemci tarafinda da
      // kapaniyor.
      paymentKeyRef.current ??= newIdempotencyKey()

      return bookingApi.createPayment(reservationId, paymentKeyRef.current)
    },
    onSuccess: setPayment,
  })

  // ================================================================
  // ODEMEYI TAMAMLA (SIMULASYON)
  // ================================================================
  const completePayment = useMutation({
    mutationFn: (paymentId: string) => bookingApi.completePayment(paymentId),

    onSuccess: (result) => {
      setPayment(result)

      // Biletler ARTIK olustu; bilet listesi onbellegi bayat.
      // invalidate etmezsek kullanici "Biletlerim"e gidince eski
      // (biletsiz) listeyi gorurdu.
      void queryClient.invalidateQueries({ queryKey: ['my-tickets'] })
      void queryClient.invalidateQueries({ queryKey: ['reservation', reservationId] })

      navigate('/biletlerim?yeni=1')
    },
  })

  const failPayment = useMutation({
    mutationFn: (paymentId: string) =>
      bookingApi.failPayment(paymentId, 'Kullanici odemeyi tamamlamadi (simulasyon).'),

    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: ['reservation', reservationId] })
      setPayment(null)
    },
  })

  const cancelReservation = useMutation({
    mutationFn: () => bookingApi.cancelReservation(reservationId, 'Kullanici vazgecti.'),
    onSuccess: () => navigate('/etkinlikler'),
  })

  const extendReservation = useMutation({
    mutationFn: () => bookingApi.extendReservation(reservationId),
    onSuccess: (updated) => {
      // Sunucudan gelen yeni remainingSeconds onbellege yaziliyor;
      // useCountdown bagimliligi degistigi icin sayac kendiliginden
      // yeni sureden baslar.
      queryClient.setQueryData(['reservation', reservationId], updated)
    },
  })

  if (reservationQuery.isPending) {
    return (
      <div className="min-h-screen bg-slate-50">
        <SiteHeader />
        <div className="mx-auto max-w-3xl px-4 py-8">
          <div className="h-80 animate-pulse rounded-2xl bg-slate-200" />
        </div>
      </div>
    )
  }

  if (reservationQuery.isError || !reservation) {
    return (
      <div className="min-h-screen bg-slate-50">
        <SiteHeader />
        <div className="mx-auto max-w-3xl px-4 py-8">
          <Alert variant="error">{toProblem(reservationQuery.error).detail}</Alert>
        </div>
      </div>
    )
  }

  // Son 60 saniyede sayaci kirmiziya cevirip nabiz veriyorum.
  // Renk degisimi, kullanicinin ekrandan gozunu ayirmisken bile
  // cevresel gorusuyle fark edebilecegi bir uyari.
  const isUrgent = remaining > 0 && remaining <= 60

  return (
    <div className="min-h-screen bg-slate-50">
      <SiteHeader />

      <main className="mx-auto max-w-3xl px-4 py-8">
        <h1 className="text-2xl font-bold text-slate-900">Rezervasyon</h1>
        <p className="mt-1 text-sm text-slate-500">
          Kod: <span className="font-mono">{reservation.reservationCode}</span>
        </p>

        {/* ---- GERI SAYIM ----
             Yalnizca rezervasyon HALA CANLIYKEN gosteriliyor.
             Iptal edilmis bir rezervasyonun yaninda geri sayan bir
             sayac, kullaniciya "hala vaktin var" diye yalan soyler. */}
        {!isConfirmed && !isDead && (
          <div
            className={`mt-6 flex flex-wrap items-center justify-between gap-4 rounded-2xl border p-5 ${
              isUrgent ? 'border-red-200 bg-red-50' : 'border-amber-200 bg-amber-50'
            }`}
          >
            <div>
              <p className="text-sm font-medium text-slate-700">Odeme icin kalan sure</p>

              <p
                className={`mt-1 font-mono text-3xl font-bold tabular-nums ${
                  isUrgent ? 'text-red-600' : 'text-amber-700'
                }`}
                // role="timer" + aria-live="off": ekran okuyucu her
                // saniye konusmasin. Saniyede bir okunan bir sayac
                // ekran okuyucu kullanicisi icin kullanilamaz olurdu.
                // Kritik uyariyi asagidaki metin veriyor.
                role="timer"
                aria-live="off"
              >
                {formatCountdown(remaining)}
              </p>
            </div>

            <div className="flex gap-2">
              <Button
                variant="secondary"
                // Uzatma bir KEZ yapilabiliyor (backend kurali).
                // Butonu pasiflestirmek, kullanicinin deneyip hata
                // almasindan iyi.
                disabled={reservation.extensionCount > 0}
                isLoading={extendReservation.isPending}
                onClick={() => extendReservation.mutate()}
              >
                {reservation.extensionCount > 0 ? 'Sure uzatildi' : '5 dakika uzat'}
              </Button>

              <Button
                variant="ghost"
                isLoading={cancelReservation.isPending}
                onClick={() => cancelReservation.mutate()}
              >
                Vazgec
              </Button>
            </div>
          </div>
        )}

        {/* ---- OLU REZERVASYON ----
             SEBEBI dogru soylemek onemli. "Suresi doldu" ile "iptal
             edildi" kullanici icin farkli seyler: birincisinde
             "geciktim", ikincisinde "ben (veya odeme) iptal ettim".
             Yanlis sebep, kullanicinin ne yaptigini anlamasini
             engeller. */}
        {isDead && !isConfirmed && (
          <div className="mt-6">
            <Alert variant="error">
              <p className="font-medium">
                {isCancelled ? 'Bu rezervasyon iptal edildi' : 'Rezervasyon suresi doldu'}
              </p>

              <p className="mt-1">
                Koltuklariniz serbest birakildi ve tekrar satisa cikti. Yeniden
                secim yapmak icin{' '}
                <Link
                  to={`/oturumlar/${reservation.eventSessionId}/koltuklar`}
                  className="font-medium underline"
                >
                  koltuk secim ekranina
                </Link>{' '}
                donebilirsiniz.
              </p>
            </Alert>
          </div>
        )}

        {/* ---- OZET ---- */}
        <section className="mt-6 rounded-2xl border border-slate-200 bg-white p-6 shadow-sm">
          <h2 className="font-semibold text-slate-900">{reservation.eventTitle}</h2>
          <p className="mt-1 text-sm text-slate-500">
            {formatDateTime(reservation.sessionStartDate)} &middot; {reservation.venueName}
          </p>

          <ul className="mt-4 divide-y divide-slate-100">
            {reservation.items.map((item) => (
              <li key={item.id} className="flex items-center justify-between py-2 text-sm">
                <div>
                  <p className="font-medium text-slate-900">{item.seatLabel}</p>
                  <p className="text-xs text-slate-500">
                    {item.sectionName} &middot; {item.ticketTypeName}
                  </p>
                </div>
                <span className="text-slate-700">
                  {formatMoney(item.unitPrice, item.currency)}
                </span>
              </li>
            ))}
          </ul>

          <div className="mt-4 flex items-center justify-between border-t border-slate-200 pt-4">
            <span className="text-sm text-slate-500">Toplam</span>
            <span className="text-xl font-semibold text-slate-900">
              {formatMoney(reservation.totalAmount, reservation.currency)}
            </span>
          </div>
        </section>

        {/* ---- ODEME ---- */}
        {isConfirmed ? (
          <div className="mt-6">
            <Alert variant="success">
              Odemeniz alindi ve biletleriniz olusturuldu.{' '}
              <Link to="/biletlerim" className="font-medium underline">
                Biletlerime git
              </Link>
            </Alert>
          </div>
        ) : (
          !isDead && (
            <section className="mt-6 rounded-2xl border border-slate-200 bg-white p-6 shadow-sm">
              <h2 className="font-semibold text-slate-900">Odeme</h2>

              {startPayment.isError && (
                <div className="mt-3">
                  <Alert variant="error">{toProblem(startPayment.error).detail}</Alert>
                </div>
              )}

              {completePayment.isError && (
                <div className="mt-3">
                  <Alert variant="error">{toProblem(completePayment.error).detail}</Alert>
                </div>
              )}

              {payment === null ? (
                <>
                  <p className="mt-2 text-sm text-slate-500">
                    Odemeye gectiginizde koltuklariniz odeme suresince
                    kilitli kalir.
                  </p>

                  <Button
                    className="mt-4 w-full"
                    isLoading={startPayment.isPending}
                    onClick={() => startPayment.mutate()}
                  >
                    {formatMoney(reservation.totalAmount, reservation.currency)} ode
                  </Button>
                </>
              ) : (
                <PaymentSimulation
                  payment={payment}
                  isCompleting={completePayment.isPending}
                  isFailing={failPayment.isPending}
                  onComplete={() => completePayment.mutate(payment.id)}
                  onFail={() => failPayment.mutate(payment.id)}
                />
              )}
            </section>
          )
        )}
      </main>
    </div>
  )
}

interface PaymentSimulationProps {
  payment: PaymentDto
  isCompleting: boolean
  isFailing: boolean
  onComplete: () => void
  onFail: () => void
}

/**
 * ==================================================================
 * ODEME SIMULASYONU -- PDF Sprint 8
 * ==================================================================
 * PDF: "Gercek bir odeme saglayicisi entegre edilmeyecektir. Ancak
 * gercek bir entegrasyona benzer bir yapi kurulmalidir."
 *
 * ------------------------------------------------------------------
 * NEDEN SAHTE BIR KART FORMU KOYMUYORUM?
 * ------------------------------------------------------------------
 * Ilk aklima gelen, gercekci gorunsun diye kart numarasi alanlari
 * olan bir form cizmekti. VAZGECTIM, iki sebeple:
 *
 * 1) Gercek bir entegrasyonda kart bilgisi BIZIM sayfamiza HIC
 *    girilmez. Kullanici saglayicinin (Iyzico, Stripe) kendi
 *    sayfasina yonlendirilir veya iframe icinde onun formunu
 *    doldurur. Kart verisi bizim sunucumuza ugramaz -- PCI-DSS
 *    zorunlulugu budur. Kart formu cizmek, ogrenilmesi GEREKEN
 *    seyin tam tersini ogretirdi.
 *
 * 2) Sahte de olsa kart alani gosteren bir ekran, birinin oraya
 *    GERCEK kart numarasi yazmasina davetiye cikarir.
 *
 * Bunun yerine simulasyonun ne oldugunu acikca yaziyorum ve iki
 * sonucu da denenebilir kiliyorum -- basarisiz odeme yolu en az
 * basarili yol kadar test edilmeli.
 * ------------------------------------------------------------------
 * BU BUTONLAR GERCEKTE KIM?
 * ------------------------------------------------------------------
 * "Odemeyi onayla" butonu POST /payments/{id}/complete cagiriyor.
 * Gercek hayatta bu adresi KULLANICI degil, SAGLAYICI cagirir
 * (callback / webhook).
 *
 * Backend bunu bildigi icin callback'e koru korune guvenmiyor:
 * islemi saglayiciya sorup DOGRULUYOR (VerifyPaymentAsync). Yani
 * bu butona basmak "bilet ver" demek degil, "saglayici bize haber
 * verdi" demek. Dogrulama gecmezse bilet uretilmiyor.
 * ==================================================================
 */
function PaymentSimulation({
  payment,
  isCompleting,
  isFailing,
  onComplete,
  onFail,
}: PaymentSimulationProps) {
  if (payment.status === PaymentStatus.Failed) {
    return (
      <div className="mt-3">
        <Alert variant="error">
          Odeme basarisiz oldu. {payment.failureReason}
        </Alert>
      </div>
    )
  }

  return (
    <div className="mt-3 rounded-xl border border-dashed border-slate-300 bg-slate-50 p-5">
      <p className="text-xs font-semibold uppercase tracking-wide text-slate-500">
        Odeme simulasyonu
      </p>

      <p className="mt-2 text-sm text-slate-600">
        Gercek bir odeme saglayicisi kullanilmiyor. Normalde bu adimda
        saglayicinin guvenli sayfasina yonlendirilir, kart bilgilerinizi
        ORAYA girer ve saglayici sonucu bize bildirirdi. Asagidaki iki
        buton o bildirimi taklit ediyor.
      </p>

      <dl className="mt-4 space-y-1 text-xs text-slate-500">
        <div className="flex gap-2">
          <dt>Saglayici:</dt>
          <dd className="font-medium text-slate-700">{payment.providerName}</dd>
        </div>
        <div className="flex gap-2">
          <dt>Islem referansi:</dt>
          <dd className="font-mono text-slate-700">{payment.providerReference ?? '-'}</dd>
        </div>
        <div className="flex gap-2">
          <dt>Tutar:</dt>
          <dd className="font-medium text-slate-700">
            {formatMoney(payment.amount, payment.currency)}
          </dd>
        </div>
      </dl>

      <div className="mt-5 flex flex-wrap gap-3">
        <Button className="flex-1" isLoading={isCompleting} onClick={onComplete}>
          Odeme basarili
        </Button>

        <Button
          variant="secondary"
          className="flex-1"
          isLoading={isFailing}
          onClick={onFail}
        >
          Odeme basarisiz
        </Button>
      </div>

      <p className="mt-3 text-xs text-slate-500">
        Odeme basarisiz olursa rezervasyon iptal edilir ve koltuklar
        serbest birakilir (PDF Sprint 8 kurali).
      </p>
    </div>
  )
}
