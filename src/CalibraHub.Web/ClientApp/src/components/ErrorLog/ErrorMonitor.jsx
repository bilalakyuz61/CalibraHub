import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import {
  AlertTriangle, AlertOctagon, Search, RefreshCw, X, ChevronLeft, ChevronRight,
  ServerCrash,
} from 'lucide-react'
import './errorLog.css'
import { getErrorLogs } from '../../services/errorLogService'

const LEVELS = [
  { id: 'all', label: 'Tümü' },
  { id: 'error', label: 'Error' },
  { id: 'critical', label: 'Critical' },
]

function toDateInput(d) {
  var p = function (n) { return String(n).padStart(2, '0') }
  return d.getFullYear() + '-' + p(d.getMonth() + 1) + '-' + p(d.getDate())
}

function defaultRange() {
  var today = new Date()
  var from = new Date(today)
  from.setDate(from.getDate() - 6)
  return { from: toDateInput(from), to: toDateInput(today) }
}

/** UTC ISO → "10.07.2026 21:34:05" (yerel saat, tr-TR) */
function formatTs(iso) {
  if (!iso) return ''
  var d = new Date(iso)
  if (isNaN(d.getTime())) return String(iso)
  return d.toLocaleDateString('tr-TR', { day: '2-digit', month: '2-digit', year: 'numeric' }) +
    ' ' + d.toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit', second: '2-digit' })
}

function levelMeta(level) {
  var l = (level || '').toLowerCase()
  if (l === 'critical') return { cls: 'critical', icon: <AlertOctagon size={12} />, label: 'Critical' }
  return { cls: 'error', icon: <AlertTriangle size={12} />, label: level || 'Error' }
}

/**
 * Hata Logları izleme ekranı (/Admin/ErrorLog).
 * Veri kaynağı: backend Diagnostics dosya-tabanlı hata logu (/Admin/ErrorLogData).
 * Desen: AuditMonitor.jsx (İşlem Logları) ile birebir aynı — tarih aralığı + arama +
 * seviye filtresi + sayfalı liste + satır detay (burada modal, stack trace geniş olduğu için).
 */
