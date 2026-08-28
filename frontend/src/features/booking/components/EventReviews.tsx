import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert } from '../../../components/ui/Alert'
import { Button } from '../../../components/ui/Button'
import { toProblem } from '../../../lib/api/client'
import { formatDate } from '../../../lib/format'
import { bookingApi, EventStatus, type ReviewDto } from '../api/bookingApi'

/**
 * Yildiz gostergesi. Salt okunur veya secilebilir.
 *
 * ==================================================================
 * ERISILEBILIRLIK: YILDIZLAR SADECE GORSEL DEGIL
 * ==================================================================
 * Yildizlari yalnizca sembol olarak cizseydik ekran okuyucu
 * "yildiz yildiz yildiz" derdi -- kac tane oldugunu saymak
 * kullaniciya kalirdi.
 *
 * Salt okunur halde tek bir aria-label ("5 uzerinden 4 puan")
 * veriyorum ve yildizlari aria-hidden yapiyorum. Secilebilir halde
 * ise gercek radio dugmeleri kullaniyorum -- klavyeyle ok tuslariyla
 * gezilebiliyor.
 * ==================================================================
 */
function StarRating({
  value,
  onChange,
  size = 'sm',
}: {
  value: number
  onChange?: (v: number) => void
  size?: 'sm' | 'lg'
}) {
  const boyut = size === 'lg' ? 'text-2xl' : 'text-base'

  if (!onChange) {
    return (
      <span
        className={`${boyut} leading-none text-amber-500`}
        aria-label={`5 uzerinden ${value} puan`}
      >
        <span aria-hidden="true">
          {'★'.repeat(Math.round(value))}
          <span className="text-slate-300">{'★'.repeat(5 - Math.round(value))}</span>
        </span>
      </span>
    )
  }

  return (
    <fieldset className="flex items-center gap-1">
      <legend className="sr-only">Puaniniz</legend>

      {[1, 2, 3, 4, 5].map((puan) => (
        <label key={puan} className="cursor-pointer">
          {/* Gercek radio: klavye ve ekran okuyucu icin.
              sr-only ile gorsel olarak gizli ama erisilebilir. */}
          <input
            type="radio"
            name="puan"
            value={puan}
            checked={value === puan}
            onChange={() => onChange(puan)}
            className="sr-only"
          />
          <span
            className={`${boyut} leading-none transition-colors ${
              puan <= value ? 'text-amber-500' : 'text-slate-300'
            }`}
            aria-hidden="true"
          >
            {'★'}
          </span>
          <span className="sr-only">{puan} puan</span>
        </label>
      ))}
    </fieldset>
  )
}

interface EventReviewsProps {
  eventId: string
  eventStatus: number
}

/**
 * ==================================================================
 * ETKINLIK YORUMLARI -- PDF Sprint 12
 * ==================================================================
 * PDF is kurallarinin arayuze yansimasi:
 *
 *   "Etkinlik tamamlanmadan yorum yapilamaz"
 *      -> form yalnizca Completed durumunda gorunuyor
 *
 *   "Yalnizca gecerli bilet almis kullanici yorum yapabilir"
 *      -> bunu ISTEMCIDE bilemiyoruz (bilet bilgisi burada yok).
 *         Kullanici deneyip 403 aliyor ve NET bir mesaj goruyor.
 *
 *   "Kullanici yalnizca kendi yorumunu duzenleyebilir"
 *      -> Duzenle/Sil dugmeleri yalnizca isMine=true olanlarda
 *
 * ------------------------------------------------------------------
 * NEDEN BILET KONTROLUNU ISTEMCIDE YAPMIYORUM?
 * ------------------------------------------------------------------
 * Yapabilirdim: "biletlerim" listesini cekip bu etkinlik var mi diye
 * bakardim. Yapmadim cunku:
 *
 *   1) Fazladan bir istek, herkes icin, yalnizca bir dugmeyi
 *      gizlemek ugruna
 *   2) Sunucu ZATEN kontrol ediyor -- istemcideki kontrol yalnizca
 *      kolaylik olurdu, guvenlik degil
 *   3) Yanlis pozitif riski: bilet listesi bayatsa dugmeyi haksiz
 *      yere gizlerdim
 *
 * Hatayi acikca gostermek, sessizce dugme gizlemekten daha durust.
 * ------------------------------------------------------------------
 */
