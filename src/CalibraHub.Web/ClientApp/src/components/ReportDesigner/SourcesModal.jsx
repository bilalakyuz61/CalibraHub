import React, { useState, useEffect, useCallback } from 'react'

function getCsrf() {
  return document.querySelector('input[name="__RequestVerificationToken"]')?.value
    || document.querySelector('meta[name="csrf-token"]')?.content
    || ''
}

/* ── küçük ikonlar (lucide-stili, stroke) ───────────────────────── */
const svgBase = { viewBox: '0 0 24 24', fill: 'none', stroke: 'currentColor', strokeWidth: 1.9, strokeLinecap: 'round', strokeLinejoin: 'round' }
const SvgDb     = p => <svg {...svgBase} {...p}><ellipse cx="12" cy="5" rx="9" ry="3" /><path d="M3 5v14c0 1.66 4.03 3 9 3s9-1.34 9-3V5" /><path d="M3 12c0 1.66 4.03 3 9 3s9-1.34 9-3" /></svg>
const SvgGrid   = p => <svg {...svgBase} {...p}><rect x="3" y="3" width="18" height="18" rx="2" /><path d="M3 9h18M3 15h18M9 3v18M15 3v18" /></svg>
const SvgCode   = p => <svg {...svgBase} {...p}><polyline points="16 18 22 12 16 6" /><polyline points="8 6 2 12 8 18" /></svg>
const SvgLayers = p => <svg {...svgBase} {...p}><path d="m12 2 9 5-9 5-9-5 9-5Z" /><path d="m3 12 9 5 9-5" /><path d="m3 17 9 5 9-5" /></svg>
const SvgBolt   = p => <svg {...svgBase} {...p}><path d="M13 2 3 14h7l-1 8 10-12h-7l1-8Z" /></svg>
const SvgClock  = p => <svg {...svgBase} {...p}><circle cx="12" cy="12" r="9" /><path d="M12 7v5l3 2" /></svg>
const SvgRows   = p => <svg {...svgBase} {...p}><rect x="3" y="5" width="18" height="14" rx="2" /><path d="M3 10h18M3 14.5h18" /></svg>
const SvgRefresh= p => <svg {...svgBase} {...p}><path d="M3 12a9 9 0 0 1 15-6.7L21 8" /><path d="M21 3v5h-5" /><path d="M21 12a9 9 0 0 1-15 6.7L3 16" /><path d="M3 21v-5h5" /></svg>
const SvgEdit   = p => <svg {...svgBase} {...p}><path d="M12 20h9" /><path d="M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4 12.5-12.5Z" /></svg>
const SvgTrash  = p => <svg {...svgBase} {...p}><path d="M3 6h18M8 6V4h8v2M19 6l-1 14H6L5 6" /></svg>
const SvgCal    = p => <svg {...svgBase} {...p}><rect x="3" y="4" width="18" height="18" rx="2" /><path d="M16 2v4M8 2v4M3 10h18" /></svg>
const SvgArrow  = p => <svg {...svgBase} {...p}><path d="M5 12h14M13 6l6 6-6 6" /></svg>

