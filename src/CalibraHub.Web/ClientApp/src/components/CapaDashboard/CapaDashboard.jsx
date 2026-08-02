/**
 * CapaDashboard — "DÖF Panosu" KPI ekranı
 *
 * GET {config.kpiUrl} (varsayılan /Quality/CapaKpi) çağırır ve tek ekranda:
 *   - Stat kartları (Toplam / Açık / Gecikmiş / Ort. Kapanış / Bu Ay Açılan / Bu Ay Kapanan)
 *   - Durum Dağılımı (Bar), Tip Dağılımı (Donut), Önem Dağılımı (Bar),
 *     Aylık Trend (Area, açılan vs kapanan), Sorumluya Göre Açık DÖF (yatay Bar)
 * gösterir. Standalone bileşen — kendi kökünde color-scheme + light/dark CSS
 * değişkenleri taşır (CLAUDE.md "React bileşenlerinde tema" kuralı).
 */
import React, { useState, useEffect, useCallback, useMemo } from 'react'
import {
  BarChart, Bar, Cell, PieChart, Pie, AreaChart, Area, XAxis, YAxis,
  Tooltip, Legend, ResponsiveContainer,
} from 'recharts'
import {
  ClipboardList, AlertCircle, AlertTriangle, Clock, TrendingUp, CheckCircle2,
  Loader2, RefreshCw, Users,
} from 'lucide-react'
import './CapaDashboard.css'

// ── Tema algılama — body.app-theme-dark class'ına göre renk paleti seçilir ────
function useIsDarkTheme() {
  const [isDark, setIsDark] = useState(() =>
    typeof document !== 'undefined' && document.body.classList.contains('app-theme-dark'))
  useEffect(() => {
    if (typeof document === 'undefined') return
    const observer = new MutationObserver(() => {
      setIsDark(document.body.classList.contains('app-theme-dark'))
    })
    observer.observe(document.body, { attributes: true, attributeFilter: ['class'] })
    return () => observer.disconnect()
  }, [])
  return isDark
}

// ── Renk paletleri (light/dark) — enum value'ya göre sabit renk ───────────────
// byStatus value: 0 Açık,1 Kök Neden Analiz,2 Aksiyonda,3 Doğrulama Bekliyor,4 Kapalı,5 İptal
const STATUS_COLOR = {
  0: { light: '#d97706', dark: '#fbbf24' }, // amber
  1: { light: '#4f46e5', dark: '#a5b4fc' }, // indigo
  2: { light: '#2563eb', dark: '#60a5fa' }, // blue
  3: { light: '#7c3aed', dark: '#c4b5fd' }, // violet
  4: { light: '#059669', dark: '#34d399' }, // emerald
  5: { light: '#64748b', dark: '#94a3b8' }, // slate
}
// byType value: 1 Düzeltici,2 Önleyici,3 8D
const TYPE_COLOR = {
  1: { light: '#4f46e5', dark: '#a5b4fc' }, // indigo
  2: { light: '#059669', dark: '#34d399' }, // emerald
  3: { light: '#7c3aed', dark: '#c4b5fd' }, // violet
}
// bySeverity value: 1 Düşük,2 Orta,3 Yüksek,4 Kritik — emerald→amber→orange→rose gradyanı
const SEVERITY_COLOR = {
  1: { light: '#059669', dark: '#34d399' }, // emerald
  2: { light: '#d97706', dark: '#fbbf24' }, // amber
  3: { light: '#ea580c', dark: '#fb923c' }, // orange
  4: { light: '#dc2626', dark: '#f87171' }, // rose
}
const TREND_OPENED = { light: '#4f46e5', dark: '#a5b4fc' } // indigo
const TREND_CLOSED = { light: '#059669', dark: '#34d399' } // emerald
const RESP_BAR      = { light: '#d97706', dark: '#fbbf24' } // amber (açık = dikkat)

function pick(entry, isDark) { return isDark ? entry.dark : entry.light }

// ── Yardımcılar ─────────────────────────────────────────────────────────────
function fmtInt(n) { return (n ?? 0).toLocaleString('tr-TR') }
function fmtMonth(ym) {
  if (!ym) return ''
  const parts = String(ym).split('-')
  if (parts.length < 2) return ym
  const d = new Date(Number(parts[0]), Number(parts[1]) - 1, 1)
  if (isNaN(d.getTime())) return ym
  return d.toLocaleDateString('tr-TR', { month: 'short', year: '2-digit' })
}

