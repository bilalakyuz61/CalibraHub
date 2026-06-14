/**
 * Step 2 (yeni numara: 1) — Hedef REST Endpoint Seç
 *
 * Layout:
 *   ┌── üst satır: API Profile (1fr) + Endpoint dropdown (1fr) + [+] [✎] butonlar
 *   ├── auth özet (varsa)
 *   └── Body Schema (full width, collapsible JSON tree)
 *
 * "Sadece Prosedür Modu" switch'i ContextBar'a taşındı; burada sadece info banner.
 */
import React, { useState, useEffect, useCallback, useRef } from 'react'
import { Loader2, Plus, Edit2, ChevronRight, ChevronDown, Info } from 'lucide-react'
import EndpointEditModal from './EndpointEditModal'

export default function WizardStep2Endpoint({ apiBase, state, update }) {
  const [profiles, setProfiles]             = useState([])
  const [endpoints, setEndpoints]           = useState([])
  const [selectedProfile, setSelectedProfile] = useState('')
  const [endpointDetail, setEndpointDetail] = useState(null)
  const [fieldDocs, setFieldDocs]           = useState({})   // { "FatUst.CariKod": { description, allowedValues, example, notes } }
  const [loading, setLoading]               = useState(true)
  const [editingEndpoint, setEditingEndpoint] = useState(null)
  const [refreshKey, setRefreshKey]         = useState(0)

  const isProcedureOnly = state.procedureOnlyMode === true

  // Profiles
  useEffect(() => {
    (async () => {
      try {
        const r = await fetch(`${apiBase}/profiles`, { credentials: 'same-origin' })
        const d = await r.json()
        if (d.success) setProfiles(d.profiles || [])
      } catch { /* */ } finally { setLoading(false) }
    })()
  }, [apiBase])

  // Profile → endpoints
  useEffect(() => {
    if (!selectedProfile) { setEndpoints([]); return }
    fetch(`${apiBase}/endpoints?profileId=${selectedProfile}`, { credentials: 'same-origin' })
      .then(r => r.json())
      .then(d => { if (d.success) setEndpoints(d.endpoints || []) })
  }, [apiBase, selectedProfile, refreshKey])

  // Endpoint detail + field docs (paralel)
  useEffect(() => {
    if (!state.targetEndpointId) { setEndpointDetail(null); setFieldDocs({}); return }
    fetch(`${apiBase}/endpoints/${state.targetEndpointId}`, { credentials: 'same-origin' })
      .then(r => r.json())
      .then(d => { if (d.success) setEndpointDetail(d.endpoint) })
    fetch(`${apiBase}/field-docs?endpointId=${state.targetEndpointId}`, { credentials: 'same-origin' })
      .then(r => r.json())
      .then(d => { setFieldDocs(d?.success ? (d.docs || {}) : {}) })
      .catch(() => setFieldDocs({}))
  }, [apiBase, state.targetEndpointId])

  // Edit modu — endpoint zaten seçili, profile'ı geri keşfet
  useEffect(() => {
    if (state.targetEndpointId && !selectedProfile && endpointDetail) {
      setSelectedProfile(endpointDetail.apiProfileId)
    }
  }, [state.targetEndpointId, selectedProfile, endpointDetail])

  const handleEndpointSaved = useCallback((newId) => {
    setEditingEndpoint(null)
    setRefreshKey(k => k + 1)
    if (newId && newId !== state.targetEndpointId) update({ targetEndpointId: newId })
  }, [state.targetEndpointId, update])

  // ────────────────────────────────────────────────────────────────────
  // Sadece Prosedür modu — endpoint UI'ı tamamen gizle
  // ────────────────────────────────────────────────────────────────────
  if (isProcedureOnly) {
    return (
      <div style={{
        margin: '20px auto', maxWidth: 720, padding: 28, textAlign: 'center',
        border: '1px dashed var(--iw-border)', borderRadius: 10,
        background: 'var(--iw-surface)', color: 'var(--iw-muted)', fontSize: 13, lineHeight: 1.7,
      }}>
        ⚙ <strong style={{ color: 'var(--iw-emerald-color)' }}>Sadece Prosedür</strong> modu aktif —
        endpoint seçimi atlandı.<br />
        <strong>İleri</strong> ile devam edin (Yayına Al adımında prosedürü tanımlayın).
      </div>
    )
  }

  // ────────────────────────────────────────────────────────────────────
  // HTTP modu — kompakt yatay düzen
  // ────────────────────────────────────────────────────────────────────
  const selectedP = profiles.find(p => p.id === selectedProfile)
  const isOAuth   = (selectedP?.authType || '').toLowerCase() === 'oauth2password'

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 12, padding: '12px 0' }}>

      {/* Üst satır: API Profile + Endpoint inline label (etiket + dropdown ayni satirda) + [+] [✎] */}
      <div style={{
        display: 'grid',
        gridTemplateColumns: '1fr 1fr 36px 36px',
        gap: 10, alignItems: 'center',
      }}>
        {/* API Profile — label sol, dropdown sag (inline) */}
        <div style={{ display: 'flex', alignItems: 'center', gap: 8, minWidth: 0 }}>
          <label style={{
            fontSize: 12, fontWeight: 600, color: 'var(--iw-text)',
            whiteSpace: 'nowrap', flexShrink: 0,
          }}>API Profile</label>
          {loading
            ? <Loader2 className="iw-spin" size={16} />
            : (
              <select value={selectedProfile}
                      onChange={e => { setSelectedProfile(e.target.value); update({ targetEndpointId: 0 }) }}
                      style={{
                        flex: 1, minWidth: 0, padding: '6px 8px', fontSize: 12,
                        border: '1px solid var(--iw-border)', borderRadius: 6,
                        background: 'var(--iw-bg)', color: 'var(--iw-text)', outline: 'none',
                      }}>
                <option value="">— Auth profile seç —</option>
                {profiles.map(p => (
                  <option key={p.id} value={p.id}>
                    {p.name} — {p.baseUrl}{p.authSummary ? ` · ${p.authSummary}` : ''}
                  </option>
                ))}
              </select>
            )}
        </div>

        {/* Endpoint — label sol, dropdown sag (inline) */}
        <div style={{ display: 'flex', alignItems: 'center', gap: 8, minWidth: 0 }}>
          <label style={{
            fontSize: 12, fontWeight: 600, color: 'var(--iw-text)',
            whiteSpace: 'nowrap', flexShrink: 0,
          }}>Endpoint</label>
          <select value={state.targetEndpointId || ''}
                  onChange={e => update({ targetEndpointId: parseInt(e.target.value) || 0 })}
                  disabled={!selectedProfile}
                  style={{
                    flex: 1, minWidth: 0, padding: '6px 8px', fontSize: 12,
                    border: '1px solid var(--iw-border)', borderRadius: 6,
                    background: 'var(--iw-bg)', color: 'var(--iw-text)', outline: 'none',
                    opacity: selectedProfile ? 1 : 0.5,
                  }}>
            <option value="">— Endpoint seç —</option>
            {endpoints.map(ep => (
              <option key={ep.id} value={ep.id}>
                [{ep.httpMethod}] {ep.name} ({ep.urlTemplate})
              </option>
            ))}
          </select>
        </div>

        <button className="iw-btn-secondary"
                onClick={() => setEditingEndpoint('new')}
                disabled={!selectedProfile}
                title="Bu profile altına yeni endpoint ekle"
                style={{ height: 32, padding: 0, justifyContent: 'center' }}>
          <Plus size={14} />
        </button>
        <button className="iw-btn-secondary"
                onClick={() => setEditingEndpoint(endpointDetail || endpoints.find(e => e.id === state.targetEndpointId))}
                disabled={!state.targetEndpointId}
                title="Seçili endpoint'i düzenle"
                style={{ height: 32, padding: 0, justifyContent: 'center' }}>
          <Edit2 size={14} />
        </button>
      </div>

      {/* Auth özet (kompakt — sadece profile seçiliyse) */}
      {selectedP && (
        <div style={{
          padding: '6px 10px', fontSize: 11,
          background: isOAuth ? 'var(--iw-emerald-bg)' : 'var(--iw-slate-bg)',
          color: isOAuth ? 'var(--iw-emerald-color)' : 'var(--iw-muted)',
          borderRadius: 6, lineHeight: 1.5,
        }}>
          {isOAuth ? '🔐' : 'ℹ'} {selectedP.authSummary || 'Auth yok'}
          {selectedP.tokenEndpoint && <> · <code>{selectedP.tokenEndpoint}</code></>}
          {!isOAuth && (selectedP.authType || '').toLowerCase() === 'none' && (
            <> · <span style={{ color: 'var(--iw-amber-color)' }}>⚠ Auth tanımsız — 401 alabilirsiniz.</span></>
          )}
        </div>
      )}

      {/* Body Schema — full width, collapsible JSON tree */}
      {endpointDetail && (
        <div style={{
          flex: 1, display: 'flex', flexDirection: 'column',
          border: '1px solid var(--iw-border)', borderRadius: 8,
          background: 'var(--iw-surface)', overflow: 'hidden',
        }}>
          <div style={{
            padding: '8px 12px', borderBottom: '1px solid var(--iw-border)',
            background: 'var(--iw-bg)',
            display: 'flex', alignItems: 'center', gap: 10, fontSize: 12,
          }}>
            <span style={{
              padding: '2px 8px', borderRadius: 4,
              background: 'var(--iw-indigo-bg)', color: 'var(--iw-indigo-color)',
              fontWeight: 700, fontSize: 10,
            }}>{endpointDetail.httpMethod}</span>
            <code style={{ fontSize: 12, color: 'var(--iw-text)', fontFamily: 'ui-monospace, Menlo, Consolas, monospace' }}>
              {endpointDetail.urlTemplate}
            </code>
            <span style={{ flex: 1 }} />
            {Object.keys(fieldDocs || {}).length > 0 && (
              <span style={{
                display: 'inline-flex', alignItems: 'center', gap: 6,
                padding: '2px 8px', borderRadius: 12,
                background: 'var(--iw-indigo-bg)', color: 'var(--iw-indigo-color)',
                fontSize: 11, fontWeight: 600,
              }} title="Açıklamalı alanlar — ⓘ ikonuna tıkla">
                <Info size={11} /> {Object.keys(fieldDocs).length} alan açıklamalı
              </span>
            )}
            <span style={{ color: 'var(--iw-muted)', fontSize: 11 }}>
              Body Schema · {endpointDetail.name}
            </span>
          </div>
          <div style={{
            padding: '12px 14px', overflow: 'auto', maxHeight: 480,
            fontFamily: 'ui-monospace, Menlo, Consolas, monospace', fontSize: 12,
            color: 'var(--iw-text)',
          }}>
            <JsonTree json={endpointDetail.bodySchema} fieldDocs={fieldDocs} />
          </div>
        </div>
      )}

      {!endpointDetail && (
        <div style={{
          padding: 24, textAlign: 'center', color: 'var(--iw-muted)', fontSize: 12,
          border: '1px dashed var(--iw-border)', borderRadius: 8, background: 'var(--iw-surface)',
        }}>
          {!selectedProfile
            ? 'Önce API Profile seçin.'
            : !state.targetEndpointId
              ? 'Endpoint seçin — Body Schema burada gösterilecek.'
              : 'Schema yükleniyor…'}
        </div>
      )}

      {editingEndpoint && (
        <EndpointEditModal
          profileId={selectedProfile}
          profiles={profiles}
          endpoint={editingEndpoint === 'new' ? null : editingEndpoint}
          onClose={() => setEditingEndpoint(null)}
          onSaved={handleEndpointSaved}
        />
      )}
    </div>
  )
}

