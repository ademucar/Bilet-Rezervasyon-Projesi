import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert } from '../../../components/ui/Alert'
import { Button } from '../../../components/ui/Button'
import { toProblem } from '../../../lib/api/client'
import { formatMoney } from '../../../lib/format'
import { bookingApi } from '../api/bookingApi'

interface Props {
  ticketId: string
  onKapat: () => void
}

/**
 * Bilet iptal onayı -- PDF sayfa 4: "Kullanıcı biletini iptal edebilir."
 *
 * Neden doğrudan iptal etmiyorum da araya bu adımı koyuyorum?
 *
 * Çünkü iptalin bedeli kullanıcıya göre değişiyor ve o an görünmüyor.
 * Etkinliğe 8 gün varsa parasının tamamını geri alır, 3 gün varsa
 * yarısını, 1 gün varsa hiçbir şey alamaz. "İptal et" düğmesine basıp
 * parasının yandığını sonradan öğrenen kullanıcı haklı olarak kızar.
 *
 * Bu yüzden önce sunucuya soruyorum (cancellation-preview), rakamı
 * gösteriyorum, sonra onay istiyorum.
 */
export function TicketCancelPanel({ ticketId, onKapat }: Props) {
  const queryClient = useQueryClient()

  const onizleme = useQuery({
    queryKey: ['ticketCancellationPreview', ticketId],
    queryFn: () => bookingApi.getTicketCancellationPreview(ticketId),
  })

  const iptal = useMutation({
    mutationFn: () => bookingApi.cancelMyTicket(ticketId),
    onSuccess: () => {
      // Bilet listesini tazeliyorum: durum "İade edildi"ye dönecek
      // ve QR kaybolacak.
      queryClient.invalidateQueries({ queryKey: ['my-tickets'] })
      onKapat()
    },
  })

  const veri = onizleme.data

  return (
    <div className="border-t border-dashed border-slate-300 bg-slate-50 px-4 py-3.5">
      {onizleme.isPending && (
        <p className="text-[13px] text-slate-500">İade tutarı hesaplanıyor...</p>
      )}

      {onizleme.isError && (
        <Alert variant="error">
          {toProblem(onizleme.error).detail ?? 'İade bilgisi alınamadı.'}
        </Alert>
      )}

      {iptal.isError && (
        <div className="mb-3">
          <Alert variant="error">{toProblem(iptal.error).detail ?? 'Bilet iptal edilemedi.'}</Alert>
        </div>
      )}

      {veri && !veri.canCancel && (
        <div className="flex flex-wrap items-center justify-between gap-3">
          <p className="text-[13px] text-slate-600">Bu bilet iptal edilemiyor. {veri.reason}</p>
          <Button variant="secondary" onClick={onKapat}>
            Kapat
          </Button>
        </div>
      )}

      {veri && veri.canCancel && (
        <div>
          <p className="label-xs text-slate-500">İptal edilirse</p>

          {/* Rakami buyuk yaziyorum. Bu ekrandaki tek onemli bilgi o:
              kullanicinin karar vermek icin bakacagi sey "ne kadarini
              geri aliyorum" sorusunun cevabi. */}
          <p className="num mt-1 text-xl font-semibold text-slate-900">
            {formatMoney(veri.refundAmount, veri.currency)}{' '}
            <span className="text-sm font-normal text-slate-500">
              iade edilir (%{veri.refundPercentage})
            </span>
          </p>

          {veri.refundPercentage === 100 && (
            <p className="mt-1 text-[13px] text-slate-600">
              Etkinliğe yeterli süre var, ödediğiniz tutarın tamamı iade edilir.
            </p>
          )}

          {veri.refundPercentage > 0 && veri.refundPercentage < 100 && (
            <p className="mt-1 text-[13px] text-slate-600">
              Etkinlik yaklaştığı için iade oranı düştü. Ödediğiniz{' '}
              <span className="num">{formatMoney(veri.price, veri.currency)}</span> tutarın %
              {veri.refundPercentage}&apos;i geri ödenir.
            </p>
          )}

          {veri.refundPercentage === 0 && (
            <p className="mt-1 text-[13px] text-red-700">
              Etkinliğe kalan süre nedeniyle iade yapılamıyor. İptal ederseniz koltuğunuz serbest
              bırakılır ama ödediğiniz tutar geri ödenmez.
            </p>
          )}

          <p className="mt-2 text-xs text-slate-500">Bu işlem geri alınamaz.</p>

          <div className="mt-3 flex flex-wrap gap-2">
            <Button
              onClick={() => iptal.mutate()}
              isLoading={iptal.isPending}
              variant={veri.refundPercentage === 0 ? 'secondary' : 'primary'}
            >
              Bileti iptal et
            </Button>
            <Button variant="secondary" onClick={onKapat}>
              Vazgeç
            </Button>
          </div>
        </div>
      )}
    </div>
  )
}
