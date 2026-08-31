import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { AdminLayout } from '../components/AdminLayout'
import { Alert } from '../../../components/ui/Alert'
import { Button } from '../../../components/ui/Button'
import { toProblem } from '../../../lib/api/client'
import { formatDateTime } from '../../../lib/format'
import { auditApi, degerleriOku, islemEtiketi, type AuditLogListItem } from '../api/userAdminApi'

// Backend EntityName'i tip adiyla donuyor ("Event", "User"). Suzgec
// dugmeleri o adlari kullaniyor; etiketler Turkce.
const TUR_SUZGECLERI: { etiket: string; deger?: string }[] = [
  { etiket: 'Tümü' },
  { etiket: 'Etkinlik', deger: 'Event' },
  { etiket: 'Kullanıcı', deger: 'User' },
  { etiket: 'Başvuru', deger: 'OrganizerApplication' },
  { etiket: 'Bilet türü', deger: 'TicketType' },
]

/**
 * Denetim kayıtları -- PDF sayfa 5:
 * "Admin: Audit log kayıtlarını inceleyebilir."
 *
 * SERİLOG VARKEN BU EKRAN NEDEN VAR?
 *
 * İkisi farklı sorulara cevap veriyor. Serilog "sistemde ne oldu?"
 * sorusunun cevabı: teknik akış, hata ayıklama için, ve dosya sink'i
 * 14 gün sonra dönüyor. Denetim kaydı ise "bu KAYIT üzerinde kim ne
 * değiştirdi?" sorusunun cevabı: iş sorusudur, kaydın kendisiyle
 * birlikte yaşar ve silinmez.
 *
 * Bir müşteri altı ay sonra "bilet fiyatım neden değişti" diye
 * sorarsa Serilog'da hiçbir şey bulunmaz; burada eski ve yeni fiyat,
 * değiştiren kişi ve tarih durur.
 */
export function AuditLogsPage() {
  const [tur, setTur] = useState<string | undefined>(undefined)
  const [sayfa, setSayfa] = useState(1)

  const sorgu = useQuery({
    queryKey: ['auditLogs', tur, sayfa],
    queryFn: () => auditApi.list({ entityName: tur, pageNumber: sayfa, pageSize: 25 }),
  })

  return (
    <AdminLayout title="Denetim kayıtları" subtitle="Kim, ne zaman, neyi değiştirdi">
      <div className="mb-4 flex flex-wrap gap-2">
        {TUR_SUZGECLERI.map((s) => (
          <button
            key={s.etiket}
            type="button"
            onClick={() => {
              setTur(s.deger)
              setSayfa(1)
            }}
            aria-pressed={tur === s.deger}
            className={`rounded-[4px] border px-2.5 py-1 text-xs font-medium transition-colors ${
              tur === s.deger
                ? 'border-slate-900 bg-slate-900 text-white'
                : 'border-slate-300 bg-white text-slate-600 hover:border-slate-900'
            }`}
          >
            {s.etiket}
          </button>
        ))}
      </div>

      {sorgu.isError && (
        <Alert variant="error">
          {toProblem(sorgu.error).detail ?? 'Denetim kayıtları yüklenemedi.'}
        </Alert>
      )}

      {sorgu.isPending && (
        <ul className="space-y-2" aria-busy="true" aria-label="Kayıtlar yükleniyor">
          {[1, 2, 3].map((i) => (
            <li key={i} className="h-20 animate-pulse rounded-[4px] bg-slate-200" />
          ))}
        </ul>
      )}

      {sorgu.data && sorgu.data.items.length === 0 && (
        <div className="rounded-[4px] border border-slate-300 bg-white px-5 py-10 text-center">
          <p className="text-sm text-slate-500">Bu filtrede denetim kaydı yok.</p>
          <p className="mt-1 text-xs text-slate-400">
            Kayıtlar yalnızca işlem yapıldıkça oluşur; elle eklenemez.
          </p>
        </div>
      )}

      {sorgu.data && sorgu.data.items.length > 0 && (
        <>
          <p className="label-xs mb-2 text-slate-500">
            <span className="num">{sorgu.data.totalCount}</span> kayıt
          </p>

          <ul className="space-y-2">
            {sorgu.data.items.map((k) => (
              <KayitSatiri key={k.id} kayit={k} />
            ))}
          </ul>

          {sorgu.data.totalPages > 1 && (
            <div className="mt-4 flex items-center justify-between gap-3">
              <Button
                variant="secondary"
                onClick={() => setSayfa((s) => s - 1)}
                disabled={!sorgu.data.hasPreviousPage}
              >
                Önceki
              </Button>
              <span className="num text-sm text-slate-500">
                {sorgu.data.pageNumber} / {sorgu.data.totalPages}
              </span>
              <Button
                variant="secondary"
                onClick={() => setSayfa((s) => s + 1)}
                disabled={!sorgu.data.hasNextPage}
              >
                Sonraki
              </Button>
            </div>
          )}
        </>
      )}
    </AdminLayout>
  )
}

function KayitSatiri({ kayit }: { kayit: AuditLogListItem }) {
  const eski = degerleriOku(kayit.oldValues)
  const yeni = degerleriOku(kayit.newValues)

  return (
    <li className="rounded-[4px] border border-slate-300 bg-white p-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <p className="font-medium text-slate-900">{islemEtiketi(kayit.action)}</p>
          <p className="mt-0.5 text-[13px] text-slate-500">
            {kayit.userEmail ?? 'Sistem'} &middot;{' '}
            <span className="num">{formatDateTime(kayit.createdAt)}</span>
          </p>
        </div>

        <span className="label-xs shrink-0 border border-slate-300 bg-slate-50 px-1.5 py-[3px] text-slate-600">
          {kayit.entityName}
        </span>
      </div>

      {(eski.length > 0 || yeni.length > 0) && (
        <dl className="mt-3 grid gap-3 border-t border-slate-100 pt-3 text-[13px] sm:grid-cols-2">
          {eski.length > 0 && (
            <div>
              <dt className="label-xs">Önceki</dt>
              <dd className="num mt-1 text-slate-500 line-through">{eski.join(', ')}</dd>
            </div>
          )}
          {yeni.length > 0 && (
            <div>
              <dt className="label-xs">Sonraki</dt>
              <dd className="num mt-1 text-slate-900">{yeni.join(', ')}</dd>
            </div>
          )}
        </dl>
      )}

      {/* IP ve correlation id'yi KUCUK ve en altta tutuyorum.
          Gunluk kullanimda kimse bakmiyor; ama bir olay incelenirken
          correlation id, bu kaydi tetikleyen istegin TUM Serilog
          satirlarina baglayan tek ip ucu. Gizlemek yerine
          onemsizlestirdim. */}
      <p className="num mt-2 text-[11px] text-slate-400">
        {kayit.ipAddress && <>IP {kayit.ipAddress} &middot; </>}
        {kayit.correlationId && <>istek {kayit.correlationId.slice(0, 12)} &middot; </>}
        kayıt {kayit.entityId.slice(0, 8)}
      </p>
    </li>
  )
}
