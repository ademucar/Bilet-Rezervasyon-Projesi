import { useState } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import {
  Bar,
  BarChart,
  CartesianGrid,
  Cell,
  Line,
  LineChart,
  Pie,
  PieChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts'
import { SiteHeader } from '../../../components/layout/SiteHeader'
import { Alert } from '../../../components/ui/Alert'
import { Button } from '../../../components/ui/Button'
import { toProblem } from '../../../lib/api/client'
import { formatMoney } from '../../../lib/format'
import { useAuthStore } from '../../../stores/authStore'
import { Roles } from '../../../types/auth'
import { ReportFormat, ReportType, reportsApi } from '../api/reportsApi'

/** Tek bir metrik karti. */
function MetricCard({
  label,
  value,
  hint,
  tone = 'default',
}: {
  label: string
  value: string
  hint?: string
  tone?: 'default' | 'warning' | 'danger'
}) {
  const tonlar = {
    default: 'border-slate-200',
    warning: 'border-amber-200 bg-amber-50/50',
    danger: 'border-red-200 bg-red-50/50',
  }

  return (
    <div className={`rounded-2xl border bg-white p-4 shadow-sm ${tonlar[tone]}`}>
      <p className="text-xs font-medium text-slate-500">{label}</p>
      <p className="mt-1 text-2xl font-bold tabular-nums text-slate-900">{value}</p>
      {hint && <p className="mt-0.5 text-xs text-slate-400">{hint}</p>}
    </div>
  )
}

// Grafik renkleri. Tek yerde: iki grafikte farkli palet kullanmak
// panelin dagilmis gorunmesine yol acar.
const GRAFIK_RENKLERI = ['#2563eb', '#7c3aed', '#0891b2', '#ea580c', '#65a30d']

/**
 * ==================================================================
 * ORGANIZATOR VE ADMIN PANELI -- PDF Sprint 13
 * ==================================================================
 * Iki panel TEK sayfada, sekmeli.
 *
 * Neden ayri iki sayfa degil? Cunku admin olan bir kullanici cogu
 * zaman AYNI ZAMANDA organizator (bizim demo kullanicimiz gibi) ve
 * iki panel arasinda gidip gelmek istiyor. Ayri adresler olsaydi
 * her gecis tam sayfa yuklemesi olurdu.
 *
 * Admin sekmesi YALNIZCA admin rolunde gorunuyor. Bu bir guvenlik
 * onlemi DEGIL, kullanici deneyimi -- gercek kontrol backend'deki
 * AdminOnly policy'sinde (dogrulandi: normal kullanici 403 aliyor).
 * ==================================================================
 */
export function DashboardPage() {
  const user = useAuthStore((s) => s.user)
  const isAdmin = user?.roles.includes(Roles.Admin) ?? false

  const [tab, setTab] = useState<'organizer' | 'admin'>('organizer')

  return (
    <div className="min-h-screen bg-slate-50">
      <SiteHeader />

      <main className="mx-auto max-w-6xl px-4 py-8">
        <h1 className="text-2xl font-bold text-slate-900">Panel</h1>

        {isAdmin && (
          <div className="mt-4 flex gap-2">
            {(['organizer', 'admin'] as const).map((t) => (
              <button
                key={t}
                type="button"
                onClick={() => setTab(t)}
                aria-pressed={tab === t}
                className={`rounded-lg px-3 py-1.5 text-sm font-medium transition-colors ${
                  tab === t ? 'bg-brand-600 text-white' : 'bg-white text-slate-600 hover:bg-slate-100'
                }`}
              >
                {t === 'organizer' ? 'Organizator' : 'Yonetici'}
              </button>
            ))}
          </div>
        )}

        {tab === 'organizer' || !isAdmin ? <OrganizerPanel /> : <AdminPanel />}

        <ReportExportPanel />
      </main>
    </div>
  )
}

// ===================================================================
// ORGANIZATOR PANELI -- PDF'in 10 metrigi
// ===================================================================

function OrganizerPanel() {
  const q = useQuery({
    queryKey: ['dashboard', 'organizer'],
    queryFn: () => reportsApi.getOrganizerDashboard(30),
  })

  if (q.isPending) {
    return (
      <div className="mt-6 grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        {[1, 2, 3, 4, 5, 6, 7, 8].map((i) => (
          <div key={i} className="h-24 animate-pulse rounded-2xl bg-slate-200" />
        ))}
      </div>
    )
  }

  if (q.isError || !q.data) {
    return (
      <div className="mt-6">
        <Alert variant="error">{toProblem(q.error).detail}</Alert>
      </div>
    )
  }

  const d = q.data

  return (
    <div className="mt-6 space-y-6">
      {/* ---- 7 sayisal metrik ---- */}
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <MetricCard label="Toplam etkinlik" value={String(d.totalEvents)} />
        <MetricCard label="Yayindaki etkinlik" value={String(d.publishedEvents)} />
        <MetricCard label="Satilan bilet" value={String(d.totalTicketsSold)} />
        <MetricCard label="Toplam gelir" value={formatMoney(d.totalRevenue, d.currency)} />
        <MetricCard
          label="Iade edilen bilet"
          value={String(d.refundedTickets)}
          tone={d.refundedTickets > 0 ? 'warning' : 'default'}
        />
        <MetricCard
          label="Doluluk orani"
          value={`%${d.occupancyRate}`}
          hint="Satilan / uretilmis koltuk"
        />
        <MetricCard
          label="En cok satan bilet turu"
          // Hic satis yoksa null geliyor -- tire gostermek "0" yazmaktan
          // daha dogru: "0 adet Tam bilet" ile "hic bilet turu yok"
          // farkli seyler.
          value={d.topTicketTypeName ?? '-'}
          hint={d.topTicketTypeName ? `${d.topTicketTypeCount} adet` : undefined}
        />
      </div>

      {/* ---- 8) Gunluk satis grafigi ---- */}
      <section className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
        <h2 className="font-semibold text-slate-900">Gunluk satis (son 30 gun)</h2>

        {/* ResponsiveContainer: grafik kapsayicinin genisligine uyum
            saglar. Sabit genislik verseydik mobilde tasardi. */}
        <div className="mt-4 h-64">
          <ResponsiveContainer width="100%" height="100%">
            <LineChart data={d.dailySales}>
              <CartesianGrid strokeDasharray="3 3" stroke="#e2e8f0" />
              <XAxis
                dataKey="date"
                tick={{ fontSize: 11 }}
                // Tarihi kisaltiyorum: 30 gunun tamami "2026-08-28"
                // olarak yazilsaydi etiketler ust uste binerdi.
                tickFormatter={(v: string) => v.slice(5)}
              />
              <YAxis tick={{ fontSize: 11 }} allowDecimals={false} />
              {/* Recharts formatter'i genis bir tip kullaniyor
                  (ValueType | undefined). Kendi tipimize daraltmak
                  yerine gelen degeri KONTROL EDIYORUM -- cast ile
                  susturmak, gercekten undefined geldiginde calisma
                  zamaninda patlamak demekti. */}
              <Tooltip
                formatter={(value) =>
                  typeof value === 'number' ? String(value) : ''
                }
              />
              <Line
                type="monotone"
                dataKey="ticketCount"
                name="Bilet"
                stroke={GRAFIK_RENKLERI[0]}
                strokeWidth={2}
                dot={false}
              />
            </LineChart>
          </ResponsiveContainer>
        </div>
      </section>

      <div className="grid gap-6 lg:grid-cols-2">
        {/* ---- 9) Etkinlik bazli gelir ---- */}
        <section className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
          <h2 className="font-semibold text-slate-900">Etkinlik bazli gelir</h2>

          {d.revenueByEvent.length === 0 ? (
            <p className="mt-4 text-sm text-slate-500">Henuz satis yok.</p>
          ) : (
            <div className="mt-4 h-64">
              <ResponsiveContainer width="100%" height="100%">
                <BarChart data={d.revenueByEvent} layout="vertical">
                  <CartesianGrid strokeDasharray="3 3" stroke="#e2e8f0" />
                  <XAxis type="number" tick={{ fontSize: 11 }} />
                  {/* Yatay cubuk: etkinlik adlari uzun oldugu icin
                      dikey eksende daha okunakli. */}
                  <YAxis
                    type="category"
                    dataKey="title"
                    width={110}
                    tick={{ fontSize: 11 }}
                  />
                  <Tooltip
                    formatter={(value) =>
                      typeof value === 'number' ? formatMoney(value, d.currency) : ''
                    }
                  />
                  <Bar dataKey="revenue" name="Gelir" fill={GRAFIK_RENKLERI[1]} />
                </BarChart>
              </ResponsiveContainer>
            </div>
          )}
        </section>

        {/* ---- 10) Bolum bazli doluluk ---- */}
        <section className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
          <h2 className="font-semibold text-slate-900">Bolum bazli doluluk</h2>

          {d.sectionOccupancies.length === 0 ? (
            <p className="mt-4 text-sm text-slate-500">Henuz koltuk uretilmemis.</p>
          ) : (
            <ul className="mt-4 space-y-3">
              {d.sectionOccupancies.map((s) => (
                <li key={s.sectionName}>
                  <div className="flex justify-between text-sm">
                    <span className="font-medium text-slate-700">{s.sectionName}</span>
                    <span className="text-slate-500">
                      {s.soldSeats} / {s.totalSeats} (%{s.occupancyRate})
                    </span>
                  </div>
                  <div className="mt-1 h-2 overflow-hidden rounded-full bg-slate-100">
                    <div
                      className="h-full bg-brand-500"
                      style={{ width: `${s.occupancyRate}%` }}
                    />
                  </div>
                </li>
              ))}
            </ul>
          )}
        </section>
      </div>
    </div>
  )
}

