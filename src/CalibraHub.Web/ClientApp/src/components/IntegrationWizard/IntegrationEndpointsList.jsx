/**
 * IntegrationEndpointsList — REST endpoint kataloğunun admin sayfası.
 *
 * Wizard'da kullanıcı bunu seçer (Step 2). Bu sayfada CRUD yapılır:
 *   - Liste (ApiProfile bazlı gruplama)
 *   - Yeni endpoint ekle (modal)
 *   - Düzenle (modal)
 *   - Aktif/pasif toggle
 *   - Sil (onay modali — FK violation varsa anlamlı mesaj)
 */
import React, { useState, useEffect, useCallback, useMemo, useRef } from 'react'
import {
  Globe, Plus, Search, Edit2, Trash2, Power, RefreshCw,
  AlertTriangle, Loader2, Check, X, Upload, FileUp,
} from 'lucide-react'
import EndpointEditModal from './EndpointEditModal'

function getCsrf() {
  const el = document.querySelector('input[name="__RequestVerificationToken"]')
  return el ? el.value : ''
}
function toast(msg, kind) {
  if (window.CalibraHub?.toast) window.CalibraHub.toast(msg, kind || 'info')
}

/**
 * BulkImportModal — CSV'den toplu endpoint seed.
 *
 * Format: NetsisRestEndpoints.csv (Resource,Method,HttpMethod,UrlTemplate,InputType,ReturnType)
 * Profile mevcut bir id ile veya yeni profile yaratarak yapılır.
 * Idempotent: aynı (HttpMethod + UrlTemplate) varsa skip.
 */
