/**
 * IntegrationQueue — "Aktarım Kuyruğu" sayfası
 *
 * Sol panel: Manual tetikleyicili aktif entegrasyon listesi (rozetlerle: bekleyen/hatalı/hariç)
 * Sağ panel: Seçili entegrasyonun kayıt listesi
 *   - Filtre tab'ları: [Aktif] [Bekleyen] [Hatalı] [Gönderilen] [Hariç] [Tümü]
 *   - Toplu seçim + [Seçilileri Aktar] / [Seçilileri Hariç Tut] / [Geri Al]
 *   - Her satırda: ⚡ Aktar, ⊘ Hariç Tut (veya ↩ Geri Al)
 *   - Hata satırlarında [Detay] → modal'da tam hata
 *   - Aktarım sırasında progress, sonunda özet toast
 */
import React, { useState, useEffect, useMemo, useCallback } from 'react'
import {
  Loader2, Send, Ban, RotateCcw, AlertCircle, CheckCircle2,
  RefreshCw, Search, Eye, X, Plug,
} from 'lucide-react'

const FILTER_TABS = [
  { key: 'active',  label: 'Aktif',      desc: 'Bekleyen + Hatalı' },
  { key: 'pending', label: 'Bekleyen',   desc: 'Hiç gönderilmemiş' },
  { key: 'failed',  label: 'Hatalı',     desc: 'Son deneme başarısız' },
  { key: 'sent',    label: 'Gönderilen', desc: 'Başarıyla aktarıldı' },
  { key: 'skipped', label: 'Hariç',      desc: 'Manuel hariç tutulmuş' },
  { key: 'all',     label: 'Tümü',       desc: '' },
]

const STATUS_COLORS = {
  Pending: { bg: '#1f2937', fg: '#94a3b8', label: 'Bekliyor'  },
  Failed:  { bg: '#7f1d1d', fg: '#fca5a5', label: 'Hatalı'    },
  Sent:    { bg: '#064e3b', fg: '#6ee7b7', label: 'Gönderildi'},
  Skipped: { bg: '#3f3f46', fg: '#d4d4d8', label: 'Hariç'     },
}

