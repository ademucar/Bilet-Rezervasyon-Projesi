import { useEffect, useRef, useState } from 'react'
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr'

/**
 * GERCEK ZAMANLI KOLTUK GUNCELLEME -- PDF Sprint 10
 *
 * Sprint 7'de koltuk haritasini 10 saniyede bir yokluyorduk
 * (refetchInterval). O zaman koda su notu birakmistim:
 *
 *   "Bu bir GECICI çözüm. PDF Sprint 10'da SignalR gelecek ve
 *    sunucu değişiklikleri ANINDA itecek."
 *
 * Bu kanca o notun karşılığı.
 *
 */

/** Sunucudan gelen olaylar. Adlar backend'deki sabitlerle birebir aynı. */
export interface SeatHubHandlers {
  onSeatsLocked: (eventSeatIds: string[]) => void
  onSeatsReleased: (eventSeatIds: string[]) => void
  onSeatsSold: (eventSeatIds: string[]) => void
  onReservationExpired: (reservationId: string) => void
  onEventCancelled: (eventTitle: string) => void
  /** Yeniden baglandiktan sonra: kaçırdığım olaylar için listeyi tazele. */
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
   * Handler'lari ref'te tutmanin sebebi
   *
   * Bu kancayi cagiran bileşen her render'da YENI bir handlers
   * nesnesi olusturuyor (nesne literali). Eger handlers'i aşağıdaki
   * useEffect'in bagimlilik dizisine koysaydım, effect HER RENDER'DA
   * yeniden calisirdi.
   *
   * Sonuç: saniyede birkaç kez bağlantı kurulup kapatilirdi. Sunucu
   * her seferinde yeni bir bağlantı kaydeder, gruba ekler, sonra
   * siler. Uygulama çalışıyor gibi görünür ama sunucu boşuna yanar
   * ve olaylar kacirilir.
   *
   * Ref ile: effect YALNIZCA eventSessionId değişince çalışıyor,
   * ama olay geldiğinde her zaman EN GUNCEL handler'lar cagriliyor.
   *
   */
  const handlersRef = useRef(handlers)

  // Atamayi RENDER SIRASINDA değil, render'dan SONRA yapıyorum.
  //
  // İlk yazimim `handlersRef.current = handlers` seklinde, doğrudan
  // govdedeydi. Lint (react/refs) haklı olarak uyardi: render
  // sırasında ref'e yazmak, React'in saf render sozunu bozar.
  //
  // Bagimlilik dizisi YOK -- yani her render'dan sonra çalışıyor.
  // Bu tam olarak istedigim sey: ref her zaman en son
  // handler'lari tutuyor ama bağlantı effect'i tetiklenmiyor.
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
      // Mutlak adres yazsaydım uretimde ortam bazlı yapilandirma
      // gerekirdi -- API istemcisinde de aynı yaklasimi kullanıyorum.
      .withUrl('/hubs/seats')

