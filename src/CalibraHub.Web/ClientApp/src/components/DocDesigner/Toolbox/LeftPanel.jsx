import React, { useState, useEffect, useRef } from 'react'
import DataSourceTree from './DataSourceTree'
import { BAND_TYPES } from '../designerReducer'

// ── Sabitler ────────────────────────────────────────────────────────────────

// QR ayri buton degil — Barkod elementinin tipi olarak secilir (BARCODE_TYPES).
// Kullanici Barkod elementi ekleyip sag panelden tipi "QR Kod" yapar.
const ELEMENT_KINDS = [
  { kind: 'Label',         label: 'Etiket',         shortLabel: 'Etiket',   icon: 'T',   color: '#6366f1' },
  { kind: 'BoundField',    label: 'Veri Alanı',     shortLabel: 'Veri',     icon: '{}',  color: '#3b82f6' },
  { kind: 'Image',         label: 'Resim',          shortLabel: 'Resim',    icon: '▣',   color: '#10b981' },
  { kind: 'Shape',         label: 'Çizgi / Şekil',  shortLabel: 'Şekil',    icon: '━',   color: '#8b5cf6' },
  { kind: 'Barcode',       label: 'Barkod / QR',    shortLabel: 'Barkod',   icon: '▥',   color: '#0ea5e9' },
  { kind: 'AmountInWords', label: 'Yazı ile Tutar', shortLabel: 'YazıTutar',icon: '₺',   color: '#f59e0b' },
  { kind: 'PageNumber',    label: 'Sayfa No',       shortLabel: 'SayfaNo',  icon: '#',   color: '#ec4899' },
  { kind: 'DateTimeNow',   label: 'Tarih / Saat',   shortLabel: 'Tarih',    icon: '⏱',   color: '#06b6d4' },
]

const BAND_COLORS = {
  PageHeader:        '#6366f1',
  DocumentHeader:    '#f59e0b',
  TableHeader:       '#10b981',
  Detail:            '#8b5cf6',
  SubDetailHeader:   '#22d3ee',
  SubDetail:         '#06b6d4',
  SubDetailFooter:   '#0e7490',
  TotalsBlock:       '#ec4899',
  SignatureBlock:    '#3b82f6',
  PageFooter:        '#6b7280',
}

// ── Yardımcı ─────────────────────────────────────────────────────────────────

function SectionHeader({ title, action }) {
  return (
    <div style={{
      display: 'flex', alignItems: 'center', justifyContent: 'space-between',
      padding: '5px 10px 4px',
      background: 'var(--dd-section-bg, #f4f5f9)',
      borderTop: '1px solid var(--dd-border, #e5e7eb)',
      borderBottom: '1px solid var(--dd-border, #e5e7eb)',
    }}>
      <span style={{ fontSize: 9.5, fontWeight: 800, letterSpacing: 0.8, textTransform: 'uppercase', color: '#6366f1' }}>
        {title}
      </span>
      {action}
    </div>
  )
}

// ── Element palette ──────────────────────────────────────────────────────────

function ElementPalette() {
  const [hovered, setHovered] = useState(null)

  return (
    <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 3, padding: '6px 8px' }}>
      {ELEMENT_KINDS.map(({ kind, label, shortLabel, icon, color }) => (
        <div
          key={kind}
          draggable
          title={label}
          onDragStart={e => e.dataTransfer.setData('element-kind', kind)}
          onMouseEnter={() => setHovered(kind)}
          onMouseLeave={() => setHovered(null)}
          style={{
            display: 'flex', alignItems: 'center', gap: 5,
            padding: '5px 7px', borderRadius: 5, cursor: 'grab',
            background: hovered === kind ? `rgba(${hexToRgb(color)},0.15)` : 'var(--dd-surface-alt, #fafafa)',
            border: `1px solid ${hovered === kind ? color : 'var(--dd-border, #e8e8ec)'}`,
            transition: 'all 0.12s', userSelect: 'none',
          }}
        >
          <span style={{
            width: 20, height: 20, borderRadius: 4, flexShrink: 0,
            background: `rgba(${hexToRgb(color)},0.15)`,
            border: `1px solid rgba(${hexToRgb(color)},0.3)`,
            display: 'flex', alignItems: 'center', justifyContent: 'center',
            fontSize: 10, color, fontWeight: 700,
          }}>{icon}</span>
          <span style={{ fontSize: 10, color: 'var(--dd-text, #374151)', lineHeight: 1.2, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
            {shortLabel}
          </span>
        </div>
      ))}
    </div>
  )
}

