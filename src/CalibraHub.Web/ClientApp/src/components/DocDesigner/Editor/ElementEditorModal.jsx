import React, { useEffect, useRef, useState } from 'react'
import { getViewColumns, getDbViewColumns } from '../services/docDesignerService'

const FORMAT_PRESETS = {
  Text: [
    { label: '(Format yok)', value: '' },
    { label: 'BÜYÜK HARF',   value: 'upper' },
    { label: 'küçük harf',   value: 'lower' },
  ],
  Number: [
    { label: '#,##0.00',  value: '#,##0.00' },
    { label: '#,##0',     value: '#,##0' },
    { label: '0.00',      value: '0.00' },
    { label: '%0.00',     value: '%0.00' },
    { label: '₺ #,##0.00',value: '₺ #,##0.00' },
  ],
  Date: [
    { label: 'dd.MM.yyyy',          value: 'dd.MM.yyyy' },
    { label: 'dd.MM.yyyy HH:mm',    value: 'dd.MM.yyyy HH:mm' },
    { label: 'dd MMMM yyyy',        value: 'dd MMMM yyyy' },
    { label: 'yyyy-MM-dd',          value: 'yyyy-MM-dd' },
    { label: 'HH:mm',               value: 'HH:mm' },
  ],
  Boolean: [
    { label: 'Evet / Hayır',  value: 'evet/hayır' },
    { label: 'Aktif / Pasif', value: 'aktif/pasif' },
    { label: 'True / False',  value: 'true/false' },
  ],
}

