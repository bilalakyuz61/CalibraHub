import React from 'react'
import GridLayout, { WidthProvider } from 'react-grid-layout'
import 'react-grid-layout/css/styles.css'
import 'react-resizable/css/styles.css'

// Genişliği otomatik ölçen serbest ızgara (sürükle + boyutlandır)
const RGL = WidthProvider(GridLayout)

export const GRID_COLS = 12
export const ROW_HEIGHT = 26

const HEIGHT_ROWS = { normal: 7, tall: 11, full: 16 }

// Eski tasarımlara (layout'suz panellere) serbest-ızgara koordinatı ata.
// Layout'u olan panelleri korur; eksik olanları alttan akıtarak yerleştirir.
export function ensureLayouts(pages) {
  return (pages || []).map(pg => {
    const list = pg.panels || []
    let maxBottom = list.reduce(
      (m, p) => (p.layout && Number.isFinite(p.layout.y)) ? Math.max(m, p.layout.y + (p.layout.h || 7)) : m, 0)
    let cx = 0, rowH = 0
    const out = list.map(p => {
      if (p.layout && Number.isFinite(p.layout.w)) return p
      const w = Math.min(12, (p.colSpan || 1) * 3)
      const h = HEIGHT_ROWS[p.panelHeight] || (p.type === 'filter' ? 8 : 7)
      if (cx + w > 12) { cx = 0; maxBottom += rowH; rowH = 0 }
      const layout = { x: cx, y: maxBottom, w, h }
      cx += w; rowH = Math.max(rowH, h)
      return { ...p, layout }
    })
    return { ...pg, panels: out }
  })
}

// Yeni panel için sayfanın altına yerleşecek layout
export function nextPanelLayout(panels, type) {
  const maxBottom = (panels || []).reduce((m, p) => Math.max(m, (p.layout?.y || 0) + (p.layout?.h || 7)), 0)
  return type === 'filter'
    ? { x: 0, y: maxBottom, w: 3, h: 8 }
    : { x: 0, y: maxBottom, w: 6, h: 7 }
}

// Eski tasarımlara (page.source yoksa) panellerin baskın kaynağından sayfa kaynağı türet
export function ensurePageSource(pages) {
  return (pages || []).map(pg => {
    if (pg.source && (pg.source.source || pg.source.sqlQuery || pg.source.sourceId)) return pg
    const list = pg.panels || []
    let src = { sourceType: 'view', source: '', sourceLabel: '', sqlQuery: '', sourceId: null, sourceName: '' }
    const counts = {}; let best = null, bestN = 0
    list.forEach(p => {
      if ((p.sourceType || 'view') === 'view' && p.source) {
        counts[p.source] = (counts[p.source] || 0) + 1
        if (counts[p.source] > bestN) { bestN = counts[p.source]; best = p }
      }
    })
    if (best) src = { ...src, sourceType: 'view', source: best.source, sourceLabel: best.sourceLabel || best.source }
    else {
      const sv = list.find(p => p.sourceType === 'saved' && p.sourceId)
      if (sv) src = { ...src, sourceType: 'saved', sourceId: sv.sourceId, sourceName: sv.sourceName }
      else {
        const sq = list.find(p => p.sourceType === 'sql' && p.sqlQuery)
        if (sq) src = { ...src, sourceType: 'sql', sqlQuery: sq.sqlQuery }
      }
    }
    return { ...pg, source: src }
  })
}

function panelLayout(p) {
  const l = p.layout || {}
  return {
    i: p.id,
    x: Number.isFinite(l.x) ? l.x : 0,
    y: Number.isFinite(l.y) ? l.y : 0,
    w: Number.isFinite(l.w) ? l.w : 6,
    h: Number.isFinite(l.h) ? l.h : 7,
    minW: 2,
    minH: 3,
  }
}

/**
 * Serbest yerleşim ızgarası — hem designer (editable) hem viewer (salt-okunur).
 * panels: layout taşıyan panel listesi.
 * renderPanel(panel): kart içeriğini döndürür (yüksekliği %100 doldurmalı).
 * onLayoutChange(layoutArray): sürükleme/boyutlandırma bitince çağrılır.
 */
export default function ReportGrid({ panels, editable, onLayoutChange, renderPanel }) {
  const layout = panels.map(panelLayout)

  return (
    <RGL
      className="rd-rgl"
      layout={layout}
      cols={GRID_COLS}
      rowHeight={ROW_HEIGHT}
      margin={[12, 12]}
      containerPadding={[0, 0]}
      isDraggable={!!editable}
      isResizable={!!editable}
      isDroppable={false}
      compactType="vertical"
      preventCollision={false}
      draggableHandle=".rd-panel__grip"
      draggableCancel=".rd-panel__del,.rd-filterp,.rv-panel__chart,input,select,textarea,button"
      resizeHandles={['se']}
      onDragStop={l => onLayoutChange && onLayoutChange(l)}
      onResizeStop={l => onLayoutChange && onLayoutChange(l)}
    >
      {panels.map(p => (
        <div key={p.id} className="rd-gi">
          {renderPanel(p)}
        </div>
      ))}
    </RGL>
  )
}
