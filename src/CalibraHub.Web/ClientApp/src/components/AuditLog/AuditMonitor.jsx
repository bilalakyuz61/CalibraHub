import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import {
  ScrollText, Search, RefreshCw, Download, X, ChevronDown, ChevronRight,
  ChevronLeft, PlusCircle, PencilLine, Trash2, LogIn, LogOut, ShieldAlert,
  Users, Activity, Sparkles,
} from 'lucide-react'
import './auditLog.css'
import { ACTION_META, formatTs, changePreview } from './auditShared'

const RANGE_PRESETS = [
  { id: 'today', label: 'Bugün', days: 0 },
  { id: 'yesterday', label: 'Dün', days: -1 },
  { id: '7d', label: '7 Gün', days: 6 },
  { id: '30d', label: '30 Gün', days: 29 },
  { id: '90d', label: '90 Gün', days: 89 },
  { id: 'custom', label: 'Özel', days: null },
]

function toDateInput(d) {
  const p = n => String(n).padStart(2, '0')
  return d.getFullYear() + '-' + p(d.getMonth() + 1) + '-' + p(d.getDate())
}

function presetRange(id) {
  const today = new Date()
  if (id === 'today') return { from: toDateInput(today), to: toDateInput(today) }
  if (id === 'yesterday') {
    const y = new Date(today); y.setDate(y.getDate() - 1)
    return { from: toDateInput(y), to: toDateInput(y) }
  }
  const preset = RANGE_PRESETS.find(r => r.id === id)
  const from = new Date(today); from.setDate(from.getDate() - (preset && preset.days ? preset.days : 6))
  return { from: toDateInput(from), to: toDateInput(today) }
}

/**
 * İşlem Logları izleme/raporlama ekranı.
 * Veri kaynağı dosya tabanlı audit trail (AuditLogController).
 */
