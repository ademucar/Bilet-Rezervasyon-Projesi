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
// (legend) aynı değeri kullansin. Ayrı ayrı yazsaydık birini
// değiştirip digerini unutmak kacinilmazdi.
//
// Renk seçimi keyfi değil:
//   - Boş: notr gri-mavi. "Tıklanabilir" hissi verir.
//   - Seçili: yesil. Olumlu, kullanıcının kendi eylemi.
//   - Kilitli: amber. "Şimdilik değil" -- 10 dakika sonra bosalabilir.
//   - Satıldı: koyu gri. Kalici, umut yok.
//
// ERİŞİLEBİLİRLİK NOTU: Yalnızca RENGE guvenmiyoruz. Her koltuğun
// <title> etiketinde durumu METIN olarak da yazıyor; renk korlugu
// olan kullanıcı fareyle uzerine gelince veya ekran okuyucuyla
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
 *   - Görsel koltuk seçimi                    -> SeatMap
 *   - Koltuk kilitleme (10 dk)                -> POST /reservations
 *   - Çakışma bildirimi                       -> 409 yakalama + otomatik yenileme
 *   - Rezervasyon özeti                       -> sag sutun
 * ==================================================================
 */
export function SeatSelectionPage() {
  const { sessionId = '' } = useParams()
  const navigate = useNavigate()
  const queryClient = useQueryClient()

  // Seçili koltuklar. Set kullanıyorum, dizi değil.
  //
  // Sebep: "bu koltuk seçili mi?" sorusu her koltuk için, her
  // render'da soruluyor. Dizide includes() O(n); 2000 koltukluk bir
  // salonda 2000 x 2000 = 4 milyon karsilastirma demek. Set.has()
  // O(1).
  const [selected, setSelected] = useState<ReadonlySet<string>>(new Set())

  /** Etkinlik canlı olarak iptal edildiyse gösterilecek uyarı. */
  const [cancelledTitle, setCancelledTitle] = useState<string | null>(null)

  // ================================================================
  // GELEN OLAYI ONBELLEGE ISLE -- PDF: "Gerçek zamanlı koltuk
  // güncelleme"
  // ================================================================
  // Olay geldiğinde sunucudan listeyi TEKRAR CEKMIYORUZ, elimizdeki
  // önbelleği YAMALIYORUZ.
  //
  // Neden? Popüler bir konserde saniyede birkaç olay gelir. Her
  // olayda tam listeyi cekseydik (2000 koltuk, ~200 KB) sunucuyu
  // yoklamadan bile beter yorardik -- SignalR'in butun kazanci
  // giderdi.
  //
  // Yamalama ile tek bir koltuğun durumu değişiyor ve React
  // yalnızca o <rect>'i yeniden ciziyor.
  //
  // setQueryData ile YENI nesneler uretiyorum (yayma operatoru),
  // mevcut diziyi değiştirmiyorum. Yerinde değiştirseydim React
  // referansin aynı olduğunu gorup EKRANI HİÇ GUNCELLEMEZDI --
  // sessizce çalışmayan bir arayüz olurdu.
  // ================================================================
  const patchSeatStatus = useCallback(
    (eventSeatIds: string[], newStatus: number) => {
      queryClient.setQueryData<SeatAvailability>(['seat-availability', sessionId], (previous) => {
        if (!previous) {
          // Liste henüz yuklenmemis. Olayi atlamak güvenli:
          // birazdan gelecek ilk cekimde zaten güncel durum var.
          return previous
        }

        const hedef = new Set(eventSeatIds)
        let değişti = false

        const seats = previous.seats.map((seat) => {
          if (!hedef.has(seat.eventSeatId) || seat.status === newStatus) {
            return seat
          }

          değişti = true

          return { ...seat, status: newStatus }
        })

        // Hicbir sey degismediyse ESKİ nesneyi aynen donuyorum.
        //
        // Yeni nesne donseydik React "veri değişti" deyip tüm
        // koltuk haritasini yeniden hesaplardi -- 2000 koltuk
        // için boşuna bir is.
        if (!değişti) {
          return previous
        }

        return {
          ...previous,
          seats,

          // Boş koltuk sayacini da guncelliyorum.
          //
          // Unutsaydik baslikta "65 / 68 koltuk boş" yazarken
          // haritada 60 boş koltuk görünürdü. Küçük ama
          // kullanıcının sisteme guvenini sarsan turden bir
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
    // Çünkü sunucu aynı anda SeatReleased de gönderiyor ve koltukları
    // asil bosaltan o. Burada ikinci kez işlem yapmak gereksiz olurdu.
    //
    // Peki neden dinliyoruz? Çünkü olay, rezervasyon SAHIBI için
    // anlamlı: kendi rezervasyonunun bittigini ogreniyor. O ekran
    // (ReservationPage) sunucunun verdiği saniyeden geri sayiyor ve
    // sifirlaninca sunucuya soruyor -- aynı sonuca oradan variyor.
    onReservationExpired: () => {},

    // PDF olayi: EventCancelled.
    //
    // Kullanıcı tam koltuk seçerken etkinlik iptal edilirse, secime
    // devam etmesinin anlami yok. Uyariyi ANINDA göstermek, boşuna
    // koltuk seçip rezervasyonda hata almasindan iyi.
    onEventCancelled: (title) => setCancelledTitle(title),

    // PDF: "Güncel koltuk listesini yeniden çekme"
    //
    // Bağlantı kopukken gecen surede kaçırdığımız olaylar var ve
    // SignalR onlari biriktirmiyor. Yamalama ile telafi edemeyiz --
    // neyi kacirdigimizi bilmiyoruz. Tek doğru yol tam listeyi
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
    // YOKLAMA ARTIK ASIL YOL DEĞİL, YEDEK -- PDF Sprint 10
    // ==============================================================
    // Sprint 7'de buraya sabit 10 saniyelik bir yoklama koymus ve
    // su notu birakmistim:
    //
    //   "Bu bir GECICI çözüm. Sprint 10'da SignalR gelecek ve
    //    o zaman bu satır KALDIRILACAK."
    //
    // Sprint 10 geldi ve satiri TAMAMEN KALDIRMADIM. Fikrimi
    // değiştiren sey su: SignalR bağlantısı HER ZAMAN kurulamiyor.
    // Kurumsal aglar WebSocket'i engelleyebiliyor, vekil sunucular
    // uzun baglantilari kesebiliyor, kullanıcının interneti
    // gidebiliyor.
    //
    // Yoklamayi tamamen silseydik, bu durumlarda koltuk haritası
    // TAMAMEN DONARDI -- Sprint 7'deki halinden bile kötü olurdu.
    //
    // Cozum: yoklama SignalR calisirken KAPALI, calismazken ACIK.
    //
    //   canlı bağlantı var  -> false (yoklama yok, olaylar geliyor)
    //   canlı bağlantı yok  -> 10 saniye (Sprint 7 davranisi)
    //
    // Yani en iyi durumda gerçek zamanlı, en kötü durumda eskisi
    // kadar iyi. "Zarif bozulma" (graceful degradation) denen sey.
    // ==============================================================
    refetchInterval: hubStatus === 'connected' ? false : 10_000,

    // staleTime'i 0'a cekiyorum. App.tsx'te varsayılan 60 saniye ve
    // o deger burada YANLIS olurdu: yenileme isteği gitse bile
    // "veri hâlâ taze" denip sonuç yok sayilabilirdi.
    staleTime: 0,
  })

  const seats = useMemo(() => availabilityQuery.data?.seats ?? [], [availabilityQuery.data])

  // ================================================================
  // CAKISMA TESPITI -- PDF Sprint 7: "Çakışma bildirimi"
  // ================================================================
  // Her yenilemeden sonra soruyoruz: sectigim koltuklardan biri
  // artık boş değil mi?
  //
  // Bu, kullanıcının kaybettigi koltuğu SESSIZCE seçili gostermeyi
  // engelliyor. Aksi halde kullanıcı "Koltukları ayırt"a basar,
  // 409 alır ve neden olduğunu anlamaz. Kotu haberi erken vermek,
  // geç vermekten iyidir.
  //
  // ----------------------------------------------------------------
  // NEDEN useEffect DEĞİL?
  // ----------------------------------------------------------------
  // İlk yazimimda bunu bir effect içinde yapip kaybedilen koltukları
  // setSelected ile state'ten siliyordum. Calisiyordu ama yanlış
  // yontemdi: kullanıcı hiçbir sey YAPMADIGI halde state
  // değiştiriyordu ve React fazladan bir render türü doneyordu.
  //
  // Kaybedilen koltuk, `selected` ile `seats`in bir SONUCU --
  // bağımsız bir bilgi değil. Sonuç olan seyi state'te tutmak,
  // aynı gercegi iki yerde saklamak demek; ikisi kacinilmaz olarak
  // birbirinden ayrilir.
  //
  // Bu yuzden render sırasında HESAPLIYORUM. `selected` içinde eski
  // kimlikler kalabilir ama hiçbir yerde doğrudan kullanılmıyor;
  // her tuketici aşağıdaki `activeSelected`i okuyor.
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
   * Anahtar, SECIME bağlı. Kullanıcı aynı koltuklarla ikinci kez
   * gonderirse (butona iki kez basti, ag koptu ve tekrar denedi)
   * AYNI anahtar gider ve backend ikinci rezervasyonu olusturmaz.
   *
   * Ama kullanıcı seçimi DEGISTIRIRSE bu artık bambaska bir istek --
   * eski anahtarla gonderirsek backend "bunu zaten yaptım" deyip
   * ESKİ rezervasyonu dondururdu ve kullanıcı yanlış koltukları
   * satin alırdı. Bu yuzden seçim değişince anahtari sifirliyorum.
   *
   * useRef kullanıyorum, useState değil: bu deger ekranda
   * gorunmuyor, degismesi yeniden cizim gerektirmiyor.
   * ----------------------------------------------------------------
   */
  const idempotencyKeyRef = useRef<string | null>(null)

  const toggleSeat = (eventSeatId: string) => {
    idempotencyKeyRef.current = null

    // ==============================================================
    // NEDEN FONKSIYONEL GUNCELLEME? -- TARAYICIDA YAKALADIGIM HATA
    // ==============================================================
    // İlk yazimim soyleydi:
    //
    //     const next = new Set(activeSelected)   // <-- HATALI
    //     ...
    //     setSelected(next)
    //
    // Tek tek tiklamada calisiyordu. Ama tarayıcıda ucunu ARKA ARKAYA
    // tıklayınca yalnızca SONUNCUSU seçili kaldı.
    //
    // Sebep: React aynı tur icindeki state guncellemelerini TOPLUYOR.
    // Uc cagri da aynı render'in `activeSelected` degerini görüyor --
    // yani ucu de BOŞ kumeden turetiliyor ve birbirini eziyor.
    //
    // `setSelected(prev => ...)` ile React bize O ANKI değeri veriyor;
    // ikinci cagri birincinin sonucunu görüyor. Hizli tiklamada da
    // doğru çalışıyor.
    //
    // Kaybedilen koltukları `lostIds` ile burada temizliyorum:
    // `prev` güncel olmalı ama `lostIds` sunucu verisinden geliyor ve
    // tiklama anindaki değeri doğru -- ikisini karistirmamak önemli.
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

  /** Çakışma uyarisini kapatır: kaybedilen koltukları seçimden duser. */
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
      // Rezervasyon detayını onbellege ELIMIZLE koyuyorum.
      //
      // Yonlendirdigimiz sayfa aynı veriyi isteyecek. Onbellege
      // koymasaydik o sayfa açılır acilmaz boş bir iskelet gosterip
      // yeni bir istek atardi -- oysa veri elimizde.
      //
      // Bu ozellikle önemli çünkü GERİ SAYIM o sayfada başlıyor;
      // fazladan bir gidis-donus, sayacin geç baslamasi demekti.
      queryClient.setQueryData(['reservation', reservation.id], reservation)

      navigate(`/rezervasyonlar/${reservation.id}`)
    },

    onError: () => {
      // Hata ne olursa olsun haritayi tazele.
      //
      // 409 aldiysak koltuğu başkası kapmis demektir; kullanıcıya
      // güncel durumu gostermeliyiz. Yenilemeseydik kullanıcı aynı
      // dolu koltukla tekrar tekrar denerdi.
      void queryClient.invalidateQueries({ queryKey: ['seat-availability', sessionId] })
    },
  })

  // ================================================================
  // HARITA VERISINI HAZIRLA
  // ================================================================
  // Backend koltukları DUZ bir liste olarak dönüyor; harita ise
  // bolumlere gruplanmis istiyor. Ceviriyi burada yapıyorum.
  //
  // useMemo ŞART: bu hesap 2000 koltuk için yeni nesneler uretiyor.
  // Sarmasaydik, her saniye (geri sayım, fare hareketi, herhangi bir
  // state degisikligi) yeniden çalışır ve SeatMap'in kendi useMemo'su
  // da bosa çıkardı -- çünkü ona her seferinde YENI bir dizi
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
      // Backend bölüm sırasını bu uc noktada dondurmuyor; listedeki
      // gorulme sırasını kullanıyorum. Backend koltukları bölüm ve
      // sıra etiketine göre sıralı döndürüyor, bu yuzden sonuç
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

        // Yalnızca BOŞ koltuk tıklanabilir.
        //
        // Kilitli koltuğu tiklatmamak bilinçli: kullanıcı "10 dakika
        // sonra bosalir" diye bekleyemez, o sırada başkası ödemeyi
        // tamamlamis olabilir. Umut vermek yerine net olmak daha iyi.
        selectable: seat.status === EventSeatStatus.Available,
        description: `${seat.ticketTypeName}, ${formatMoney(seat.price, seat.currency)} - ${seatStatusLabel(seat.status)}`,
      })),
    }))
  }, [seats, activeSelected])

  // Seçili koltuklarin detaylari -- özet paneli için.
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
          <h1 className="text-2xl font-bold text-slate-900">Koltuk seçimi</h1>

          {/* PDF Sprint 10: "Bağlantı durumu göstergesi" */}
          <ConnectionIndicator status={hubStatus} />
        </div>

        {availabilityQuery.data && (
          <p className="mt-1 text-sm text-slate-500">
            {formatDateTime(availabilityQuery.data.startDate)} &middot;{' '}
            {availabilityQuery.data.availableSeats} / {availabilityQuery.data.totalSeats} koltuk bos
          </p>
        )}

        {/* PDF Sprint 10 olayi: EventCancelled.
            Kullanıcı koltuk seçerken etkinlik iptal edilirse anında
            haber veriyoruz -- rezervasyonda hata almasini beklemeden. */}
        {cancelledTitle && (
          <div className="mt-4">
            <Alert variant="error">
              <strong>{cancelledTitle}</strong> etkinliği az önce iptal edildi. Bu oturum için bilet
              alınamaz.{' '}
              <button
                type="button"
                onClick={() => navigate('/etkinlikler')}
                className="font-medium underline"
              >
                Etkinliklere dön
              </button>
            </Alert>
          </div>
        )}

        {lostSeats.length > 0 && (
          <div className="mt-4">
            <Alert variant="error">
              <div className="flex flex-wrap items-center justify-between gap-3">
                <span>
                  {lostSeats.map((s) => s.displayLabel).join(', ')} koltuğu siz seçerken başka bir
                  kullanıcı tarafından alındı. Seçiminizden çıkardım.
                </span>

                <button
                  type="button"
                  onClick={dismissConflict}
                  className="shrink-0 rounded-lg border border-red-300 px-3 py-1 text-xs font-medium hover:bg-red-100"
                >
                  Anladım
                </button>
              </div>
            </Alert>
          </div>
        )}

        {problem && (
          <div className="mt-4">
            <Alert variant="error">
              {/* Kullanıcıya errorCode DEĞİL, backend'in yazdigi
                  aciklamayi gösteriyorum. Ama 409 için ozel bir
                  metin veriyorum: "çakışma" teknik bir kelime,
                  kullanıcı ne yapmasi gerektigini bilmeli. */}
              {problem.errorCode === 'reservation.seat_conflict'
                ? 'Seçtiğiniz koltuklardan biri siz seçerken başkası tarafından alındı. Harita güncellendi, lütfen tekrar seçin.'
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
                emptyMessage="Bu oturum için koltuk üretilmemiş."
                legend={[
                  { label: 'Boş', color: SEAT_COLORS.available },
                  { label: 'Seçiminiz', color: SEAT_COLORS.selected },
                  { label: 'Tutuluyor', color: SEAT_COLORS.locked },
                  { label: 'Satıldı', color: SEAT_COLORS.sold },
                  { label: 'Satışa kapalı', color: SEAT_COLORS.blocked },
                ]}
              />
            )}
          </div>

          {/* ---- OZET PANELİ ---- */}
          <aside className="h-fit rounded-2xl border border-slate-200 bg-white p-5 shadow-sm lg:sticky lg:top-6">
            <h2 className="font-semibold text-slate-900">Seçiminiz</h2>

            {selectedSeats.length === 0 ? (
              <p className="mt-3 text-sm text-slate-500">
                Haritadan koltuk seçin. Seçtiğiniz koltuklar rezervasyon oluşturana kadar kimseye
                kapatılmaz.
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
                          aria-label={`${seat.displayLabel} koltuğunu seçimden çıkar`}
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

                {/* Toplami EKRANDA hesapliyorum ama bu yalnızca
                    gosterim. Rezervasyon isteginde tutar GONDERMIYORUZ;
                    backend fiyati kendi veritabanindan okuyor.
                    (PDF Sprint 6: "Frontend'in gonderdigi tutara
                    güvenilmemelidir.") */}
              </>
            )}

            <Button
              className="mt-5 w-full"
              disabled={selectedSeats.length === 0}
              isLoading={createReservation.isPending}
              onClick={() => createReservation.mutate()}
            >
              Koltukları ayırt
            </Button>

            <p className="mt-3 text-xs text-slate-500">
              Rezervasyon oluşturunca koltuklar <strong>10 dakika</strong> size kilitlenir. Bu süre
              içinde ödemeyi tamamlamazsanız koltuklar otomatik olarak serbest bırakılır.
            </p>
          </aside>
        </div>
      </main>
    </div>
  )
}