function BulkImportModal({ profiles, onClose, onImported }) {
  // Varsayilan: aktif profile varsa "mevcut'a ekle" — kullanici degerli auth
  // ayarlarini (OAuth2Password + Netsis ek alanlari) yeni bir bos profile ile
  // ezmesin diye bilincli secim. Kullanici "yeni yarat" demek isterse degistirir.
  const [mode, setMode]               = useState(profiles.length > 0 ? 'existing' : 'new')
  const [selectedId, setSelectedId]   = useState(profiles[0]?.id || '')
  const [newName, setNewName]         = useState('Netsis (NetOpenX REST)')
  const [newBaseUrl, setNewBaseUrl]   = useState('http://localhost:7070')
  const [csvText, setCsvText]         = useState('')
  const [importing, setImporting]     = useState(false)
  const [result, setResult]           = useState(null)
  const fileRef = useRef(null)

  useEffect(() => {
    const h = e => { if (e.key === 'Escape' && !importing) onClose() }
    window.addEventListener('keydown', h)
    return () => window.removeEventListener('keydown', h)
  }, [onClose, importing])

  const handleFile = async (e) => {
    const f = e.target.files?.[0]
    if (!f) return
    try {
      const text = await f.text()
      setCsvText(text)
    } catch (err) {
      toast('Dosya okunamadı: ' + err.message, 'err')
    }
  }

  const handleImport = async () => {
    if (!csvText.trim()) { toast('CSV içeriği boş', 'err'); return }
    if (mode === 'existing' && !selectedId) { toast('Profile seçin', 'err'); return }
    if (mode === 'new' && (!newName.trim() || !newBaseUrl.trim())) {
      toast('Yeni profile için ad ve base URL zorunlu', 'err'); return
    }

    setImporting(true)
    setResult(null)
    try {
      const body = mode === 'existing'
        ? { apiProfileId: selectedId, csvText }
        : { newProfileName: newName.trim(), newProfileBaseUrl: newBaseUrl.trim(), csvText }

      const r = await fetch('/Integrations/api/endpoints/bulk-import', {
        method: 'POST', credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json', RequestVerificationToken: getCsrf() },
        body: JSON.stringify(body),
      })
      const d = await r.json()
      if (d.success) {
        setResult(d)
        toast(`✓ ${d.created} eklendi, ${d.skipped} atlandı`, 'ok')
      } else {
        toast(d.error || 'Import hatası', 'err')
      }
    } catch (e) {
      toast('Sunucu hatası: ' + e.message, 'err')
    } finally {
      setImporting(false)
    }
  }

  const handleDone = () => {
    onImported?.()
    onClose()
  }

  return (
    <div className="iw-modal-bd" onClick={() => !importing && onClose()}>
      <div className="eem-modal" onClick={e => e.stopPropagation()}
           style={{ width: 720, height: 'auto', maxHeight: '92vh' }}>
        <div className="eem-header">
          <div className="eem-title">
            <Upload size={15} style={{ verticalAlign: 'middle', marginRight: 8 }} />
            Endpoint Toplu Import
          </div>
          <button className="eem-icon-btn" title="Kapat (Esc)"
                  onClick={onClose} disabled={importing}>
            <X size={16} />
          </button>
        </div>

        <div className="eem-content" style={{ padding: '20px 24px' }}>
          {!result && (
            <>
              {/* Profile seçimi */}
              <div className="iw-field" style={{ maxWidth: 'none', marginBottom: 14 }}>
                <label>API Profile</label>
                <div style={{ display: 'flex', gap: 14, marginBottom: 8 }}>
                  <label style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 13, fontWeight: 'normal', cursor: 'pointer' }}>
                    <input type="radio" checked={mode === 'existing'} disabled={profiles.length === 0}
                           onChange={() => setMode('existing')} />
                    Mevcut profile'a ekle
                    {profiles.length > 0 && <span style={{ fontSize: 10, color: 'var(--iw-emerald-color)', fontWeight: 600 }}>(önerilir)</span>}
                  </label>
                  <label style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 13, fontWeight: 'normal', cursor: 'pointer' }}>
                    <input type="radio" checked={mode === 'new'} onChange={() => setMode('new')} />
                    Yeni profile yarat
                  </label>
                </div>

                {mode === 'existing' && (
                  <>
                    <select value={selectedId} onChange={e => setSelectedId(e.target.value)} disabled={importing}>
                      <option value="">— Seçin —</option>
                      {profiles.map(p => (
                        <option key={p.id} value={p.id}>
                          {p.name} — {p.baseUrl}
                          {p.authSummary ? ` · ${p.authSummary}` : ''}
                        </option>
                      ))}
                    </select>
                    {selectedId && (() => {
                      const p = profiles.find(x => x.id === selectedId)
                      if (!p) return null
                      return (
                        <div style={{
                          marginTop: 6, padding: '6px 10px', fontSize: 11,
                          background: 'var(--iw-emerald-bg)', color: 'var(--iw-emerald-color)',
                          borderRadius: 6, lineHeight: 1.5,
                        }}>
                          ✓ <strong>{p.name}</strong>: <code>{p.baseUrl}</code>
                          {p.authSummary && <> · <strong>{p.authSummary}</strong></>}
                          {p.tokenEndpoint && <> · token: <code>{p.tokenEndpoint}</code></>}
                          <br /><span style={{ opacity: 0.85 }}>
                            Bu profile'ın kimlik bilgileri (OAuth2 + Netsis ek alanları) korunur,
                            yalnızca endpoint listesi eklenir.
                          </span>
                        </div>
                      )
                    })()}
                  </>
                )}

                {mode === 'new' && (
                  <div style={{ display: 'grid', gridTemplateColumns: '1fr 1.4fr', gap: 10 }}>
                    <input value={newName} onChange={e => setNewName(e.target.value)}
                           placeholder="Profile adı" disabled={importing} />
                    <input value={newBaseUrl} onChange={e => setNewBaseUrl(e.target.value)}
                           placeholder="https://server:port (base URL)" disabled={importing} />
                  </div>
                )}
              </div>

              {/* CSV input */}
              <div className="iw-field" style={{ maxWidth: 'none' }}>
                <label style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
                  <span>CSV İçeriği</span>
                  <button className="iw-btn-ghost" onClick={() => fileRef.current?.click()}
                          disabled={importing} style={{ padding: '4px 10px' }}>
                    <FileUp size={13} style={{ verticalAlign: 'middle', marginRight: 5 }} />
                    Dosyadan yükle
                  </button>
                  <input ref={fileRef} type="file" accept=".csv,text/csv" style={{ display: 'none' }}
                         onChange={handleFile} />
                </label>
                <textarea
                  value={csvText} onChange={e => setCsvText(e.target.value)}
                  disabled={importing}
                  className="eem-schema-editor"
                  style={{ minHeight: 220, maxHeight: 360 }}
                  placeholder={`"Resource","Method","HttpMethod","UrlTemplate","InputType","ReturnType"\n"ARPs","PostInternal","POST","/api/v2/ARPs","ARPs","TResult\`1"\n"Items","GetInternal","GET","/api/v2/Items","SelectFilter","TSelectData\`1"\n…`}
                  spellCheck={false} />
                <span className="iw-field-hint">
                  Format: <code>Resource,Method,HttpMethod,UrlTemplate,InputType,ReturnType</code> —
                  header satırı opsiyonel. Aynı (Method + URL) varsa atlanır.
                </span>
              </div>
            </>
          )}

          {/* Sonuç paneli */}
          {result && (
            <div style={{ padding: '8px 0' }}>
              <div style={{ fontSize: 14, fontWeight: 600, marginBottom: 12, color: 'var(--iw-emerald-color)' }}>
                ✓ Import tamamlandı
              </div>
              <div style={{
                display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: 10,
                marginBottom: 12,
              }}>
                {[
                  { label: 'Toplam',   value: result.total,   color: 'var(--iw-text)'           },
                  { label: 'Eklendi',  value: result.created, color: 'var(--iw-emerald-color)' },
                  { label: 'Atlandı',  value: result.skipped, color: 'var(--iw-muted)'         },
                  { label: 'Hatalı',   value: result.errors,  color: result.errors > 0 ? 'var(--iw-rose-color)' : 'var(--iw-muted)' },
                ].map(s => (
                  <div key={s.label} style={{
                    background: 'var(--iw-bg)', border: '1px solid var(--iw-border)',
                    borderRadius: 8, padding: '10px 12px', textAlign: 'center',
                  }}>
                    <div style={{ fontSize: 22, fontWeight: 700, color: s.color }}>{s.value}</div>
                    <div style={{ fontSize: 11, color: 'var(--iw-muted)', marginTop: 2 }}>{s.label}</div>
                  </div>
                ))}
              </div>
              {result.errorMessages?.length > 0 && (
                <details style={{ fontSize: 12 }}>
                  <summary style={{ cursor: 'pointer', color: 'var(--iw-rose-color)' }}>Hata örnekleri ({result.errorMessages.length})</summary>
                  <pre style={{
                    marginTop: 6, padding: 10, background: 'var(--iw-rose-bg)',
                    border: '1px solid var(--iw-rose-color)', borderRadius: 6,
                    fontSize: 11, whiteSpace: 'pre-wrap',
                  }}>{result.errorMessages.join('\n')}</pre>
                </details>
              )}
            </div>
          )}
        </div>

        <div className="eem-footer">
          {!result ? (
            <>
              <button className="iw-btn-secondary" onClick={onClose} disabled={importing}>Vazgeç</button>
              <button className="iw-btn-primary" onClick={handleImport} disabled={importing}>
                {importing ? <><Loader2 className="iw-spin" size={14} /> İçeri alınıyor…</> : <><Upload size={14} /> İçeri Al</>}
              </button>
            </>
          ) : (
            <button className="iw-btn-primary" onClick={handleDone}>Tamam</button>
          )}
        </div>
      </div>
    </div>
  )
}