// ── Tooltip (tema uyumlu — CSS değişkenleri) ──────────────────────────────────
function ChartTooltip({ active, payload, label }) {
  if (!active || !payload?.length) return null
  return (
    <div className="capa-tooltip">
      {label != null && <div className="capa-tooltip-label">{label}</div>}
      {payload.map((p, i) => (
        <div key={i} className="capa-tooltip-row">
          <span className="capa-tooltip-dot" style={{ background: p.color || p.fill }} />
          <span className="capa-tooltip-name">{p.name}</span>
          <span className="capa-tooltip-value">{typeof p.value === 'number' ? p.value.toLocaleString('tr-TR') : p.value}</span>
        </div>
      ))}
    </div>
  )
}

// ── Stat kartı ─────────────────────────────────────────────────────────────
function StatCard({ icon: Icon, label, value, variant }) {
  return (
    <div className={`capa-stat-card capa-stat-card--${variant || 'indigo'}`}>
      <div className="capa-stat-icon"><Icon size={18} /></div>
      <div className="capa-stat-body">
        <div className="capa-stat-value">{value}</div>
        <div className="capa-stat-label">{label}</div>
      </div>
    </div>
  )
}

// ── Grafik kart kabuğu ─────────────────────────────────────────────────────
function ChartCard({ title, subtitle, children, className }) {
  return (
    <div className={`capa-chart-card${className ? ' ' + className : ''}`}>
      <div className="capa-chart-card-head">
        <div className="capa-chart-card-title">{title}</div>
        {subtitle && <div className="capa-chart-card-subtitle">{subtitle}</div>}
      </div>
      <div className="capa-chart-card-body">{children}</div>
    </div>
  )
}

function EmptyMini({ text }) {
  return <div className="capa-mini-empty">{text || 'Veri yok'}</div>
}

