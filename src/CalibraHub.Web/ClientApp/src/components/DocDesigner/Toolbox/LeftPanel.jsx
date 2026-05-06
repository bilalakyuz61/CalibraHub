import React, { useState } from 'react'
import ElementPalette from './ElementPalette'
import DataSourceTree from './DataSourceTree'
import BandList from './BandList'

const TABS = [
  { key: 'elements', label: 'Elementler' },
  { key: 'data',     label: 'Veri' },
  { key: 'bands',    label: 'Bantlar' },
]

export default function LeftPanel({ state, dispatch, availableViews }) {
  const [tab, setTab] = useState('elements')

  return (
    <div style={{
      width: 220, flexShrink: 0, borderRight: '1px solid var(--app-border, #e5e7eb)',
      background: 'var(--app-surface, #fff)', display: 'flex', flexDirection: 'column',
      overflow: 'hidden'
    }}>
      {/* Sekme başlıkları */}
      <div style={{ display: 'flex', borderBottom: '1px solid var(--app-border, #e5e7eb)' }}>
        {TABS.map(t => (
          <button key={t.key}
            onClick={() => setTab(t.key)}
            style={{
              flex: 1, border: 'none', background: 'none', padding: '8px 4px',
              fontSize: 11, fontWeight: tab === t.key ? 700 : 400,
              color: tab === t.key ? '#6366f1' : '#666',
              borderBottom: tab === t.key ? '2px solid #6366f1' : '2px solid transparent',
              cursor: 'pointer', transition: 'all 0.15s'
            }}
          >{t.label}</button>
        ))}
      </div>

      <div style={{ flex: 1, overflow: 'auto', padding: 8 }}>
        {tab === 'elements' && <ElementPalette />}
        {tab === 'data'     && <DataSourceTree state={state} dispatch={dispatch} availableViews={availableViews} />}
        {tab === 'bands'    && <BandList state={state} dispatch={dispatch} />}
      </div>
    </div>
  )
}
