import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import {
  Play, Square, AlertTriangle, Search, X, ChevronDown, ChevronRight,
  Database, Globe, ServerCrash, Timer,
} from 'lucide-react'

/**
 * Canlı İzleme (SQL Profiler benzeri) — /AuditLog ekranının "Canlı İzleme" sekmesi.
 * Sunucu sözleşmesi (backend ajanı tarafından sabit tutulur, değiştirme):
 *   POST /AuditLog/Trace/Start  { durationMinutes } -> { ok, expiresAt }
 *   POST /AuditLog/Trace/Stop                       -> { ok }
 *   GET  /AuditLog/Trace/Events?after=<seq>          -> { ok, running, expiresAt, droppedCount, events[] }
 * events[]: { seq, ts, kind:'sql'|'request'|'error', requestId, durationMs, text, parameters, path, method, statusCode, error, database }
 */

const DURATION_OPTIONS = [5, 10, 30, 60]
const POLL_MS = 1500
const MAX_EVENTS = 2000 // liste sınırsız büyümesin — eski kayıtlar baştan atılır (tampon taşması DEĞİL, yalnız ekran belleği)

const KIND_META = {
  sql: { label: 'SQL', icon: Database, cls: 'sql' },
  request: { label: 'İstek', icon: Globe, cls: 'request' },
  error: { label: 'Hata', icon: ServerCrash, cls: 'error' },
}

function csrfToken() {
  try {
    const el = document.querySelector('input[name="__RequestVerificationToken"]')
    return el ? el.value : ''
  } catch (_) { return '' }
}

function formatClock(ms) {
  if (ms == null || ms < 0) return '00:00'
  const total = Math.floor(ms / 1000)
  const m = Math.floor(total / 60)
  const s = total % 60
  return String(m).padStart(2, '0') + ':' + String(s).padStart(2, '0')
}

function formatTime(iso) {
  if (!iso) return ''
  const d = new Date(iso)
  if (isNaN(d.getTime())) return String(iso)
  return d.toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit', second: '2-digit' })
}

/**
 * Yanıtı güvenle JSON'a çevirir. Sunucu 404/500 döndüğünde ya da gövde boşsa
 * ham `r.json()` "Unexpected end of JSON input" gibi teşhis edilemez bir hata
 * firlatiyordu — kullanıcı bunu ekranda görüyordu. Artık HTTP durumunu içeren
 * anlaşılır bir mesaj üretiliyor.
 */
function readJson(r) {
  return r.text().then(function (txt) {
    if (!r.ok) {
      var hint = r.status === 404
        ? 'Sunucu ucu bulunamadı (HTTP 404) — bu özellik henüz yüklenmemiş olabilir.'
        : 'Sunucu hatası (HTTP ' + r.status + ').'
      throw new Error(hint)
    }
    if (!txt) throw new Error('Sunucu boş yanıt döndü.')
    try { return JSON.parse(txt) }
    catch (e) { throw new Error('Sunucu yanıtı okunamadı (geçersiz biçim).') }
  })
}

