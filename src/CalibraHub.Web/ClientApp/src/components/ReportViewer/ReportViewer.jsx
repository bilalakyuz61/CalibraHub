import React, { useState, useEffect, useRef } from 'react'
import PanelChart, { distinctVals } from '../ReportDesigner/PanelChart'
import FilterField from '../ReportDesigner/FilterField'
import ReportGrid, { ensureLayouts, ensurePageSource } from '../ReportDesigner/ReportGrid'

function normalizePagesData(raw) {
  if (!Array.isArray(raw) || raw.length === 0) return []
  if (typeof raw[0].panels !== 'undefined') return raw
  return [{ id: 'pg_1', title: 'Sayfa 1', panels: raw }]
}

const EMPTY_FILTERS = {}

export default function ReportViewer({ loadUrl }) {
  const [title,          setTitle]         = useState('')
  const [pages,          setPages]         = useState([])
  const [currentPageIdx, setCurrentPageIdx] = useState(0)
  const [filtersByPage, setFiltersByPage] = useState({})
  const [viewFields, setViewFields] = useState({})   // view → alan adları (filtre eşleştirme)
  const [loading, setLoading] = useState(true)
  const [error,   setError]   = useState(null)

  const handleFilterChange = (key, entry) => {
    const pg = pages[currentPageIdx]
    if (!pg) return
    setFiltersByPage(prev => ({ ...prev, [pg.id]: { ...(prev[pg.id] || {}), [key]: entry } }))
  }

  // ── Dışa aktarma ──────────────────────────────────────────────
  const [exportOpen, setExportOpen] = useState(false)
  const [filtersOpen, setFiltersOpen] = useState(false)  // sol kaymalı filtre drawer'ı aç/kapa
  const panelData = useRef({})   // panelId → { title, type, columns, rows } (export için)
  const [pdata, setPdata] = useState({})   // panelId → { columns, rows } (filtre değerleri için)

  const handlePanelData = (panel, d) => {
    panelData.current[panel.id] = { title: panel.title, type: panel.type, columns: d.columns, rows: d.rows }
    setPdata(prev => (prev[panel.id] === d ? prev : { ...prev, [panel.id]: d }))
  }

  function doExportPdf() {
    setExportOpen(false)
    setTimeout(() => window.print(), 50)
  }

  function doExportExcel() {
    setExportOpen(false)
    const pg = pages[currentPageIdx]
    if (!pg) return
    const sheets = []
    ;(pg.panels || []).forEach((p, i) => {
      if (p.type === 'filter') return
      const d = panelData.current[p.id]
      if (!d || !d.columns || !d.columns.length || !d.rows || !d.rows.length) return

      // Tablo panelinde: yalnız panelde GÖRÜNEN kolonlar, panel sırasıyla, panel etiketleriyle
      let cols
      if (p.type === 'table') {
        const cfg     = p.columns || {}
        const order   = p.columnOrder || []
        const inOrder = order.filter(n => d.columns.includes(n))
        const rest    = d.columns.filter(n => !inOrder.includes(n))
        cols = [...inOrder, ...rest]
          .filter(n => !(cfg[n] && cfg[n].visible === false))
          .map(n => ({ idx: d.columns.indexOf(n), label: (cfg[n] && cfg[n].label) || n }))
      } else {
        cols = d.columns.map((c, ci) => ({ idx: ci, label: String(c) }))
      }
      if (!cols.length) return

      const headers = cols.map((c, ci) => ({ id: 'c' + ci, label: String(c.label) }))
      const rows = d.rows.map(r => {
        const o = {}
        cols.forEach((c, ci) => { o['c' + ci] = r[c.idx] })
        return o
      })
      sheets.push({ sheetName: (p.title || ('Panel ' + (i + 1))).slice(0, 31), headers, rows })
    })
    if (!sheets.length) {
      if (window.CalibraHub && window.CalibraHub.toast) window.CalibraHub.toast('Aktarılacak veri yok — paneller yüklenince tekrar deneyin.', 'err')
      else window.alert('Aktarılacak veri yok.')
      return
    }
    const ts = new Date()
    const pad = n => (n < 10 ? '0' + n : '' + n)
    const stamp = ts.getFullYear() + pad(ts.getMonth() + 1) + pad(ts.getDate()) + '_' + pad(ts.getHours()) + pad(ts.getMinutes())
    const payload = { fileName: ((title || 'rapor') + '_' + stamp + '.xlsx'), sheets }

    const form = document.createElement('form')
    form.method = 'POST'
    form.action = '/api/export/report-excel'
    form.target = '_self'
    form.style.display = 'none'
    const ta = document.createElement('textarea')
    ta.name = 'payload'
    ta.value = JSON.stringify(payload)
    form.appendChild(ta)
    document.body.appendChild(form)
    form.submit()
    setTimeout(() => { if (form.parentNode) form.parentNode.removeChild(form) }, 1500)
  }

  useEffect(() => {
    fetch(loadUrl, { credentials: 'same-origin' })
      .then(r => r.json())
      .then(d => {
        if (!d.ok) { setError(d.error || 'Yüklenemedi'); return }
        setTitle(d.title || 'Rapor')
        try {
          const raw = JSON.parse(d.panelsJson || '[]')
          setPages(ensurePageSource(ensureLayouts(normalizePagesData(raw))))
        } catch { setPages([]) }
      })
      .catch(() => setError('Yükleme hatası'))
      .finally(() => setLoading(false))
  }, [loadUrl])

  // View → alan adları (filtrenin alan-adı eşleştirmesi için)
  useEffect(() => {
    fetch('/Dashboard/DesignerSources', { credentials: 'same-origin' })
      .then(r => r.ok ? r.json() : [])
      .then(data => {
        const m = {}
        ;(data || []).forEach(s => {
          const f = []
          ;(s.metrics || []).forEach(x => f.push(x.value))
          ;(s.groups  || []).forEach(x => f.push(x.value))
          m[s.name] = f
        })
        setViewFields(m)
      })
      .catch(() => {})
  }, [])

  // ESC ile filtre drawer'ını kapat
  useEffect(() => {
    if (!filtersOpen) return undefined
    const onKey = e => { if (e.key === 'Escape') setFiltersOpen(false) }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [filtersOpen])

  const currentPage      = pages[currentPageIdx] ?? pages[0]
  const panels           = currentPage?.panels ?? []
  const filterPanels     = panels.filter(p => p.type === 'filter')
  const dataPanels       = panels.filter(p => p.type !== 'filter')
  const hasMultiplePages = pages.length > 1
  const pageFilters      = (currentPage && filtersByPage[currentPage.id]) || EMPTY_FILTERS
  const activeFilterCount = Object.values(pageFilters).filter(e => e && Array.isArray(e.values) && e.values.length > 0).length

  // "Filtrede kullan" işaretli kolonlardan otomatik filtre alanları (sol ray)
  const pageSrcName = currentPage?.source?.sourceName || currentPage?.source?.source || ''
  const filterFields = (() => {
    const out = [], seen = new Set()
    dataPanels.forEach(p => {
      const cc = p.columns || {}
      Object.keys(cc).forEach(name => {
        if (cc[name] && cc[name].filter && !seen.has(name)) {
          seen.add(name)
          out.push({ field: name, label: cc[name].label || name, source: pageSrcName })
        }
      })
    })
    return out
  })()
  function distinctOf(field) {
    for (const pid in pdata) {
      const d = pdata[pid]
      if (!d || !d.columns) continue
      const ci = d.columns.indexOf(field)
      if (ci >= 0) return distinctVals(d.rows || [], ci)
    }
    return []
  }

  return (
    <div className="rv-root">
      <header className="rv-topbar">
        <button type="button" className="rv-btn rv-btn--ghost" onClick={() => window.history.back()}>
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" style={{ width: 14, height: 14 }}>
            <path d="m15 18-6-6 6-6" />
          </svg>
          Geri
        </button>

        <h1 className="rv-title">{loading ? '…' : title}</h1>

        <div style={{ flex: 1 }} />

        {!loading && !error && pages.length > 0 && (filterPanels.length > 0 || filterFields.length > 0) && (
          <button type="button" className="rv-btn rv-btn--outline" onClick={() => setFiltersOpen(true)}>
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" style={{ width: 13, height: 13 }}>
              <polygon points="22 3 2 3 10 12.46 10 19 14 21 14 12.46 22 3" />
            </svg>
            Filtre
            {activeFilterCount > 0 && <span className="rv-fcount">{activeFilterCount}</span>}
          </button>
        )}

        {!loading && !error && pages.length > 0 && (
          <div className="rv-export">
            <button type="button" className="rv-btn rv-btn--outline" onClick={() => setExportOpen(o => !o)}>
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" style={{ width: 13, height: 13 }}>
                <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" /><polyline points="7 10 12 15 17 10" /><line x1="12" y1="15" x2="12" y2="3" />
              </svg>
              Dışa Aktar
            </button>
            {exportOpen && (
              <>
                <div className="rv-export__backdrop" onClick={() => setExportOpen(false)} />
                <div className="rv-export__menu">
                  <button type="button" onClick={doExportPdf}>
                    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" style={{ width: 13, height: 13 }}>
                      <path d="M6 9V2h12v7" /><path d="M6 18H4a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2h-2" /><rect x="6" y="14" width="12" height="8" rx="1" />
                    </svg>
                    PDF (yazdır)
                  </button>
                  <button type="button" onClick={doExportExcel}>
                    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" style={{ width: 13, height: 13 }}>
                      <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" /><polyline points="14 2 14 8 20 8" /><line x1="9" y1="13" x2="15" y2="19" /><line x1="15" y1="13" x2="9" y2="19" />
                    </svg>
                    Excel (veri)
                  </button>
                </div>
              </>
            )}
          </div>
        )}

      </header>

      {!loading && !error && hasMultiplePages && (
        <div className="rv-pages">
          {pages.map((pg, idx) => (
            <button
              key={pg.id}
              type="button"
              className={`rv-page-tab${idx === currentPageIdx ? ' rv-page-tab--active' : ''}`}
              onClick={() => setCurrentPageIdx(idx)}
            >
              {pg.title}
            </button>
          ))}
        </div>
      )}

      <div className="rv-body">
        <div className="rv-canvas">
          {loading && (
            <div className="rv-state">
              <svg className="rd-spin" viewBox="0 0 24 24" fill="none" stroke="#6366f1" strokeWidth="2.5" style={{ width: 28, height: 28 }}>
                <path d="M21 12a9 9 0 1 1-6.219-8.56" />
              </svg>
            </div>
          )}

          {error && (
            <div className="rv-state rv-state--error">{error}</div>
          )}

          {!loading && !error && dataPanels.length === 0 && (
            <div className="rv-state rv-state--empty">Bu sayfada henüz panel yok.</div>
          )}

          {!loading && !error && dataPanels.length > 0 && (
            <ReportGrid
              panels={dataPanels}
              editable={false}
              renderPanel={panel => (
                <div className="rv-panel">
                  <div className="rv-panel__head">
                    <span className="rv-panel__title">{panel.title || 'Panel'}</span>
                    <span className="rv-panel__type">{panel.type}</span>
                  </div>
                  <div className="rv-panel__chart">
                    <PanelChart panel={{ ...panel, ...(currentPage?.source || {}) }} chartHeight="full" activeFilters={pageFilters} onFilterChange={handleFilterChange} onData={d => handlePanelData(panel, d)} viewFields={viewFields} />
                  </div>
                </div>
              )}
            />
          )}
        </div>
      </div>

      {/* Sol kaymalı filtre drawer — C-Grid tarzı (backdrop + slide-in) */}
      {!loading && !error && (filterPanels.length > 0 || filterFields.length > 0) && (
        <>
          <div
            className={`rv-fdrawer__backdrop${filtersOpen ? ' is-open' : ''}`}
            onClick={() => setFiltersOpen(false)}
          />
          <aside className={`rv-fdrawer${filtersOpen ? ' is-open' : ''}`} aria-hidden={!filtersOpen}>
            <div className="rv-fdrawer__head">
              <span className="rv-fdrawer__icon">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" style={{ width: 14, height: 14 }}>
                  <polygon points="22 3 2 3 10 12.46 10 19 14 21 14 12.46 22 3" />
                </svg>
              </span>
              <span className="rv-fdrawer__title">Filtreler</span>
              <button type="button" className="rv-fdrawer__close" onClick={() => setFiltersOpen(false)} title="Kapat (Esc)">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" style={{ width: 15, height: 15 }}>
                  <path d="M18 6 6 18M6 6l12 12" />
                </svg>
              </button>
            </div>
            <div className="rv-fdrawer__list">
              {filterFields.map(ff => (
                <FilterField
                  key={ff.field}
                  label={ff.label}
                  values={distinctOf(ff.field)}
                  selected={(pageFilters[ff.field] && pageFilters[ff.field].values) || []}
                  onChange={vals => handleFilterChange(ff.field, { source: ff.source, field: ff.field, values: vals })}
                />
              ))}
              {filterPanels.map(panel => (
                <div key={panel.id} className="rv-filters__item">
                  <PanelChart panel={{ ...panel, ...(currentPage?.source || {}) }} chartHeight={260} activeFilters={pageFilters} onFilterChange={handleFilterChange} />
                </div>
              ))}
            </div>
          </aside>
        </>
      )}
    </div>
  )
}
