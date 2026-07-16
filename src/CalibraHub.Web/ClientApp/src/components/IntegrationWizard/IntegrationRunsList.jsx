/**
 * IntegrationRunsList — Çalıştırma logu (IntegrationRun audit) SmartBoard.
 *
 * Hub'ın 3. tab'ı + ayrı /Integrations/Runs URL'inden açılabilir.
 * Filtreler: status (Success/Failed/Skipped/Retrying), integrationId, sinceDays.
 * Detay modal: request/response body görüntüleme + JSON pretty-print.
 *
 * Periyodik refresh yok (yenileme manuel) — log volume'u büyürse polling eklenir.
 */
import React, { useState, useEffect, useCallback, useMemo } from 'react'
import {
  Activity, RefreshCw, Loader2, Check, X as XIcon, AlertTriangle, RotateCcw,
  Clock, Zap, Search, ExternalLink, X,
} from 'lucide-react'

function toast(msg, kind) {
  if (window.CalibraHub?.toast) window.CalibraHub.toast(msg, kind || 'info')
}

const STATUS_CONFIG = {
  Success:  { label: 'Başarılı', icon: Check,        color: 'emerald' },
  Failed:   { label: 'Hatalı',   icon: XIcon,        color: 'rose'    },
  Skipped:  { label: 'Atlandı',  icon: AlertTriangle, color: 'amber'  },
  Retrying: { label: 'Tekrar',   icon: RotateCcw,    color: 'indigo'  },
}

function formatDuration(ms) {
  if (ms == null) return '—'
  if (ms < 1000) return `${ms} ms`
  const s = ms / 1000
  if (s < 60) return `${s.toFixed(1)} s`
  return `${(s / 60).toFixed(1)} dk`
}

function formatDate(iso) {
  if (!iso) return '—'
  try {
    const d = new Date(iso)
    return d.toLocaleString('tr-TR', { dateStyle: 'short', timeStyle: 'medium' })
  } catch { return iso }
}

function tryPretty(text) {
  if (!text) return text
  try { return JSON.stringify(JSON.parse(text), null, 2) }
  catch { return text }
}

/**
 * 2026-05-21: Hata tab'inin üstündeki "friendly" mesaj.
 * Netsis response body örneği:
 *   { "IsSuccessful": false, "ErrorCode": "101",
 *     "ErrorDesc": "Hata Kodu : 402\r\nDetay : ...<ErrorHeader>...</ErrorHeader>\r\n<Hata>\r\nTKL202600000013 Nolu Evrak Daha önceden kaydedilmiş..." }
 * <Hata>...</Hata> içeriği (veya kapanış yoksa sona kadar) parse edilip kullanıcı dostu kısa mesaj olarak gösterilir.
 *
 * Diğer provider'lar için extension: <Error>, <Message>, <Detail> pattern'leri de denenir.
 * Hiçbiri eşleşmezse null döner — caller ham mesajı zaten gösterir.
 *
 * @returns {{ label: string, text: string } | null}
 */
