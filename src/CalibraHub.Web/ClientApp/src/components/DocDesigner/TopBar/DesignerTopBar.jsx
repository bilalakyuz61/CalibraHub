import React, { useState } from 'react'
import DataSourceModal from './DataSourceModal'
import { buildMockPreviewHtml } from '../utils/mockPreview'

export default function DesignerTopBar({ state, dispatch, zoom, onZoomChange, onSave, onPreview, onPdf }) {
  const { meta, bands, dataSources, saving, dirty, previewing } = state
  const [showDsModal, setShowDsModal] = useState(false)

  // Çoklu seçim hizalama çubuğu kaldırıldı; sağ PropertiesPanel'den yönetiliyor.

  const handleMockPreview = () => {
    const html = buildMockPreviewHtml(meta, bands)
    dispatch({ type: 'SET_PREVIEW_HTML', html })
  }

  return (
    <>
      {/* Ana çubuk */}
      <div style={{
        height: 48, flexShrink: 0, borderBottom: '1px solid var(--dd-border, #e5e7eb)',
        background: 'var(--dd-surface, #fff)',
        display: 'flex', alignItems: 'center', gap: 8, padding: '0 16px',
      }}>
        {/* Geri */}
        <a href="/DocDesigner" style={{ color: 'var(--dd-text-muted, #6b7280)', textDecoration: 'none', fontSize: 12, marginRight: 4, flexShrink: 0 }}>
          ← Liste
        </a>

        <div style={divider} />

        {/* Şablon adı */}
        <input
          value={meta.name}
          onChange={e => dispatch({ type: 'SET_META', payload: { name: e.target.value } })}
          placeholder="Şablon adı"
          style={{ border: 'none', outline: 'none', fontSize: 14, fontWeight: 600, color: 'var(--dd-text, #111)', background: 'transparent', minWidth: 160, maxWidth: 220 }}
        />

        {dirty && <span style={{ fontSize: 11, color: '#f59e0b', flexShrink: 0 }}>●</span>}

        <div style={{ flex: 1 }} />

        {/* Zoom */}
        <div style={{ display: 'flex', alignItems: 'center', gap: 2, marginRight: 4 }}>
          <button onClick={() => onZoomChange(-0.1)} style={zBtn}>−</button>
          <button onClick={() => onZoomChange(0)}    style={{ ...zBtn, minWidth: 44, fontSize: 11, fontWeight: 600 }}>
            {Math.round(zoom * 100)}%
          </button>
          <button onClick={() => onZoomChange(0.1)}  style={zBtn}>+</button>
        </div>

        <div style={divider} />

        {/* Veri kaynağı */}
        <button onClick={() => setShowDsModal(true)} style={btn()}>
          + Veri
        </button>

        {/* Mock Önizle (frontend, sunucu gerektirmez) */}
        <button onClick={handleMockPreview} style={btn()} title="Örnek verilerle önizle (sunucu gerekmez)">
          Mock Önizle
        </button>

        {/* Backend Önizle — sabit genişlik (label "..." iken daralip diger butonlari oynatmasin) */}
        <button onClick={onPreview} disabled={previewing} style={{ ...btn(), minWidth: 64 }}>
          {previewing ? '...' : 'Önizle'}
        </button>

        {/* PDF */}
        <button onClick={onPdf} style={btn()}>PDF</button>

        <div style={divider} />

        {/* Kaydet — sabit genişlik (label "Kaydediliyor…" iken genisleyip toolbar'i kaydirmasin) */}
        <button onClick={onSave} disabled={saving} style={{ ...primaryBtn, minWidth: 110 }} title="Kaydet (Ctrl+S)">
          {saving ? 'Kaydediliyor…' : 'Kaydet'}
        </button>

        {showDsModal && (
          <DataSourceModal
            existingSources={dataSources ?? []}
            onAdd={src => dispatch({ type: 'ADD_DATASOURCE', source: src })}
            onDelete={alias => dispatch({ type: 'REMOVE_DATASOURCE', alias })}
            onUpdate={(alias, patch) => dispatch({ type: 'UPDATE_DATASOURCE', alias, patch })}
            onClose={() => setShowDsModal(false)}
          />
        )}
      </div>

      {/* Hizalama çubuğu kaldırıldı — çoklu seçim yönetimi sağ PropertiesPanel
          üzerinden yapılır (aynı butonlar orada zaten var). Üst çubukta tekrar
          ettirmek görsel gürültü yaratıyordu (kullanıcı raporu). */}
    </>
  )
}

// AlignBtn kaldırıldı (hizalama çubuğu silindi). divider hala top bar'ın diğer
// kısımlarında (toolbar grup ayırıcılar) kullanılıyor.
const divider = { width: 1, height: 24, background: 'var(--dd-border, #e5e7eb)', margin: '0 2px', flexShrink: 0 }

const btn = () => ({
  padding: '5px 11px', borderRadius: 6, border: '1px solid var(--dd-border, #e5e7eb)',
  background: 'var(--dd-surface, #fff)', color: 'var(--dd-text, #374151)',
  fontSize: 12, cursor: 'pointer', fontWeight: 500, flexShrink: 0,
})

const primaryBtn = {
  padding: '5px 11px', borderRadius: 6, border: '1px solid var(--dd-accent, #6366f1)',
  background: 'var(--dd-accent, #6366f1)', color: '#fff',
  fontSize: 12, cursor: 'pointer', fontWeight: 500, flexShrink: 0,
}

const zBtn = {
  width: 26, height: 26, border: '1px solid var(--dd-border, #e5e7eb)', borderRadius: 4,
  background: 'var(--dd-surface, #fff)', cursor: 'pointer', fontSize: 14, fontWeight: 700,
  display: 'flex', alignItems: 'center', justifyContent: 'center', color: 'var(--dd-text, #374151)',
}
