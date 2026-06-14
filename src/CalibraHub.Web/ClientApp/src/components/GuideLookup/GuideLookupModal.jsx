/**
 * GuideLookupModal — Iki tip rehber icin (Sabit alan & Widget) tek runtime
 * arama/secim modali. Bir gelistirme tek noktada (bu dosya) yapildiginda
 * iki tip de otomatik olarak ozelligi kazanir.
 *
 * Ozellik seti:
 *   - Schema fetch (kolon listesi + label)
 *   - Debounced arama (300ms)
 *   - Infinite scroll (IntersectionObserver, sayfa boyutu 50)
 *   - Excel-tarzi distinct kolon filtre popover'i (kolon basligindaki huni
 *     ikonu — `columns[i].distinct === true` olan kolonlarda)
 *   - Cift tiklama satir secimi (sabit; her iki tip de ayni davranis)
 *   - ESC ile kapanma (acik popover varsa once popover, sonra modal)
 *   - Footer: kayit sayisi + aktif filtre sayisi
 *
 * Props:
 *   guideCode         — string (zorunlu)
 *   guideLabel        — string (opsiyonel, baslik metni)
 *   columnsAdapter    — (schemaColumns: string[]) => Array<{ name, label, visible, distinct }>
 *                       Schema yuklendiginde cagrilir; iki tip icin guideLookupAdapters'tan
 *                       (adaptFormatJson / adaptGuideConfig) closure'lanmis fonksiyon gecirilir.
 *   open              — boolean
 *   onClose           — () => void
 *   onPick            — (row: { value, display, cells }) => void
 *   staticConstraint  — Tip 1: filterJson  /  Tip 2: guideConfig.constraint  (string ya da array)
 *   runtimeConstraint — Tip 2: DynamicWidgetRenderer'in {w_xxx} resolve etmis hali (string)
 *   schemaVersion     — opsiyonel, schema cache'ini sifirlamak icin (FieldSettings save sonrasi)
 *   headerActions     — opsiyonel, header'a sigdirilan ek butonlar (Tip 1'de "Alan Ayarlari")
 *
 * Sozlesme:
 *   row = { value, display, cells: { [col]: any } }  — Tip 1 fillMap
 *   icin row.cells'e ihtiyac duyar; Tip 2 yalnizca value/display tuketir.
 */
import { useState, useEffect, useRef, useMemo, useCallback } from 'react'
import { createPortal } from 'react-dom'
import { Search, X, Loader2, Filter } from 'lucide-react'
import { guideSchema, guideSearch, guideDistinct } from '../DynamicWidgetRenderer/dynamicWidgetService'
import { mergeConstraints } from './guideLookupAdapters'
import ColumnFilterPopover from './ColumnFilterPopover'

var PAGE_SIZE = 50

