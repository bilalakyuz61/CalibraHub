import React, { useRef, useState } from 'react'
import { Stage, Layer, Line } from 'react-konva'
import KonvaElement from './KonvaElement'
import { mmToPx, pxToMm } from './DesignerCanvas'
import { makeDefaultElement } from '../designerReducer'

// Tint alpha 0.05-0.07 idi → her iki temada da çok soluk kalıyordu (kullanıcı raporu).
// 0.12'ye yükseltildi: light mode'da hafif renk imzası, dark mode'da görünürlük arttı.
const BAND_TINT = {
  PageHeader:        'rgba(99,102,241,0.12)',
  DocumentHeader:    'rgba(245,158,11,0.12)',
  TableHeader:       'rgba(16,185,129,0.13)',
  Detail:            'rgba(139,92,246,0.11)',
  SubDetailHeader:   'rgba(34,211,238,0.13)',
  SubDetail:         'rgba(6,182,212,0.12)',
  SubDetailFooter:   'rgba(14,116,144,0.13)',
  TotalsBlock:       'rgba(236,72,153,0.12)',
  SignatureBlock:    'rgba(59,130,246,0.11)',
  PageFooter:        'rgba(107,114,128,0.12)',
  mail_body:         'rgba(99,102,241,0.18)',
}
const BAND_BORDER = {
  PageHeader:        'rgba(99,102,241,0.55)',
  DocumentHeader:    'rgba(245,158,11,0.55)',
  TableHeader:       'rgba(16,185,129,0.55)',
  Detail:            'rgba(139,92,246,0.50)',
  SubDetailHeader:   'rgba(34,211,238,0.55)',
  SubDetail:         'rgba(6,182,212,0.50)',
  SubDetailFooter:   'rgba(14,116,144,0.55)',
  TotalsBlock:       'rgba(236,72,153,0.55)',
  SignatureBlock:    'rgba(59,130,246,0.50)',
  PageFooter:        'rgba(107,114,128,0.55)',
  mail_body:         'rgba(99,102,241,0.65)',
}
const BAND_SOLID = {
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
  mail_body:         '#6366f1',
}
const BAND_LABELS = {
  PageHeader:        'Sayfa Başlığı',
  DocumentHeader:    'Belge Başlığı',
  TableHeader:       'Tablo Başlığı',
  Detail:            'Detay Satırı',
  SubDetailHeader:   'Alt Detay Başlığı',
  SubDetail:         'Alt Detay Satırı',
  SubDetailFooter:   'Alt Detay Altı',
  TotalsBlock:       'Toplam Bloku',
  SignatureBlock:    'İmza Bloku',
  PageFooter:        'Sayfa Altı',
  mail_body:         'Mail Gövdesi (gönderim sırasında doldurulacak)',
}