// ===================================================================
// ADMIN PANELI -- PDF'in 10 metrigi
// ===================================================================

function AdminPanel() {
  const q = useQuery({
    queryKey: ['dashboard', 'admin'],
    queryFn: reportsApi.getAdminDashboard,
  })

  if (q.isPending) {
    return (
      <div className="mt-6 grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        {[1, 2, 3, 4, 5, 6, 7, 8].map((i) => (
          <div key={i} className="h-24 animate-pulse rounded-2xl bg-slate-200" />
        ))}
      </div>
    )
  }

  if (q.isError || !q.data) {
    return (
      <div className="mt-6">
        <Alert variant="error">{toProblem(q.error).detail}</Alert>
      </div>
    )
  }

  const d = q.data

  return (
    <div className="mt-6 space-y-6">
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <MetricCard label="Toplam kullanici" value={String(d.totalUsers)} />
        <MetricCard label="Toplam organizator" value={String(d.totalOrganizers)} />
        <MetricCard label="Toplam etkinlik" value={String(d.totalEvents)} />
        <MetricCard label="Aktif satis" value={String(d.activeSales)} hint="Satisi acik etkinlik" />
        <MetricCard
          label="Toplam islem hacmi"
          value={formatMoney(d.totalTransactionVolume, d.currency)}
          hint="Iade dusulmemis"
        />
        <MetricCard
          label="Iptal edilen etkinlik"
          value={String(d.cancelledEvents)}
          tone={d.cancelledEvents > 0 ? 'warning' : 'default'}
        />
        <MetricCard
          label="Basarisiz odeme orani"
          value={`%${d.failedPaymentRate}`}
          hint="Sonuclanmis odemeler icinde"
          // %20 uzeri kirmizi: odeme saglayicisinda bir sorun
          // olabilecegini gosteren esik.
          tone={d.failedPaymentRate > 20 ? 'danger' : 'default'}
        />
        <MetricCard
          label="Sistem hatasi"
          value={String(d.systemErrorCount)}
          hint="Dead letter olmus mesaj"
          // Sifirdan buyukse HER ZAMAN kirmizi: bunlar insan
          // mudahalesi bekleyen isler.
          tone={d.systemErrorCount > 0 ? 'danger' : 'default'}
        />
      </div>

      <div className="grid gap-6 lg:grid-cols-2">
        <section className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
          <h2 className="font-semibold text-slate-900">En populer sehirler</h2>
          <p className="mt-0.5 text-xs text-slate-500">Satilan bilet sayisina gore</p>

          {d.topCities.length === 0 ? (
            <p className="mt-4 text-sm text-slate-500">Henuz satis yok.</p>
          ) : (
            <div className="mt-4 h-56">
              <ResponsiveContainer width="100%" height="100%">
                <BarChart data={d.topCities}>
                  <CartesianGrid strokeDasharray="3 3" stroke="#e2e8f0" />
                  <XAxis dataKey="name" tick={{ fontSize: 11 }} />
                  <YAxis tick={{ fontSize: 11 }} allowDecimals={false} />
                  <Tooltip />
                  <Bar dataKey="count" name="Bilet" fill={GRAFIK_RENKLERI[2]} />
                </BarChart>
              </ResponsiveContainer>
            </div>
          )}
        </section>

        <section className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
          <h2 className="font-semibold text-slate-900">En populer kategoriler</h2>
          <p className="mt-0.5 text-xs text-slate-500">Satilan bilet sayisina gore</p>

          {d.topCategories.length === 0 ? (
            <p className="mt-4 text-sm text-slate-500">Henuz satis yok.</p>
          ) : (
            <div className="mt-4 h-56">
              <ResponsiveContainer width="100%" height="100%">
                <PieChart>
                  <Pie
                    data={d.topCategories}
                    dataKey="count"
                    nameKey="name"
                    outerRadius={80}
                    label={(e: { name?: string }) => e.name ?? ''}
                  >
                    {d.topCategories.map((_, i) => (
                      <Cell
                        key={i}
                        fill={GRAFIK_RENKLERI[i % GRAFIK_RENKLERI.length]}
                      />
                    ))}
                  </Pie>
                  <Tooltip />
                </PieChart>
              </ResponsiveContainer>
            </div>
          )}
        </section>
      </div>
    </div>
  )
}

