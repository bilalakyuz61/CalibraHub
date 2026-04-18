/**
 * LineGridCell — CalibraLineItemsGrid icin tek hucre renderer
 *
 * Props:
 *   column: { key, label, type, width, align, readonly, computed, precision, icon,
 *             lookupUrl, lookupValueKey, lookupLabelKey, lookupFillMap,
 *             optionsUrl, optionsValueKey, optionsLabelKey, options, min, max }
 *   row: mevcut satir objesi
 *   value: hucre degeri (computed icin dis kaynaktan)
 *   onChange: function(columnKey, newValue, optionalFillPatch)
 *     — fillPatch: text-lookup secimi sonrasi diger alanlara auto-fill icin
 *   isFirst: focus yonetimi icin (yeni satir eklendiginde ilk hucre focus'lanir)
 */
import { useState, useRef, useEffect, useCallback, useMemo } from 'react'
import { createPortal } from 'react-dom'
import { useLookup } from './useLookup'
import { guideSchema, guideSearch, guideResolve } from '../DynamicWidgetRenderer/dynamicWidgetService'
import FieldSettingsForm from './FieldSettingsForm'
import CombinationPickerModal from './CombinationPickerModal'

/**
 * Bir obje'den verilen key ile deger oku — case-insensitive.
 * ASP.NET Core JSON bazen camelCase bazen PascalCase dondugu icin
 * lookupFillMap'te tanimli source key'i hangi formda olursa olsun yakalar.
 * Oncelik: birebir match -> lowerCamel -> UpperCamel -> case-insensitive scan.
 */
function readCaseInsensitive(obj, key) {
  if (!obj || key == null) return undefined
  if (obj[key] !== undefined) return obj[key]
  var first = key.charAt(0)
  var lowerKey = first.toLowerCase() + key.slice(1)
  if (obj[lowerKey] !== undefined) return obj[lowerKey]
  var upperKey = first.toUpperCase() + key.slice(1)
  if (obj[upperKey] !== undefined) return obj[upperKey]
  var needle = key.toLowerCase()
  var keys = Object.keys(obj)
  for (var i = 0; i < keys.length; i++) {
    if (keys[i].toLowerCase() === needle) return obj[keys[i]]
  }
  return undefined
}

function useIsLight() {
  var [light, setLight] = useState(function() {
    return document.body.classList.contains('app-theme-light')
  })
  useEffect(function() {
    var obs = new MutationObserver(function() {
      setLight(document.body.classList.contains('app-theme-light'))
    })
    obs.observe(document.body, { attributes: true, attributeFilter: ['class'] })
    return function() { obs.disconnect() }
  }, [])
  return light
}

var TR_LOCALE = 'tr-TR'

function formatNumber(val, precision) {
  if (val == null || val === '') return ''
  var n = typeof val === 'number' ? val : parseFloat(String(val).replace(',', '.'))
  if (isNaN(n)) return ''
  return n.toLocaleString(TR_LOCALE, {
    minimumFractionDigits: precision != null ? precision : 2,
    maximumFractionDigits: precision != null ? precision : 2,
  })
}

function parseNumber(raw) {
  if (raw == null || raw === '') return null
  var s = String(raw).replace(/\s/g, '').replace(',', '.')
  var n = parseFloat(s)
  return isNaN(n) ? null : n
}

