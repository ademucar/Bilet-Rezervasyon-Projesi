import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { SiteHeader } from '../../../components/layout/SiteHeader'
import { Alert } from '../../../components/ui/Alert'
import { Button } from '../../../components/ui/Button'
import { Input } from '../../../components/ui/Input'
import { toProblem } from '../../../lib/api/client'
import { formatDateTime } from '../../../lib/format'
import { useAuthStore } from '../../../stores/authStore'
import { Roles } from '../../../types/auth'
import {
  organizerApi,
  ticketTypeApi,
  fileApi,
  posterApi,
  EVENT_STATUS_LABELS,
  EventStatus,
} from '../api/organizerApi'
import { EventEditForm } from '../components/EventEditForm'
import { SessionAddForm } from '../components/SessionAddForm'
import { TicketTypePanel } from '../components/TicketTypePanel'

/**
 * Etkinlik yönetim ekranı -- PDF Sprint 5'in kalan altı maddesi.
 *
 * Düzenleme, oturum ekleme, görsel yükleme, önizleme, yayına alma
 * ve iptal etme; hepsi tek sayfada. Ayrı ekranlara bölmedim çünkü
 * organizatör bunları arka arkaya yapıyor: etkinliği oluşturuyor,
 * oturum ekliyor, bilet türü tanımlıyor, sonra onaya gönderiyor.
 * Her adım için ayrı sayfa, dört kez geri-ileri gitmek demekti.
 */
