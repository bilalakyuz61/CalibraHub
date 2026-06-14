import React, { useState, useEffect, useCallback, useMemo } from 'react'
import './PurchaseRequestWizard.css'

export default function PurchaseRequestWizard({ antiForgeryToken }) {
  const [lines, setLines]           = useState([])
  const [loading, setLoading]       = useState(true)
  const [error, setError]           = useState(null)

  // filters
  const [matSearch, setMatSearch]   = useState('')
  const [docNoSearch, setDocNoSearch] = useState('')
  const [stockFilter, setStockFilter] = useState('all') // 'all' | 'instock' | 'nostock'

  // selection
  const [selected, setSelected]     = useState(new Set())

  // wizard state
  const [notes, setNotes]           = useState('')
  const [saving, setSaving]         = useState(false)
  const [saveError, setSaveError]   = useState(null)

  const loadLines = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      const params = new URLSearchParams()
      if (matSearch.trim())   params.set('materialSearch', matSearch.trim())
      if (docNoSearch.trim()) params.set('requestNumber', docNoSearch.trim())
      if (stockFilter === 'instock') params.set('hasStock', 'true')
      if (stockFilter === 'nostock') params.set('hasStock', 'false')

      const res  = await fetch('/Purchase/AllOpenRequestLines?' + params.toString())
      const data = await res.json()
      setLines(data)
      // deselect lines no longer in results
      setSelected(prev => {
        const ids = new Set(data.map(l => l.lineId))
        const next = new Set([...prev].filter(id => ids.has(id)))
        return next
      })
    } catch (e) {
      setError('Kalemler yüklenemedi: ' + e.message)
    } finally {
      setLoading(false)
    }
  }, [matSearch, docNoSearch, stockFilter])

  useEffect(() => {
    const t = setTimeout(loadLines, 300)
    return () => clearTimeout(t)
  }, [loadLines])

  const toggleLine = (lineId) => {
    setSelected(prev => {
      const next = new Set(prev)
      if (next.has(lineId)) next.delete(lineId)
      else next.add(lineId)
      return next
    })
  }

  const toggleAll = () => {
    if (selected.size === lines.length && lines.length > 0) {
      setSelected(new Set())
    } else {
      setSelected(new Set(lines.map(l => l.lineId)))
    }
  }

  const selectedLines = useMemo(() =>
    lines.filter(l => selected.has(l.lineId)),
  [lines, selected])

  const handleCreate = async () => {
    if (selected.size === 0) return
    setSaving(true)
    setSaveError(null)
    try {
      const res = await fetch('/Purchase/CreatePurchaseDemand', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'RequestVerificationToken': antiForgeryToken || '',
        },
        body: JSON.stringify({
          lineIds: [...selected],
          notes: notes.trim() || null,
        }),
      })
      const data = await res.json()
      if (data.ok) {
        window.location.href = '/Purchase/PurchaseDemands'
      } else {
        setSaveError(data.error || 'Belge oluşturulamadı.')
      }
    } catch (e) {
      setSaveError('İstek hatası: ' + e.message)
    } finally {
      setSaving(false)
    }
  }

  const formatQty = (n) => {
    if (n == null) return '—'
    return Number(n).toLocaleString('tr-TR', { minimumFractionDigits: 0, maximumFractionDigits: 2 })
  }

  const allChecked = lines.length > 0 && selected.size === lines.length
  const someChecked = selected.size > 0 && selected.size < lines.length

  return (
    <div className="prw-root">
      {/* Header */}
      <div className="prw-header">
        <div className="prw-header__left">
          <div className="prw-header__icon">
            <svg xmlns="http://www.w3.org/2000/svg" width="20" height="20" viewBox="0 0 24 24"
              fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
              <path d="M6 2 3 6v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2V6l-3-4z"/>
              <line x1="3" y1="6" x2="21" y2="6"/>
              <path d="M16 10a4 4 0 0 1-8 0"/>
            </svg>
          </div>
          <div>
            <h1 className="prw-header__title">Satın Alma Talebi Oluştur</h1>
            <p className="prw-header__sub">Açık ihtiyaç kalemlerinden satın alma talebi belgesi oluşturun</p>
          </div>
        </div>
        <a href="/Purchase/PurchaseDemands" className="prw-btn prw-btn--ghost">
          <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24"
            fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <line x1="19" y1="12" x2="5" y2="12"/><polyline points="12 19 5 12 12 5"/>
          </svg>
          Geri
        </a>
      </div>

      <div className="prw-body">
        {/* Left: filters + line list */}
        <div className="prw-left">
          {/* Filters */}
          <div className="prw-filters">
            <div className="prw-filter-row">
              <div className="prw-search-wrap">
                <svg className="prw-search-icon" xmlns="http://www.w3.org/2000/svg" width="14" height="14"
                  viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"
                  strokeLinecap="round" strokeLinejoin="round">
                  <circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/>
                </svg>
                <input
                  className="prw-input prw-input--search"
                  placeholder="Malzeme kodu veya adı..."
                  value={matSearch}
                  onChange={e => setMatSearch(e.target.value)}
                />
              </div>
              <input
                className="prw-input"
                placeholder="Belge no..."
                value={docNoSearch}
                onChange={e => setDocNoSearch(e.target.value)}
              />
            </div>
            <div className="prw-filter-row prw-filter-row--chips">
              <span className="prw-filter-label">Stok:</span>
              {[
                { key: 'all',     label: 'Tümü' },
                { key: 'instock', label: 'Stokta var' },
                { key: 'nostock', label: 'Stok yok' },
              ].map(opt => (
                <button
                  key={opt.key}
                  className={'prw-chip' + (stockFilter === opt.key ? ' prw-chip--active' : '')}
                  onClick={() => setStockFilter(opt.key)}
                  type="button"
                >{opt.label}</button>
              ))}
            </div>
          </div>

          {/* Line list */}
          <div className="prw-table-wrap">
            {loading && (
              <div className="prw-empty">Yükleniyor...</div>
            )}
            {!loading && error && (
              <div className="prw-empty prw-empty--err">{error}</div>
            )}
            {!loading && !error && lines.length === 0 && (
              <div className="prw-empty">Açık ihtiyaç kalemi bulunamadı.</div>
            )}
            {!loading && !error && lines.length > 0 && (
              <table className="prw-table">
                <thead>
                  <tr>
                    <th className="prw-th prw-th--check">
                      <input
                        type="checkbox"
                        checked={allChecked}
                        ref={el => { if (el) el.indeterminate = someChecked }}
                        onChange={toggleAll}
                      />
                    </th>
                    <th className="prw-th">Malzeme</th>
                    <th className="prw-th">Miktar</th>
                    <th className="prw-th">Talep Edilen</th>
                    <th className="prw-th prw-th--right">Stok</th>
                    <th className="prw-th">Belge No</th>
                    <th className="prw-th">Tarih</th>
                  </tr>
                </thead>
                <tbody>
                  {lines.map(line => {
                    const isSelected = selected.has(line.lineId)
                    const hasStock = line.stockBalance > 0
                    return (
                      <tr
                        key={line.lineId}
                        className={'prw-tr' + (isSelected ? ' prw-tr--selected' : '')}
                        onClick={() => toggleLine(line.lineId)}
                      >
                        <td className="prw-td prw-td--check" onClick={e => e.stopPropagation()}>
                          <input
                            type="checkbox"
                            checked={isSelected}
                            onChange={() => toggleLine(line.lineId)}
                          />
                        </td>
                        <td className="prw-td">
                          <div className="prw-mat-name">{line.materialName || '—'}</div>
                          <div className="prw-mat-code">{line.materialCode || ''}</div>
                        </td>
                        <td className="prw-td prw-td--num">
                          {formatQty(line.remaining)}
                          <span className="prw-unit">{line.unitCode || ''}</span>
                        </td>
                        <td className="prw-td prw-td--num">
                          {formatQty(line.quantity)}
                          {line.fulfilledByPurchase > 0 && (
                            <span className="prw-fulfilled">{formatQty(line.fulfilledByPurchase)} karş.</span>
                          )}
                        </td>
                        <td className="prw-td prw-td--right">
                          {hasStock ? (
                            <span className="prw-badge prw-badge--stock">{formatQty(line.stockBalance)}</span>
                          ) : (
                            <span className="prw-badge prw-badge--nostock">Yok</span>
                          )}
                        </td>
                        <td className="prw-td">
                          <span className="prw-docno">{line.docNumber}</span>
                        </td>
                        <td className="prw-td prw-td--date">{line.docDate}</td>
                      </tr>
                    )
                  })}
                </tbody>
              </table>
            )}
          </div>

          {/* Count footer */}
          {lines.length > 0 && (
            <div className="prw-foot">
              <span>{lines.length} kalem</span>
              {selected.size > 0 && (
                <span className="prw-foot__sel">{selected.size} seçili</span>
              )}
            </div>
          )}
        </div>

        {/* Right: summary + create */}
        <div className="prw-right">
          <div className="prw-summary-card">
            <h2 className="prw-summary-title">Satın Alma Talebi</h2>

            {selectedLines.length === 0 ? (
              <p className="prw-summary-empty">Kalem seçilmedi.<br/>Soldaki listeden kalem seçin.</p>
            ) : (
              <div className="prw-summary-lines">
                {selectedLines.map(l => (
                  <div key={l.lineId} className="prw-summary-line">
                    <div className="prw-summary-line__name">{l.materialName || l.materialCode || '—'}</div>
                    <div className="prw-summary-line__meta">
                      <span className="prw-summary-line__qty">{formatQty(l.remaining)} {l.unitCode || ''}</span>
                      <span className="prw-summary-line__doc">{l.docNumber}</span>
                    </div>
                    <button
                      className="prw-summary-line__remove"
                      onClick={() => toggleLine(l.lineId)}
                      title="Çıkar"
                      type="button"
                    >×</button>
                  </div>
                ))}
              </div>
            )}

            <div className="prw-notes-wrap">
              <label className="prw-label" htmlFor="prw-notes">Notlar</label>
              <textarea
                id="prw-notes"
                className="prw-textarea"
                placeholder="İsteğe bağlı notlar..."
                rows={3}
                value={notes}
                onChange={e => setNotes(e.target.value)}
              />
            </div>

            {saveError && (
              <div className="prw-save-error">{saveError}</div>
            )}

            <button
              className="prw-btn prw-btn--primary prw-btn--full"
              disabled={selected.size === 0 || saving}
              onClick={handleCreate}
              type="button"
            >
              {saving ? 'Oluşturuluyor...' : `Satın Alma Talebi Oluştur (${selected.size} kalem)`}
            </button>

            <a href="/Purchase/PurchaseDemands" className="prw-btn prw-btn--ghost prw-btn--full prw-btn--cancel">
              İptal
            </a>
          </div>
        </div>
      </div>
    </div>
  )
}