function extractFriendlyError(errorMessage, responseBody) {
  var sources = [errorMessage, responseBody].filter(Boolean)
  // Önce JSON parse — ErrorDesc / Message / errorMessage gibi alanları ara
  for (var i = 0; i < sources.length; i++) {
    var src = sources[i]
    if (typeof src !== 'string') continue
    var unescaped = src.replace(/\\r\\n/g, '\n').replace(/\\n/g, '\n').replace(/\\r/g, '\n')

    // 1) Netsis <Hata>...</Hata> (kapanış opsiyonel — sona kadar)
    var m = unescaped.match(/<Hata>\s*([\s\S]*?)(?:<\/Hata>|$)/i)
    if (m && m[1]) {
      var msg = cleanupError(m[1])
      if (msg) return { label: 'Hata Mesajı', text: msg }
    }

    // 2) <Error>...</Error>
    m = unescaped.match(/<Error>\s*([\s\S]*?)(?:<\/Error>|$)/i)
    if (m && m[1]) {
      var em = cleanupError(m[1])
      if (em) return { label: 'Hata', text: em }
    }

    // 3) JSON ErrorDesc / Message / errorMessage / errorDetail alanları
    try {
      var obj = JSON.parse(src)
      var candidates = [
        obj.ErrorDesc, obj.errorDesc, obj.Message, obj.message,
        obj.errorMessage, obj.error_message, obj.errorDetail, obj.detail,
        obj.error && obj.error.message,
      ].filter(function (x) { return x && typeof x === 'string' })
      // JSON içindeki ilk uygun field'da yine <Hata> ara, yoksa ilk satırı dön
      for (var k = 0; k < candidates.length; k++) {
        var unesc = candidates[k].replace(/\\r\\n/g, '\n').replace(/\\n/g, '\n').replace(/\\r/g, '\n')
        var inner = unesc.match(/<Hata>\s*([\s\S]*?)(?:<\/Hata>|$)/i)
        if (inner && inner[1]) {
          var v = cleanupError(inner[1])
          if (v) return { label: 'Hata Mesajı', text: v }
        }
        // <Hata> yoksa ilk satır + 'Hata Kodu' bilgisi
        var firstLine = unesc.split('\n').map(function (l) { return l.trim() }).filter(Boolean)[0]
        if (firstLine && firstLine.length < 200) return { label: 'Hata', text: firstLine }
      }
    } catch (_) { /* JSON değil — skip */ }
  }
  return null
}

function cleanupError(raw) {
  return String(raw || '')
    .replace(/<[^>]+>/g, ' ')        // diğer iç tag'leri at
    .replace(/\\r\\n/g, '\n')
    .replace(/\\n/g, '\n')
    .replace(/\s+/g, ' ')
    .trim()
}