export default function LiveTrace({ apiBase }) {
  const [duration, setDuration] = useState(10)
  const [running, setRunning] = useState(false)
  const [expiresAt, setExpiresAt] = useState(null)
  const [remainingMs, setRemainingMs] = useState(0)
  const [events, setEvents] = useState([])
  const [droppedCount, setDroppedCount] = useState(0)
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const [expanded, setExpanded] = useState(null)
  const [kindFilter, setKindFilter] = useState('')
  const [text, setText] = useState('')
  const [threshold, setThreshold] = useState(500)

  const lastSeqRef = useRef(0)
  const pollRef = useRef(null)
  const tickRef = useRef(null)

  const stopPolling = useCallback(() => {
    if (pollRef.current) { clearInterval(pollRef.current); pollRef.current = null }
  }, [])

  const fetchEvents = useCallback(() => {
    if (typeof document !== 'undefined' && document.hidden) return // sekme arka plandayken poll etme
    fetch(apiBase + '/Trace/Events?after=' + lastSeqRef.current, { credentials: 'same-origin' })
      .then(readJson)
      .then(d => {
        if (!d || !d.ok) { setError('İzleme akışı okunamadı.'); return }
        setError('')
        if (d.events && d.events.length) {
          lastSeqRef.current = d.events[d.events.length - 1].seq
          setEvents(prev => {
            const merged = prev.concat(d.events)
            return merged.length > MAX_EVENTS ? merged.slice(merged.length - MAX_EVENTS) : merged
          })
        }
        setDroppedCount(d.droppedCount || 0)
        setExpiresAt(d.expiresAt || null)
        if (!d.running) { setRunning(false); stopPolling() }
      })
      .catch(e => setError('İzleme akışı okunamadı: ' + (e && e.message ? e.message : String(e))))
  }, [apiBase, stopPolling])

  useEffect(() => {
    if (!running) return undefined
    fetchEvents()
    pollRef.current = setInterval(fetchEvents, POLL_MS)
    return () => stopPolling()
  }, [running, fetchEvents, stopPolling])

  // Geri sayım: sunucudan gelen expiresAt'ten yerel hesap — poll gecikse de sapma birikmez.
  // Süre dolunca arayüz kendiliğinden durur (sunucu zaten söndürüyor, burada yanlış "çalışıyor" göstermeyi önler).
  useEffect(() => {
    if (!running || !expiresAt) { setRemainingMs(0); return undefined }
    const tick = () => {
      const ms = new Date(expiresAt).getTime() - Date.now()
      setRemainingMs(ms)
      if (ms <= 0) { setRunning(false); stopPolling() }
    }
    tick()
    tickRef.current = setInterval(tick, 1000)
    return () => { if (tickRef.current) clearInterval(tickRef.current) }
  }, [running, expiresAt, stopPolling])

  // Sekme arka plandan öne dönünce kaçırılan olayları hemen çek (bir sonraki poll'u bekleme).
  useEffect(() => {
    function onVis() { if (typeof document !== 'undefined' && !document.hidden && running) fetchEvents() }
    document.addEventListener('visibilitychange', onVis)
    return () => document.removeEventListener('visibilitychange', onVis)
  }, [running, fetchEvents])

  useEffect(() => () => { stopPolling(); if (tickRef.current) clearInterval(tickRef.current) }, [stopPolling])

  const start = () => {
    if (busy) return
    setBusy(true); setError('')
    fetch(apiBase + '/Trace/Start', {
      method: 'POST', credentials: 'same-origin',
      headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': csrfToken() },
      body: JSON.stringify({ durationMinutes: duration }),
    })
      .then(readJson)
      .then(d => {
        if (!d || !d.ok) { setError((d && d.error) || 'İzleme başlatılamadı.'); return }
        setEvents([]); lastSeqRef.current = 0; setDroppedCount(0); setExpanded(null)
        setExpiresAt(d.expiresAt); setRunning(true)
      })
      .catch(e => setError('İzleme başlatılamadı: ' + (e && e.message ? e.message : String(e))))
      .finally(() => setBusy(false))
  }

  const stop = () => {
    if (busy) return
    setBusy(true); setError('')
    fetch(apiBase + '/Trace/Stop', {
      method: 'POST', credentials: 'same-origin',
      headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': csrfToken() },
    })
      .then(readJson)
      .then(d => {
        if (!d || !d.ok) { setError((d && d.error) || 'İzleme durdurulamadı.'); return }
        setRunning(false); stopPolling()
      })
      .catch(e => setError('İzleme durdurulamadı: ' + (e && e.message ? e.message : String(e))))
      .finally(() => setBusy(false))
  }

  const filtered = useMemo(() => {
    let list = events
    if (kindFilter) list = list.filter(e => e.kind === kindFilter)
    if (text.trim()) {
      const q = text.trim().toLowerCase()
      list = list.filter(e =>
        (e.text || '').toLowerCase().includes(q) ||
        (e.path || '').toLowerCase().includes(q) ||
        (e.error || '').toLowerCase().includes(q) ||
        (e.parameters || []).some(p =>
          String((p && p.value) ?? '').toLowerCase().includes(q) ||
          String((p && p.name) ?? '').toLowerCase().includes(q)))
    }
    return list.slice().reverse() // en yeni üstte
  }, [events, kindFilter, text])

  const counts = useMemo(() => {
    const c = { sql: 0, request: 0, error: 0 }
    events.forEach(e => { if (c[e.kind] != null) c[e.kind]++ })
    return c
  }, [events])

  return (
    <div className="al-trace">
      <div className="al-trace-toolbar">
        <div className="al-trace-controls">
          <select value={duration} disabled={running} onChange={e => setDuration(Number(e.target.value))} title="İzleme süresi">
            {DURATION_OPTIONS.map(m => <option key={m} value={m}>{m} Dakika</option>)}
          </select>
          {!running ? (
            <button type="button" className="al-btn al-trace-start" disabled={busy} onClick={start}>
              <Play size={14} /> İzlemeyi Başlat
            </button>
          ) : (
            <button type="button" className="al-btn al-trace-stop" disabled={busy} onClick={stop}>
              <Square size={14} /> Durdur
            </button>
          )}
          {running && (
            <span className="al-trace-countdown" title="Kalan süre">
              <Timer size={13} /> {formatClock(remainingMs)}
            </span>
          )}
          {!running && expiresAt && events.length > 0 && (
            <span className="al-trace-ended">İzleme oturumu sona erdi.</span>
          )}
        </div>
        <div className="al-trace-filters">
          <div className="al-search al-trace-search">
            <Search size={13} />
            <input placeholder="SQL, yol, parametre ara…" value={text} onChange={e => setText(e.target.value)} />
            {text ? <X size={12} style={{ cursor: 'pointer' }} onClick={() => setText('')} /> : null}
          </div>
          <div className="al-chipset">
            <button type="button" className={'al-chip' + (kindFilter === '' ? ' is-active' : '')} onClick={() => setKindFilter('')}>Tümü ({events.length})</button>
            <button type="button" className={'al-chip' + (kindFilter === 'sql' ? ' is-active' : '')} onClick={() => setKindFilter('sql')}>SQL ({counts.sql})</button>
            <button type="button" className={'al-chip' + (kindFilter === 'request' ? ' is-active' : '')} onClick={() => setKindFilter('request')}>İstek ({counts.request})</button>
            <button type="button" className={'al-chip' + (kindFilter === 'error' ? ' is-active' : '')} onClick={() => setKindFilter('error')}>Hata ({counts.error})</button>
          </div>
          <label className="al-trace-threshold" title="Bu süreyi aşan SQL/istek satırları vurgulanır">
            Yavaş Eşik
            <input type="number" min={0} step={50} value={threshold}
              onChange={e => setThreshold(Math.max(0, Number(e.target.value) || 0))} /> ms
          </label>
        </div>
      </div>

      {error && (
        <div className="al-trace-error"><AlertTriangle size={14} /> {error}</div>
      )}
      {droppedCount > 0 && (
        <div className="al-trace-dropped">
          <AlertTriangle size={14} /> {droppedCount.toLocaleString('tr-TR')} kayıt tampon taştığı için atlandı — liste eksik olabilir.
        </div>
      )}

      <div className="al-trace-list-wrap">
        {filtered.length === 0 ? (
          <div className="al-empty">
            <Timer size={30} />
            <div>{running ? 'Olay bekleniyor…' : 'İzleme kapalı. Başlatmak için yukarıdaki düğmeyi kullanın.'}</div>
          </div>
        ) : (
          <div className="al-trace-list">
            {filtered.map(e => {
              const meta = KIND_META[e.kind] || KIND_META.request
              const Icon = meta.icon
              const key = e.seq
              const open = expanded === key
              const slow = typeof e.durationMs === 'number' && e.durationMs >= threshold
              return (
                <div key={key} className={'al-trace-row' + (open ? ' is-open' : '') + (slow ? ' is-slow' : '')}
                  onClick={() => setExpanded(open ? null : key)}>
                  <div className="al-trace-row-main">
                    <span className="al-trace-caret">{open ? <ChevronDown size={13} /> : <ChevronRight size={13} />}</span>
                    <span className={'al-badge al-badge--' + meta.cls}><Icon size={12} /> {meta.label}</span>
                    <span className="al-trace-time">{formatTime(e.ts)}</span>
                    {typeof e.durationMs === 'number' && (
                      <span className={'al-trace-dur' + (slow ? ' is-slow' : '')}>{e.durationMs.toLocaleString('tr-TR')} ms</span>
                    )}
                    <span className="al-trace-summary" title={e.text || e.path || e.error || ''}>
                      {e.kind === 'sql'
                        ? (e.text || '').split('\n')[0]
                        : (e.kind === 'request'
                          ? ((e.method || '') + ' ' + (e.path || '') + (e.statusCode ? ' · ' + e.statusCode : ''))
                          : (e.error || e.text || ''))}
                    </span>
                  </div>
                  {open && (
                    <div className="al-trace-detail" onClick={ev => ev.stopPropagation()}>
                      {e.kind === 'request' && (
                        <div className="al-trace-detail-meta">
                          <span>Yöntem: <b>{e.method || '—'}</b></span>
                          <span>Yol: <b>{e.path || '—'}</b></span>
                          {e.statusCode ? <span>Durum: <b>{e.statusCode}</b></span> : null}
                          {e.requestId ? <span>İstek No: <b>{e.requestId}</b></span> : null}
                        </div>
                      )}
                      {e.text ? <pre className="al-trace-sql">{e.text}</pre> : null}
                      {e.error && e.kind === 'error' ? <pre className="al-trace-sql">{e.error}</pre> : null}
                      {e.parameters && e.parameters.length > 0 && (
                        <table className="al-diff-table al-trace-params">
                          <thead><tr><th>Parametre</th><th>Değer</th></tr></thead>
                          <tbody>
                            {e.parameters.map((p, pi) => (
                              <tr key={pi}>
                                <td className="al-diff-field">{p && p.name}</td>
                                <td className="al-trace-param-val">{String((p && p.value) ?? '')}</td>
                              </tr>
                            ))}
                          </tbody>
                        </table>
                      )}
                      {(e.database || (e.requestId && e.kind !== 'request')) && (
                        <div className="al-trace-detail-meta">
                          {e.database ? <span>Veritabanı: {e.database}</span> : null}
                          {e.requestId && e.kind !== 'request' ? <span>İstek No: {e.requestId}</span> : null}
                        </div>
                      )}
                    </div>
                  )}
                </div>
              )
            })}
          </div>
        )}
      </div>
    </div>
  )
}
