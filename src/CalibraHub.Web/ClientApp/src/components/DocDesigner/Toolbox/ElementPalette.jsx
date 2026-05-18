import React from 'react'
import { Type, Database, Image, Square, DollarSign, Hash, Clock, GripVertical } from 'lucide-react'

const KINDS = [
  { kind: 'Label',         label: 'Etiket',          Icon: Type },
  { kind: 'BoundField',    label: 'Veri Alanı',      Icon: Database },
  { kind: 'Image',         label: 'Resim',            Icon: Image },
  { kind: 'Shape',         label: 'Şekil',            Icon: Square },
  { kind: 'AmountInWords', label: 'Yazı ile Tutar',  Icon: DollarSign },
  { kind: 'PageNumber',    label: 'Sayfa No',         Icon: Hash },
  { kind: 'DateTimeNow',   label: 'Tarih / Saat',    Icon: Clock },
]

export default function ElementPalette() {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
      {KINDS.map(({ kind, label, Icon }) => (
        <div
          key={kind}
          draggable
          onDragStart={e => e.dataTransfer.setData('element-kind', kind)}
          style={{
            display: 'flex', alignItems: 'center', gap: 8,
            height: 36, padding: '0 8px', borderRadius: 6,
            cursor: 'grab', userSelect: 'none',
            transition: 'background 0.12s',
          }}
          onMouseEnter={e => e.currentTarget.style.background = 'rgba(99,102,241,0.08)'}
          onMouseLeave={e => e.currentTarget.style.background = 'transparent'}
          onMouseDown={e => e.currentTarget.style.cursor = 'grabbing'}
          onMouseUp={e => e.currentTarget.style.cursor = 'grab'}
        >
          <GripVertical size={12} color="#d1d5db" style={{ flexShrink: 0 }} />
          <Icon size={14} color="#6366f1" style={{ flexShrink: 0 }} />
          <span style={{ fontSize: 12, color: '#374151', flex: 1 }}>{label}</span>
        </div>
      ))}
    </div>
  )
}