// ── Ana bileşen ────────────────────────────────────────────────────────────
export default function CapaDashboard({ config }) {
  const kpiUrl = config?.kpiUrl || '/Quality/CapaKpi'
  const isDark = useIsDarkTheme()

  const [data, setData] = useState(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState(null)

  const load = useCallback(() => {
    setLoading(true)
    setError(null)
    fetch(kpiUrl, { credentials: 'same-origin' })
      .then(r => {
        if (!r.ok) throw new Error('HTTP ' + r.status)
        return r.json()
      })
      .then(d => { setData(d); setLoading(false) })
      .catch(e => { setError(e.message || 'Veri yüklenemedi'); setLoading(false) })
  }, [kpiUrl])

  useEffect(() => { load() }, [load])

  const tickColor = isDark ? '#94a3b8' : '#64748b'
  const tickStyle = useMemo(() => ({ fill: tickColor, fontSize: 11 }), [tickColor])
  const axisLine  = useMemo(() => ({ stroke: isDark ? 'rgba(255,255,255,.10)' : '#e2e8f0' }), [isDark])

  if (loading) {
    return (
      <div className="capa-dash">
        <div className="capa-loading"><Loader2 size={22} className="capa-spin" /> Yükleniyor…</div>
      </div>
    )
  }

  if (error) {
    return (
      <div className="capa-dash">
        <div className="capa-error">
          <AlertTriangle size={22} />
          <div>DÖF Panosu yüklenemedi: {error}</div>
          <button type="button" className="capa-btn" onClick={load}><RefreshCw size={14} /> Tekrar Dene</button>
        </div>
      </div>
    )
  }

  const totalCount   = data?.totalCount ?? 0
  const openCount    = data?.openCount ?? 0
  const overdueCount = data?.overdueCount ?? 0
  const closedCount  = data?.closedCount ?? 0
  const openedThisMonth = data?.openedThisMonth ?? 0
  const closedThisMonth = data?.closedThisMonth ?? 0
  const avgClosureDays  = data?.avgClosureDays

  const byStatus   = data?.byStatus || []
  const byType     = data?.byType || []
  const bySeverity = data?.bySeverity || []
  const monthlyTrend = (data?.monthlyTrend || []).map(m => ({ ...m, monthLabel: fmtMonth(m.month) }))
  const topResponsible = data?.topResponsible || []

  return (
    <div className="capa-dash">
      <div className="capa-dash-header">
        <div className="capa-dash-title-wrap">
          <div className="capa-dash-icon"><ClipboardList size={20} /></div>
          <div>
            <div className="capa-dash-title">DÖF Panosu</div>
            <div className="capa-dash-subtitle">{fmtInt(totalCount)} kayıt · {fmtInt(openCount)} açık</div>
          </div>
        </div>
        <button type="button" className="capa-btn capa-btn--ghost" onClick={load} title="Yenile">
          <RefreshCw size={14} /> Yenile
        </button>
      </div>

      {totalCount === 0 ? (
        <div className="capa-empty">
          <ClipboardList size={32} />
          <div>Henüz DÖF kaydı yok</div>
        </div>
      ) : (
        <>
          <div className="capa-stats-row">
            <StatCard icon={ClipboardList} label="Toplam DÖF" value={fmtInt(totalCount)} variant="indigo" />
            <StatCard icon={AlertCircle} label="Açık" value={fmtInt(openCount)} variant="amber" />
            <StatCard icon={AlertTriangle} label="Gecikmiş" value={fmtInt(overdueCount)} variant="rose" />
            <StatCard icon={Clock} label="Ort. Kapanış (Gün)" value={avgClosureDays == null ? '—' : avgClosureDays.toLocaleString('tr-TR', { maximumFractionDigits: 1 })} variant="blue" />
            <StatCard icon={TrendingUp} label="Bu Ay Açılan" value={fmtInt(openedThisMonth)} variant="indigo" />
            <StatCard icon={CheckCircle2} label="Bu Ay Kapanan" value={fmtInt(closedThisMonth)} variant="emerald" />
          </div>

          <div className="capa-charts-grid">
            <ChartCard title="Durum Dağılımı" subtitle={`${byStatus.length} durum`}>
              {byStatus.length ? (
                <ResponsiveContainer width="100%" height={220}>
                  <BarChart data={byStatus} margin={{ top: 6, right: 8, bottom: 0, left: -14 }}>
                    <XAxis dataKey="label" tick={tickStyle} axisLine={axisLine} tickLine={false} interval={0} angle={-12} textAnchor="end" height={44} />
                    <YAxis tick={tickStyle} axisLine={false} tickLine={false} allowDecimals={false} />
                    <Tooltip content={<ChartTooltip />} cursor={{ fill: isDark ? 'rgba(255,255,255,.04)' : 'rgba(15,23,42,.04)' }} />
                    <Bar dataKey="count" name="Adet" radius={[4, 4, 0, 0]} maxBarSize={44}>
                      {byStatus.map((d, i) => <Cell key={i} fill={pick(STATUS_COLOR[d.value] || STATUS_COLOR[5], isDark)} />)}
                    </Bar>
                  </BarChart>
                </ResponsiveContainer>
              ) : <EmptyMini />}
            </ChartCard>

            <ChartCard title="Tip Dağılımı" subtitle={`${byType.length} tip`}>
              {byType.length && byType.some(d => d.count > 0) ? (
                <div className="capa-donut-wrap">
                  <ResponsiveContainer width="100%" height={200}>
                    <PieChart>
                      <Pie data={byType} dataKey="count" nameKey="label" cx="50%" cy="50%" innerRadius={46} outerRadius={78} paddingAngle={2}>
                        {byType.map((d, i) => <Cell key={i} fill={pick(TYPE_COLOR[d.value] || TYPE_COLOR[1], isDark)} />)}
                      </Pie>
                      <Tooltip content={<ChartTooltip />} />
                    </PieChart>
                  </ResponsiveContainer>
                  <div className="capa-legend">
                    {byType.map((d, i) => (
                      <div key={i} className="capa-legend-row">
                        <span className="capa-legend-dot" style={{ background: pick(TYPE_COLOR[d.value] || TYPE_COLOR[1], isDark) }} />
                        <span className="capa-legend-label">{d.label}</span>
                        <span className="capa-legend-value">{fmtInt(d.count)}</span>
                      </div>
                    ))}
                  </div>
                </div>
              ) : <EmptyMini />}
            </ChartCard>

            <ChartCard title="Önem Dağılımı" subtitle={`${bySeverity.length} seviye`}>
              {bySeverity.length ? (
                <ResponsiveContainer width="100%" height={220}>
                  <BarChart data={bySeverity} margin={{ top: 6, right: 8, bottom: 0, left: -14 }}>
                    <XAxis dataKey="label" tick={tickStyle} axisLine={axisLine} tickLine={false} />
                    <YAxis tick={tickStyle} axisLine={false} tickLine={false} allowDecimals={false} />
                    <Tooltip content={<ChartTooltip />} cursor={{ fill: isDark ? 'rgba(255,255,255,.04)' : 'rgba(15,23,42,.04)' }} />
                    <Bar dataKey="count" name="Adet" radius={[4, 4, 0, 0]} maxBarSize={44}>
                      {bySeverity.map((d, i) => <Cell key={i} fill={pick(SEVERITY_COLOR[d.value] || SEVERITY_COLOR[1], isDark)} />)}
                    </Bar>
                  </BarChart>
                </ResponsiveContainer>
              ) : <EmptyMini />}
            </ChartCard>

            <ChartCard title="Aylık Trend" subtitle="Son 12 ay" className="capa-chart-card--wide">
              {monthlyTrend.length ? (
                <ResponsiveContainer width="100%" height={220}>
                  <AreaChart data={monthlyTrend} margin={{ top: 6, right: 8, bottom: 0, left: -14 }}>
                    <defs>
                      <linearGradient id="capaOpenedGrad" x1="0" y1="0" x2="0" y2="1">
                        <stop offset="0%" stopColor={pick(TREND_OPENED, isDark)} stopOpacity={0.32} />
                        <stop offset="100%" stopColor={pick(TREND_OPENED, isDark)} stopOpacity={0} />
                      </linearGradient>
                      <linearGradient id="capaClosedGrad" x1="0" y1="0" x2="0" y2="1">
                        <stop offset="0%" stopColor={pick(TREND_CLOSED, isDark)} stopOpacity={0.32} />
                        <stop offset="100%" stopColor={pick(TREND_CLOSED, isDark)} stopOpacity={0} />
                      </linearGradient>
                    </defs>
                    <XAxis dataKey="monthLabel" tick={tickStyle} axisLine={axisLine} tickLine={false} interval="preserveStartEnd" />
                    <YAxis tick={tickStyle} axisLine={false} tickLine={false} allowDecimals={false} />
                    <Tooltip content={<ChartTooltip />} />
                    <Legend wrapperStyle={{ fontSize: 11, color: tickColor }} iconSize={9} />
                    <Area type="monotone" dataKey="opened" name="Açılan" stroke={pick(TREND_OPENED, isDark)} strokeWidth={2} fill="url(#capaOpenedGrad)" dot={false} activeDot={{ r: 4, strokeWidth: 0 }} />
                    <Area type="monotone" dataKey="closed" name="Kapanan" stroke={pick(TREND_CLOSED, isDark)} strokeWidth={2} fill="url(#capaClosedGrad)" dot={false} activeDot={{ r: 4, strokeWidth: 0 }} />
                  </AreaChart>
                </ResponsiveContainer>
              ) : <EmptyMini />}
            </ChartCard>

            <ChartCard title="Sorumluya Göre Açık DÖF" subtitle={topResponsible.length ? `İlk ${topResponsible.length}` : ''} className="capa-chart-card--wide">
              {topResponsible.length ? (
                <ResponsiveContainer width="100%" height={Math.max(160, topResponsible.length * 34)}>
                  <BarChart data={topResponsible} layout="vertical" margin={{ top: 4, right: 24, bottom: 0, left: 8 }}>
                    <XAxis type="number" tick={tickStyle} axisLine={false} tickLine={false} allowDecimals={false} />
                    <YAxis type="category" dataKey="name" tick={tickStyle} axisLine={axisLine} tickLine={false} width={140} />
                    <Tooltip content={<ChartTooltip />} cursor={{ fill: isDark ? 'rgba(255,255,255,.04)' : 'rgba(15,23,42,.04)' }} />
                    <Bar dataKey="openCount" name="Açık DÖF" fill={pick(RESP_BAR, isDark)} radius={[0, 4, 4, 0]} maxBarSize={22} />
                  </BarChart>
                </ResponsiveContainer>
              ) : (
                <div className="capa-mini-empty"><Users size={16} /> Açık DÖF'ü olan sorumlu yok</div>
              )}
            </ChartCard>
          </div>
        </>
      )}
    </div>
  )
}