export function EventManagePage() {
  const { eventId = '' } = useParams()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const kullanici = useAuthStore((s) => s.user)
  const [hata, setHata] = useState<string | null>(null)
  const [iptalSebebi, setIptalSebebi] = useState('')
  const [iptalAcik, setIptalAcik] = useState(false)

  const etkinlik = useQuery({
    queryKey: ['event', eventId],
    queryFn: () => organizerApi.getEvent(eventId),
    enabled: Boolean(eventId),
  })

  const biletTurleri = useQuery({
    queryKey: ['ticketTypes', eventId],
    queryFn: () => ticketTypeApi.list(eventId),
    enabled: Boolean(eventId),
  })

  const tazele = () => {
    queryClient.invalidateQueries({ queryKey: ['event', eventId] })
    queryClient.invalidateQueries({ queryKey: ['events'] })
  }

  // Durum işlemleri tek mutation ailesi: üçü de aynı deseni
  // izliyor (çağır, hatayı yaz, listeyi tazele).
  const onayaGonder = useMutation({
    mutationFn: () => organizerApi.submitForApproval(eventId),
    onSuccess: tazele,
    onError: (e) => setHata(toProblem(e).detail ?? 'Onaya gönderilemedi.'),
  })

  const iptalEt = useMutation({
    mutationFn: () => organizerApi.cancelEvent(eventId, iptalSebebi),
    onSuccess: () => {
      setIptalAcik(false)
      tazele()
    },
    onError: (e) => setHata(toProblem(e).detail ?? 'İptal edilemedi.'),
  })

  const sil = useMutation({
    mutationFn: () => organizerApi.deleteEvent(eventId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['events'] })
      navigate('/panel/etkinlikler')
    },
    onError: (e) => setHata(toProblem(e).detail ?? 'Silinemedi.'),
  })

  const afisYukle = useMutation({
    mutationFn: async (dosya: File) => {
      const yuklenen = await fileApi.upload(dosya)
      await posterApi.set(eventId, yuklenen.downloadUrl)
    },
    onSuccess: tazele,
    onError: (e) => setHata(toProblem(e).detail ?? 'Görsel yüklenemedi.'),
  })

  const afisKaldir = useMutation({
    mutationFn: () => posterApi.set(eventId, null),
    onSuccess: tazele,
    onError: (e) => setHata(toProblem(e).detail ?? 'Görsel kaldırılamadı.'),
  })

  if (etkinlik.isPending) {
    return (
      <div className="min-h-screen bg-slate-100">
        <SiteHeader />
        <main className="mx-auto max-w-4xl px-4 py-8">
          <div className="h-64 animate-pulse rounded-[4px] bg-slate-200" />
        </main>
      </div>
    )
  }

  if (etkinlik.isError || !etkinlik.data) {
    return (
      <div className="min-h-screen bg-slate-100">
        <SiteHeader />
        <main className="mx-auto max-w-4xl px-4 py-8">
          <Alert variant="error">
            {toProblem(etkinlik.error).detail ?? 'Etkinlik bulunamadı.'}
          </Alert>
        </main>
      </div>
    )
  }

  const e = etkinlik.data
  const rozet = EVENT_STATUS_LABELS[e.status] ?? {
    text: 'Bilinmiyor',
    tone: 'border-slate-300 bg-slate-50 text-slate-500',
  }

  // Hangi işlem ne zaman yapılabilir?
  //
  // Bu kuralları backend zaten uyguluyor; buradakiler yalnızca
  // düğmeyi gizlemek için. Kullanıcıya basınca hata alacağı bir
  // düğme göstermek, düğmeyi hiç göstermemekten kötü.
  const taslak = e.status === EventStatus.Draft
  const onayBekliyor = e.status === EventStatus.PendingApproval
  const bitti = [EventStatus.Cancelled, EventStatus.Completed].includes(e.status as never)
  const adminMi = kullanici?.roles.includes(Roles.Admin) ?? false

  return (
    <div className="min-h-screen bg-slate-100">
      <SiteHeader />

      <main className="mx-auto max-w-4xl px-4 py-8">
        <Link to="/panel/etkinlikler" className="text-sm text-brand-600 hover:underline">
          &larr; Etkinliklerim
        </Link>

        <div className="mt-3 flex flex-wrap items-start justify-between gap-3">
          <div className="min-w-0">
            <h1 className="font-display text-2xl font-bold tracking-tight text-kagit">{e.title}</h1>
            <p className="mt-1 text-sm text-kagit-soluk">
              {e.venueName}, {e.cityName} &middot; {formatDateTime(e.eventDate)}
            </p>
          </div>
          <span className={`label-xs shrink-0 border px-2 py-1 ${rozet.tone}`}>{rozet.text}</span>
        </div>

        {e.cancellationReason && (
          <div className="mt-4">
            <Alert variant="error">
              <span className="font-medium">İptal edildi.</span> {e.cancellationReason}
            </Alert>
          </div>
        )}

        {hata && (
          <div className="mt-4">
            <Alert variant="error">{hata}</Alert>
          </div>
        )}

        {/* ---- DURUM İŞLEMLERİ ---- */}
        <section className="mt-6 rounded-[4px] border border-slate-300 bg-white p-5">
          <h2 className="font-display font-semibold text-slate-900">Durum</h2>

          {taslak && (
            <p className="mt-1 text-[13px] text-slate-500">
              Taslak durumdayken etkinlik kimseye görünmüyor. Oturum ve bilet türü ekledikten sonra
              onaya gönderin.
            </p>
          )}
          {onayBekliyor && (
            <p className="mt-1 text-[13px] text-slate-500">
              Admin onayı bekleniyor. Onaylanınca yayına alınır ve satışa açılır.
            </p>
          )}

          <div className="mt-4 flex flex-wrap gap-2">
            {taslak && (
              <Button
                onClick={() => {
                  setHata(null)
                  onayaGonder.mutate()
                }}
                isLoading={onayaGonder.isPending}
                // Oturumsuz etkinlik onaya gonderilemez: koltugu
                // olmayan bir etkinlik yayina alinirsa kullanici
                // "bilet al" deyip bos ekranla karsilasir.
                disabled={e.sessions.length === 0}
              >
                Onaya gönder
              </Button>
            )}

            {/* Önizleme: etkinliğin kullanıcıya nasıl göründüğü.
                Ayrı bir "önizleme" ekranı yazmadım -- gerçek detay
                sayfasının kendisi en doğru önizleme. Sahte bir
                önizleme, gerçeğinden farklı olduğu gün yalan söyler. */}
            <Link
              to={`/etkinlikler/${e.id}`}
              className="inline-flex items-center justify-center gap-2 rounded-[4px] border border-slate-300 bg-white px-4 py-2.5 text-sm font-medium text-slate-700 transition-colors hover:bg-slate-50"
            >
              Önizle
            </Link>

            {!bitti && !taslak && (
              <Button variant="secondary" onClick={() => setIptalAcik((v) => !v)}>
                Etkinliği iptal et
              </Button>
            )}

            {taslak && (
              <Button
                variant="secondary"
                onClick={() => {
                  setHata(null)
                  sil.mutate()
                }}
                isLoading={sil.isPending}
              >
                Taslağı sil
              </Button>
            )}
          </div>

          {/* Yayina alma ADMIN yetkisi (PDF sayfa 5). Organizatorun
              kendi etkinligini onaylamasi, onay surecini anlamsiz
              kilardi -- backend de POST /publish'i AdminOnly yapmis. */}
          {onayBekliyor && adminMi && (
            <div className="mt-4 border-t border-slate-200 pt-4">
              <p className="label-xs mb-2">Yönetici işlemi</p>
              <Button
                onClick={() => {
                  setHata(null)
                  organizerApi
                    .publishEvent(e.id)
                    .then(tazele)
                    .catch((err: unknown) => setHata(toProblem(err).detail ?? 'Yayına alınamadı.'))
                }}
              >
                Yayına al
              </Button>
            </div>
          )}

          {iptalAcik && (
            <div className="mt-4 border-t border-slate-200 pt-4">
              <Input
                label="İptal sebebi"
                placeholder="Sanatçının sağlık sorunu nedeniyle..."
                value={iptalSebebi}
                onChange={(ev) => setIptalSebebi(ev.target.value)}
              />
              <p className="mt-1 text-xs text-slate-500">
                Bu metin, bilet almış kullanıcılara bildirim olarak gönderilir.
              </p>
              <div className="mt-3 flex gap-2">
                <Button
                  onClick={() => {
                    setHata(null)
                    iptalEt.mutate()
                  }}
                  isLoading={iptalEt.isPending}
                  disabled={iptalSebebi.trim().length === 0}
                >
                  İptali onayla
                </Button>
                <Button variant="secondary" onClick={() => setIptalAcik(false)}>
                  Vazgeç
                </Button>
              </div>
            </div>
          )}
        </section>

        {/* ---- BİLGİLER ---- */}
        <EventEditForm etkinlik={e} onKaydedildi={tazele} onHata={setHata} />

        {/* ---- AFİŞ ---- */}
        <section className="mt-6 rounded-[4px] border border-slate-300 bg-white p-5">
          <h2 className="font-display font-semibold text-slate-900">Afiş görseli</h2>
          <p className="mt-1 text-[13px] text-slate-500">JPEG, PNG veya WebP. En fazla 5 MB.</p>

          <div className="mt-4 flex flex-wrap items-center gap-4">
            {e.posterImagePath ? (
              <img
                src={e.posterImagePath}
                alt=""
                className="h-24 w-40 rounded-[4px] border border-slate-300 object-cover"
              />
            ) : (
              <div className="flex h-24 w-40 items-center justify-center rounded-[4px] border border-dashed border-slate-300 text-xs text-slate-400">
                Afiş yok
              </div>
            )}

            <div className="flex flex-col gap-2">
              <input
                type="file"
                accept="image/jpeg,image/png,image/webp"
                aria-label="Afiş görseli seç"
                className="text-sm file:mr-3 file:rounded-[4px] file:border file:border-slate-300 file:bg-white file:px-3 file:py-1.5 file:text-sm file:text-slate-700"
                onChange={(ev) => {
                  const dosya = ev.target.files?.[0]
                  if (dosya) {
                    setHata(null)
                    afisYukle.mutate(dosya)
                  }
                }}
              />
              {afisYukle.isPending && <p className="text-xs text-slate-500">Yükleniyor...</p>}
              {e.posterImagePath && (
                <button
                  type="button"
                  onClick={() => afisKaldir.mutate()}
                  className="self-start text-xs text-slate-500 underline hover:text-slate-900"
                >
                  Afişi kaldır
                </button>
              )}
            </div>
          </div>
        </section>

        {/* ---- OTURUMLAR ---- */}
        <SessionAddForm etkinlik={e} onEklendi={tazele} onHata={setHata} duzenlenebilir={!bitti} />

        {/* ---- BİLET TÜRLERİ ---- */}
        <TicketTypePanel
          eventId={e.id}
          turler={biletTurleri.data ?? []}
          yukleniyor={biletTurleri.isPending}
          duzenlenebilir={!bitti}
          onDegisti={() => queryClient.invalidateQueries({ queryKey: ['ticketTypes', eventId] })}
          onHata={setHata}
        />
      </main>
    </div>
  )
}
