import React, { useRef, useState, useCallback, useEffect } from 'react'
import BandContainer from './BandContainer'
import { HRuler, VRuler, RULER_SIZE_PX } from './Ruler'
import { BAND_ORDER } from '../designerReducer'

export const MM_TO_PX = 96 / 25.4   // ≈ 3.7795
export const mmToPx = mm => mm * MM_TO_PX
export const pxToMm = px => px / MM_TO_PX

// Bant sirasi designerReducer'dan import edilir (single source of truth).
const bandSort = b => { const i = BAND_ORDER.indexOf(b.type); return i === -1 ? 99 : i }

const CANVAS_PAD_LEFT = 24   // narrow gutter (band labels now on top of each band)
const CANVAS_PAD      = 40   // top / right / bottom

function MarginHandle({ side, pos, zoom, meta, dispatch }) {
  const [hovered, setHovered] = React.useState(false)
  const [dragging, setDragging] = React.useState(false)
  const isHorizontal = side === 'top' || side === 'bottom'

  const onDown = e => {
    e.preventDefault()
    e.stopPropagation()
    setDragging(true)
    const startClient = isHorizontal ? e.clientY : e.clientX
    const startMm = side === 'top' ? meta.marginTop : side === 'bottom' ? meta.marginBot : side === 'left' ? meta.marginLeft : meta.marginRight

    const onMove = ev => {
      const cur = isHorizontal ? ev.clientY : ev.clientX
      const sign = (side === 'bottom' || side === 'right') ? -1 : 1
      const deltaMm = ((cur - startClient) * sign) / zoom / MM_TO_PX
      const next = Math.max(0, Math.round((startMm + deltaMm) * 10) / 10)
      const key = side === 'top' ? 'marginTop' : side === 'bottom' ? 'marginBot' : side === 'left' ? 'marginLeft' : 'marginRight'
      dispatch({ type: 'SET_META', payload: { [key]: next } })
    }
    const onUp = () => {
      setDragging(false)
      window.removeEventListener('pointermove', onMove)
      window.removeEventListener('pointerup', onUp)
    }
    window.addEventListener('pointermove', onMove)
    window.addEventListener('pointerup', onUp)
  }

  const baseStyle = {
    position: 'absolute', zIndex: 5,
    background: dragging || hovered ? 'rgba(99,102,241,0.35)' : 'transparent',
    transition: 'background 0.1s',
  }
  const indicatorStyle = {
    position: 'absolute', background: 'var(--dd-accent, #6366f1)',
    opacity: dragging || hovered ? 1 : 0,
    transition: 'opacity 0.1s',
  }

  if (isHorizontal) {
    return (
      <div
        onPointerDown={onDown}
        onMouseEnter={() => setHovered(true)}
        onMouseLeave={() => setHovered(false)}
        style={{ ...baseStyle, left: 0, right: 0, top: pos - 4, height: 8, cursor: 'ns-resize' }}
        title={`${side === 'top' ? 'Üst' : 'Alt'} kenar boşluğu — sürükleyerek ayarla`}
      >
        <div style={{ ...indicatorStyle, left: 0, right: 0, top: 3, height: 2 }} />
        {(dragging || hovered) && (
          <div style={{ position: 'absolute', left: 6, top: -16, fontSize: 10, fontWeight: 700, color: 'var(--dd-accent)', background: 'var(--dd-surface, #fff)', padding: '1px 5px', borderRadius: 3, border: '1px solid var(--dd-accent)' }}>
            {(side === 'top' ? meta.marginTop : meta.marginBot).toFixed(1)} mm
          </div>
        )}
      </div>
    )
  }
  return (
    <div
      onPointerDown={onDown}
      onMouseEnter={() => setHovered(true)}
      onMouseLeave={() => setHovered(false)}
      style={{ ...baseStyle, top: 0, bottom: 0, left: pos - 4, width: 8, cursor: 'ew-resize' }}
      title={`${side === 'left' ? 'Sol' : 'Sağ'} kenar boşluğu — sürükleyerek ayarla`}
    >
      <div style={{ ...indicatorStyle, top: 0, bottom: 0, left: 3, width: 2 }} />
      {(dragging || hovered) && (
        <div style={{ position: 'absolute', top: 4, left: -2, fontSize: 10, fontWeight: 700, color: 'var(--dd-accent)', background: 'var(--dd-surface, #fff)', padding: '1px 5px', borderRadius: 3, border: '1px solid var(--dd-accent)', whiteSpace: 'nowrap' }}>
          {(side === 'left' ? meta.marginLeft : meta.marginRight).toFixed(1)} mm
        </div>
      )}
    </div>
  )
}