// ===================================================================
// RAPOR DISA AKTARMA -- PDF Sprint 13
// ===================================================================

function ReportExportPanel() {
  const [type, setType] = useState<number>(ReportType.SalesSummary)
  const [format, setFormat] = useState<number>(ReportFormat.Excel)

  const exportMutation = useMutation({
    mutationFn: () => reportsApi.requestExport(type, format),
  })

  const selectClass =
    'w-full rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm outline-none ' +
    'transition-colors focus:border-brand-500'

  return (
    <section className="mt-6 rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
      <h2 className="font-semibold text-slate-900">Rapor indir</h2>

      <div className="mt-4 grid gap-3 sm:grid-cols-3">
        <div className="space-y-1.5">
          <label htmlFor="rapor-tur" className="block text-xs font-medium text-slate-600">
            Rapor
          </label>
          <select
            id="rapor-tur"
            className={selectClass}
            value={type}
            onChange={(e) => setType(Number(e.target.value))}
          >
            <option value={ReportType.SalesSummary}>Satis ozeti</option>
            <option value={ReportType.EventOccupancy}>Etkinlik dolulugu</option>
            <option value={ReportType.RevenueByEvent}>Etkinlik bazli gelir</option>
            <option value={ReportType.TicketTypeSales}>Bilet turu satislari</option>
            <option value={ReportType.PaymentStatuses}>Odeme durumlari</option>
          </select>
        </div>

        <div className="space-y-1.5">
          <label htmlFor="rapor-bicim" className="block text-xs font-medium text-slate-600">
            Bicim
          </label>
          <select
            id="rapor-bicim"
            className={selectClass}
            value={format}
            onChange={(e) => setFormat(Number(e.target.value))}
          >
            <option value={ReportFormat.Excel}>Excel (.xlsx)</option>
            <option value={ReportFormat.Csv}>CSV</option>
            <option value={ReportFormat.Pdf}>PDF</option>
          </select>
        </div>

        <div className="flex items-end">
          <Button
            className="w-full"
            isLoading={exportMutation.isPending}
            onClick={() => exportMutation.mutate()}
          >
            Rapor olustur
          </Button>
        </div>
      </div>

      {/* ==============================================================
          BEKLENTIYI ACIKCA SOYLUYORUM
          ==============================================================
          PDF: "Rapor uretimi background job olarak calistirilmali ve
          tamamlandiginda kullaniciya bildirim gonderilmelidir."

          Yani dugmeye basinca dosya INMEZ. Bunu yazmasaydik kullanici
          bir sey olmadigini dusunup dugmeye tekrar tekrar basardi --
          ve her basis yeni bir rapor uretirdi.
          ============================================================== */}
      {exportMutation.isSuccess && (
        <div className="mt-4">
          <Alert variant="success">
            Rapor talebiniz alindi. Uretim arka planda suruyor; hazir
            oldugunda size bildirim gonderilecek.
          </Alert>
        </div>
      )}

      {exportMutation.isError && (
        <div className="mt-4">
          <Alert variant="error">{toProblem(exportMutation.error).detail}</Alert>
        </div>
      )}

      <p className="mt-3 text-xs text-slate-500">
        Raporlar arka planda uretilir. Buyuk raporlar birkac dakika
        surebilir; bu sirada sayfadan ayrilabilirsiniz.
      </p>
    </section>
  )
}