function RunDetailModal({ runId, onClose }) {
  const [run, setRun]         = useState(null)
  const [loading, setLoading] = useState(true)
  const [tab, setTab]         = useState('summary')

  useEffect(() => {
    const h = e => { if (e.key === 'Escape') onClose() }
    window.addEventListener('keydown', h)
    return () => window.removeEventListener('keydown', h)
  }, [onClose])

  useEffect(() => {
    setLoading(true)
    fetch(`/Integrations/api/runs/${runId}`, { credentials: 'same-origin' })
      .then(r => r.json())
      .then(d => { if (d.success) setRun(d.run); else toast(d.error || 'Detay yüklenemedi', 'err') })
      .catch(e => toast('Sunucu hatası: ' + e.message, 'err'))
      .finally(() => setLoading(false))
  }, [runId])

  const cfg = run ? STATUS_CONFIG[run.status] || STATUS_CONFIG.Failed : null

  return (
    <div className="iw-modal-bd" onClick={onClose}>
      <div className="eem-modal" onClick={e => e.stopPropagation()}
           style={{ width: 880, height: 640, maxHeight: '92vh' }}>
        {/* Header */}
        <div className="eem-header">
          <div className="eem-title">
            <Activity size={15} style={{ verticalAlign: 'middle', marginRight: 8 }} />
            Run #{runId}
            {run && (
              <span className="eem-title-sub">
                {' '}— {run.integrationName} · {formatDate(run.startedAt)}
              </span>
            )}
          </div>
          <button className="eem-icon-btn" onClick={onClose} title="Kapat (Esc)">
            <X size={16} />
          </button>
        </div>

        {/* Tab nav */}
        <div style={{
          display: 'flex', gap: 0, padding: '0 12px',
          borderBottom: '1px solid var(--iw-border)', background: 'var(--iw-bg)',
        }}>
          {[
            { id: 'summary', label: 'Özet' },
            { id: 'request', label: 'Request' },
            { id: 'response', label: 'Response' },
            { id: 'error', label: 'Hata', show: run?.errorMessage },
          ].filter(t => t.show !== false).map(t => (
            <button key={t.id} onClick={() => setTab(t.id)}
                    style={{
                      padding: '10px 16px', fontSize: 12, fontWeight: 500,
                      background: 'transparent', border: 'none', cursor: 'pointer',
                      borderBottom: '2px solid ' + (tab === t.id ? 'var(--iw-indigo-color)' : 'transparent'),
                      color: tab === t.id ? 'var(--iw-indigo-color)' : 'var(--iw-muted)',
                    }}>
              {t.label}
            </button>
          ))}
        </div>

        {/* Content */}
        <div style={{ flex: 1, overflow: 'auto', padding: 16 }}>
          {loading && (
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', height: '100%' }}>
              <Loader2 className="iw-spin" size={32} />
            </div>
          )}
          {!loading && run && tab === 'summary' && (
            <div style={{ display: 'grid', gridTemplateColumns: '160px 1fr', gap: '8px 16px', fontSize: 12 }}>
              <SummaryRow label="Status">
                <span className={'il-chip il-chip-' + (cfg?.color === 'emerald' ? 'success' : cfg?.color === 'rose' ? 'failed' : 'runs')}>
                  {cfg?.icon && React.createElement(cfg.icon, { size: 11 })}
                  {cfg?.label || run.status}
                </span>
              </SummaryRow>
              <SummaryRow label="Integration">
                <a href={`/Integrations/Wizard/${run.integrationId}`} target="_blank" rel="noopener"
                   style={{ color: 'var(--iw-indigo-color)' }}>
                  {run.integrationName} <ExternalLink size={11} style={{ verticalAlign: 'middle' }} />
                </a>
              </SummaryRow>
              <SummaryRow label="Source Form">{run.sourceFormCode || '—'}</SummaryRow>
              <SummaryRow label="Source Record">{run.sourceRecordId || '—'}</SummaryRow>
              <SummaryRow label="Trigger">{run.triggerType}</SummaryRow>
              <SummaryRow label="HTTP Status">
                {run.httpStatusCode ? (
                  <span style={{
                    fontFamily: 'ui-monospace, Menlo, Consolas, monospace', fontWeight: 600,
                    color: run.httpStatusCode < 300 ? 'var(--iw-emerald-color)'
                         : run.httpStatusCode < 500 ? 'var(--iw-amber-color)'
                         : 'var(--iw-rose-color)',
                  }}>{run.httpStatusCode}</span>
                ) : '—'}
              </SummaryRow>
              <SummaryRow label="Süre">{formatDuration(run.durationMs)}</SummaryRow>
              <SummaryRow label="Başlangıç">{formatDate(run.startedAt)}</SummaryRow>
              <SummaryRow label="Bitiş">{formatDate(run.finishedAt)}</SummaryRow>
              <SummaryRow label="Tetikleyen">{run.triggeredBy || '—'}</SummaryRow>
              <SummaryRow label="Retry">{run.retryAttempt || 0}</SummaryRow>
            </div>
          )}
          {!loading && run && tab === 'request' && (
            <pre style={preStyle}>{tryPretty(run.requestBody) || '(boş)'}</pre>
          )}
          {!loading && run && tab === 'response' && (
            <pre style={preStyle}>{tryPretty(run.responseBody) || '(boş)'}</pre>
          )}
          {!loading && run && tab === 'error' && run.errorMessage && (() => {
            // 2026-05-21: Hata tab'ında üstte parse edilmiş kısa mesaj (örn. Netsis
            // <Hata>...</Hata> içeriği), altında ham errorMessage. Provider'a göre
            // pattern eşleştirme — eşleşme yoksa ham metin tek başına gösterilir.
            var parsed = extractFriendlyError(run.errorMessage, run.responseBody);
            return (
              <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
                {parsed && (
                  <div style={{
                    padding: '14px 16px',
                    borderRadius: 8,
                    background: 'rgba(244, 63, 94, 0.10)',
                    border: '1px solid var(--iw-rose-color)',
                    color: 'var(--iw-rose-color)',
                    fontSize: 13.5, fontWeight: 600,
                    lineHeight: 1.5,
                    whiteSpace: 'pre-wrap',
                    wordBreak: 'break-word',
                  }}>
                    <div style={{
                      fontSize: 10, fontWeight: 700, letterSpacing: '0.05em',
                      textTransform: 'uppercase', opacity: 0.7, marginBottom: 6,
                    }}>
                      {parsed.label}
                    </div>
                    {parsed.text}
                  </div>
                )}
                <details open={!parsed}>
                  <summary style={{
                    cursor: 'pointer', fontSize: 11, color: 'var(--iw-muted)',
                    padding: '6px 0', userSelect: 'none',
                  }}>
                    Ham hata mesajı (provider yanıtı tam içerik)
                  </summary>
                  <pre style={{ ...preStyle, color: 'var(--iw-rose-color)', borderColor: 'var(--iw-rose-color)', marginTop: 6 }}>
                    {run.errorMessage}
                  </pre>
                </details>
              </div>
            );
          })()}
        </div>

        {/* Footer */}
        <div className="eem-footer">
          <button className="iw-btn-secondary" onClick={onClose}>Kapat</button>
        </div>
      </div>
    </div>
  )
}

