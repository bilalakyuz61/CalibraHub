/**
 * SalesQuotesWidget — Satis tekliflerinin duruma gore mini istatistik kartlari
 * (Taslak / Onay Bekliyor / Onaylandı) + toplam + acik tutar. Tiklayinca satis
 * teklifleri ekranina workspace tab acar.
 *
 * GET /api/dashboard/widget/sales-quotes
 *   → { ok, draft, pending, approved, total, openTotal, currency, url }
 *
 * Props (widget kontrati): { size, settings, isDark, lang }
 */
import { useState, useEffect } from 'react'
import { ArrowUpRight } from 'lucide-react'
import dashboardService from '../dashboardService'
import { navigateInWorkspace } from '../../../utils/workspaceNav'
import WidgetSkeleton from './WidgetSkeleton'

var CELLS = [
  { key: 'draft', label: 'Taslak', color: '#64748b' },
  { key: 'pending', label: 'Onay Bekliyor', color: '#d97706' },
  { key: 'approved', label: 'Onaylandı', color: '#059669' },
]

function fmtMoney(v, currency) {
  if (v == null || isNaN(v)) return null
  var s = Number(v).toLocaleString('tr-TR', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
  return currency ? (s + ' ' + currency) : s
}

export default function SalesQuotesWidget() {
  var [state, setState] = useState({ loading: true, error: null, data: null })

  useEffect(function () {
    var alive = true
    dashboardService.getSalesQuotes()
      .then(function (d) {
        if (!alive) return
        setState({ loading: false, error: null, data: d || {} })
      })
      .catch(function (err) {
        if (!alive) return
        setState({ loading: false, error: err.message || 'Hata', data: null })
      })
    return function () { alive = false }
  }, [])

  function openQuotes() {
    var url = (state.data && state.data.url) || '/Sales/Quotes'
    try {
      if (window.top && window.top.CalibraHub && typeof window.top.CalibraHub.openWorkspaceTab === 'function') {
        window.top.CalibraHub.openWorkspaceTab({ url: url, title: 'Satış Teklifleri' })
        return
      }
    } catch (e) { /* fallback */ }
    navigateInWorkspace(url)
  }

  if (state.loading) return <WidgetSkeleton lines={3} />
  if (state.error) return <div className="dash-widget-empty">{state.error}</div>

  var d = state.data || {}
  var open = fmtMoney(d.openTotal, d.currency)

  return (
    <div style={{ height: '100%', display: 'flex', flexDirection: 'column', gap: 12 }}>
      <div className="dash-stat-grid" style={{ gridTemplateColumns: 'repeat(3, minmax(0,1fr))' }}>
        {CELLS.map(function (c) {
          return (
            <div key={c.key} className="dash-stat-cell">
              <div className="dash-stat-cell__value" style={{ color: c.color }}>{d[c.key] || 0}</div>
              <div className="dash-stat-cell__label">{c.label}</div>
            </div>
          )
        })}
      </div>
      {open && (
        <div className="dash-stat-cell" style={{ display: 'flex', alignItems: 'baseline', justifyContent: 'space-between' }}>
          <span className="dash-stat-cell__label">Açık teklif tutarı</span>
          <span style={{ fontSize: 15, fontWeight: 800, color: 'var(--dash-text-primary)', fontVariantNumeric: 'tabular-nums' }}>{open}</span>
        </div>
      )}
      <div
        onClick={openQuotes}
        style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', cursor: 'pointer', marginTop: 'auto' }}
      >
        <span className="dash-row__sub">Toplam: <strong style={{ color: 'var(--dash-text-primary)' }}>{d.total || 0}</strong></span>
        <span style={{ display: 'inline-flex', alignItems: 'center', gap: 5, fontSize: 12.5, fontWeight: 700, color: '#6366f1' }}>
          Tümünü Gör <ArrowUpRight size={14} />
        </span>
      </div>
    </div>
  )
}
