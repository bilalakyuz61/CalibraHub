/**
 * PendingApprovalsWidget — Onayinizi bekleyen belge sayisini buyuk rakamla
 * gosterir + "Onayları Görüntüle" baglantisi. Sayi > 0 ise rose vurgu.
 *
 * GET /api/dashboard/widget/pending-approvals → { ok, count, url }
 *
 * Props (widget kontrati): { size, settings, isDark, lang }
 */
import { useState, useEffect } from 'react'
import { Inbox, ArrowUpRight, CheckCircle2 } from 'lucide-react'
import dashboardService from '../dashboardService'
import { navigateInWorkspace } from '../../../utils/workspaceNav'
import WidgetSkeleton from './WidgetSkeleton'

export default function PendingApprovalsWidget() {
  var [state, setState] = useState({ loading: true, error: null, count: 0, url: '/PendingApproval' })

  useEffect(function () {
    var alive = true
    dashboardService.getPendingApprovals()
      .then(function (d) {
        if (!alive) return
        setState({ loading: false, error: null, count: (d && d.count) || 0, url: (d && d.url) || '/PendingApproval' })
      })
      .catch(function (err) {
        if (!alive) return
        setState({ loading: false, error: err.message || 'Hata', count: 0, url: '/PendingApproval' })
      })
    return function () { alive = false }
  }, [])

  function openApprovals() {
    try {
      if (window.top && window.top.CalibraHub && typeof window.top.CalibraHub.openWorkspaceTab === 'function') {
        window.top.CalibraHub.openWorkspaceTab({ url: state.url, title: 'Onay Bekleyenler' })
        return
      }
    } catch (e) { /* fallback */ }
    navigateInWorkspace(state.url)
  }

  if (state.loading) return <WidgetSkeleton lines={2} />
  if (state.error) return <div className="dash-widget-empty">{state.error}</div>

  var has = state.count > 0
  var accent = has ? '#e11d48' : 'var(--dash-text-muted)'

  // 2026-06-19: Tıklama davranışı — sadece bekleyen belge varsa (has=true) sayı bloğu
  // ve "Onayları Görüntüle" linki tıklanır. Bekleyen yoksa kart durağan — kullanıcı
  // yanlışlıkla boş ekrana yönlendirilmez. Outer div onClick kaldırıldı (whole-card click).
  return (
    <div style={{ height: '100%', display: 'flex', flexDirection: 'column', justifyContent: 'space-between' }}>
      <div
        onClick={has ? openApprovals : undefined}
        style={{
          display: 'flex', alignItems: 'center', gap: 14,
          cursor: has ? 'pointer' : 'default',
          borderRadius: 8, padding: 2,
          transition: 'background 120ms ease-out',
        }}
        onMouseEnter={has ? function (e) { e.currentTarget.style.background = 'rgba(244,63,94,0.06)' } : undefined}
        onMouseLeave={has ? function (e) { e.currentTarget.style.background = 'transparent' } : undefined}
      >
        <div
          style={{
            width: 48, height: 48, borderRadius: 14, flexShrink: 0,
            display: 'flex', alignItems: 'center', justifyContent: 'center',
            background: has ? 'rgba(244,63,94,0.12)' : 'var(--dash-chip-bg)',
            border: '1px solid ' + (has ? 'rgba(244,63,94,0.30)' : 'var(--dash-card-border)'),
          }}
        >
          {has ? <Inbox size={22} style={{ color: accent }} /> : <CheckCircle2 size={22} style={{ color: '#059669' }} />}
        </div>
        <div>
          <div className="dash-stat-big" style={{ color: has ? accent : 'var(--dash-text-primary)' }}>
            {state.count}
          </div>
          <div className="dash-stat-label">
            {has ? 'belge onayınızı bekliyor' : 'bekleyen onay yok'}
          </div>
        </div>
      </div>
      {has && (
        <div
          onClick={openApprovals}
          style={{
            display: 'inline-flex', alignSelf: 'flex-start', alignItems: 'center', gap: 5,
            marginTop: 12, fontSize: 12.5, fontWeight: 700, color: '#6366f1',
            cursor: 'pointer',
          }}
        >
          Onayları Görüntüle <ArrowUpRight size={14} />
        </div>
      )}
    </div>
  )
}
