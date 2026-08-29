import { useQuery } from '@tanstack/react-query'
import { bookingApi, type EventFilters } from '../api/bookingApi'
import { Button } from '../../../components/ui/Button'

interface EventFilterPanelProps {
  filters: EventFilters
  onChange: (degisiklik: Partial<EventFilters>) => void
  onReset: () => void
  /** Aktif filtre sayısı. Rozet olarak gösteriliyor. */
  activeCount: number
}

/**
 * ==================================================================
 * ETKİNLİK FILTRE PANELİ -- PDF Sprint 11
 * ==================================================================
 * PDF'in saydığı sekiz filtre:
 *   Şehir, Kategori, Tarih, Fiyat aralığı, Mekan, Organizatör,
 *   Yaş sınırı, Satış durumu
 *
 * Bu panelde ALTISI var. Mekan ve organizatör BILEREK yok:
 *
 *   MEKAN: kullanıcı mekan adını genelde bilmez ("Demo Sahne" mi
 *   "Zorlu PSM" mi?). Etkinlik secince zaten görüyor. Uc TARAFINDA
 *   destekleniyor (venueId) -- organizatör paneli ve admin ekranlari
 *   kullanacak.
 *
 *   ORGANİZATÖR: aynı gerekce. Ustelik organizatorun KENDİ
 *   etkinliklerini gormesi için zaten kullanılıyor (Sprint 5).
 *
 * Yani sekiz filtre de API'de VAR; panelde son kullanıcının gerçekten
 * kullanacagi altisi gösteriliyor. Her filtreyi ekrana koymak
 * "eksiksiz" değil, "kullanilamaz" bir arayüz üretirdi.
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
  // Iki katmanli önbellek gibi görünüyor ve oyle -- ikisi de gerekli:
  //
  //   Redis (sunucu)     -> tüm kullanıcılar için veritabani yukunu
  //                         kaldiriyor
  //   TanStack (istemci) -> bu kullanıcının sayfa gecislerinde AG
  //                         istegini bile ortadan kaldiriyor
  //
  // staleTime 1 saat: şehir ve kategori listesi neredeyse hiç
  // degismiyor. Varsayılan 60 saniye burada gereksiz istek üretirdi.
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
    <aside className="rounded-[4px] border border-slate-300 bg-white p-5">
      <div className="flex items-center justify-between">
        <h2 className="font-display font-semibold text-slate-900">Filtreler</h2>

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
            Şehir
          </label>
          <select
            id="filtre-sehir"
            className={selectClass}
            value={filters.cityId ?? ''}
            onChange={(e) => onChange({ cityId: e.target.value || undefined })}
          >
            {/* Boş deger "Tümü" anlaminda. bookingApi boş metinleri
                temizledigi için backend'e hiç gitmiyor. */}
            <option value="">Tümü</option>
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
            <option value="">Tümü</option>
            {categoriesQuery.data?.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
          </select>
        </div>

        {/* ---- 3) TARIH ARALIGI ---- */}
        <div className="space-y-1.5">
          <span className={labelClass}>Tarih aralığı</span>
          <div className="grid grid-cols-2 gap-2">
            <input
              type="date"
              aria-label="Başlangıç tarihi"
              className={selectClass}
              value={filters.dateFrom?.slice(0, 10) ?? ''}
              // Tarihi ISO 8601'e cevirip UTC olarak gonderiyorum.
              //
              // Ham "2026-12-05" gonderseydik backend bunu yerel saat
              // sanabilir ve zaman dilimi farki yuzunden bir günlük
              // kayma olusabilirdi -- kullanıcı 5 Aralik seçip
              // 4 Aralik'taki etkinliği gormezdi.
              onChange={(e) =>
                onChange({
                  dateFrom: e.target.value ? `${e.target.value}T00:00:00Z` : undefined,
                })
              }
            />
            <input
              type="date"
              aria-label="Bitiş tarihi"
              className={selectClass}
              value={filters.dateTo?.slice(0, 10) ?? ''}
              // Bitiş gununun SONU (23:59:59).
              //
              // T00:00:00 gonderseydik, kullanıcının sectigi son günde
              // olan etkinlikler haric kalırdı. "5-10 Aralik" diyen
              // kullanıcı 10 Aralik'taki konseri gormezdi -- sessiz
              // ve can sıkıcı bir hata.
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
          <span className={labelClass}>Fiyat aralığı (TL)</span>
          <div className="grid grid-cols-2 gap-2">
            <input
              type="number"
              min={0}
              placeholder="En az"
              aria-label="En düşük fiyat"
              className={selectClass}
              value={filters.minPrice ?? ''}
              // Number('') === 0 tuzagi.
              //
              // Dogrudan Number(e.target.value) yazsaydık, kullanıcı
              // alanı TEMIZLEDIGINDE filtre "minPrice=0" olurdu --
              // yani filtre kalkmis görünür ama aslında hâlâ aktif
              // kalırdı. Boş kontrolü ŞART.
              onChange={(e) =>
                onChange({ minPrice: e.target.value ? Number(e.target.value) : undefined })
              }
            />
            <input
              type="number"
              min={0}
              placeholder="En çok"
              aria-label="En yüksek fiyat"
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
            Yaş durumum
          </label>
          <select
            id="filtre-yas"
            className={selectClass}
            value={filters.maxMinimumAge ?? ''}
            onChange={(e) =>
              onChange({ maxMinimumAge: e.target.value ? Number(e.target.value) : undefined })
            }
          >
            {/* Kullanıcıya "yaş sınırı" değil "yasim" soruyorum.
                "Yaş sınırı 18" secenegi belirsiz olurdu: 18 sinirli
                etkinlikleri mi, 18 yasindakinin girebileceklerini mi?
                "18 yaşındayım" hiçbir yoruma yer birakmiyor. */}
            <option value="">Farketmez</option>
            <option value="0">Her yaş (sınırsız)</option>
            <option value="15">15 yaşındayım</option>
            <option value="18">18 yaşındayım</option>
            <option value="21">21 yaşındayım</option>
          </select>
        </div>

        {/* ---- 6) SATIS DURUMU ---- */}
        <div className="space-y-1.5">
          <label htmlFor="filtre-durum" className={labelClass}>
            Satış durumu
          </label>
          <select
            id="filtre-durum"
            className={selectClass}
            value={filters.status ?? ''}
            onChange={(e) =>
              onChange({ status: e.target.value ? Number(e.target.value) : undefined })
            }
          >
            <option value="">Tümü</option>
            {/* Yalnızca HERKESE ACIK durumlar listeleniyor.
                Taslak/onay bekleyen secenegini koysaydık kullanıcı
                secer, sonuç boş döner ve arayuzun bozuk olduğunu
                dusunurdu. (Backend zaten gormesine izin vermiyor.) */}
            <option value="4">Satışta</option>
            <option value="3">Yayında</option>
            <option value="5">Satış kapandı</option>
          </select>
        </div>

        {/* ---- SIRALAMA ---- */}
        <div className="space-y-1.5 border-t border-slate-100 pt-4">
          <label htmlFor="filtre-sirala" className={labelClass}>
            Sıralama
          </label>
          <select
            id="filtre-sirala"
            className={selectClass}
            value={`${filters.sortBy ?? 'date'}:${filters.sortDirection ?? 'asc'}`}
            // Iki alanı TEK açılır listede birlestiriyorum.
            //
            // Ayrı "alan" ve "yon" kutulari daha esnek olurdu ama
            // kullanıcı için iki karar demek. Birlesik liste, gerçek
            // sorunun "listeyi nasil gormek istiyorum" olduğunu
            // doğrudan cevapliyor.
            onChange={(e) => {
              const [sortBy, sortDirection] = e.target.value.split(':')

              onChange({
                sortBy: sortBy as EventFilters['sortBy'],
                sortDirection: sortDirection as EventFilters['sortDirection'],
              })
            }}
          >
            <option value="date:asc">Tarihe göre (yakın önce)</option>
            <option value="date:desc">Tarihe göre (uzak önce)</option>
            <option value="title:asc">İsme göre (A-Z)</option>
            <option value="title:desc">İsme göre (Z-A)</option>
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