export default function SourcesModal({ open, onClose, onSelect, views = [], onSelectView, currentSource, onSaved }) {
  const [sources,      setSources]     = useState([])
  const [loading,      setLoading]     = useState(false)
  const [step,         setStep]        = useState('list')   // 'list' | 'form'
  const [kind,         setKind]        = useState('sql')    // 'sql' | 'view'
  const [editing,      setEditing]     = useState(null)
  const [saving,       setSaving]      = useState(false)
  const [error,        setError]       = useState(null)
  const [deleteTarget, setDeleteTarget] = useState(null)    // { id, name } | null
  const [viewQ,        setViewQ]       = useState('')
  const [materializing, setMaterializing] = useState(0)     // o an snapshot'ı yenilenen kaynak id'si

  const load = useCallback(async () => {
    setLoading(true)
    try {
      const r = await fetch('/api/report/sources', { credentials: 'same-origin' })
      const d = await r.json()
      setSources(Array.isArray(d) ? d : [])
    } catch { setSources([]) }
    finally { setLoading(false) }
  }, [])

  useEffect(() => {
    if (open) { load(); setStep('list'); setEditing(null); setError(null); setDeleteTarget(null); setViewQ('') }
  }, [open, load])

  if (!open) return null

  function pickKind(k) {
    setKind(k); setError(null); setViewQ('')
    if (k === 'sql') setEditing({ name: '', description: '', sqlQuery: '', cacheTtlMinutes: 5, materialize: false, refreshMode: 'off', refreshTime: '03:00', refreshWeekday: 1 })
    else             setEditing(null)
    setStep('form')
  }

  function startEdit(src) {
    setKind('sql')
    let rm = 'off', rt = '03:00', rw = 1
    if (src.refreshScheduleJson) {
      try { const s = JSON.parse(src.refreshScheduleJson); rm = s.mode || 'off'; if (s.time) rt = s.time; if (typeof s.weekday === 'number') rw = s.weekday } catch { /* ignore */ }
    }
    setEditing({ id: src.id, name: src.name, description: src.description || '', sqlQuery: src.sqlQuery, cacheTtlMinutes: src.cacheTtlMinutes, materialize: !!src.materialize, refreshMode: rm, refreshTime: rt, refreshWeekday: rw })
    setError(null); setDeleteTarget(null)
    setStep('form')
  }

  function buildScheduleJson(e) {
    if (!e.materialize || !e.refreshMode || e.refreshMode === 'off') return null
    if (e.refreshMode === 'hourly') return JSON.stringify({ mode: 'hourly' })
    if (e.refreshMode === 'weekly') return JSON.stringify({ mode: 'weekly', time: e.refreshTime || '03:00', weekday: e.refreshWeekday ?? 1 })
    return JSON.stringify({ mode: 'daily', time: e.refreshTime || '03:00' })
  }

  function backToList() { setStep('list'); setEditing(null); setError(null) }

  async function handleSave() {
    if (!editing) return
    if (!editing.name.trim())     { setError('Kaynak adı zorunlu'); return }
    if (!editing.sqlQuery.trim()) { setError('SQL sorgusu zorunlu'); return }
    setSaving(true); setError(null)
    try {
      const r = await fetch('/api/report/sources', {
        method: 'POST',
        credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': getCsrf() },
        body: JSON.stringify({ ...editing, refreshScheduleJson: buildScheduleJson(editing) }),
      })
      const d = await r.json()
      if (d.ok) { backToList(); load(); onSaved && onSaved(d.id) }
      else setError(d.error || 'Kayıt başarısız')
    } catch (e) { setError(e.message) }
    finally { setSaving(false) }
  }

  async function doDelete(id) {
    try {
      await fetch(`/api/report/sources/${id}`, {
        method: 'DELETE',
        credentials: 'same-origin',
        headers: { 'RequestVerificationToken': getCsrf() },
      })
      load()
    } catch { /* ignore */ }
  }

  // Snapshot'ı yenile (kaynağı materialize et — ağır sorgu tabloya yazılır)
  async function doMaterialize(id) {
    setMaterializing(id); setError(null)
    try {
      const r = await fetch(`/api/report/sources/${id}/materialize`, {
        method: 'POST', credentials: 'same-origin',
        headers: { 'RequestVerificationToken': getCsrf() },
      })
      const d = await r.json()
      if (!d.ok) setError(d.error || 'Yenileme başarısız')
      else onSaved && onSaved(id)   // snapshot tazelendi → paneller yeni veriyi çeksin
      load()
    } catch (e) { setError(e.message) }
    finally { setMaterializing(0) }
  }

  function fmtDate(s) {
    if (!s) return null
    try { return new Date(s).toLocaleString('tr-TR', { day: '2-digit', month: '2-digit', year: '2-digit', hour: '2-digit', minute: '2-digit' }) } catch { return s }
  }

  function scheduleLabel(json) {
    if (!json) return null
    try {
      const s = JSON.parse(json)
      if (!s.mode || s.mode === 'off') return null
      if (s.mode === 'hourly') return 'Saatlik'
      const days = ['Paz', 'Pzt', 'Sal', 'Çar', 'Per', 'Cum', 'Cmt']
      if (s.mode === 'weekly') return `${days[s.weekday ?? 1]} ${s.time || '03:00'}`
      return `Günlük ${s.time || '03:00'}`
    } catch { return null }
  }

  const ql = viewQ.trim().toLowerCase()
  const filteredViews = ql ? views.filter(v => (v.label || v.name).toLowerCase().includes(ql)) : views

  const headTitle = step === 'list' ? 'Veri Kaynağı'
    : kind === 'view' ? 'Hazır Görünüm Seç'
    : editing?.id ? 'Kaynağı Düzenle' : 'Yeni SQL Kaynağı'
  const headSub = step === 'list' ? 'Raporun beslendiği kaynağı seçin veya yönetin'
    : kind === 'view' ? 'SQL yazmadan mevcut bir görünüm seçin'
    : editing?.id ? 'Sorguyu, snapshot ve yenileme ayarlarını düzenleyin'
    : 'Kendi sorgunuzu yazıp kaydedin'
  const HeadIcon = step === 'form' && kind === 'sql' ? SvgCode : step === 'form' && kind === 'view' ? SvgGrid : SvgDb

  return (
    <div className="rs-backdrop" onClick={e => { if (e.target === e.currentTarget) { onClose(); setDeleteTarget(null) } }}>
      <div className="rs-modal">
        <div className="rs-head">
          <div className="rs-head__left">
            {step === 'form' && (
              <button type="button" className="rs-back" onClick={backToList} title="Geri">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.4" strokeLinecap="round" style={{ width: 15, height: 15 }}>
                  <polyline points="15 18 9 12 15 6" />
                </svg>
              </button>
            )}
            <span className="rs-head__icon"><HeadIcon style={{ width: 18, height: 18 }} /></span>
            <span className="rs-head__text">
              <span className="rs-head__h">{headTitle}</span>
              <span className="rs-head__sub">{headSub}</span>
            </span>
          </div>
          <button type="button" className="rs-close" onClick={() => { onClose(); setDeleteTarget(null) }} title="Kapat">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.4" strokeLinecap="round" style={{ width: 16, height: 16 }}>
              <path d="M18 6 6 18M6 6l12 12" />
            </svg>
          </button>
        </div>

        <div className="rs-body">
          {/* ── Landing: mevcut kaynaklar + yeni ekleme türleri ── */}
          {step === 'list' && (
            <>
              <div className="rs-secthead">
                <span className="rs-secthead__title">Mevcut Kaynaklar</span>
                {!loading && sources.length > 0 && <span className="rs-secthead__count">{sources.length}</span>}
              </div>

              {loading ? (
                <div className="rs-empty"><span className="rs-empty__spin" />Yükleniyor…</div>
              ) : sources.length === 0 ? (
                <div className="rs-empty rs-empty--box">
                  <span className="rs-empty__icon"><SvgDb style={{ width: 26, height: 26 }} /></span>
                  <span className="rs-empty__h">Henüz kayıtlı kaynak yok</span>
                  <span className="rs-empty__sub">Aşağıdan bir tür seçerek ilk kaynağınızı ekleyin.</span>
                </div>
              ) : (
                <ul className="rs-srclist">
                  {sources.map(src => {
                    const active = currentSource?.sourceType === 'saved' && currentSource?.sourceId === src.id
                    const snap   = !!src.materialize
                    const sched  = snap ? scheduleLabel(src.refreshScheduleJson) : null
                    const busy   = materializing === src.id
                    return (
                      <li key={src.id} className={`rs-src${active ? ' rs-src--active' : ''}`}>
                        <span className={`rs-src__icon rs-src__icon--${snap ? 'snap' : 'live'}`}>
                          {snap ? <SvgLayers style={{ width: 18, height: 18 }} /> : <SvgBolt style={{ width: 18, height: 18 }} />}
                        </span>

                        <div className="rs-src__main">
                          <div className="rs-src__top">
                            <span className="rs-src__name" title={src.name}>{src.name}</span>
                            <span className={`rs-pill rs-pill--${snap ? 'snap' : 'live'}`}>
                              <i className="rs-dot" />{snap ? 'Snapshot' : 'Canlı'}
                            </span>
                            {sched && (
                              <span className="rs-pill rs-pill--auto" title={`Otomatik yenileme: ${sched}`}>
                                <SvgCal style={{ width: 11, height: 11 }} />{sched}
                              </span>
                            )}
                          </div>
                          {src.description && <div className="rs-src__desc">{src.description}</div>}
                          <div className="rs-src__meta">
                            {snap ? (
                              <>
                                <span className="rs-meta"><SvgRows style={{ width: 12, height: 12 }} />{(src.materializedRows ?? 0).toLocaleString('tr-TR')} satır</span>
                                <span className="rs-meta"><SvgClock style={{ width: 12, height: 12 }} />{src.lastMaterialized ? fmtDate(src.lastMaterialized) : 'henüz yüklenmedi'}</span>
                              </>
                            ) : (
                              <span className="rs-meta"><SvgClock style={{ width: 12, height: 12 }} />cache {src.cacheTtlMinutes} dk</span>
                            )}
                          </div>
                        </div>

                        <div className="rs-src__actions">
                          {onSelect && (
                            <button type="button" className={`rs-btn rs-btn--sm ${active ? 'rs-btn--selected' : 'rs-btn--primary'}`} onClick={() => { if (!active) { onSelect(src); onClose() } }}>
                              {active ? (<><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="3" strokeLinecap="round" strokeLinejoin="round" style={{ width: 12, height: 12 }}><polyline points="20 6 9 17 4 12" /></svg>Seçili</>) : 'Seç'}
                            </button>
                          )}
                          {deleteTarget?.id === src.id ? (
                            <span className="rs-confirm-inline">
                              <span className="rs-confirm-inline__q">Silinsin mi?</span>
                              <button type="button" className="rs-btn rs-btn--sm rs-btn--outline" onClick={() => setDeleteTarget(null)}>Vazgeç</button>
                              <button type="button" className="rs-btn rs-btn--sm rs-btn--danger" onClick={() => { doDelete(src.id); setDeleteTarget(null) }}>Sil</button>
                            </span>
                          ) : (
                            <div className="rs-iconrow">
                              {snap && (
                                <button type="button" className="rs-iconbtn" disabled={busy} title="Snapshot'ı yenile" onClick={() => doMaterialize(src.id)}>
                                  <SvgRefresh style={{ width: 14, height: 14 }} className={busy ? 'rs-spin' : undefined} />
                                </button>
                              )}
                              <button type="button" className="rs-iconbtn" title="Düzenle" onClick={() => startEdit(src)}><SvgEdit style={{ width: 14, height: 14 }} /></button>
                              <button type="button" className="rs-iconbtn rs-iconbtn--danger" title="Sil" onClick={() => setDeleteTarget({ id: src.id, name: src.name })}><SvgTrash style={{ width: 14, height: 14 }} /></button>
                            </div>
                          )}
                        </div>
                      </li>
                    )
                  })}
                </ul>
              )}

              <div className="rs-secthead rs-secthead--mt"><span className="rs-secthead__title">Yeni Kaynak Ekle</span></div>
              <div className="rs-type-grid">
                <button type="button" className="rs-type-card" onClick={() => pickKind('view')}>
                  <span className="rs-type-card__go"><SvgArrow style={{ width: 15, height: 15 }} /></span>
                  <span className="rs-type-card__icon rs-type-card__icon--view"><SvgGrid style={{ width: 22, height: 22 }} /></span>
                  <span className="rs-type-card__title">Hazır Görünüm</span>
                  <span className="rs-type-card__desc">Mevcut bir veritabanı görünümünü seçin — SQL yazmadan.</span>
                </button>
                <button type="button" className="rs-type-card" onClick={() => pickKind('sql')}>
                  <span className="rs-type-card__go"><SvgArrow style={{ width: 15, height: 15 }} /></span>
                  <span className="rs-type-card__icon rs-type-card__icon--sql"><SvgCode style={{ width: 22, height: 22 }} /></span>
                  <span className="rs-type-card__title">Özel SQL Sorgusu</span>
                  <span className="rs-type-card__desc">Kendi SQL sorgunuzu yazın, parametreleyin ve kaydedin.</span>
                </button>
              </div>
            </>
          )}

          {/* ── Hazır Görünüm: doğrudan görünüm seç ── */}
          {step === 'form' && kind === 'view' && (
            <div className="rs-form">
              {views.length === 0 ? (
                <div className="rs-empty rs-empty--box">
                  <span className="rs-empty__icon"><SvgGrid style={{ width: 26, height: 26 }} /></span>
                  <span className="rs-empty__h">Kullanılabilir görünüm yok</span>
                  <span className="rs-empty__sub">Geri dönüp "Özel SQL Sorgusu" ile devam edin.</span>
                </div>
              ) : (
                <>
                  <div className="rs-search">
                    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" className="rs-search__ic"><circle cx="11" cy="11" r="7" /><path d="m21 21-4.3-4.3" /></svg>
                    <input className="rs-input rs-input--search" type="text" placeholder="Görünüm ara…" value={viewQ} onChange={e => setViewQ(e.target.value)} />
                  </div>
                  <ul className="rs-srclist">
                    {filteredViews.map(v => {
                      const active = currentSource?.sourceType === 'view' && currentSource?.source === v.name
                      return (
                        <li key={v.name} className={`rs-src${active ? ' rs-src--active' : ''}`}>
                          <span className="rs-src__icon rs-src__icon--view"><SvgGrid style={{ width: 18, height: 18 }} /></span>
                          <div className="rs-src__main">
                            <div className="rs-src__top">
                              <span className="rs-src__name" title={v.label || v.name}>{v.label || v.name}</span>
                              <span className="rs-pill rs-pill--view"><i className="rs-dot" />Görünüm</span>
                            </div>
                            {v.label && v.label !== v.name && <div className="rs-src__meta"><span className="rs-meta rs-meta--mono">{v.name}</span></div>}
                          </div>
                          <div className="rs-src__actions">
                            <button type="button" className={`rs-btn rs-btn--sm ${active ? 'rs-btn--selected' : 'rs-btn--primary'}`}
                              onClick={() => { if (!active) onSelectView && onSelectView(v.name) }}>
                              {active ? (<><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="3" strokeLinecap="round" strokeLinejoin="round" style={{ width: 12, height: 12 }}><polyline points="20 6 9 17 4 12" /></svg>Seçili</>) : 'Seç'}
                            </button>
                          </div>
                        </li>
                      )
                    })}
                    {filteredViews.length === 0 && <div className="rs-empty">Eşleşen görünüm yok</div>}
                  </ul>
                </>
              )}
            </div>
          )}

          {/* ── Özel SQL: yaz + kaydet ── */}
          {step === 'form' && kind === 'sql' && editing && (
            <div className="rs-form">
              <div className="rs-grid2">
                <div className="rs-field">
                  <label className="rs-label">Ad</label>
                  <input className="rs-input" type="text" placeholder="Ör: Aylık Satış Özeti"
                    value={editing.name} onChange={e => setEditing(v => ({ ...v, name: e.target.value }))} />
                </div>
                <div className="rs-field">
                  <label className="rs-label">Açıklama (opsiyonel)</label>
                  <input className="rs-input" type="text" placeholder="Ne işe yarıyor?"
                    value={editing.description} onChange={e => setEditing(v => ({ ...v, description: e.target.value }))} />
                </div>
              </div>
              <div className="rs-field">
                <label className="rs-label">SQL Sorgusu</label>
                <textarea className="rs-sql" rows={8}
                  placeholder={'SELECT\n  CAST(Tarih AS DATE) AS [time],\n  SUM(Tutar)          AS [value]\nFROM dbo.SatisTablosu\nGROUP BY CAST(Tarih AS DATE)\nORDER BY [time]'}
                  spellCheck={false}
                  value={editing.sqlQuery} onChange={e => setEditing(v => ({ ...v, sqlQuery: e.target.value }))} />
              </div>

              <div className="rs-field">
                <label className="rs-label">Veri Modu</label>
                <div className="rs-toggle-card">
                  <button type="button" className={`rd-toggle${editing.materialize ? ' rd-toggle--on' : ''}`}
                    onClick={() => setEditing(v => ({ ...v, materialize: !v.materialize }))}>
                    <span className="rd-toggle__thumb" />
                  </button>
                  <div className="rs-toggle-card__text">
                    <span className="rs-toggle-card__h">{editing.materialize ? 'Snapshot tablosu (ağır sorgu)' : 'Canlı SQL'}</span>
                    <span className="rs-toggle-card__sub">
                      {editing.materialize
                        ? 'Sorgu bir kez tabloya yazılır; rapor oradan (hızlı) okunur. "Yenile" ile tazelenir.'
                        : 'Her istekte çalışır, sonuç bellekte cache\'lenir.'}
                    </span>
                  </div>
                </div>
              </div>

              {editing.materialize ? (
                <div className="rs-field">
                  <label className="rs-label">Otomatik Yenileme (zamanlanmış)</label>
                  <div className="rs-sched-row">
                    <select className="rs-input" style={{ maxWidth: 130 }} value={editing.refreshMode || 'off'}
                      onChange={e => setEditing(v => ({ ...v, refreshMode: e.target.value }))}>
                      <option value="off">Kapalı</option>
                      <option value="hourly">Saatlik</option>
                      <option value="daily">Günlük</option>
                      <option value="weekly">Haftalık</option>
                    </select>
                    {(editing.refreshMode === 'daily' || editing.refreshMode === 'weekly') && (
                      <input type="time" className="rs-input" style={{ maxWidth: 120 }} value={editing.refreshTime || '03:00'}
                        onChange={e => setEditing(v => ({ ...v, refreshTime: e.target.value }))} />
                    )}
                    {editing.refreshMode === 'weekly' && (
                      <select className="rs-input" style={{ maxWidth: 130 }} value={editing.refreshWeekday ?? 1}
                        onChange={e => setEditing(v => ({ ...v, refreshWeekday: +e.target.value }))}>
                        <option value={1}>Pazartesi</option>
                        <option value={2}>Salı</option>
                        <option value={3}>Çarşamba</option>
                        <option value={4}>Perşembe</option>
                        <option value={5}>Cuma</option>
                        <option value={6}>Cumartesi</option>
                        <option value={0}>Pazar</option>
                      </select>
                    )}
                  </div>
                  <div className="rs-hint">Worker bu kaynağın snapshot'ını seçilen sıklıkta otomatik tazeler.</div>
                </div>
              ) : (
                <div className="rs-field">
                  <label className="rs-label">Cache Süresi (dakika)</label>
                  <input className="rs-input" type="number" min={1} max={1440} style={{ maxWidth: 140 }}
                    value={editing.cacheTtlMinutes} onChange={e => setEditing(v => ({ ...v, cacheTtlMinutes: +e.target.value || 5 }))} />
                </div>
              )}

              {error && <div className="rs-error">{error}</div>}

              <div className="rs-form__actions">
                <button type="button" className="rs-btn rs-btn--outline" onClick={backToList}>Geri</button>
                <button type="button" className="rs-btn rs-btn--primary" onClick={handleSave} disabled={saving}>
                  {saving ? 'Kaydediliyor…' : 'Kaydet'}
                </button>
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  )
}
