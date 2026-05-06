import React from 'react'
import { ELEMENT_KINDS } from '../designerReducer'

export default function ElementPalette() {
  return (
    <div>
      <div style={{ fontSize: 11, color: '#888', marginBottom: 8, textTransform: 'uppercase', letterSpacing: 0.5 }}>
        Sürükle → Banda Bırak
      </div>
      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 6 }}>
        {ELEMENT_KINDS.map(({ kind, label, icon }) => (
          <div
            key={kind}
            draggable
            onDragStart={(e) => e.dataTransfer.setData('element-kind', kind)}
            style={{
              border: '1px solid #e0e0e0',
              borderRadius: 6,
              padding: '8px 6px',
              cursor: 'grab',
              background: '#fafafa',
              display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 4,
              fontSize: 11, color: '#555',
              transition: 'all 0.15s',
              userSelect: 'none'
            }}
            onMouseEnter={e => { e.currentTarget.style.background = '#ede9fe'; e.currentTarget.style.borderColor = '#a5b4fc' }}
            onMouseLeave={e => { e.currentTarget.style.background = '#fafafa'; e.currentTarget.style.borderColor = '#e0e0e0' }}
            title={`${label} ekle`}
          >
            <span style={{ fontSize: 18 }}>{icon}</span>
            <span>{label}</span>
          </div>
        ))}
      </div>
    </div>
  )
}
