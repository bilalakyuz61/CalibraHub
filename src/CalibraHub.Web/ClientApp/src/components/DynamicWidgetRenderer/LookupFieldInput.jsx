/**
 * LookupFieldInput — EAV lookup (rehber / LOV) widget'inin kullanici UI'i.
 *
 * Bir readonly input + "ara" butonu cizer. Tiklaninca document.body'ye
 * portal edilmis modal acilir; icinde:
 *   - autoFocus'lu debounced arama kutusu (300ms)
 *   - Server-side sort destekli tablo
 *   - Infinite scroll (IntersectionObserver ile sonraki sayfayi yukler)
 *
 * Satir secildiginde onPick(value, display) cagirir:
 *   value   → WidgetTra.Value'ye yazilacak ValueColumn degeri
 *   display → input'a basilacak DisplayColumn degeri
 *
 * Props:
 *   widgetId, guideCode, value (kayitli value), display (cozumlenmis gosterim),
 *   onPick(value, display), classPrefix ('mce' | 'ca' | 'sqe')
 */
import { useState, useEffect, useRef, useCallback } from 'react'
import { createPortal } from 'react-dom'
import { Search, X, Loader2 } from 'lucide-react'
import { guideSchema, guideSearch } from './dynamicWidgetService'

var PAGE_SIZE = 50

export default function LookupFieldInput(props) {
  var widgetId   = props.widgetId
  var guideCode  = props.guideCode || ''
  var value      = props.value || ''
  var display    = props.display || ''
  var onPick     = props.onPick
  var constraints = props.constraints || null  // JSON string — resolved constraints
  var prefix     = props.classPrefix || 'mce'

  var [modalOpen, setModalOpen] = useState(false)
  var [schema, setSchema]       = useState(null)
  var [search, setSearch]       = useState('')
  var [rows, setRows]           = useState([])
  var [page, setPage]           = useState(1)
  var [hasMore, setHasMore]     = useState(false)
  var [loading, setLoading]     = useState(false)
  var [error, setError]         = useState(null)

  var sentinelRef = useRef(null)
  var scrollContainerRef = useRef(null)

  // ── Schema fetch — modal ilk acildiginda (sadece bir kez) ──
  useEffect(function () {
    if (!modalOpen || schema || !guideCode) return
    var alive = true
    guideSchema(guideCode)
      .then(function (s) { if (alive) setSchema(s) })
      .catch(function (e) { if (alive) setError('Schema yuklenemedi: ' + e.message) })
    return function () { alive = false }
  }, [modalOpen, guideCode, schema])

  // ── Arama tetigi: debounced search + page=1 reset ──
  // search veya modal acik durumu degisince: 300ms bekle, sayfa 1'i yukle.
  useEffect(function () {
    if (!modalOpen || !guideCode) return
    var handle = setTimeout(function () {
      setPage(1)
      loadPage(1, search, true)
    }, 300)
    return function () { clearTimeout(handle) }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [search, modalOpen, guideCode])

  // ── Infinite scroll: sentinel görününce page++ + append ──
  useEffect(function () {
    if (!modalOpen || !hasMore || loading) return
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
    }, { root: scrollContainerRef.current, threshold: 0.1 })
    observer.observe(el)
    return function () { observer.disconnect() }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [modalOpen, hasMore, loading, page, search])

  var loadPage = useCallback(function (pageNumber, searchTerm, replace) {
    if (!guideCode) return
    setLoading(true)
    setError(null)
    guideSearch(guideCode, {
      search: searchTerm,
      page: pageNumber,
      pageSize: PAGE_SIZE,
      constraints: constraints || undefined,
    })
      .then(function (result) {
        if (!result) {
          setRows([])
          setHasMore(false)
          setError('Rehber bulunamadi: ' + guideCode + '. Rehber Yonetimi ekranindan tanimi kontrol edin.')
          return
        }
        if (replace) {
          setRows(result.rows || [])
        } else {
          setRows(function (prev) { return prev.concat(result.rows || []) })
        }
        setHasMore(!!result.hasMore)
      })
      .catch(function (e) {
        setError('Arama basarisiz: ' + e.message)
        if (replace) setRows([])
        setHasMore(false)
      })
      .finally(function () { setLoading(false) })
  }, [guideCode])

  function openModal() {
    if (!guideCode) return
    setModalOpen(true)
    // search resetlenmez — kullanicinin onceki aramasi kalsin; isterse temizler.
  }
  function closeModal() {
    setModalOpen(false)
  }
  function pickRow(row) {
    if (onPick) onPick(row.value || '', row.display || '')
    closeModal()
  }
  function clearValue(e) {
    e.stopPropagation()
    if (onPick) onPick('', '')
  }

  // ── Main render: readonly input + search butonu (+ clear) ──
  var hasValue = value && String(value).length > 0
  var inputDisplay = hasValue ? (display || value) : ''
  var placeholder = guideCode ? 'Secmek icin tiklayin...' : 'Rehber tanimli degil'

  return (
    <div className={prefix + '-lookup-group'}>
      <input
        id={'dyn_' + widgetId}
        type="text"
        className={prefix + '-input'}
        data-widget-code={widgetId}
        value={inputDisplay}
        placeholder={placeholder}
        readOnly
        onClick={openModal}
        style={{ cursor: guideCode ? 'pointer' : 'not-allowed' }}
      />
      <button
        type="button"
        className={prefix + '-lookup-btn'}
        onClick={openModal}
        disabled={!guideCode}
        title="Rehber ac"
      >
        <Search size={14} strokeWidth={2} />
      </button>
      {hasValue && (
        <button
          type="button"
          className={prefix + '-lookup-clear'}
          onClick={clearValue}
          title="Temizle"
        >
          <X size={14} strokeWidth={2.2} />
        </button>
      )}

      {modalOpen && createPortal(
        <LookupModal
          prefix={prefix}
          schema={schema}
          search={search}
          setSearch={setSearch}
          rows={rows}
          loading={loading}
          hasMore={hasMore}
          error={error}
          onPick={pickRow}
          onClose={closeModal}
          sentinelRef={sentinelRef}
          scrollContainerRef={scrollContainerRef}
        />,
        document.body
      )}
    </div>
  )
}

