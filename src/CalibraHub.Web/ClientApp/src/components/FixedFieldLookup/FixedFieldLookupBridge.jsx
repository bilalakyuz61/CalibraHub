/**
 * FixedFieldLookupBridge — Sabit form alanlarina rehber lookup davranisi ekler.
 *
 * Runtime'da mountFixedFieldLookups(formCode) cagrildiginda:
 *   1) /api/field-settings/runtime/{formCode} fetch edilir
 *   2) Her binding icin DOM'da input bulunur
 *   3) Input readonly yapilir, yanina arama butonu eklenir
 *   4) Bu bilesen her bir alan icin mount edilir
 *
 * Mevcut LookupFieldInput'tan bagimsiz — kendi minimal modal UI'ini icerir.
 * guideSearch/guideSchema/guideResolve fonksiyonlari dynamicWidgetService'den gelir.
 */
import { useState, useEffect, useRef, useCallback } from 'react'
import { createPortal } from 'react-dom'
import { Search, X, Loader2, Settings } from 'lucide-react'
import { guideSchema, guideSearch, guideResolve } from '../DynamicWidgetRenderer/dynamicWidgetService'
import { getRuntimeBindings } from '../../services/fieldSettingService'
import FieldSettingsForm from '../CalibraLineItemsGrid/FieldSettingsForm'

var PAGE_SIZE = 50

/**
 * Tek bir sabit alan icin lookup davranisi.
 *
 * Props:
 *   inputElement  — Mevcut DOM input elemani
 *   fieldKey      — Alan anahtari
 *   guideCode     — Bagli rehber kodu
 *   filterJson    — Opsiyonel constraint JSON
 *   isRequired    — Zorunlu mu
 *   fillMap       — { '#otherSelector': 'guideColumnName' } — secim sonrasi diger alanlara deger doldurur
 */