export default function IntegrationQueue({ config }) {
  const apiBase = config?.apiBase || '/Integrations/api'

  const [integrations, setIntegrations] = useState([])
  const [selectedId, setSelectedId]     = useState(null)
  const [loading, setLoading]           = useState(true)

  // Yükleme: entegrasyon listesi
  useEffect(() => {
    (async () => {
      try {
        const r = await fetch(`${apiBase}/queue/integrations`, { credentials: 'same-origin' })
        const d = await r.json()
        if (d.success) {
          setIntegrations(d.items || [])
          if ((d.items || []).length > 0 && !selectedId) setSelectedId(d.items[0].integrationId)
        }
      } catch {/* */} finally { setLoading(false) }
    })()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [apiBase])

  const refreshIntegrations = useCallback(async () => {
    const r = await fetch(`${apiBase}/queue/integrations`, { credentials: 'same-origin' })
    const d = await r.json()
    if (d.success) setIntegrations(d.items || [])
  }, [apiBase])

  if (loading) {
    return (
      <div className="iq-root" style={{ padding: 40, textAlign: 'center', color: 'var(--iq-muted2)' }}>
        <Loader2 size={20} className="ch-spin" /> Yükleniyor…
      </div>
    )
  }

  return (
    <div className="iq-root" style={{
      display: 'flex', flexDirection: 'row', height: '100%', width: '100%',
      fontFamily: 'system-ui, -apple-system, sans-serif',
    }}>
      {/* SOL — Entegrasyon listesi */}
      <SidePanel
        integrations={integrations}
        selectedId={selectedId}
        onSelect={setSelectedId}
        onRefresh={refreshIntegrations}
      />

      {/* SAĞ — Kayıt listesi + işlemler */}
      <div style={{ flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column' }}>
        {selectedId ? (
          <QueueDetail
            key={selectedId}
            apiBase={apiBase}
            integration={integrations.find(x => x.integrationId === selectedId)}
            onMutated={refreshIntegrations}
          />
        ) : (
          <EmptyHint />
        )}
      </div>
    </div>
  )
}

// ────────────────────────────────────────────────────────────────────────────
// Sol panel — entegrasyon listesi + rozetler
// ────────────────────────────────────────────────────────────────────────────
function SidePanel({ integrations, selectedId, onSelect, onRefresh }) {
  return (
    <div style={{
      width: 320, minWidth: 260, borderRight: '1px solid var(--iq-border)',
      display: 'flex', flexDirection: 'column', background: 'var(--iq-sidebar)',
    }}>
      <div style={{
        padding: '16px 16px 12px', borderBottom: '1px solid var(--iq-border)',
        display: 'flex', alignItems: 'center', gap: 10,
      }}>
        <Send size={18} color="#6366f1" />
        <div style={{ flex: 1 }}>
          <div style={{ fontSize: 13, fontWeight: 700 }}>Aktarım Kuyruğu</div>
          <div style={{ fontSize: 11, color: 'var(--iq-muted)' }}>Manuel entegrasyonlar</div>
        </div>
        <button onClick={onRefresh} title="Yenile"
                style={btnIcon}>
          <RefreshCw size={14} />
        </button>
      </div>
      <div style={{ flex: 1, overflow: 'auto' }}>
        {integrations.length === 0 ? (
          <div style={{ padding: 24, textAlign: 'center', color: 'var(--iq-muted)', fontSize: 12 }}>
            Manuel tetikleyicili aktif entegrasyon bulunamadı.
          </div>
        ) : integrations.map(it => (
          /* 2026-05-22: Sidebar sadelestirildi —
             - Form name (Liste / Üst Bilgi) kaldirildi: integration adindan zaten belli
             - Pending/Failed/Skipped chip'leri kaldirildi: sag tarafta filter tab'larinda
               (Aktif/Bekleyen/Hatalı/Gönderilen/Hariç) zaten gözüküyor
             Sadece plug ikonu + integration adi (+ secili durumda indigo sol border). */
          <button key={it.integrationId}
                  onClick={() => onSelect(it.integrationId)}
                  style={{
                    display: 'flex', alignItems: 'center', gap: 10,
                    width: '100%', textAlign: 'left',
                    padding: '12px 16px', border: 'none', background: 'transparent',
                    color: 'var(--iq-text)', cursor: 'pointer',
                    borderLeft: it.integrationId === selectedId
                      ? '3px solid #6366f1' : '3px solid transparent',
                    backgroundColor: it.integrationId === selectedId ? 'var(--iq-row-sel)' : 'transparent',
                  }}>
            <Plug size={14} color="#6366f1" style={{ flexShrink: 0 }} />
            <div style={{
              flex: 1, fontSize: 13, fontWeight: 600,
              overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap',
            }}>
              {it.name}
            </div>
          </button>
        ))}
      </div>
    </div>
  )
}

function Badge({ color, fg, label }) {
  return (
    <span style={{
      display: 'inline-flex', padding: '2px 8px', borderRadius: 999,
      background: color, color: fg, fontSize: 10, fontWeight: 600,
    }}>{label}</span>
  )
}

function EmptyHint() {
  return (
    <div style={{ flex: 1, display: 'flex', alignItems: 'center', justifyContent: 'center', color: 'var(--iq-muted)' }}>
      <div style={{ textAlign: 'center' }}>
        <Send size={48} color="var(--iq-border)" />
        <div style={{ marginTop: 12, fontSize: 14 }}>Sol panelden bir entegrasyon seç</div>
      </div>
    </div>
  )
}

// ────────────────────────────────────────────────────────────────────────────
// Sağ panel — kayıt listesi + işlemler
// ────────────────────────────────────────────────────────────────────────────
function QueueDetail({ apiBase, integration, onMutated }) {
  const [filter, setFilter]     = useState('active')
  const [search, setSearch]     = useState('')
  const [data, setData]         = useState({ rows: [], total: 0, summary: { pending: 0, failed: 0, sent: 0, skipped: 0 } })
  const [loading, setLoading]   = useState(false)
  const [page, setPage]         = useState(1)
  const PAGE_SIZE = 100
  const [selected, setSelected] = useState(new Set())
  const [running, setRunning]   = useState(false)
  const [progress, setProgress] = useState(null) // { current, total, results }
  const [errorModal, setErrorModal] = useState(null) // { row, ... }
  const [toast, setToast]       = useState(null)
  const [skipModal, setSkipModal] = useState(null) // { recordIds, batch:bool }

  const load = useCallback(async () => {
    setLoading(true)
    try {
      const params = new URLSearchParams({
        filter, page: String(page), pageSize: String(PAGE_SIZE),
      })
      if (search.trim()) params.set('search', search.trim())
      const r = await fetch(`${apiBase}/queue/${integration.integrationId}?${params}`, { credentials: 'same-origin' })
      const d = await r.json()
      if (d.success) {
        setData({ rows: d.rows || [], total: d.total || 0, summary: d.summary || {} })
        setSelected(new Set())
      } else {
        setToast({ kind: 'error', text: d.error || 'Yüklenemedi' })
      }
    } catch (e) {
      setToast({ kind: 'error', text: String(e) })
    } finally { setLoading(false) }
  }, [apiBase, integration?.integrationId, filter, search, page])

  useEffect(() => { load() }, [load])
  // Filtre/arama degisince ilk sayfaya don
  useEffect(() => { setPage(1) }, [filter, search])

  const toggleAll = () => {
    if (selected.size === data.rows.length) setSelected(new Set())
    else setSelected(new Set(data.rows.map(r => r.recordId)))
  }
  const toggle = (id) => {
    const next = new Set(selected)
    if (next.has(id)) next.delete(id); else next.add(id)
    setSelected(next)
  }

  // Aktarım — sıralı, devam et stratejisi
  const runBatch = async (recordIds) => {
    if (!recordIds || recordIds.length === 0) return
    setRunning(true)
    setProgress({ current: 0, total: recordIds.length, results: [] })

    // Backend tek POST'la hepsini sırayla işliyor — biz progress'i animasyonla göster
    let progressTimer = null
    let i = 0
    progressTimer = setInterval(() => {
      i = Math.min(i + 1, recordIds.length - 1)
      setProgress(p => p ? { ...p, current: i } : p)
    }, 600)

    try {
      const r = await fetch(`${apiBase}/queue/run`, {
        method: 'POST', credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ integrationId: integration.integrationId, recordIds }),
      })
      const d = await r.json()
      clearInterval(progressTimer)
      if (d.success) {
        setProgress({ current: recordIds.length, total: recordIds.length, results: d.results || [] })
        setToast({
          kind: d.fail > 0 ? 'warn' : 'success',
          text: `Aktarım tamamlandı — ${d.ok} başarılı, ${d.fail} hatalı`,
        })
        await load()
        onMutated && onMutated()
      } else {
        setToast({ kind: 'error', text: d.error || 'Aktarım başarısız' })
      }
    } catch (e) {
      clearInterval(progressTimer)
      setToast({ kind: 'error', text: String(e) })
    } finally {
      setRunning(false)
      setTimeout(() => setProgress(null), 2500)
    }
  }

  const runOne = (recordId) => runBatch([recordId])
  const runSelected = () => runBatch(Array.from(selected))

  const skipBatch = async (recordIds, reason) => {
    try {
      const r = await fetch(`${apiBase}/queue/skip`, {
        method: 'POST', credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ integrationId: integration.integrationId, recordIds, reason }),
      })
      const d = await r.json()
      if (d.success) {
        setToast({ kind: 'success', text: `${recordIds.length} kayıt hariç tutuldu` })
        await load()
        onMutated && onMutated()
      } else setToast({ kind: 'error', text: d.error || 'İşlem başarısız' })
    } catch (e) { setToast({ kind: 'error', text: String(e) }) }
  }

  const restoreBatch = async (recordIds) => {
    try {
      const r = await fetch(`${apiBase}/queue/restore`, {
        method: 'POST', credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ integrationId: integration.integrationId, recordIds }),
      })
      const d = await r.json()
      if (d.success) {
        setToast({ kind: 'success', text: `${recordIds.length} kayıt geri alındı` })
        await load()
        onMutated && onMutated()
      } else setToast({ kind: 'error', text: d.error || 'İşlem başarısız' })
    } catch (e) { setToast({ kind: 'error', text: String(e) }) }
  }

  const isSkippedTab = filter === 'skipped'
  const allChecked = selected.size > 0 && selected.size === data.rows.length

  return (
    <>
      {/* Başlık */}
      <div style={{
        padding: '14px 20px', borderBottom: '1px solid var(--iq-border)',
        display: 'flex', alignItems: 'center', gap: 12,
      }}>
        <div style={{ flex: 1 }}>
          <div style={{ fontSize: 16, fontWeight: 700 }}>{integration.name}</div>
          <div style={{ fontSize: 12, color: 'var(--iq-muted)' }}>
            {integration.formName || integration.formCode} ·
            Toplam: <strong style={{ color: 'var(--iq-text)' }}>{data.total}</strong>
          </div>
        </div>
      </div>

      {/* Filtre + arama */}
      <div style={{
        padding: '10px 20px', borderBottom: '1px solid var(--iq-border)',
        display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap',
      }}>
        {FILTER_TABS.map(tab => {
          // Aktif = bekleyen + hatali, Tümü = bekleyen + hatali + gönderilen + hariç.
          // Diger filtreler dogrudan summary[key]'i okur.
          const sm = data.summary || {}
          let cnt
          if      (tab.key === 'active') cnt = (sm.pending || 0) + (sm.failed || 0)
          else if (tab.key === 'all')    cnt = (sm.pending || 0) + (sm.failed || 0) + (sm.sent || 0) + (sm.skipped || 0)
          else                            cnt = sm[tab.key]
          const active = filter === tab.key
          return (
            <button key={tab.key}
                    onClick={() => setFilter(tab.key)}
                    title={tab.desc}
                    style={{
                      padding: '6px 12px', borderRadius: 6,
                      background: active ? '#6366f1' : 'var(--iq-filter-bg)',
                      color: active ? '#fff' : 'var(--iq-muted2)',
                      border: 'none', cursor: 'pointer', fontSize: 12, fontWeight: 600,
                    }}>
              {tab.label}{cnt !== undefined ? ` (${cnt})` : ''}
            </button>
          )
        })}
        <div style={{ flex: 1 }} />
        <div style={{ position: 'relative' }}>
          <Search size={12} style={{ position: 'absolute', left: 8, top: 8, color: 'var(--iq-muted)' }} />
          <input value={search} onChange={(e) => setSearch(e.target.value)}
                 placeholder="Kod / ad ara…"
                 style={{
                   padding: '6px 8px 6px 26px', fontSize: 12,
                   background: 'var(--iq-input-bg)', color: 'var(--iq-text)',
                   border: '1px solid var(--iq-border2)', borderRadius: 6, width: 220,
                 }} />
        </div>
        <button onClick={load} title="Yenile" style={btnIcon}><RefreshCw size={14} /></button>
      </div>

      {/* Toplu aksiyonlar */}
      {selected.size > 0 && (
        <div style={{
          padding: '8px 20px', background: 'var(--iq-bulk-bg)', color: '#e2e8f0',
          display: 'flex', alignItems: 'center', gap: 10, fontSize: 12,
        }}>
          <span style={{ fontWeight: 600 }}>{selected.size} seçili</span>
          <div style={{ flex: 1 }} />
          {!isSkippedTab ? (
            <>
              <button onClick={runSelected} disabled={running}
                      style={{ ...btnPrimary, opacity: running ? 0.5 : 1 }}>
                <Send size={12} /> Seçilileri Aktar
              </button>
              <button onClick={() => setSkipModal({ recordIds: Array.from(selected), batch: true })}
                      style={btnGhost}>
                <Ban size={12} /> Hariç Tut
              </button>
            </>
          ) : (
            <button onClick={() => restoreBatch(Array.from(selected))} style={btnPrimary}>
              <RotateCcw size={12} /> Geri Al
            </button>
          )}
        </div>
      )}

      {/* Tablo */}
      <div style={{ flex: 1, overflow: 'auto' }}>
        {loading ? (
          <div style={{ padding: 40, textAlign: 'center', color: 'var(--iq-muted2)' }}>
            <Loader2 size={20} className="ch-spin" /> Yükleniyor…
          </div>
        ) : data.rows.length === 0 ? (
          <div style={{ padding: 40, textAlign: 'center', color: 'var(--iq-muted)' }}>
            Bu filtrede kayıt yok.
          </div>
        ) : (
          <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 12 }}>
            <thead style={{ background: 'var(--iq-thead)', position: 'sticky', top: 0, zIndex: 1 }}>
              <tr style={{ color: 'var(--iq-muted2)', textAlign: 'left' }}>
                <th style={{ padding: '8px 12px', width: 30 }}>
                  <input type="checkbox" checked={allChecked} onChange={toggleAll} />
                </th>
                <th style={{ padding: '8px 12px', width: 100 }}>Kod</th>
                <th style={{ padding: '8px 12px' }}>Ad</th>
                <th style={{ padding: '8px 12px', width: 100 }}>Durum</th>
                <th style={{ padding: '8px 12px', width: 130 }}>Son İşlem</th>
                <th style={{ padding: '8px 12px', width: 200 }}>İşlem</th>
              </tr>
            </thead>
            <tbody>
              {data.rows.map(row => {
                const cfg = STATUS_COLORS[row.status] || STATUS_COLORS.Pending
                const isSel = selected.has(row.recordId)
                return (
                  <tr key={row.recordId}
                      style={{ borderBottom: '1px solid var(--iq-border)', background: isSel ? 'var(--iq-row-sel)' : 'transparent' }}>
                    <td style={{ padding: '8px 12px' }}>
                      <input type="checkbox" checked={isSel} onChange={() => toggle(row.recordId)} />
                    </td>
                    <td style={{ padding: '8px 12px', fontFamily: 'ui-monospace, Consolas, monospace', color: 'var(--iq-text)' }}>
                      {row.code || row.recordId}
                    </td>
                    <td style={{ padding: '8px 12px' }}>{row.name || '—'}</td>
                    <td style={{ padding: '8px 12px' }}>
                      <span style={{
                        display: 'inline-flex', alignItems: 'center', gap: 4,
                        padding: '2px 8px', borderRadius: 999,
                        background: cfg.bg, color: cfg.fg, fontSize: 10, fontWeight: 600,
                      }}>
                        {row.status === 'Sent'    && <CheckCircle2 size={10} />}
                        {row.status === 'Failed'  && <AlertCircle size={10} />}
                        {cfg.label}
                      </span>
                      {row.attemptCount > 0 && (
                        <span style={{ marginLeft: 6, fontSize: 10, color: 'var(--iq-muted)' }}>
                          {row.attemptCount}×
                        </span>
                      )}
                    </td>
                    <td style={{ padding: '8px 12px', fontSize: 11, color: 'var(--iq-muted2)' }}>
                      {row.lastSentAt ? new Date(row.lastSentAt).toLocaleString('tr-TR') : '—'}
                    </td>
                    <td style={{ padding: '8px 12px' }}>
                      <div style={{ display: 'flex', gap: 6, alignItems: 'center' }}>
                        {row.status !== 'Skipped' ? (
                          <>
                            <button onClick={() => runOne(row.recordId)} disabled={running}
                                    style={{ ...btnSmallPrimary, opacity: running ? 0.5 : 1 }}
                                    title="Şimdi aktar">
                              <Send size={11} /> Aktar
                            </button>
                            <button onClick={() => setSkipModal({ recordIds: [row.recordId], batch: false })}
                                    style={btnSmallGhost}
                                    title="Bu kaydı hariç tut (zaten ERP'de varsa)">
                              <Ban size={11} />
                            </button>
                            {row.lastError && (
                              <button onClick={() => setErrorModal(row)} style={btnSmallDanger}
                                      title="Hata detayı">
                                <Eye size={11} />
                              </button>
                            )}
                          </>
                        ) : (
                          <>
                            <button onClick={() => restoreBatch([row.recordId])}
                                    style={btnSmallPrimary} title="Tekrar kuyruğa al">
                              <RotateCcw size={11} /> Geri Al
                            </button>
                            {row.skipReason && (
                              <span style={{ fontSize: 11, color: 'var(--iq-muted2)', fontStyle: 'italic' }}>
                                ({row.skipReason})
                              </span>
                            )}
                          </>
                        )}
                      </div>
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        )}
      </div>

      {/* Pagination — Tümü ve diger filtrelerde 100'er sayfalik gezinme */}
      {!loading && data.total > PAGE_SIZE && (
        <div style={{
          padding: '10px 20px', borderTop: '1px solid var(--iq-border)',
          display: 'flex', alignItems: 'center', gap: 12, fontSize: 12, color: 'var(--iq-muted2)',
        }}>
          <span>
            {((page - 1) * PAGE_SIZE) + 1}
            –
            {Math.min(page * PAGE_SIZE, data.total)} / <strong style={{ color: 'var(--iq-text)' }}>{data.total}</strong>
          </span>
          <div style={{ flex: 1 }} />
          <button
            onClick={() => setPage(p => Math.max(1, p - 1))}
            disabled={page <= 1}
            style={{ ...btnGhost, opacity: page <= 1 ? 0.4 : 1, cursor: page <= 1 ? 'not-allowed' : 'pointer' }}
          >
            ← Önceki
          </button>
          <span style={{ padding: '0 6px' }}>
            Sayfa <strong style={{ color: 'var(--iq-text)' }}>{page}</strong> / {Math.max(1, Math.ceil(data.total / PAGE_SIZE))}
          </span>
          <button
            onClick={() => setPage(p => (p * PAGE_SIZE < data.total ? p + 1 : p))}
            disabled={page * PAGE_SIZE >= data.total}
            style={{ ...btnGhost, opacity: page * PAGE_SIZE >= data.total ? 0.4 : 1, cursor: page * PAGE_SIZE >= data.total ? 'not-allowed' : 'pointer' }}
          >
            Sonraki →
          </button>
        </div>
      )}

      {/* Progress overlay */}
      {progress && running && (
        <div style={overlayStyle}>
          <div style={{
            background: 'var(--iq-card-bg)', border: '1px solid var(--iq-card-bdr)', borderRadius: 12,
            padding: 24, minWidth: 320, textAlign: 'center',
          }}>
            <Loader2 size={28} className="ch-spin" color="#6366f1" />
            <div style={{ marginTop: 12, fontWeight: 600 }}>Aktarılıyor…</div>
            <div style={{ marginTop: 6, fontSize: 12, color: 'var(--iq-muted2)' }}>
              {progress.current}/{progress.total} kayıt
            </div>
            <div style={{
              marginTop: 12, height: 6, background: 'var(--iq-progress-bg)', borderRadius: 3, overflow: 'hidden',
            }}>
              <div style={{
                height: '100%', width: `${(progress.current / progress.total) * 100}%`,
                background: '#6366f1', transition: 'width 0.5s ease',
              }} />
            </div>
          </div>
        </div>
      )}

      {/* Hata detay modal */}
      {errorModal && (
        <ErrorDetailModal row={errorModal} onClose={() => setErrorModal(null)} />
      )}

      {/* Hariç tut modal */}
      {skipModal && (
        <SkipReasonModal
          count={skipModal.recordIds.length}
          onCancel={() => setSkipModal(null)}
          onConfirm={(reason) => {
            skipBatch(skipModal.recordIds, reason)
            setSkipModal(null)
          }}
        />
      )}

      {/* Toast */}
      {toast && (
        <Toast {...toast} onClose={() => setToast(null)} />
      )}
    </>
  )
}

// ────────────────────────────────────────────────────────────────────────────
// Modallar
// ────────────────────────────────────────────────────────────────────────────
function ErrorDetailModal({ row, onClose }) {
  return (
    <div style={overlayStyle} onClick={onClose}>
      <div onClick={(e) => e.stopPropagation()}
           style={{
             background: 'var(--iq-card-bg)', border: '1px solid var(--iq-err-bdr)', borderRadius: 10,
             padding: 0, minWidth: 480, maxWidth: 720, maxHeight: '80vh',
             display: 'flex', flexDirection: 'column',
           }}>
        <div style={{
          padding: '14px 18px', borderBottom: '1px solid var(--iq-border)',
          display: 'flex', alignItems: 'center', gap: 10,
        }}>
          <AlertCircle size={18} color="#fca5a5" />
          <div style={{ flex: 1 }}>
            <div style={{ fontWeight: 700, fontSize: 14 }}>Aktarım Hatası</div>
            <div style={{ fontSize: 11, color: 'var(--iq-muted2)' }}>
              {row.code || row.recordId} · {row.name || ''}
            </div>
          </div>
          <button onClick={onClose} style={btnIcon}><X size={16} /></button>
        </div>
        <div style={{ padding: 18, overflow: 'auto', fontSize: 12, color: 'var(--iq-text)' }}>
          <div style={{ marginBottom: 12 }}>
            <div style={{ color: 'var(--iq-muted2)', marginBottom: 4 }}>Deneme sayısı: <strong>{row.attemptCount}</strong></div>
            <div style={{ color: 'var(--iq-muted2)', marginBottom: 4 }}>
              Son deneme: {row.lastSentAt ? new Date(row.lastSentAt).toLocaleString('tr-TR') : '—'}
            </div>
            {row.lastRunId && (
              <div style={{ color: 'var(--iq-muted2)' }}>Run ID: <code>{row.lastRunId}</code></div>
            )}
          </div>
          <div style={{ color: '#fca5a5', fontWeight: 600, marginBottom: 6 }}>Hata mesajı:</div>
          <pre style={{
            background: 'var(--iq-err-bg)', border: '1px solid var(--iq-err-bdr)', borderRadius: 6,
            padding: 12, margin: 0, fontFamily: 'ui-monospace, Consolas, monospace', fontSize: 11,
            whiteSpace: 'pre-wrap', wordBreak: 'break-word', color: '#fecaca',
          }}>{row.lastError}</pre>
        </div>
      </div>
    </div>
  )
}

function SkipReasonModal({ count, onCancel, onConfirm }) {
  const [reason, setReason] = useState('Zaten ERP\'de tanımlı')
  return (
    <div style={overlayStyle} onClick={onCancel}>
      <div onClick={(e) => e.stopPropagation()}
           style={{
             background: 'var(--iq-card-bg)', border: '1px solid #6366f1', borderRadius: 10,
             padding: 20, minWidth: 380, maxWidth: 480,
           }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 14 }}>
          <Ban size={20} color="#a5b4fc" />
          <div style={{ flex: 1 }}>
            <div style={{ fontSize: 15, fontWeight: 700 }}>Hariç Tut</div>
            <div style={{ fontSize: 11, color: 'var(--iq-muted2)' }}>{count} kayıt kuyruktan çıkarılacak</div>
          </div>
        </div>
        <div style={{ fontSize: 12, color: 'var(--iq-text)', marginBottom: 10, lineHeight: 1.5 }}>
          Bu kayıtlar artık kuyrukta görünmeyecek. "Hariç" filtresinden geri alabilirsin.
        </div>
        <label style={{ display: 'block', fontSize: 11, color: 'var(--iq-muted2)', marginBottom: 4 }}>
          Sebep (opsiyonel)
        </label>
        <input value={reason} onChange={(e) => setReason(e.target.value)}
               style={{
                 width: '100%', padding: '8px 10px', fontSize: 12,
                 background: 'var(--iq-input-bg)', color: 'var(--iq-text)',
                 border: '1px solid var(--iq-border2)', borderRadius: 6,
                 boxSizing: 'border-box',
               }} />
        <div style={{ marginTop: 16, display: 'flex', gap: 8, justifyContent: 'flex-end' }}>
          <button onClick={onCancel} style={btnGhost}>Vazgeç</button>
          <button onClick={() => onConfirm(reason)} style={btnPrimary}>
            <Ban size={12} /> Hariç Tut
          </button>
        </div>
      </div>
    </div>
  )
}

