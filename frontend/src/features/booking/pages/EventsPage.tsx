import { useCallback, useMemo, useState } from 'react'
import { useQuery, keepPreviousData } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { SiteHeader } from '../../../components/layout/SiteHeader'
import { Alert } from '../../../components/ui/Alert'
import { Button } from '../../../components/ui/Button'
import { Input } from '../../../components/ui/Input'
import { toProblem } from '../../../lib/api/client'
import { formatDateParts, formatDateTime } from '../../../lib/format'
import { ActiveFilterChips } from '../components/ActiveFilterChips'
import { EventFilterPanel } from '../components/EventFilterPanel'
import { bookingApi, EventStatus, type EventFilters, type EventListItem } from '../api/bookingApi'

/**
 * etkinlik listesi -- PDF Sprint 11
 *
 * Sprint 7'de bu sayfa yalnızca metin aramasi ve sayfalama
 * yapiyordu ve su notu birakmistim:
 *
 *   "PDF Sprint 11 gelismis arama ve filtreleri getirecek.
 *    Ekrani simdiden asiri tasarlamiyorum."
 *
 * Sprint 11 geldi: sekiz filtre, sıralama ve popüler etkinlikler.
 *
 */
export function EventsPage() {
  const [search, setSearch] = useState('')

  /**
   * neden tek bir `filters` nesnesi?
   *
   * Her filtre için ayrı useState acabilirdim: cityId, categoryId,
   * minPrice, maxPrice, dateFrom, dateTo, sortBy... on tane state.
   *
   * Sorun sayfalama ile ortaya çıkardı: filtre DEGISTIGINDE sayfayı
   * 1'e dondurmek zorundayız. Ayrı state'lerde bunu ON AYRI onChange
   * içinde tekrarlamak gerekirdi ve birinde unutmak kacinilmazdi --
   * kullanıcı 5. sayfadayken filtre değiştirir, boş sonuç görür ve
   * "arama bozuk" der.
   *
   * Tek nesne + tek güncelleme fonksiyonu ile bu kuralı tek yerde
   * uyguluyorum (bkz. updateFilters).
   *
   */
  const [filters, setFilters] = useState<EventFilters>({
    pageNumber: 1,
    pageSize: 9,
    sortBy: 'date',
    sortDirection: 'asc',
  })

  const updateFilters = useCallback((degisiklik: Partial<EventFilters>) => {
    setFilters((onceki) => ({
      ...onceki,
      ...degisiklik,

      // Filtre değişince her zaman 1. Sayfaya dön.
      //
      // Tek istisna: degisiklik zaten sayfa numarasi ise (kullanıcı
      // "Sonraki"ye basmis). O zaman gelen değeri koruyorum.
      pageNumber: degisiklik.pageNumber ?? 1,
    }))
  }, [])

  const resetFilters = useCallback(() => {
    setSearch('')
    setFilters({ pageNumber: 1, pageSize: 9, sortBy: 'date', sortDirection: 'asc' })
  }, [])

  /**
   * Kac filtre aktif?
   *
   * Sayfalama ve sıralama alanlarini SAYMIYORUM: onlar her zaman
   * dolu. Saysaydik rozet hiçbir filtre secilmemisken bile
   * "4 aktif" yazardi ve anlamsizlasirdi.
   */
  const activeCount = useMemo(() => {
    const sayilmayanlar = new Set(['pageNumber', 'pageSize', 'sortBy', 'sortDirection'])

    return Object.entries(filters).filter(
      ([anahtar, deger]) => !sayilmayanlar.has(anahtar) && deger !== undefined && deger !== '',
    ).length
  }, [filters])

  const eventsQuery = useQuery({
    // queryKey'e TÜM filtreler dahil.
    //
    // Bu sadece bir isim değil, onbellek anahtari: filtre değişince
    // yeni veri çekiliyor, aynı filtreye geri donuldugunde onbellekten
    // anında gösteriliyor.
    queryKey: ['events', filters],
    queryFn: () => bookingApi.getEvents(filters),

    // Sayfa/filtre degisirken eski veriyi ekranda TUT.
    // Olmasaydı her degisiklikte liste bosalip iskelete donerdi.
    placeholderData: keepPreviousData,
  })

  // Populer etkinlikler -- PDF Sprint 11 (Redis'te 10 dakika)
  //
  // YALNIZCA filtresiz gorunumde gösteriliyor.
  //
  // Kullanıcı filtre uyguladiginda ne aradigini biliyor; ustune
  // alakasiz "popüler" onerileri koymak ekrani kalabaliklastirir ve
  // gerçek sonuclari asagi iter.
  const filtresizMi = activeCount === 0 && !search

  const popularQuery = useQuery({
    queryKey: ['events', 'popular'],
    queryFn: () => bookingApi.getPopularEvents(4),
    enabled: filtresizMi,

    // 5 dakika: sunucudaki Redis süresi (10 dk) ile uyumlu.
    // Daha kisa vermek, sunucunun zaten onbellekledigi veriyi
    // gereksiz yere tekrar istemek olurdu.
    staleTime: 5 * 60 * 1000,
  })

  const onSearch = (e: React.FormEvent) => {
    e.preventDefault()
    updateFilters({ search: search.trim() || undefined })
  }

  const data = eventsQuery.data

  return (
    <div className="min-h-screen bg-slate-100">
      <SiteHeader />

      <main className="mx-auto max-w-6xl px-4 py-8">
        <h1 className="font-display text-2xl font-bold tracking-tight text-kagit">Etkinlikler</h1>
        <p className="mt-1 text-sm text-kagit-soluk">
          Bir etkinlik seçin, oturumunu belirleyin ve koltuğunuzu ayırtın.
        </p>

        {/* Arama kutusunu krem kartin ICINE aldim.
            Once dogrudan koyu zemindeydi ve "ARA" etiketi
            okunmuyordu. Etiketi kreme cevirmek yerine kutuyu karta
            almayi sectim: form alanlari zaten krem ve etrafinda
            zemin olmasi onlari havada birakiyordu. */}
        <form
          onSubmit={onSearch}
          className="mt-6 flex flex-wrap gap-3 rounded-[4px] border border-slate-300 bg-white p-4"
        >
          <div className="min-w-64 flex-1">
            <Input
              label="Ara"
              placeholder="Etkinlik adı veya açıklaması"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
            />
          </div>

          <div className="flex items-end">
            <Button type="submit">Ara</Button>
          </div>
        </form>

        {/* ---- POPULER ETKINLIKLER ---- */}
        {filtresizMi && popularQuery.data && popularQuery.data.length > 0 && (
          <section className="mt-8">
            <h2 className="font-display text-lg font-semibold text-kagit">Popüler etkinlikler</h2>
            <p className="mt-0.5 text-xs text-kagit-soluk">En çok bilet satılanlar</p>

            <ul className="mt-3 grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
              {popularQuery.data.map((ev, sira) => (
                <li key={ev.id}>
                  <Link
                    to={`/etkinlikler/${ev.id}`}
                    className="flex h-full items-start gap-3 rounded-[4px] border border-slate-300 bg-white p-3.5 transition-colors hover:border-slate-900"
                  >
                    {/* Sıra numarası artık dolgu daire değil.
                        Dört turuncu daire yan yana dizildiğinde
                        sayfanın en dikkat çeken şeyi oluyorlardı --
                        oysa asıl bilgi etkinliğin ADI.

                        Şimdi mono rakam + ince çerçeve: sıra
                        okunuyor ama başlıkla yarışmıyor. */}
                    <span
                      className="num flex size-6 shrink-0 items-center justify-center border border-slate-300 text-xs font-semibold text-slate-500"
                      aria-hidden="true"
                    >
                      {sira + 1}
                    </span>
                    <div className="min-w-0">
                      <p className="truncate font-medium text-slate-900">{ev.title}</p>
                      <p className="mt-1 truncate text-xs text-slate-500">{ev.venueName}</p>
                      <p className="num text-[11px] text-slate-500">
                        {formatDateTime(ev.eventDate)}
                      </p>
                    </div>
                  </Link>
                </li>
              ))}
            </ul>
          </section>
        )}

        <div className="mt-8 grid gap-6 lg:grid-cols-[260px_1fr]">
          {/* ---- FILTRE PANELİ ---- */}
          <div className="lg:sticky lg:top-6 lg:self-start">
            <EventFilterPanel
              filters={filters}
              onChange={updateFilters}
              onReset={resetFilters}
              activeCount={activeCount}
            />
          </div>

          {/* ---- SONUCLAR ---- */}
          <div>
            {/* Aktif filtreler sonuçların TEPESİNDE, çıkarılabilir
                rozetler hâlinde. Yan panel mobilde yukarıda kalıyor
                ve kullanıcı sonuçlara bakarken neyi filtrelediğini
                göremiyordu. */}
            <ActiveFilterChips
              filters={filters}
              onChange={updateFilters}
              onReset={resetFilters}
              search={filters.search ?? ''}
              onClearSearch={() => {
                setSearch('')
                updateFilters({ search: undefined })
              }}
              totalCount={data?.totalCount}
            />

            {eventsQuery.isError && (
              <Alert variant="error">{toProblem(eventsQuery.error).detail}</Alert>
            )}

            {eventsQuery.isPending && (
              /*
                 İSKELET, GELECEK İÇERİĞİN BİÇİMİNİ TAKLİT ETMELİ
                 Önceki hâl altı tane düz gri dikdörtgendi. Veri
                 gelince sayfa tamamen başka bir şeye dönüşüyordu ve
                 her şey yerinden zıplıyordu.

                 Şimdi iskelet gerçek kartın anatomisini taşıyor:
                 solda 72px'lik takvim bloğu, sağda başlık ve mekan
                 satırları. Veri gelince yalnızca renkler değişiyor,
                 yerleşim aynı kalıyor.

                 PDF Sprint 18: "Skeleton loading hazırlanmalıdır."
                 */
              <div
                className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3"
                aria-busy="true"
                aria-label="Etkinlikler yükleniyor"
              >
                {[1, 2, 3, 4, 5, 6].map((i) => (
                  <div key={i} className="flex rounded-[4px] border border-slate-200 bg-white">
                    <div className="h-[104px] w-[72px] shrink-0 animate-pulse bg-slate-200" />
                    <div className="flex flex-grow flex-col gap-2 p-3.5">
                      <div className="h-3.5 w-3/5 animate-pulse bg-slate-200" />
                      <div className="h-2.5 w-4/5 animate-pulse bg-slate-100" />
                      <div className="mt-auto flex gap-1.5">
                        <div className="h-4 w-14 animate-pulse bg-slate-100" />
                        <div className="h-4 w-16 animate-pulse bg-slate-100" />
                      </div>
                    </div>
                  </div>
                ))}
              </div>
            )}

            {data && data.items.length === 0 && (
              /*
                 "SONUÇ BULUNAMADI" TEK BAŞINA YETMİYOR
                 Eski hâl kullanıcıyı ölü bir ekranda bırakıyordu:
                 hangi filtrenin sonucu boşalttığını kendisi
                 bulmak zorundaydı.

                 Artık üç şey var: ne arandığı, neden boş olduğu ve
                 tek tıkla çıkış yolu.
                 */
              <div className="flex flex-col items-center gap-3 rounded-[4px] border border-slate-300 bg-white px-5 py-10 text-center">
                <svg
                  className="size-8 text-slate-300"
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  strokeWidth="1.4"
                  strokeLinecap="round"
                  strokeLinejoin="round"
                  aria-hidden="true"
                >
                  <circle cx="11" cy="11" r="7" />
                  <path d="m20 20-3.5-3.5" />
                </svg>

                <div>
                  <h3 className="font-display text-[15px] font-semibold text-slate-900">
                    {search ? `"${search}" için sonuç yok` : 'Filtrelere uyan etkinlik yok'}
                  </h3>
                  <p className="mt-1 text-[13px] leading-relaxed text-slate-500">
                    {activeCount > 0
                      ? `${activeCount} filtre birlikte uygulandığında hiçbir etkinlik kalmıyor.`
                      : 'Arama terimini değiştirmeyi deneyin.'}
                  </p>
                </div>

                {(activeCount > 0 || search) && (
                  <div className="mt-1 flex flex-wrap justify-center gap-2">
                    {search && (
                      <button
                        type="button"
                        onClick={() => {
                          setSearch('')
                          updateFilters({ search: undefined })
                        }}
                        className="rounded-[4px] border border-slate-300 px-2.5 py-1.5 text-xs font-medium text-slate-700 hover:bg-slate-50"
                      >
                        Aramayı kaldır
                      </button>
                    )}
                    {activeCount > 0 && (
                      <button
                        type="button"
                        onClick={resetFilters}
                        className="rounded-[4px] border border-slate-300 px-2.5 py-1.5 text-xs font-medium text-slate-700 hover:bg-slate-50"
                      >
                        Tüm filtreleri temizle
                      </button>
                    )}
                  </div>
                )}
              </div>
            )}

            {data && data.items.length > 0 && (
              <>
                <ul className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
                  {data.items.map((ev) => (
                    <li key={ev.id}>
                      <EventCard event={ev} />
                    </li>
                  ))}
                </ul>

                <div className="mt-8 flex items-center justify-between gap-3">
                  <Button
                    variant="secondary"
                    disabled={!data.hasPreviousPage}
                    onClick={() => updateFilters({ pageNumber: data.pageNumber - 1 })}
                  >
                    Önceki
                  </Button>

                  <span className="text-sm text-slate-500">
                    Sayfa <span className="num">{data.pageNumber}</span> /{' '}
                    <span className="num">{data.totalPages}</span>
                  </span>

                  <Button
                    variant="secondary"
                    disabled={!data.hasNextPage}
                    onClick={() => updateFilters({ pageNumber: data.pageNumber + 1 })}
                  >
                    Sonraki
                  </Button>
                </div>
              </>
            )}
          </div>
        </div>
      </main>
    </div>
  )
}

