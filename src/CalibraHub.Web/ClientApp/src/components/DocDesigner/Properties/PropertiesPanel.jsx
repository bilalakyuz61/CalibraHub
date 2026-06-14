import React, { useEffect, useState } from 'react'
import { BARCODE_TYPES } from '../designerReducer'
import { listDocTypes } from '../services/docDesignerService'

// 2026-05-26: Belge tipleri DB'den dinamik gelir (DocumentType tablosu) —
// modül seviyesinde cache tutariz ki her PageGrid render'inda yeniden fetch olmasin.
// İlk yuklemeden sonra in-memory cached liste kullanilir (sayfa yenilenmeden tasarimci
// acilip kapatildiginda hatirli olur).
let _docTypeCache = null
let _docTypeFetching = null
function fetchDocTypesCached() {
  if (_docTypeCache) return Promise.resolve(_docTypeCache)
  if (_docTypeFetching) return _docTypeFetching
  _docTypeFetching = listDocTypes()
    .then(list => { _docTypeCache = Array.isArray(list) ? list : []; return _docTypeCache })
    .catch(() => { _docTypeCache = []; return _docTypeCache })
    .finally(() => { _docTypeFetching = null })
  return _docTypeFetching
}

// ─── Yardımcı bileşenler ──────────────────────────────────────────────────────

function PanelWrap({ children }) {
  return (
    <div style={{
      width: 240, flexShrink: 0,
      borderLeft: '1px solid var(--dd-border, #e5e7eb)',
      background: 'var(--dd-surface, #fff)',
      overflow: 'auto', fontSize: 12,
      display: 'flex', flexDirection: 'column',
    }}>
      {children}
    </div>
  )
}

