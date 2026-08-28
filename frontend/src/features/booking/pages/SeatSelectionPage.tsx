import { useCallback, useMemo, useRef, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useNavigate, useParams } from 'react-router-dom'
import { SiteHeader } from '../../../components/layout/SiteHeader'
import { ConnectionIndicator } from '../components/ConnectionIndicator'
import { useSeatHub } from '../hooks/useSeatHub'
import { SeatMap, type SeatMapSection } from '../../../components/seatmap/SeatMap'
import { Alert } from '../../../components/ui/Alert'
import { Button } from '../../../components/ui/Button'
import { toProblem } from '../../../lib/api/client'
import { formatDateTime, formatMoney } from '../../../lib/format'
import {
  bookingApi,
  EventSeatStatus,
  newIdempotencyKey,
  type SeatAvailability,
  type SeatAvailabilityItem,
} from '../api/bookingApi'

// ===================================================================
// KOLTUK DURUM RENKLERI
// ===================================================================
// Renkleri tek bir yerde topluyorum: hem harita hem de gosterge
// (legend) ayni degeri kullansin. Ayri ayri yazsaydik birini
// degistirip digerini unutmak kacinilmazdi.
//
// Renk secimi keyfi degil:
//   - Bos: notr gri-mavi. "Tiklanabilir" hissi verir.
//   - Secili: yesil. Olumlu, kullanicinin kendi eylemi.
//   - Kilitli: amber. "Simdilik degil" -- 10 dakika sonra bosalabilir.
//   - Satildi: koyu gri. Kalici, umut yok.
//
// ERISILEBILIRLIK NOTU: Yalnizca RENGE guvenmiyoruz. Her koltugun
// <title> etiketinde durumu METIN olarak da yaziyor; renk korlugu
// olan kullanici fareyle uzerine gelince veya ekran okuyucuyla
// durumu ogrenebiliyor.
// ===================================================================
const SEAT_COLORS = {
  available: '#cbd5e1',
  selected: '#16a34a',
  locked: '#fbbf24',
  sold: '#475569',
  blocked: '#e2e8f0',
} as const

function seatStatusLabel(status: number): string {
  switch (status) {
    case EventSeatStatus.Locked:
      return 'baskasi tarafindan tutuluyor'
    case EventSeatStatus.Sold:
      return 'satildi'
    case EventSeatStatus.Blocked:
      return 'satisa kapali'
    default:
      return 'bos'
  }
}

/**
 * ==================================================================
 * GORSEL KOLTUK SECIMI -- PDF Sprint 7
 * ==================================================================
 * PDF'in bu sprintten bekledikleri:
 *   - Gorsel koltuk secimi                    -> SeatMap
 *   - Koltuk kilitleme (10 dk)                -> POST /reservations
 *   - Cakisma bildirimi                       -> 409 yakalama + otomatik yenileme
 *   - Rezervasyon ozeti                       -> sag sutun
 * ==================================================================
 */
