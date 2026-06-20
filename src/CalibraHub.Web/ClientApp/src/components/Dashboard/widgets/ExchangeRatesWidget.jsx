/**
 * ExchangeRatesWidget — USD/EUR/GBP (ve settings.codes ile ozellestirilebilir)
 * doviz kurlari. Alis/Satis + bir onceki güne gore trend oku (yukari/asagi).
 *
 * GET /api/dashboard/widget/exchange-rates?codes=USD,EUR,GBP
 *   → { ok, items: [{ code, name, symbol, buying, selling, rateDate, trendVsPrev }] }
 *   trendVsPrev: 'up' | 'down' | 'flat' | null
 *
 * Props (widget kontrati): { size, settings, isDark, lang }
 */
import { useState, useEffect, useMemo } from 'react'
import { TrendingUp, TrendingDown, Minus } from 'lucide-react'
import dashboardService from '../dashboardService'
import WidgetSkeleton from './WidgetSkeleton'

var DEFAULT_CODES = ['USD', 'EUR', 'GBP']

function fmtRate(v) {
  if (v == null || isNaN(v)) return '—'
  return Number(v).toLocaleString('tr-TR', { minimumFractionDigits: 4, maximumFractionDigits: 4 })
}

function TrendBadge(props) {
  var t = props.trend
  if (t === 'up') return <span className="dash-trend-up" style={{ display: 'inline-flex' }}><TrendingUp size={14} /></span>
  if (t === 'down') return <span className="dash-trend-down" style={{ display: 'inline-flex' }}><TrendingDown size={14} /></span>
  return <span className="dash-trend-flat" style={{ display: 'inline-flex' }}><Minus size={14} /></span>
}

export default function ExchangeRatesWidget(props) {
  var settings = props.settings || {}
  var codes = useMemo(function () {
    return (Array.isArray(settings.codes) && settings.codes.length > 0) ? settings.codes : DEFAULT_CODES
  }, [settings.codes])

  var [state, setState] = useState({ loading: true, error: null, items: [] })

  useEffect(function () {
    var alive = true
    dashboardService.getExchangeRates(codes)
      .then(function (d) {
        if (!alive) return
        setState({ loading: false, error: null, items: (d && d.items) || [] })
      })
      .catch(function (err) {
        if (!alive) return
        setState({ loading: false, error: err.message || 'Hata', items: [] })
      })
    return function () { alive = false }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [codes.join(',')])

  if (state.loading) return <WidgetSkeleton lines={3} />
  if (state.error) return <div className="dash-widget-empty">{state.error}</div>
  if (state.items.length === 0) return <div className="dash-widget-empty">Kur bilgisi bulunamadı.</div>

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
      {/* Baslik satiri */}
      <div style={{ display: 'flex', alignItems: 'center', padding: '0 10px 6px', fontSize: 10.5, fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.04em', color: 'var(--dash-text-muted)' }}>
        <span style={{ flex: '1 1 auto' }}>Birim</span>
        <span style={{ width: 92, textAlign: 'right' }}>Alış</span>
        <span style={{ width: 92, textAlign: 'right' }}>Satış</span>
        <span style={{ width: 26 }} />
      </div>
      {state.items.map(function (it) {
        return (
          <div key={it.code} className="dash-row" style={{ cursor: 'default' }}>
            <div className="dash-row__main" style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
              <span style={{ fontWeight: 800, fontSize: 13, color: 'var(--dash-text-primary)' }}>{it.code}</span>
              {it.name && <span className="dash-row__sub">{it.name}</span>}
            </div>
            <span className="dash-row__value" style={{ width: 92, textAlign: 'right' }}>{fmtRate(it.buying)}</span>
            <span className="dash-row__value" style={{ width: 92, textAlign: 'right' }}>{fmtRate(it.selling)}</span>
            <span style={{ width: 26, textAlign: 'center' }}><TrendBadge trend={it.trendVsPrev} /></span>
          </div>
        )
      })}
    </div>
  )
}