function Toast({ kind, text, onClose }) {
  useEffect(() => {
    const t = setTimeout(onClose, 4000)
    return () => clearTimeout(t)
  }, [onClose])
  const color = kind === 'success' ? '#10b981'
              : kind === 'warn'    ? '#f59e0b'
              : kind === 'error'   ? '#ef4444'
              : '#6366f1'
  return (
    <div style={{
      position: 'fixed', bottom: 24, right: 24, zIndex: 1100,
      padding: '12px 16px', background: 'var(--iq-card-bg)', border: `1px solid ${color}`,
      borderRadius: 8, color: 'var(--iq-text)', fontSize: 13, fontWeight: 500,
      display: 'flex', alignItems: 'center', gap: 10, maxWidth: 460,
      boxShadow: '0 8px 24px rgba(0,0,0,0.5)',
    }}>
      {kind === 'success' && <CheckCircle2 size={16} color={color} />}
      {kind === 'error'   && <AlertCircle  size={16} color={color} />}
      {kind === 'warn'    && <AlertCircle  size={16} color={color} />}
      <span style={{ flex: 1 }}>{text}</span>
      <button onClick={onClose} style={{ ...btnIcon, color: 'var(--iq-muted2)' }}><X size={14} /></button>
    </div>
  )
}

// ────────────────────────────────────────────────────────────────────────────
// Inline style tokens
// ────────────────────────────────────────────────────────────────────────────
const overlayStyle = {
  position: 'fixed', inset: 0, background: 'rgba(0,0,0,0.6)',
  zIndex: 1050, display: 'flex', alignItems: 'center', justifyContent: 'center',
}
const btnIcon = {
  display: 'inline-flex', alignItems: 'center', justifyContent: 'center',
  padding: 6, background: 'transparent', color: 'var(--iq-muted2)',
  border: '1px solid var(--iq-border2)', borderRadius: 6, cursor: 'pointer',
}
const btnPrimary = {
  display: 'inline-flex', alignItems: 'center', gap: 6,
  padding: '6px 12px', background: '#6366f1', color: '#fff',
  border: 'none', borderRadius: 6, cursor: 'pointer', fontSize: 12, fontWeight: 600,
}
const btnGhost = {
  display: 'inline-flex', alignItems: 'center', gap: 6,
  padding: '6px 12px', background: 'transparent', color: 'var(--iq-text)',
  border: '1px solid var(--iq-border2)', borderRadius: 6, cursor: 'pointer', fontSize: 12, fontWeight: 600,
}
const btnSmallPrimary = {
  display: 'inline-flex', alignItems: 'center', gap: 4,
  padding: '4px 8px', background: '#6366f1', color: '#fff',
  border: 'none', borderRadius: 4, cursor: 'pointer', fontSize: 11, fontWeight: 600,
}
const btnSmallGhost = {
  display: 'inline-flex', alignItems: 'center',
  padding: 4, background: 'transparent', color: 'var(--iq-muted2)',
  border: '1px solid var(--iq-border2)', borderRadius: 4, cursor: 'pointer',
}
const btnSmallDanger = {
  display: 'inline-flex', alignItems: 'center',
  padding: 4, background: '#7f1d1d', color: '#fca5a5',
  border: 'none', borderRadius: 4, cursor: 'pointer',
}
