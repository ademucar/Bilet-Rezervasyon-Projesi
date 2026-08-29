import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { bookingApi } from '../api/bookingApi'

/**
 *
 * FAVORI DUGMESI -- PDF Sprint 12
 *
 * PDF uclari:
 *   POST   /api/v1/events/{eventId}/favorite
 *   DELETE /api/v1/events/{eventId}/favorite
 *
 * IYIMSER GUNCELLEME (optimistic update)
 *
 * Kalp ikonu, sunucu cevabini BEKLEMEDEN doluyor.
 *
 * Neden? Çünkü favorileme "anlik" hissetmesi gereken bir eylem.
 * 200 ms bile beklemek dugmenin bozuk olduğu izlenimi verir ve
 * kullanıcı tekrar tiklar.
 *
 * Risk: istek başarısız olursa ekran YALAN soylemis olur. Bu yuzden
 * onError'da eski duruma GERİ ALIYORUM. Iyimser guncellemenin
 * vazgecilmez parcasi budur -- geri alma olmadan yapilirsa arayüz
 * ile sunucu sessizce ayrisir.
 *
 */
export function FavoriteButton({ eventId }: { eventId: string }) {
  const queryClient = useQueryClient()

  const favoritesQuery = useQuery({
    queryKey: ['favorites'],
    queryFn: bookingApi.getMyFavorites,

    // Favoriler kullanıcıya ozel ve nadiren değişiyor.
    // 5 dakika, her sayfa gecisinde yeniden istek atmayi onluyor.
    staleTime: 5 * 60 * 1000,
  })

  const favoriMi = favoritesQuery.data?.some((e) => e.id === eventId) ?? false

  const degistir = useMutation({
    mutationFn: () =>
      favoriMi ? bookingApi.removeFavorite(eventId) : bookingApi.addFavorite(eventId),

    onMutate: async () => {
      // Devam eden bir çekim varsa İPTAL ET.
      //
      // Etmeseydim, o çekim benim iyimser guncellememizden SONRA
      // tamamlanip ESKİ veriyi geri yazabilirdi -- kalp bir dolup
      // bir bosalirdi.
      await queryClient.cancelQueries({ queryKey: ['favorites'] })

      const onceki = queryClient.getQueryData(['favorites'])

      queryClient.setQueryData(['favorites'], (eski: { id: string }[] | undefined) => {
        if (!eski) {
          return eski
        }

        return favoriMi
          ? eski.filter((e) => e.id !== eventId)
          : // Ekleme durumunda TAM etkinlik nesnesi elimizde yok --
            // yalnızca Id var. Gecici bir kayıt koyuyorum; dugmenin
            // dolu görünmesi için bu yeterli.
            //
            // onSettled'daki invalidate, sunucudan gerçek veriyi
            // getirip bu geçici kaydin uzerine yazacak.
            [...eski, { id: eventId }]
      })

      return { onceki }
    },

    onError: (_hata, _degisken, baglam) => {
      // GERİ ALMA: sunucu reddetti, ekrani eski haline dondur.
      if (baglam?.onceki !== undefined) {
        queryClient.setQueryData(['favorites'], baglam.onceki)
      }
    },

    // Başarılı da olsa başarısız da olsa sunucudan gerçek durumu al.
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: ['favorites'] })
    },
  })

  return (
    <button
      type="button"
      onClick={() => degistir.mutate()}
      // aria-pressed: ekran okuyucuya dugmenin ACIK/KAPALI olduğunu
      // söyler. Yalnızca ikonu değiştirseydim görmeyen kullanıcı
      // favoride olup olmadigini anlayamazdi.
      aria-pressed={favoriMi}
      aria-label={favoriMi ? 'Favorilerden çıkar' : 'Favorilere ekle'}
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
