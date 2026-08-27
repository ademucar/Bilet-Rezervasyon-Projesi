import type { ConnectionStatus } from '../hooks/useSeatHub'

/**
 * Canli baglanti durumu gostergesi.
 * PDF Sprint 10 frontend gorevi: "Baglanti durumu gostergesi".
 *
 * ==================================================================
 * NEDEN GEREKLI? "Calisiyor gorunen ama calismayan" ekran problemi
 * ==================================================================
 * Gosterge olmasaydi baglanti koptugunda ekranda HICBIR SEY
 * degismezdi. Kullanici donmus bir koltuk haritasina bakip
 * "kimse bilet almiyor, acele etmeme gerek yok" diye dusunurdu.
 *
 * Sonra bir koltuk secip rezervasyon denerdi ve 409 alirdi --
 * hicbir sey anlamadan.
 *
 * Gosterge, sessiz basarisizligi GORUNUR kiliyor. Arka plan
 * islerinde de ayni ilkeyi uygulamistik (Hangfire izleme ekrani):
 * en tehlikeli durum, calismadigi halde calisiyor gorunmektir.
 * ==================================================================
 */
export function ConnectionIndicator({ status }: { status: ConnectionStatus }) {
  const durumlar = {
    connected: {
      metin: 'Canli',
      aciklama: 'Koltuk degisiklikleri aninda ekraniniza yansiyor.',
      nokta: 'bg-emerald-500',
      kutu: 'bg-emerald-50 text-emerald-700 border-emerald-200',
      nabiz: false,
    },
    connecting: {
      metin: 'Baglaniyor',
      aciklama: 'Canli baglanti kuruluyor.',
      nokta: 'bg-slate-400',
      kutu: 'bg-slate-50 text-slate-600 border-slate-200',
      nabiz: true,
    },
    reconnecting: {
      metin: 'Yeniden baglaniyor',
      aciklama: 'Baglanti koptu, tekrar deneniyor. Harita gecici olarak eski olabilir.',
      nokta: 'bg-amber-500',
      kutu: 'bg-amber-50 text-amber-700 border-amber-200',
      nabiz: true,
    },
    disconnected: {
      metin: 'Canli baglanti yok',
      aciklama: 'Harita 10 saniyede bir yenileniyor.',
      nokta: 'bg-red-500',
      kutu: 'bg-red-50 text-red-700 border-red-200',
      nabiz: false,
    },
  } as const

  const durum = durumlar[status]

  return (
    <span
      className={`inline-flex items-center gap-2 rounded-full border px-3 py-1 text-xs font-medium ${durum.kutu}`}
      // role="status" + aria-live="polite": ekran okuyucu, kullanici
      // mola verdiginde durumu okur. "alert" kullansaydik her durum
      // degisiminde kullaniciyi bolerdi -- yeniden baglanma sirasinda
      // bu cok rahatsiz edici olurdu.
      role="status"
      aria-live="polite"
      title={durum.aciklama}
    >
      <span
        className={`h-2 w-2 rounded-full ${durum.nokta} ${durum.nabiz ? 'animate-pulse' : ''}`}
        aria-hidden="true"
      />
      {durum.metin}
    </span>
  )
}