function detectCategory(fmt) {
  if (!fmt) return 'Text'
  if (/[#0%]|[₺€$]/.test(fmt)) return 'Number'
  if (/(yyyy|MM|dd|HH|mm|ss)/.test(fmt)) return 'Date'
  if (/(evet|aktif|true)/i.test(fmt)) return 'Boolean'
  return 'Text'
}

function parseViewFromSql(sql) {
  if (!sql) return null
  const m = sql.match(/FROM\s+\[?(\w+)\]?\.\[?(\w+)\]?/i)
  if (m) return `${m[1]}.${m[2]}`
  const m2 = sql.match(/FROM\s+\[?(\w+)\]?/i)
  return m2 ? m2[1] : null
}

export default function ElementEditorModal({ el, dataSources, onSave, onClose }) {
  // Barkod elementi de bir alana baglanir (Barcode/QR icin "deger" kaynagi).
  const isBound = el.kind === 'BoundField' || el.kind === 'AmountInWords' || el.kind === 'Barcode'
  const initialExpr = isBound
    ? (el.binding?.alias && el.binding?.col ? `[${el.binding.alias}.${el.binding.col}]` : '')
    : (el.text ?? '')

  const [tab, setTab]         = useState('text')
  const [expr, setExpr]       = useState(initialExpr)
  const [format, setFormat]   = useState(el.format ?? '')
  const [category, setCategory] = useState(detectCategory(el.format))
  const [decimalSep, setDecimalSep] = useState(',')
  const [expandedAlias, setExpandedAlias] = useState(null)
  const [columns, setColumns] = useState({})
  const [loadingAlias, setLoadingAlias] = useState(null)
  const [search,    setSearch]    = useState('')   // veri kaynakları arama
  const exprRef = useRef(null)

  // Esc kapatır, Enter onaylar
  useEffect(() => {
    const onKey = e => {
      if (e.key === 'Escape') { e.preventDefault(); onClose() }
      else if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) { e.preventDefault(); handleOk() }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  })

  // Arama açıkken (boş değil) tüm view'ların kolonlarını lazy preload et —
  // kullanıcı her view'ı tek tek açmasın diye. Cache'leniyor.
  useEffect(() => {
    if (!search.trim()) return
    let cancelled = false
    ;(async () => {
      for (const src of dataSources) {
        if (cancelled) break
        if (columns[src.alias]) continue   // zaten yüklü
        try {
          let cols
          if (src.viewId) cols = await getViewColumns(src.viewId)
          else {
            const viewName = src.dbView ?? parseViewFromSql(src.adHocSql)
            if (viewName) cols = await getDbViewColumns(viewName)
          }
          if (cols && !cancelled) setColumns(prev => ({ ...prev, [src.alias]: cols }))
        } catch {}
      }
    })()
    return () => { cancelled = true }
  }, [search])   // eslint-disable-line

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
          const viewName = src.dbView ?? parseViewFromSql(src.adHocSql)
          if (viewName) cols = await getDbViewColumns(viewName)
        }
        if (cols) setColumns(prev => ({ ...prev, [src.alias]: cols }))
      } catch {}
      setLoadingAlias(null)
    }
  }

  const insertColumn = (alias, col) => {
    const token = `[${alias}.${col}]`
    const input = exprRef.current
    if (!input) { setExpr(token); return }
    const s = input.selectionStart ?? expr.length
    const e = input.selectionEnd   ?? expr.length
    const next = expr.slice(0, s) + token + expr.slice(e)
    setExpr(next)
    requestAnimationFrame(() => {
      input.focus()
      input.selectionStart = input.selectionEnd = s + token.length
    })
  }

  const handleOk = () => {
    const patch = {}
    const m = expr.match(/^\s*\[(\w+)\.(\w+)\]\s*$/)
    if (isBound) {
      if (m) {
        patch.binding = { alias: m[1], col: m[2] }
        patch.text    = null
      } else {
        patch.binding = el.binding ?? { alias: '', col: '' }
        patch.text    = expr
      }
    } else {
      patch.text = expr
    }
    patch.format = format || null
    onSave(patch)
    onClose()
  }

  return (
    <div onMouseDown={e => { if (e.target === e.currentTarget) onClose() }} style={backdrop}>
      <div style={card} onMouseDown={e => e.stopPropagation()}>
        <div style={titleBar}>
          <span style={{ fontSize: 13, fontWeight: 700 }}>
            {el.kind === 'Label' ? 'Etiket Düzenle'
             : el.kind === 'BoundField' ? 'Veri Alanı Düzenle'
             : el.kind === 'AmountInWords' ? 'Yazı ile Tutar Düzenle'
             : el.kind === 'Barcode' ? (el.barcodeType === 'QR' ? 'QR Düzenle' : 'Barkod Düzenle')
             : 'Element Düzenle'}
          </span>
          <button onClick={onClose} style={closeBtn}>×</button>
        </div>

        {/* Tabs */}
        <div style={tabBar}>
          <button onClick={() => setTab('text')}
            style={{ ...tabBtn, ...(tab === 'text' ? tabBtnActive : null) }}>
            İfade
          </button>
          <button onClick={() => setTab('format')}
            style={{ ...tabBtn, ...(tab === 'format' ? tabBtnActive : null) }}>
            Format
          </button>
        </div>

        {/* Body */}
        <div style={body}>
          {/* Sol panel */}
          <div style={leftPanel}>
            {tab === 'text' && (
              <>
                <div style={fieldLabel}>İfade</div>
                <textarea
                  ref={exprRef}
                  value={expr}
                  onChange={e => setExpr(e.target.value)}
                  placeholder="Sağdan kolon çift-tıklayın veya doğrudan yazın. Örnek: [PLV.CARI_ISIM]"
                  style={textarea}
                  autoFocus
                />
                <div style={{ fontSize: 10.5, color: 'var(--dd-text-muted, #94a3b8)', marginTop: 6, lineHeight: 1.5 }}>
                  Tek başına <code style={code}>[alias.kolon]</code> yazarsanız o kolona bağlanır.
                  Karışık metin yazarsanız (ör. <code style={code}>Tutar: [F.Total]</code>) etiket olarak tutulur.
                </div>
              </>
            )}

            {tab === 'format' && (
              <>
                <div style={fieldLabel}>Kategori</div>
                <div style={{ display: 'flex', gap: 6, marginBottom: 12 }}>
                  {['Text','Number','Date','Boolean'].map(c => (
                    <button key={c}
                      onClick={() => { setCategory(c); setFormat('') }}
                      style={{
                        flex: 1, padding: '6px 8px', fontSize: 11,
                        border: `1px solid ${category === c ? 'var(--dd-accent)' : 'var(--dd-border)'}`,
                        background: category === c ? 'var(--dd-accent-soft)' : 'var(--dd-surface)',
                        color: category === c ? 'var(--dd-accent)' : 'var(--dd-text)',
                        borderRadius: 4, cursor: 'pointer', fontWeight: category === c ? 600 : 400,
                      }}>
                      {c === 'Text' ? 'Metin' : c === 'Number' ? 'Sayı' : c === 'Date' ? 'Tarih' : 'Mantıksal'}
                    </button>
                  ))}
                </div>

                <div style={fieldLabel}>Hazır format</div>
                <div style={{ display: 'flex', flexDirection: 'column', gap: 4, marginBottom: 12 }}>
                  {(FORMAT_PRESETS[category] ?? []).map(p => (
                    <button key={p.value}
                      onClick={() => setFormat(p.value)}
                      style={{
                        textAlign: 'left', padding: '5px 8px',
                        border: `1px solid ${format === p.value ? 'var(--dd-accent)' : 'var(--dd-border)'}`,
                        background: format === p.value ? 'var(--dd-accent-soft)' : 'var(--dd-surface)',
                        color: 'var(--dd-text)', borderRadius: 4, cursor: 'pointer',
                        fontFamily: 'ui-monospace, Menlo, Consolas, monospace', fontSize: 11,
                      }}>
                      {p.label}
                    </button>
                  ))}
                </div>

                <div style={fieldLabel}>Özel format dizesi</div>
                <input
                  value={format}
                  onChange={e => setFormat(e.target.value)}
                  placeholder="#,##0.00"
                  style={input}
                />

                {category === 'Number' && (
                  <div style={{ marginTop: 12 }}>
                    <div style={fieldLabel}>Ondalık ayırıcı</div>
                    <div style={{ display: 'flex', gap: 6 }}>
                      {[',', '.'].map(s => (
                        <button key={s}
                          onClick={() => setDecimalSep(s)}
                          style={{
                            flex: 1, padding: '5px 0', fontSize: 11,
                            border: `1px solid ${decimalSep === s ? 'var(--dd-accent)' : 'var(--dd-border)'}`,
                            background: decimalSep === s ? 'var(--dd-accent-soft)' : 'var(--dd-surface)',
                            color: 'var(--dd-text)', borderRadius: 4, cursor: 'pointer',
                          }}>
                          {s === ',' ? 'Virgül (,)' : 'Nokta (.)'}
                        </button>
                      ))}
                    </div>
                  </div>
                )}
              </>
            )}
          </div>

          {/* Sağ panel — Veri kaynakları + arama */}
          <div style={rightPanel}>
            <div style={fieldLabel}>Veri Kaynakları</div>

            {/* Arama — alias VEYA kolon adında match */}
            <input
              type="text" value={search} onChange={e => setSearch(e.target.value)}
              placeholder="Ara: alias veya kolon adı…"
              style={{
                width: '100%', padding: '5px 8px', fontSize: 11,
                background: 'var(--dd-surface, #fff)', color: 'var(--dd-text, #111)',
                border: '1px solid var(--dd-border, #e5e7eb)', borderRadius: 4,
                marginBottom: 8, boxSizing: 'border-box',
              }}
            />

            {dataSources.length === 0 && (
              <div style={{ fontSize: 11, color: 'var(--dd-text-muted)', textAlign: 'center', marginTop: 16 }}>
                Önce üst çubuktan veri kaynağı ekleyin
              </div>
            )}

            {(() => {
              const q = search.trim().toLowerCase()
              return dataSources.map(src => {
                const aliasMatch = !q || src.alias.toLowerCase().includes(q)
                const cols = columns[src.alias] ?? []
                const visibleCols = !q ? cols : cols.filter(c =>
                  (c.colName ?? '').toLowerCase().includes(q) ||
                  (c.displayName ?? '').toLowerCase().includes(q)
                )
                // Arama varsa: alias match OR kolon match olan gösterilir, alias otomatik açık
                const hasColMatch = q && visibleCols.length > 0
                if (q && !aliasMatch && !hasColMatch) return null   // tamamen filtre dışı

                const isExpanded = q ? (aliasMatch || hasColMatch) : (expandedAlias === src.alias)
                return (
                  <div key={src.alias} style={{ marginBottom: 2 }}>
                    <div
                      onClick={() => !q && toggleAlias(src)}
                      style={{
                        display: 'flex', alignItems: 'center', gap: 5, padding: '4px 6px',
                        borderRadius: 4, cursor: q ? 'default' : 'pointer',
                        fontSize: 11.5, fontWeight: 600,
                        color: 'var(--dd-text)',
                        background: isExpanded && !q ? 'var(--dd-accent-soft)' : 'transparent',
                      }}>
                      <span style={{ fontSize: 9 }}>{isExpanded ? '▼' : '▶'}</span>
                      <span style={{ flex: 1 }}>
                        {q && aliasMatch
                          ? <HighlightMatch text={src.alias} q={q} />
                          : src.alias}
                      </span>
                      <span style={{ fontSize: 9, opacity: 0.6 }}>{src.role}</span>
                    </div>
                    {isExpanded && (
                      <div style={{ paddingLeft: 14, marginTop: 2, marginBottom: 4 }}>
                        {loadingAlias === src.alias && !cols.length && (
                          <div style={{ fontSize: 10.5, color: 'var(--dd-text-muted)' }}>Yükleniyor…</div>
                        )}
                        {visibleCols.map(col => (
                          <div
                            key={col.colName}
                            onDoubleClick={() => insertColumn(src.alias, col.colName)}
                            title={`Çift tıkla ekle — ${col.dataType ?? ''}`}
                            style={{
                              padding: '2px 6px', borderRadius: 3, cursor: 'pointer', fontSize: 10.5,
                              color: 'var(--dd-text-muted)', display: 'flex', alignItems: 'center', gap: 4,
                              userSelect: 'none',
                            }}
                            onMouseEnter={e => e.currentTarget.style.background = 'var(--dd-surface-alt)'}
                            onMouseLeave={e => e.currentTarget.style.background = 'transparent'}
                          >
                            <span style={{ width: 5, height: 5, borderRadius: '50%', background: 'var(--dd-accent)', flexShrink: 0 }} />
                            <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                              {q
                                ? <HighlightMatch text={col.displayName ?? col.colName} q={q} />
                                : (col.displayName ?? col.colName)}
                            </span>
                          </div>
                        ))}
                        {q && visibleCols.length === 0 && cols.length > 0 && (
                          <div style={{ fontSize: 10, color: 'var(--dd-text-muted)', fontStyle: 'italic', padding: '2px 6px' }}>
                            Kolon eşleşmiyor (alias match)
                          </div>
                        )}
                      </div>
                    )}
                  </div>
                )
              })
            })()}

            {(() => {
              const q = search.trim().toLowerCase()
              if (!q) return null
              const anyMatch = dataSources.some(s =>
                s.alias.toLowerCase().includes(q) ||
                (columns[s.alias] ?? []).some(c =>
                  (c.colName ?? '').toLowerCase().includes(q) ||
                  (c.displayName ?? '').toLowerCase().includes(q))
              )
              if (anyMatch) return null
              return (
                <div style={{ fontSize: 11, color: 'var(--dd-text-muted)', textAlign: 'center', marginTop: 10 }}>
                  Eşleşen alias / kolon bulunamadı
                </div>
              )
            })()}
          </div>
        </div>

        {/* Footer */}
        <div style={footer}>
          <button onClick={onClose} style={ghostBtn}>Vazgeç</button>
          <button onClick={handleOk} style={primaryBtn}>Tamam</button>
        </div>
      </div>
    </div>
  )
}

