import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { bookingApi } from '../api/bookingApi'

/**
 * ==================================================================
 * FAVORI DUGMESI -- PDF Sprint 12
 * ==================================================================
 * PDF uclari:
 *   POST   /api/v1/events/{eventId}/favorite
 *   DELETE /api/v1/events/{eventId}/favorite
 *
 * ------------------------------------------------------------------
 * IYIMSER GUNCELLEME (optimistic update)
 * ------------------------------------------------------------------
 * Kalp ikonu, sunucu cevabini BEKLEMEDEN doluyor.
 *
 * Neden? Cunku favorileme "anlik" hissetmesi gereken bir eylem.
 * 200 ms bile beklemek dugmenin bozuk oldugu izlenimi verir ve
 * kullanici tekrar tiklar.
 *
 * Risk: istek basarisiz olursa ekran YALAN soylemis olur. Bu yuzden
 * onError'da eski duruma GERI ALIYORUZ. Iyimser guncellemenin
 * vazgecilmez parcasi budur -- geri alma olmadan yapilirsa arayuz
 * ile sunucu sessizce ayrisir.
 * ------------------------------------------------------------------
 */
export function FavoriteButton({ eventId }: { eventId: string }) {
  const queryClient = useQueryClient()

  const favoritesQuery = useQuery({
    queryKey: ['favorites'],
    queryFn: bookingApi.getMyFavorites,

    // Favoriler kullaniciya ozel ve nadiren degisiyor.
    // 5 dakika, her sayfa gecisinde yeniden istek atmayi onluyor.
    staleTime: 5 * 60 * 1000,
  })

  const favoriMi = favoritesQuery.data?.some((e) => e.id === eventId) ?? false

  const degistir = useMutation({
    mutationFn: () =>
      favoriMi ? bookingApi.removeFavorite(eventId) : bookingApi.addFavorite(eventId),

    onMutate: async () => {
      // Devam eden bir cekim varsa IPTAL ET.
      //
      // Etmeseydik, o cekim bizim iyimser guncellememizden SONRA
      // tamamlanip ESKI veriyi geri yazabilirdi -- kalp bir dolup
      // bir bosalirdi.
      await queryClient.cancelQueries({ queryKey: ['favorites'] })

      const onceki = queryClient.getQueryData(['favorites'])

      queryClient.setQueryData(
        ['favorites'],
        (eski: { id: string }[] | undefined) => {
          if (!eski) {
            return eski
          }

          return favoriMi
            ? eski.filter((e) => e.id !== eventId)
            // Ekleme durumunda TAM etkinlik nesnesi elimizde yok --
            // yalnizca Id var. Gecici bir kayit koyuyorum; dugmenin
            // dolu gorunmesi icin bu yeterli.
            //
            // onSettled'daki invalidate, sunucudan gercek veriyi
            // getirip bu gecici kaydin uzerine yazacak.
            : [...eski, { id: eventId }]
        },
      )

      return { onceki }
    },

    onError: (_hata, _degisken, baglam) => {
      // GERI ALMA: sunucu reddetti, ekrani eski haline dondur.
      if (baglam?.onceki !== undefined) {
        queryClient.setQueryData(['favorites'], baglam.onceki)
      }
    },

    // Basarili da olsa basarisiz da olsa sunucudan gercek durumu al.
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: ['favorites'] })
    },
  })

  return (
    <button
      type="button"
      onClick={() => degistir.mutate()}
      // aria-pressed: ekran okuyucuya dugmenin ACIK/KAPALI oldugunu
      // soyler. Yalnizca ikonu degistirseydik gormeyen kullanici
      // favoride olup olmadigini anlayamazdi.
      aria-pressed={favoriMi}
      aria-label={favoriMi ? 'Favorilerden cikar' : 'Favorilere ekle'}
      className={`inline-flex items-center gap-2 rounded-lg border px-3 py-2 text-sm font-medium transition-colors ${
        favoriMi
          ? 'border-red-200 bg-red-50 text-red-700 hover:bg-red-100'
          : 'border-slate-300 bg-white text-slate-600 hover:bg-slate-50'
      }`}
    >
      <span aria-hidden="true">{favoriMi ? '♥' : '♡'}</span>
      {favoriMi ? 'Favorilerimde' : 'Favorilere ekle'}
    </button>
  )
}
