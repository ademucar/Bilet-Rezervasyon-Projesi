import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { AdminLayout } from '../components/AdminLayout'
import { Alert } from '../../../components/ui/Alert'
import { Button } from '../../../components/ui/Button'
import { Input } from '../../../components/ui/Input'
import { toProblem } from '../../../lib/api/client'
import { formatDate } from '../../../lib/format'
import { useAuthStore } from '../../../stores/authStore'
import { Roles } from '../../../types/auth'
import { userAdminApi, type UserListItem } from '../api/userAdminApi'

const DURUM_SUZGECLERI: { etiket: string; deger?: boolean }[] = [
  { etiket: 'Tümü' },
  { etiket: 'Aktif', deger: true },
  { etiket: 'Pasif', deger: false },
]

const ROLLER = [Roles.User, Roles.Organizer, Roles.Admin]

const ROL_ETIKETLERI: Record<string, string> = {
  User: 'Kullanıcı',
  Organizer: 'Organizatör',
  Admin: 'Yönetici',
}

/**
 * Kullanıcı yönetimi -- PDF sayfa 5:
 * "Admin: Tüm kullanıcıları yönetebilir."
 *
 * "Yönetmek" burada üç şey demek: görmek, hesabı açıp kapatmak ve rol
 * vermek. Kullanıcı SİLMEYİ bilerek koymadım -- backend'de de yok.
 * Bir hesabı silmek, o kişinin geçmiş rezervasyonlarını, biletlerini
 * ve ödemelerini sahipsiz bırakırdı; mali geçmiş bozulurdu.
 * Pasifleştirme aynı işi görüyor: kişi giriş yapamıyor ama kayıtlar
 * duruyor.
 */
