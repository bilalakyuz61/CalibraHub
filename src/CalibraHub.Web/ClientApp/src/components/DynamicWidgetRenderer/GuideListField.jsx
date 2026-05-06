/**
 * GuideListField — Salt okunur akordion rehber listesi widget'i.
 *
 * Davranis:
 *   - Default kapali; <details><summary> ile akordion
 *   - Acildiginda guideSchema + guideSearch (50/sayfa, infinite scroll)
 *   - WHERE constraint runtime'da DynamicWidgetRenderer'da resolve edilip
 *     props.constraints olarak iletilir; degisiklikte cache invalidate olur.
 *   - Selection yok, click yok — pasif goruntuleme
 *   - WidgetTra'ya deger yazmaz (read-only widget tipi: 'guide-list')
 *
 * Props:
 *   widgetId      string
 *   label         string
 *   guideCode     string  (orn. 'PURCHASE_HISTORY')
 *   guideConfig   object | null  (admin GuideCustomizationModal'dan: {viewCode,columns,constraint})
 *   constraints   string | null  (runtime'da token'lar resolve edilmis WHERE fragment)
 *   classPrefix   string  ('mce' | 'sqe' | 'wf' ...)
 */
import { useEffect, useRef, useState, useMemo } from 'react'
import { ChevronDown, Loader2, AlertCircle, Search, X } from 'lucide-react'
import { guideSchema, guideSearch } from './dynamicWidgetService'

var PAGE_SIZE = 50

/**
 * Constraint string'ini backend formatina cevir.
 *   - JSON array ise (`[{rawSql,logic}]` veya `[{field,operator,value}]`) direkt kullan
 *   - Aksi halde raw SQL kabul edip `[{rawSql, logic:'and'}]` formatina sarmala
 * Backend GuidesController constraint'i JSON array bekliyor; mergeConstraints adapter
 * GuideLookupModal'da bunu yapiyor — biz GuideListField'da minimal versiyonunu uygularız.
 */
function buildConstraintsParam(raw) {
  if (raw == null) return undefined
  var s = String(raw).trim()
  if (!s) return undefined
  if (s.charAt(0) === '[') {
    try { JSON.parse(s); return s } catch (_) { /* fallthrough */ }
  }
  return JSON.stringify([{ rawSql: s, logic: 'and' }])
}

