import React, { useState, useEffect, useCallback } from 'react'

function getCsrf() {
  return document.querySelector('input[name="__RequestVerificationToken"]')?.value
    || document.querySelector('meta[name="csrf-token"]')?.content
    || ''
}

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
    try { return new Date(s).toLocaleString('tr-TR') } catch { return s }
  }

  const ql = viewQ.trim().toLowerCase()
  const filteredViews = ql ? views.filter(v => (v.label || v.name).toLowerCase().includes(ql)) : views

  return (
    <div className="rs-backdrop" onClick={e => { if (e.target === e.currentTarget) { onClose(); setDeleteTarget(null) } }}>
      <div className="rs-modal">
        <div className="rs-head">
          <span className="rs-head__title">
            {step === 'form' && (
              <button type="button" className="rs-back" onClick={backToList} title="Geri">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" style={{ width: 14, height: 14 }}>
                  <polyline points="15 18 9 12 15 6" />
                </svg>
              </button>
            )}
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" style={{ width: 14, height: 14, color: '#6366f1' }}>
              <ellipse cx="12" cy="5" rx="9" ry="3" />
              <path d="M3 5v14c0 1.66 4.03 3 9 3s9-1.34 9-3V5" />
              <path d="M3 12c0 1.66 4.03 3 9 3s9-1.34 9-3" />
            </svg>
            {step === 'list' ? 'Veri Kaynağı'
              : kind === 'view' ? 'Hazır Görünüm Seç'
              : editing?.id ? 'Kaynağı Düzenle' : 'Yeni SQL Kaynağı'}
          </span>
          <button type="button" className="rs-close" onClick={() => { onClose(); setDeleteTarget(null) }}>
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" style={{ width: 14, height: 14 }}>
              <path d="M18 6 6 18M6 6l12 12" />
            </svg>
          </button>
        </div>

        <div className="rs-body">
          {/* ── Landing: mevcut kaynaklar + yeni ekleme türleri ── */}
          {step === 'list' && (
            <>
              <div className="rs-section__title">Mevcut Kaynaklar</div>
              {loading ? (
                <div className="rs-empty">Yükleniyor…</div>
              ) : sources.length === 0 ? (
                <div className="rs-empty" style={{ padding: '16px' }}>
                  Henüz kayıtlı kaynak yok.<br />
                  <small>Aşağıdan bir tür seçerek ekleyin.</small>
                </div>
              ) : (
                <ul className="rs-list">
                  {sources.map(src => {
                    const active = currentSource?.sourceType === 'saved' && currentSource?.sourceId === src.id
                    return (
                      <li key={src.id} className={`rs-item${active ? ' rs-item--active' : ''}`}>
                        <div className="rs-item__info">
                          <span className="rs-item__name">{src.name}</span>
                          {src.description && <span className="rs-item__desc">{src.description}</span>}
                          {src.materialize ? (
                            <span className="rs-item__ttl rs-item__snap">
                              ⛁ Snapshot · {src.lastMaterialized ? `${fmtDate(src.lastMaterialized)} · ${src.materializedRows ?? 0} satır` : 'henüz yüklenmedi'}
                            </span>
                          ) : (
                            <span className="rs-item__ttl">Canlı SQL · cache {src.cacheTtlMinutes}dk</span>
                          )}
                        </div>
                        <div className="rs-item__actions">
                          {onSelect && (
                            <button type="button" className={`rs-btn rs-btn--sm ${active ? 'rs-btn--primary' : 'rs-btn--ghost'}`} onClick={() => { onSelect(src); onClose() }}>
                              {active ? '✓ Seçili' : 'Seç'}
                            </button>
                          )}
                          {src.materialize && (
                            <button type="button" className="rs-btn rs-btn--sm rs-btn--outline" disabled={materializing === src.id} onClick={() => doMaterialize(src.id)}>
                              {materializing === src.id ? 'Yükleniyor…' : 'Yenile'}
                            </button>
                          )}
                          <button type="button" className="rs-btn rs-btn--sm rs-btn--outline" onClick={() => startEdit(src)}>Düzenle</button>
                          {deleteTarget?.id === src.id ? (
                            <span className="rs-confirm-inline">
                              <span>Silinsin mi?</span>
                              <button type="button" className="rs-btn rs-btn--sm rs-btn--outline" onClick={() => setDeleteTarget(null)}>Vazgeç</button>
                              <button type="button" className="rs-btn rs-btn--sm rs-btn--danger" onClick={() => { doDelete(src.id); setDeleteTarget(null) }}>Sil</button>
                            </span>
                          ) : (
                            <button type="button" className="rs-btn rs-btn--sm rs-btn--danger" onClick={() => setDeleteTarget({ id: src.id, name: src.name })}>Sil</button>
                          )}
                        </div>
                      </li>
                    )
                  })}
                </ul>
              )}

              <div className="rs-section__title rs-section__title--mt">Yeni Kaynak Ekle</div>
              <div className="rs-type-grid">
                <button type="button" className="rs-type-card" onClick={() => pickKind('view')}>
                  <span className="rs-type-card__icon">
                    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" style={{ width: 26, height: 26 }}>
                      <rect x="3" y="3" width="18" height="18" rx="2" />
                      <path d="M3 9h18M3 15h18M9 3v18M15 3v18" />
                    </svg>
                  </span>
                  <span className="rs-type-card__title">Hazır Görünüm</span>
                  <span className="rs-type-card__desc">Mevcut bir veritabanı görünümünü seçin — SQL yazmadan.</span>
                </button>
                <button type="button" className="rs-type-card" onClick={() => pickKind('sql')}>
                  <span className="rs-type-card__icon">
                    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" style={{ width: 26, height: 26 }}>
                      <polyline points="16 18 22 12 16 6" />
                      <polyline points="8 6 2 12 8 18" />
                    </svg>
                  </span>
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
                <div className="rs-empty" style={{ padding: '16px' }}>
                  Kullanılabilir görünüm yok.<br />
                  <small>Geri dönüp "Özel SQL Sorgusu" ile devam edin.</small>
                </div>
              ) : (
                <>
                  <div className="rs-field">
                    <input className="rs-input" type="text" placeholder="Görünüm ara…" value={viewQ} onChange={e => setViewQ(e.target.value)} />
                  </div>
                  <ul className="rs-list">
                    {filteredViews.map(v => {
                      const active = currentSource?.sourceType === 'view' && currentSource?.source === v.name
                      return (
                        <li key={v.name} className={`rs-item${active ? ' rs-item--active' : ''}`}>
                          <div className="rs-item__info">
                            <span className="rs-item__name">{v.label || v.name}</span>
                            <span className="rs-item__ttl">View</span>
                          </div>
                          <div className="rs-item__actions">
                            <button type="button" className={`rs-btn rs-btn--sm ${active ? 'rs-btn--primary' : 'rs-btn--ghost'}`}
                              onClick={() => { onSelectView && onSelectView(v.name) }}>
                              {active ? '✓ Seçili' : 'Seç'}
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
              <div className="rs-field">
                <label className="rs-label">SQL Sorgusu</label>
                <textarea className="rs-sql" rows={8}
                  placeholder={'SELECT\n  CAST(Tarih AS DATE) AS [time],\n  SUM(Tutar)          AS [value]\nFROM dbo.SatisTablosu\nGROUP BY CAST(Tarih AS DATE)\nORDER BY [time]'}
                  spellCheck={false}
                  value={editing.sqlQuery} onChange={e => setEditing(v => ({ ...v, sqlQuery: e.target.value }))} />
              </div>
              <div className="rs-field">
                <label className="rs-label">Snapshot tablosu (ağır sorgu)</label>
                <div className="rs-toggle-row">
                  <button type="button" className={`rd-toggle${editing.materialize ? ' rd-toggle--on' : ''}`}
                    onClick={() => setEditing(v => ({ ...v, materialize: !v.materialize }))}>
                    <span className="rd-toggle__thumb" />
                  </button>
                  <span className="rs-toggle-hint">
                    {editing.materialize
                      ? 'Sorgu bir kez tabloya yazılır; rapor oradan (hızlı) okunur. "Yenile" ile tazelenir.'
                      : 'Canlı SQL — her istekte çalışır (bellek cache).'}
                  </span>
                </div>
              </div>

              {editing.materialize && (
                <div className="rs-field">
                  <label className="rs-label">Otomatik yenileme (zamanlanmış)</label>
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
                  <div className="rs-toggle-hint">Worker bu kaynağın snapshot'ını seçilen sıklıkta otomatik tazeler.</div>
                </div>
              )}

              {!editing.materialize && (
                <div className="rs-field">
                  <label className="rs-label">Cache Süresi (dakika)</label>
                  <input className="rs-input" type="number" min={1} max={1440}
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