function PHeader({ kind, label, color = '#6366f1' }) {
  return (
    <div style={{
      height: 30, display: 'flex', alignItems: 'center', gap: 8, padding: '0 8px',
      borderBottom: '2px solid ' + color,
      background: `rgba(${hexToRgb(color)},0.12)`,
      flexShrink: 0,
    }}>
      <span style={{
        fontSize: 9, fontWeight: 700, background: color, color: '#fff',
        padding: '2px 5px', borderRadius: 3, letterSpacing: 0.3, flexShrink: 0,
      }}>{kind}</span>
      <span style={{ fontSize: 12, fontWeight: 600, color: 'var(--dd-text, #222)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{label}</span>
    </div>
  )
}

function PCategory({ title, color = '#6366f1' }) {
  return (
    <div style={{
      height: 19, display: 'flex', alignItems: 'center',
      background: `rgba(${hexToRgb(color)},0.12)`,
      borderLeft: `3px solid ${color}`,
      padding: '0 7px',
      fontSize: 9, fontWeight: 800, textTransform: 'uppercase',
      letterSpacing: 0.7, color,
      borderBottom: '1px solid var(--dd-border, #e8e8e8)',
      borderTop: '1px solid var(--dd-border, #e8e8e8)',
      marginTop: 1,
    }}>
      {title}
    </div>
  )
}

function PRow({ label, children, last }) {
  return (
    <div style={{
      display: 'flex', minHeight: 22, alignItems: 'stretch',
      borderBottom: last ? 'none' : '1px solid var(--dd-border, #f3f3f3)',
    }}>
      <div style={{
        width: 84, flexShrink: 0,
        padding: '0 6px', fontSize: 10.5, color: 'var(--dd-text-muted, #6b7280)',
        background: 'var(--dd-surface-alt, #f9f9fb)',
        borderRight: '1px solid var(--dd-border, #ebebeb)',
        display: 'flex', alignItems: 'center',
        whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis',
      }}>
        {label}
      </div>
      <div style={{ flex: 1, display: 'flex', alignItems: 'center', padding: '1px 4px' }}>
        {children}
      </div>
    </div>
  )
}

// Stacked variant — label ustte, icerik full-width altta. Uzun textarea / multi-line input icin.
function PStackedRow({ label, children, last }) {
  return (
    <div style={{
      display: 'flex', flexDirection: 'column',
      borderBottom: last ? 'none' : '1px solid var(--dd-border, #f3f3f3)',
      padding: '6px 8px 8px',
    }}>
      <div style={{
        fontSize: 10.5, color: 'var(--dd-text-muted, #6b7280)',
        marginBottom: 4, fontWeight: 600, textTransform: 'uppercase', letterSpacing: '.04em',
      }}>
        {label}
      </div>
      <div style={{ width: '100%' }}>
        {children}
      </div>
    </div>
  )
}

const gi = {
  border: 'none', outline: 'none', background: 'transparent',
  fontSize: 11, color: 'var(--dd-text, #111)', width: '100%', padding: '0 2px',
  fontFamily: 'inherit',
}

const hexToRgb = hex => {
  const h = (hex ?? '#6366f1').replace('#', '')
  const r = parseInt(h.slice(0,2),16), g = parseInt(h.slice(2,4),16), b = parseInt(h.slice(4,6),16)
  return `${r},${g},${b}`
}

// ─── Hizalama radio-button grubu + SVG ikonları ──────────────────────────────

function IconRadioGroup({ value, onChange, options, color = '#6366f1' }) {
  return (
    <div style={{ display: 'flex', gap: 2, width: '100%' }}>
      {options.map(opt => {
        const active = value === opt.value
        return (
          <button key={opt.value}
            type="button"
            title={opt.label}
            onClick={() => onChange(opt.value)}
            style={{
              flex: 1, height: 22, padding: 0,
              display: 'flex', alignItems: 'center', justifyContent: 'center',
              border: `1px solid ${active ? color : 'var(--dd-border, #d1d5db)'}`,
              background: active ? `rgba(${hexToRgb(color)},0.18)` : 'var(--dd-surface, #fff)',
              color: active ? color : 'var(--dd-text-muted, #6b7280)',
              cursor: 'pointer', borderRadius: 3, transition: 'all 0.1s',
            }}>
            {opt.icon}
          </button>
        )
      })}
    </div>
  )
}

function AlignIcon({ variant }) {
  // Her satır farklı uzunlukta dört çizgi; hizalama yönüne göre kaydırılır.
  const lines = (() => {
    switch (variant) {
      case 'left':    return [[2,12], [2,9], [2,11], [2,7]]
      case 'center':  return [[3,11], [5,9], [3,11], [5,7]]
      case 'right':   return [[2,12], [5,9], [3,11], [7,7]]
      case 'justify': return [[2,12], [2,12], [2,12], [2,12]]
      default:        return []
    }
  })()
  return (
    <svg width="13" height="13" viewBox="0 0 14 14"
         stroke="currentColor" strokeWidth="1.4" strokeLinecap="round">
      {lines.map(([x, w], i) => (
        <line key={i} x1={x} y1={2 + i * 3} x2={x + w} y2={2 + i * 3} />
      ))}
    </svg>
  )
}

function VAlignIcon({ variant }) {
  // İçi dolu blok + bir yatay çizgi (üst/orta/alt) — dikey hizalamayı simgeler.
  const block = <rect x="3" y="4" width="8" height="6" rx="0.5" fillOpacity="0.55" />
  return (
    <svg width="13" height="13" viewBox="0 0 14 14"
         stroke="currentColor" strokeWidth="1.4" strokeLinecap="round" fill="currentColor">
      {variant === 'top' && <>
        <line x1="2" y1="2" x2="12" y2="2" stroke="currentColor" />
        <rect x="3" y="4" width="8" height="6" rx="0.5" fillOpacity="0.55" stroke="none" />
      </>}
      {variant === 'middle' && <>
        <line x1="2" y1="7" x2="12" y2="7" stroke="currentColor" />
        <rect x="3" y="3" width="8" height="3" rx="0.5" fillOpacity="0.55" stroke="none" />
        <rect x="3" y="8" width="8" height="3" rx="0.5" fillOpacity="0.55" stroke="none" />
      </>}
      {variant === 'bottom' && <>
        <rect x="3" y="4" width="8" height="6" rx="0.5" fillOpacity="0.55" stroke="none" />
        <line x1="2" y1="12" x2="12" y2="12" stroke="currentColor" />
      </>}
    </svg>
  )
}

// ─── Ana bileşen ──────────────────────────────────────────────────────────────

export default function PropertiesPanel({ state, dispatch }) {
  const { bands, selectedElementId, selectedElementIds, selectedBandId, meta } = state

  const selectedBand    = bands.find(b => b.id === selectedBandId)
  const selectedElement = selectedBand?.elements.find(e => e.id === selectedElementId)
    ?? bands.flatMap(b => b.elements).find(e => e.id === selectedElementId)

  if ((selectedElementIds?.length ?? 0) > 1) {
    // Seçili elementlerin nesnelerini topla — ortak property editor için
    const allElements = bands.flatMap(b => b.elements)
    const selectedEls = allElements.filter(e => selectedElementIds.includes(e.id))
    return <PanelWrap><MultiSelectionPanel elements={selectedEls} ids={selectedElementIds} dispatch={dispatch} /></PanelWrap>
  }
  if (!selectedElementId && !selectedBandId) {
    return <PanelWrap><PageGrid meta={meta} dispatch={dispatch} /></PanelWrap>
  }
  if (selectedElementId && selectedElement) {
    return <PanelWrap><ElementGrid el={selectedElement} dispatch={dispatch} /></PanelWrap>
  }
  if (selectedBandId && selectedBand) {
    return <PanelWrap><BandGrid band={selectedBand} dataSources={state.dataSources} dispatch={dispatch} /></PanelWrap>
  }
  return <PanelWrap><div style={{ padding: 12, color: '#aaa', fontSize: 12 }}>Seçim yok</div></PanelWrap>
}

// ─── Çoklu seçim hizalama paneli ─────────────────────────────────────────────

const ALIGN_BTNS = [
  { align: 'left',    icon: '⊢', tip: 'Sola hizala (referans: 1. element)' },
  { align: 'centerH', icon: '⊥', tip: 'Yatay ortala' },
  { align: 'right',   icon: '⊣', tip: 'Sağa hizala' },
  { align: 'top',     icon: '⊤', tip: 'Üste hizala' },
  { align: 'centerV', icon: '⊕', tip: 'Dikey ortala' },
  { align: 'bottom',  icon: '⊦', tip: 'Alta hizala' },
]

function AlignBtn({ title, onClick, children }) {
  return (
    <button
      title={title}
      onClick={onClick}
      style={{
        width: 34, height: 30, border: '1px solid var(--dd-border, #e5e7eb)', borderRadius: 5,
        background: 'var(--dd-surface, #fff)', cursor: 'pointer', fontSize: 15,
        display: 'flex', alignItems: 'center', justifyContent: 'center',
        color: 'var(--dd-accent, #6366f1)', transition: 'background 0.1s',
      }}
      onMouseEnter={e => e.currentTarget.style.background = 'var(--dd-accent-soft, #ede9fe)'}
      onMouseLeave={e => e.currentTarget.style.background = 'var(--dd-surface, #fff)'}
    >
      {children}
    </button>
  )
}

/**
 * Çoklu seçim panel — hizalama + ORTAK PROPERTY editörü.
 * Seçili elementlerin ortak değerlerini bulup gösterir. Bir değer değiştirilince
 * tüm seçili elementlere UPDATE_ELEMENTS_BULK ile uygulanır.
 *
 * Tutarsız değerli property'ler "—" (mixed) olarak gösterilir; değişiklik
 * yapılırsa tümüne aynı değer set edilir.
 */
function MultiSelectionPanel({ elements, ids, dispatch }) {
  const count = elements.length

  // Ortak değer bulucu: tüm elementlerde aynı değer varsa onu döner, yoksa undefined
  const commonProp = (getter) => {
    if (count === 0) return undefined
    const first = getter(elements[0])
    for (let i = 1; i < count; i++) {
      if (getter(elements[i]) !== first) return undefined   // mixed
    }
    return first
  }

  // Style erişim helper'ı (style undefined olabilir)
  const cs = (key) => commonProp(e => e.style?.[key])

  const bulkSetStyle = (patch) => dispatch({ type: 'UPDATE_ELEMENTS_BULK', elementIds: ids, patch: { style: patch } })

  // Mevcut ortak değerler
  const fontSize   = cs('fontSize')
  const bold       = cs('bold')
  const italic     = cs('italic')
  const underline  = cs('underline')
  const align      = cs('align')
  const color      = cs('color')
  const bgColor    = cs('bgColor')

  return (
    <>
      <PHeader kind="ÇOK" label={`${count} element seçili`} color="#6366f1" />

      {/* Ortak Görünüm */}
      <PCategory title="Görünüm" color="#6366f1" />

      <PRow label="Font Boyutu">
        <input type="number" min="4" max="72" step="0.5"
               value={fontSize ?? ''}
               placeholder={fontSize === undefined ? 'Karışık' : ''}
               onChange={e => { const v = +e.target.value; if (!isNaN(v) && v > 0) bulkSetStyle({ fontSize: v }) }}
               style={gi} />
      </PRow>

      <PRow label="Stil">
        <div style={{ display: 'flex', gap: 4 }}>
          <StyleBtn active={bold === true} mixed={bold === undefined}
                    onClick={() => bulkSetStyle({ bold: !(bold === true) })} title="Kalın"><strong>B</strong></StyleBtn>
          <StyleBtn active={italic === true} mixed={italic === undefined}
                    onClick={() => bulkSetStyle({ italic: !(italic === true) })} title="İtalik"><em>I</em></StyleBtn>
          <StyleBtn active={underline === true} mixed={underline === undefined}
                    onClick={() => bulkSetStyle({ underline: !(underline === true) })} title="Altı çizili"><u>U</u></StyleBtn>
        </div>
      </PRow>

      <PRow label="Hizalama">
        <div style={{ display: 'flex', gap: 4 }}>
          {['left','center','right','justify'].map(a => (
            <StyleBtn key={a} active={align === a} mixed={align === undefined && a === 'left'}
                      onClick={() => bulkSetStyle({ align: a })} title={a}>
              {a === 'left' ? '⊢' : a === 'center' ? '↔' : a === 'right' ? '⊣' : '☰'}
            </StyleBtn>
          ))}
        </div>
      </PRow>

      <PRow label="Yazı Rengi">
        <ColorInput value={color} placeholder={color === undefined ? 'Karışık' : ''}
                    onChange={v => bulkSetStyle({ color: v })} />
      </PRow>

      <PRow label="Arka Plan">
        <ColorInput value={bgColor} placeholder={bgColor === undefined ? 'Karışık' : ''}
                    onChange={v => bulkSetStyle({ bgColor: v })} allowTransparent />
      </PRow>

      <PRow label="Kenarlık Kenarları">
        <div style={{ display: 'flex', gap: 4, flexWrap: 'wrap' }}>
          {/* Her bir kenar için toggle — tek element editöründekiyle aynı pattern.
              Tüm seçili elementlere aynı anda uygulanır. */}
          {[
            { key: 'borderTop',    label: 'Üst', icon: '⎯' },
            { key: 'borderRight',  label: 'Sağ', icon: '⏐' },
            { key: 'borderBottom', label: 'Alt', icon: '⎯' },
            { key: 'borderLeft',   label: 'Sol', icon: '⏐' },
          ].map(side => {
            const active = cs(side.key) === true
            return (
              <button key={side.key}
                      onClick={() => bulkSetStyle({
                        border: undefined,
                        [side.key]: !active,
                      })}
                      title={`${side.label} kenarlık`}
                      style={{
                        ...miniBtnStyle,
                        background: active ? 'var(--dd-accent-soft, #ede9fe)' : 'var(--dd-surface, #fff)',
                        borderColor: active ? 'var(--dd-accent, #6366f1)' : 'var(--dd-border, #e5e7eb)',
                        color: active ? 'var(--dd-accent, #6366f1)' : 'var(--dd-text, #374151)',
                        minWidth: 38,
                      }}>
                {side.label}
              </button>
            )
          })}
        </div>
      </PRow>

      <PRow label="Kısayol">
        <div style={{ display: 'flex', gap: 4 }}>
          <button onClick={() => bulkSetStyle({
                    border: undefined, borderTop: true, borderRight: true, borderBottom: true, borderLeft: true,
                  })}
                  style={miniBtnStyle}
                  title="4 kenara birden ekle">Tüm Kenarlar</button>
          <button onClick={() => bulkSetStyle({
                    border: undefined, borderTop: false, borderRight: false, borderBottom: false, borderLeft: false,
                  })}
                  style={miniBtnStyle}
                  title="Tüm kenarları kaldır">Hiçbiri</button>
        </div>
      </PRow>

      <PCategory title="Hizalama (Konum)" color="#6366f1" />
      <div style={{ padding: '8px 8px 4px', display: 'flex', flexWrap: 'wrap', gap: 4 }}>
        {ALIGN_BTNS.map(({ align, icon, tip }) => (
          <AlignBtn key={align} title={tip} onClick={() => dispatch({ type: 'ALIGN_ELEMENTS', align })}>
            {icon}
          </AlignBtn>
        ))}
      </div>

      {count >= 3 && (
        <>
          <PCategory title="Aralık Eşitle" color="#8b5cf6" />
          <div style={{ padding: '8px 8px 4px', display: 'flex', gap: 4 }}>
            <button onClick={() => dispatch({ type: 'DISTRIBUTE_ELEMENTS', axis: 'h' })}
                    style={{ flex: 1, height: 30, border: '1px solid var(--dd-border, #e5e7eb)', borderRadius: 5, background: 'var(--dd-surface, #fff)', cursor: 'pointer', fontSize: 11, fontWeight: 600, color: '#8b5cf6' }}>↔ Yatay</button>
            <button onClick={() => dispatch({ type: 'DISTRIBUTE_ELEMENTS', axis: 'v' })}
                    style={{ flex: 1, height: 30, border: '1px solid var(--dd-border, #e5e7eb)', borderRadius: 5, background: 'var(--dd-surface, #fff)', cursor: 'pointer', fontSize: 11, fontWeight: 600, color: '#8b5cf6' }}>↕ Dikey</button>
          </div>
        </>
      )}

      <div style={{ padding: '10px 8px' }}>
        <button onClick={() => dispatch({ type: 'DELETE_ELEMENT', elementIds: ids })}
                style={{ width: '100%', border: '1px solid #fca5a5', borderRadius: 5, padding: '6px 0', background: '#fef2f2', color: '#dc2626', cursor: 'pointer', fontSize: 11 }}>
          Seçilenleri Sil ({count})
        </button>
      </div>

      <div style={{ padding: '4px 10px 8px', fontSize: 10, color: '#9ca3af', lineHeight: 1.5 }}>
        Karışık değerli alanlar "Karışık" yazar — değiştirirsen tüm seçime uygulanır.
      </div>
    </>
  )
}

// Görsel toggle butonu — bold/italic/align gibi durumlar için
function StyleBtn({ active, mixed, onClick, title, children }) {
  return (
    <button onClick={onClick} title={title} style={{
      width: 28, height: 26, borderRadius: 4, cursor: 'pointer',
      border: '1px solid ' + (active ? 'var(--dd-accent, #6366f1)' : 'var(--dd-border, #e5e7eb)'),
      background: active ? 'var(--dd-accent-soft, #ede9fe)' : 'var(--dd-surface, #fff)',
      color: active ? 'var(--dd-accent, #6366f1)' : (mixed ? 'var(--dd-text-muted, #9ca3af)' : 'var(--dd-text, #374151)'),
      fontSize: 12, fontWeight: 600, display: 'flex', alignItems: 'center', justifyContent: 'center',
    }}>{children}</button>
  )
}

// Renk girişi — color picker + text input + transparent toggle
function ColorInput({ value, placeholder, onChange, allowTransparent }) {
  const isTransparent = value === 'transparent'
  return (
    <div style={{ display: 'flex', gap: 4, alignItems: 'center' }}>
      <input type="color"
             value={isTransparent || !value ? '#000000' : value}
             onChange={e => onChange(e.target.value)}
             style={{ width: 28, height: 26, padding: 0, border: '1px solid var(--dd-border, #e5e7eb)', borderRadius: 4, cursor: 'pointer', opacity: isTransparent ? 0.4 : 1 }} />
      <input type="text"
             value={value ?? ''} placeholder={placeholder}
             onChange={e => onChange(e.target.value)}
             style={{ ...gi, fontFamily: 'monospace', fontSize: 11 }} />
      {allowTransparent && (
        <button onClick={() => onChange('transparent')}
                title="Şeffaf"
                style={{ ...miniBtnStyle, padding: '0 6px', minWidth: 0 }}>—</button>
      )}
    </div>
  )
}

const miniBtnStyle = {
  height: 26, padding: '0 8px', fontSize: 10.5, fontWeight: 600,
  border: '1px solid var(--dd-border, #e5e7eb)',
  background: 'var(--dd-surface, #fff)', color: 'var(--dd-text, #374151)',
  borderRadius: 4, cursor: 'pointer', whiteSpace: 'nowrap',
}

function AlignmentPanel({ ids, dispatch }) {
  const count = ids.length
  return (
    <>
      <PHeader kind="ÇOK" label={`${count} element seçili`} color="#6366f1" />

      <PCategory title="Hizalama" color="#6366f1" />
      <div style={{ padding: '8px 8px 4px', display: 'flex', flexWrap: 'wrap', gap: 4 }}>
        {ALIGN_BTNS.map(({ align, icon, tip }) => (
          <AlignBtn key={align} title={tip} onClick={() => dispatch({ type: 'ALIGN_ELEMENTS', align })}>
            {icon}
          </AlignBtn>
        ))}
      </div>

      {count >= 3 && (
        <>
          <PCategory title="Aralık Eşitle" color="#8b5cf6" />
          <div style={{ padding: '8px 8px 4px', display: 'flex', gap: 4 }}>
            <button
              onClick={() => dispatch({ type: 'DISTRIBUTE_ELEMENTS', axis: 'h' })}
              style={{ flex: 1, height: 30, border: '1px solid var(--dd-border, #e5e7eb)', borderRadius: 5, background: 'var(--dd-surface, #fff)', cursor: 'pointer', fontSize: 11, fontWeight: 600, color: '#8b5cf6' }}
              onMouseEnter={e => e.currentTarget.style.background = 'var(--dd-surface-alt, #f5f3ff)'}
              onMouseLeave={e => e.currentTarget.style.background = 'var(--dd-surface, #fff)'}
            >↔ Yatay</button>
            <button
              onClick={() => dispatch({ type: 'DISTRIBUTE_ELEMENTS', axis: 'v' })}
              style={{ flex: 1, height: 30, border: '1px solid var(--dd-border, #e5e7eb)', borderRadius: 5, background: 'var(--dd-surface, #fff)', cursor: 'pointer', fontSize: 11, fontWeight: 600, color: '#8b5cf6' }}
              onMouseEnter={e => e.currentTarget.style.background = 'var(--dd-surface-alt, #f5f3ff)'}
              onMouseLeave={e => e.currentTarget.style.background = 'var(--dd-surface, #fff)'}
            >↕ Dikey</button>
          </div>
        </>
      )}

      <div style={{ padding: '10px 8px' }}>
        <button
          onClick={() => dispatch({ type: 'DELETE_ELEMENT', elementIds: ids })}
          style={{ width: '100%', border: '1px solid #fca5a5', borderRadius: 5, padding: '6px 0', background: '#fef2f2', color: '#dc2626', cursor: 'pointer', fontSize: 11 }}
        >
          Seçilenleri Sil ({count})
        </button>
      </div>

      <div style={{ padding: '4px 10px 8px', fontSize: 10, color: '#9ca3af', lineHeight: 1.5 }}>
        Shift / Ctrl + tık ile seçime ekle/çıkar.<br/>
        İlk seçilen element hizalama referansıdır.
      </div>
    </>
  )
}

// ─── Sayfa özellikleri ────────────────────────────────────────────────────────

const PAPER_SIZES = [
  { value: 'A4',     label: 'A4 (210×297 mm)',         w: 210,   h: 297   },
  { value: 'A5',     label: 'A5 (148×210 mm)',         w: 148,   h: 210   },
  { value: 'A3',     label: 'A3 (297×420 mm)',         w: 297,   h: 420   },
  { value: 'A6',     label: 'A6 (105×148 mm)',         w: 105,   h: 148   },
  { value: 'B5',     label: 'B5 (176×250 mm)',         w: 176,   h: 250   },
  { value: 'B4',     label: 'B4 (250×353 mm)',         w: 250,   h: 353   },
  { value: 'Letter', label: 'Letter (215,9×279,4 mm)', w: 215.9, h: 279.4 },
  { value: 'Legal',  label: 'Legal (215,9×355,6 mm)',  w: 215.9, h: 355.6 },
  { value: 'custom', label: 'Özel',                    w: null,  h: null  },
]

function detectPaperSize(w, h) {
  const match = PAPER_SIZES.find(p => p.w &&
    ((Math.abs(p.w - w) < 1 && Math.abs(p.h - h) < 1) ||
     (Math.abs(p.w - h) < 1 && Math.abs(p.h - w) < 1)))
  return match?.value ?? 'custom'
}

function PageGrid({ meta, dispatch }) {
  const set = patch => dispatch({ type: 'SET_META', payload: patch })
  const paperSize   = detectPaperSize(meta.pageW, meta.pageH)
  const orientation = meta.pageW > meta.pageH ? 'landscape' : 'portrait'
  const isCustom    = paperSize === 'custom'

  // Belge tipleri DB'den (DocumentType tablosu) — cache'li fetch.
  const [docTypes, setDocTypes] = useState(_docTypeCache || [])
  useEffect(() => {
    if (_docTypeCache) return
    fetchDocTypesCached().then(list => setDocTypes(list))
  }, [])

  // NOT: Mail-spesifik view/kolon yuklemeleri (listDbViews / getDbViewColumns) artik
  // kullanilmiyor — eski 'email cikti modu' bloğuyla beraber kaldirildi. Mail
  // sablonu artik sadece "useAsMailTemplate" toggle'i — standart dizayn akisi.

  const onPaperChange = value => {
    if (value === 'custom') return  // mevcut değerleri koru
    const p = PAPER_SIZES.find(x => x.value === value)
    if (!p?.w) return
    // Mevcut yön korunsun
    if (orientation === 'landscape') set({ pageW: p.h, pageH: p.w })
    else                              set({ pageW: p.w, pageH: p.h })
  }

  const onOrientationChange = next => {
    if (next === orientation) return
    set({ pageW: meta.pageH, pageH: meta.pageW })
  }

  return (
    <>
      <PHeader kind="SAYFA" label="Sayfa Özellikleri" color="#6366f1" />
      <PCategory title="Genel" color="#6366f1" />
      <PRow label="Ad"><input value={meta.name} onChange={e => set({ name: e.target.value })} style={gi} /></PRow>

      {/* Belge Tipi — TÜM tasarimlar bir DocumentType ile iliskilidir.
          Onceden 'email cikti modu' bu satiri gizliyor + Mail Sablonu'na zorlu yordu;
          kaldirildi. Toplu mail icin kullanici yeni bir belge tipi (orn. 'bulk_email')
          tanimlar ve bu tipi seçer. 'custom' filtresi de kaldirildi — kullanici hangi
          belge tipini tanimladiysa onu seçebilir. */}
      <PRow label="Belge Tipi">
        {(() => {
          const currentVal = meta.documentTypeId != null
            ? String(meta.documentTypeId)
            : (docTypes.find(dt => dt.code === meta.docType)?.id != null
                ? String(docTypes.find(dt => dt.code === meta.docType).id)
                : '')
          return (
            <select value={currentVal}
                    onChange={e => {
                      const v = e.target.value
                      const dt = docTypes.find(x => String(x.id) === v)
                      if (dt) set({ documentTypeId: dt.id, docType: dt.code })
                    }}
                    style={{ ...gi, cursor: 'pointer' }}>
              {docTypes.length === 0 && (
                <option value="">— Yükleniyor —</option>
              )}
              {!currentVal && docTypes.length > 0 && (
                <option value="" disabled>Seçim yapın</option>
              )}
              {docTypes.map(dt => (
                <option key={dt.code} value={dt.id != null ? String(dt.id) : dt.code}>
                  {dt.name || dt.code}
                </option>
              ))}
            </select>
          )
        })()}
      </PRow>

      <PRow label="Varsayılan">
        <label style={{ display: 'flex', alignItems: 'center', gap: 5, cursor: 'pointer' }}>
          <input type="checkbox" checked={meta.isDefault ?? false}
            onChange={e => set({ isDefault: e.target.checked })} />
          <span style={{ fontSize: 10.5, color: 'var(--dd-text-muted, #6b7280)' }}>
            Bu belge tipi için varsayılan
          </span>
        </label>
      </PRow>

      {/* "Mail şablonu olarak da kullan" toggle:
          Acik ise bu dizayn mail compose ekraninda da listelenir. Cikti hala PDF
          render mantigi ile uretilir (HTML body olarak); kullanici bir belgeyi
          (orn. SalesQuote #123) sectiginde view'a belgeId parametresi gonderilir,
          dizayn render edilir ve mail govdesi olur. Standart akista herhangi bir
          ozel UI yok; bayrak DocLayout tablosunda use_as_mail_template kolonu. */}
      <PRow label="Mail şablonu">
        <label style={{ display: 'flex', alignItems: 'center', gap: 5, cursor: 'pointer' }}>
          <input type="checkbox" checked={meta.useAsMailTemplate ?? false}
            onChange={e => set({ useAsMailTemplate: e.target.checked })} />
          <span style={{ fontSize: 10.5, color: 'var(--dd-text-muted, #6b7280)' }}>
            Mail şablonu olarak da kullan
          </span>
        </label>
      </PRow>

      <PCategory title="Kağıt" color="#6366f1" />
      <PRow label="Kağıt Boyutu">
        <select value={paperSize} onChange={e => onPaperChange(e.target.value)}
          style={{ ...gi, cursor: 'pointer' }}>
          {PAPER_SIZES.map(p => (
            <option key={p.value} value={p.value}>{p.label}</option>
          ))}
        </select>
      </PRow>
      <PRow label="Yön">
        <div style={{ display: 'flex', gap: 4, width: '100%' }}>
          <button onClick={() => onOrientationChange('portrait')}
            style={{
              flex: 1, height: 20, fontSize: 10, cursor: 'pointer',
              border: `1px solid ${orientation === 'portrait' ? '#6366f1' : 'var(--dd-border, #d1d5db)'}`,
              background: orientation === 'portrait' ? 'rgba(99,102,241,0.18)' : 'var(--dd-surface, #fff)',
              color: orientation === 'portrait' ? '#6366f1' : 'var(--dd-text-muted, #555)',
              borderRadius: 3, fontWeight: orientation === 'portrait' ? 700 : 400,
            }}>
            ▯ Dikey
          </button>
          <button onClick={() => onOrientationChange('landscape')}
            style={{
              flex: 1, height: 20, fontSize: 10, cursor: 'pointer',
              border: `1px solid ${orientation === 'landscape' ? '#6366f1' : 'var(--dd-border, #d1d5db)'}`,
              background: orientation === 'landscape' ? 'rgba(99,102,241,0.18)' : 'var(--dd-surface, #fff)',
              color: orientation === 'landscape' ? '#6366f1' : 'var(--dd-text-muted, #555)',
              borderRadius: 3, fontWeight: orientation === 'landscape' ? 700 : 400,
            }}>
            ▭ Yatay
          </button>
        </div>
      </PRow>
      <PRow label="Genişlik (mm)">
        <input type="number" value={meta.pageW} step="0.1"
          onChange={e => set({ pageW: +e.target.value })}
          disabled={!isCustom}
          style={{ ...gi, opacity: isCustom ? 1 : 0.6 }} />
      </PRow>
      <PRow label="Yükseklik (mm)">
        <input type="number" value={meta.pageH} step="0.1"
          onChange={e => set({ pageH: +e.target.value })}
          disabled={!isCustom}
          style={{ ...gi, opacity: isCustom ? 1 : 0.6 }} />
      </PRow>

      <PCategory title="Kenar Boşlukları (mm)" color="#6366f1" />
      <PRow label="Üst"><input type="number" value={meta.marginTop} onChange={e => set({ marginTop: +e.target.value })} style={gi} /></PRow>
      <PRow label="Alt"><input type="number" value={meta.marginBot} onChange={e => set({ marginBot: +e.target.value })} style={gi} /></PRow>
      <PRow label="Sol"><input type="number" value={meta.marginLeft} onChange={e => set({ marginLeft: +e.target.value })} style={gi} /></PRow>
      <PRow label="Sağ" last><input type="number" value={meta.marginRight} onChange={e => set({ marginRight: +e.target.value })} style={gi} /></PRow>
    </>
  )
}

// ─── Bant özellikleri ─────────────────────────────────────────────────────────

const BAND_LABELS = {
  PageHeader: 'Sayfa Başlığı', DocumentHeader: 'Belge Başlığı', TableHeader: 'Tablo Başlığı',
  Detail: 'Detay Satırı',
  SubDetailHeader: 'Alt Detay Başlığı', SubDetail: 'Alt Detay Satırı', SubDetailFooter: 'Alt Detay Altı',
  TotalsBlock: 'Toplam Bloku', SignatureBlock: 'İmza Bloku', PageFooter: 'Sayfa Altı',
  mail_body: 'Mail Gövdesi',
}

function BandGrid({ band, dataSources, dispatch }) {
  const set = patch => dispatch({ type: 'UPDATE_BAND', bandId: band.id, patch })
  return (
    <>
      <PHeader kind="BANT" label={BAND_LABELS[band.type] ?? band.type} color="#10b981" />
      <PCategory title="Bant Özellikleri" color="#10b981" />
      <PRow label="Yükseklik (mm)">
        <input type="number" value={band.height} step="0.5"
          onChange={e => dispatch({ type: 'RESIZE_BAND', bandId: band.id, height: +e.target.value })}
          style={gi} />
      </PRow>
      <PRow label="Veri Kaynağı">
        <select value={band.dataAlias ?? ''} onChange={e => set({ dataAlias: e.target.value || null })}
          style={{ ...gi, cursor: 'pointer' }}>
          <option value="">— (bağlama yok)</option>
          {(dataSources ?? []).map(ds => (
            <option key={ds.alias} value={ds.alias}>
              {ds.alias} ({ds.role})
            </option>
          ))}
        </select>
      </PRow>
      <PRow label="Her Sayfada">
        <input type="checkbox" checked={band.repeatOnEveryPage} onChange={e => set({ repeatOnEveryPage: e.target.checked })} />
      </PRow>
      <PRow label="Büyüyebilir">
        <input type="checkbox" checked={band.canGrow} onChange={e => set({ canGrow: e.target.checked })} />
      </PRow>

      <PCategory title="Sayfa Akışı" color="#10b981" />
      <PRow label="Yeni Sayfa Başlat">
        <input type="checkbox" checked={band.startNewPage ?? false}
          onChange={e => set({ startNewPage: e.target.checked })}
          title="Bu bant öncesi yeni sayfaya geç" />
      </PRow>
      <PRow label="Detay Boşsa Bas">
        <input type="checkbox" checked={band.printIfDetailEmpty ?? true}
          onChange={e => set({ printIfDetailEmpty: e.target.checked })}
          title="Bağlı veri kaynağı boşsa bile bantı bas (header/footer için)" />
      </PRow>
      <PRow label="Bölünebilir">
        <input type="checkbox" checked={band.allowSplit ?? false}
          onChange={e => set({ allowSplit: e.target.checked })}
          title="Sayfa sonunda bant satır arasında bölünebilir mi (büyüyebilir bantlar için)" />
      </PRow>
      <PRow label="Detayı Bir Arada Tut" last={band.type !== 'Detail' && band.type !== 'SubDetail'}>
        <input type="checkbox" checked={band.keepDetailTogether ?? true}
          onChange={e => set({ keepDetailTogether: e.target.checked })}
          title="Master ve detay bantları mümkünse aynı sayfada kalsın" />
      </PRow>

      {/* Zebra modu — sadece Detail / SubDetail bantlarında anlamlı */}
      {(band.type === 'Detail' || band.type === 'SubDetail') && (
        <>
          <PCategory title="Zebra (Satır Bandı)" color="#10b981" />
          <PRow label="Zebra Modu">
            <input type="checkbox" checked={band.zebraEnabled ?? false}
              onChange={e => set({ zebraEnabled: e.target.checked })}
              title="Çift sıra satırlara hafif arka plan rengi uygula" />
          </PRow>
          {band.zebraEnabled && (
            <PRow label="Zebra Rengi" last>
              <input type="color" value={band.zebraColor ?? '#F3F4F6'}
                onChange={e => set({ zebraColor: e.target.value })}
                style={{ ...gi, padding: 0, height: 24, cursor: 'pointer' }}
                title="Çift sıralara uygulanacak arka plan rengi" />
            </PRow>
          )}
        </>
      )}
    </>
  )
}

// ─── Element özellikleri ──────────────────────────────────────────────────────

const KIND_LABELS = {
  Label: 'Etiket', BoundField: 'Veri Alanı', Image: 'Resim', Shape: 'Şekil',
  Barcode: 'Barkod', QrCode: 'QR Kod',
  AmountInWords: 'Yazı ile Tutar', PageNumber: 'Sayfa No', DateTimeNow: 'Tarih/Saat',
}
const KIND_COLORS = {
  Label: '#6366f1', BoundField: '#3b82f6', Image: '#10b981', Shape: '#8b5cf6',
  Barcode: '#0ea5e9', QrCode: '#14b8a6',
  AmountInWords: '#f59e0b', PageNumber: '#ec4899', DateTimeNow: '#06b6d4',
}

function ElementGrid({ el, dispatch }) {
  const set       = patch => dispatch({ type: 'UPDATE_ELEMENT', elementId: el.id, patch })
  const setStyle  = s    => set({ style: { ...el.style, ...s } })
  const setBinding = b   => set({ binding: { ...(el.binding ?? { alias: '', col: '' }), ...b } })
  const color     = KIND_COLORS[el.kind] ?? '#6366f1'

  return (
    <>
      <PHeader kind={el.kind.toUpperCase()} label={KIND_LABELS[el.kind] ?? el.kind} color={color} />

      <PCategory title="Konum & Boyut" color={color} />
      <PRow label="X (mm)">
        <input type="number" step="0.5" value={(el.x ?? 0).toFixed(1)} onChange={e => set({ x: +e.target.value })} style={gi} />
      </PRow>
      <PRow label="Y (mm)">
        <input type="number" step="0.5" value={(el.y ?? 0).toFixed(1)} onChange={e => set({ y: +e.target.value })} style={gi} />
      </PRow>
      <PRow label="Genişlik (mm)">
        <input type="number" step="0.5" value={(el.w ?? 0).toFixed(1)} onChange={e => set({ w: +e.target.value })} style={gi} />
      </PRow>
      <PRow label="Yükseklik (mm)">
        <input type="number" step="0.5" value={(el.h ?? 0).toFixed(1)} onChange={e => set({ h: +e.target.value })} style={gi} />
      </PRow>

      <PCategory title="Görünüm" color={color} />
      <PRow label="Font Boyutu">
        <input type="number" value={el.style?.fontSize ?? 10} min={6} max={72}
          onChange={e => setStyle({ fontSize: +e.target.value })} style={{ ...gi, width: 48 }} />
        <span style={{ fontSize: 10, color: '#aaa', marginLeft: 4 }}>pt</span>
      </PRow>
      <PRow label="Stil">
        <div style={{ display: 'flex', gap: 3 }}>
          {[['bold','B'],['italic','İ'],['underline','U']].map(([k, lbl]) => (
            <button key={k}
              onClick={() => setStyle({ [k]: !el.style?.[k] })}
              style={{
                width: 22, height: 22, border: `1px solid ${el.style?.[k] ? color : 'var(--dd-border, #d1d5db)'}`,
                borderRadius: 3, background: el.style?.[k] ? `rgba(${hexToRgb(color)},0.18)` : 'var(--dd-surface, #fff)',
                cursor: 'pointer', fontSize: 11,
                fontWeight: k === 'bold' ? 'bold' : 'normal',
                fontStyle: k === 'italic' ? 'italic' : 'normal',
                textDecoration: k === 'underline' ? 'underline' : 'none',
                color: el.style?.[k] ? color : 'var(--dd-text-muted, #555)',
              }}
            >{lbl}</button>
          ))}
        </div>
      </PRow>
      {/* 2026-05-30: Dar properties panel'inde ikon-rozet seti okunaksiz oluyordu;
          metin combobox'larina cevirdik. Hizalama + Dikey + Tasma uc satir da
          standart `<select>` (gi style) ile homojen gozukur. */}
      <PRow label="Hizalama">
        <select value={el.style?.align ?? 'left'} onChange={e => setStyle({ align: e.target.value })}
          style={{ ...gi, cursor: 'pointer' }}>
          <option value="left">Sol</option>
          <option value="center">Orta</option>
          <option value="right">Sağ</option>
          <option value="justify">İki Yana</option>
        </select>
      </PRow>
      <PRow label="Dikey">
        <select value={el.style?.verticalAlign ?? 'middle'} onChange={e => setStyle({ verticalAlign: e.target.value })}
          style={{ ...gi, cursor: 'pointer' }}>
          <option value="top">Üst</option>
          <option value="middle">Orta</option>
          <option value="bottom">Alt</option>
        </select>
      </PRow>
      {['Label','BoundField','AmountInWords'].includes(el.kind) && (
        <PRow label="Taşma">
          <select value={el.style?.overflow ?? 'ellipsis'} onChange={e => setStyle({ overflow: e.target.value })}
            style={{ ...gi, cursor: 'pointer' }}>
            <option value="ellipsis">Kes (…)</option>
            <option value="wrap">Sar</option>
            <option value="clip">Kes</option>
            <option value="shrink">Sığdır (yazıyı küçült)</option>
          </select>
        </PRow>
      )}
      <PRow label="Yazı Rengi">
        <div style={{ display: 'flex', alignItems: 'center', gap: 4, width: '100%' }}>
          <div style={{ width: 16, height: 16, borderRadius: 2, border: '1px solid #ddd', background: el.style?.color ?? '#000', flexShrink: 0 }} />
          <input type="color" value={el.style?.color ?? '#000000'}
            onChange={e => setStyle({ color: e.target.value })}
            style={{ width: 32, height: 20, border: 'none', padding: 0, cursor: 'pointer', background: 'none' }} />
          <span style={{ fontSize: 10, color: '#999', fontFamily: 'monospace' }}>{el.style?.color ?? '#000000'}</span>
        </div>
      </PRow>
      <PRow label="Arkaplan">
        <div style={{ display: 'flex', alignItems: 'center', gap: 4, width: '100%' }}>
          {el.style?.bgColor === 'transparent'
            ? <div style={{ width: 16, height: 16, borderRadius: 2, border: '1px solid #ddd', flexShrink: 0, backgroundImage: 'repeating-linear-gradient(45deg,#ccc,#ccc 2px,#fff 2px,#fff 6px)' }} />
            : <div style={{ width: 16, height: 16, borderRadius: 2, border: '1px solid #ddd', flexShrink: 0, background: el.style?.bgColor ?? '#fff' }} />
          }
          <input type="color"
            value={el.style?.bgColor === 'transparent' ? '#ffffff' : (el.style?.bgColor ?? '#ffffff')}
            onChange={e => setStyle({ bgColor: e.target.value })}
            style={{ width: 32, height: 20, border: 'none', padding: 0, cursor: 'pointer', background: 'none' }} />
          <label style={{ display: 'flex', gap: 3, alignItems: 'center', cursor: 'pointer', fontSize: 10, color: '#888' }}>
            <input type="checkbox" checked={el.style?.bgColor === 'transparent'}
              onChange={e => setStyle({ bgColor: e.target.checked ? 'transparent' : '#ffffff' })} />
            Şeffaf
          </label>
        </div>
      </PRow>
      {/* Kenarlik — etiketli 4 yön toggle (Üst / Sağ / Alt / Sol).
          Combobox kaldirildi; her buton bagimsiz toggle, secili kenar mavi vurgulanir. */}
      <PRow label="Kenarlık">
        {(() => {
          const legacyAll = el.style?.border === true
          const sides = {
            top:    el.style?.borderTop    ?? legacyAll,
            right:  el.style?.borderRight  ?? legacyAll,
            bottom: el.style?.borderBottom ?? legacyAll,
            left:   el.style?.borderLeft   ?? legacyAll,
          }
          const toggleSide = key => {
            const cur = sides[key]
            setStyle({
              border: undefined,
              [`border${key[0].toUpperCase() + key.slice(1)}`]: !cur,
            })
          }
          const sideBtn = (key, label, active) => (
            <button key={key} title={label}
              onClick={() => toggleSide(key)}
              style={{
                flex: 1, height: 22, padding: 0, cursor: 'pointer', fontSize: 11, fontWeight: 600,
                border: `1px solid ${active ? color : 'var(--dd-border, #d1d5db)'}`,
                background: active ? `rgba(${hexToRgb(color)},0.18)` : 'var(--dd-surface, #fff)',
                color: active ? color : 'var(--dd-text-muted, #555)',
                borderRadius: 3,
              }}>
              {label}
            </button>
          )
          return (
            <div style={{ display: 'flex', gap: 3, alignItems: 'center', width: '100%' }}>
              {sideBtn('top',    '↑ Üst', sides.top)}
              {sideBtn('right',  '→ Sağ', sides.right)}
              {sideBtn('bottom', '↓ Alt', sides.bottom)}
              {sideBtn('left',   '← Sol', sides.left)}
            </div>
          )
        })()}
      </PRow>

      {/* Resim ayarları */}
      {el.kind === 'Image' && (
        <>
          <PCategory title="Resim Kaynağı" color={color} />
          <PRow label="Resim">
            <div style={{ display: 'flex', gap: 4, width: '100%', alignItems: 'center' }}>
              <label style={{
                flex: 1, height: 20, display: 'flex', alignItems: 'center', justifyContent: 'center',
                fontSize: 10, fontWeight: 600, cursor: 'pointer', borderRadius: 3,
                border: `1px solid ${color}`,
                background: `rgba(${hexToRgb(color)},0.12)`,
                color,
              }}>
                {el.imageSrc ? 'Değiştir' : 'Yükle'}
                <input type="file" accept="image/*" style={{ display: 'none' }}
                  onChange={e => {
                    const file = e.target.files?.[0]
                    if (!file) return
                    if (file.size > 3 * 1024 * 1024) {
                      // Rapor §6.6 — toast fallback
                      const m = 'Resim 3MB üzerinde olamaz.'
                      if (window.CalibraAlert && window.CalibraAlert.warn) window.CalibraAlert.warn(m)
                      else if (window.CalibraHub && window.CalibraHub.toast) window.CalibraHub.toast(m, 'warn')
                      else alert(m)
                      e.target.value = ''
                      return
                    }
                    const reader = new FileReader()
                    reader.onload = ev => set({ imageSrc: ev.target.result })
                    reader.readAsDataURL(file)
                    e.target.value = ''
                  }} />
              </label>
              {el.imageSrc && (
                <button onClick={() => set({ imageSrc: null })}
                  title="Resmi temizle"
                  style={{
                    width: 22, height: 20, fontSize: 11, cursor: 'pointer', borderRadius: 3,
                    border: '1px solid #fca5a5', background: '#fef2f2', color: '#dc2626',
                  }}>
                  ×
                </button>
              )}
            </div>
          </PRow>
          {el.imageSrc && (
            <PRow label="Durum">
              <span style={{ fontSize: 10, color: '#10b981', fontWeight: 600 }}>
                ✓ Resim yüklü
              </span>
            </PRow>
          )}
          <PRow label="Sığdırma">
            <select value={el.imageFit ?? 'contain'} onChange={e => set({ imageFit: e.target.value })}
              style={{ ...gi, cursor: 'pointer' }}>
              <option value="contain">Sığdır (oran korunur)</option>
              <option value="stretch">Sıkıştır (kutuyu doldur)</option>
              <option value="original">Serbest (doğal boyut)</option>
            </select>
          </PRow>
        </>
      )}

      {/* Barkod ayarlari — QR ayri kind degil, barcodeType='QR' ile birlesik. */}
      {el.kind === 'Barcode' && (() => {
        const isQrType = el.barcodeType === 'QR'
        return (
          <>
            <PCategory title={isQrType ? 'QR Ayarları' : 'Barkod Ayarları'} color={color} />
            <PRow label="Barkod Tipi">
              <select value={el.barcodeType ?? 'Code128'} onChange={e => set({ barcodeType: e.target.value })}
                className="dd-select" style={{ ...gi, cursor: 'pointer' }}>
                {BARCODE_TYPES.map(t => <option key={t.value} value={t.value}>{t.label}</option>)}
              </select>
            </PRow>
            {!isQrType && (
              <PRow label="Yazıyı Göster">
                <input type="checkbox" checked={el.showBarcodeText ?? true}
                  onChange={e => set({ showBarcodeText: e.target.checked })} />
              </PRow>
            )}
            {isQrType && (
              <PRow label="Hata Düzeltme">
                <select value={el.qrErrorCorrection ?? 'M'} onChange={e => set({ qrErrorCorrection: e.target.value })}
                  className="dd-select" style={{ ...gi, cursor: 'pointer' }}>
                  <option value="L">L — %7</option>
                  <option value="M">M — %15</option>
                  <option value="Q">Q — %25</option>
                  <option value="H">H — %30</option>
                </select>
              </PRow>
            )}
            <PRow label="Alias">
              <input value={el.binding?.alias ?? ''} onChange={e => setBinding({ alias: e.target.value })} style={gi}
                placeholder={isQrType ? 'Header' : 'Header'} />
            </PRow>
            <PRow label="Kolon">
              <input value={el.binding?.col ?? ''} onChange={e => setBinding({ col: e.target.value })} style={gi}
                placeholder={isQrType ? 'QrValue' : 'BarcodeValue'} />
            </PRow>
            <PRow label="Sabit Değer">
              <input value={el.text ?? ''} onChange={e => set({ text: e.target.value })} style={gi} placeholder="(binding boşsa)" />
            </PRow>
            <div style={{ padding: '6px 10px', fontSize: 10.5, color: 'var(--dd-text-muted, #94a3b8)', lineHeight: 1.4 }}>
              💡 Veriyi seçmek için barkod elementine <strong>çift tıklayın</strong> — kolon ağacından seçim yapabilirsiniz.
            </div>
          </>
        )
      })()}

      {/* Veri Bağlama */}
      {(el.kind === 'Label' || el.kind === 'BoundField' || el.kind === 'AmountInWords') && (
        <>
          <PCategory title={el.kind === 'AmountInWords' ? 'Tutar Kaynağı' : 'Veri Bağlama'} color={color} />
          {el.kind === 'Label' && (
            <PRow label="İçerik">
              <textarea value={el.text ?? ''} rows={2}
                onChange={e => set({ text: e.target.value })}
                style={{ ...gi, resize: 'vertical', paddingTop: 2 }} />
            </PRow>
          )}
          {(el.kind === 'BoundField' || el.kind === 'AmountInWords') && (
            <>
              <PRow label="Alias">
                <input value={el.binding?.alias ?? ''} onChange={e => setBinding({ alias: e.target.value })} style={gi} placeholder="Header" />
              </PRow>
              <PRow label="Kolon">
                <input value={el.binding?.col ?? ''} onChange={e => setBinding({ col: e.target.value })} style={gi} placeholder="Toplam" />
              </PRow>
              {el.kind === 'BoundField' && (
                <PRow label="Format">
                  <input value={el.format ?? ''} placeholder="#,##0.00" onChange={e => set({ format: e.target.value || null })} style={gi} />
                </PRow>
              )}
              {el.kind === 'AmountInWords' && (
                <PRow label="Para Birimi">
                  <select value={el.currencyLabel ?? 'Türk Lirası'} onChange={e => set({ currencyLabel: e.target.value })}
                    style={{ ...gi, cursor: 'pointer' }}>
                    <option value="Türk Lirası">Türk Lirası</option>
                    <option value="Euro">Euro</option>
                    <option value="US Dollar">US Dollar</option>
                  </select>
                </PRow>
              )}
            </>
          )}
        </>
      )}

      <PCategory title="Davranış" color="#6b7280" />
      <PRow label="Görünür">
        <input type="checkbox" checked={el.visible !== false}
          onChange={e => set({ visible: e.target.checked })}
          title="Hem tasarım hem çıktıda gizle/göster" />
      </PRow>
      <PRow label="Yazdırılabilir">
        <input type="checkbox" checked={el.printable !== false}
          onChange={e => set({ printable: e.target.checked })}
          title="Tasarımda görünür ama PDF/baskıda yer almaz (yardım notları için)" />
      </PRow>
      <PRow label="Döndürme (°)">
        <div style={{ display: 'flex', gap: 3, alignItems: 'center', width: '100%' }}>
          {[0, 90, 180, 270].map(deg => {
            const active = (el.rotation ?? 0) === deg
            return (
              <button key={deg}
                onClick={() => set({ rotation: deg })}
                style={{
                  flex: 1, height: 20, fontSize: 9, fontWeight: 700, cursor: 'pointer',
                  border: `1px solid ${active ? '#6b7280' : 'var(--dd-border, #d1d5db)'}`,
                  background: active ? 'rgba(107,114,128,0.18)' : 'var(--dd-surface, #fff)',
                  color: active ? '#6b7280' : 'var(--dd-text-muted, #555)', borderRadius: 3,
                }}>
                {deg}°
              </button>
            )
          })}
          <input type="number" value={el.rotation ?? 0}
            onChange={e => set({ rotation: +e.target.value || 0 })}
            style={{ ...gi, width: 36, textAlign: 'center' }}
            title="Serbest derece" />
        </div>
      </PRow>
      {(el.kind === 'BoundField' || el.kind === 'AmountInWords') && (
        <>
          <PRow label="Tekrarı Bastır">
            <input type="checkbox" checked={el.suppressRepeated ?? false}
              onChange={e => set({ suppressRepeated: e.target.checked })}
              title="Aynı değer ardışık satırlarda tekrar etmesin (örn. her detayda belge no görünmesin)" />
          </PRow>
          <PRow label="Sıfırları Gizle">
            <input type="checkbox" checked={el.hideZeros ?? false}
              onChange={e => set({ hideZeros: e.target.checked })}
              title="Sayı değeri 0 ise boş göster" />
          </PRow>
        </>
      )}

      <PCategory title="Gelişmiş" color="#6b7280" />
      <PRow label="Z-Index" last>
        <input type="number" value={el.zIndex ?? 0} onChange={e => set({ zIndex: +e.target.value })} style={gi} />
      </PRow>

      <div style={{ padding: '10px 10px 12px' }}>
        <button
          onClick={() => dispatch({ type: 'DELETE_ELEMENT', elementIds: [el.id] })}
          style={{
            width: '100%', border: '1px solid #fca5a5', borderRadius: 5, padding: '6px 0',
            background: '#fef2f2', color: '#dc2626', cursor: 'pointer', fontSize: 11, fontWeight: 500,
          }}
        >
          Element Sil
        </button>
      </div>
    </>
  )
}
