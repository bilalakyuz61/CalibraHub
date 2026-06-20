/**
 * StockAlertsWidget — Minimum seviyenin altina dusen stok kalemleri (amber
 * uyari satirlari). Satira tiklayinca stok/envanter ekranina workspace tab acar.
 *
 * GET /api/dashboard/widget/stock-alerts?take=8
 *   → { ok, items: [{ itemCode, itemName, onHand, minLevel, unitCode, url }] }
 *
 * Veri kaynagi yoksa backend bos liste doner → "uyari yok / yapilandirilmamis"
 * empty state gosterilir (pano kirilmaz).
 *
 * Props (widget kontrati): { size, settings, isDark, lang }
 */
import { useState, useEffect } from 'react'
import { PackageX, PackageCheck, AlertTriangle } from 'lucide-react'
import dashboardService from '../dashboardService'
import { navigateInWorkspace } from '../../../utils/workspaceNav'
import WidgetSkeleton from './WidgetSkeleton'

function fmtQty(v) {
  if (v == null || isNaN(v)) return '0'
  return Number(v).toLocaleString('tr-TR', { maximumFractionDigits: 2 })
}

export default function StockAlertsWidget(props) {
  var settings = props.settings || {}
  var take = settings.take || 8
  var [state, setState] = useState({ loading: true, error: null, items: [] })

  useEffect(function () {
    var alive = true
    dashboardService.getStockAlerts(take)
      .then(function (d) {
        if (!alive) return
        setState({ loading: false, error: null, items: (d && d.items) || [] })
      })
      .catch(function (err) {
        if (!alive) return
        setState({ loading: false, error: err.message || 'Hata', items: [] })
      })
    return function () { alive = false }
  }, [take])

  function openInventory(url) {
    var target = url || '/Warehouse/Inventory'
    try {
      if (window.top && window.top.CalibraHub && typeof window.top.CalibraHub.openWorkspaceTab === 'function') {
        window.top.CalibraHub.openWorkspaceTab({ url: target, title: 'Stok' })
        return
      }
    } catch (e) { /* fallback */ }
    navigateInWorkspace(target)
  }

  if (state.loading) return <WidgetSkeleton lines={4} />
  if (state.error) return <div className="dash-widget-empty">{state.error}</div>
  if (state.items.length === 0) {
    return (
      <div className="dash-widget-empty">
        <PackageCheck size={22} strokeWidth={1.6} style={{ color: '#059669' }} />
        <span>Minimum altı stok yok.</span>
      </div>
    )
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
      {state.items.map(function (it, idx) {
        var unit = it.unitCode ? (' ' + it.unitCode) : ''
        return (
          <div key={(it.itemCode || '') + '_' + idx} className="dash-row" onClick={function () { openInventory(it.url) }}>
            <AlertTriangle size={15} style={{ color: '#d97706', flexShrink: 0 }} />
            <div className="dash-row__main">
              <div className="dash-row__title">{it.itemName || it.itemCode || '—'}</div>
              <div className="dash-row__sub">
                Mevcut: {fmtQty(it.onHand)}{unit} · Min: {fmtQty(it.minLevel)}{unit}
              </div>
            </div>
            <PackageX size={15} style={{ color: '#e11d48', flexShrink: 0 }} />
          </div>
        )
      })}
    </div>
  )
}