export function EventReviews({ eventId, eventStatus }: EventReviewsProps) {
  const queryClient = useQueryClient()

  const [rating, setRating] = useState(5)
  const [comment, setComment] = useState('')
  const [editingId, setEditingId] = useState<string | null>(null)

  const reviewsQuery = useQuery({
    queryKey: ['reviews', eventId],
    queryFn: () => bookingApi.getEventReviews(eventId),
  })

  const tazele = () => {
    void queryClient.invalidateQueries({ queryKey: ['reviews', eventId] })

    // Etkinlik detayi onbellegi de bayatladi olabilir (ortalama puan).
    void queryClient.invalidateQueries({ queryKey: ['event', eventId] })
  }

  const createReview = useMutation({
    mutationFn: () => bookingApi.createReview(eventId, { rating, comment }),
    onSuccess: () => {
      setComment('')
      setRating(5)
      tazele()
    },
  })

  const updateReview = useMutation({
    mutationFn: (id: string) => bookingApi.updateReview(id, { rating, comment }),
    onSuccess: () => {
      setEditingId(null)
      setComment('')
      setRating(5)
      tazele()
    },
  })

  const deleteReview = useMutation({
    mutationFn: (id: string) => bookingApi.deleteReview(id),
    onSuccess: tazele,
  })

  const duzenlemeyeBasla = (review: ReviewDto) => {
    setEditingId(review.id)
    setRating(review.rating)
    setComment(review.comment)
  }

  const duzenlemeyiIptalEt = () => {
    setEditingId(null)
    setComment('')
    setRating(5)
  }

  const data = reviewsQuery.data
  const ozet = data?.summary

  // PDF: "Etkinlik tamamlanmadan yorum yapilamaz."
  const yorumYapilabilir = eventStatus === EventStatus.Completed

  // Kullanicinin zaten bir yorumu var mi?
  //
  // Varsa "yeni yorum" formunu gostermiyorum -- backend zaten
  // reddedecek ve kullanici bosuna yazmis olacakti.
  const mevcutYorumum = data?.reviews.items.find((r) => r.isMine)

  const hata = createReview.isError
    ? toProblem(createReview.error)
    : updateReview.isError
      ? toProblem(updateReview.error)
      : null

  return (
    <section className="mt-6 rounded-2xl border border-slate-200 bg-white p-6 shadow-sm">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h2 className="text-lg font-semibold text-slate-900">Yorumlar</h2>

        {ozet && ozet.totalCount > 0 && (
          <div className="flex items-center gap-2">
            <StarRating value={ozet.averageRating} />
            <span className="text-sm font-medium text-slate-900">
              {ozet.averageRating.toFixed(1)}
            </span>
            <span className="text-sm text-slate-500">({ozet.totalCount} yorum)</span>
          </div>
        )}
      </div>

      {/* ---- PUAN DAGILIMI ---- */}
      {ozet && ozet.totalCount > 0 && (
        <div className="mt-4 space-y-1">
          {[5, 4, 3, 2, 1].map((puan) => {
            const adet = ozet.ratingCounts[String(puan)] ?? 0

            // Sifira bolme korumasi: totalCount 0 ise bu blok zaten
            // gorunmuyor ama yine de savunmayi birakiyorum.
            const yuzde = ozet.totalCount > 0 ? (adet / ozet.totalCount) * 100 : 0

            return (
              <div key={puan} className="flex items-center gap-2 text-xs">
                <span className="w-8 text-slate-500">
                  {puan}
                  {'★'}
                </span>
                <div className="h-2 flex-1 overflow-hidden rounded-full bg-slate-100">
                  <div className="h-full bg-amber-400" style={{ width: `${yuzde}%` }} />
                </div>
                <span className="w-8 text-right text-slate-500">{adet}</span>
              </div>
            )
          })}
        </div>
      )}

      {/* ---- YORUM FORMU ---- */}
      {!yorumYapilabilir ? (
        <p className="mt-5 rounded-lg bg-slate-50 px-4 py-3 text-sm text-slate-500">
          Yorumlar etkinlik tamamlandiktan sonra acilir.
        </p>
      ) : (
        (!mevcutYorumum || editingId) && (
          <form
            className="mt-5 space-y-3 rounded-xl border border-slate-200 p-4"
            onSubmit={(e) => {
              e.preventDefault()

              if (editingId) {
                updateReview.mutate(editingId)
              } else {
                createReview.mutate()
              }
            }}
          >
            <p className="text-sm font-medium text-slate-700">
              {editingId ? 'Yorumunuzu duzenleyin' : 'Deneyiminizi paylasin'}
            </p>

            <StarRating value={rating} onChange={setRating} size="lg" />

            <textarea
              value={comment}
              onChange={(e) => setComment(e.target.value)}
              rows={3}
              maxLength={2000}
              required
              aria-label="Yorumunuz"
              placeholder="Etkinlik hakkinda ne dusunuyorsunuz?"
              className="w-full rounded-lg border border-slate-300 px-3 py-2 text-sm outline-none transition-colors focus:border-brand-500"
            />

            {hata && (
              <Alert variant="error">
                {/* Backend'in mesajini oldugu gibi gosteriyorum:
                    "gecerli biletiniz yok" veya "zaten yorumunuz var"
                    gibi mesajlar zaten kullanici diliyle yazildi. */}
                {hata.detail}
              </Alert>
            )}

            <div className="flex gap-2">
              <Button
                type="submit"
                isLoading={createReview.isPending || updateReview.isPending}
                disabled={comment.trim().length === 0}
              >
                {editingId ? 'Kaydet' : 'Yorum yap'}
              </Button>

              {editingId && (
                <Button type="button" variant="secondary" onClick={duzenlemeyiIptalEt}>
                  Vazgec
                </Button>
              )}
            </div>
          </form>
        )
      )}

      {/* ---- YORUM LISTESI ---- */}
      {reviewsQuery.isPending && (
        <div className="mt-5 space-y-3">
          {[1, 2].map((i) => (
            <div key={i} className="h-20 animate-pulse rounded-xl bg-slate-100" />
          ))}
        </div>
      )}

      {data && data.reviews.items.length === 0 && (
        <p className="mt-5 text-sm text-slate-500">
          Henuz yorum yok. {yorumYapilabilir ? 'Ilk yorumu siz yapin.' : ''}
        </p>
      )}

      <ul className="mt-5 divide-y divide-slate-100">
        {data?.reviews.items.map((review) => (
          <li key={review.id} className="py-4">
            <div className="flex flex-wrap items-start justify-between gap-2">
              <div>
                <p className="text-sm font-medium text-slate-900">
                  {review.userDisplayName}
                  {review.isMine && (
                    <span className="ml-2 rounded bg-brand-50 px-1.5 py-0.5 text-xs text-brand-700">
                      Siz
                    </span>
                  )}
                </p>
                <div className="mt-1 flex items-center gap-2">
                  <StarRating value={review.rating} />
                  <span className="text-xs text-slate-500">
                    {formatDate(review.createdAt)}
                    {/* Duzenlenmis yorumlari isaretliyorum.
                        Seffaflik: okuyan kisi metnin sonradan
                        degistirilmis olabilecegini bilmeli. */}
                    {review.updatedAt && ' (duzenlendi)'}
                  </span>
                </div>
              </div>

              {/* PDF: "Kullanici yalnizca kendi yorumunu duzenleyebilir."
                  Dugmeler yalnizca kendi yorumunda gorunuyor.
                  Gercek kontrol backend'de -- bu yalnizca deneyim. */}
              {review.isMine && !editingId && (
                <div className="flex gap-2 text-xs">
                  <button
                    type="button"
                    onClick={() => duzenlemeyeBasla(review)}
                    className="font-medium text-brand-600 hover:underline"
                  >
                    Duzenle
                  </button>
                  <button
                    type="button"
                    onClick={() => deleteReview.mutate(review.id)}
                    className="font-medium text-red-600 hover:underline"
                  >
                    Sil
                  </button>
                </div>
              )}
            </div>

            <p className="mt-2 whitespace-pre-line text-sm text-slate-700">{review.comment}</p>
          </li>
        ))}
      </ul>
    </section>
  )
}