export default function LineGridCell(props) {
  var column = props.column
  var row = props.row
  var value = props.value
  var onChange = props.onChange
  var isReadonly = column.readonly === true || column.computed === true

  var alignClass =
    column.align === 'right'  ? 'text-right'  :
    column.align === 'center' ? 'text-center' : 'text-left'

  var baseInputClass =
    'w-full h-full bg-transparent border-0 outline-none px-2.5 py-2 ' +
    'text-[13px] text-slate-800 placeholder:text-slate-400 ' +
    'dark:text-white/85 dark:placeholder:text-white/40 ' +
    'focus:bg-indigo-50/60 dark:focus:bg-white/[0.08] ' +
    'focus:ring-2 focus:ring-indigo-400/60 focus:ring-inset ' +
    'transition-colors rounded ' +
    alignClass

  // ── Readonly / Computed ────────────────────────────
  if (isReadonly) {
    var displayValue = value
    if (column.type === 'currency' || column.type === 'number' || column.type === 'percent') {
      displayValue = formatNumber(value, column.precision)
    }
    return (
      <div
        className={
          'w-full px-2.5 py-2 text-[13px] font-medium ' +
          (column.computed
            ? 'text-amber-600 dark:text-amber-300 tabular-nums font-mono font-bold'
            : 'text-slate-600 dark:text-white/55') +
          ' ' + alignClass
        }
      >
        {displayValue || <span className="text-slate-300 dark:text-white/40">—</span>}
      </div>
    )
  }

  // ── Text-lookup (autocomplete) ─────────────────────
  if (column.type === 'text-lookup') {
    // guideCode varsa tam modal deneyimi (buton + tum kolonlar)
    if (column.guideCode) {
      return <GuideLookupCell column={column} value={value} onChange={onChange} baseInputClass={baseInputClass} />
    }
    return <TextLookupCell column={column} row={row} value={value} onChange={onChange} baseInputClass={baseInputClass} alignClass={alignClass} />
  }

  // ── Select ─────────────────────────────────────────
  if (column.type === 'select') {
    return <SelectCell column={column} row={row} value={value} onChange={onChange} baseInputClass={baseInputClass} alignClass={alignClass} />
  }

  // ── Number / Currency / Percent ────────────────────
  if (column.type === 'number' || column.type === 'currency' || column.type === 'percent') {
    return <NumericCell column={column} value={value} onChange={onChange} baseInputClass={baseInputClass} />
  }

  // ── Combination Lookup (Kombinasyon Seçici) ────────
  if (column.type === 'combination-lookup') {
    return <CombinationLookupCell column={column} row={row} value={value} onChange={onChange} />
  }

  // ── Text (default) ─────────────────────────────────
  return (
    <input
      type="text"
      value={value == null ? '' : String(value)}
      onChange={function(e) { onChange(column.key, e.target.value) }}
      className={baseInputClass}
      placeholder={column.placeholder || ''}
    />
  )
}

/* ══════════════════════════════════════════════════════════════
   TextLookupCell — autocomplete ile material code tarzi arama
   ══════════════════════════════════════════════════════════════ */
