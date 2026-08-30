import { useEffect, useRef, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { SiteHeader } from '../../../components/layout/SiteHeader'
import { Alert } from '../../../components/ui/Alert'
import { Button } from '../../../components/ui/Button'
import { toProblem } from '../../../lib/api/client'
import { formatDateTime, formatMoney } from '../../../lib/format'
import { useCountdown } from '../hooks/useCountdown'
import { ReservationCountdown } from '../components/ReservationCountdown'
import {
  bookingApi,
  newIdempotencyKey,
  PaymentStatus,
  ReservationStatus,
  type PaymentDto,
} from '../api/bookingApi'

/**
 * REZERVASYON VE ÖDEME EKRANI -- PDF Sprint 7 + Sprint 8
 *
 * Iki sprintin frontend'i tek sayfada bulusuyor çünkü kullanıcı
 * acisindan bunlar tek bir an: "koltugum tutuldu, süreyi kaybetmeden
 * odeyeyim". Ayrı sayfalara bolseydik geri sayım sıfırdan başlar,
 * kullanıcı da her geciste bir tur yukleme beklerdi.
 *
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

    // Bu veri HİÇ bayatlamamali.
    //
    // App.tsx'teki 60 saniyelik varsayılan burada tehlikeli olurdu:
    // kullanıcı sekmeye geri dondugunde bir dakikalik eski
    // "remainingSeconds" degeriyle sayaci baslatirdik ve süre
    // oldugundan uzun görünürdü.
    staleTime: 0,
  })

  const reservation = reservationQuery.data

  // Geri sayım SUNUCUNUN verdiği saniyeden başlıyor.
  // Detayli gerekce useCountdown içinde.
  const remaining = useCountdown(reservation?.remainingSeconds)

  // "OLU" REZERVASYON UC HALDE OLUR -- VE UCU AYNI SEY DEĞİL
  //
  // İlk yazimimda ucunu tek bir `isExpired` degiskeninde toplamistim.
  // Tarayicida denerken hatayi gordum: ödemeyi "başarısız" olarak
  // isaretleyince ekranda "Rezervasyon süresi doldu" yazıyor ve
  // sayaç hâlâ 09:42'den geri sayiyordu.
  //
  // Oysa rezervasyon İPTAL olmustu, süresi dolmamisti. Kullanıcıya
  // yanlış sebebi söylemek, "neden bilet alamadim?" sorusunu
  // cevapsiz birakir -- hatta destek talebine yol acar.
  //
  // Ayırt etmek zorundayız.
  const isCancelled = reservation?.status === ReservationStatus.Cancelled

  const isTimedOut =
    reservation?.status === ReservationStatus.Expired ||
    (reservation !== undefined && !isCancelled && remaining === 0)

  /** Bu rezervasyonla artık hiçbir sey yapılamaz. */
  const isDead = isCancelled || isTimedOut

  const isConfirmed = reservation?.status === ReservationStatus.Confirmed

  // Sure doldugunda sunucuyla teyitles
  //
  // Sayac sıfıra dustugunde ekranda "süreniz doldu" yazıyorum ama
  // bu YALNIZCA istemcinin tahmini. Gerçek karari veren sunucu.
  //
  // Bu yuzden sıfıra dusunce bir kez daha soruyorum. Ornegin
  // kullanıcı başka bir sekmede süreyi uzatmis olabilir; o zaman
  // sunucu yeni süreyi döner ve sayaç devam eder.
  //
  // useRef ile "bir kez" garantisi: olmasaydı her render'da yeni
  // istek gider, sonsuz donguye girerdik.
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

  // Ödeme baslat
  const paymentKeyRef = useRef<string | null>(null)

  const startPayment = useMutation({
    mutationFn: () => {
      // Anahtar rezervasyona bağlı ve SABIT kaliyor.
      //
      // Kullanıcı "Ödemeye geç"e iki kez basarsa ikinci istek aynı
      // anahtarla gider ve backend yeni bir ödeme kaydı olusturmaz --
      // ilkini döner. Cift çekim riski boylece istemci tarafında da
      // kapaniyor.
      paymentKeyRef.current ??= newIdempotencyKey()

      return bookingApi.createPayment(reservationId, paymentKeyRef.current)
    },
    onSuccess: setPayment,
  })

  // Odemeyi tamamla (simulasyon)
  const completePayment = useMutation({
    mutationFn: (paymentId: string) => bookingApi.completePayment(paymentId),

    onSuccess: (result) => {
      setPayment(result)

      // Biletler ARTIK oluştu; bilet listesi önbelleği bayat.
      // invalidate etmezsek kullanıcı "Biletlerim"e gidince eski
      // (biletsiz) listeyi gorurdu.
      void queryClient.invalidateQueries({ queryKey: ['my-tickets'] })
      void queryClient.invalidateQueries({ queryKey: ['reservation', reservationId] })

      navigate('/biletlerim?yeni=1')
    },
  })

  const failPayment = useMutation({
    mutationFn: (paymentId: string) =>
      bookingApi.failPayment(paymentId, 'Kullanıcı ödemeyi tamamlamadı (simülasyon).'),

    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: ['reservation', reservationId] })
      setPayment(null)
    },
  })

  const cancelReservation = useMutation({
    mutationFn: () => bookingApi.cancelReservation(reservationId, 'Kullanıcı vazgeçti.'),
    onSuccess: () => navigate('/etkinlikler'),
  })

  const extendReservation = useMutation({
    mutationFn: () => bookingApi.extendReservation(reservationId),
    onSuccess: (updated) => {
      // Sunucudan gelen yeni remainingSeconds onbellege yaziliyor;
      // useCountdown bagimliligi değiştigi için sayaç kendiliginden
      // yeni sureden başlar.
      queryClient.setQueryData(['reservation', reservationId], updated)
    },
  })

  if (reservationQuery.isPending) {
    return (
      <div className="min-h-screen bg-slate-100">
        <SiteHeader />
        <div className="mx-auto max-w-3xl px-4 py-8">
          <div className="h-80 animate-pulse rounded-[4px] bg-slate-200" />
        </div>
      </div>
    )
  }

  if (reservationQuery.isError || !reservation) {
    return (
      <div className="min-h-screen bg-slate-100">
        <SiteHeader />
        <div className="mx-auto max-w-3xl px-4 py-8">
          <Alert variant="error">{toProblem(reservationQuery.error).detail}</Alert>
        </div>
      </div>
    )
  }

  return (
    <div className="min-h-screen bg-slate-100">
      <SiteHeader />

      <main className="mx-auto max-w-3xl px-4 py-8">
        <h1 className="font-display text-2xl font-bold tracking-tight text-slate-900">
          Rezervasyon
        </h1>
        <p className="mt-1 text-sm text-slate-500">
          Kod: <span className="num text-slate-900">{reservation.reservationCode}</span>
        </p>

        {/* ---- GERİ SAYIM ----
             Yalnızca rezervasyon HÂLÂ CANLIYKEN gösteriliyor.
             İptal edilmiş bir rezervasyonun yanında geri sayan bir
             sayaç, kullanıcıya "hâlâ vaktin var" diye yalan söyler. */}
        {!isConfirmed && !isDead && (
          <ReservationCountdown
            remaining={remaining}
            actions={
              <>
                <Button
                  variant="secondary"
                  // Uzatma bir KEZ yapılabiliyor (backend kuralı).
                  // Butonu pasifleştirmek, kullanıcının deneyip hata
                  // almasından iyi.
                  disabled={reservation.extensionCount > 0}
                  isLoading={extendReservation.isPending}
                  onClick={() => extendReservation.mutate()}
                >
                  {reservation.extensionCount > 0 ? 'Süre uzatıldı' : '5 dakika uzat'}
                </Button>

                <Button
                  variant="ghost"
                  isLoading={cancelReservation.isPending}
                  onClick={() => cancelReservation.mutate()}
                >
                  Vazgeç
                </Button>
              </>
            }
          />
        )}

        {/* ---- OLU REZERVASYON ----
             SEBEBI doğru söylemek önemli. "Süresi doldu" ile "iptal
             edildi" kullanıcı için farklı şeyler: birincisinde
             "geciktim", ikincisinde "ben (veya ödeme) iptal ettim".
             Yanlis sebep, kullanıcının ne yaptigini anlamasini
             engeller. */}
        {isDead && !isConfirmed && (
          <div className="mt-6">
            <Alert variant="error">
              <p className="font-medium">
                {isCancelled ? 'Bu rezervasyon iptal edildi' : 'Rezervasyon süresi doldu'}
              </p>

              <p className="mt-1">
                Koltuklarınız serbest bırakıldı ve tekrar satışa çıktı. Yeniden seçim yapmak için{' '}
                <Link
                  to={`/oturumlar/${reservation.eventSessionId}/koltuklar`}
                  className="font-medium underline"
                >
                  koltuk seçim ekranına
                </Link>{' '}
                dönebilirsiniz.
              </p>
            </Alert>
          </div>
        )}

        {/* ---- OZET ---- */}
        <section className="mt-6 rounded-[4px] border border-slate-300 bg-white p-6">
          <h2 className="font-display font-semibold text-slate-900">{reservation.eventTitle}</h2>
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
                <span className="text-slate-700">{formatMoney(item.unitPrice, item.currency)}</span>
              </li>
            ))}
          </ul>

          <div className="mt-4 flex items-center justify-between border-t border-slate-200 pt-4">
            <span className="text-sm text-slate-500">Toplam</span>
            <span className="font-display text-xl font-semibold tracking-tight text-slate-900">
              {formatMoney(reservation.totalAmount, reservation.currency)}
            </span>
          </div>
        </section>

        {/* ---- ÖDEME ---- */}
        {isConfirmed ? (
          <div className="mt-6">
            <Alert variant="success">
              Ödemeniz alındı ve biletleriniz oluşturuldu.{' '}
              <Link to="/biletlerim" className="font-medium underline">
                Biletlerime git
              </Link>
            </Alert>
          </div>
        ) : (
          !isDead && (
            <section className="mt-6 rounded-[4px] border border-slate-300 bg-white p-6">
              <h2 className="font-display font-semibold text-slate-900">Ödeme</h2>

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
                    Ödemeye geçtiğinizde koltuklarınız ödeme süresince kilitli kalır.
                  </p>

                  <Button
                    className="mt-4 w-full"
                    isLoading={startPayment.isPending}
                    onClick={() => startPayment.mutate()}
                  >
                    {formatMoney(reservation.totalAmount, reservation.currency)} öde
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
 * ÖDEME SIMULASYONU -- PDF Sprint 8
 *
 * PDF: "Gerçek bir ödeme sağlayıcısı entegre edilmeyecektir. Ancak
 * gerçek bir entegrasyona benzer bir yapi kurulmalidir."
 *
 * Neden sahte bir kart formu koymuyorum?
 *
 * İlk aklima gelen, gercekci gorunsun diye kart numarasi alanlari
 * olan bir form cizmekti. VAZGECTIM, iki sebeple:
 *
 * 1) Gerçek bir entegrasyonda kart bilgisi BENIM sayfamiza HİÇ
 *    girilmez. Kullanıcı sağlayıcının (Iyzico, Stripe) kendi
 *    sayfasına yönlendirilir veya iframe içinde onun formunu
 *    doldurur. Kart verisi benim sunucumuza ugramaz -- PCI-DSS
 *    zorunlulugu budur. Kart formu cizmek, ogrenilmesi GEREKEN
 *    seyin tam tersini ogretirdi.
 *
 * 2) Sahte de olsa kart alanı gosteren bir ekran, birinin oraya
 *    GERCEK kart numarasi yazmasina davetiye cikarir.
 *
 * Bunun yerine simulasyonun ne olduğunu acikca yazıyorum ve iki
 * sonucu da denenebilir kiliyorum -- başarısız ödeme yolu en az
 * başarılı yol kadar test edilmeli.
 *
 * Bu butonlar gercekte kim?
 *
 * "Ödemeyi onayla" butonu POST /payments/{id}/complete cagiriyor.
 * Gerçek hayatta bu adresi KULLANICI değil, SAGLAYICI cagirir
 * (callback / webhook).
 *
 * Backend bunu bildigi için callback'e koru korune guvenmiyor:
 * islemi saglayiciya sorup DOGRULUYOR (VerifyPaymentAsync). Yani
 * bu butona basmak "bilet ver" demek değil, "sağlayıcı bana haber
 * verdi" demek. Dogrulama gecmezse bilet uretilmiyor.
 *
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
        <Alert variant="error">Ödeme başarısız oldu. {payment.failureReason}</Alert>
      </div>
    )
  }

  return (
    <div className="mt-3 rounded-[4px] border border-slate-300 bg-slate-50 p-5">
      <p className="text-xs font-semibold uppercase tracking-wide text-slate-500">
        Ödeme simülasyonu
      </p>

      <p className="mt-2 text-sm text-slate-600">
        Gerçek bir ödeme sağlayıcısı kullanılmıyor. Normalde bu adımda sağlayıcının güvenli
        sayfasına yönlendirilir, kart bilgilerinizi ORAYA girer ve sağlayıcı sonucu bize bildirirdi.
        Aşağıdaki iki buton o bildirimi taklit ediyor.
      </p>

      <dl className="mt-4 space-y-1 text-xs text-slate-500">
        <div className="flex gap-2">
          <dt>Sağlayıcı:</dt>
          <dd className="font-medium text-slate-700">{payment.providerName}</dd>
        </div>
        <div className="flex gap-2">
          <dt>İşlem referansı:</dt>
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
          Ödeme başarılı
        </Button>

        <Button variant="secondary" className="flex-1" isLoading={isFailing} onClick={onFail}>
          Ödeme başarısız
        </Button>
      </div>

      <p className="mt-3 text-xs text-slate-500">
        Ödeme başarısız olursa rezervasyon iptal edilir ve koltuklar serbest bırakılır (PDF Sprint 8
        kuralı).
      </p>
    </div>
  )
}