export function UsersPage() {
  const queryClient = useQueryClient()
  const benimId = useAuthStore((s) => s.user?.id)

  const [arama, setArama] = useState('')
  const [rol, setRol] = useState<string | undefined>(undefined)
  const [aktif, setAktif] = useState<boolean | undefined>(undefined)
  const [sayfa, setSayfa] = useState(1)
  const [hata, setHata] = useState<string | null>(null)

  const sorgu = useQuery({
    queryKey: ['adminUsers', arama, rol, aktif, sayfa],
    queryFn: () =>
      userAdminApi.list({
        search: arama.trim() || undefined,
        role: rol,
        isActive: aktif,
        pageNumber: sayfa,
        pageSize: 20,
      }),
  })

  const tazele = () => queryClient.invalidateQueries({ queryKey: ['adminUsers'] })

  const durumDegistir = useMutation({
    mutationFn: (p: { id: string; aktif: boolean }) => userAdminApi.setActive(p.id, p.aktif),
    onSuccess: tazele,
    onError: (e) => setHata(toProblem(e).detail ?? 'Durum değiştirilemedi.'),
  })

  const rolDegistir = useMutation({
    mutationFn: (p: { id: string; rol: string; ata: boolean }) =>
      userAdminApi.setRole(p.id, p.rol, p.ata),
    onSuccess: tazele,
    onError: (e) => setHata(toProblem(e).detail ?? 'Rol değiştirilemedi.'),
  })

  // Filtre değişince ilk sayfaya dönmek şart. Dönmezsem 5. sayfadayken
  // arama yapan admin boş liste görür ve "sonuç yok" sanır.
  const filtreDegisti = (islem: () => void) => {
    islem()
    setSayfa(1)
    setHata(null)
  }

  return (
    <AdminLayout title="Kullanıcılar" subtitle="Hesapları ve rolleri yönetin">
      <div className="mb-4 flex flex-wrap items-end gap-3 rounded-[4px] border border-slate-300 bg-white p-4">
        <div className="w-full sm:w-64">
          <Input
            label="Ara"
            placeholder="E-posta veya ad..."
            value={arama}
            onChange={(e) => filtreDegisti(() => setArama(e.target.value))}
          />
        </div>

        <div className="flex flex-wrap gap-2">
          {DURUM_SUZGECLERI.map((s) => (
            <button
              key={s.etiket}
              type="button"
              onClick={() => filtreDegisti(() => setAktif(s.deger))}
              aria-pressed={aktif === s.deger}
              className={`rounded-[4px] border px-2.5 py-1 text-xs font-medium transition-colors ${
                aktif === s.deger
                  ? 'border-slate-900 bg-slate-900 text-white'
                  : 'border-slate-300 bg-white text-slate-600 hover:border-slate-900'
              }`}
            >
              {s.etiket}
            </button>
          ))}
        </div>

        <div className="flex flex-wrap gap-2">
          <button
            type="button"
            onClick={() => filtreDegisti(() => setRol(undefined))}
            aria-pressed={rol === undefined}
            className={`rounded-[4px] border px-2.5 py-1 text-xs font-medium transition-colors ${
              rol === undefined
                ? 'border-brand-600 bg-brand-50 text-brand-700'
                : 'border-slate-300 bg-white text-slate-600 hover:border-brand-600'
            }`}
          >
            Tüm roller
          </button>
          {ROLLER.map((r) => (
            <button
              key={r}
              type="button"
              onClick={() => filtreDegisti(() => setRol(r))}
              aria-pressed={rol === r}
              className={`rounded-[4px] border px-2.5 py-1 text-xs font-medium transition-colors ${
                rol === r
                  ? 'border-brand-600 bg-brand-50 text-brand-700'
                  : 'border-slate-300 bg-white text-slate-600 hover:border-brand-600'
              }`}
            >
              {ROL_ETIKETLERI[r] ?? r}
            </button>
          ))}
        </div>
      </div>

      {hata && (
        <div className="mb-4">
          <Alert variant="error">{hata}</Alert>
        </div>
      )}

      {sorgu.isError && (
        <Alert variant="error">
          {toProblem(sorgu.error).detail ?? 'Kullanıcılar yüklenemedi.'}
        </Alert>
      )}

      {sorgu.isPending && (
        <ul className="space-y-2" aria-busy="true" aria-label="Kullanıcılar yükleniyor">
          {[1, 2, 3].map((i) => (
            <li key={i} className="h-24 animate-pulse rounded-[4px] bg-slate-200" />
          ))}
        </ul>
      )}

      {sorgu.data && sorgu.data.items.length === 0 && (
        <div className="rounded-[4px] border border-slate-300 bg-white px-5 py-10 text-center">
          <p className="text-sm text-slate-500">Bu filtreye uyan kullanıcı yok.</p>
        </div>
      )}

      {sorgu.data && sorgu.data.items.length > 0 && (
        <>
          <p className="label-xs mb-2 text-slate-500">
            <span className="num">{sorgu.data.totalCount}</span> kullanıcı
          </p>

          <ul className="space-y-2">
            {sorgu.data.items.map((k) => (
              <KullaniciSatiri
                key={k.id}
                kullanici={k}
                benimHesabim={k.id === benimId}
                onDurum={(a) => {
                  setHata(null)
                  durumDegistir.mutate({ id: k.id, aktif: a })
                }}
                onRol={(r, ata) => {
                  setHata(null)
                  rolDegistir.mutate({ id: k.id, rol: r, ata })
                }}
                bekliyor={
                  (durumDegistir.isPending && durumDegistir.variables?.id === k.id) ||
                  (rolDegistir.isPending && rolDegistir.variables?.id === k.id)
                }
              />
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

interface SatirProps {
  kullanici: UserListItem
  benimHesabim: boolean
  onDurum: (aktif: boolean) => void
  onRol: (rol: string, ata: boolean) => void
  bekliyor: boolean
}

function KullaniciSatiri({ kullanici: k, benimHesabim, onDurum, onRol, bekliyor }: SatirProps) {
  return (
    <li
      className={`rounded-[4px] border p-4 ${
        k.isActive ? 'border-slate-300 bg-white' : 'border-slate-200 bg-slate-50'
      }`}
    >
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <p className="font-display font-semibold text-slate-900">
            {k.firstName} {k.lastName}
            {benimHesabim && <span className="label-xs ml-2 text-brand-600">bu sizsiniz</span>}
          </p>
          <p className="mt-0.5 text-[13px] text-slate-500">
            {k.email} &middot; kayıt <span className="num">{formatDate(k.createdAt)}</span>
          </p>
        </div>

        <div className="flex shrink-0 flex-wrap gap-1.5">
          {!k.isActive && (
            <span className="label-xs border border-red-300 bg-red-50 px-1.5 py-[3px] text-red-700">
              Pasif
            </span>
          )}
          {/* Kilit, pasiflikten FARKLI bir durum: art arda hatali
              girisin gecici sonucu, adminin karari degil. Ikisini ayri
              rozetle gostermek gerekiyor -- yoksa admin "ben bu hesabi
              kapatmadim ki" der. */}
          {k.isLockedOut && (
            <span className="label-xs border border-amber-300 bg-amber-50 px-1.5 py-[3px] text-amber-700">
              Kilitli
            </span>
          )}
          {!k.isEmailConfirmed && (
            <span className="label-xs border border-slate-300 bg-slate-50 px-1.5 py-[3px] text-slate-500">
              E-posta onaysız
            </span>
          )}
        </div>
      </div>

      <div className="mt-3 flex flex-wrap items-center gap-2 border-t border-slate-200 pt-3">
        <span className="label-xs text-slate-500">Roller:</span>

        {ROLLER.map((r) => {
          const var_ = k.roles.includes(r)

          return (
            <button
              key={r}
              type="button"
              disabled={bekliyor}
              onClick={() => onRol(r, !var_)}
              // Rol rozetleri aynı zamanda düğme: tıklayınca veriyor
              // veya alıyor. Ayrı bir "rol düzenle" ekranı açmak, bu
              // kadar basit bir iş için fazla adım olurdu.
              title={var_ ? `${ROL_ETIKETLERI[r]} rolünü kaldır` : `${ROL_ETIKETLERI[r]} rolü ver`}
              className={`label-xs border px-1.5 py-[3px] transition-colors disabled:opacity-50 ${
                var_
                  ? 'border-emerald-300 bg-emerald-50 text-emerald-700 hover:border-red-400 hover:bg-red-50 hover:text-red-700'
                  : 'border-slate-300 bg-white text-slate-400 hover:border-emerald-400 hover:text-emerald-700'
              }`}
            >
              {ROL_ETIKETLERI[r] ?? r}
            </button>
          )
        })}

        <div className="ml-auto">
          <Button
            variant="secondary"
            onClick={() => onDurum(!k.isActive)}
            isLoading={bekliyor}
            // Kendi hesabını pasifleştirmeyi backend zaten reddediyor;
            // düğmeyi burada da kapatıyorum ki admin reddedilecek bir
            // işlemi hiç denemesin.
            disabled={benimHesabim && k.isActive}
          >
            {k.isActive ? 'Pasifleştir' : 'Aktifleştir'}
          </Button>
        </div>
      </div>
    </li>
  )
}