function TextLookupCell(props) {
  var column = props.column
  var row = props.row
  var value = props.value
  var onChange = props.onChange

  var isLight = useIsLight()
  var lookup = useLookup(column.lookupUrl, row)
  var [open, setOpen] = useState(false)
  var [filter, setFilter] = useState('')
  var [dropPos, setDropPos] = useState({ top: 0, left: 0, width: 200 })
  var inputRef = useRef(null)

  // Dropdown pozisyonunu input'un bounding rect'inden hesapla
  function calcPos() {
    if (!inputRef.current) return
    var r = inputRef.current.getBoundingClientRect()
    setDropPos({ top: r.bottom + 2, left: r.left, width: Math.max(r.width, 280) })
  }

  useEffect(function() {
    if (!open) return undefined
    calcPos()
    function onDoc(e) {
      if (inputRef.current && !inputRef.current.closest('[data-lookup-wrap]').contains(e.target)) {
        setOpen(false)
      }
    }
    function onScroll() { calcPos() }
    document.addEventListener('mousedown', onDoc)
    window.addEventListener('scroll', onScroll, true)
    return function() {
      document.removeEventListener('mousedown', onDoc)
      window.removeEventListener('scroll', onScroll, true)
    }
  }, [open])

  var valueKey = column.lookupValueKey || 'code'
  var labelKey = column.lookupLabelKey || 'name'

  var filtered = lookup.options
  if (filter) {
    var q = filter.toLowerCase()
    filtered = lookup.options.filter(function(o) {
      var v = String(o[valueKey] || '').toLowerCase()
      var l = String(o[labelKey] || '').toLowerCase()
      return v.indexOf(q) !== -1 || l.indexOf(q) !== -1
    })
  }
  filtered = filtered.slice(0, 12)

  function handlePick(opt) {
    var patch = {}
    patch[column.key] = readCaseInsensitive(opt, valueKey)
    if (column.lookupFillMap) {
      Object.keys(column.lookupFillMap).forEach(function(rowKey) {
        var optKey = column.lookupFillMap[rowKey]
        patch[rowKey] = readCaseInsensitive(opt, optKey)
      })
    }
    onChange(column.key, patch[column.key], patch)
    setOpen(false)
    setFilter('')
  }

  var dropdownBaseStyle = isLight
    ? { background: '#ffffff', border: '1px solid #e2e8f0', boxShadow: '0 8px 24px rgba(0,0,0,0.12)' }
    : { background: 'rgba(15,20,35,0.97)', backdropFilter: 'blur(24px)', WebkitBackdropFilter: 'blur(24px)', border: '1px solid rgba(255,255,255,0.12)', boxShadow: '0 12px 40px rgba(0,0,0,0.4)' }

  var portalStyle = Object.assign({}, dropdownBaseStyle, {
    position: 'fixed',
    top: dropPos.top,
    left: dropPos.left,
    width: dropPos.width,
    maxHeight: '256px',
    overflowY: 'auto',
    borderRadius: '8px',
    zIndex: 9999,
  })

  var codeClass  = isLight ? 'text-[11px] font-mono text-indigo-600 tabular-nums' : 'text-[11px] font-mono text-indigo-300 tabular-nums'
  var labelClass = isLight ? 'text-[12px] text-slate-700 truncate flex-1'         : 'text-[12px] text-white/70 truncate flex-1'
  var hoverClass = isLight ? 'hover:bg-slate-100'                                  : 'hover:bg-white/[0.06]'
  var emptyClass = isLight ? 'text-[11px] text-slate-400'                          : 'text-[11px] text-white/40'

  var showList  = open && filtered.length > 0
  var showEmpty = open && !lookup.loading && filtered.length === 0

  return (
    <div data-lookup-wrap="">
      <input
        ref={inputRef}
        type="text"
        value={open ? filter : (value == null ? '' : String(value))}
        onFocus={function() { setOpen(true); setFilter(''); calcPos() }}
        onChange={function(e) { setFilter(e.target.value); setOpen(true) }}
        className={props.baseInputClass + ' font-mono'}
        placeholder={column.placeholder || 'Ara...'}
      />
      {showList && createPortal(
        <div style={portalStyle}>
          {filtered.map(function(opt, idx) {
            return (
              <button
                key={opt[valueKey] + '-' + idx}
                type="button"
                onMouseDown={function(e) { e.preventDefault() }}
                onClick={function() { handlePick(opt) }}
                className={'w-full flex items-center gap-3 px-3 py-2 text-left transition-colors ' + hoverClass}
              >
                <span className={codeClass}>{opt[valueKey]}</span>
                <span className={labelClass}>{opt[labelKey]}</span>
              </button>
            )
          })}
        </div>,
        document.body
      )}
      {showEmpty && createPortal(
        <div style={Object.assign({}, portalStyle, { maxHeight: 'none', padding: '8px 12px' })} className={emptyClass}>
          Eşleşme bulunamadı
        </div>,
        document.body
      )}
    </div>
  )
}

/* ══════════════════════════════════════════════════════════════
   SelectCell — options (sabit veya URL tabanli)
   ══════════════════════════════════════════════════════════════ */
