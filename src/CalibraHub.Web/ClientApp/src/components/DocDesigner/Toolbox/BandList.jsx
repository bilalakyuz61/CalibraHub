import React from 'react'
import { BAND_TYPES } from '../designerReducer'

export default function BandList({ state, dispatch }) {
  const { bands, meta } = state
  const existingTypes = new Set(bands.map(b => b.type))
  const isEmail = (meta?.outputFormat ?? 'pdf') === 'email'
  // PDF mod'da mail_body gizli; email mod'da sadece mail_body gosterilir.
  const visibleBandTypes = BAND_TYPES.filter(b =>
    isEmail ? b.mailOnly === true : b.mailOnly !== true)

  return (
    <div>
      <div style={{ fontSize: 11, color: '#888', marginBottom: 8, textTransform: 'uppercase', letterSpacing: 0.5 }}>
        Bant Ekle {isEmail ? '(Email)' : ''}
      </div>
      {visibleBandTypes.map(({ type, label }) => (
        <button
          key={type}
          onClick={() => dispatch({ type: 'ADD_BAND', bandType: type })}
          disabled={existingTypes.has(type)}
          style={{
            display: 'block', width: '100%', textAlign: 'left', padding: '6px 10px',
            marginBottom: 4, borderRadius: 6, border: '1px solid #e0e0e0',
            background: existingTypes.has(type) ? '#f9fafb' : '#fff',
            fontSize: 12, cursor: existingTypes.has(type) ? 'not-allowed' : 'pointer',
            color: existingTypes.has(type) ? '#bbb' : '#444', transition: 'all 0.15s',
          }}
          onMouseEnter={e => { if (!existingTypes.has(type)) e.currentTarget.style.background = '#ede9fe' }}
          onMouseLeave={e => { if (!existingTypes.has(type)) e.currentTarget.style.background = '#fff' }}
        >
          {existingTypes.has(type) ? '✓ ' : '+ '}{label}
        </button>
      ))}

      {bands.length > 0 && (
        <>
          <div style={{ fontSize: 11, color: '#888', marginTop: 16, marginBottom: 8, textTransform: 'uppercase', letterSpacing: 0.5 }}>
            Mevcut Bantlar
          </div>
          {bands.map(b => {
            const def = BAND_TYPES.find(d => d.type === b.type)
            return (
              <div key={b.id} style={{ padding: '4px 8px', fontSize: 11, color: '#555',
                display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                <span>{def?.label ?? b.type}</span>
                <span style={{ color: '#bbb' }}>{b.height.toFixed(0)}mm</span>
              </div>
            )
          })}
        </>
      )}
    </div>
  )
}
