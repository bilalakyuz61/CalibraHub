import React, { useRef, useCallback } from 'react'
import { Stage, Layer } from 'react-konva'
import KonvaElement from './KonvaElement'
import { mmToPx, pxToMm } from './DesignerCanvas'
import { BAND_TYPES } from '../designerReducer'

export default function BandContainer({ band, dispatch, selectedElementId, selectedBandId, contentWidthMm, dataSources }) {
  const bandDef = BAND_TYPES.find(b => b.type === band.type)
  const label = bandDef?.label ?? band.type
  const heightPx = mmToPx(band.height)
  const widthPx = mmToPx(contentWidthMm)
  const isSelected = selectedBandId === band.id
  const resizingRef = useRef(null)

  // Band yüksekliği yeniden boyutlandırma
  const onResizeStart = useCallback((e) => {
    e.preventDefault()
    const startY = e.clientY
    const startH = band.height

    function onMove(ev) {
      const deltaY = ev.clientY - startY
      const newH = Math.max(5, startH + pxToMm(deltaY))
      dispatch({ type: 'RESIZE_BAND', bandId: band.id, height: newH })
    }
    function onUp() {
      document.removeEventListener('pointermove', onMove)
      document.removeEventListener('pointerup', onUp)
    }
    document.addEventListener('pointermove', onMove)
    document.addEventListener('pointerup', onUp)
  }, [band.id, band.height, dispatch])

  // Toolbox'tan element bırakma
  const onDropElement = useCallback((e) => {
    e.preventDefault()
    const kind = e.dataTransfer?.getData('element-kind')
    if (!kind) return
    const rect = e.currentTarget.getBoundingClientRect()
    const x = pxToMm(e.clientX - rect.left)
    const y = pxToMm(e.clientY - rect.top)
    dispatch({ type: 'ADD_ELEMENT', bandId: band.id, kind, x, y })
  }, [band.id, dispatch])

  const onDragOver = (e) => e.preventDefault()

  const handleBandClick = useCallback((e) => {
    if (e.target === e.currentTarget)
      dispatch({ type: 'SELECT_BAND', bandId: band.id })
  }, [band.id, dispatch])

  return (
    <div
      className="dd-band"
      style={{
        position: 'relative',
        borderTop: '1px solid',
        borderTopColor: isSelected ? '#6366f1' : '#ddd',
        marginBottom: 0,
      }}
      onClick={handleBandClick}
    >
      {/* Band başlık etiketi */}
      <div style={{
        display: 'flex', alignItems: 'center', justifyContent: 'space-between',
        background: isSelected ? '#ede9fe' : '#f5f5f5',
        borderBottom: '1px solid #e0e0e0',
        padding: '2px 6px',
        fontSize: 11,
        color: '#666',
        userSelect: 'none',
        cursor: 'pointer'
      }}
        onClick={() => dispatch({ type: 'SELECT_BAND', bandId: band.id })}
      >
        <span style={{ fontWeight: 600, color: isSelected ? '#6366f1' : '#555' }}>{label}</span>
        <div style={{ display: 'flex', gap: 4 }}>
          {band.dataAlias && (
            <span style={{ background: '#e0f2fe', color: '#0284c7', borderRadius: 3, padding: '0 4px', fontSize: 10 }}>
              {band.dataAlias}
            </span>
          )}
          <button
            style={{ background: 'none', border: 'none', cursor: 'pointer', color: '#ef4444', fontSize: 12, padding: '0 2px' }}
            title="Bandı sil"
            onClick={(e) => { e.stopPropagation(); dispatch({ type: 'REMOVE_BAND', bandId: band.id }) }}
          >✕</button>
        </div>
      </div>

      {/* Konva canvas — element katmanı */}
      <div
        style={{ position: 'relative', height: heightPx, overflow: 'hidden' }}
        onDrop={onDropElement}
        onDragOver={onDragOver}
      >
        <Stage
          width={widthPx}
          height={heightPx}
          onClick={(e) => {
            if (e.target === e.target.getStage()) dispatch({ type: 'DESELECT' })
          }}
        >
          <Layer>
            {/* z-index sırası: elements dizisindeki sıra = z-index (JSON'da zIndex alanıyla sıralanır) */}
            {[...band.elements]
              .sort((a, b) => (a.zIndex ?? 0) - (b.zIndex ?? 0))
              .map(el => (
                <KonvaElement
                  key={el.id}
                  element={el}
                  bandId={band.id}
                  isSelected={selectedElementId === el.id}
                  dispatch={dispatch}
                />
              ))}
          </Layer>
        </Stage>
      </div>

      {/* Band yüksekliği resize tutamacı */}
      <div
        style={{
          height: 5, background: 'transparent', cursor: 'ns-resize',
          borderBottom: '2px dashed #ccc',
          transition: 'border-color 0.15s'
        }}
        onPointerDown={onResizeStart}
        title={`Yükseklik: ${band.height.toFixed(1)} mm`}
      />
    </div>
  )
}