export function SeatSelectionPage() {
  const { sessionId = '' } = useParams()
  const navigate = useNavigate()
  const queryClient = useQueryClient()

  // Secili koltuklar. Set kullaniyorum, dizi degil.
  //
  // Sebep: "bu koltuk secili mi?" sorusu her koltuk icin, her
  // render'da soruluyor. Dizide includes() O(n); 2000 koltukluk bir
  // salonda 2000 x 2000 = 4 milyon karsilastirma demek. Set.has()
  // O(1).
  const [selected, setSelected] = useState<ReadonlySet<string>>(new Set())

  /** Etkinlik canli olarak iptal edildiyse gosterilecek uyari. */
  const [cancelledTitle, setCancelledTitle] = useState<string | null>(null)

  // ================================================================
  // GELEN OLAYI ONBELLEGE ISLE -- PDF: "Gercek zamanli koltuk
  // guncelleme"
  // ================================================================
  // Olay geldiginde sunucudan listeyi TEKRAR CEKMIYORUZ, elimizdeki
  // onbellegi YAMALIYORUZ.
  //
  // Neden? Populer bir konserde saniyede birkac olay gelir. Her
  // olayda tam listeyi cekseydik (2000 koltuk, ~200 KB) sunucuyu
  // yoklamadan bile beter yorardik -- SignalR'in butun kazanci
  // giderdi.
  //
  // Yamalama ile tek bir koltugun durumu degisiyor ve React
  // yalnizca o <rect>'i yeniden ciziyor.
  //
  // setQueryData ile YENI nesneler uretiyorum (yayma operatoru),
  // mevcut diziyi degistirmiyorum. Yerinde degistirseydim React
  // referansin ayni oldugunu gorup EKRANI HIC GUNCELLEMEZDI --
  // sessizce calismayan bir arayuz olurdu.
  // ================================================================
  const patchSeatStatus = useCallback(
    (eventSeatIds: string[], newStatus: number) => {
      queryClient.setQueryData<SeatAvailability>(['seat-availability', sessionId], (previous) => {
        if (!previous) {
          // Liste henuz yuklenmemis. Olayi atlamak guvenli:
          // birazdan gelecek ilk cekimde zaten guncel durum var.
          return previous
        }

        const hedef = new Set(eventSeatIds)
        let degisti = false

        const seats = previous.seats.map((seat) => {
          if (!hedef.has(seat.eventSeatId) || seat.status === newStatus) {
            return seat
          }

          degisti = true

          return { ...seat, status: newStatus }
        })

        // Hicbir sey degismediyse ESKI nesneyi aynen donuyorum.
        //
        // Yeni nesne donseydik React "veri degisti" deyip tum
        // koltuk haritasini yeniden hesaplardi -- 2000 koltuk
        // icin bosuna bir is.
        if (!degisti) {
          return previous
        }

        return {
          ...previous,
          seats,

          // Bos koltuk sayacini da guncelliyorum.
          //
          // Unutsaydik baslikta "65 / 68 koltuk bos" yazarken
          // haritada 60 bos koltuk gorunurdu. Kucuk ama
          // kullanicinin sisteme guvenini sarsan turden bir
          // tutarsizlik.
          availableSeats: seats.filter((x) => x.status === EventSeatStatus.Available).length,
        }
      })
    },
    [queryClient, sessionId],
  )

  const hubStatus = useSeatHub(sessionId || undefined, {
    onSeatsLocked: (ids) => patchSeatStatus(ids, EventSeatStatus.Locked),
    onSeatsReleased: (ids) => patchSeatStatus(ids, EventSeatStatus.Available),
    onSeatsSold: (ids) => patchSeatStatus(ids, EventSeatStatus.Sold),

    // ReservationExpired bu ekranda haritayi ETKILEMIYOR.
    //
    // Cunku sunucu ayni anda SeatReleased de gonderiyor ve koltuklari
    // asil bosaltan o. Burada ikinci kez islem yapmak gereksiz olurdu.
    //
    // Peki neden dinliyoruz? Cunku olay, rezervasyon SAHIBI icin
    // anlamli: kendi rezervasyonunun bittigini ogreniyor. O ekran
    // (ReservationPage) sunucunun verdigi saniyeden geri sayiyor ve
    // sifirlaninca sunucuya soruyor -- ayni sonuca oradan variyor.
    onReservationExpired: () => {},

    // PDF olayi: EventCancelled.
    //
    // Kullanici tam koltuk secerken etkinlik iptal edilirse, secime
    // devam etmesinin anlami yok. Uyariyi ANINDA gostermek, bosuna
    // koltuk secip rezervasyonda hata almasindan iyi.
    onEventCancelled: (title) => setCancelledTitle(title),

    // PDF: "Guncel koltuk listesini yeniden cekme"
    //
    // Baglanti kopukken gecen surede kacirdigimiz olaylar var ve
    // SignalR onlari biriktirmiyor. Yamalama ile telafi edemeyiz --
    // neyi kacirdigimizi bilmiyoruz. Tek dogru yol tam listeyi
    // bastan cekmek.
    onReconnected: () => {
      void queryClient.invalidateQueries({ queryKey: ['seat-availability', sessionId] })
    },
  })

  const availabilityQuery = useQuery({
    queryKey: ['seat-availability', sessionId],
    queryFn: () => bookingApi.getSeatAvailability(sessionId),
    enabled: sessionId.length > 0,

    // ==============================================================
    // YOKLAMA ARTIK ASIL YOL DEGIL, YEDEK -- PDF Sprint 10
    // ==============================================================
    // Sprint 7'de buraya sabit 10 saniyelik bir yoklama koymus ve
    // su notu birakmistim:
    //
    //   "Bu bir GECICI cozum. Sprint 10'da SignalR gelecek ve
    //    o zaman bu satir KALDIRILACAK."
    //
    // Sprint 10 geldi ve satiri TAMAMEN KALDIRMADIM. Fikrimi
    // degistiren sey su: SignalR baglantisi HER ZAMAN kurulamiyor.
    // Kurumsal aglar WebSocket'i engelleyebiliyor, vekil sunucular
    // uzun baglantilari kesebiliyor, kullanicinin interneti
    // gidebiliyor.
    //
    // Yoklamayi tamamen silseydik, bu durumlarda koltuk haritasi
    // TAMAMEN DONARDI -- Sprint 7'deki halinden bile kotu olurdu.
    //
    // Cozum: yoklama SignalR calisirken KAPALI, calismazken ACIK.
    //
    //   canli baglanti var  -> false (yoklama yok, olaylar geliyor)
    //   canli baglanti yok  -> 10 saniye (Sprint 7 davranisi)
    //
    // Yani en iyi durumda gercek zamanli, en kotu durumda eskisi
    // kadar iyi. "Zarif bozulma" (graceful degradation) denen sey.
    // ==============================================================
    refetchInterval: hubStatus === 'connected' ? false : 10_000,

    // staleTime'i 0'a cekiyorum. App.tsx'te varsayilan 60 saniye ve
    // o deger burada YANLIS olurdu: yenileme istegi gitse bile
    // "veri hala taze" denip sonuc yok sayilabilirdi.
    staleTime: 0,
  })

  const seats = useMemo(() => availabilityQuery.data?.seats ?? [], [availabilityQuery.data])

  // ================================================================
  // CAKISMA TESPITI -- PDF Sprint 7: "Cakisma bildirimi"
  // ================================================================
  // Her yenilemeden sonra soruyoruz: sectigim koltuklardan biri
  // artik bos degil mi?
  //
  // Bu, kullanicinin kaybettigi koltugu SESSIZCE secili gostermeyi
  // engelliyor. Aksi halde kullanici "Koltuklari ayirt"a basar,
  // 409 alir ve neden oldugunu anlamaz. Kotu haberi erken vermek,
  // gec vermekten iyidir.
  //
  // ----------------------------------------------------------------
  // NEDEN useEffect DEGIL?
  // ----------------------------------------------------------------
  // Ilk yazimimda bunu bir effect icinde yapip kaybedilen koltuklari
  // setSelected ile state'ten siliyordum. Calisiyordu ama yanlis
  // yontemdi: kullanici hicbir sey YAPMADIGI halde state
  // degistiriyordu ve React fazladan bir render turu doneyordu.
  //
  // Kaybedilen koltuk, `selected` ile `seats`in bir SONUCU --
  // bagimsiz bir bilgi degil. Sonuc olan seyi state'te tutmak,
  // ayni gercegi iki yerde saklamak demek; ikisi kacinilmaz olarak
  // birbirinden ayrilir.
  //
  // Bu yuzden render sirasinda HESAPLIYORUM. `selected` icinde eski
  // kimlikler kalabilir ama hicbir yerde dogrudan kullanilmiyor;
  // her tuketici asagidaki `activeSelected`i okuyor.
  // ================================================================
  const lostSeats = useMemo(
    () =>
      seats.filter((s) => selected.has(s.eventSeatId) && s.status !== EventSeatStatus.Available),
    [seats, selected],
  )

  const lostIds = useMemo(() => new Set(lostSeats.map((s) => s.eventSeatId)), [lostSeats])

  const activeSelected = useMemo<ReadonlySet<string>>(
    () => (lostIds.size === 0 ? selected : new Set([...selected].filter((id) => !lostIds.has(id)))),
    [selected, lostIds],
  )

  /**
   * ----------------------------------------------------------------
   * IDEMPOTENCY ANAHTARININ OMRU
   * ----------------------------------------------------------------
   * Anahtar, SECIME bagli. Kullanici ayni koltuklarla ikinci kez
   * gonderirse (butona iki kez basti, ag koptu ve tekrar denedi)
   * AYNI anahtar gider ve backend ikinci rezervasyonu olusturmaz.
   *
   * Ama kullanici secimi DEGISTIRIRSE bu artik bambaska bir istek --
   * eski anahtarla gonderirsek backend "bunu zaten yaptim" deyip
   * ESKI rezervasyonu dondururdu ve kullanici yanlis koltuklari
   * satin alirdi. Bu yuzden secim degisince anahtari sifirliyorum.
   *
   * useRef kullaniyorum, useState degil: bu deger ekranda
   * gorunmuyor, degismesi yeniden cizim gerektirmiyor.
   * ----------------------------------------------------------------
   */
  const idempotencyKeyRef = useRef<string | null>(null)

  const toggleSeat = (eventSeatId: string) => {
    idempotencyKeyRef.current = null

    // ==============================================================
    // NEDEN FONKSIYONEL GUNCELLEME? -- TARAYICIDA YAKALADIGIM HATA
    // ==============================================================
    // Ilk yazimim soyleydi:
    //
    //     const next = new Set(activeSelected)   // <-- HATALI
    //     ...
    //     setSelected(next)
    //
    // Tek tek tiklamada calisiyordu. Ama tarayicida ucunu ARKA ARKAYA
    // tiklayinca yalnizca SONUNCUSU secili kaldi.
    //
    // Sebep: React ayni tur icindeki state guncellemelerini TOPLUYOR.
    // Uc cagri da ayni render'in `activeSelected` degerini goruyor --
    // yani ucu de BOS kumeden turetiliyor ve birbirini eziyor.
    //
    // `setSelected(prev => ...)` ile React bize O ANKI degeri veriyor;
    // ikinci cagri birincinin sonucunu goruyor. Hizli tiklamada da
    // dogru calisiyor.
    //
    // Kaybedilen koltuklari `lostIds` ile burada temizliyorum:
    // `prev` guncel olmali ama `lostIds` sunucu verisinden geliyor ve
    // tiklama anindaki degeri dogru -- ikisini karistirmamak onemli.
    // ==============================================================
    setSelected((prev) => {
      const next = new Set([...prev].filter((id) => !lostIds.has(id)))

      if (next.has(eventSeatId)) {
        next.delete(eventSeatId)
      } else {
        next.add(eventSeatId)
      }

      return next
    })
  }

  /** Cakisma uyarisini kapatir: kaybedilen koltuklari secimden duser. */
  const dismissConflict = () =>
    setSelected((prev) => new Set([...prev].filter((id) => !lostIds.has(id))))

  const createReservation = useMutation({
    mutationFn: () => {
      idempotencyKeyRef.current ??= newIdempotencyKey()

      return bookingApi.createReservation(
        { eventSessionId: sessionId, eventSeatIds: [...activeSelected] },
        idempotencyKeyRef.current,
      )
    },

    onSuccess: (reservation) => {
      // Rezervasyon detayini onbellege ELIMIZLE koyuyorum.
      //
      // Yonlendirdigimiz sayfa ayni veriyi isteyecek. Onbellege
      // koymasaydik o sayfa acilir acilmaz bos bir iskelet gosterip
      // yeni bir istek atardi -- oysa veri elimizde.
      //
      // Bu ozellikle onemli cunku GERI SAYIM o sayfada basliyor;
      // fazladan bir gidis-donus, sayacin gec baslamasi demekti.
      queryClient.setQueryData(['reservation', reservation.id], reservation)

      navigate(`/rezervasyonlar/${reservation.id}`)
    },

    onError: () => {
      // Hata ne olursa olsun haritayi tazele.
      //
      // 409 aldiysak koltugu baskasi kapmis demektir; kullaniciya
      // guncel durumu gostermeliyiz. Yenilemeseydik kullanici ayni
      // dolu koltukla tekrar tekrar denerdi.
      void queryClient.invalidateQueries({ queryKey: ['seat-availability', sessionId] })
    },
  })

  // ================================================================
  // HARITA VERISINI HAZIRLA
  // ================================================================
  // Backend koltuklari DUZ bir liste olarak donuyor; harita ise
  // bolumlere gruplanmis istiyor. Ceviriyi burada yapiyorum.
  //
  // useMemo SART: bu hesap 2000 koltuk icin yeni nesneler uretiyor.
  // Sarmasaydik, her saniye (geri sayim, fare hareketi, herhangi bir
  // state degisikligi) yeniden calisir ve SeatMap'in kendi useMemo'su
  // da bosa cikardi -- cunku ona her seferinde YENI bir dizi
  // gonderirdik.
  // ================================================================
  const mapSections = useMemo<SeatMapSection[]>(() => {
    const bySection = new Map<string, { name: string; seats: SeatAvailabilityItem[] }>()

    for (const seat of seats) {
      const existing = bySection.get(seat.sectionId)

      if (existing) {
        existing.seats.push(seat)
      } else {
        bySection.set(seat.sectionId, { name: seat.sectionName, seats: [seat] })
      }
    }

    return [...bySection.entries()].map(([sectionId, section], index) => ({
      id: sectionId,
      name: section.name,
      // Backend bolum sirasini bu uc noktada dondurmuyor; listedeki
      // gorulme sirasini kullaniyorum. Backend koltuklari bolum ve
      // sira etiketine gore sirali donduruyor, bu yuzden sonuc
      // tutarli.
      displayOrder: index,
      seats: section.seats.map((seat) => ({
        id: seat.eventSeatId,
        rowLabel: seat.rowLabel,
        seatNumber: seat.seatNumber,
        label: seat.displayLabel,
        fill: activeSelected.has(seat.eventSeatId)
          ? SEAT_COLORS.selected
          : seat.status === EventSeatStatus.Available
            ? SEAT_COLORS.available
            : seat.status === EventSeatStatus.Locked
              ? SEAT_COLORS.locked
              : seat.status === EventSeatStatus.Sold
                ? SEAT_COLORS.sold
                : SEAT_COLORS.blocked,

        // Yalnizca BOS koltuk tiklanabilir.
        //
        // Kilitli koltugu tiklatmamak bilincli: kullanici "10 dakika
        // sonra bosalir" diye bekleyemez, o sirada baskasi odemeyi
        // tamamlamis olabilir. Umut vermek yerine net olmak daha iyi.
        selectable: seat.status === EventSeatStatus.Available,
        description: `${seat.ticketTypeName}, ${formatMoney(seat.price, seat.currency)} - ${seatStatusLabel(seat.status)}`,
      })),
    }))
  }, [seats, activeSelected])

  // Secili koltuklarin detaylari -- ozet paneli icin.
  const selectedSeats = useMemo(
    () => seats.filter((s) => activeSelected.has(s.eventSeatId)),
    [seats, activeSelected],
  )

  const total = selectedSeats.reduce((sum, s) => sum + s.price, 0)
  const currency = selectedSeats[0]?.currency ?? 'TRY'

  const problem = createReservation.isError ? toProblem(createReservation.error) : null

  return (
    <div className="min-h-screen bg-slate-50">
      <SiteHeader />

      <main className="mx-auto max-w-6xl px-4 py-8">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <h1 className="text-2xl font-bold text-slate-900">Koltuk secimi</h1>

          {/* PDF Sprint 10: "Baglanti durumu gostergesi" */}
          <ConnectionIndicator status={hubStatus} />
        </div>

        {availabilityQuery.data && (
          <p className="mt-1 text-sm text-slate-500">
            {formatDateTime(availabilityQuery.data.startDate)} &middot;{' '}
            {availabilityQuery.data.availableSeats} / {availabilityQuery.data.totalSeats} koltuk bos
          </p>
        )}

        {/* PDF Sprint 10 olayi: EventCancelled.
            Kullanici koltuk secerken etkinlik iptal edilirse aninda
            haber veriyoruz -- rezervasyonda hata almasini beklemeden. */}
        {cancelledTitle && (
          <div className="mt-4">
            <Alert variant="error">
              <strong>{cancelledTitle}</strong> etkinligi az once iptal edildi. Bu oturum icin bilet
              alinamaz.{' '}
              <button
                type="button"
                onClick={() => navigate('/etkinlikler')}
                className="font-medium underline"
              >
                Etkinliklere don
              </button>
            </Alert>
          </div>
        )}

        {lostSeats.length > 0 && (
          <div className="mt-4">
            <Alert variant="error">
              <div className="flex flex-wrap items-center justify-between gap-3">
                <span>
                  {lostSeats.map((s) => s.displayLabel).join(', ')} koltugu siz secerken baska bir
                  kullanici tarafindan alindi. Seciminizden cikardim.
                </span>

                <button
                  type="button"
                  onClick={dismissConflict}
                  className="shrink-0 rounded-lg border border-red-300 px-3 py-1 text-xs font-medium hover:bg-red-100"
                >
                  Anladim
                </button>
              </div>
            </Alert>
          </div>
        )}

        {problem && (
          <div className="mt-4">
            <Alert variant="error">
              {/* Kullaniciya errorCode DEGIL, backend'in yazdigi
                  aciklamayi gosteriyorum. Ama 409 icin ozel bir
                  metin veriyorum: "cakisma" teknik bir kelime,
                  kullanici ne yapmasi gerektigini bilmeli. */}
              {problem.errorCode === 'reservation.seat_conflict'
                ? 'Sectiginiz koltuklardan biri siz secerken baskasi tarafindan alindi. Harita guncellendi, lutfen tekrar secin.'
                : problem.detail}
            </Alert>
          </div>
        )}

        <div className="mt-6 grid gap-6 lg:grid-cols-[1fr_320px]">
          <div>
            {availabilityQuery.isPending ? (
              <div className="h-96 animate-pulse rounded-2xl bg-slate-200" />
            ) : availabilityQuery.isError ? (
              <Alert variant="error">{toProblem(availabilityQuery.error).detail}</Alert>
            ) : (
              <SeatMap
                sections={mapSections}
                onSeatClick={toggleSeat}
                selectedSeatIds={activeSelected}
                emptyMessage="Bu oturum icin koltuk uretilmemis."
                legend={[
                  { label: 'Bos', color: SEAT_COLORS.available },
                  { label: 'Seciminiz', color: SEAT_COLORS.selected },
                  { label: 'Tutuluyor', color: SEAT_COLORS.locked },
                  { label: 'Satildi', color: SEAT_COLORS.sold },
                  { label: 'Satisa kapali', color: SEAT_COLORS.blocked },
                ]}
              />
            )}
          </div>

          {/* ---- OZET PANELI ---- */}
          <aside className="h-fit rounded-2xl border border-slate-200 bg-white p-5 shadow-sm lg:sticky lg:top-6">
            <h2 className="font-semibold text-slate-900">Seciminiz</h2>

            {selectedSeats.length === 0 ? (
              <p className="mt-3 text-sm text-slate-500">
                Haritadan koltuk secin. Sectiginiz koltuklar rezervasyon olusturana kadar kimseye
                kapatilmaz.
              </p>
            ) : (
              <>
                <ul className="mt-3 space-y-2">
                  {selectedSeats.map((seat) => (
                    <li
                      key={seat.eventSeatId}
                      className="flex items-start justify-between gap-2 text-sm"
                    >
                      <div>
                        <p className="font-medium text-slate-900">{seat.displayLabel}</p>
                        <p className="text-xs text-slate-500">
                          {seat.sectionName} &middot; {seat.ticketTypeName}
                        </p>
                      </div>

                      <div className="flex shrink-0 items-center gap-2">
                        <span className="text-slate-700">
                          {formatMoney(seat.price, seat.currency)}
                        </span>

                        <button
                          type="button"
                          onClick={() => toggleSeat(seat.eventSeatId)}
                          className="rounded px-1 text-slate-400 transition-colors hover:text-red-600"
                          aria-label={`${seat.displayLabel} koltugunu secimden cikar`}
                        >
                          &times;
                        </button>
                      </div>
                    </li>
                  ))}
                </ul>

                <div className="mt-4 flex items-center justify-between border-t border-slate-200 pt-4">
                  <span className="text-sm text-slate-500">Toplam</span>
                  <span className="text-lg font-semibold text-slate-900">
                    {formatMoney(total, currency)}
                  </span>
                </div>

                {/* Toplami EKRANDA hesapliyorum ama bu yalnizca
                    gosterim. Rezervasyon isteginde tutar GONDERMIYORUZ;
                    backend fiyati kendi veritabanindan okuyor.
                    (PDF Sprint 6: "Frontend'in gonderdigi tutara
                    guvenilmemelidir.") */}
              </>
            )}

            <Button
              className="mt-5 w-full"
              disabled={selectedSeats.length === 0}
              isLoading={createReservation.isPending}
              onClick={() => createReservation.mutate()}
            >
              Koltuklari ayirt
            </Button>

            <p className="mt-3 text-xs text-slate-500">
              Rezervasyon olusturunca koltuklar <strong>10 dakika</strong> size kilitlenir. Bu sure
              icinde odemeyi tamamlamazsaniz koltuklar otomatik olarak serbest birakilir.
            </p>
          </aside>
        </div>
      </main>
    </div>
  )
}