function SelectCell(props) {
  var column = props.column
  var row = props.row
  var value = props.value
  var onChange = props.onChange

  // Sabit options VEYA URL'den
  var staticOptions = Array.isArray(column.options) ? column.options : null
  var dynamicLookup = useLookup(staticOptions ? null : column.optionsUrl, row)
  var options = staticOptions || dynamicLookup.options

  var valueKey = column.optionsValueKey || 'code'
  var labelKey = column.optionsLabelKey || 'name'

  // autoSelectFirst=true olan kolonlar (ornegin Birim): options yuklendiginde
  // ve hucre bos ise, ilk secenek (master birim) varsayilan olarak atanir.
  useEffect(function() {
    if (!column.autoSelectFirst) return
    if (!options || options.length === 0) return
    if (value != null && value !== '') return
    var first = options[0]
    var firstVal = first ? first[valueKey] : null
    if (firstVal != null && firstVal !== '') {
      onChange(column.key, firstVal)
    }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [options, value, column.autoSelectFirst])

  return (
    <select
      value={value == null ? '' : String(value)}
      onChange={function(e) { onChange(column.key, e.target.value) }}
      className={props.baseInputClass + ' appearance-none cursor-pointer'}
      style={{ backgroundImage: 'none' }}
    >
      <option value="" style={{ background: '#0c0f1a' }}>—</option>
      {options.map(function(opt, idx) {
        return (
          <option
            key={opt[valueKey] + '-' + idx}
            value={opt[valueKey]}
            style={{ background: '#0c0f1a', color: '#fff' }}
          >
            {opt[labelKey] || opt[valueKey]}
          </option>
        )
      })}
    </select>
  )
}

/* ══════════════════════════════════════════════════════════════
   NumericCell — number / currency / percent (tabular-nums)
   ══════════════════════════════════════════════════════════════ */
function NumericCell(props) {
  var column = props.column
  var value = props.value
  var onChange = props.onChange
  var [focused, setFocused] = useState(false)
  var [localText, setLocalText] = useState('')

  // Focus disindaysa formatli goster, focus'ta ham degeri goster
  var displayValue
  if (focused) {
    displayValue = localText
  } else {
    if (value == null || value === '') displayValue = ''
    else displayValue = formatNumber(value, column.precision)
  }

  function handleFocus(e) {
    setFocused(true)
    setLocalText(value == null || value === '' ? '' : String(value).replace('.', ','))
    setTimeout(function() { e.target.select() }, 0)
  }

  function handleBlur() {
    var parsed = parseNumber(localText)
    // Min/max clamp
    if (parsed != null) {
      if (column.min != null && parsed < column.min) parsed = column.min
      if (column.max != null && parsed > column.max) parsed = column.max
    }
    onChange(column.key, parsed)
    setFocused(false)
  }

  return (
    <input
      type="text"
      inputMode="decimal"
      value={displayValue}
      onFocus={handleFocus}
      onBlur={handleBlur}
      onChange={function(e) { setLocalText(e.target.value) }}
      className={props.baseInputClass + ' tabular-nums font-mono'}
      placeholder={column.precision ? '0,' + '0'.repeat(column.precision) : '0'}
    />
  )
}

/* ══════════════════════════════════════════════════════════════
   GuideLookupCell — guideCode ile tam modal rehber deneyimi
   (FixedFieldLookupBridge'in grid-cell versiyonu)
   ══════════════════════════════════════════════════════════════ */
var GUIDE_PAGE_SIZE = 50

function GuideLookupCell(props) {
  var column   = props.column
  var value    = props.value
  var onChange = props.onChange

  var isLight = useIsLight()

  var [displayVal, setDisplayVal] = useState('')
  var [modalOpen, setModalOpen]   = useState(false)
  var [settingsOpen, setSettingsOpen] = useState(false)
  var [schema, setSchema]         = useState(null)
  var [search, setSearch]         = useState('')
  var [rows, setRows]             = useState([])
  var [page, setPage]             = useState(1)
  var [hasMore, setHasMore]       = useState(false)
  var [loading, setLoading]       = useState(false)
  var [error, setError]           = useState(null)

  // formatJson'dan görünür kolon listesi ve kolon etiketleri
  var parsedFormat = useMemo(function() {
    if (!column.formatJson) return {}
    try { return JSON.parse(column.formatJson) || {} }
    catch (e) { return {} }
  }, [column.formatJson])
  var parsedVisible = parsedFormat.visibleColumns || null
  var colLabels     = parsedFormat.columnLabels   || {}

  var sentinelRef = useRef(null)
  var scrollRef   = useRef(null)

  // Sayfa yüklenince mevcut değeri göster (value = kod, display gösterme)
  useEffect(function() {
    if (!value || !column.guideCode) { setDisplayVal(''); return }
    setDisplayVal(String(value))
  }, [value, column.guideCode])

  // Modal açılınca schema yükle
  useEffect(function() {
    if (!modalOpen || schema || !column.guideCode) return undefined
    var alive = true
    guideSchema(column.guideCode)
      .then(function(s) { if (alive) setSchema(s) })
      .catch(function(e) { if (alive) setError('Schema yüklenemedi: ' + e.message) })
    return function() { alive = false }
  }, [modalOpen, column.guideCode, schema])

  // Debounced arama
  useEffect(function() {
    if (!modalOpen || !column.guideCode) return undefined
    var handle = setTimeout(function() { setPage(1); loadPage(1, search, true) }, 300)
    return function() { clearTimeout(handle) }
  }, [search, modalOpen, column.guideCode])

  // Infinite scroll
  useEffect(function() {
    if (!modalOpen || !hasMore || loading) return undefined
    var el = sentinelRef.current
    if (!el) return undefined
    var observer = new IntersectionObserver(function(entries) {
      if (entries[0].isIntersecting) {
        var next = page + 1
        setPage(next)
        loadPage(next, search, false)
      }
    }, { root: scrollRef.current, threshold: 0.1 })
    observer.observe(el)
    return function() { observer.disconnect() }
  }, [modalOpen, hasMore, loading, page, search])

  var loadPage = useCallback(function(pageNum, searchTerm, replace) {
    if (!column.guideCode) return
    setLoading(true)
    setError(null)
    guideSearch(column.guideCode, { search: searchTerm, page: pageNum, pageSize: GUIDE_PAGE_SIZE })
      .then(function(result) {
        if (!result) { setRows([]); setHasMore(false); setError('Rehber bulunamadı: ' + column.guideCode); return }
        if (replace) setRows(result.rows || [])
        else setRows(function(prev) { return prev.concat(result.rows || []) })
        setHasMore(!!result.hasMore)
      })
      .catch(function(e) { setError('Arama başarısız: ' + e.message); if (replace) setRows([]); setHasMore(false) })
      .finally(function() { setLoading(false) })
  }, [column.guideCode])

  function openModal() { setModalOpen(true) }
  function closeModal() {
    setModalOpen(false); setSearch(''); setRows([]); setPage(1); setHasMore(false); setSchema(null)
  }

  function pickRow(guideRow) {
    var patch = {}
    patch[column.key] = guideRow.value
    if (column.lookupFillMap && guideRow.cells) {
      Object.keys(column.lookupFillMap).forEach(function(gridKey) {
        var cellKey = column.lookupFillMap[gridKey]
        var val = readCaseInsensitive(guideRow.cells, cellKey)
        if (val != null) patch[gridKey] = val
      })
    }
    setDisplayVal(guideRow.value || '')
    onChange(column.key, guideRow.value, patch)
    closeModal()
  }

  function clearValue(e) {
    e.stopPropagation()
    setDisplayVal('')
    onChange(column.key, null, { [column.key]: null })
  }

  var schemaColumns  = (schema && schema.columns) || []
  var displayColumns = parsedVisible
    ? schemaColumns.filter(function(c) { return parsedVisible.includes(c) })
    : schemaColumns

  // ── Stiller ──
  var inputBg    = isLight ? '#fff'                : 'transparent'
  var inputBdr   = isLight ? '1px solid #e2e8f0'  : '1px solid rgba(255,255,255,0.12)'
  var inputColor = isLight ? '#1e293b'             : 'rgba(255,255,255,0.85)'
  var btnBg      = isLight ? '#f1f5f9'             : 'rgba(255,255,255,0.07)'
  var btnColor   = isLight ? '#6366f1'             : '#818cf8'
  var modalBg    = isLight
    ? { background: '#fff', border: '1px solid #e2e8f0', boxShadow: '0 24px 64px rgba(0,0,0,0.18)' }
    : { background: 'rgba(13,17,27,0.98)', border: '1px solid rgba(255,255,255,0.12)', boxShadow: '0 24px 64px rgba(0,0,0,0.6)', backdropFilter: 'blur(24px)' }
  var headerBg   = isLight ? '#f8fafc' : 'rgba(255,255,255,0.04)'
  var rowHover   = isLight ? '#f1f5f9' : 'rgba(255,255,255,0.05)'
  var thColor    = isLight ? '#64748b' : 'rgba(255,255,255,0.4)'
  var tdColor    = isLight ? '#334155' : 'rgba(255,255,255,0.75)'
  var searchBg   = isLight ? '#f8fafc' : 'rgba(255,255,255,0.06)'
  var searchBdr  = isLight ? '1px solid #e2e8f0'  : '1px solid rgba(255,255,255,0.12)'
  var divBdr     = isLight ? '1px solid #e2e8f0'  : '1px solid rgba(255,255,255,0.08)'

  return (
    <div style={{ display: 'flex', alignItems: 'center', width: '100%', height: '100%', gap: 2 }}>
      {/* Değer alanı */}
      <input
        type="text"
        value={displayVal}
        onChange={function(e) {
          var v = e.target.value
          setDisplayVal(v)
          onChange(column.key, v, { [column.key]: v, materialName: '', stockCardId: null })
        }}
        placeholder={column.placeholder || 'Kod giriniz...'}
        style={{
          flex: 1, height: '100%', background: inputBg,
          border: (column.required && !value && !displayVal)
            ? '1px solid rgba(239,68,68,0.7)'
            : inputBdr,
          borderRadius: 5, padding: '0 8px', fontSize: 12, color: inputColor,
          cursor: 'text', outline: 'none', minWidth: 0,
          fontFamily: 'Consolas, monospace',
        }}
      />
      {/* Temizle butonu */}
      {(displayVal || value) && (
        <button
          type="button"
          onClick={clearValue}
          title="Temizle"
          style={{
            flexShrink: 0, width: 20, height: 20, border: 'none',
            background: 'transparent', color: isLight ? '#94a3b8' : 'rgba(255,255,255,0.3)',
            cursor: 'pointer', borderRadius: 4, display: 'flex', alignItems: 'center', justifyContent: 'center',
            fontSize: 14, lineHeight: 1,
          }}
        >×</button>
      )}
      {/* Rehber aç butonu */}
      <button
        type="button"
        onClick={openModal}
        title={'Rehber: ' + column.guideCode}
        style={{
          flexShrink: 0, width: 26, height: 26, border: inputBdr,
          background: btnBg, color: btnColor,
          cursor: 'pointer', borderRadius: 5, display: 'flex', alignItems: 'center', justifyContent: 'center',
          fontSize: 14,
        }}
      >
        <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round">
          <circle cx="11" cy="11" r="8"/><path d="m21 21-4.35-4.35"/>
        </svg>
      </button>

      {/* Alan Ayarları formu */}
      <FieldSettingsForm
        column={column}
        isOpen={settingsOpen}
        onClose={function() { setSettingsOpen(false); setSchema(null) }}
      />

      {/* ── Rehber Arama Modalı ── */}
      {modalOpen && createPortal(
        <div
          onClick={function(e) { if (e.target === e.currentTarget) closeModal() }}
          style={{
            position: 'fixed', inset: 0, background: 'rgba(0,0,0,0.55)',
            display: 'flex', alignItems: 'center', justifyContent: 'center',
            zIndex: 9999, padding: 16,
          }}
        >
          <div style={Object.assign({ borderRadius: 12, width: '90%', maxWidth: 760, maxHeight: '80vh', display: 'flex', flexDirection: 'column', overflow: 'hidden' }, modalBg)}>

            {/* Başlık + arama */}
            <div style={{ display: 'flex', alignItems: 'center', gap: 8, padding: '10px 16px', borderBottom: divBdr, background: headerBg, flexShrink: 0 }}>
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke={btnColor} strokeWidth="2.2" strokeLinecap="round" style={{ flexShrink: 0 }}>
                <circle cx="11" cy="11" r="8"/><path d="m21 21-4.35-4.35"/>
              </svg>
              <input
                autoFocus
                type="text"
                value={search}
                onChange={function(e) { setSearch(e.target.value) }}
                placeholder={schema ? ('Ara: ' + (schema.guideLabel || column.guideCode)) : 'Yükleniyor...'}
                style={{
                  flex: 1, background: searchBg, border: searchBdr, borderRadius: 6,
                  padding: '6px 10px', fontSize: 13, color: inputColor, outline: 'none',
                }}
              />
              {column.formCode && (
                <button
                  type="button"
                  onClick={function() { setSettingsOpen(true) }}
                  title="Alan Ayarları"
                  style={{ background: 'none', border: 'none', color: thColor, cursor: 'pointer', padding: '0 4px', display: 'flex', alignItems: 'center' }}
                >
                  <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
                    <circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83-2.83l.06-.06A1.65 1.65 0 0 0 4.68 15a1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 2.83-2.83l.06.06A1.65 1.65 0 0 0 9 4.68a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 2.83l-.06.06A1.65 1.65 0 0 0 19.4 9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z"/>
                  </svg>
                </button>
              )}
              <button type="button" onClick={closeModal}
                style={{ background: 'none', border: 'none', color: thColor, cursor: 'pointer', fontSize: 18, lineHeight: 1, padding: '0 4px' }}>×</button>
            </div>

            {error && (
              <div style={{ padding: '8px 16px', fontSize: 12, color: '#f87171', background: 'rgba(239,68,68,0.08)', flexShrink: 0 }}>{error}</div>
            )}

            {/* Tablo */}
            <div ref={scrollRef} style={{ flex: 1, overflowY: 'auto' }}>
              <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 12 }}>
                <thead style={{ position: 'sticky', top: 0, background: headerBg, zIndex: 1 }}>
                  <tr>
                    {displayColumns.length > 0
                      ? displayColumns.map(function(c) {
                          return (
                            <th key={c} style={{ padding: '7px 12px', textAlign: 'left', color: thColor, fontWeight: 600, fontSize: 10.5, textTransform: 'uppercase', letterSpacing: '.05em', borderBottom: divBdr, whiteSpace: 'nowrap' }}>
                              {colLabels[c] || c}
                            </th>
                          )
                        })
                      : <th style={{ padding: '7px 12px', color: thColor }}>Yükleniyor...</th>
                    }
                  </tr>
                </thead>
                <tbody>
                  {rows.length === 0 && !loading && (
                    <tr>
                      <td colSpan={Math.max(displayColumns.length, 1)} style={{ padding: '24px', textAlign: 'center', color: thColor, fontSize: 12 }}>
                        Kayıt bulunamadı
                      </td>
                    </tr>
                  )}
                  {rows.map(function(guideRow, idx) {
                    return (
                      <tr
                        key={(guideRow.value || '') + '_' + idx}
                        onClick={function() { pickRow(guideRow) }}
                        style={{ cursor: 'pointer', borderBottom: '1px solid ' + (isLight ? '#f1f5f9' : 'rgba(255,255,255,0.04)') }}
                        onMouseEnter={function(e) { e.currentTarget.style.background = rowHover }}
                        onMouseLeave={function(e) { e.currentTarget.style.background = '' }}
                      >
                        {displayColumns.map(function(c) {
                          var cell = guideRow.cells ? guideRow.cells[c] : null
                          return (
                            <td key={c} style={{ padding: '7px 12px', color: tdColor, whiteSpace: 'nowrap' }}>
                              {cell != null ? String(cell) : ''}
                            </td>
                          )
                        })}
                      </tr>
                    )
                  })}
                  {hasMore && (
                    <tr ref={sentinelRef}>
                      <td colSpan={Math.max(displayColumns.length, 1)} style={{ padding: '8px 12px', textAlign: 'center', color: thColor, fontSize: 11 }}>
                        {loading ? 'Yükleniyor...' : 'Daha fazla...'}
                      </td>
                    </tr>
                  )}
                  {loading && !hasMore && rows.length === 0 && (
                    <tr>
                      <td colSpan={Math.max(displayColumns.length, 1)} style={{ padding: '16px', textAlign: 'center', color: thColor, fontSize: 11 }}>
                        Yükleniyor...
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>

            {/* Alt bilgi */}
            <div style={{ padding: '6px 16px', borderTop: divBdr, background: headerBg, flexShrink: 0, display: 'flex', alignItems: 'center', gap: 8 }}>
              <span style={{ fontSize: 10.5, color: thColor }}>{rows.length} kayıt{hasMore ? '+' : ''}</span>
              <span style={{ flex: 1 }} />
              <span style={{ fontSize: 10.5, color: thColor, fontFamily: 'monospace' }}>{column.guideCode}</span>
            </div>
          </div>
        </div>,
        document.body
      )}
    </div>
  )
}

/* ══════════════════════════════════════════════════════════════
   CombinationLookupCell — Kombinasyon seçim butonu + modal
   row.trackCombinations false ise pasif (—) gösterir
   row.combinationCode set ise butonda gözükür
   Modal kapanınca onChange('combinationCode', code) + onChange('combinationDetails', details)
   ══════════════════════════════════════════════════════════════ */
function CombinationLookupCell(props) {
  var column = props.column
  var row = props.row
  var value = props.value
  var onChange = props.onChange
  var [open, setOpen] = useState(false)

  var materialCode = row.materialCode || ''
  var trackable = !!row.trackCombinations
  // Fallback: lookupFillMap ile row'a trackCombinations gelmemisse global materials
  // snapshot'undan materialCode ile kontrol et (sales quote sayfasinda set edilir).
  if (!trackable && materialCode && typeof window !== 'undefined' && Array.isArray(window.__SQ_MATERIALS__)) {
    var match = window.__SQ_MATERIALS__.find(function (m) {
      var mc = m.materialCode != null ? m.materialCode : m.MaterialCode
      return mc === materialCode
    })
    if (match) {
      var tc = match.trackCombinations != null ? match.trackCombinations : match.TrackCombinations
      if (tc === true) trackable = true
    }
  }

  if (!trackable) {
    return (
      <div
        className="w-full px-2.5 py-2 text-[12px] text-center text-slate-300 dark:text-white/30"
        title="Bu malzemede kombinasyon takibi yok"
      >—</div>
    )
  }

  if (!materialCode) {
    return (
      <div
        className="w-full px-2.5 py-2 text-[12px] text-center text-slate-300 dark:text-white/30"
        title="Önce malzeme seçin"
      >—</div>
    )
  }

  var hasValue = !!value
  return (
    <>
      <button
        type="button"
        onClick={function() { setOpen(true) }}
        className={
          'w-full px-2.5 py-1.5 text-[12px] font-semibold rounded-md transition-colors ' +
          (hasValue
            ? 'bg-indigo-50 text-indigo-700 hover:bg-indigo-100 dark:bg-indigo-500/15 dark:text-indigo-300 dark:hover:bg-indigo-500/25'
            : 'bg-slate-100 text-slate-500 hover:bg-slate-200 dark:bg-white/[0.05] dark:text-white/55 dark:hover:bg-white/[0.1]')
        }
        title={hasValue ? ('Seçili kombinasyon: ' + value) : 'Kombinasyon seç'}
      >
        {hasValue ? value : 'Seç...'}
      </button>
      {open && (
        <CombinationPickerModal
          materialCode={materialCode}
          currentCode={value}
          currentDetails={row.combinationDetails || []}
          onApply={function(code, details) {
            // fillPatch kullanarak combinationCode + combinationDetails'i tek state update'te set et
            onChange('combinationCode', code, { combinationDetails: details })
            setOpen(false)
          }}
          onClose={function() { setOpen(false) }}
        />
      )}
    </>
  )
}
