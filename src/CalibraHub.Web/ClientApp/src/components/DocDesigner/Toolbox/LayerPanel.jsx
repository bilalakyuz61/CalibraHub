import React, { useState } from 'react'
import { ChevronDown, ChevronRight, Type, Database, Image, Square, DollarSign, Hash, Clock, Layers } from 'lucide-react'
import { BAND_TYPES } from '../designerReducer'

const BAND_COLORS = {
  PageHeader: '#6366f1', DocumentHeader: '#f59e0b', TableHeader: '#10b981',
  Detail: '#8b5cf6', TotalsBlock: '#ec4899', SignatureBlock: '#3b82f6', PageFooter: '#6b7280',
}

const KIND_ICONS = {
  Label: Type, BoundField: Database, Image, Shape: Square,
  AmountInWords: DollarSign, PageNumber: Hash, DateTimeNow: Clock,
}

function elementPreview(el) {
  if (el.kind === 'Label')      return el.text || '—'
  if (el.kind === 'BoundField') return `${el.binding?.alias ?? '?'}.${el.binding?.col ?? '?'}`
  return el.kind
}

export default function LayerPanel({ state, dispatch }) {
  const { bands, selectedElementId } = state
  const [collapsed, setCollapsed] = useState({})

  const toggle = id => setCollapsed(c => ({ ...c, [id]: !c[id] }))

  if (bands.length === 0) {
    return (
      <div style={{ padding: 16, color: '#9ca3af', fontSize: 12, textAlign: 'center' }}>
        <Layers size={28} color="#e5e7eb" style={{ marginBottom: 8, display: 'block', margin: '0 auto 8px' }} />
        Henüz bant yok
      </div>
    )
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
      {bands.map(band => {
        const def   = BAND_TYPES.find(d => d.type === band.type)
        const color = BAND_COLORS[band.type] ?? '#6b7280'
        const open  = !collapsed[band.id]
        return (
          <div key={band.id}>
            {/* Band row */}
            <div
              onClick={() => toggle(band.id)}
              style={{
                display: 'flex', alignItems: 'center', gap: 6, padding: '5px 8px',
                cursor: 'pointer', borderRadius: 4,
                background: 'rgba(0,0,0,0.02)',
              }}
              onMouseEnter={e => e.currentTarget.style.background = 'rgba(0,0,0,0.05)'}
              onMouseLeave={e => e.currentTarget.style.background = 'rgba(0,0,0,0.02)'}
            >
              {open
                ? <ChevronDown size={12} color="#9ca3af" style={{ flexShrink: 0 }} />
                : <ChevronRight size={12} color="#9ca3af" style={{ flexShrink: 0 }} />}
              <div style={{
                width: 8, height: 8, borderRadius: 2, background: color, flexShrink: 0,
              }} />
              <span style={{ fontSize: 11, fontWeight: 600, color: '#374151', flex: 1 }}>
                {def?.label ?? band.type}
              </span>
              <span style={{ fontSize: 10, color: '#9ca3af' }}>{band.height.toFixed(0)}mm</span>
            </div>

            {/* Element rows */}
            {open && band.elements.map(el => {
              const Icon = KIND_ICONS[el.kind] ?? Layers
              const isSel = selectedElementId === el.id
              return (
                <div
                  key={el.id}
                  onClick={() => dispatch({ type: 'SELECT_ELEMENT', elementId: el.id, bandId: band.id })}
                  style={{
                    display: 'flex', alignItems: 'center', gap: 6,
                    padding: '4px 8px 4px 28px',
                    borderRadius: 4, cursor: 'pointer',
                    background: isSel ? 'rgba(99,102,241,0.12)' : 'transparent',
                    borderLeft: isSel ? `2px solid ${color}` : '2px solid transparent',
                  }}
                  onMouseEnter={e => { if (!isSel) e.currentTarget.style.background = 'rgba(99,102,241,0.06)' }}
                  onMouseLeave={e => { if (!isSel) e.currentTarget.style.background = 'transparent' }}
                >
                  <Icon size={12} color={isSel ? '#6366f1' : '#9ca3af'} style={{ flexShrink: 0 }} />
                  <span style={{
                    fontSize: 11, color: isSel ? '#6366f1' : '#555', flex: 1,
                    overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap',
                  }}>
                    {elementPreview(el)}
                  </span>
                </div>
              )
            })}

            {open && band.elements.length === 0 && (
              <div style={{ padding: '3px 8px 3px 28px', fontSize: 10, color: '#d1d5db' }}>
                boş
              </div>
            )}
          </div>
        )
      })}
    </div>
  )
}