export default function AuditMonitor({ apiBase = '/AuditLog' }) {
  const [preset, setPreset] = useState('7d')
  const [range, setRange] = useState(() => presetRange('7d'))
  const [action, setAction] = useState('')
  const [entity, setEntity] = useState('')
  const [user, setUser] = useState('')
  const [text, setText] = useState('')
  const [debouncedText, setDebouncedText] = useState('')
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(50)
  const [data, setData] = useState({ items: [], total: 0, facets: null })
  const [stats, setStats] = useState(null)
  const [loading, setLoading] = useState(false)
  const [expanded, setExpanded] = useState(null)
  const [autoRefresh, setAutoRefresh] = useState(false)
  const [exporting, setExporting] = useState(false)
  const fetchSeq = useRef(0)

  useEffect(() => {
    const t = setTimeout(() => { setDebouncedText(text.trim()); setPage(1) }, 350)
    return () => clearTimeout(t)
  }, [text])

  const query = useCallback((extra) => {
    const p = new URLSearchParams()
    p.set('from', range.from); p.set('to', range.to)
    if (action) p.set('action', action)
    if (entity) p.set('entity', entity)
    if (user) p.set('user', user)
    if (debouncedText) p.set('text', debouncedText)
    if (extra) Object.keys(extra).forEach(k => p.set(k, extra[k]))
    return p.toString()
  }, [range, action, entity, user, debouncedText])

  const load = useCallback((silent) => {
    const seq = ++fetchSeq.current
    if (!silent) setLoading(true)
    const searchUrl = apiBase + '/Search?' + query({ page: String(page), pageSize: String(pageSize) })
    const statsUrl = apiBase + '/Stats?from=' + range.from + '&to=' + range.to
    Promise.all([
      fetch(searchUrl, { credentials: 'same-origin' }).then(r => r.json()),
      fetch(statsUrl, { credentials: 'same-origin' }).then(r => r.json()),
    ])
      .then(([s, st]) => {
        if (seq !== fetchSeq.current) return
        if (s && s.ok) setData({ items: s.items || [], total: s.total || 0, facets: s.facets || null })
        if (st && st.ok) setStats(st.stats || null)
      })
      .catch(() => { /* ağ hatası — mevcut veri korunur */ })
      .finally(() => { if (seq === fetchSeq.current) setLoading(false) })
  }, [apiBase, query, page, pageSize, range])

  useEffect(() => { load(false) }, [load])

  useEffect(() => {
    if (!autoRefresh) return undefined
    const t = setInterval(() => load(true), 15000)
    return () => clearInterval(t)
  }, [autoRefresh, load])

  const applyPreset = (id) => {
    setPreset(id)
    if (id !== 'custom') { setRange(presetRange(id)); setPage(1) }
  }

  const clearFilters = () => {
    setAction(''); setEntity(''); setUser(''); setText(''); setPage(1)
  }
  const hasFilter = action || entity || user || debouncedText

  const totalPages = Math.max(1, Math.ceil(data.total / pageSize))

  const exportExcel = async () => {
    if (exporting) return
    setExporting(true)
    try {
      // Filtrelenmiş sonucun tamamını (2000 satır sınırıyla) sayfa sayfa çek
      const rows = []
      for (let p = 1; p <= 4; p++) {
        const res = await fetch(apiBase + '/Search?' + query({ page: String(p), pageSize: '500' }),
          { credentials: 'same-origin' }).then(r => r.json())
        if (!res || !res.ok) break
        rows.push(...(res.items || []))
        if (rows.length >= (res.total || 0) || (res.items || []).length < 500) break
      }
      const payload = {
        fileName: 'islem-loglari-' + range.from + '_' + range.to + '.xlsx',
        sheetName: 'İşlem Logları',
        headers: [
          { id: 'ts', label: 'Zaman' }, { id: 'user', label: 'Kullanıcı' },
          { id: 'action', label: 'İşlem' }, { id: 'entity', label: 'Kayıt Türü' },
          { id: 'title', label: 'Kayıt' }, { id: 'changes', label: 'Değişiklikler' },
          { id: 'detail', label: 'Detay' }, { id: 'ip', label: 'IP' }, { id: 'src', label: 'Kaynak' },
        ],
        rows: rows.map(e => ({
          ts: formatTs(e.ts),
          user: e.user || '',
          action: e.actionLabel || e.action,
          entity: e.entityLabel || e.entity || '',
          title: e.title || (e.entityId ? '#' + e.entityId : ''),
          changes: (e.changes || []).map(c => c.label + ': ' + (c.old ?? '—') + ' → ' + (c.new ?? '—')).join('; '),
          detail: e.detail || '',
          ip: e.ip || '',
          src: e.src || '',
        })),
      }
      const ti = document.querySelector('input[name="__RequestVerificationToken"]')
      const token = ti ? ti.value : ''
      const form = document.createElement('form')
      form.method = 'POST'; form.action = '/api/export/smartboard-excel'
      form.target = '_self'; form.style.display = 'none'
      const hidden = document.createElement('textarea')
      hidden.name = 'payload'; hidden.value = JSON.stringify(payload)
      form.appendChild(hidden)
      if (token) {
        const ti2 = document.createElement('input')
        ti2.type = 'hidden'; ti2.name = '__RequestVerificationToken'; ti2.value = token
        form.appendChild(ti2)
      }
      document.body.appendChild(form)
      form.submit()
      setTimeout(() => { if (form.parentNode) form.parentNode.removeChild(form) }, 1500)
    } catch (e) {
      if (window.CalibraHub && window.CalibraHub.toast) window.CalibraHub.toast('Aktarma hatası: ' + (e.message || e), 'err')
    } finally {
      setExporting(false)
    }
  }

  const chartMax = useMemo(() => {
    if (!stats || !stats.byDay || !stats.byDay.length) return 0
    return Math.max(...stats.byDay.map(d => d.count))
  }, [stats])

  const actionIcon = (a) => {
    switch ((a || '').toLowerCase()) {
      case 'insert': return <PlusCircle size={12} />
      case 'update': return <PencilLine size={12} />
      case 'delete': return <Trash2 size={12} />
      case 'login': return <LogIn size={12} />
      case 'loginfailed': return <ShieldAlert size={12} />
      case 'logout': return <LogOut size={12} />
      default: return <Sparkles size={12} />
    }
  }

  const facetActions = (data.facets && data.facets.actions) || []
  const facetEntities = (data.facets && data.facets.entities) || []
  const facetUsers = (data.facets && data.facets.users) || []

  return (
    <div className="al-root">
      {/* Header */}
      <div className="al-header">
        <div className="al-header-icon"><ScrollText size={20} /></div>
        <div className="al-header-titles">
          <div className="al-header-title">İşlem Logları</div>
          <div className="al-header-sub">
            {data.total.toLocaleString('tr-TR')} kayıt · {range.from.split('-').reverse().join('.')} – {range.to.split('-').reverse().join('.')}
          </div>
        </div>
        <div className="al-header-actions">
          <div className="al-search">
            <Search size={14} />
            <input
              placeholder="Belge no, kullanıcı, alan, değer ara…"
              value={text}
              onChange={e => setText(e.target.value)}
            />
            {text ? <X size={13} style={{ cursor: 'pointer' }} onClick={() => setText('')} /> : null}
          </div>
          <button type="button" className={'al-btn' + (autoRefresh ? ' is-on' : '')}
            title="15 saniyede bir otomatik yenile"
            onClick={() => setAutoRefresh(v => !v)}>
            <Activity size={14} /> Canlı
          </button>
          <button type="button" className="al-btn" onClick={() => load(false)} disabled={loading} title="Yenile">
            <RefreshCw size={14} className={loading ? 'al-spin' : ''} />
          </button>
          <button type="button" className="al-btn" onClick={exportExcel} disabled={exporting} title="Excel'e aktar">
            <Download size={14} /> Excel
          </button>
        </div>
      </div>

      {/* Stat kartları */}
      <div className="al-stats">
        <div className="al-stat al-stat--indigo">
          <div className="al-stat-ic"><Activity size={16} /></div>
          <div><div className="al-stat-num">{stats ? stats.total.toLocaleString('tr-TR') : '—'}</div><div className="al-stat-lbl">Toplam İşlem</div></div>
        </div>
        <div className="al-stat al-stat--emerald">
          <div className="al-stat-ic"><PlusCircle size={16} /></div>
          <div><div className="al-stat-num">{stats ? stats.inserts.toLocaleString('tr-TR') : '—'}</div><div className="al-stat-lbl">Ekleme</div></div>
        </div>
        <div className="al-stat al-stat--amber">
          <div className="al-stat-ic"><PencilLine size={16} /></div>
          <div><div className="al-stat-num">{stats ? stats.updates.toLocaleString('tr-TR') : '—'}</div><div className="al-stat-lbl">Güncelleme</div></div>
        </div>
        <div className="al-stat al-stat--rose">
          <div className="al-stat-ic"><Trash2 size={16} /></div>
          <div><div className="al-stat-num">{stats ? stats.deletes.toLocaleString('tr-TR') : '—'}</div><div className="al-stat-lbl">Silme</div></div>
        </div>
        <div className="al-stat al-stat--violet">
          <div className="al-stat-ic"><ShieldAlert size={16} /></div>
          <div><div className="al-stat-num">{stats ? stats.securityEvents.toLocaleString('tr-TR') : '—'}</div><div className="al-stat-lbl">Güvenlik Olayı</div></div>
        </div>
        <div className="al-stat al-stat--blue">
          <div className="al-stat-ic"><Users size={16} /></div>
          <div><div className="al-stat-num">{stats ? stats.distinctUsers.toLocaleString('tr-TR') : '—'}</div><div className="al-stat-lbl">Kullanıcı</div></div>
        </div>
        <div className="al-stat-chart">
          <div className="al-stat-chart-title">Gün Bazlı İşlem Dağılımı</div>
          <div className="al-chart">
            {stats && stats.byDay && stats.byDay.length > 0 ? stats.byDay.map(d => (
              <div key={d.day} className="al-chart-bar"
                style={{ height: chartMax ? Math.max(6, Math.round((d.count / chartMax) * 100)) + '%' : '6%' }}
                title={d.day.split('-').reverse().join('.') + ' — ' + d.count.toLocaleString('tr-TR') + ' işlem'} />
            )) : <div style={{ fontSize: 11, color: 'var(--al-muted-2)', alignSelf: 'center' }}>Veri yok</div>}
          </div>
        </div>
      </div>

      {/* Filtreler */}
      <div className="al-filters">
        <div className="al-chipset">
          {RANGE_PRESETS.map(r => (
            <button key={r.id} type="button"
              className={'al-chip' + (preset === r.id ? ' is-active' : '')}
              onClick={() => applyPreset(r.id)}>{r.label}</button>
          ))}
        </div>
        {preset === 'custom' && (
          <>
            <input type="date" data-native-date value={range.from}
              onChange={e => { setRange(r => ({ ...r, from: e.target.value || r.from })); setPage(1) }} />
            <input type="date" data-native-date value={range.to}
              onChange={e => { setRange(r => ({ ...r, to: e.target.value || r.to })); setPage(1) }} />
          </>
        )}
        <div className="al-sep" />
        <select value={action} onChange={e => { setAction(e.target.value); setPage(1) }}>
          <option value="">Tüm İşlemler</option>
          {facetActions.map(a => <option key={a.code} value={a.code}>{a.label}</option>)}
        </select>
        <select value={entity} onChange={e => { setEntity(e.target.value); setPage(1) }}>
          <option value="">Tüm Kayıt Türleri</option>
          {facetEntities.map(e2 => <option key={e2.code} value={e2.code}>{e2.label}</option>)}
        </select>
        <select value={user} onChange={e => { setUser(e.target.value); setPage(1) }}>
          <option value="">Tüm Kullanıcılar</option>
          {facetUsers.map(u => <option key={u} value={u}>{u}</option>)}
        </select>
        {hasFilter ? (
          <button type="button" className="al-clear-btn" onClick={clearFilters}>
            <X size={12} /> Filtreleri temizle
          </button>
        ) : null}
      </div>

      {/* Tablo */}
      <div className="al-table-wrap">
        {data.items.length === 0 ? (
          <div className="al-empty">
            <ScrollText size={34} />
            <div>{loading ? 'Yükleniyor…' : 'Seçili aralık ve filtrelerde log kaydı bulunamadı.'}</div>
          </div>
        ) : (
          <table className="al-table">
            <thead>
              <tr>
                <th style={{ width: 28 }} />
                <th>Zaman</th>
                <th>Kullanıcı</th>
                <th>İşlem</th>
                <th>Kayıt</th>
                <th>Değişiklik</th>
                <th>IP</th>
                <th>Kaynak</th>
              </tr>
            </thead>
            <tbody>
              {data.items.map((e, i) => {
                const key = e.ts + '|' + i
                const open = expanded === key
                const meta = ACTION_META[(e.action || '').toLowerCase()] || ACTION_META.event
                const hasChanges = e.changes && e.changes.length > 0
                return (
                  <React.Fragment key={key}>
                    <tr className={'al-row' + (open ? ' is-open' : '')}
                      onClick={() => setExpanded(open ? null : key)}>
                      <td style={{ color: 'var(--al-muted-2)' }}>
                        {open ? <ChevronDown size={14} /> : <ChevronRight size={14} />}
                      </td>
                      <td className="al-td-time">{formatTs(e.ts)}</td>
                      <td className="al-td-user" title={e.user || ''}>{e.user || '—'}</td>
                      <td>
                        <span className={'al-badge al-badge--' + meta.cls}>
                          {actionIcon(e.action)} {e.actionLabel || e.action}
                        </span>
                      </td>
                      <td className="al-td-entity">
                        <div className="al-entity-label">{e.entityLabel || e.entity || '—'}</div>
                        {(e.title || e.entityId) ? (
                          <div className="al-entity-title">{e.title || ('#' + e.entityId)}</div>
                        ) : null}
                      </td>
                      <td>
                        {hasChanges ? (
                          <>
                            <span className="al-chg-pill">{e.changes.length} alan</span>
                            <span className="al-chg-preview">{changePreview(e.changes)}</span>
                          </>
                        ) : (e.detail ? <span className="al-chg-preview">{e.detail}</span> : <span style={{ color: 'var(--al-muted-2)' }}>—</span>)}
                      </td>
                      <td className="al-td-ip">{e.ip || ''}</td>
                      <td style={{ color: 'var(--al-muted)', fontSize: 12 }}>{e.src || ''}</td>
                    </tr>
                    {open && (
                      <tr>
                        <td className="al-expand-cell" colSpan={8}>
                          <div className="al-expand">
                            {hasChanges ? (
                              <table className="al-diff-table">
                                <thead>
                                  <tr><th>Alan</th><th>Eski Değer</th><th /><th>Yeni Değer</th></tr>
                                </thead>
                                <tbody>
                                  {e.changes.map((c, ci) => (
                                    <tr key={ci}>
                                      <td className="al-diff-field">{c.label || c.field}</td>
                                      <td>{c.old != null && c.old !== '' ? <span className="al-diff-old">{c.old}</span> : <span className="al-diff-empty">boş</span>}</td>
                                      <td className="al-trail-arrow">→</td>
                                      <td>{c.new != null && c.new !== '' ? <span className="al-diff-new">{c.new}</span> : <span className="al-diff-empty">boş</span>}</td>
                                    </tr>
                                  ))}
                                </tbody>
                              </table>
                            ) : (
                              <div style={{ fontSize: 12.5, color: 'var(--al-muted)' }}>
                                {e.detail || 'Bu işlem için alan değişikliği kaydı yok.'}
                              </div>
                            )}
                            <div className="al-expand-meta">
                              {e.entityId ? <span>Kayıt No: <b>#{e.entityId}</b></span> : null}
                              {e.detail && hasChanges ? <span>{e.detail}</span> : null}
                              {e.ip ? <span>IP: {e.ip}</span> : null}
                              {e.src ? <span>Kaynak: {e.src}</span> : null}
                            </div>
                          </div>
                        </td>
                      </tr>
                    )}
                  </React.Fragment>
                )
              })}
            </tbody>
          </table>
        )}
      </div>

      {/* Alt bar */}
      <div className="al-footer">
        <span>
          Toplam <b>{data.total.toLocaleString('tr-TR')}</b> kayıt
          {hasFilter ? ' (filtreli)' : ''}
        </span>
        <select value={pageSize} onChange={e => { setPageSize(Number(e.target.value)); setPage(1) }}>
          <option value={25}>25 / sayfa</option>
          <option value={50}>50 / sayfa</option>
          <option value={100}>100 / sayfa</option>
          <option value={200}>200 / sayfa</option>
        </select>
        <div className="al-pager">
          <button type="button" disabled={page <= 1} onClick={() => setPage(p => Math.max(1, p - 1))}>
            <ChevronLeft size={14} />
          </button>
          <span className="al-page-num">{page} / {totalPages}</span>
          <button type="button" disabled={page >= totalPages} onClick={() => setPage(p => Math.min(totalPages, p + 1))}>
            <ChevronRight size={14} />
          </button>
        </div>
      </div>
    </div>
  )
}
