import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert } from '../../../components/ui/Alert'
import { Button } from '../../../components/ui/Button'
import { Input } from '../../../components/ui/Input'
import { toProblem } from '../../../lib/api/client'
import type { CategoryDto } from '../../booking/api/bookingApi'
import { categoryApi, slugOner, type SaveCategoryBody } from '../api/referenceApi'

const BOS: SaveCategoryBody = { name: '', slug: '', iconName: null, displayOrder: 0 }

/**
 * Kategori yönetimi -- PDF sayfa 5.
 *
 * Ekleme, düzenleme ve silme aynı formu kullanıyor: düzenlemeye
 * basınca form o kategorinin değerleriyle doluyor. İki ayrı form
 * yazsaydım (biri ekleme, biri düzenleme) alanları, doğrulamayı ve
 * slug önerisini iki yerde tutmam gerekirdi.
 */
export function CategoryPanel() {
  const queryClient = useQueryClient()
  const [taslak, setTaslak] = useState<SaveCategoryBody>(BOS)
  const [duzenlenen, setDuzenlenen] = useState<string | null>(null)
  const [hata, setHata] = useState<string | null>(null)

  // Slug'ı kullanıcı elle değiştirdiyse bir daha üzerine yazmıyorum.
  // Bu bayrak olmasaydı, adı düzeltmek için tek harf eklediğinde
  // özenle yazdığı slug silinirdi.
  const [slugElle, setSlugElle] = useState(false)

  const sorgu = useQuery({ queryKey: ['adminCategories'], queryFn: categoryApi.list })

  const tazele = () => {
    queryClient.invalidateQueries({ queryKey: ['adminCategories'] })
    // Filtre açılır listeleri de aynı veriyi kullanıyor.
    queryClient.invalidateQueries({ queryKey: ['categories'] })
  }

  const sifirla = () => {
    setTaslak(BOS)
    setDuzenlenen(null)
    setSlugElle(false)
    setHata(null)
  }

  const kaydet = useMutation({
    // Dallarin ikisi de void donmeli: create bir Guid donuyor,
    // update donmuyor. Tipleri esitlemezsem TanStack Query
    // mutationFn'i cozemiyor. Donen kimligi zaten kullanmiyorum --
    // listeyi bastan cekiyorum.
    mutationFn: async (): Promise<void> => {
      if (duzenlenen) {
        await categoryApi.update(duzenlenen, taslak)
        return
      }

      await categoryApi.create(taslak)
    },
    onSuccess: () => {
      sifirla()
      tazele()
    },
    onError: (e) => setHata(toProblem(e).detail ?? 'Kaydedilemedi.'),
  })

  const sil = useMutation({
    mutationFn: (id: string) => categoryApi.remove(id),
    onSuccess: tazele,
    onError: (e) => setHata(toProblem(e).detail ?? 'Silinemedi.'),
  })

  const duzenle = (k: CategoryDto) => {
    setDuzenlenen(k.id)
    setTaslak({
      name: k.name,
      slug: k.slug,
      iconName: k.iconName,
      displayOrder: 0,
    })
    // Var olan kaydın slug'ı zaten yazılmış; ada dokunulunca
    // üzerine yazılmasın.
    setSlugElle(true)
    setHata(null)
  }

  const gecerli = taslak.name.trim().length > 0 && taslak.slug.trim().length > 0

  return (
    <section className="rounded-[4px] border border-slate-300 bg-white p-5">
      <h2 className="font-display font-semibold text-slate-900">
        Kategoriler <span className="num text-slate-400">({sorgu.data?.length ?? 0})</span>
      </h2>

      {hata && (
        <div className="mt-3">
          <Alert variant="error">{hata}</Alert>
        </div>
      )}

      {sorgu.isPending && <div className="mt-4 h-32 animate-pulse rounded-[4px] bg-slate-200" />}

      {sorgu.data && sorgu.data.length > 0 && (
        <ul className="mt-4 divide-y divide-slate-100">
          {sorgu.data.map((k) => (
            <li key={k.id} className="flex flex-wrap items-center justify-between gap-2 py-2.5">
              <div className="min-w-0">
                <p className="text-sm font-medium text-slate-900">{k.name}</p>
                <p className="num text-xs text-slate-500">{k.slug}</p>
              </div>
              <div className="flex shrink-0 gap-1.5">
                <button
                  type="button"
                  onClick={() => duzenle(k)}
                  className="text-[13px] font-medium text-brand-600 hover:underline"
                >
                  Düzenle
                </button>
                <span className="text-slate-300">·</span>
                <button
                  type="button"
                  onClick={() => {
                    setHata(null)
                    sil.mutate(k.id)
                  }}
                  className="text-[13px] font-medium text-slate-500 hover:text-red-700 hover:underline"
                >
                  Sil
                </button>
              </div>
            </li>
          ))}
        </ul>
      )}

      <div className="mt-4 space-y-4 border-t border-slate-200 pt-4">
        <p className="label-xs text-slate-500">
          {duzenlenen ? 'Kategoriyi düzenle' : 'Yeni kategori'}
        </p>

        <Input
          label="Ad"
          value={taslak.name}
          onChange={(e) => {
            const ad = e.target.value
            setTaslak((t) => ({
              ...t,
              name: ad,
              slug: slugElle ? t.slug : slugOner(ad),
            }))
          }}
        />

        <div>
          <Input
            label="Slug"
            value={taslak.slug}
            onChange={(e) => {
              setSlugElle(true)
              setTaslak((t) => ({ ...t, slug: e.target.value }))
            }}
          />
          <p className="mt-1 text-xs text-slate-500">
            Adreste geçer: <span className="num">/etkinlikler?kategori={taslak.slug || '...'}</span>
            {duzenlenen && ' — değiştirirseniz eski bağlantılar kırılır.'}
          </p>
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
