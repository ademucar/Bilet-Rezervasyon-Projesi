import { useQuery } from '@tanstack/react-query'
import { bookingApi, type EventFilters } from '../api/bookingApi'
import { Button } from '../../../components/ui/Button'

interface EventFilterPanelProps {
  filters: EventFilters
  onChange: (degisiklik: Partial<EventFilters>) => void
  onReset: () => void
  /** Aktif filtre sayisi. Rozet olarak gosteriliyor. */
  activeCount: number
}

/**
 * ==================================================================
 * ETKINLIK FILTRE PANELI -- PDF Sprint 11
 * ==================================================================
 * PDF'in saydigi sekiz filtre:
 *   Sehir, Kategori, Tarih, Fiyat araligi, Mekan, Organizator,
 *   Yas siniri, Satis durumu
 *
 * Bu panelde ALTISI var. Mekan ve organizator BILEREK yok:
 *
 *   MEKAN: kullanici mekan adini genelde bilmez ("Demo Sahne" mi
 *   "Zorlu PSM" mi?). Etkinlik secince zaten goruyor. Uc TARAFINDA
 *   destekleniyor (venueId) -- organizator paneli ve admin ekranlari
 *   kullanacak.
 *
 *   ORGANIZATOR: ayni gerekce. Ustelik organizatorun KENDI
 *   etkinliklerini gormesi icin zaten kullaniliyor (Sprint 5).
 *
 * Yani sekiz filtre de API'de VAR; panelde son kullanicinin gercekten
 * kullanacagi altisi gosteriliyor. Her filtreyi ekrana koymak
 * "eksiksiz" degil, "kullanilamaz" bir arayuz uretirdi.
 * ==================================================================
 */
