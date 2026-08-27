import { useEffect, useRef, useState } from 'react'
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr'

/**
 * ==================================================================
 * GERCEK ZAMANLI KOLTUK GUNCELLEME -- PDF Sprint 10
 * ==================================================================
 * Sprint 7'de koltuk haritasini 10 saniyede bir yokluyorduk
 * (refetchInterval). O zaman koda su notu birakmistim:
 *
 *   "Bu bir GECICI cozum. PDF Sprint 10'da SignalR gelecek ve
 *    sunucu degisiklikleri ANINDA itecek."
 *
 * Bu kanca o notun karsiligi.
 * ==================================================================
 */

/** Sunucudan gelen olaylar. Adlar backend'deki sabitlerle birebir ayni. */
export interface SeatHubHandlers {
  onSeatsLocked: (eventSeatIds: string[]) => void
  onSeatsReleased: (eventSeatIds: string[]) => void
  onSeatsSold: (eventSeatIds: string[]) => void
  onReservationExpired: (reservationId: string) => void
  onEventCancelled: (eventTitle: string) => void
  /** Yeniden baglandiktan sonra: kacirdigimiz olaylar icin listeyi tazele. */
  onReconnected: () => void
}

export type ConnectionStatus = 'connecting' | 'connected' | 'reconnecting' | 'disconnected'

interface SeatEventPayload {
  eventSessionId: string
  eventSeatIds: string[]
}

