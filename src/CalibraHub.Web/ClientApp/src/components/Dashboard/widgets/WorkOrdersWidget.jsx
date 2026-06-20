/**
 * WorkOrdersWidget — Is emirlerinin duruma gore mini istatistik kartlari
 * (Planlandı / Yayınlandı / Devam / Tamamlandı) + toplam. Tiklayinca is
 * emirleri ekranina workspace tab acar.
 *
 * GET /api/dashboard/widget/work-orders
 *   → { ok, planned, released, inProgress, completed, total, url }
 *
 * Props (widget kontrati): { size, settings, isDark, lang }
 */
import { useState, useEffect } from 'react'
import { ArrowUpRight } from 'lucide-react'
import dashboardService from '../dashboardService'
import { navigateInWorkspace } from '../../../utils/workspaceNav'
import WidgetSkeleton from './WidgetSkeleton'

var CELLS = [
  { key: 'planned', label: 'Planlandı', color: '#64748b' },
  { key: 'released', label: 'Yayınlandı', color: '#2563eb' },
  { key: 'inProgress', label: 'Devam Ediyor', color: '#d97706' },
  { key: 'completed', label: 'Tamamlandı', color: '#059669' },
]

export default function WorkOrdersWidget() {
  var [state, setState] = useState({ loading: true, error: null, data: null })

  useEffect(function () {
    var alive = true
    dashboardService.getWorkOrders()
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

  function openWorkOrders() {
    var url = (state.data && state.data.url) || '/Production/WorkOrders'
    try {
      if (window.top && window.top.CalibraHub && typeof window.top.CalibraHub.openWorkspaceTab === 'function') {
        window.top.CalibraHub.openWorkspaceTab({ url: url, title: 'İş Emirleri' })
        return
      }
    } catch (e) { /* fallback */ }
    navigateInWorkspace(url)
  }

  if (state.loading) return <WidgetSkeleton lines={3} />
  if (state.error) return <div className="dash-widget-empty">{state.error}</div>

  var d = state.data || {}

  return (
    <div style={{ height: '100%', display: 'flex', flexDirection: 'column', gap: 12 }}>
      <div className="dash-stat-grid">
        {CELLS.map(function (c) {
          return (
            <div key={c.key} className="dash-stat-cell">
              <div className="dash-stat-cell__value" style={{ color: c.color }}>{d[c.key] || 0}</div>
              <div className="dash-stat-cell__label">{c.label}</div>
            </div>
          )
        })}
      </div>
      <div
        onClick={openWorkOrders}
        style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', cursor: 'pointer', marginTop: 'auto' }}
      >
        <span className="dash-row__sub">Toplam aktif: <strong style={{ color: 'var(--dash-text-primary)' }}>{d.total || 0}</strong></span>
        <span style={{ display: 'inline-flex', alignItems: 'center', gap: 5, fontSize: 12.5, fontWeight: 700, color: '#6366f1' }}>
          Tümünü Gör <ArrowUpRight size={14} />
        </span>
      </div>
    </div>
  )
}