// ──────────────────────────────────────────────────────────────
// LookupModal — portal edilmis backdrop + tablo
// ──────────────────────────────────────────────────────────────
function LookupModal(props) {
  var prefix               = props.prefix
  var schema               = props.schema
  var search               = props.search
  var setSearch            = props.setSearch
  var rows                 = props.rows
  var loading              = props.loading
  var hasMore              = props.hasMore
  var error                = props.error
  var onPick               = props.onPick
  var onClose              = props.onClose
  var sentinelRef          = props.sentinelRef
  var scrollContainerRef   = props.scrollContainerRef

  var columns = (schema && schema.columns) || []

  // ESC ile kapat
  useEffect(function () {
    function onKey(e) { if (e.key === 'Escape') onClose() }
    document.addEventListener('keydown', onKey)
    return function () { document.removeEventListener('keydown', onKey) }
  }, [onClose])

  return (
    <div
      className={prefix + '-lookup-modal-backdrop'}
      onClick={function (e) { if (e.target === e.currentTarget) onClose() }}
    >
      <div className={prefix + '-lookup-modal'}>
        <header>
          <Search size={16} strokeWidth={2} />
          <input
            type="text"
            autoFocus
            value={search}
            onChange={function (e) { setSearch(e.target.value) }}
            placeholder={schema ? ('Ara: ' + (schema.guideLabel || schema.guideCode)) : 'Ara...'}
          />
          <button type="button" onClick={onClose} title="Kapat" className={prefix + '-lookup-close'}>
            <X size={16} strokeWidth={2.2} />
          </button>
        </header>

        {error && (
          <div className={prefix + '-lookup-error'}>{error}</div>
        )}

        <div className={prefix + '-lookup-scroll'} ref={scrollContainerRef}>
          <table className={prefix + '-lookup-table'}>
            <thead>
              <tr>
                {columns.length > 0
                  ? columns.map(function (c) {
                      return <th key={c}>{c}</th>
                    })
                  : <th>Yukleniyor...</th>}
              </tr>
            </thead>
            <tbody>
              {rows.length === 0 && !loading && (
                <tr><td colSpan={Math.max(columns.length, 1)} className={prefix + '-lookup-empty'}>
                  Kayit bulunamadi
                </td></tr>
              )}
              {rows.map(function (row, idx) {
                return (
                  <tr key={(row.value || '') + '_' + idx} onClick={function () { onPick(row) }}>
                    {columns.map(function (c) {
                      var cell = row.cells ? row.cells[c] : null
                      return <td key={c}>{cell != null ? String(cell) : ''}</td>
                    })}
                  </tr>
                )
              })}
              {hasMore && (
                <tr ref={sentinelRef}>
                  <td colSpan={Math.max(columns.length, 1)} className={prefix + '-lookup-sentinel'}>
                    {loading ? (
                      <span><Loader2 size={14} className="spin" /> Yukleniyor...</span>
                    ) : 'Daha fazla...'}
                  </td>
                </tr>
              )}
              {loading && !hasMore && rows.length === 0 && (
                <tr>
                  <td colSpan={Math.max(columns.length, 1)} className={prefix + '-lookup-sentinel'}>
                    <span><Loader2 size={14} className="spin" /> Yukleniyor...</span>
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>

        <footer className={prefix + '-lookup-footer'}>
          <span>{rows.length} kayit{hasMore ? '+' : ''}</span>
        </footer>
      </div>
    </div>
  )
}
