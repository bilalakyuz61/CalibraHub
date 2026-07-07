import React, { useReducer, useEffect, useState, useCallback, useRef } from 'react'
import './docDesigner.css'
import { reducer, initialState, buildLayoutJson } from './designerReducer'
import DesignerCanvas from './Canvas/DesignerCanvas'
import LeftPanel from './Toolbox/LeftPanel'
import PropertiesPanel from './Properties/PropertiesPanel'
import DesignerTopBar from './TopBar/DesignerTopBar'
import ElementEditorModal from './Editor/ElementEditorModal'
import { getLayout, saveLayout, renderPdf } from './services/docDesignerService'
// previewLayout artik kullanilmiyor — onizleme workspace tab'inda direkt
// /DocDesigner/Preview/{id} GET endpoint'i ile yuklenir.

export default function DocDesignerApp({ layoutId }) {
  const [state, dispatch] = useReducer(reducer, initialState)
  const [zoom, setZoom] = useState(1.0)
  const stateRef = useRef(state)

  useEffect(() => { stateRef.current = state }, [state])

  const changeZoom = useCallback(delta => {
    if (delta === 0) { setZoom(1.0); return }
    setZoom(z => Math.min(2.0, Math.max(0.4, Math.round((z + delta) * 10) / 10)))
  }, [])

  // ── Yükle ────────────────────────────────────────────────────────────────
  useEffect(() => {
    if (layoutId && layoutId > 0) {
      getLayout(layoutId)
        .then(layout => dispatch({ type: 'LOAD', layout }))
        .catch(ex => dispatch({ type: 'SET_ERROR', message: ex.message }))
    }
  }, [layoutId])

  // ── Kaydet ───────────────────────────────────────────────────────────────
  const handleSave = useCallback(async () => {
    const { meta, bands, dataSources } = stateRef.current
    if (!meta.name?.trim()) {
      dispatch({ type: 'SET_ERROR', message: 'Şablon adı boş olamaz.' })
      return
    }
    // Kod otomatik türetilir (Kullanıcı kod girmez kuralı)
    const derivedCode = meta.code?.trim()
      || meta.name.trim().toUpperCase().replace(/[^A-Z0-9]+/g, '_').slice(0, 40)
      || `LAYOUT_${Date.now()}`
    dispatch({ type: 'SET_SAVING', value: true })
    try {
      const req = {
        id: meta.id, code: derivedCode, name: meta.name.trim(), docType: meta.docType,
        documentTypeId: meta.documentTypeId ?? null,
        description: null, layoutJson: buildLayoutJson(meta, bands),
        pageW: meta.pageW, pageH: meta.pageH,
        marginTop: meta.marginTop, marginBot: meta.marginBot,
        marginLeft: meta.marginLeft, marginRight: meta.marginRight,
        isDefault: meta.isDefault ?? false,
        // 2026-05-20: UI'da Cikti Turu ve mail-spesifik alanlar (view/konu/govde/where)
        // kaldirildi. outputFormat her zaman 'pdf' gonderilir; legacy alanlar null
        // gonderilir — backward-compat icin backend hala bu alanlari kabul ediyor.
        outputFormat: 'pdf',
        defaultSubject: null,
        defaultBody:    null,
        defaultsViewName:      null,
        defaultsSubjectColumn: null,
        defaultsBodyColumn:    null,
        defaultsWhere:         null,
        // Yeni: bu dizayn mail compose ekraninda da listelensin mi?
        useAsMailTemplate: meta.useAsMailTemplate ?? false,
        dataSources: dataSources.map((ds, i) => ({ ...ds, id: ds.id ?? 0, layoutId: meta.id, ordinal: i })),
      }
      const id = await saveLayout(req)
      dispatch({ type: 'MARK_SAVED', id })
    } catch (ex) {
      dispatch({ type: 'SET_ERROR', message: ex.message })
    }
  }, [])

  // ── Önizle ───────────────────────────────────────────────────────────────
  // 2026-05-30: Onizleme modal yerine workspace tab'inda acilir — solda yeni
  // sekme cikar. Backend: GET /DocDesigner/Preview/{id} HTML icerigi dondurur,
  // tab iframe ile yukler; kullanici Ctrl+P ile native print baslatabilir.
  const handlePreview = useCallback(() => {
    const { meta } = stateRef.current
    if (!meta.id) { dispatch({ type: 'SET_ERROR', message: 'Önce kaydedin.' }); return }
    const url   = `/DocDesigner/Preview/${meta.id}`
    const title = (meta.name || 'Belge') + ' — Önizleme'
    try {
      if (window.top && window.top.CalibraHub && typeof window.top.CalibraHub.openWorkspaceTab === 'function') {
        window.top.CalibraHub.openWorkspaceTab({
          url,
          title,
          matchPath: `/DocDesigner/Preview/${meta.id}`,
        })
        return
      }
    } catch (e) { /* cross-origin fallback */ }
    // Fallback (iframe disinda) — ayni sekmede ac
    window.location.href = url
  }, [])

  // ── PDF ──────────────────────────────────────────────────────────────────
  const handlePdf = useCallback(async () => {
    const { meta } = stateRef.current
    if (!meta.id) { dispatch({ type: 'SET_ERROR', message: 'Önce kaydedin.' }); return }
    try {
      const blob = await renderPdf({ layoutId: meta.id, documentId: null, paramOverrides: null })
      const url  = URL.createObjectURL(blob)
      const a    = document.createElement('a')
      a.href = url; a.download = `${meta.code || 'belge'}.pdf`; a.click()
      URL.revokeObjectURL(url)
    } catch (ex) {
      dispatch({ type: 'SET_ERROR', message: ex.message })
    }
  }, [])

  // ── Klavye kısayolları ───────────────────────────────────────────────────
  useEffect(() => {
    const onKeyDown = e => {
      const tag = document.activeElement?.tagName
      if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') return

      const { selectedElementId, selectedElementIds } = stateRef.current
      const activeIds = selectedElementIds?.length > 0
        ? selectedElementIds
        : selectedElementId ? [selectedElementId] : []

      if (e.key === 'Delete' || e.key === 'Backspace') {
        if (activeIds.length) {
          dispatch({ type: 'DELETE_ELEMENT', elementIds: activeIds })
          e.preventDefault()
        }
        return
      }
      if (e.key === 'Escape') { dispatch({ type: 'DESELECT' }); return }

      if ((e.ctrlKey || e.metaKey) && e.key === 's') {
        e.preventDefault(); handleSave(); return
      }
      if ((e.ctrlKey || e.metaKey) && e.key === 'c') {
        dispatch({ type: 'COPY_ELEMENTS' }); return
      }
      if ((e.ctrlKey || e.metaKey) && e.key === 'v') {
        dispatch({ type: 'PASTE_ELEMENTS' }); return
      }
      // Undo/Redo — Ctrl+Z / Ctrl+Y / Ctrl+Shift+Z
      if ((e.ctrlKey || e.metaKey) && !e.shiftKey && e.key === 'z') {
        e.preventDefault(); dispatch({ type: 'UNDO' }); return
      }
      if ((e.ctrlKey || e.metaKey) && (e.key === 'y' || (e.shiftKey && e.key === 'Z'))) {
        e.preventDefault(); dispatch({ type: 'REDO' }); return
      }

      if (['ArrowUp','ArrowDown','ArrowLeft','ArrowRight'].includes(e.key) && activeIds.length) {
        e.preventDefault()
        const step = e.shiftKey ? 5 : 1
        dispatch({
          type: 'NUDGE_ELEMENT',
          dx: e.key === 'ArrowLeft' ? -step : e.key === 'ArrowRight' ? step : 0,
          dy: e.key === 'ArrowUp'   ? -step : e.key === 'ArrowDown'  ? step : 0,
        })
      }
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [handleSave])

  return (
    <div className="doc-designer-root" style={{ display: 'flex', flexDirection: 'column', height: '100%', overflow: 'hidden', background: 'var(--dd-bg, #eef0f5)' }}>
      <DesignerTopBar
        state={state}
        dispatch={dispatch}
        zoom={zoom}
        onZoomChange={changeZoom}
        onSave={handleSave}
        onPreview={handlePreview}
        onPdf={handlePdf}
      />

      {state.error && (
        <div style={{
          background: '#fef2f2', borderBottom: '1px solid #fca5a5',
          padding: '6px 16px', fontSize: 12, color: '#dc2626',
          display: 'flex', alignItems: 'center', gap: 8, flexShrink: 0,
        }}>
          <span>Hata: {state.error}</span>
          <button onClick={() => dispatch({ type: 'CLEAR_ERROR' })}
            style={{ background: 'none', border: 'none', cursor: 'pointer', color: '#dc2626', fontWeight: 700, fontSize: 16 }}>×</button>
        </div>
      )}

      <div style={{ flex: 1, display: 'flex', overflow: 'hidden' }}>
        <LeftPanel state={state} dispatch={dispatch} />
        <DesignerCanvas state={state} dispatch={dispatch} zoom={zoom} onZoomChange={changeZoom} />
        <PropertiesPanel state={state} dispatch={dispatch} />
      </div>

      {/* PreviewModal kaldirildi — onizleme workspace tab'inda yeni sekme olarak acilir. */}

      {state.editingElementId && (() => {
        const el = state.bands.flatMap(b => b.elements).find(e => e.id === state.editingElementId)
        if (!el) return null
        return (
          <ElementEditorModal
            el={el}
            dataSources={state.dataSources}
            onSave={patch => dispatch({ type: 'UPDATE_ELEMENT', elementId: el.id, patch })}
            onClose={() => dispatch({ type: 'CLOSE_ELEMENT_EDITOR' })}
          />
        )
      })()}
    </div>
  )
}