      // OTOMATIK YENIDEN BAGLANMA -- PDF: "SignalR bağlantısı
      // kesildiginde frontend yeniden baglanmalidir."
      //
      // Varsayılan withAutomaticReconnect() yalnızca DORT kez dener
      // (0, 2, 10, 30 sn) ve sonra PES EDER.
      //
      // Bizim için bu yetersiz: kullanıcı koltuk seçim ekraninda
      // 10 dakika kalabilir. Wi-Fi'si iki dakika kesilse bağlantı
      // kalici olarak olurdu ve kullanıcı bunu FARK ETMEDEN eski
      // bir haritaya bakmaya devam ederdi.
      //
      // Kendi stratejimi veriyorum: artan araliklarla ama SONSUZA
      // KADAR deniyor.
      //
      // Neden artan? Sunucu tamamen kapaliysa her saniye denemek
      // hem istemciyi hem sunucuyu boşuna yorar. Neden 30 saniyede
      // duruyor? Daha uzun beklemek, sunucu geri geldiğinde
      // kullanıcının yarim dakikadan fazla eski veri gormesi
      // demek olurdu.
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: (context) => {
          const gecikmeler = [0, 2000, 5000, 10000, 30000]

          return gecikmeler[Math.min(context.previousRetryCount, gecikmeler.length - 1)]
        },
      })

      // Uretimde yalnızca hatalar. Gelistirmede Information:
      // bağlantı kurulumu ve grup islemleri konsolda görünüyor,
      // sorun teshisi çok kolaylasiyor.
      .configureLogging(import.meta.env.DEV ? LogLevel.Information : LogLevel.Error)
      .build()

    connectionRef.current = connection

    // OLAY DINLEYICILERI -- adlar backend ile BIREBIR
    //
    // SignalR eslesmeyen bir olay adını HATA SAYMAZ; mesaj sessizce
    // hiçbir yere gitmez. Yani "SeatLocked" yerine "seatLocked"
    // yazsaydım hiçbir uyarı almadan calismaz olurdu.
    //
    // Bu yuzden adları kopyala-yapistir ile alıyorum, elle
    // yazmiyorum.
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

    // ---- Bağlantı durumu ----
    connection.onreconnecting(() => setStatus('reconnecting'))

    connection.onreconnected(() => {
      setStatus('connected')

      // Yeniden baglaninca listeyi bastan cek
      // PDF Frontend gorevi: "Güncel koltuk listesini yeniden çekme"
      //
      // Bu satır, kancanin en kritik yeri.
      //
      // Bağlantı kopukken gecen surede sunucu onlarca olay
      // gondermis olabilir ve HICBIRI bana ulasmadi. SignalR
      // kacirilan mesajlari BIRIKTIRMEZ.
      //
      // Yani yeniden bağlantı tek başına yetmez: elimizdeki
      // harita hâlâ eski. Tam listeyi cekmek, kaçırdığım her
      // seyi tek hamlede telafi ediyor.
      //
      // Bu aynı zamanda SignalR'a neden "kaybolursa olur"
      // diyebildigimizin sebebi: her zaman guvenilir bir
      // toparlanma yolum var.
      void connection.invoke('JoinSession', eventSessionId).catch(() => {
        // Gruba yeniden katilma başarısız olursa da liste
        // çekiliyor; kullanıcı en azindan güncel veriyi görüyor.
      })

      handlersRef.current.onReconnected()
    })

    connection.onclose(() => setStatus('disconnected'))

    // ---- Baglan ----
    //
    // LINT: react/set-state-in-effect -- GEREKCEYLE SUSTURULDU
    //
    // Kural, effect içinde senkron setState cagirmaya karsi uyariyor
    // ve çoğu durumda haklı. Ama kuralin KENDİ açıklaması istisnayi
    // söylüyor: "Use an effect only when synchronizing with an
    // external system."
    //
    // Burada tam olarak o durum var: React'i bir WEBSOCKET
    // BAGLANTISINA bagliyoruz. Bağlantı kurulmaya baslarken durumu
    // "connecting" yapmak, dis sistemin durumunu ekrana yansitmak
    // demek.
    //
    // Bu satiri silmeyi denedim: oturum değiştirildiginde (kullanıcı
    // başka bir oturuma gecince) eski bağlantı kapanip "disconnected"
    // yazıyor ve gosterge bir an KIRMIZI yaniyordu. Kullanıcıya
    // olmayan bir sorunu bildirmek, küçük bir lint uyarisindan
    // daha kötü.
    //
    // Projede benimsedigim kural: susturmak serbest değil, yalnızca
    // "neden" yazildiginda serbest.
    // oxlint-disable-next-line react/set-state-in-effect
    setStatus('connecting')

    connection
      .start()
      .then(() => {
        setStatus('connected')

        // PDF is kuralı: "Kullanıcı yalnızca goruntuledigi etkinlik
        // oturumunun grubuna katilmalidir."
        return connection.invoke('JoinSession', eventSessionId)
      })
      .catch(() => {
        // Bağlantı kurulamadi.
        //
        // Bu bir FELAKET DEĞİL: koltuk haritası yine çalışıyor,
        // sadece yoklama (polling) ile guncelleniyor. Durum
        // göstergesi kullanıcıya bunu söylüyor.
        setStatus('disconnected')
      })

    // Temizlik -- şart
    //
    // Kullanıcı sayfadan ayrilinca baglantiyi kapatmazsak:
    //   - Sunucu tarafında açık bağlantı birikir
    //   - Gruptan cikilmadigi için mesaj gonderilmeye devam eder
    //   - Kaldirilmis bileşende setState cagrilir (React uyarısı)
    //
    // stop() zaten gruptan da cikariyor; yine de LeaveSession
    // cagiriyorum ki sunucu tarafında niyet açık olsun.
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
