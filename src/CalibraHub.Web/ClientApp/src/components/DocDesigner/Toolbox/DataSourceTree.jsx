import React, { useState } from 'react'
import { getViewColumns, getDbViewColumns } from '../services/docDesignerService'

/** adHocSql içinden "schema.view" veya "view" çekmeye çalışır */
function parseViewFromSql(sql) {
  if (!sql) return null
  const m = sql.match(/FROM\s+\[?(\w+)\]?\.\[?(\w+)\]?/i)
  if (m) return `${m[1]}.${m[2]}`
  const m2 = sql.match(/FROM\s+\[?(\w+)\]?/i)
  return m2 ? m2[1] : null
}

export default function DataSourceTree({ state, dispatch }) {
  const { dataSources } = state
  const [expandedAlias, setExpandedAlias] = useState(null)
  const [columns, setColumns] = useState({})
  const [loadingAlias, setLoadingAlias] = useState(null)
  // confirmingAlias kaldırıldı — silme artık "+ Veri" modal'ından yapılıyor.

  const toggleAlias = async src => {
    if (expandedAlias === src.alias) { setExpandedAlias(null); return }
    setExpandedAlias(src.alias)
    if (!columns[src.alias]) {
      setLoadingAlias(src.alias)
      try {
        let cols
        if (src.viewId) {
          cols = await getViewColumns(src.viewId)
        } else {
          // DB view veya ad-hoc SQL: dbView alanına bak, yoksa SQL'den çözümle
          const viewName = src.dbView ?? parseViewFromSql(src.adHocSql)
          if (viewName) cols = await getDbViewColumns(viewName)
        }
        if (cols) setColumns(prev => ({ ...prev, [src.alias]: cols }))
      } catch {}
      setLoadingAlias(null)
    }
  }

  return (
    <div>
      {dataSources.length === 0 && (
        <div style={{ color: 'var(--dd-text-muted, #bbb)', fontSize: 11, textAlign: 'center', marginTop: 16, padding: '0 8px' }}>
          Üst çubuktan veri kaynağı ekleyin
        </div>
      )}

      {dataSources.map(src => {
        // SSMS'den kontrol kolaylığı için arka plandaki view/SQL kaynağını da göster.
        // Öncelik sırası: dbView (Admin tarafından bağlanmış view) > adHocSql'den parse
        // edilen FROM hedefi > "ad-hoc SQL" generic etiketi.
        const sourceName = src.dbView
          ?? parseViewFromSql(src.adHocSql)
          ?? (src.adHocSql ? 'ad-hoc SQL' : null)
        return (
        <div key={src.alias} style={{ marginBottom: 4 }}>
          <div
            onClick={() => toggleAlias(src)}
            style={{
              display: 'flex', alignItems: 'center', gap: 6, padding: '5px 8px',
              borderRadius: 6, cursor: 'pointer',
              background: expandedAlias === src.alias ? 'var(--dd-accent-soft, #ede9fe)' : 'transparent',
              fontSize: 12, fontWeight: 600, color: 'var(--dd-text, #444)',
            }}
          >
            <span style={{ fontSize: 9, flexShrink: 0 }}>{expandedAlias === src.alias ? '▼' : '▶'}</span>
            {/* min-width:0 olmadan flex child ellipsis çalışmaz; içeride break-word ile
                view adı uzun olsa bile satıra sığar (sola tekrar wrap) */}
            <span style={{ flex: 1, minWidth: 0,
                            display: 'flex', flexDirection: 'column', gap: 1 }}>
              <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                {src.alias}
              </span>
              {sourceName && (
                <span style={{ fontSize: 10, fontWeight: 400, color: 'var(--dd-text-muted, #6b7280)',
                                fontFamily: 'ui-monospace, Menlo, Consolas, monospace',
                                wordBreak: 'break-all', lineHeight: 1.25 }}
                      title={src.adHocSql ?? sourceName}>
                  {sourceName}
                </span>
              )}
            </span>
            {/* Silme işlemi sol menüden kaldırıldı — sadece "+ Veri" modal'ında.
                Buradaki amaç salt görüntüleme ve element sürükleme (column listesi). */}
          </div>

          {expandedAlias === src.alias && (
            <div style={{ paddingLeft: 16, marginTop: 2 }}>
              {loadingAlias === src.alias && <div style={{ fontSize: 11, color: 'var(--dd-text-muted, #aaa)' }}>Yükleniyor...</div>}
              {(columns[src.alias] ?? []).map(col => (
                <div
                  key={col.colName}
                  draggable
                  onDragStart={e => {
                    e.dataTransfer.setData('element-kind', 'BoundField')
                    e.dataTransfer.setData('element-binding', JSON.stringify({ alias: src.alias, col: col.colName }))
                    e.dataTransfer.setData('element-text', col.displayName ?? col.colName)
                  }}
                  style={{ padding: '3px 6px', borderRadius: 4, cursor: 'grab', fontSize: 11, color: 'var(--dd-text-muted, #555)',
                    display: 'flex', alignItems: 'center', gap: 4 }}
                  onMouseEnter={e => e.currentTarget.style.background = 'var(--dd-surface-alt, #f3f4f6)'}
                  onMouseLeave={e => e.currentTarget.style.background = 'transparent'}
                  title={`${src.alias}.${col.colName} — ${col.dataType ?? ''}`}
                >
                  <span style={{ width: 6, height: 6, borderRadius: '50%', background: 'var(--dd-accent, #a5b4fc)', flexShrink: 0 }}/>
                  <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                    {col.displayName ?? col.colName}
                  </span>
                </div>
              ))}
            </div>
          )}
        </div>
        )
      })}
    </div>
  )
}