export default function ErrorMonitor() {
  const [range, setRange] = useState(defaultRange)
  const [level, setLevel] = useState('all')
  const [text, setText] = useState('')
  const [debouncedText, setDebouncedText] = useState('')
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(50)
  const [data, setData] = useState({ entries: [], total: 0 })
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState(null)
  const [selected, setSelected] = useState(null)
  const fetchSeq = useRef(0)

  useEffect(() => {
    const t = setTimeout(() => { setDebouncedText(text.trim()); setPage(1) }, 350)
    return () => clearTimeout(t)
  }, [text])

  const load = useCallback((silent) => {
    const seq = ++fetchSeq.current
    if (!silent) setLoading(true)
    getErrorLogs({ from: range.from, to: range.to, level, q: debouncedText, page, pageSize })
      .then(res => {
        if (seq !== fetchSeq.current) return
        if (res && res.ok) {
          setData({ entries: res.entries || [], total: res.total || 0 })
          setError(null)
        } else {
          setError('Loglar yüklenemedi.')
        }
      })
      .catch(() => { if (seq === fetchSeq.current) setError('Loglar yüklenemedi (ağ hatası).') })
      .finally(() => { if (seq === fetchSeq.current) setLoading(false) })
  }, [range, level, debouncedText, page, pageSize])

  useEffect(() => { load(false) }, [load])

  // Esc ile detay modalını kapat
  useEffect(() => {
    if (!selected) return undefined
    const onKey = e => { if (e.key === 'Escape') setSelected(null) }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [selected])

  const clearFilters = () => { setLevel('all'); setText(''); setPage(1) }
  const hasFilter = level !== 'all' || debouncedText

  const totalPages = Math.max(1, Math.ceil(data.total / pageSize))

  return (
    <div className="elog-root">
      {/* Header */}
      <div className="elog-header">
        <div className="elog-header-icon"><ServerCrash size={20} /></div>
        <div className="elog-header-titles">
          <div className="elog-header-title">Hata Logları</div>
          <div className="elog-header-sub">
            {data.total.toLocaleString('tr-TR')} kayıt · {range.from.split('-').reverse().join('.')} – {range.to.split('-').reverse().join('.')}
          </div>
        </div>
        <div className="elog-header-actions">
          <div className="elog-search">
            <Search size={14} />
            <input
              placeholder="Mesaj, exception, kaynak ara…"
              value={text}
              onChange={e => setText(e.target.value)}
            />
            {text ? <X size={13} style={{ cursor: 'pointer' }} onClick={() => setText('')} /> : null}
          </div>
          <button type="button" className="elog-btn" onClick={() => load(false)} disabled={loading} title="Yenile">
            <RefreshCw size={14} className={loading ? 'elog-spin' : ''} />
          </button>
        </div>
      </div>

      {/* Filtreler */}
      <div className="elog-filters">
        <input type="date" data-native-date value={range.from}
          onChange={e => { setRange(r => ({ ...r, from: e.target.value || r.from })); setPage(1) }} />
        <span className="elog-filter-dash">–</span>
        <input type="date" data-native-date value={range.to}
          onChange={e => { setRange(r => ({ ...r, to: e.target.value || r.to })); setPage(1) }} />
        <div className="elog-sep" />
        <div className="elog-chipset">
          {LEVELS.map(l => (
            <button key={l.id} type="button"
              className={'elog-chip' + (level === l.id ? ' is-active' : '')}
              onClick={() => { setLevel(l.id); setPage(1) }}>{l.label}</button>
          ))}
        </div>
        {hasFilter ? (
          <button type="button" className="elog-clear-btn" onClick={clearFilters}>
            <X size={12} /> Filtreleri temizle
          </button>
        ) : null}
      </div>

      {/* Tablo */}
      <div className="elog-table-wrap">
        {error ? (
          <div className="elog-empty elog-empty--error">
            <AlertOctagon size={34} />
            <div>{error}</div>
          </div>
        ) : data.entries.length === 0 ? (
          <div className="elog-empty">
            <ServerCrash size={34} />
            <div>{loading ? 'Yükleniyor…' : 'Seçili aralık ve filtrelerde hata kaydı bulunamadı.'}</div>
          </div>
        ) : (
          <table className="elog-table">
            <thead>
              <tr>
                <th>Zaman</th>
                <th>Seviye</th>
                <th>Kaynak</th>
                <th>Mesaj</th>
                <th>Kullanıcı</th>
              </tr>
            </thead>
            <tbody>
              {data.entries.map((e, i) => {
                const meta = levelMeta(e.level)
                return (
                  <tr key={e.id || i} className="elog-row" onClick={() => setSelected(e)}>
                    <td className="elog-td-time">{formatTs(e.timestampUtc)}</td>
                    <td>
                      <span className={'elog-badge elog-badge--' + meta.cls}>
                        {meta.icon} {meta.label}
                      </span>
                    </td>
                    <td className="elog-td-src" title={(e.category || '') + ' / ' + (e.source || '')}>
                      {e.category || e.source || '—'}
                    </td>
                    <td className="elog-td-msg" title={e.message || ''}>{e.message || e.exceptionMessage || '—'}</td>
                    <td className="elog-td-user">{e.userName || '—'}</td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        )}
      </div>

      {/* Alt bar (sayfalama) */}
      <div className="elog-footer">
        <span>Toplam <b>{data.total.toLocaleString('tr-TR')}</b> kayıt{hasFilter ? ' (filtreli)' : ''}</span>
        <select value={pageSize} onChange={e => { setPageSize(Number(e.target.value)); setPage(1) }}>
          <option value={25}>25 / sayfa</option>
          <option value={50}>50 / sayfa</option>
          <option value={100}>100 / sayfa</option>
          <option value={200}>200 / sayfa</option>
        </select>
        <div className="elog-pager">
          <button type="button" disabled={page <= 1} onClick={() => setPage(p => Math.max(1, p - 1))}>
            <ChevronLeft size={14} />
          </button>
          <span className="elog-page-num">{page} / {totalPages}</span>
          <button type="button" disabled={page >= totalPages} onClick={() => setPage(p => Math.min(totalPages, p + 1))}>
            <ChevronRight size={14} />
          </button>
        </div>
      </div>

      {/* Detay modalı — sabit boyut, stack trace kendi içinde kayar (Modal boyut sabitliği kuralı) */}
      {selected && (
        <div className="elog-modal-backdrop" onClick={() => setSelected(null)}>
          <div className="elog-modal" onClick={e => e.stopPropagation()}>
            <div className="elog-modal-head">
              <span className={'elog-badge elog-badge--' + levelMeta(selected.level).cls}>
                {levelMeta(selected.level).icon} {levelMeta(selected.level).label}
              </span>
              <span className="elog-modal-time">{formatTs(selected.timestampUtc)}</span>
              <button type="button" className="elog-modal-close" onClick={() => setSelected(null)} title="Kapat (Esc)">
                <X size={16} />
              </button>
            </div>
            <div className="elog-modal-body">
              <div className="elog-detail-row"><span className="elog-detail-lbl">Mesaj</span><span className="elog-detail-val">{selected.message || '—'}</span></div>
              {selected.category ? <div className="elog-detail-row"><span className="elog-detail-lbl">Kategori</span><span className="elog-detail-val">{selected.category}</span></div> : null}
              {selected.source ? <div className="elog-detail-row"><span className="elog-detail-lbl">Kaynak</span><span className="elog-detail-val">{selected.source}</span></div> : null}
              {selected.requestPath ? <div className="elog-detail-row"><span className="elog-detail-lbl">İstek Yolu</span><span className="elog-detail-val elog-mono">{selected.requestPath}</span></div> : null}
              {selected.userName ? <div className="elog-detail-row"><span className="elog-detail-lbl">Kullanıcı</span><span className="elog-detail-val">{selected.userName}</span></div> : null}
              {selected.companyId != null ? <div className="elog-detail-row"><span className="elog-detail-lbl">Şirket No</span><span className="elog-detail-val">#{selected.companyId}</span></div> : null}
              {selected.exceptionType ? <div className="elog-detail-row"><span className="elog-detail-lbl">Exception Türü</span><span className="elog-detail-val elog-mono">{selected.exceptionType}</span></div> : null}
              {selected.exceptionMessage ? <div className="elog-detail-row"><span className="elog-detail-lbl">Exception Mesajı</span><span className="elog-detail-val">{selected.exceptionMessage}</span></div> : null}
              {selected.stackTrace ? (
                <div className="elog-stack-wrap">
                  <div className="elog-detail-lbl" style={{ marginBottom: 6 }}>Stack Trace</div>
                  <pre className="elog-stack">{selected.stackTrace}</pre>
                </div>
              ) : null}
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