/**
 * Arama match'ini text içinde sarı highlight ile gösterir. Case-insensitive.
 */
function HighlightMatch({ text, q }) {
  if (!q || !text) return <>{text}</>
  const lower = text.toLowerCase()
  const idx = lower.indexOf(q)
  if (idx < 0) return <>{text}</>
  return (
    <>
      {text.slice(0, idx)}
      <mark style={{ background: 'rgba(250,204,21,0.4)', color: 'inherit', padding: 0 }}>
        {text.slice(idx, idx + q.length)}
      </mark>
      {text.slice(idx + q.length)}
    </>
  )
}

// ── Stiller ──────────────────────────────────────────────────────────────────

const backdrop = {
  position: 'fixed', inset: 0, background: 'rgba(0,0,0,0.55)', backdropFilter: 'blur(2px)',
  display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 1000,
}
const card = {
  width: 720, maxWidth: '90vw', height: 480, maxHeight: '90vh',
  background: 'var(--dd-surface, #fff)', border: '1px solid var(--dd-border, #e5e7eb)',
  borderRadius: 8, boxShadow: '0 12px 40px rgba(0,0,0,0.4)',
  display: 'flex', flexDirection: 'column', overflow: 'hidden',
}
const titleBar = {
  display: 'flex', alignItems: 'center', justifyContent: 'space-between',
  padding: '10px 14px', borderBottom: '1px solid var(--dd-border, #e5e7eb)',
  background: 'var(--dd-surface-alt, #f8f9fb)', color: 'var(--dd-text, #111)',
}
const closeBtn = {
  width: 26, height: 26, border: 'none', background: 'transparent',
  fontSize: 18, color: 'var(--dd-text-muted, #6b7280)', cursor: 'pointer', lineHeight: 1,
}
const tabBar = {
  display: 'flex', gap: 0, borderBottom: '1px solid var(--dd-border, #e5e7eb)',
  background: 'var(--dd-surface, #fff)', padding: '0 8px',
}
const tabBtn = {
  padding: '8px 16px', fontSize: 12, fontWeight: 500,
  background: 'transparent', border: 'none', cursor: 'pointer',
  color: 'var(--dd-text-muted, #6b7280)', borderBottom: '2px solid transparent',
}
const tabBtnActive = {
  color: 'var(--dd-accent, #6366f1)',
  borderBottomColor: 'var(--dd-accent, #6366f1)',
  fontWeight: 700,
}
const body = { flex: 1, display: 'flex', overflow: 'hidden' }
const leftPanel = { flex: 1, padding: 14, overflow: 'auto', borderRight: '1px solid var(--dd-border, #e5e7eb)' }
const rightPanel = { width: 240, padding: 10, overflow: 'auto', background: 'var(--dd-surface-alt, #fafafa)' }
const fieldLabel = { fontSize: 10, fontWeight: 700, textTransform: 'uppercase', letterSpacing: 0.5, color: 'var(--dd-text-muted, #6b7280)', marginBottom: 5 }
const textarea = {
  width: '100%', minHeight: 100, padding: '8px 10px', fontFamily: 'ui-monospace, Menlo, Consolas, monospace', fontSize: 12,
  background: 'var(--dd-surface-alt, #f9fafb)', color: 'var(--dd-text, #111)',
  border: '1px solid var(--dd-border, #e5e7eb)', borderRadius: 4, resize: 'vertical', boxSizing: 'border-box',
}
const input = {
  width: '100%', padding: '6px 9px', fontFamily: 'ui-monospace, Menlo, Consolas, monospace', fontSize: 12,
  background: 'var(--dd-surface, #fff)', color: 'var(--dd-text, #111)',
  border: '1px solid var(--dd-border, #e5e7eb)', borderRadius: 4, boxSizing: 'border-box',
}
const code = { background: 'var(--dd-surface-alt, #f3f4f6)', padding: '1px 4px', borderRadius: 2, fontFamily: 'ui-monospace, Menlo, Consolas, monospace', fontSize: 10 }
const footer = {
  display: 'flex', justifyContent: 'flex-end', gap: 6,
  padding: '10px 14px', borderTop: '1px solid var(--dd-border, #e5e7eb)',
  background: 'var(--dd-surface-alt, #f8f9fb)',
}
const ghostBtn = {
  padding: '6px 14px', fontSize: 12, fontWeight: 500,
  background: 'var(--dd-surface, #fff)', color: 'var(--dd-text, #374151)',
  border: '1px solid var(--dd-border, #e5e7eb)', borderRadius: 4, cursor: 'pointer',
}
const primaryBtn = {
  padding: '6px 14px', fontSize: 12, fontWeight: 600,
  background: 'var(--dd-accent, #6366f1)', color: '#fff',
  border: '1px solid var(--dd-accent, #6366f1)', borderRadius: 4, cursor: 'pointer',
}