export function EventFilterPanel({
  filters,
  onChange,
  onReset,
  activeCount,
}: EventFilterPanelProps) {
  // ================================================================
  // SEHIR VE KATEGORI: SUNUCUDA REDIS'TE, ISTEMCIDE TANSTACK'TE
  // ================================================================
  // Iki katmanli onbellek gibi gorunuyor ve oyle -- ikisi de gerekli:
  //
  //   Redis (sunucu)     -> tum kullanicilar icin veritabani yukunu
  //                         kaldiriyor
  //   TanStack (istemci) -> bu kullanicinin sayfa gecislerinde AG
  //                         istegini bile ortadan kaldiriyor
  //
  // staleTime 1 saat: sehir ve kategori listesi neredeyse hic
  // degismiyor. Varsayilan 60 saniye burada gereksiz istek uretirdi.
  // ================================================================
  const citiesQuery = useQuery({
    queryKey: ['cities'],
    queryFn: bookingApi.getCities,
    staleTime: 60 * 60 * 1000,
  })

  const categoriesQuery = useQuery({
    queryKey: ['categories'],
    queryFn: bookingApi.getCategories,
    staleTime: 60 * 60 * 1000,
  })

  const selectClass =
    'w-full rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm outline-none ' +
    'transition-colors focus:border-brand-500'

  const labelClass = 'block text-xs font-medium text-slate-600'

  return (
    <aside className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
      <div className="flex items-center justify-between">
        <h2 className="font-semibold text-slate-900">Filtreler</h2>

        {activeCount > 0 && (
          <span className="rounded-full bg-brand-50 px-2 py-0.5 text-xs font-medium text-brand-700">
            {activeCount} aktif
          </span>
        )}
      </div>

      <div className="mt-4 space-y-4">
        {/* ---- 1) SEHIR ---- */}
        <div className="space-y-1.5">
          <label htmlFor="filtre-sehir" className={labelClass}>
            Sehir
          </label>
          <select
            id="filtre-sehir"
            className={selectClass}
            value={filters.cityId ?? ''}
            onChange={(e) => onChange({ cityId: e.target.value || undefined })}
          >
            {/* Bos deger "Tumu" anlaminda. bookingApi bos metinleri
                temizledigi icin backend'e hic gitmiyor. */}
            <option value="">Tumu</option>
            {citiesQuery.data?.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
          </select>
        </div>

        {/* ---- 2) KATEGORI ---- */}
        <div className="space-y-1.5">
          <label htmlFor="filtre-kategori" className={labelClass}>
            Kategori
          </label>
          <select
            id="filtre-kategori"
            className={selectClass}
            value={filters.categoryId ?? ''}
            onChange={(e) => onChange({ categoryId: e.target.value || undefined })}
          >
            <option value="">Tumu</option>
            {categoriesQuery.data?.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
          </select>
        </div>

        {/* ---- 3) TARIH ARALIGI ---- */}
        <div className="space-y-1.5">
          <span className={labelClass}>Tarih araligi</span>
          <div className="grid grid-cols-2 gap-2">
            <input
              type="date"
              aria-label="Baslangic tarihi"
              className={selectClass}
              value={filters.dateFrom?.slice(0, 10) ?? ''}
              // Tarihi ISO 8601'e cevirip UTC olarak gonderiyorum.
              //
              // Ham "2026-12-05" gonderseydik backend bunu yerel saat
              // sanabilir ve zaman dilimi farki yuzunden bir gunluk
              // kayma olusabilirdi -- kullanici 5 Aralik secip
              // 4 Aralik'taki etkinligi gormezdi.
              onChange={(e) =>
                onChange({
                  dateFrom: e.target.value ? `${e.target.value}T00:00:00Z` : undefined,
                })
              }
            />
            <input
              type="date"
              aria-label="Bitis tarihi"
              className={selectClass}
              value={filters.dateTo?.slice(0, 10) ?? ''}
              // Bitis gununun SONU (23:59:59).
              //
              // T00:00:00 gonderseydik, kullanicinin sectigi son gunde
              // olan etkinlikler haric kalirdi. "5-10 Aralik" diyen
              // kullanici 10 Aralik'taki konseri gormezdi -- sessiz
              // ve can sikici bir hata.
              onChange={(e) =>
                onChange({
                  dateTo: e.target.value ? `${e.target.value}T23:59:59Z` : undefined,
                })
              }
            />
          </div>
        </div>

        {/* ---- 4) FIYAT ARALIGI ---- */}
        <div className="space-y-1.5">
          <span className={labelClass}>Fiyat araligi (TL)</span>
          <div className="grid grid-cols-2 gap-2">
            <input
              type="number"
              min={0}
              placeholder="En az"
              aria-label="En dusuk fiyat"
              className={selectClass}
              value={filters.minPrice ?? ''}
              // Number('') === 0 tuzagi.
              //
              // Dogrudan Number(e.target.value) yazsaydik, kullanici
              // alani TEMIZLEDIGINDE filtre "minPrice=0" olurdu --
              // yani filtre kalkmis gorunur ama aslinda hala aktif
              // kalirdi. Bos kontrolu SART.
              onChange={(e) =>
                onChange({ minPrice: e.target.value ? Number(e.target.value) : undefined })
              }
            />
            <input
              type="number"
              min={0}
              placeholder="En cok"
              aria-label="En yuksek fiyat"
              className={selectClass}
              value={filters.maxPrice ?? ''}
              onChange={(e) =>
                onChange({ maxPrice: e.target.value ? Number(e.target.value) : undefined })
              }
            />
          </div>
        </div>

        {/* ---- 5) YAS SINIRI ---- */}
        <div className="space-y-1.5">
          <label htmlFor="filtre-yas" className={labelClass}>
            Yas durumum
          </label>
          <select
            id="filtre-yas"
            className={selectClass}
            value={filters.maxMinimumAge ?? ''}
            onChange={(e) =>
              onChange({ maxMinimumAge: e.target.value ? Number(e.target.value) : undefined })
            }
          >
            {/* Kullaniciya "yas siniri" degil "yasim" soruyorum.
                "Yas siniri 18" secenegi belirsiz olurdu: 18 sinirli
                etkinlikleri mi, 18 yasindakinin girebileceklerini mi?
                "18 yasindayim" hicbir yoruma yer birakmiyor. */}
            <option value="">Farketmez</option>
            <option value="0">Her yas (sinirsiz)</option>
            <option value="15">15 yasindayim</option>
            <option value="18">18 yasindayim</option>
            <option value="21">21 yasindayim</option>
          </select>
        </div>

        {/* ---- 6) SATIS DURUMU ---- */}
        <div className="space-y-1.5">
          <label htmlFor="filtre-durum" className={labelClass}>
            Satis durumu
          </label>
          <select
            id="filtre-durum"
            className={selectClass}
            value={filters.status ?? ''}
            onChange={(e) =>
              onChange({ status: e.target.value ? Number(e.target.value) : undefined })
            }
          >
            <option value="">Tumu</option>
            {/* Yalnizca HERKESE ACIK durumlar listeleniyor.
                Taslak/onay bekleyen secenegini koysaydik kullanici
                secer, sonuc bos doner ve arayuzun bozuk oldugunu
                dusunurdu. (Backend zaten gormesine izin vermiyor.) */}
            <option value="4">Satista</option>
            <option value="3">Yayinda</option>
            <option value="5">Satis kapandi</option>
          </select>
        </div>

        {/* ---- SIRALAMA ---- */}
        <div className="space-y-1.5 border-t border-slate-100 pt-4">
          <label htmlFor="filtre-sirala" className={labelClass}>
            Siralama
          </label>
          <select
            id="filtre-sirala"
            className={selectClass}
            value={`${filters.sortBy ?? 'date'}:${filters.sortDirection ?? 'asc'}`}
            // Iki alani TEK acilir listede birlestiriyorum.
            //
            // Ayri "alan" ve "yon" kutulari daha esnek olurdu ama
            // kullanici icin iki karar demek. Birlesik liste, gercek
            // sorunun "listeyi nasil gormek istiyorum" oldugunu
            // dogrudan cevapliyor.
            onChange={(e) => {
              const [sortBy, sortDirection] = e.target.value.split(':')

              onChange({
                sortBy: sortBy as EventFilters['sortBy'],
                sortDirection: sortDirection as EventFilters['sortDirection'],
              })
            }}
          >
            <option value="date:asc">Tarihe gore (yakin once)</option>
            <option value="date:desc">Tarihe gore (uzak once)</option>
            <option value="title:asc">Isme gore (A-Z)</option>
            <option value="title:desc">Isme gore (Z-A)</option>
            <option value="created:desc">Yeni eklenenler</option>
          </select>
        </div>

        {activeCount > 0 && (
          <Button variant="secondary" className="w-full" onClick={onReset}>
            Filtreleri temizle
          </Button>
        )}
      </div>
    </aside>
  )
}