export default function GuideListField(props) {
  var guideCode   = props.guideCode || ''
  var guideConfig = props.guideConfig || null
  var constraints = props.constraints || null
  var label       = props.label || 'Rehber Listesi'
  // Modal/popup baglaminda summary tiklamasi olmadan icerik daima goruntulenir.
  var alwaysOpen  = props.alwaysOpen === true

  var [open, setOpen]         = useState(alwaysOpen)
  var [schema, setSchema]     = useState(null)   // { columns, defaultSortColumn, defaultSortDirection }
  var [rows, setRows]         = useState([])
  var [page, setPage]         = useState(1)
  var [hasMore, setHasMore]   = useState(false)
  var [sortCol, setSortCol]   = useState('')
  var [sortDir, setSortDir]   = useState('ASC')
  var [loading, setLoading]   = useState(false)
  var [error, setError]       = useState('')
  var [totalLoaded, setTotal] = useState(0)
  var [searchText, setSearchText] = useState('')
  var [searchDebounced, setSearchDebounced] = useState('')
  var sentinelRef = useRef(null)
  var openedOnceRef = useRef(false)

  // Admin'den gelen "Görünür Kolonlar" konfigurasyonu — guideConfig.columns
  // [{name,label,visible,distinct}]. visible=false olanlar tabloda gizlenir.
  var configCols = useMemo(function() {
    if (!guideConfig) return null
    try {
      var cfg = typeof guideConfig === 'string' ? JSON.parse(guideConfig) : guideConfig
      return Array.isArray(cfg && cfg.columns) ? cfg.columns : null
    } catch (e) { return null }
  }, [guideConfig])

  // searchEnabled — admin tarafindan opsiyonel olarak acilir, tablo ustunde
  // free-text search input'u render eder. guideConfig.searchEnabled flag.
  var searchEnabled = useMemo(function() {
    if (!guideConfig) return false
    try {
      var cfg = typeof guideConfig === 'string' ? JSON.parse(guideConfig) : guideConfig
      return !!(cfg && cfg.searchEnabled)
    } catch (e) { return false }
  }, [guideConfig])

  // Debounced search — kullanici yazma sirasinda her keystroke'ta fetch yapma.
  useEffect(function() {
    var t = setTimeout(function() { setSearchDebounced(searchText) }, 280)
    return function() { clearTimeout(t) }
  }, [searchText])

  // Goruntulenen kolon listesi — schema.columns string array; admin override
  // (configCols) ile etiket/gorunurluk uygulanir.
  var displayColumns = useMemo(function() {
    if (!schema || !Array.isArray(schema.columns)) return []
    var cfgMap = {}
    if (configCols) configCols.forEach(function(c) {
      if (c && c.name) cfgMap[String(c.name).toLowerCase()] = c
    })
    var visible = []
    schema.columns.forEach(function(col) {
      // Backend PR 3 sonrasi GuideSchemaDto.Columns = IReadOnlyCollection<string>
      // (sadece kolon adlari). Eski nesne formatini da destekle (defansif).
      var name = (typeof col === 'string') ? col : String(col && (col.name || col.column || ''))
      if (!name) return
      var key = name.toLowerCase()
      var ov  = cfgMap[key]
      if (ov && ov.visible === false) return
      visible.push({
        name:     name,
        label:    (ov && ov.label) || name,
        sortable: true,
      })
    })
    return visible
  }, [schema, configCols])

  // Ilk acilista schema fetch
  useEffect(function() {
    if (!open || !guideCode || schema) return undefined
    var cancelled = false
    setError('')
    guideSchema(guideCode)
      .then(function(s) {
        if (cancelled) return
        if (!s) { setError('Rehber bulunamadı: ' + guideCode); return }
        setSchema(s)
        setSortCol(s.defaultSortColumn || '')
        setSortDir((s.defaultSortDirection || 'ASC').toUpperCase())
      })
      .catch(function(e) { if (!cancelled) setError(e.message || 'Schema yüklenemedi') })
    return function() { cancelled = true }
  }, [open, guideCode, schema])

  // Akordion ilk acildiginda VEYA constraints/sort degistiginde liste yeniden cek
  // Schema gelmeden fetch yapma. Constraints/sort/guideCode degisiminde sayfa 1'e doner.
  useEffect(function() {
    if (!open || !schema) return undefined
    var cancelled = false
    setLoading(true)
    setError('')
    setRows([])
    setPage(1)
    setTotal(0)
    guideSearch(guideCode, {
      page: 1,
      pageSize: PAGE_SIZE,
      sortColumn: sortCol || undefined,
      sortDirection: sortDir,
      search: searchEnabled && searchDebounced ? searchDebounced : undefined,
      constraints: buildConstraintsParam(constraints),
    })
      .then(function(res) {
        if (cancelled) return
        var list = (res && res.rows) || []
        setRows(list)
        setHasMore(!!(res && res.hasMore))
        setTotal(list.length)
      })
      .catch(function(e) {
        if (!cancelled) setError(e.message || 'Liste alınamadı')
      })
      .finally(function() { if (!cancelled) setLoading(false) })
    openedOnceRef.current = true
    return function() { cancelled = true }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, schema, guideCode, constraints, sortCol, sortDir, searchDebounced, searchEnabled])

  // Sonraki sayfa fetch
  function loadMore() {
    if (loading || !hasMore || !schema) return
    setLoading(true)
    var nextPage = page + 1
    guideSearch(guideCode, {
      page: nextPage,
      pageSize: PAGE_SIZE,
      sortColumn: sortCol || undefined,
      sortDirection: sortDir,
      search: searchEnabled && searchDebounced ? searchDebounced : undefined,
      constraints: buildConstraintsParam(constraints),
    })
      .then(function(res) {
        var list = (res && res.rows) || []
        setRows(function(prev) { return prev.concat(list) })
        setPage(nextPage)
        setHasMore(!!(res && res.hasMore))
        setTotal(function(t) { return t + list.length })
      })
      .catch(function(e) { setError(e.message || 'Liste alınamadı') })
      .finally(function() { setLoading(false) })
  }

  // IntersectionObserver — sentinel goruse girince loadMore
  useEffect(function() {
    if (!open || !sentinelRef.current) return undefined
    var io = new IntersectionObserver(function(entries) {
      entries.forEach(function(en) {
        if (en.isIntersecting && hasMore && !loading) loadMore()
      })
    }, { rootMargin: '120px' })
    io.observe(sentinelRef.current)
    return function() { io.disconnect() }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, hasMore, loading, page, sortCol, sortDir, constraints])

  function handleSortClick(colName, sortable) {
    if (!sortable) return
    if (sortCol === colName) {
      setSortDir(sortDir === 'ASC' ? 'DESC' : 'ASC')
    } else {
      setSortCol(colName)
      setSortDir('ASC')
    }
  }

  function formatCell(v) {
    if (v == null) return ''
    if (typeof v === 'object') {
      try { return JSON.stringify(v) } catch (e) { return String(v) }
    }
    return String(v)
  }

  return (
    <details
      className={'wf-guide-list' + (alwaysOpen ? ' wf-guide-list--bare' : '')}
      open={alwaysOpen ? true : open}
      onToggle={function(e) {
        if (alwaysOpen) { e.currentTarget.open = true; return }
        setOpen(e.currentTarget.open)
      }}
    >
      {/* Summary daima render edilir — alwaysOpen mode'da sadece baslik (label)
          gosterilir; chevron + count rozeti + tiklama davranisi gizlenir.
          Toggle modu (alwaysOpen=false) → tam etkilesimli akordion bashlik. */}
      <summary
        className={'wf-guide-list-summary' + (alwaysOpen ? ' wf-guide-list-summary--static' : '')}
        style={alwaysOpen ? { cursor: 'default', listStyle: 'none' } : undefined}
        onClick={alwaysOpen ? function(e) { e.preventDefault() } : undefined}
      >
        <span className="wf-guide-list-title">{label}</span>
        {!alwaysOpen && open && totalLoaded > 0 && (
          <span className="wf-guide-list-count">{totalLoaded}{hasMore ? '+' : ''} satır</span>
        )}
        {!alwaysOpen && <ChevronDown size={14} className="wf-guide-list-chevron" />}
      </summary>
      <div className="wf-guide-list-body">
        {!guideCode && (
          <div className="wf-guide-list-empty">
            <AlertCircle size={14} /> Rehber tanımlı değil — Alan Ayarları'ndan view seçin.
          </div>
        )}
        {error && (
          <div className="wf-guide-list-error">
            <AlertCircle size={14} /> {error}
          </div>
        )}
        {!error && schema && displayColumns.length > 0 && searchEnabled && (
          <div className="wf-guide-list-search">
            <Search size={13} className="wf-guide-list-search-icon" />
            <input
              type="text"
              value={searchText}
              onChange={function(e) { setSearchText(e.target.value) }}
              placeholder="Listede ara…"
              className="wf-guide-list-search-input"
            />
            {searchText && (
              <button
                type="button"
                onClick={function() { setSearchText('') }}
                aria-label="Temizle"
                className="wf-guide-list-search-clear"
              ><X size={12} /></button>
            )}
          </div>
        )}
        {!error && schema && displayColumns.length > 0 && (
          <div className="wf-guide-list-table-wrap">
            <table className="wf-guide-list-table">
              <thead>
                <tr>
                  {displayColumns.map(function(col) {
                    var isSorted = sortCol === col.name
                    return (
                      <th
                        key={col.name}
                        className={'wf-guide-list-th' + (col.sortable ? ' is-sortable' : '') + (isSorted ? ' is-sorted' : '')}
                        onClick={function() { handleSortClick(col.name, col.sortable) }}
                      >
                        {col.label}
                        {isSorted && (
                          <span className="wf-guide-list-sort-arrow">{sortDir === 'ASC' ? '▲' : '▼'}</span>
                        )}
                      </th>
                    )
                  })}
                </tr>
              </thead>
              <tbody>
                {rows.length === 0 && !loading && (
                  <tr><td className="wf-guide-list-empty-row" colSpan={displayColumns.length}>Sonuç bulunamadı</td></tr>
                )}
                {rows.map(function(r, i) {
                  var key = r.value || r.id || i
                  var cells = (r.cells && typeof r.cells === 'object') ? r.cells : r
                  return (
                    <tr key={String(key) + '-' + i}>
                      {displayColumns.map(function(col) {
                        return <td key={col.name}>{formatCell(cells[col.name])}</td>
                      })}
                    </tr>
                  )
                })}
              </tbody>
            </table>
            {/* Infinite scroll sentinel */}
            <div ref={sentinelRef} className="wf-guide-list-sentinel" />
            {loading && (
              <div className="wf-guide-list-loading"><Loader2 size={14} className="wf-guide-list-spin" /> Yükleniyor…</div>
            )}
          </div>
        )}
      </div>
    </details>
  )
}
