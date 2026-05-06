import React, { useRef, useCallback } from 'react'
import BandContainer from './BandContainer'
import { BAND_TYPES } from '../designerReducer'

// mm → px dönüşümü (96 DPI)
export const MM_TO_PX = 96 / 25.4

export function mmToPx(mm) { return mm * MM_TO_PX }
export function pxToMm(px) { return px / MM_TO_PX }

export default function DesignerCanvas({ state, dispatch, dataSources }) {
  const { meta, bands, selectedElementId, selectedBandId } = state
  const pageWidthPx  = mmToPx(meta.pageW)
  const pageHeightPx = mmToPx(meta.pageH)

  const handleStageClick = useCallback((e) => {
    // Canvas boş alanına tıklanınca seçimi kaldır
    if (e.target === e.currentTarget) dispatch({ type: 'DESELECT' })
  }, [dispatch])

  return (
    <div className="dd-canvas-wrap" style={{ flex: 1, overflow: 'auto', background: '#e8e8e8', padding: '24px' }}>
      {/* A4 kağıt gölgesi */}
      <div
        className="dd-page"
        style={{
          width: pageWidthPx,
          minHeight: pageHeightPx,
          background: '#fff',
          margin: '0 auto',
          boxShadow: '0 4px 20px rgba(0,0,0,0.2)',
          position: 'relative',
          paddingTop: mmToPx(meta.marginTop),
          paddingBottom: mmToPx(meta.marginBot),
          paddingLeft: mmToPx(meta.marginLeft),
          paddingRight: mmToPx(meta.marginRight),
        }}
        onClick={handleStageClick}
      >
        {bands.length === 0 && (
          <div style={{
            position: 'absolute', inset: 0, display: 'flex', alignItems: 'center',
            justifyContent: 'center', color: '#aaa', flexDirection: 'column', gap: 8,
            pointerEvents: 'none'
          }}>
            <svg width="40" height="40" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5">
              <rect x="3" y="3" width="18" height="18" rx="2"/>
              <path d="M3 9h18M9 21V9"/>
            </svg>
            <span>Sol panelden bant ekleyin</span>
          </div>
        )}

        {bands.map(band => (
          <BandContainer
            key={band.id}
            band={band}
            dispatch={dispatch}
            selectedElementId={selectedElementId}
            selectedBandId={selectedBandId}
            contentWidthMm={meta.pageW - meta.marginLeft - meta.marginRight}
            dataSources={dataSources}
          />
        ))}
      </div>
    </div>
  )
}
