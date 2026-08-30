import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { Button } from '../../../components/ui/Button'
import { Input } from '../../../components/ui/Input'
import { toProblem } from '../../../lib/api/client'
import { formatMoney } from '../../../lib/format'
import { ticketTypeApi, type TicketTypeDto } from '../api/organizerApi'

interface Props {
  eventId: string
  turler: TicketTypeDto[]
  yukleniyor: boolean
  duzenlenebilir: boolean
  onDegisti: () => void
  onHata: (mesaj: string) => void
}

/**
 * Bilet türü ve fiyat tanımlama -- PDF Sprint 6.
 *
 * PDF örnek türler sayıyor: Standard, Student, VIP, EarlyBird,
 * Balcony, FrontStage. Bunları sabit bir listeye koymadım;
 * organizatör kendi adını yazsın. "Öğrenci" ile "Student" arasında
 * seçim yapmak zorunda bırakmak, Türkçe bir sitede tuhaf olurdu.
 *
 * Fiyat değiştirme için ayrı bir uç var (PUT /ticket-types/{id}/price)
 * çünkü fiyat değişikliği denetim kaydı gerektiriyor. Bu ekranda
 * yalnızca ekleme ve silme var; fiyat değişikliğini ayrı bir adım
 * olarak bırakmak, yanlışlıkla fiyat değiştirmeyi zorlaştırıyor.
 */
export function TicketTypePanel({
  eventId,
  turler,
  yukleniyor,
  duzenlenebilir,
  onDegisti,
  onHata,
}: Props) {
  const [acik, setAcik] = useState(false)
  const [ad, setAd] = useState('')
  const [fiyat, setFiyat] = useState('')
  const [kota, setKota] = useState('')
  const [ogrenci, setOgrenci] = useState(false)

  const ekle = useMutation({
    mutationFn: () =>
      ticketTypeApi.create(eventId, {
        name: ad.trim(),
        price: Number(fiyat),
        currency: 'TRY',
        // Bos birakilirsa kota YOK demek: salon kapasitesi kadar
        // satilabilir. 0 gondermek "hic satilamaz" anlamina
        // gelirdi -- bu ikisini karistirmak kolay, o yuzden bos
        // degeri acikca null'a ceviriyorum.
        quota: kota.trim() === '' ? null : Number(kota),
        requiresStudentVerification: ogrenci,
      }),
    onSuccess: () => {
      setAcik(false)
      setAd('')
      setFiyat('')
      setKota('')
      setOgrenci(false)
      onDegisti()
    },
    onError: (e) => onHata(toProblem(e).detail ?? 'Bilet türü eklenemedi.'),
  })

  const sil = useMutation({
    mutationFn: (id: string) => ticketTypeApi.remove(id),
    onSuccess: onDegisti,
    onError: (e) => onHata(toProblem(e).detail ?? 'Bilet türü silinemedi.'),
  })

  const gecerli = ad.trim().length > 0 && fiyat.trim() !== '' && Number(fiyat) >= 0

  return (
    <section className="mt-6 rounded-[4px] border border-slate-300 bg-white p-5">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 className="font-display font-semibold text-slate-900">
          Bilet türleri <span className="num text-slate-400">({turler.length})</span>
        </h2>
        {duzenlenebilir && (
          <Button variant="secondary" onClick={() => setAcik((v) => !v)}>
            {acik ? 'Vazgeç' : 'Bilet türü ekle'}
          </Button>
        )}
      </div>

      {yukleniyor && <div className="mt-4 h-16 animate-pulse rounded-[4px] bg-slate-100" />}

      {!yukleniyor && turler.length === 0 && !acik && (
        <p className="mt-3 text-[13px] text-slate-500">
          Henüz bilet türü yok. En az bir tür tanımlanmadan koltuklar fiyatlandırılamaz.
        </p>
      )}

      {turler.length > 0 && (
        <ul className="mt-4 divide-y divide-slate-100">
          {turler.map((t) => (
            <li key={t.id} className="flex flex-wrap items-center justify-between gap-2 py-2.5">
              <div className="min-w-0">
                <p className="text-sm font-medium text-slate-900">
                  {t.name}
                  {t.requiresStudentVerification && (
                    <span className="label-xs ml-2 border border-slate-300 px-1.5 py-[2px] text-slate-500">
                      Öğrenci belgesi
                    </span>
                  )}
                </p>
                <p className="text-xs text-slate-500">
                  {t.quota === null ? (
                    'Kota yok'
                  ) : (
                    <>
                      Kota: <span className="num">{t.quota}</span>
                    </>
                  )}
                  {t.assignedSectionIds.length > 0 && (
                    <>
                      {' '}
                      &middot; <span className="num">{t.assignedSectionIds.length}</span> bölüme
                      atanmış
                    </>
                  )}
                </p>
              </div>

              <div className="flex items-center gap-3">
                <span className="num text-sm font-semibold text-slate-900">
                  {formatMoney(t.price, t.currency)}
                </span>
                {duzenlenebilir && (
                  <button
                    type="button"
                    onClick={() => sil.mutate(t.id)}
                    className="text-xs text-slate-500 underline hover:text-red-700"
                  >
                    Sil
                  </button>
                )}
              </div>
            </li>
          ))}
        </ul>
      )}

      {acik && (
        <div className="mt-4 space-y-4 border-t border-slate-200 pt-4">
          <div className="grid gap-4 sm:grid-cols-3">
            <Input
              label="Tür adı"
              placeholder="Tam, Öğrenci, VIP..."
              value={ad}
              onChange={(e) => setAd(e.target.value)}
            />
            <Input
              label="Fiyat (TL)"
              type="number"
              min="0"
              step="0.01"
              value={fiyat}
              onChange={(e) => setFiyat(e.target.value)}
            />
            <Input
              label="Kota (boş = sınırsız)"
              type="number"
              min="1"
              value={kota}
              onChange={(e) => setKota(e.target.value)}
            />
          </div>

          <label className="flex items-center gap-2 text-sm text-slate-700">
            <input
              type="checkbox"
              checked={ogrenci}
              onChange={(e) => setOgrenci(e.target.checked)}
              className="size-4 rounded-[2px] border-slate-300"
            />
            Öğrenci belgesi gerektirir
          </label>

          <Button onClick={() => ekle.mutate()} isLoading={ekle.isPending} disabled={!gecerli}>
            Türü ekle
          </Button>
        </div>
      )}
    </section>
  )
}