export default function FixedFieldLookupBridge(props) {
  var inputEl    = props.inputElement
  var guideCode  = props.guideCode
  var filterJson = props.filterJson || null

  var formCode   = props.formCode || null
  var fieldKey   = props.fieldKey || null

  var [modalOpen, setModalOpen] = useState(false)
  var [settingsOpen, setSettingsOpen] = useState(false)
  var [schema, setSchema]       = useState(null)
  var [formatJson, setFormatJson] = useState(props.formatJson || null)
  var [search, setSearch]       = useState('')
  var [rows, setRows]           = useState([])
  var [page, setPage]           = useState(1)
  var [hasMore, setHasMore]     = useState(false)
  var [loading, setLoading]     = useState(false)
  var [error, setError]         = useState(null)

  var sentinelRef = useRef(null)
  var scrollRef   = useRef(null)
  // FieldSettingsForm column objesini mutate eder — ref ile referansi sabit tut
  var settingsColumnRef = useRef({ key: fieldKey, label: fieldKey, formCode: formCode, guideCode: guideCode, filterJson: filterJson, formatJson: formatJson })
  settingsColumnRef.current = { key: fieldKey, label: fieldKey, formCode: formCode, guideCode: guideCode, filterJson: filterJson, formatJson: formatJson }

  // ── Sayfa yuklendiginde mevcut degeri resolve et ──
  useEffect(function () {
    if (!inputEl || !guideCode) return
    var currentVal = inputEl.value
    if (!currentVal) return
    var alive = true
    guideResolve(guideCode, currentVal)
      .then(function (result) {
        if (alive && result && result.display) {
          inputEl.setAttribute('data-display', result.display)
          inputEl.value = result.display
          inputEl.setAttribute('data-value', currentVal)
        }
      })
      .catch(function () { /* sessizce devam */ })
    return function () { alive = false }
  }, [inputEl, guideCode])

  // ── Modal acildiginda DB'den formatJson yukle ──
  useEffect(function () {
    if (!modalOpen || !formCode || !fieldKey) return
    var alive = true
    getRuntimeBindings(formCode)
      .then(function (bindings) {
        if (!alive) return
        var binding = (bindings || []).find(function (b) { return b.fieldKey === fieldKey })
        if (binding && binding.formatJson) {
          setFormatJson(binding.formatJson)
        }
      })
      .catch(function () { /* sessizce devam */ })
    return function () { alive = false }
  }, [modalOpen, formCode, fieldKey])

  // ── Schema fetch ──
  useEffect(function () {
    if (!modalOpen || schema || !guideCode) return
    var alive = true
    guideSchema(guideCode)
      .then(function (s) { if (alive) setSchema(s) })
      .catch(function (e) { if (alive) setError('Schema yuklenemedi: ' + e.message) })
    return function () { alive = false }
  }, [modalOpen, guideCode, schema])

  // ── Debounced search ──
  useEffect(function () {
    if (!modalOpen || !guideCode) return
    var handle = setTimeout(function () {
      setPage(1)
      loadPage(1, search, true)
    }, 300)
    return function () { clearTimeout(handle) }
  }, [search, modalOpen, guideCode])

  // ── Infinite scroll ──
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
    }, { root: scrollRef.current, threshold: 0.1 })
    observer.observe(el)
    return function () { observer.disconnect() }
  }, [modalOpen, hasMore, loading, page, search])

  var loadPage = useCallback(function (pageNumber, searchTerm, replace) {
    if (!guideCode) return
    setLoading(true)
    setError(null)
    guideSearch(guideCode, {
      search: searchTerm,
      page: pageNumber,
      pageSize: PAGE_SIZE,
      constraints: filterJson || undefined,
    })
      .then(function (result) {
        if (!result) {
          setRows([])
          setHasMore(false)
          setError('Rehber bulunamadi: ' + guideCode)
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
  }, [guideCode, filterJson])

  function openModal() {
    if (!guideCode) return
    setSearch('')
    setRows([])
    setPage(1)
    setHasMore(false)
    setError(null)
    setModalOpen(true)
  }
  function closeModal() {
    setModalOpen(false)
  }

  // ESC ile kapanma
  useEffect(function () {
    if (!modalOpen) return
    function handleKeyDown(e) {
      if (e.key === 'Escape') closeModal()
    }
    document.addEventListener('keydown', handleKeyDown)
    return function () { document.removeEventListener('keydown', handleKeyDown) }
  }, [modalOpen])
  function pickRow(row) {
    if (inputEl) {
      inputEl.setAttribute('data-value', row.value || '')
      inputEl.setAttribute('data-display', row.display || '')
      // Input alanina value (kod) yaz, display (isim) fillMap ile ayri alana gider
      inputEl.value = row.value || ''
      // Native event dispatch — mevcut JS listener'lari tetiklemek icin
      inputEl.dispatchEvent(new Event('input', { bubbles: true }))
      inputEl.dispatchEvent(new Event('change', { bubbles: true }))
    }
    // fillMap: secim sonrasi diger alanlara deger doldur
    var fillMap = props.fillMap
    if (fillMap && row.cells) {
      Object.keys(fillMap).forEach(function(selector) {
        var colName = fillMap[selector]
        var target = document.querySelector(selector)
        if (target && row.cells[colName] != null) {
          target.value = String(row.cells[colName])
          target.dispatchEvent(new Event('input', { bubbles: true }))
          target.dispatchEvent(new Event('change', { bubbles: true }))
        }
      })
    }
    closeModal()
  }
  function clearValue(e) {
    e.stopPropagation()
    if (inputEl) {
      inputEl.removeAttribute('data-value')
      inputEl.removeAttribute('data-display')
      inputEl.value = ''
      inputEl.dispatchEvent(new Event('input', { bubbles: true }))
      inputEl.dispatchEvent(new Event('change', { bubbles: true }))
    }
  }

  // formatJson'dan column labels ve visible columns parse et
  var parsedFormat = (function () {
    if (!formatJson) return { visibleColumns: null, columnLabels: {} }
    try {
      var p = typeof formatJson === 'string' ? JSON.parse(formatJson) : formatJson
      return { visibleColumns: p.visibleColumns || null, columnLabels: p.columnLabels || {} }
    } catch (e) { return { visibleColumns: null, columnLabels: {} } }
  })()

  var allColumns = (schema && schema.columns) || []
  var columns = parsedFormat.visibleColumns
    ? allColumns.filter(function (c) { return parsedFormat.visibleColumns.indexOf(c) !== -1 })
    : allColumns

  function colLabel(colName) {
    return parsedFormat.columnLabels[colName] || colName
  }

  var hasValue = inputEl && inputEl.value && inputEl.value.length > 0

  return (
    <>
      <button
        type="button"
        className="ffl-lookup-btn"
        onClick={openModal}
        title="Rehber ac"
      >
        <Search size={14} strokeWidth={2} />
      </button>
      {hasValue && (
        <button
          type="button"
          className="ffl-clear-btn"
          onClick={clearValue}
          title="Temizle"
        >
          <X size={14} strokeWidth={2.2} />
        </button>
      )}

      {modalOpen && createPortal(
        <div
          className="ffl-modal-backdrop"
          onClick={function (e) { if (e.target === e.currentTarget) closeModal() }}
        >
          <div className="ffl-modal">
            <header className="ffl-modal-header">
              <Search size={16} strokeWidth={2} />
              <input
                type="text"
                autoFocus
                value={search}
                onChange={function (e) { setSearch(e.target.value) }}
                placeholder={schema ? ('Ara: ' + (schema.guideLabel || schema.guideCode)) : 'Ara...'}
              />
              {formCode && (
                <button type="button" onClick={function () { setSettingsOpen(true) }} title="Alan Ayarlari" className="ffl-settings-btn">
                  <Settings size={15} strokeWidth={2} />
                </button>
              )}
              <button type="button" onClick={closeModal} title="Kapat" className="ffl-close-btn">
                <X size={16} strokeWidth={2.2} />
              </button>
            </header>

            {formCode && (
              <FieldSettingsForm
                column={settingsColumnRef.current}
                isOpen={settingsOpen}
                onClose={function () {
                  setSettingsOpen(false)
                  // formatJson guncellenmis olabilir — state'e yansit
                  if (settingsColumnRef.current.formatJson !== formatJson) {
                    setFormatJson(settingsColumnRef.current.formatJson)
                  }
                  // Schema'yi temizle — yeniden yuklenecek, guncel etiketler uygulanacak
                  setSchema(null)
                  setRows([])
                  setPage(1)
                }}
              />
            )}

            {error && <div className="ffl-error">{error}</div>}

            <div className="ffl-scroll" ref={scrollRef}>
              <table className="ffl-table">
                <thead>
                  <tr>
                    {columns.length > 0
                      ? columns.map(function (c) { return <th key={c}>{colLabel(c)}</th> })
                      : <th>Yukleniyor...</th>}
                  </tr>
                </thead>
                <tbody>
                  {rows.length === 0 && !loading && (
                    <tr><td colSpan={Math.max(columns.length, 1)} className="ffl-empty">
                      Kayit bulunamadi
                    </td></tr>
                  )}
                  {rows.map(function (row, idx) {
                    return (
                      <tr key={(row.value || '') + '_' + idx} onDoubleClick={function () { pickRow(row) }}>
                        {columns.map(function (c) {
                          var cell = row.cells ? row.cells[c] : null
                          return <td key={c}>{cell != null ? String(cell) : ''}</td>
                        })}
                      </tr>
                    )
                  })}
                  {hasMore && (
                    <tr ref={sentinelRef}>
                      <td colSpan={Math.max(columns.length, 1)} className="ffl-sentinel">
                        {loading ? <span><Loader2 size={14} className="spin" /> Yukleniyor...</span> : 'Daha fazla...'}
                      </td>
                    </tr>
                  )}
                  {loading && !hasMore && rows.length === 0 && (
                    <tr>
                      <td colSpan={Math.max(columns.length, 1)} className="ffl-sentinel">
                        <span><Loader2 size={14} className="spin" /> Yukleniyor...</span>
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>

            <footer className="ffl-footer">
              <span>{rows.length} kayit{hasMore ? '+' : ''}</span>
            </footer>
          </div>
        </div>,
        document.body
      )}
    </>
  )
}