// ── Rapor yapısı (bantlar) ───────────────────────────────────────────────────

function BandStructure({ state, dispatch }) {
  const { bands, selectedBandId } = state
  const [showAddMenu, setShowAddMenu] = useState(false)
  const addMenuRef = useRef(null)

  useEffect(() => {
    if (!showAddMenu) return
    const onDocClick = e => {
      if (addMenuRef.current && !addMenuRef.current.contains(e.target)) setShowAddMenu(false)
    }
    const onEsc = e => { if (e.key === 'Escape') setShowAddMenu(false) }
    document.addEventListener('mousedown', onDocClick)
    document.addEventListener('keydown', onEsc)
    return () => {
      document.removeEventListener('mousedown', onDocClick)
      document.removeEventListener('keydown', onEsc)
    }
  }, [showAddMenu])

  const BAND_ORDER = ['PageHeader','DocumentHeader','TableHeader','Detail','SubDetailHeader','SubDetail','SubDetailFooter','TotalsBlock','SignatureBlock','PageFooter']
  const sorted = [...bands].sort((a, b) => {
    const ai = BAND_ORDER.indexOf(a.type), bi = BAND_ORDER.indexOf(b.type)
    return (ai === -1 ? 99 : ai) - (bi === -1 ? 99 : bi)
  })
  const existingTypes = new Set(bands.map(b => b.type))
  const available = BAND_TYPES.filter(bt => !existingTypes.has(bt.type))

  return (
    <div>
      {/* Mevcut bantlar */}
      {sorted.length === 0 && (
        <div style={{ padding: '10px 12px', fontSize: 11, color: 'var(--dd-text-muted, #bbb)', textAlign: 'center' }}>
          Henüz bant yok
        </div>
      )}
      {sorted.map(band => {
        const color = BAND_COLORS[band.type] ?? '#6b7280'
        const def   = BAND_TYPES.find(d => d.type === band.type)
        const isSel = selectedBandId === band.id
        return (
          <div
            key={band.id}
            onClick={() => dispatch({ type: 'SELECT_BAND', bandId: band.id })}
            style={{
              display: 'flex', alignItems: 'center', gap: 7,
              padding: '5px 10px 5px 8px',
              cursor: 'pointer',
              background: isSel ? `rgba(${hexToRgb(color)},0.1)` : 'transparent',
              borderLeft: `3px solid ${isSel ? color : 'transparent'}`,
              transition: 'all 0.12s',
            }}
            onMouseEnter={e => { if (!isSel) e.currentTarget.style.background = 'var(--dd-surface-alt, #f5f5fb)' }}
            onMouseLeave={e => { if (!isSel) e.currentTarget.style.background = 'transparent' }}
          >
            {/* Renk noktası */}
            <span style={{ width: 8, height: 8, borderRadius: 2, background: color, flexShrink: 0 }} />

            {/* Bant adı */}
            <span style={{ flex: 1, fontSize: 11, color: isSel ? color : 'var(--dd-text, #374151)', fontWeight: isSel ? 600 : 400, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
              {def?.label ?? band.type}
            </span>

            {/* Yükseklik */}
            <span style={{ fontSize: 9.5, color: 'var(--dd-text-muted, #9ca3af)', flexShrink: 0 }}>{band.height.toFixed(0)}mm</span>

            {/* Sil */}
            <button
              onClick={e => { e.stopPropagation(); dispatch({ type: 'DELETE_BAND', bandId: band.id }) }}
              style={{
                width: 16, height: 16, border: 'none', background: 'none',
                cursor: 'pointer', padding: 0, display: 'flex', alignItems: 'center',
                justifyContent: 'center', color: '#d1d5db', borderRadius: 3, flexShrink: 0,
              }}
              title="Bandı sil"
              onMouseEnter={e => e.currentTarget.style.color = '#dc2626'}
              onMouseLeave={e => e.currentTarget.style.color = 'var(--dd-text-muted, #d1d5db)'}
            >×</button>
          </div>
        )
      })}

      {/* Bant ekle */}
      {available.length > 0 && (
        <div ref={addMenuRef} style={{ padding: '4px 8px 6px', position: 'relative' }}>
          <button
            onClick={() => setShowAddMenu(v => !v)}
            style={{
              width: '100%', padding: '5px 8px', borderRadius: 5,
              border: '1px dashed var(--dd-border-strong, #c4c4d4)', background: 'var(--dd-surface-alt, #fafafa)',
              fontSize: 11, color: 'var(--dd-accent, #6366f1)', cursor: 'pointer',
              display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 4,
            }}
          >
            <span style={{ fontSize: 14, lineHeight: 1 }}>+</span> Bant Ekle
          </button>

          {showAddMenu && (
            <div style={{
              position: 'absolute', left: 8, right: 8, top: 'calc(100% - 2px)', zIndex: 100,
              background: 'var(--dd-surface, #fff)', border: '1px solid var(--dd-border, #e5e7eb)', borderRadius: 6,
              boxShadow: '0 4px 12px rgba(0,0,0,0.30)', overflow: 'hidden',
            }}>
              {available.map(({ type, label }) => {
                const color = BAND_COLORS[type] ?? '#6b7280'
                return (
                  <div
                    key={type}
                    onClick={() => { dispatch({ type: 'ADD_BAND', bandType: type }); setShowAddMenu(false) }}
                    style={{
                      display: 'flex', alignItems: 'center', gap: 8,
                      padding: '6px 10px', cursor: 'pointer', fontSize: 11, color: 'var(--dd-text, #374151)',
                    }}
                    onMouseEnter={e => e.currentTarget.style.background = 'var(--dd-accent-soft, #f5f3ff)'}
                    onMouseLeave={e => e.currentTarget.style.background = 'transparent'}
                  >
                    <span style={{ width: 8, height: 8, borderRadius: 2, background: color, flexShrink: 0 }} />
                    {label}
                  </div>
                )
              })}
            </div>
          )}
        </div>
      )}
    </div>
  )
}

// ── Veri kaynakları (collapsible) ────────────────────────────────────────────

function CollapsibleSection({ title, defaultOpen = false, children }) {
  const [open, setOpen] = useState(defaultOpen)
  return (
    <>
      <div
        onClick={() => setOpen(v => !v)}
        style={{
          display: 'flex', alignItems: 'center', justifyContent: 'space-between',
          padding: '5px 10px 4px', cursor: 'pointer',
          background: 'var(--dd-section-bg, #f4f5f9)', borderTop: '1px solid var(--dd-border, #e5e7eb)', borderBottom: '1px solid var(--dd-border, #e5e7eb)',
        }}
      >
        <span style={{ fontSize: 9.5, fontWeight: 800, letterSpacing: 0.8, textTransform: 'uppercase', color: '#6366f1' }}>
          {title}
        </span>
        <span style={{ fontSize: 10, color: '#9ca3af' }}>{open ? '▲' : '▼'}</span>
      </div>
      {open && children}
    </>
  )
}

// ── Yardımcı ─────────────────────────────────────────────────────────────────

function hexToRgb(hex) {
  const h = (hex ?? '#6366f1').replace('#', '')
  const r = parseInt(h.slice(0,2),16)
  const g = parseInt(h.slice(2,4),16)
  const b = parseInt(h.slice(4,6),16)
  return `${r},${g},${b}`
}

// ── Ana bileşen ───────────────────────────────────────────────────────────────

export default function LeftPanel({ state, dispatch }) {
  return (
    <div style={{
      width: 200, flexShrink: 0, borderRight: '1px solid var(--dd-border, #e5e7eb)',
      background: 'var(--dd-surface, #fff)', display: 'flex', flexDirection: 'column',
      overflow: 'hidden',
    }}>
      <div style={{ flex: 1, overflow: 'auto' }}>
        {/* Araçlar / Element palette */}
        <SectionHeader title="Araçlar" />
        <ElementPalette />

        {/* Rapor yapısı / Band list */}
        <SectionHeader title="Rapor Yapısı" />
        <BandStructure state={state} dispatch={dispatch} />

        {/* Veri kaynakları (collapsible) */}
        <CollapsibleSection title="Veri Kaynakları" defaultOpen={state.dataSources.length > 0}>
          <div style={{ padding: '4px 0 8px' }}>
            <DataSourceTree state={state} dispatch={dispatch} />
          </div>
        </CollapsibleSection>
      </div>
    </div>
  )
}