// ────────────────────────────────────────────────────────────────────────────
// JsonTree — collapsible JSON viewer. Object/array → expand/collapse, primitive → tek satır.
// Default: tüm seviyeler açık. Tıklanınca toggle.
// `fieldDocs` (opsiyonel) — path → { description, allowedValues, example, notes }
// eşlemesi. Eşleşen leaf'lerin yanına (i) ikonu render edilir, hover/click ile
// tooltip açar.
// ────────────────────────────────────────────────────────────────────────────
function JsonTree({ json, fieldDocs }) {
  let parsed
  try { parsed = typeof json === 'string' ? JSON.parse(json) : json }
  catch { return <pre style={{ margin: 0, color: 'var(--iw-muted)' }}>{String(json) || '(şema tanımlı değil)'}</pre> }
  if (parsed === null || parsed === undefined) {
    return <span style={{ color: 'var(--iw-muted)' }}>(şema tanımlı değil)</span>
  }
  return <JsonNode value={parsed} depth={0} path="" fieldDocs={fieldDocs || {}} />
}

function JsonNode({ keyName, value, depth, isLast = true, parentIsArray = false, path = '', fieldDocs = {} }) {
  const [open, setOpen] = useState(true)   // default açık

  // Primitive (string/number/bool/null) → tek satır
  if (value === null || typeof value !== 'object') {
    return (
      <div style={{
        paddingLeft: depth * 14, lineHeight: 1.6,
        display: 'flex', alignItems: 'center', gap: 4, flexWrap: 'wrap',
      }}>
        {keyName !== undefined && (
          <>
            <span style={{ color: parentIsArray ? 'var(--iw-muted)' : 'var(--iw-emerald-color)' }}>
              {parentIsArray ? `[${keyName}]` : `"${keyName}"`}
            </span>
            <span style={{ color: 'var(--iw-muted)' }}>:</span>
          </>
        )}
        <PrimitiveValue v={value} />
        {!isLast && <span style={{ color: 'var(--iw-muted)' }}>,</span>}
      </div>
    )
  }

  // Array veya Object
  const isArray = Array.isArray(value)
  const entries = isArray
    ? value.map((v, i) => [i, v])
    : Object.entries(value)
  const childCount = entries.length
  const openBracket  = isArray ? '[' : '{'
  const closeBracket = isArray ? ']' : '}'

  return (
    <div style={{ paddingLeft: depth * 14, lineHeight: 1.6 }}>
      <div style={{ display: 'inline-flex', alignItems: 'center', cursor: 'pointer', userSelect: 'none' }}
           onClick={() => setOpen(o => !o)}>
        <span style={{ color: 'var(--iw-muted)', marginRight: 2, display: 'inline-flex' }}>
          {open ? <ChevronDown size={11} /> : <ChevronRight size={11} />}
        </span>
        {keyName !== undefined && (
          <>
            <span style={{ color: parentIsArray ? 'var(--iw-muted)' : 'var(--iw-emerald-color)' }}>
              {parentIsArray ? `[${keyName}]` : `"${keyName}"`}
            </span>
            <span style={{ color: 'var(--iw-muted)' }}>: </span>
          </>
        )}
        <span style={{ color: 'var(--iw-amber-color)' }}>{openBracket}</span>
        {!open && (
          <span style={{ color: 'var(--iw-muted)', fontStyle: 'italic', marginLeft: 4 }}>
            {childCount === 0 ? '' : `${childCount} ${isArray ? 'element' : 'alan'}`}
          </span>
        )}
        {!open && <span style={{ color: 'var(--iw-amber-color)' }}>{closeBracket}</span>}
        {!open && !isLast && <span style={{ color: 'var(--iw-muted)' }}>,</span>}
      </div>
      {open && (
        <>
          {entries.map(([k, v], i) => {
            const childPath = buildChildPath(path, k, isArray)
            return (
              <JsonNode key={k} keyName={k} value={v}
                        depth={depth + 1}
                        isLast={i === entries.length - 1}
                        parentIsArray={isArray}
                        path={childPath}
                        fieldDocs={fieldDocs} />
            )
          })}
          <div style={{ paddingLeft: depth * 14 }}>
            <span style={{ color: 'var(--iw-amber-color)' }}>{closeBracket}</span>
            {!isLast && <span style={{ color: 'var(--iw-muted)' }}>,</span>}
          </div>
        </>
      )}
    </div>
  )
}

// Catalog path notation: "FatUst.CariKod" / "Kalems[].StokKodu"
//   Object child  → parent + "." + key
//   Array child   → parent + "[]"          (her eleman tek path'i paylaşır)
//   Root          → key
function buildChildPath(parentPath, key, parentIsArray) {
  if (parentIsArray) return parentPath + '[]'
  if (!parentPath) return String(key)
  return parentPath + '.' + key
}

function PrimitiveValue({ v }) {
  if (v === null) return <span style={{ color: 'var(--iw-muted)' }}>null</span>
  if (typeof v === 'string') return <span style={{ color: 'var(--iw-emerald-color)' }}>"{v}"</span>
  if (typeof v === 'number') return <span style={{ color: 'var(--iw-indigo-color)' }}>{v}</span>
  if (typeof v === 'boolean') return <span style={{ color: 'var(--iw-rose-color)' }}>{String(v)}</span>
  return <span>{String(v)}</span>
}