export default function BandContainer({ band, innerW, zoom, selectedElementId, selectedElementIds, selectedBandId, dispatch, crossBandXTargets = [] }) {
  const bandUnitH  = mmToPx(band.height)
  const bandCssH   = bandUnitH * zoom
  const isSelected = selectedBandId === band.id && !selectedElementId
  const [resizing, setResizing] = useState(false)
  const [dragGuides, setDragGuides] = useState(null)   // { x: number|null, y: number|null }
  const startY = useRef(0)
  const startH = useRef(0)

  const handleResizeStart = e => {
    e.stopPropagation()
    setResizing(true)
    startY.current = e.clientY
    startH.current = band.height
    const onMove = ev => {
      const delta = pxToMm((ev.clientY - startY.current) / zoom)
      dispatch({ type: 'RESIZE_BAND', bandId: band.id, height: Math.max(4, startH.current + delta) })
    }
    const onUp = () => {
      setResizing(false)
      window.removeEventListener('pointermove', onMove)
      window.removeEventListener('pointerup', onUp)
    }
    window.addEventListener('pointermove', onMove)
    window.addEventListener('pointerup', onUp)
  }

  const handleDrop = e => {
    e.preventDefault()
    const kind       = e.dataTransfer.getData('element-kind')
    const rawBind    = e.dataTransfer.getData('element-binding')
    const rawSnippet = e.dataTransfer.getData('element-snippet')
    if (!kind) return
    const rect     = e.currentTarget.getBoundingClientRect()
    const stageTop = e.currentTarget.querySelector('canvas')?.getBoundingClientRect().top ?? rect.top
    const xPx = (e.clientX - rect.left) / zoom
    const yPx = (e.clientY - stageTop)  / zoom
    const binding = rawBind ? JSON.parse(rawBind) : null
    const el = makeDefaultElement(kind, pxToMm(xPx), pxToMm(yPx), binding)

    // Hazir snippet payload'i: text + style override + boyut override.
    // ElementPalette "Hazir Sablonlar" section'undan gelir (Sayin, Tarih, vb.).
    if (rawSnippet) {
      try {
        const snip = JSON.parse(rawSnippet)
        if (snip.text != null) el.text = snip.text
        if (snip.style && typeof snip.style === 'object') el.style = { ...el.style, ...snip.style }
        if (snip.w) el.w = snip.w
        if (snip.h) el.h = snip.h
      } catch { /* malformed snippet payload — Label default'la birak */ }
    }
    dispatch({ type: 'ADD_ELEMENT', bandId: band.id, element: el })
  }

  const sorted = [...band.elements].sort((a, b) => a.zIndex - b.zIndex)
  const multiSet = new Set(selectedElementIds ?? [])
  const tint   = BAND_TINT[band.type]   ?? 'rgba(99,102,241,0.05)'
  const bColor = BAND_BORDER[band.type] ?? 'rgba(99,102,241,0.4)'
  const sColor = BAND_SOLID[band.type]  ?? '#6366f1'
  const bLabel = BAND_LABELS[band.type] ?? band.type

  return (
    <div style={{ position: 'relative', marginBottom: 0 }}>
      {/* Bant etiket şeridi (band header strip) */}
      <div
        onClick={() => dispatch({ type: 'SELECT_BAND', bandId: band.id })}
        style={{
          height: 16, display: 'flex', alignItems: 'center',
          padding: '0 7px', gap: 6, cursor: 'pointer',
          background: sColor, color: '#fff',
          fontSize: 9.5, fontWeight: 700, letterSpacing: 0.4,
          textTransform: 'uppercase', lineHeight: 1,
          borderTop: isSelected ? `2px solid ${sColor}` : 'none',
          boxShadow: isSelected ? `inset 0 0 0 1px rgba(255,255,255,0.5)` : 'none',
        }}
      >
        <span>{bLabel}</span>
        {band.dataAlias && (
          <span style={{
            fontSize: 9, fontWeight: 700, textTransform: 'none', letterSpacing: 0.2,
            padding: '1px 5px', borderRadius: 3,
            background: 'rgba(255,255,255,0.25)', color: '#fff',
          }}>
            ⛓ {band.dataAlias}
          </span>
        )}
        <span style={{ marginLeft: 'auto', opacity: 0.7, fontSize: 9, fontWeight: 500, textTransform: 'none', letterSpacing: 0 }}>
          {band.height.toFixed(1)} mm
        </span>
      </div>

      <div
        onDrop={handleDrop}
        onDragOver={e => e.preventDefault()}
        style={{
          background: isSelected ? 'var(--dd-accent-soft, #f5f3ff)' : tint,
          borderBottom: `1px dashed ${isSelected ? 'var(--dd-accent, #6366f1)' : bColor}`,
          outline: isSelected ? `1.5px solid ${sColor}` : 'none',
          outlineOffset: -1,
          boxSizing: 'border-box',
          lineHeight: 0,
          position: 'relative',
        }}
      >
        {/* mail_body bandi placeholder — element olmaz, runtime mail govdesi ile doldurulur */}
        {band.type === 'mail_body' && (
          <div style={{
            position: 'absolute', inset: 0,
            display: 'flex', alignItems: 'center', justifyContent: 'center',
            fontSize: 12, color: '#6366f1', fontStyle: 'italic',
            pointerEvents: 'none', letterSpacing: 0.3, textAlign: 'center', padding: '0 20px',
            lineHeight: 1.5,
          }}>
            ✉ Mail gövdesi (gönderim sırasında doldurulacak)
          </div>
        )}
        <Stage
          width={innerW * zoom}
          height={bandCssH}
          scaleX={zoom}
          scaleY={zoom}
          onClick={() => dispatch({ type: 'SELECT_BAND', bandId: band.id })}
        >
          <Layer>
            {sorted.map(el => (
              <KonvaElement
                key={el.id}
                el={el}
                bandId={band.id}
                isSelected={selectedElementId === el.id}
                isMultiSelected={multiSet.has(el.id)}
                dispatch={dispatch}
                zoom={zoom}
                bandWidthPx={innerW}
                bandHeightPx={bandUnitH}
                siblings={band.elements.filter(o => o.id !== el.id)}
                extraXTargets={crossBandXTargets}
                onDragGuide={setDragGuides}
              />
            ))}
            {/* Snap guide çizgileri — sürükleme sırasında hizalama yakalandığında */}
            {dragGuides?.x != null && (
              <Line
                points={[dragGuides.x, 0, dragGuides.x, bandUnitH]}
                stroke="#ec4899" strokeWidth={1} dash={[3, 3]}
                strokeScaleEnabled={false}
                listening={false}
              />
            )}
            {dragGuides?.y != null && (
              <Line
                points={[0, dragGuides.y, innerW, dragGuides.y]}
                stroke="#ec4899" strokeWidth={1} dash={[3, 3]}
                strokeScaleEnabled={false}
                listening={false}
              />
            )}
          </Layer>
        </Stage>
      </div>

      <div
        onPointerDown={handleResizeStart}
        style={{
          position: 'absolute', bottom: -3, left: 0, right: 0, height: 6,
          cursor: 'ns-resize', zIndex: 10,
          background: resizing ? 'rgba(99,102,241,0.15)' : 'transparent',
          display: 'flex', alignItems: 'center', justifyContent: 'center',
        }}
      >
        <div style={{ width: 32, height: 2, borderRadius: 1, background: resizing ? 'var(--dd-accent, #6366f1)' : 'var(--dd-border, #d1d5db)' }} />
      </div>
    </div>
  )
}