function EventCard({ event }: { event: EventListItem }) {
  const tarih = formatDateParts(event.eventDate)

  return (
    <Link
      to={`/etkinlikler/${event.id}`}
      className="group flex h-full rounded-[4px] border border-slate-300 bg-white transition-colors hover:border-slate-900"
    >
      {/*
          TAKVİM YIRTMACI
          Tarihi cümle olarak yazmıyorum ("27 Ekim 2026 20:00").
          Listede on kart alt alta olduğunda o cümleleri kimse
          okumuyor -- hepsi aynı uzunlukta gri bir şerit gibi
          görünüyordu.

          Ay / gün / saat üç ayrı satır olunca göz doğrudan iri
          rakama gidiyor ve kartlar arasında dikey bir ritim
          kuruluyor: 27, 3, 14...

          Koyu blok aynı zamanda kartın "tutamağı": ekranda göz
          hangi satırdayım sorusunu buradan cevaplıyor.
          */}
      <div className="flex w-[72px] shrink-0 flex-col items-center justify-center border-r border-slate-300 bg-slate-900 py-3 text-white">
        <span className="label-xs text-slate-400">{tarih.ay}</span>
        <span className="num mt-0.5 text-[26px] font-semibold leading-none">{tarih.gun}</span>
        <span className="num mt-1 text-[11px] text-slate-300">{tarih.saat}</span>
      </div>

      <div className="flex min-w-0 flex-grow flex-col gap-1.5 p-3.5">
        <div className="flex items-start justify-between gap-2">
          <h3 className="font-display text-[15px] font-semibold leading-tight text-slate-900">
            {event.title}
          </h3>

          {/* Rozet artık dolgu değil ÇERÇEVE.
              Dolgu rozetler kartın içinde ikinci bir renk lekesi
              oluşturuyordu; ince çerçeve aynı bilgiyi veriyor ama
              başlığın önüne geçmiyor. */}
          {event.status === EventStatus.SalesOpen && (
            <span className="label-xs shrink-0 border border-emerald-300 bg-emerald-50 px-1.5 py-[3px] text-emerald-700">
              Satışta
            </span>
          )}
        </div>

        <p className="flex items-center gap-1.5 truncate text-[13px] text-slate-600">
          {/* Konum ikonu: satırın ne olduğunu kelime harcamadan söylüyor.
              aria-hidden -- ekran okuyucu için zaten metin var. */}
          <svg
            className="size-3.5 shrink-0 text-slate-400"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            strokeWidth="2"
            strokeLinecap="round"
            strokeLinejoin="round"
            aria-hidden="true"
          >
            <path d="M20 10c0 6-8 12-8 12s-8-6-8-12a8 8 0 0 1 16 0Z" />
            <circle cx="12" cy="10" r="3" />
          </svg>
          {event.venueName}, {event.cityName}
        </p>

        {/*
            FİYAT YOK -- ÇÜNKÜ VERİ YOK
            Tasarım taslağında sağ altta "450 ₺'den başlayan" vardı
            ve kart bununla çok daha iyi çalışıyor: kullanıcının ilk
            sorduğu şey fiyat.

            Ama /api/v1/events listesi fiyat DÖNMÜYOR (EventListItem:
            id, title, categoryName, cityName, venueName, poster,
            eventDate, status, minimumAge, sessionCount).

            Olmayan veriyi uydurmak yerine boş bırakıyorum. Fiyatı
            göstermek istersek önce backend'de EventListItem'a
            minPrice eklenmeli -- bu bir frontend işi değil.
            */}
        <div className="mt-auto flex flex-wrap items-center gap-1.5 pt-1">
          <span className="border border-slate-200 px-1.5 py-px text-[11px] text-slate-600">
            {event.categoryName}
          </span>
          <span className="border border-slate-200 px-1.5 py-px text-[11px] text-slate-600">
            <span className="num">{event.sessionCount}</span> oturum
          </span>
          {event.minimumAge > 0 && (
            <span className="border border-amber-300 bg-amber-50 px-1.5 py-px text-[11px] text-amber-700">
              <span className="num">{event.minimumAge}</span>+
            </span>
          )}
        </div>
      </div>
    </Link>
  )
}