export function useSeatHub(
  eventSessionId: string | undefined,
  handlers: SeatHubHandlers,
): ConnectionStatus {
  const [status, setStatus] = useState<ConnectionStatus>('connecting')

  /**
   * ----------------------------------------------------------------
   * HANDLER'LARI REF'TE TUTMANIN SEBEBI
   * ----------------------------------------------------------------
   * Bu kancayi cagiran bilesen her render'da YENI bir handlers
   * nesnesi olusturuyor (nesne literali). Eger handlers'i asagidaki
   * useEffect'in bagimlilik dizisine koysaydik, effect HER RENDER'DA
   * yeniden calisirdi.
   *
   * Sonuc: saniyede birkac kez baglanti kurulup kapatilirdi. Sunucu
   * her seferinde yeni bir baglanti kaydeder, gruba ekler, sonra
   * siler. Uygulama calisiyor gibi gorunur ama sunucu bosuna yanar
   * ve olaylar kacirilir.
   *
   * Ref ile: effect YALNIZCA eventSessionId degisince calisiyor,
   * ama olay geldiginde her zaman EN GUNCEL handler'lar cagriliyor.
   * ----------------------------------------------------------------
   */
  const handlersRef = useRef(handlers)

  // Atamayi RENDER SIRASINDA degil, render'dan SONRA yapiyorum.
  //
  // Ilk yazimim `handlersRef.current = handlers` seklinde, dogrudan
  // govdedeydi. Lint (react/refs) hakli olarak uyardi: render
  // sirasinda ref'e yazmak, React'in saf render sozunu bozar.
  //
  // Bagimlilik dizisi YOK -- yani her render'dan sonra calisiyor.
  // Bu tam olarak istedigimiz sey: ref her zaman en son
  // handler'lari tutuyor ama baglanti effect'i tetiklenmiyor.
  useEffect(() => {
    handlersRef.current = handlers
  })

  const connectionRef = useRef<HubConnection | null>(null)

  useEffect(() => {
    if (!eventSessionId) {
      return
    }

    const connection = new HubConnectionBuilder()
      // Gorece adres: Vite proxy'si /hubs'i backend'e yonlendiriyor.
      // Mutlak adres yazsaydik uretimde ortam bazli yapilandirma
      // gerekirdi -- API istemcisinde de ayni yaklasimi kullaniyoruz.
      .withUrl('/hubs/seats')

      // ==========================================================
      // OTOMATIK YENIDEN BAGLANMA -- PDF: "SignalR baglantisi
      // kesildiginde frontend yeniden baglanmalidir."
      // ==========================================================
      // Varsayilan withAutomaticReconnect() yalnizca DORT kez dener
      // (0, 2, 10, 30 sn) ve sonra PES EDER.
      //
      // Bizim icin bu yetersiz: kullanici koltuk secim ekraninda
      // 10 dakika kalabilir. Wi-Fi'si iki dakika kesilse baglanti
      // kalici olarak olurdu ve kullanici bunu FARK ETMEDEN eski
      // bir haritaya bakmaya devam ederdi.
      //
      // Kendi stratejimi veriyorum: artan araliklarla ama SONSUZA
      // KADAR deniyor.
      //
      // Neden artan? Sunucu tamamen kapaliysa her saniye denemek
      // hem istemciyi hem sunucuyu bosuna yorar. Neden 30 saniyede
      // duruyor? Daha uzun beklemek, sunucu geri geldiginde
      // kullanicinin yarim dakikadan fazla eski veri gormesi
      // demek olurdu.
      // ==========================================================
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: (context) => {
          const gecikmeler = [0, 2000, 5000, 10000, 30000]

          return gecikmeler[Math.min(context.previousRetryCount, gecikmeler.length - 1)]
        },
      })

      // Uretimde yalnizca hatalar. Gelistirmede Information:
      // baglanti kurulumu ve grup islemleri konsolda gorunuyor,
      // sorun teshisi cok kolaylasiyor.
      .configureLogging(import.meta.env.DEV ? LogLevel.Information : LogLevel.Error)
      .build()

    connectionRef.current = connection

    // ==============================================================
    // OLAY DINLEYICILERI -- adlar backend ile BIREBIR
    // ==============================================================
    // SignalR eslesmeyen bir olay adini HATA SAYMAZ; mesaj sessizce
    // hicbir yere gitmez. Yani "SeatLocked" yerine "seatLocked"
    // yazsaydik hicbir uyari almadan calismaz olurdu.
    //
    // Bu yuzden adlari kopyala-yapistir ile aliyorum, elle
    // yazmiyorum.
    // ==============================================================
    connection.on('SeatLocked', (payload: SeatEventPayload) => {
      handlersRef.current.onSeatsLocked(payload.eventSeatIds)
    })

    connection.on('SeatReleased', (payload: SeatEventPayload) => {
      handlersRef.current.onSeatsReleased(payload.eventSeatIds)
    })

    connection.on('SeatSold', (payload: SeatEventPayload) => {
      handlersRef.current.onSeatsSold(payload.eventSeatIds)
    })

    connection.on('ReservationExpired', (payload: { reservationId: string }) => {
      handlersRef.current.onReservationExpired(payload.reservationId)
    })

    connection.on('EventCancelled', (payload: { eventId: string; eventTitle: string }) => {
      handlersRef.current.onEventCancelled(payload.eventTitle)
    })

    // ---- Baglanti durumu ----
    connection.onreconnecting(() => setStatus('reconnecting'))

    connection.onreconnected(() => {
      setStatus('connected')

      // ==========================================================
      // YENIDEN BAGLANINCA LISTEYI BASTAN CEK
      // PDF Frontend gorevi: "Guncel koltuk listesini yeniden cekme"
      // ==========================================================
      // Bu satir, kancanin en kritik yeri.
      //
      // Baglanti kopukken gecen surede sunucu onlarca olay
      // gondermis olabilir ve HICBIRI bize ulasmadi. SignalR
      // kacirilan mesajlari BIRIKTIRMEZ.
      //
      // Yani yeniden baglanti tek basina yetmez: elimizdeki
      // harita hala eski. Tam listeyi cekmek, kacirdigimiz her
      // seyi tek hamlede telafi ediyor.
      //
      // Bu ayni zamanda SignalR'a neden "kaybolursa olur"
      // diyebildigimizin sebebi: her zaman guvenilir bir
      // toparlanma yolumuz var.
      // ==========================================================
      void connection.invoke('JoinSession', eventSessionId).catch(() => {
        // Gruba yeniden katilma basarisiz olursa da liste
        // cekiliyor; kullanici en azindan guncel veriyi goruyor.
      })

      handlersRef.current.onReconnected()
    })

    connection.onclose(() => setStatus('disconnected'))

    // ---- Baglan ----
    //
    // ==============================================================
    // LINT: react/set-state-in-effect -- GEREKCEYLE SUSTURULDU
    // ==============================================================
    // Kural, effect icinde senkron setState cagirmaya karsi uyariyor
    // ve cogu durumda hakli. Ama kuralin KENDI aciklamasi istisnayi
    // soyluyor: "Use an effect only when synchronizing with an
    // external system."
    //
    // Burada tam olarak o durum var: React'i bir WEBSOCKET
    // BAGLANTISINA bagliyoruz. Baglanti kurulmaya baslarken durumu
    // "connecting" yapmak, dis sistemin durumunu ekrana yansitmak
    // demek.
    //
    // Bu satiri silmeyi denedim: oturum degistirildiginde (kullanici
    // baska bir oturuma gecince) eski baglanti kapanip "disconnected"
    // yaziyor ve gosterge bir an KIRMIZI yaniyordu. Kullaniciya
    // olmayan bir sorunu bildirmek, kucuk bir lint uyarisindan
    // daha kotu.
    //
    // Projede benimsedigim kural: susturmak serbest degil, yalnizca
    // "neden" yazildiginda serbest.
    // oxlint-disable-next-line react/set-state-in-effect
    setStatus('connecting')

    connection
      .start()
      .then(() => {
        setStatus('connected')

        // PDF is kurali: "Kullanici yalnizca goruntuledigi etkinlik
        // oturumunun grubuna katilmalidir."
        return connection.invoke('JoinSession', eventSessionId)
      })
      .catch(() => {
        // Baglanti kurulamadi.
        //
        // Bu bir FELAKET DEGIL: koltuk haritasi yine calisiyor,
        // sadece yoklama (polling) ile guncelleniyor. Durum
        // gostergesi kullaniciya bunu soyluyor.
        setStatus('disconnected')
      })

    // ==============================================================
    // TEMIZLIK -- SART
    // ==============================================================
    // Kullanici sayfadan ayrilinca baglantiyi kapatmazsak:
    //   - Sunucu tarafinda acik baglanti birikir
    //   - Gruptan cikilmadigi icin mesaj gonderilmeye devam eder
    //   - Kaldirilmis bilesende setState cagrilir (React uyarisi)
    //
    // stop() zaten gruptan da cikariyor; yine de LeaveSession
    // cagiriyorum ki sunucu tarafinda niyet acik olsun.
    // ==============================================================
    return () => {
      if (connection.state === HubConnectionState.Connected) {
        void connection.invoke('LeaveSession', eventSessionId).catch(() => {
          // Kapanirken hata onemsiz: stop() zaten temizleyecek.
        })
      }

      void connection.stop()
      connectionRef.current = null
    }
  }, [eventSessionId])

  return status
}
