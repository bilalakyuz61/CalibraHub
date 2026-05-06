import React, { useState } from 'react'
import { getViewColumns } from '../services/docDesignerService'

export default function DataSourceTree({ state, dispatch, availableViews }) {
  const { dataSources } = state
  const [expandedAlias, setExpandedAlias] = useState(null)
  const [columns, setColumns] = useState({})
  const [loadingAlias, setLoadingAlias] = useState(null)

  const toggleAlias = async (src) => {
    if (expandedAlias === src.alias) { setExpandedAlias(null); return }
    setExpandedAlias(src.alias)
    if (!columns[src.alias] && src.viewId) {
      setLoadingAlias(src.alias)
      try {
        const cols = await getViewColumns(src.viewId)
        setColumns(prev => ({ ...prev, [src.alias]: cols }))
      } catch {}
      setLoadingAlias(null)
    }
  }

  return (
    <div>
      <div style={{ fontSize: 11, color: '#888', marginBottom: 8, textTransform: 'uppercase', letterSpacing: 0.5 }}>
        Veri Kaynakları
      </div>

      {dataSources.length === 0 && (
        <div style={{ color: '#bbb', fontSize: 11, textAlign: 'center', marginTop: 16 }}>
          Üst çubuktan veri kaynağı ekleyin
        </div>
      )}

      {dataSources.map(src => (
        <div key={src.alias} style={{ marginBottom: 4 }}>
          <div
            onClick={() => toggleAlias(src)}
            style={{
              display: 'flex', alignItems: 'center', gap: 6,
              padding: '5px 8px', borderRadius: 6, cursor: 'pointer',
              background: expandedAlias === src.alias ? '#ede9fe' : 'transparent',
              fontSize: 12, fontWeight: 600, color: '#444'
            }}
          >
            <span style={{ fontSize: 9 }}>{expandedAlias === src.alias ? '▼' : '▶'}</span>
            <span>{src.alias}</span>
            <span style={{
              marginLeft: 'auto', fontSize: 10, padding: '1px 5px', borderRadius: 3,
              background: src.role === 'master' ? '#d1fae5' : '#dbeafe',
              color: src.role === 'master' ? '#065f46' : '#1e40af'
            }}>{src.role}</span>
          </div>

          {expandedAlias === src.alias && (
            <div style={{ paddingLeft: 16, marginTop: 2 }}>
              {loadingAlias === src.alias && (
                <div style={{ fontSize: 11, color: '#aaa' }}>Yükleniyor...</div>
              )}
              {(columns[src.alias] ?? []).map(col => (
                <div
                  key={col.colName}
                  draggable
                  onDragStart={(e) => {
                    e.dataTransfer.setData('element-kind', 'BoundField')
                    e.dataTransfer.setData('element-binding', JSON.stringify({ alias: src.alias, col: col.colName }))
                    e.dataTransfer.setData('element-text', col.displayName ?? col.colName)
                  }}
                  style={{
                    padding: '3px 6px', borderRadius: 4, cursor: 'grab',
                    fontSize: 11, color: '#555',
                    display: 'flex', alignItems: 'center', gap: 4
                  }}
                  onMouseEnter={e => e.currentTarget.style.background = '#f3f4f6'}
                  onMouseLeave={e => e.currentTarget.style.background = 'transparent'}
                  title={`${src.alias}.${col.colName} — ${col.dataType ?? ''}`}
                >
                  <span style={{ width: 6, height: 6, borderRadius: '50%', background: '#a5b4fc', flexShrink: 0 }}/>
                  <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                    {col.displayName ?? col.colName}
                  </span>
                </div>
              ))}
            </div>
          )}
        </div>
      ))}
    </div>
  )
}