function DeleteModal({ name, onCancel, onConfirm, loading }) {
  useEffect(() => {
    const h = e => { if (e.key === 'Escape') onCancel() }
    window.addEventListener('keydown', h)
    return () => window.removeEventListener('keydown', h)
  }, [onCancel])
  return (
    <div className="iw-modal-bd" onClick={onCancel}>
      <div className="iw-modal" onClick={e => e.stopPropagation()}>
        <div className="iw-modal-icon"><AlertTriangle size={32} /></div>
        <div className="iw-modal-title">Endpoint'i Sil</div>
        <div className="iw-modal-msg">
          <strong>{name}</strong> silinecek. Bu endpoint'i kullanan entegrasyonlar varsa
          işlem reddedilir; önce o entegrasyonları kaldırın veya başka endpoint'e taşıyın.
        </div>
        <div className="iw-modal-actions">
          <button className="iw-modal-cancel" onClick={onCancel}>Vazgeç</button>
          <button className="iw-modal-del" onClick={onConfirm} disabled={loading}>
            {loading ? 'Siliniyor…' : 'Sil'}
          </button>
        </div>
      </div>
    </div>
  )
}

export default function IntegrationEndpointsList({ config }) {
  const [profiles, setProfiles]     = useState([])
  const [endpoints, setEndpoints]   = useState([])
  const [search, setSearch]         = useState('')
  const [filterProfile, setFilter]  = useState('all')
  const [loading, setLoading]       = useState(true)
  const [editing, setEditing]       = useState(null)   // null | "new" | endpoint obj
  const [deleteTarget, setDeleteTarget] = useState(null)
  const [deleting, setDeleting]     = useState(false)
  const [showImport, setShowImport] = useState(false)

  const refresh = useCallback(async () => {
    setLoading(true)
    try {
      const [pr, ep] = await Promise.all([
        fetch('/Integrations/api/profiles', { credentials: 'same-origin' }).then(r => r.json()),
        fetch('/Integrations/api/endpoints?includeInactive=true', { credentials: 'same-origin' }).then(r => r.json()),
      ])
      if (pr.success) setProfiles(pr.profiles || [])
      if (ep.success) setEndpoints(ep.endpoints || [])
    } catch (e) {
      toast('Veri yüklenemedi: ' + e.message, 'err')
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => { refresh() }, [refresh])

  const filtered = useMemo(() => {
    let list = endpoints
    if (filterProfile !== 'all') list = list.filter(e => e.apiProfileId === filterProfile)
    if (search) {
      const q = search.toLowerCase()
      list = list.filter(e =>
        (e.name || '').toLowerCase().includes(q) ||
        (e.urlTemplate || '').toLowerCase().includes(q) ||
        (e.apiProfileName || '').toLowerCase().includes(q)
      )
    }
    return list
  }, [endpoints, filterProfile, search])

  const handleToggle = async (ep) => {
    try {
      const r = await fetch(`/Integrations/api/endpoints/toggle/${ep.id}`, {
        method: 'POST', credentials: 'same-origin',
        headers: { RequestVerificationToken: getCsrf() },
      })
      const d = await r.json()
      if (d.success) { toast(d.isActive ? 'Aktif edildi' : 'Pasif edildi', 'ok'); refresh() }
      else toast(d.error || 'Hata', 'err')
    } catch (e) { toast('Sunucu hatası: ' + e.message, 'err') }
  }

  const handleDelete = async () => {
    if (!deleteTarget) return
    setDeleting(true)
    try {
      const r = await fetch(`/Integrations/api/endpoints/delete/${deleteTarget.id}`, {
        method: 'POST', credentials: 'same-origin',
        headers: { RequestVerificationToken: getCsrf() },
      })
      const d = await r.json()
      if (d.success) { toast('Silindi', 'ok'); setDeleteTarget(null); refresh() }
      else { toast(d.error || 'Silinemedi', 'err'); setDeleteTarget(null) }
    } catch (e) {
      toast('Sunucu hatası: ' + e.message, 'err')
      setDeleteTarget(null)
    } finally {
      setDeleting(false)
    }
  }

  return (
    <div className="il-root">
      <div className="il-toolbar">
        <div className="il-title">
          <Globe size={16} />
          <span>Endpointler</span>
          <span className="il-count">{filtered.length} / {endpoints.length}</span>
        </div>
        <div className="il-spacer" />
        <select value={filterProfile} onChange={e => setFilter(e.target.value)}
                style={{ padding: '6px 10px', borderRadius: 8, border: '1px solid var(--iw-border)',
                         background: 'var(--iw-bg)', color: 'var(--iw-text)', fontSize: 13 }}>
          <option value="all">Tüm Profile'lar</option>
          {profiles.map(p => <option key={p.id} value={p.id}>{p.name}</option>)}
        </select>
        <div className="il-search-wrap">
          <Search size={13} className="il-search-icon" />
          <input className="il-search" placeholder="Ad, URL veya profile ara…"
                 value={search} onChange={e => setSearch(e.target.value)} />
        </div>
        <button className="iw-btn-secondary" onClick={refresh}><RefreshCw size={13} /></button>
        <button className="iw-btn-secondary" onClick={() => setShowImport(true)} title="CSV'den toplu endpoint içeri al">
          <Upload size={13} /> Toplu Import
        </button>
        <button className="il-btn-primary" onClick={() => setEditing('new')}>
          <Plus size={14} /> Yeni Endpoint
        </button>
      </div>

      <div className="il-list">
        {loading && (
          <div className="il-empty">
            <Loader2 className="iw-spin" size={32} />
            <span>Yükleniyor…</span>
          </div>
        )}

        {!loading && filtered.length === 0 && (
          <div className="il-empty">
            <Globe size={48} style={{ opacity: 0.3 }} />
            <span>{endpoints.length === 0 ? 'Henüz endpoint yok.' : 'Aramaya uyan kayıt yok.'}</span>
            {endpoints.length === 0 && (
              <button className="il-btn-primary" onClick={() => setEditing('new')}>
                <Plus size={14} /> İlk endpoint'i oluştur
              </button>
            )}
          </div>
        )}

        {!loading && filtered.map(ep => (
          <div key={ep.id} className={'il-card' + (ep.isActive ? '' : ' is-inactive')}>
            <div className="il-actions il-actions--leading">
              <button className="il-act il-act-edit" title="Düzenle" onClick={() => setEditing(ep)}>
                <Edit2 size={14} />
              </button>
              <button className="il-act" title={ep.isActive ? 'Pasif yap' : 'Aktif yap'}
                      onClick={() => handleToggle(ep)}>
                <Power size={14} />
              </button>
              <button className="il-act il-act-del" title="Sil" onClick={() => setDeleteTarget(ep)}>
                <Trash2 size={14} />
              </button>
            </div>
            <div className="il-card-main">
              <div className="il-card-name">
                <span style={{
                  display: 'inline-block', padding: '1px 6px', borderRadius: 4,
                  background: 'var(--iw-indigo-bg)', color: 'var(--iw-indigo-color)',
                  fontWeight: 700, fontSize: 10, marginRight: 8, verticalAlign: '2px',
                }}>{ep.httpMethod}</span>
                {ep.name}
              </div>
              <div className="il-card-desc" style={{ fontFamily: 'monospace', fontSize: 11 }}>
                {ep.urlTemplate}
              </div>
            </div>
            <div className="il-card-flow">
              <span>API Profile</span>
              <span>{ep.apiProfileName}</span>
            </div>
            <div className="il-chips">
              <span className={'il-chip ' + (ep.isActive ? 'il-chip-active' : 'il-chip-inactive')}>
                {ep.isActive ? <Check size={11} /> : <X size={11} />}
                {ep.isActive ? 'Aktif' : 'Pasif'}
              </span>
            </div>
          </div>
        ))}
      </div>

      {editing && (
        <EndpointEditModal
          profileId={editing === 'new' ? (filterProfile === 'all' ? '' : filterProfile) : editing.apiProfileId}
          profiles={profiles}
          endpoint={editing === 'new' ? null : editing}
          onClose={() => setEditing(null)}
          onSaved={() => { setEditing(null); refresh() }}
        />
      )}

      {deleteTarget && (
        <DeleteModal
          name={deleteTarget.name}
          onCancel={() => setDeleteTarget(null)}
          onConfirm={handleDelete}
          loading={deleting}
        />
      )}

      {showImport && (
        <BulkImportModal
          profiles={profiles}
          onClose={() => setShowImport(false)}
          onImported={refresh}
        />
      )}
    </div>
  )
}
