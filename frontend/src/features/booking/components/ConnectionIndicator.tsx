import type { ConnectionStatus } from '../hooks/useSeatHub'

/**
 * Canlı bağlantı durumu göstergesi.
 * PDF Sprint 10 frontend gorevi: "Bağlantı durumu göstergesi".
 *
 * NEDEN GEREKLI? "Çalışıyor görünen ama çalışmayan" ekran problemi
 *
 * Gosterge olmasaydı bağlantı koptugunda ekranda HICBIR SEY
 * degismezdi. Kullanıcı donmus bir koltuk haritasina bakip
 * "kimse bilet almiyor, acele etmeme gerek yok" diye dusunurdu.
 *
 * Sonra bir koltuk seçip rezervasyon denerdi ve 409 alırdı --
 * hiçbir sey anlamadan.
 *
 * Gosterge, sessiz basarisizligi GORUNUR kiliyor. Arka plan
 * islerinde de aynı ilkeyi uygulamıştım (Hangfire izleme ekrani):
 * en tehlikeli durum, calismadigi halde çalışıyor gorunmektir.
 *
 */
export function ConnectionIndicator({ status }: { status: ConnectionStatus }) {
  const durumlar = {
    connected: {
      metin: 'Canlı',
      aciklama: 'Koltuk değişiklikleri anında ekranınıza yansıyor.',
      nokta: 'bg-emerald-500',
      kutu: 'bg-emerald-50 text-emerald-700 border-emerald-200',
      nabiz: false,
    },
    connecting: {
      metin: 'Bağlanıyor',
      aciklama: 'Canlı bağlantı kuruluyor.',
      nokta: 'bg-slate-400',
      kutu: 'bg-slate-50 text-slate-600 border-slate-200',
      nabiz: true,
    },
    reconnecting: {
      metin: 'Yeniden bağlanıyor',
      aciklama: 'Bağlantı koptu, tekrar deneniyor. Harita geçici olarak eski olabilir.',
      nokta: 'bg-amber-500',
      kutu: 'bg-amber-50 text-amber-700 border-amber-200',
      nabiz: true,
    },
    disconnected: {
      metin: 'Canlı bağlantı yok',
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
      // role="status" + aria-live="polite": ekran okuyucu, kullanıcı
      // mola verdiginde durumu okur. "alert" kullansaydım her durum
      // degisiminde kullanıcıyı bolerdi -- yeniden baglanma sırasında
      // bu çok rahatsiz edici olurdu.
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