export default function GuideLookupModal(props) {
  var guideCode         = props.guideCode
  var guideLabel        = props.guideLabel
  var columnsAdapter    = props.columnsAdapter
  var open              = !!props.open
  var onClose           = props.onClose
  var onPick            = props.onPick
  var staticConstraint  = props.staticConstraint || null
  var runtimeConstraint = props.runtimeConstraint || null
  var schemaVersion     = props.schemaVersion
  var headerActions     = props.headerActions

  var [schema, setSchema]   = useState(null)
  var [search, setSearch]   = useState('')
  var [rows, setRows]       = useState([])
  var [page, setPage]       = useState(1)
  var [hasMore, setHasMore] = useState(false)
  var [loading, setLoading] = useState(false)
  var [error, setError]     = useState(null)

  // Distinct popover icin sunucudan cekilmis benzersiz degerler ve secimler
  var [distinctData, setDistinctData] = useState({})
  var [distinctSel,  setDistinctSel]  = useState({})

  // Acik popover (kolon adi) + acan butonun rect'i
  var [openCol, setOpenCol]       = useState(null)
  var [anchorRect, setAnchorRect] = useState(null)

  // Popover arama: kullanicinin yazdigi raw + 300ms debounced kopyasi.
  // Debounced'a yazilan deger, server-tarafi guideDistinct(q) cagrisini tetikler.
  var [popoverSearch, setPopoverSearch]                 = useState('')
  var [popoverSearchDebounced, setPopoverSearchDebounced] = useState('')
  var [distinctLoading, setDistinctLoading]             = useState(false)

  var sentinelRef = useRef(null)
  var scrollRef   = useRef(null)

  // Schema yuklendiginde adapter'i cagir → birlesik columns
  var columns = useMemo(function () {
    if (!schema || !Array.isArray(schema.columns)) return []
    if (typeof columnsAdapter === 'function') {
      try { return columnsAdapter(schema.columns) || [] } catch (e) { return [] }
    }
    // Adapter verilmemisse schema'daki tum kolonlari default ile uret
    return schema.columns.map(function (n) {
      return { name: n, label: n, visible: true, distinct: false }
    })
  }, [schema, columnsAdapter])

  var distinctColumns = useMemo(function () {
    return columns
      .filter(function (c) { return c && c.distinct === true && c.visible !== false })
      .map(function (c) { return c.name })
  }, [columns])

  var visibleColumns = useMemo(function () {
    return columns.filter(function (c) { return c && c.visible !== false })
  }, [columns])

  // ── Modal kapanis / acilis state reset ──
  useEffect(function () {
    if (open) {
      setSearch('')
      setRows([])
      setPage(1)
      setHasMore(false)
      setError(null)
      setDistinctSel({})
      setDistinctData({})
      setOpenCol(null)
      setAnchorRect(null)
      setPopoverSearch('')
      setPopoverSearchDebounced('')
      setDistinctLoading(false)
    }
  }, [open])

  // ── schemaVersion degistiyse schema cache'ini at (FieldSettings save sonrasi) ──
  useEffect(function () {
    setSchema(null)
  }, [schemaVersion, guideCode])

  // ── Schema fetch ──
  useEffect(function () {
    if (!open || schema || !guideCode) return
    var alive = true
    guideSchema(guideCode)
      .then(function (s) { if (alive) setSchema(s) })
      .catch(function (e) { if (alive) setError('Şema yüklenemedi: ' + e.message) })
    return function () { alive = false }
  }, [open, guideCode, schema])

  // ── Popover acilinca arama state'ini sifirla ──
  useEffect(function () {
    setPopoverSearch('')
    setPopoverSearchDebounced('')
  }, [openCol])

  // ── Popover arama input'unu 300ms debounce et ──
  useEffect(function () {
    var h = setTimeout(function () { setPopoverSearchDebounced(popoverSearch) }, 300)
    return function () { clearTimeout(h) }
  }, [popoverSearch])

  // ── Distinct popover icin constraint — listede gosterilen WHERE'in aynisi ──
  // Static (FldSet filterJson) + runtime (Tip 2 cascading token'lar) + diger
  // kolon distinctSel'leri DAHIL EDILIR; ama AKTIF kolonun kendi secimi DAHIL
  // EDILMEZ — yoksa popover icindeki kontroller kendi sectiklerini filtrelerdi.
  // (Excel-tarzi cross-column awareness.)
  var distinctConstraint = useMemo(function () {
    if (!openCol) return null
    var others = {}
    Object.keys(distinctSel).forEach(function (col) {
      if (col !== openCol) others[col] = distinctSel[col]
    })
    return mergeConstraints(staticConstraint, runtimeConstraint, others)
  }, [openCol, staticConstraint, runtimeConstraint, distinctSel])

  // ── Distinct degerleri fetch et — popover acilinca + debounced search degisince ──
  // Onceki "modal acilinca tum distinct kolonlari onden cek" yaklasiminin yerine
  // "yalnizca acik popover icin gerektikce cek" yaklasimi: lazy + sunucu-tarafi
  // arama → kullanici yazdiginda alfabetik kuyruktaki gizli kayitlara da ulasilir.
  useEffect(function () {
    if (!open || !guideCode || !openCol) return
    var alive = true
    setDistinctLoading(true)
    guideDistinct(guideCode, openCol, popoverSearchDebounced || undefined, distinctConstraint || undefined)
      .then(function (arr) {
        if (!alive) return
        var values = Array.isArray(arr) ? arr : []
        setDistinctData(function (prev) {
          var next = Object.assign({}, prev)
          next[openCol] = values
          return next
        })
      })
      .catch(function () { /* sessizce gec */ })
      .finally(function () { if (alive) setDistinctLoading(false) })
    return function () { alive = false }
  }, [open, guideCode, openCol, popoverSearchDebounced, distinctConstraint])

  // ── Birlestirilmis constraint ──
  var mergedConstraint = useMemo(function () {
    return mergeConstraints(staticConstraint, runtimeConstraint, distinctSel)
  }, [staticConstraint, runtimeConstraint, distinctSel])

  // ── Debounced search + reset ──
  var loadPage = useCallback(function (pageNumber, searchTerm, replace) {
    if (!guideCode) return
    setLoading(true)
    setError(null)
    guideSearch(guideCode, {
      search: searchTerm,
      page: pageNumber,
      pageSize: PAGE_SIZE,
      constraints: mergedConstraint || undefined,
    })
      .then(function (result) {
        if (!result) {
          setRows([])
          setHasMore(false)
          setError('Rehber bulunamadı: ' + guideCode)
          return
        }
        if (replace) setRows(result.rows || [])
        else setRows(function (prev) { return prev.concat(result.rows || []) })
        setHasMore(!!result.hasMore)
      })
      .catch(function (e) {
        setError('Arama başarısız: ' + e.message)
        if (replace) setRows([])
        setHasMore(false)
      })
      .finally(function () { setLoading(false) })
  }, [guideCode, mergedConstraint])

  useEffect(function () {
    if (!open || !guideCode) return
    var handle = setTimeout(function () {
      setPage(1)
      loadPage(1, search, true)
    }, 300)
    return function () { clearTimeout(handle) }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [search, open, guideCode, mergedConstraint])

  // ── Infinite scroll ──
  useEffect(function () {
    if (!open || !hasMore || loading) return
    var el = sentinelRef.current
    if (!el) return
    var observer = new IntersectionObserver(function (entries) {
      entries.forEach(function (entry) {
        if (entry.isIntersecting) {
          var next = page + 1
          setPage(next)
          loadPage(next, search, false)
        }
      })
    }, { root: scrollRef.current, threshold: 0.1 })
    observer.observe(el)
    return function () { observer.disconnect() }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, hasMore, loading, page, search])

  // ── ESC ile kapanma (popover acikken once popover'i kapat) ──
  useEffect(function () {
    if (!open) return
    function onKey(e) {
      if (e.key !== 'Escape') return
      if (openCol) { setOpenCol(null); setAnchorRect(null) }
      else if (onClose) onClose()
    }
    document.addEventListener('keydown', onKey)
    return function () { document.removeEventListener('keydown', onKey) }
  }, [open, openCol, onClose])

  // ── Distinct popover ac/kapa ──
  function openColumnFilter(col, ev) {
    var btn = ev.currentTarget
    var rect = btn.getBoundingClientRect()
    setAnchorRect({ top: rect.top, left: rect.left, right: rect.right, bottom: rect.bottom })
    setOpenCol(col)
  }
  function closeColumnFilter() {
    setOpenCol(null)
    setAnchorRect(null)
  }
  function applyColumnFilter(col, newSel) {
    setDistinctSel(function (prev) {
      var updated = Object.assign({}, prev)
      if (!Array.isArray(newSel) || newSel.length === 0) delete updated[col]
      else updated[col] = newSel.slice()
      return updated
    })
    closeColumnFilter()
  }

  function handlePick(row) {
    if (onPick) onPick(row)
    if (onClose) onClose()
  }

  if (!open) return null

  var visibleColCount = Math.max(visibleColumns.length, 1)
  var headerPlaceholder = guideLabel
    ? ('Ara: ' + guideLabel)
    : (schema ? ('Ara: ' + (schema.guideLabel || schema.guideCode)) : 'Ara...')

  return createPortal(
    <div
      className="gl-modal-backdrop"
      onClick={function (e) { if (e.target === e.currentTarget && onClose) onClose() }}
    >
      <div className="gl-modal">
        <header className="gl-modal-header">
          <Search size={16} strokeWidth={2} />
          <input
            type="text"
            autoFocus
            value={search}
            onChange={function (e) { setSearch(e.target.value) }}
            placeholder={headerPlaceholder}
          />
          {headerActions}
          <button type="button" onClick={onClose} title="Kapat" className="gl-close-btn">
            <X size={16} strokeWidth={2.2} />
          </button>
        </header>

        {error && <div className="gl-error">{error}</div>}

        <div className="gl-scroll" ref={scrollRef}>
          <table className="gl-table">
            <thead>
              <tr>
                {visibleColumns.length > 0
                  ? visibleColumns.map(function (c) {
                      var sel = distinctSel[c.name] || []
                      var isActive = sel.length > 0
                      return (
                        <th key={c.name}>
                          <div className="gl-th-inner">
                            <span className="gl-th-label">{c.label || c.name}</span>
                            {c.distinct && (
                              <button
                                type="button"
                                className={'gl-col-filter ' + (isActive ? 'gl-col-filter--active' : '')}
                                onClick={function (e) { openColumnFilter(c.name, e) }}
                                title={isActive ? ('Filtre aktif (' + sel.length + ')') : 'Filtrele'}
                              >
                                <Filter size={11} strokeWidth={2.4} />
                                {isActive && (
                                  <span className="gl-col-filter-count">{sel.length}</span>
                                )}
                              </button>
                            )}
                          </div>
                        </th>
                      )
                    })
                  : <th>Yükleniyor...</th>}
              </tr>
            </thead>
            <tbody>
              {rows.length === 0 && !loading && (
                <tr><td colSpan={visibleColCount} className="gl-empty">
                  Kayıt bulunamadı
                </td></tr>
              )}
              {rows.map(function (row, idx) {
                return (
                  <tr
                    key={(row.value || '') + '_' + idx}
                    onDoubleClick={function () { handlePick(row) }}
                  >
                    {visibleColumns.map(function (c) {
                      var cell = row.cells ? row.cells[c.name] : null
                      return <td key={c.name}>{cell != null ? String(cell) : ''}</td>
                    })}
                  </tr>
                )
              })}
              {hasMore && (
                <tr ref={sentinelRef}>
                  <td colSpan={visibleColCount} className="gl-sentinel">
                    {loading ? <span><Loader2 size={14} className="spin" /> Yükleniyor...</span> : 'Daha fazla...'}
                  </td>
                </tr>
              )}
              {loading && !hasMore && rows.length === 0 && (
                <tr>
                  <td colSpan={visibleColCount} className="gl-sentinel">
                    <span><Loader2 size={14} className="spin" /> Yükleniyor...</span>
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>

        <footer className="gl-footer">
          <span>{rows.length} kayıt{hasMore ? '+' : ''}</span>
          {Object.keys(distinctSel).length > 0 && (
            <span className="gl-footer-filter">
              {Object.keys(distinctSel).length} filtre aktif
            </span>
          )}
        </footer>
      </div>

      {openCol && createPortal(
        <ColumnFilterPopover
          column={openCol}
          colLabel={(function () {
            var c = columns.find(function (x) { return x && x.name === openCol })
            return (c && c.label) || openCol
          })()}
          values={distinctData[openCol] || []}
          selected={distinctSel[openCol] || []}
          anchorRect={anchorRect}
          loading={distinctLoading}
          onSearchChange={setPopoverSearch}
          onApply={function (newSel) { applyColumnFilter(openCol, newSel) }}
          onClose={closeColumnFilter}
        />,
        document.body
      )}
    </div>,
    document.body
  )
}
