/**
 * RecentDocumentsWidget — Son islem goren belgelerin kompakt listesi.
 * Satira tiklayinca ilgili belgenin URL'ine workspace tab acar.
 *
 * GET /api/dashboard/widget/recent-documents?take=8
 *   → { ok, items: [{ documentNumber, documentTypeName, contactName,
 *                      grandTotal, currency, docDate, url }] }
 *
 * Props (widget kontrati): { size, settings, isDark, lang }
 */
import { useState, useEffect } from 'react'
import { Files } from 'lucide-react'
import dashboardService from '../dashboardService'
import { navigateInWorkspace } from '../../../utils/workspaceNav'
import WidgetSkeleton from './WidgetSkeleton'

function fmtDate(v) {
  if (!v) return ''
  try {
    var d = v instanceof Date ? v : new Date(v)
    if (isNaN(d.getTime())) return String(v)
    return new Intl.DateTimeFormat('tr-TR', { day: '2-digit', month: '2-digit', year: 'numeric' }).format(d)
  } catch (e) { return String(v) }
}

function fmtMoney(v, currency) {
  if (v == null || isNaN(v)) return ''
  var s = Number(v).toLocaleString('tr-TR', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
  return currency ? (s + ' ' + currency) : s
}

export default function RecentDocumentsWidget(props) {
  var settings = props.settings || {}
  var take = settings.take || 8
  var [state, setState] = useState({ loading: true, error: null, items: [] })

  useEffect(function () {
    var alive = true
    dashboardService.getRecentDocuments(take)
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

  function openDoc(url) {
    if (!url) return
    try {
      if (window.top && window.top.CalibraHub && typeof window.top.CalibraHub.openWorkspaceTab === 'function') {
        window.top.CalibraHub.openWorkspaceTab({ url: url, title: 'Belge' })
        return
      }
    } catch (e) { /* fallback */ }
    navigateInWorkspace(url)
  }

  if (state.loading) return <WidgetSkeleton lines={4} />
  if (state.error) return <div className="dash-widget-empty">{state.error}</div>
  if (state.items.length === 0) {
    return (
      <div className="dash-widget-empty">
        <Files size={22} strokeWidth={1.6} />
        <span>Son belge bulunamadı.</span>
      </div>
    )
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
      {state.items.map(function (it, idx) {
        var money = fmtMoney(it.grandTotal, it.currency)
        return (
          <div key={(it.documentNumber || '') + '_' + idx} className="dash-row" onClick={function () { openDoc(it.url) }}>
            <div className="dash-row__main">
              <div className="dash-row__title">
                {it.documentNumber || '—'}
                {it.documentTypeName ? <span style={{ fontWeight: 500, color: 'var(--dash-text-secondary)' }}>{' · ' + it.documentTypeName}</span> : null}
              </div>
              <div className="dash-row__sub">
                {it.contactName || ''}{it.contactName && it.docDate ? ' · ' : ''}{fmtDate(it.docDate)}
              </div>
            </div>
            {money && <span className="dash-row__value">{money}</span>}
          </div>
        )
      })}
    </div>
  )
}
