import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert } from '../../../components/ui/Alert'
import { Button } from '../../../components/ui/Button'
import { Input } from '../../../components/ui/Input'
import { toProblem } from '../../../lib/api/client'
import { cityApi } from '../api/referenceApi'

/**
 * Şehir yönetimi -- PDF sayfa 5.
 *
 * Plaka kodu yalnızca EKLERKEN giriliyor, düzenlerken değil.
 * Backend de öyle davranıyor: plaka şehrin kimliği gibi, 34 her zaman
 * İstanbul. Değiştirilebilir olsaydı iki şehrin plakasını yanlışlıkla
 * takas etmek tek tıklık bir hata olurdu ve fark etmek aylar sürerdi.
 */
export function CityPanel() {
  const queryClient = useQueryClient()
  const [ad, setAd] = useState('')
  const [plaka, setPlaka] = useState('')
  const [duzenlenen, setDuzenlenen] = useState<string | null>(null)
  const [hata, setHata] = useState<string | null>(null)
  const [arama, setArama] = useState('')

  const sorgu = useQuery({ queryKey: ['adminCities'], queryFn: cityApi.list })

  const tazele = () => {
    queryClient.invalidateQueries({ queryKey: ['adminCities'] })
    queryClient.invalidateQueries({ queryKey: ['cities'] })
  }

  const sifirla = () => {
    setAd('')
    setPlaka('')
    setDuzenlenen(null)
    setHata(null)
  }

  const kaydet = useMutation({
    // Iki dal da void donsun: create Guid donuyor, rename donmuyor.
    mutationFn: async (): Promise<void> => {
      if (duzenlenen) {
        await cityApi.rename(duzenlenen, ad)
        return
      }

      await cityApi.create(ad, Number(plaka))
    },
    onSuccess: () => {
      sifirla()
      tazele()
    },
    onError: (e) => setHata(toProblem(e).detail ?? 'Kaydedilemedi.'),
  })

  const sil = useMutation({
    mutationFn: (id: string) => cityApi.remove(id),
    onSuccess: tazele,
    onError: (e) => setHata(toProblem(e).detail ?? 'Silinemedi.'),
  })

  // 81 il var; hepsini alt alta göstermek listeyi kullanılmaz yapıyor.
  // Arama kutusu, sunucuya gitmeden bellekteki listeyi süzüyor --
  // veri zaten tamamen elimizde ve 81 satır için istek atmak israf.
  const suzulmus = (sorgu.data ?? []).filter((s) =>
    s.name.toLocaleLowerCase('tr').includes(arama.toLocaleLowerCase('tr')),
  )

  const gecerli = duzenlenen
    ? ad.trim().length > 0
    : ad.trim().length > 0 && Number(plaka) >= 1 && Number(plaka) <= 81

  return (
    <section className="rounded-[4px] border border-slate-300 bg-white p-5">
      <h2 className="font-display font-semibold text-slate-900">
        Şehirler <span className="num text-slate-400">({sorgu.data?.length ?? 0})</span>
      </h2>

      {hata && (
        <div className="mt-3">
          <Alert variant="error">{hata}</Alert>
        </div>
      )}

      <div className="mt-3">
        <Input
          label="Ara"
          placeholder="Şehir adı..."
          value={arama}
          onChange={(e) => setArama(e.target.value)}
        />
      </div>

      {sorgu.isPending && <div className="mt-4 h-32 animate-pulse rounded-[4px] bg-slate-200" />}

      {sorgu.data && (
        <ul className="mt-3 max-h-72 divide-y divide-slate-100 overflow-y-auto">
          {suzulmus.map((s) => (
            <li key={s.id} className="flex items-center justify-between gap-2 py-2">
              <div className="flex min-w-0 items-center gap-2.5">
                <span className="num shrink-0 rounded-[3px] bg-slate-100 px-1.5 py-0.5 text-xs text-slate-600">
                  {String(s.plateCode).padStart(2, '0')}
                </span>
                <span className="truncate text-sm text-slate-900">{s.name}</span>
              </div>
              <div className="flex shrink-0 gap-1.5">
                <button
                  type="button"
                  onClick={() => {
                    setDuzenlenen(s.id)
                    setAd(s.name)
                    setHata(null)
                  }}
                  className="text-[13px] font-medium text-brand-600 hover:underline"
                >
                  Ad değiştir
                </button>
                <span className="text-slate-300">·</span>
                <button
                  type="button"
                  onClick={() => {
                    setHata(null)
                    sil.mutate(s.id)
                  }}
                  className="text-[13px] font-medium text-slate-500 hover:text-red-700 hover:underline"
                >
                  Sil
                </button>
              </div>
            </li>
          ))}

          {suzulmus.length === 0 && (
            <li className="py-6 text-center text-sm text-slate-500">Eşleşen şehir yok.</li>
          )}
        </ul>
      )}

      <div className="mt-4 space-y-4 border-t border-slate-200 pt-4">
        <p className="label-xs text-slate-500">
          {duzenlenen ? 'Şehir adını değiştir' : 'Yeni şehir'}
        </p>

        <div className="grid gap-4 sm:grid-cols-[1fr_auto]">
          <Input label="Ad" value={ad} onChange={(e) => setAd(e.target.value)} />

          {/* Plaka yalnızca eklerken. Düzenlemede alanı gizliyorum,
              devre dışı bırakmıyorum: gri bir kutu "neden
              yazamıyorum?" sorusunu doğurur, olmayan kutu doğurmaz. */}
          {!duzenlenen && (
            <div className="sm:w-28">
              <Input
                label="Plaka"
                type="number"
                min={1}
                max={81}
                value={plaka}
                onChange={(e) => setPlaka(e.target.value)}
              />
            </div>
          )}
        </div>

        <div className="flex flex-wrap gap-2">
          <Button
            onClick={() => {
              setHata(null)
              kaydet.mutate()
            }}
            isLoading={kaydet.isPending}
            disabled={!gecerli}
          >
            {duzenlenen ? 'Kaydet' : 'Ekle'}
          </Button>
          {duzenlenen && (
            <Button variant="secondary" onClick={sifirla}>
              Vazgeç
            </Button>
          )}
        </div>
      </div>
    </section>
  )
}