export default function DesignerCanvas({ state, dispatch, zoom, onZoomChange }) {
  const { meta, bands, selectedElementId, selectedElementIds, selectedBandId } = state
  const [mouseMm, setMouseMm] = useState(null)
  const [isDark, setIsDark] = useState(() => document.body.classList.contains('app-theme-dark'))
  const scrollRef = useRef(null)

  useEffect(() => {
    const obs = new MutationObserver(() => setIsDark(document.body.classList.contains('app-theme-dark')))
    obs.observe(document.body, { attributes: true, attributeFilter: ['class'] })
    return () => obs.disconnect()
  }, [])

  // Paper dimensions in Stage coordinate units (unscaled by zoom)
  const pageW  = mmToPx(meta.pageW)
  const pageH  = mmToPx(meta.pageH)
  const padT   = mmToPx(meta.marginTop)
  const padB   = mmToPx(meta.marginBot)
  const padL   = mmToPx(meta.marginLeft)
  const padR   = mmToPx(meta.marginRight)
  const innerW = pageW - padL - padR

  // CSS pixel sizes (what the browser lays out)
  const paperCssW = pageW * zoom
  const paperCssH = pageH * zoom

  const scrollAreaW = paperCssW + CANVAS_PAD_LEFT + CANVAS_PAD
  const scrollAreaH = paperCssH + CANVAS_PAD * 2

  const sorted = [...bands].sort((a, b) => bandSort(a) - bandSort(b))

  // Cross-band X snap target'ları — tüm bantların elementlerinin x/center/right
  // pozisyonları (px). Bantlar Y'leri farklı stage'lerde olduğu için Y bant-yerel
  // ama X tüm tasarım boyunca aynı referansı kullanır → kolon hizalama için ideal.
  const crossBandXTargets = React.useMemo(() => {
    const targets = []
    for (const band of bands) {
      for (const el of (band.elements ?? [])) {
        const x = mmToPx(el.x ?? 0)
        const w = mmToPx(el.w ?? 0)
        targets.push(x)
        targets.push(x + w / 2)
        targets.push(x + w)
      }
    }
    return targets
  }, [bands])

  const handleWheel = useCallback(e => {
    if (!e.ctrlKey) return
    e.preventDefault()
    onZoomChange(e.deltaY < 0 ? 0.1 : -0.1)
  }, [onZoomChange])

  const handleMouseMove = useCallback(e => {
    const el = scrollRef.current
    if (!el) return
    const rect = el.getBoundingClientRect()
    const xInScroll = e.clientX - rect.left  + el.scrollLeft - RULER_SIZE_PX
    const yInScroll = e.clientY - rect.top   + el.scrollTop  - RULER_SIZE_PX
    // Convert CSS pixel offset from paper origin → mm
    setMouseMm({
      x: (xInScroll - CANVAS_PAD_LEFT) / zoom / MM_TO_PX,
      y: (yInScroll - CANVAS_PAD)      / zoom / MM_TO_PX,
    })
  }, [zoom])

  return (
    <div style={{ flex: 1, overflow: 'hidden', display: 'flex', flexDirection: 'column', background: 'var(--dd-bg, #eef0f5)' }}>
      {/* Top ruler row */}
      <div style={{ display: 'flex', flexShrink: 0 }}>
        <div style={{ width: RULER_SIZE_PX, height: RULER_SIZE_PX, background: 'var(--dd-ruler-bg, #f8f9fb)', borderRight: '1px solid var(--dd-border, #e5e7eb)', borderBottom: '1px solid var(--dd-border, #e5e7eb)', flexShrink: 0 }} />
        <HRuler widthPx={scrollAreaW} zoom={zoom} mouseMm={mouseMm} isDark={isDark} />
      </div>

      {/* Canvas body */}
      <div style={{ flex: 1, overflow: 'hidden', display: 'flex' }}>
        <div style={{ flexShrink: 0, overflow: 'hidden' }}>
          <VRuler heightPx={scrollAreaH} zoom={zoom} mouseMm={mouseMm} isDark={isDark} />
        </div>

        {/* Scrollable area */}
        <div
          ref={scrollRef}
          style={{ flex: 1, overflow: 'auto', position: 'relative' }}
          onWheel={handleWheel}
          onMouseMove={handleMouseMove}
          onMouseLeave={() => setMouseMm(null)}
          onClick={e => { if (e.target === e.currentTarget) dispatch({ type: 'DESELECT' }) }}
        >
          {/* Sizing container — must match paper visual size so scroll works */}
          <div style={{ width: scrollAreaW, height: scrollAreaH, position: 'relative' }}>

            {/*
              Paper — NO CSS transform: scale().
              Zoom is handled by each BandContainer's Konva Stage (scaleX/scaleY).
              Paper CSS size = unscaled px * zoom, so scroll area accounts for visual size.
            */}
            <div
              style={{
                position: 'absolute',
                left: CANVAS_PAD_LEFT,
                top:  CANVAS_PAD,
                width:  paperCssW,
                minHeight: paperCssH,
                background: 'var(--dd-paper-bg, #ffffff)',
                boxShadow: isDark
                  ? '0 0 0 1px rgba(255,255,255,0.05), 0 4px 24px rgba(0,0,0,0.6)'
                  : '0 2px 16px rgba(0,0,0,0.10), 0 1px 4px rgba(0,0,0,0.06)',
                borderRadius: 2,
                // Padding scales with zoom so margins visually match
                paddingTop:    padT * zoom,
                paddingBottom: padB * zoom,
                paddingLeft:   padL * zoom,
                paddingRight:  padR * zoom,
                boxSizing: 'border-box',
                // Flex column → PageFooter bandı 'marginTop: auto' ile alta yapışabilsin
                display: 'flex',
                flexDirection: 'column',
              }}
              onClick={e => { if (e.target === e.currentTarget) dispatch({ type: 'DESELECT' }) }}
            >
              {/* Margin guide — absolute inside paper, accounting for scaled padding */}
              <div style={{
                position: 'absolute',
                top:    padT * zoom,
                left:   padL * zoom,
                right:  padR * zoom,
                bottom: padB * zoom,
                border: '1px dashed rgba(99,102,241,0.25)',
                pointerEvents: 'none', zIndex: 0,
              }} />

              {/* Margin drag handles */}
              <MarginHandle side="top"    pos={padT * zoom}                  zoom={zoom} meta={meta} dispatch={dispatch} />
              <MarginHandle side="bottom" pos={paperCssH - padB * zoom}      zoom={zoom} meta={meta} dispatch={dispatch} />
              <MarginHandle side="left"   pos={padL * zoom}                  zoom={zoom} meta={meta} dispatch={dispatch} />
              <MarginHandle side="right"  pos={paperCssW - padR * zoom}      zoom={zoom} meta={meta} dispatch={dispatch} />

              {sorted.map(band => (
                <div
                  key={band.id}
                  style={band.type === 'PageFooter' ? { marginTop: 'auto' } : undefined}
                >
                  <BandContainer
                    band={band}
                    innerW={innerW}
                    zoom={zoom}
                    selectedElementId={selectedElementId}
                    selectedElementIds={selectedElementIds}
                    selectedBandId={selectedBandId}
                    dispatch={dispatch}
                    crossBandXTargets={crossBandXTargets}
                  />
                </div>
              ))}

              {bands.length === 0 && (
                <div style={{
                  position: 'absolute', inset: 0,
                  display: 'flex', alignItems: 'center', justifyContent: 'center',
                  color: '#9ca3af', fontSize: 13, pointerEvents: 'none',
                }}>
                  ← Sol panelden bant ekleyin
                </div>
              )}
            </div>
          </div>
        </div>
      </div>
    </div>
  )
}