function SummaryRow({ label, children }) {
  return (
    <>
      <div style={{ color: 'var(--iw-muted)', fontWeight: 500 }}>{label}</div>
      <div style={{ color: 'var(--iw-text)' }}>{children}</div>
    </>
  )
}

const preStyle = {
  margin: 0, padding: 14, fontFamily: "'JetBrains Mono','Consolas',monospace", fontSize: 11,
  background: 'var(--iw-bg)', border: '1px solid var(--iw-border)', borderRadius: 8,
  whiteSpace: 'pre-wrap', wordBreak: 'break-word', lineHeight: 1.5,
  color: 'var(--iw-text)', maxHeight: 'none',
}

export default function IntegrationRunsList({ config }) {
  const [runs, setRuns]         = useState([])
  const [loading, setLoading]   = useState(true)
  const [search, setSearch]     = useState('')
  const [statusFilter, setStat] = useState('all')
  const [sinceDays, setSince]   = useState(7)
  const [detailId, setDetail]   = useState(null)

  const refresh = useCallback(async () => {
    setLoading(true)
    try {
      const params = new URLSearchParams({ sinceDays: String(sinceDays), limit: '500' })
      if (statusFilter !== 'all') params.set('status', statusFilter)
      const r = await fetch(`/Integrations/api/runs?${params}`, { credentials: 'same-origin' })
      const d = await r.json()
      if (d.success) setRuns(d.runs || [])
      else toast(d.error || 'Run listesi alınamadı', 'err')
    } catch (e) {
      toast('Sunucu hatası: ' + e.message, 'err')
    } finally {
      setLoading(false)
    }
  }, [statusFilter, sinceDays])

  useEffect(() => { refresh() }, [refresh])

  const filtered = useMemo(() => {
    if (!search) return runs
    const q = search.toLowerCase()
    return runs.filter(r =>
      (r.integrationName || '').toLowerCase().includes(q) ||
      (r.sourceFormCode  || '').toLowerCase().includes(q) ||
      (r.sourceRecordId  || '').toLowerCase().includes(q) ||
      (r.errorMessage    || '').toLowerCase().includes(q)
    )
  }, [runs, search])

  // İstatistik bar (üstte 4 sayaç)
  const stats = useMemo(() => {
    const total = runs.length
    const success = runs.filter(r => r.status === 'Success').length
    const failed = runs.filter(r => r.status === 'Failed').length
    const other = total - success - failed
    return { total, success, failed, other }
  }, [runs])

  return (
    <div className="il-root">
      <div className="il-toolbar">
        <div className="il-title">
          <Activity size={16} />
          <span>Çalıştırma Logu</span>
          <span className="il-count">{filtered.length} / {runs.length}</span>
        </div>
        <div className="il-spacer" />

        {/* Filter chips */}
        <div style={{ display: 'flex', gap: 4 }}>
          {[
            { id: 'all',     label: 'Tümü',    count: stats.total },
            { id: 'Success', label: 'Başarı',  count: stats.success, color: 'emerald' },
            { id: 'Failed',  label: 'Hata',    count: stats.failed,  color: 'rose' },
          ].map(f => (
            <button key={f.id} onClick={() => setStat(f.id)}
                    style={{
                      padding: '5px 10px', borderRadius: 6, fontSize: 11, fontWeight: 500,
                      border: '1px solid ' + (statusFilter === f.id ? 'var(--iw-indigo-color)' : 'var(--iw-border)'),
                      background: statusFilter === f.id ? 'var(--iw-indigo-bg)' : 'var(--iw-surface)',
                      color: statusFilter === f.id ? 'var(--iw-indigo-color)' : 'var(--iw-muted)',
                      cursor: 'pointer',
                    }}>
              {f.label} <span style={{ opacity: 0.7 }}>({f.count})</span>
            </button>
          ))}
        </div>

        <select value={sinceDays} onChange={e => setSince(Number(e.target.value))}
                style={{ padding: '5px 10px', borderRadius: 6, border: '1px solid var(--iw-border)',
                         background: 'var(--iw-bg)', color: 'var(--iw-text)', fontSize: 12 }}>
          <option value={1}>Son 24 saat</option>
          <option value={7}>Son 7 gün</option>
          <option value={30}>Son 30 gün</option>
          <option value={90}>Son 90 gün</option>
        </select>

        <div className="il-search-wrap">
          <Search size={13} className="il-search-icon" />
          <input className="il-search" placeholder="Entegrasyon, kayıt ID, hata ara…"
                 value={search} onChange={e => setSearch(e.target.value)} />
        </div>
        <button className="iw-btn-secondary" onClick={refresh} title="Yenile">
          <RefreshCw size={13} />
        </button>
      </div>

      <div className="il-list">
        {loading && (
          <div className="il-empty">
            <Loader2 className="iw-spin" size={32} /><span>Yükleniyor…</span>
          </div>
        )}

        {!loading && filtered.length === 0 && (
          <div className="il-empty">
            <Activity size={48} style={{ opacity: 0.3 }} />
            <span>{runs.length === 0 ? 'Henüz çalıştırma kaydı yok.' : 'Filtreye uyan kayıt yok.'}</span>
          </div>
        )}

        {!loading && filtered.map(r => {
          const cfg = STATUS_CONFIG[r.status] || STATUS_CONFIG.Failed
          const Icon = cfg.icon
          return (
            <div key={r.id} className="il-card" onClick={() => setDetail(r.id)}
                 style={{ cursor: 'pointer' }}>
              <div className="il-card-main">
                <div className="il-card-name" style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                  <span style={{
                    width: 22, height: 22, borderRadius: 6,
                    display: 'flex', alignItems: 'center', justifyContent: 'center',
                    background: `var(--iw-${cfg.color}-bg)`,
                    color: `var(--iw-${cfg.color}-color)`,
                    flexShrink: 0,
                  }}>
                    <Icon size={12} />
                  </span>
                  <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                    {r.integrationName}
                  </span>
                </div>
                <div className="il-card-desc">
                  <Clock size={10} style={{ verticalAlign: 'middle', marginRight: 4 }} />
                  {formatDate(r.startedAt)}
                  {r.sourceRecordId && <> · kayıt: <code>{r.sourceRecordId}</code></>}
                </div>
              </div>

              <div className="il-card-flow">
                <span>{r.sourceFormCode || '—'}</span>
                <span>
                  <Zap size={10} style={{ verticalAlign: 'middle', marginRight: 3, opacity: 0.6 }} />
                  {r.triggerType}
                </span>
              </div>

              <div className="il-chips">
                {r.httpStatusCode && (
                  <span className="il-chip il-chip-runs" style={{ fontFamily: 'ui-monospace, Menlo, Consolas, monospace' }}>
                    HTTP {r.httpStatusCode}
                  </span>
                )}
                <span className="il-chip il-chip-runs">
                  ⏱ {formatDuration(r.durationMs)}
                </span>
                {r.retryAttempt > 0 && (
                  <span className="il-chip il-chip-trigger">↻ {r.retryAttempt}</span>
                )}
              </div>

              <div className="il-actions">
                <button className="il-act il-act-edit" title="Detay"
                        onClick={(e) => { e.stopPropagation(); setDetail(r.id) }}>
                  <ExternalLink size={14} />
                </button>
              </div>
            </div>
          )
        })}
      </div>

      {detailId && <RunDetailModal runId={detailId} onClose={() => setDetail(null)} />}
    </div>
  )
}
