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
import { guideSearch } from '../DynamicWidgetRenderer/dynamicWidgetService'
import FieldSettingsForm from './FieldSettingsForm'
import CombinationPickerModal from './CombinationPickerModal'
import GuideLookupModal from '../GuideLookup/GuideLookupModal'
import { adaptFormatJson, extractValueDisplay } from '../GuideLookup/guideLookupAdapters'
import { buildLineExtraOptions, resolveTokens } from '../../utils/fieldTokens'
import { Shuffle, Lock, Search, Settings } from 'lucide-react'

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

/**
 * Rehber (guide) view'inde taxRate/stockCardId/trackCombinations gibi satis teklifi
 * icin kritik alanlar eksik kalabilir — DocumentEdit sayfasinin yukledigi
 * window.__SQ_MATERIALS__ snapshot'undan materialCode ile esleyip eksik alanlari
 * tamamlar. patch zaten doluysa override etmez.
 */
function enrichMaterialPatch(patch, materialCode) {
  if (!patch || !materialCode) return patch
  if (typeof window === 'undefined' || !Array.isArray(window.__SQ_MATERIALS__)) return patch
  var target = String(materialCode).trim().toLowerCase()
  var m = window.__SQ_MATERIALS__.find(function(x) {
    var mc = readCaseInsensitive(x, 'materialCode')
    return String(mc || '').trim().toLowerCase() === target
  })
  if (!m) return patch
  function fillIfMissing(key, val) {
    if (val == null) return
    if (patch[key] == null) patch[key] = val
  }
  fillIfMissing('taxRate',           readCaseInsensitive(m, 'taxRate'))
  fillIfMissing('stockCardId',       readCaseInsensitive(m, 'id'))
  fillIfMissing('trackCombinations', readCaseInsensitive(m, 'trackCombinations'))
  fillIfMissing('locationId',        readCaseInsensitive(m, 'defaultLocationId'))
  fillIfMissing('materialName',      readCaseInsensitive(m, 'materialName'))
  // Stok kartindaki master birim — kullanici malzeme secince Olcu Birimi alani otomatik dolar
  fillIfMissing('unitId',            readCaseInsensitive(m, 'unitId'))
  return patch
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
  // siblingColumns: parent grid'in tum kolon listesi — rehber kisitlarinda
  // @ dropdown'i icin "Kalem Bilgileri" grubunu uretmek icin kullanilir.
  var siblingColumns = Array.isArray(props.siblingColumns) ? props.siblingColumns : null
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
      return <GuideLookupCell column={column} row={row} value={value} onChange={onChange} baseInputClass={baseInputClass} siblingColumns={siblingColumns} />
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

  // ── Auto-open (guided new-line workflow) ──
  // handleAddRow sonrasi grid 'material' stage event'i firlatir; kendi row'umuz
  // ve bizim column.key === 'materialCode' ise input'u focus ederiz — onFocus
  // handler zaten dropdown'i acar ve filter'i sifirlar.
  useEffect(function () {
    function onAutoOpen(e) {
      var d = e.detail || {}
      if (d.stage !== 'material') return
      if (d.rowUid !== (row && row._uid)) return
      if (column.key !== 'materialCode') return
      if (inputRef.current) {
        try { inputRef.current.focus() } catch (_) {}
      }
    }
    window.addEventListener('lineGrid:autoOpenStage', onAutoOpen)
    return function () { window.removeEventListener('lineGrid:autoOpenStage', onAutoOpen) }
  }, [row && row._uid, column.key])

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
        var val = readCaseInsensitive(opt, optKey)
        // Undefined degerler varsayilani silmesin; sadece tanimli olanlari uygula
        if (val !== undefined) patch[rowKey] = val
      })
    }
    // Eksik alanlari global materials snapshot'undan tamamla (taxRate vb.)
    enrichMaterialPatch(patch, patch[column.key])
    onChange(column.key, patch[column.key], patch)
    setOpen(false)
    setFilter('')
    triggerSilentLineSave()
    // Guided workflow chain: material secildikten sonra trackCombinations=true
    // ise kombinasyon modal'ini ac; degilse direkt ek alanlar modal'ini ac.
    // Sadece materialCode column'u icin tetikle.
    if (column.key === 'materialCode') {
      var tracks = patch.trackCombinations === true
      var nextStage = tracks ? 'combo' : 'extras'
      requestAnimationFrame(function () {
        try {
          window.dispatchEvent(new CustomEvent('lineGrid:autoOpenStage', {
            detail: { rowUid: row && row._uid, stage: nextStage }
          }))
        } catch (_) {}
      })
    }
  }

  // Manuel yazim sonrasi blur: girilen kod options icinde birebir varsa pick et —
  // boylece lookupFillMap uygulanir (taxRate, stockCardId, vb. satira gelir).
  function handleBlur() {
    // Dropdown pick'i onMouseDown ile handle ediyor; burada filter degeri varsa degerlendir.
    if (!open) return
    var typed = (filter || '').trim()
    if (!typed) { setOpen(false); return }
    var exact = (lookup.options || []).find(function(o) {
      var v = String(readCaseInsensitive(o, valueKey) || '').trim().toLowerCase()
      return v === typed.toLowerCase()
    })
    if (exact) { handlePick(exact); return }
    // Dropdown'da eslesme yok — global materials snapshot'unda ara (ornegin
    // guideSearch sonuclari farkli filtrelenmis olabilir). Bulursa lookupFillMap
    // alanlarini doldur + sessiz kaydet.
    var enriched = { }
    enriched[column.key] = typed
    enrichMaterialPatch(enriched, typed)
    var enrichedHasFill = Object.keys(enriched).some(function(k) {
      return k !== column.key && enriched[k] != null
    })
    if (enrichedHasFill) {
      onChange(column.key, typed, enriched)
      setOpen(false)
      setFilter('')
      triggerSilentLineSave()
      return
    }
    // Eslesme yok — yazili degeri ham olarak kaydet, lookupFillMap alanlarini temizle
    var rawPatch = {}
    rawPatch[column.key] = typed
    if (column.lookupFillMap) {
      Object.keys(column.lookupFillMap).forEach(function(rowKey) { rawPatch[rowKey] = null })
    }
    onChange(column.key, typed, rawPatch)
    setOpen(false)
    setFilter('')
  }

  // Satir-ici sessiz kayit — materialCode basariyla cozulunce calisir.
  // window.sqSave({ silent:true }) KITT animasyonu olmadan kayder.
  // column.saveOnResolve !== false ise tetiklenir (sales quote varsayilani).
  function triggerSilentLineSave() {
    if (column.saveOnResolve === false) return
    if (typeof window === 'undefined' || typeof window.sqSave !== 'function') return
    // Setstate'in render'a commit olmasi icin kucuk bir delay
    setTimeout(function() {
      try { window.sqSave({ silent: true }) } catch (e) { /* ignore */ }
    }, 150)
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
        onBlur={function() {
          // 120ms delay: dropdown item tiklamasi onBlur'dan once handlePick'i tamamlasin
          setTimeout(handleBlur, 120)
        }}
        onKeyDown={function(e) {
          if (e.key === 'Enter') { e.preventDefault(); handleBlur() }
        }}
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
   GuideLookupCell — guideCode ile tam modal rehber deneyimi.
   Modal UI'i ortak `GuideLookupModal`'a delege edilir; bu hucre
   yalnizca grid-spesifik input shell'i + lookupFillMap + auto-open
   chain + guided workflow event'lerini yonetir.
   ══════════════════════════════════════════════════════════════ */

function GuideLookupCell(props) {
  var column         = props.column
  var row            = props.row  // row reference — auto-open chain icin
  var value          = props.value
  var onChange       = props.onChange
  var siblingColumns = Array.isArray(props.siblingColumns) ? props.siblingColumns : []

  var isLight = useIsLight()

  // Alan Ayarlari modal'inda @ dropdown'una eklenecek extra alan secenekleri:
  // kalem grid'indeki diger kolonlar + secili satirin kombinasyon attribute'lari.
  // siblingColumns/row degisince yeniden hesaplanir.
  var extraFieldOptions = useMemo(function () {
    return buildLineExtraOptions(siblingColumns, row)
  }, [siblingColumns, row])

  // Rehber arama icin runtime token context — `{#row.fieldKey}` ve
  // `{#row.combo.attr}` token'lari mevcut satirin canli verisinden cozulur.
  var tokenContext = useMemo(function () {
    var combo = {}
    if (row && Array.isArray(row.combinationDetails)) {
      row.combinationDetails.forEach(function (d) {
        if (!d) return
        var c = d.attributeCode || d.AttributeCode || d.code || d.Code
        if (!c) return
        combo[c] = d.value != null ? d.value
                  : d.Value != null ? d.Value
                  : d.attributeValue || d.AttributeValue || ''
      })
    }
    return { row: row || {}, combo: combo }
  }, [row])

  // staticConstraint hazirlanirken filterJson icindeki row/combo token'lari
  // canli degerlerle replace edilir. {#fieldId} (DOM) token'lari GuideLookupModal/
  // FixedFieldLookupBridge tarafinda da resolve edilir; biz burada nokta-prefiksli
  // token'lari erkenden gercek degerle dolduruyoruz (modal acilirken).
  var resolvedConstraint = useMemo(function () {
    if (!column.filterJson) return null
    return resolveTokens(column.filterJson, tokenContext)
  }, [column.filterJson, tokenContext])

  var [displayVal, setDisplayVal]     = useState('')
  var [modalOpen, setModalOpen]       = useState(false)
  var [settingsOpen, setSettingsOpen] = useState(false)
  var [schemaVersion, setSchemaVersion] = useState(0)
  var [isShaking, setIsShaking]       = useState(false)
  var inputElRef = useRef(null)

  // ── Auto-open (guided new-line workflow) ──
  // Grid 'material' stage event firlattiginda modal'i otomatik ac — rehber
  // sayfasinda dogrudan arama. Stok secilince pickRow chain'i devam ettirir.
  useEffect(function () {
    function onAutoOpen(e) {
      var d = e.detail || {}
      if (d.stage !== 'material') return
      if (d.rowUid !== (row && row._uid)) return
      if (column.key !== 'materialCode') return
      setModalOpen(true)
    }
    window.addEventListener('lineGrid:autoOpenStage', onAutoOpen)
    return function () { window.removeEventListener('lineGrid:autoOpenStage', onAutoOpen) }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [row && row._uid, column.key])

  // Sayfa yüklenince mevcut değeri göster (value = kod, display gösterme)
  useEffect(function () {
    if (!value || !column.guideCode) { setDisplayVal(''); return }
    setDisplayVal(String(value))
  }, [value, column.guideCode])

  // Adapter — Tip 1 (sabit alan) ile ayni: formatJson → birlesik columns
  var columnsAdapter = useCallback(function (schemaCols) {
    return adaptFormatJson(column.formatJson, schemaCols)
  }, [column.formatJson])

  function openModal() { setModalOpen(true) }
  function closeModal() {
    setModalOpen(false)
    // Modal kapanis akisi:
    //   handlePick → onPick(pickRow) → onClose(closeModal)
    //     - pickRow: setDisplayVal/onChange + requestAnimationFrame(chain dispatch)
    //     - closeModal: setModalOpen(false) + setTimeout(0) [bu blok]
    //   Sync flow biter, React re-render → modal unmount.
    //   setTimeout(0) RAF'tan ONCE calisir → input'a odak verilir.
    //   Chain modal (combo/extras) RAF'ta acilir, odagi alir.
    //   Chain bittiginde modal kapanir, browser odagi son aktif elemana (input)
    //   geri yansitir → kullanici materialCode alaninda kalir.
    setTimeout(function() {
      if (inputElRef.current && typeof inputElRef.current.focus === 'function') {
        try { inputElRef.current.focus() } catch (_) {}
      }
    }, 0)
  }

  function pickRow(guideRow) {
    // Per-field valueColumn override (column.formatJson). Yoksa row.value (rehber default).
    var override = extractValueDisplay(column.formatJson)
    var pickedVal = (override.valueColumn && guideRow.cells && guideRow.cells[override.valueColumn] != null)
      ? String(guideRow.cells[override.valueColumn])
      : (guideRow.value || '')

    var patch = {}
    patch[column.key] = pickedVal
    if (column.lookupFillMap && guideRow.cells) {
      Object.keys(column.lookupFillMap).forEach(function (gridKey) {
        var cellKey = column.lookupFillMap[gridKey]
        var val = readCaseInsensitive(guideRow.cells, cellKey)
        if (val != null) patch[gridKey] = val
      })
    }
    // Rehber view'i taxRate/stockCardId/trackCombinations barindirmayabilir —
    // satis teklifi sayfasinin yukledigi global materials snapshot'undan tamamla.
    enrichMaterialPatch(patch, pickedVal)
    setDisplayVal(pickedVal)
    onChange(column.key, pickedVal, patch)
    // Rehberden basarili secim → sessiz satir kaydi (KITT yok)
    if (column.saveOnResolve !== false && typeof window !== 'undefined' && typeof window.sqSave === 'function') {
      setTimeout(function () {
        try { window.sqSave({ silent: true }) } catch (e) { /* ignore */ }
      }, 150)
    }
    // Guided workflow chain: material secildikten sonra trackCombinations=true
    // ise kombinasyon modal'ini ac; degilse direkt ek alanlar modal'ini ac.
    // Odak yonetimi closeModal'de — chain modal acilmadan once input'a odaklanilir,
    // chain modal kapaninca browser odagi input'a geri verir.
    if (column.key === 'materialCode' && row && row._uid) {
      var tracks = patch.trackCombinations === true
      var nextStage = tracks ? 'combo' : 'extras'
      requestAnimationFrame(function () {
        try {
          window.dispatchEvent(new CustomEvent('lineGrid:autoOpenStage', {
            detail: { rowUid: row._uid, stage: nextStage }
          }))
        } catch (_) {}
      })
    }
  }

  function clearValue(e) {
    if (e && e.stopPropagation) e.stopPropagation()
    setDisplayVal('')
    var clearPatch = { [column.key]: null }
    if (column.lookupFillMap) {
      Object.keys(column.lookupFillMap).forEach(function (k) { clearPatch[k] = null })
    }
    onChange(column.key, null, clearPatch)
  }

  // Manuel kod yazma/paste sonrasi blur'da: kod gecerli mi kontrol et,
  // gecerliyse tum alanlari (lookupFillMap'e gore) otomatik doldur,
  // gecerli degilse kirmizi titrese (shake) animasyonu ile uyari ver.
  function triggerShake() {
    setIsShaking(true)
    setTimeout(function () { setIsShaking(false) }, 550)
  }

  async function validateAndApply() {
    var v = (displayVal || '').trim()
    var currentVal = value == null ? '' : String(value)
    if (v === currentVal) return // degisme yok
    if (!v) { clearValue(); return } // bos -> tumunu temizle
    if (!column.guideCode) {
      // Rehber yoksa direkt set et (validation yok)
      onChange(column.key, v, { [column.key]: v })
      return
    }
    try {
      var result = await guideSearch(column.guideCode, { search: v, page: 1, pageSize: 10 })
      var rows = (result && result.rows) || []
      var exact = rows.find(function (r) {
        return String(r.value || '').trim().toLowerCase() === v.toLowerCase()
      })
      if (exact) {
        // pickRow zaten lookupFillMap uygulayip patch ile parent'a iletiyor
        pickRow(exact)
      } else {
        triggerShake()
        setDisplayVal(currentVal)
        forceFocusBack()
      }
    } catch (e) {
      triggerShake()
      setDisplayVal(currentVal)
      forceFocusBack()
    }
  }

  // Hatali kod sonrasi: kullanici alandan cikamasin, odak geri verilsin + yazi secilsin.
  // Bu sayede ek alan / kombinasyon ekranlarina hatali kod ile gecilemez.
  function forceFocusBack() {
    setTimeout(function () {
      if (inputElRef.current && typeof inputElRef.current.focus === 'function') {
        try {
          inputElRef.current.focus()
          if (typeof inputElRef.current.select === 'function') {
            inputElRef.current.select()
          }
        } catch (_) { /* ignore */ }
      }
    }, 0)
  }

  function handleInputKeyDown(e) {
    if (e.key === 'Enter') {
      e.preventDefault()
      if (inputElRef.current) inputElRef.current.blur()
    } else if (e.key === 'Escape') {
      e.preventDefault()
      setDisplayVal(value == null ? '' : String(value))
      if (inputElRef.current) inputElRef.current.blur()
    }
  }

  // ── Stiller (sadece input shell + butonlar; modal kendi gl-* CSS'ini kullanir) ──
  var inputBg    = isLight ? '#fff'                : 'transparent'
  var inputBdr   = isLight ? '1px solid #e2e8f0'  : '1px solid rgba(255,255,255,0.12)'
  var inputColor = isLight ? '#1e293b'             : 'rgba(255,255,255,0.85)'
  var btnBg      = isLight ? '#f1f5f9'             : 'rgba(255,255,255,0.07)'
  var btnColor   = isLight ? '#6366f1'             : '#818cf8'

  // GuideLookupModal'a header'a sigdirilan ek buton — Alan Ayarlari
  var headerActions = column.formCode ? (
    <button
      type="button"
      onClick={function () { setSettingsOpen(true) }}
      title="Alan Ayarları"
      className="gl-settings-btn"
    >
      <Settings size={15} strokeWidth={2} />
    </button>
  ) : null

  return (
    <div style={{ display: 'flex', alignItems: 'center', width: '100%', height: '100%', gap: 2 }}>
      {/* Değer alanı — manuel girise izin verir, blur'da validate eder */}
      <input
        ref={inputElRef}
        type="text"
        value={displayVal}
        onChange={function (e) {
          // Sadece local state'i guncelle — parent'a gonderme henuz.
          // Boylece elle kod yazarken materialName/unit/stockCardId gibi alanlar silinmez.
          setDisplayVal(e.target.value)
        }}
        onBlur={validateAndApply}
        onKeyDown={handleInputKeyDown}
        placeholder={column.placeholder || 'Kod giriniz...'}
        className={isShaking ? 'lgc-invalid-shake' : ''}
        style={{
          flex: 1, height: '100%', background: inputBg,
          border: isShaking
            ? '1px solid #ef4444'
            : (column.required && !value && !displayVal)
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
        <Search size={13} strokeWidth={2.2} />
      </button>

      {/* Alan Ayarları formu */}
      <FieldSettingsForm
        column={column}
        isOpen={settingsOpen}
        onClose={function () {
          setSettingsOpen(false)
          // Kullanici kolon konfigurasyonunu degistirmis olabilir → modal schema'sini yenile
          setSchemaVersion(function (v) { return v + 1 })
        }}
        extraFieldOptions={extraFieldOptions}
      />

      {/* Birlesik rehber arama modal'i — Tip 1 / Tip 2 / GuideLookupCell hep ayni.
          staticConstraint icindeki {#row.*} ve {#row.combo.*} token'lari mevcut
          satirin canli verisi ile replace edildi (resolvedConstraint). */}
      <GuideLookupModal
        guideCode={column.guideCode}
        columnsAdapter={columnsAdapter}
        open={modalOpen}
        onClose={closeModal}
        onPick={pickRow}
        staticConstraint={resolvedConstraint}
        schemaVersion={schemaVersion}
        headerActions={headerActions}
      />
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
  var compact = !!props.compact // true: action-column icon button; false: inline cell button
  var [open, setOpen] = useState(false)

  // ── Auto-open (guided new-line workflow) ──
  // Grid 'combo' stage event'i → kombinasyon modal'ini ac. Malzeme secildikten
  // sonra LineGridCell.handlePick / GuideLookupCell.pickRow tarafindan firlatilir.
  useEffect(function () {
    function onAutoOpen(e) {
      var d = e.detail || {}
      if (d.stage !== 'combo') return
      if (d.rowUid !== (row && row._uid)) return
      // Malzeme secildi ama combination takibi yoksa modal disabled — yine de
      // cagri no-op olur; picker icinde materialCode kontrolu zaten var.
      setOpen(true)
    }
    window.addEventListener('lineGrid:autoOpenStage', onAutoOpen)
    return function () { window.removeEventListener('lineGrid:autoOpenStage', onAutoOpen) }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [row && row._uid])

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

  // ── Compact (action-column) mode ──
  if (compact) {
    var disabled = !trackable || !materialCode
    var disabledTitle = !trackable
      ? 'Bu malzemede kombinasyon takibi yok'
      : (!materialCode ? 'Once malzeme seciniz' : '')
    var hasValueCompact = !!value
    // Renk mantigi:
    //  - disabled (takip kapali veya malzeme yok) → gri/lock
    //  - takip acik AND secim YAPILMIS  → yesil (basariyla secili)
    //  - takip acik AND secim YAPILMAMIS → kirmizi (zorunlu, eksik)
    var btnClassCompact
    if (disabled) {
      btnClassCompact = 'text-slate-300 dark:text-white/15 cursor-not-allowed'
    } else if (hasValueCompact) {
      btnClassCompact = 'text-emerald-700 bg-emerald-100 hover:bg-emerald-200 dark:text-emerald-300 dark:bg-emerald-500/20 dark:hover:bg-emerald-500/30'
    } else {
      btnClassCompact = 'text-rose-600 bg-rose-100 hover:bg-rose-200 dark:text-rose-300 dark:bg-rose-500/20 dark:hover:bg-rose-500/30'
    }
    return (
      <>
        <button
          type="button"
          disabled={disabled}
          onClick={function() { if (!disabled) setOpen(true) }}
          data-comb-btn=""
          data-row-uid={row._uid || ''}
          data-needs-combo={(!disabled && !hasValueCompact) ? '1' : '0'}
          className={'w-7 h-7 rounded-lg flex items-center justify-center transition-colors ' + btnClassCompact}
          title={disabled
            ? disabledTitle
            : (hasValueCompact ? ('Secili kombinasyon: ' + value + ' — degistir') : 'Kombinasyon zorunlu — secim yapiniz')}
        >
          {disabled ? <Lock size={12} strokeWidth={1.8} /> : <Shuffle size={13} strokeWidth={1.8} />}
        </button>
        {open && (
          <CombinationPickerModal
            materialCode={materialCode}
            currentCode={value}
            currentId={row.combinationId || null}
            currentDetails={row.combinationDetails || []}
            onApply={function(configId, code, details) {
              // combinationId — DB'deki FK; combinationCode — display; details — ozellik listesi
              onChange('combinationCode', code, { combinationId: configId, combinationDetails: details })
              setOpen(false)
              // Guided workflow chain: kombinasyon secildikten sonra ek alanlar
              // modal'ini grid seviyesinde ac (extras stage).
              if (row && row._uid) {
                requestAnimationFrame(function () {
                  try {
                    window.dispatchEvent(new CustomEvent('lineGrid:autoOpenStage', {
                      detail: { rowUid: row._uid, stage: 'extras' }
                    }))
                  } catch (_) {}
                })
              }
            }}
            onClose={function() { setOpen(false) }}
          />
        )}
      </>
    )
  }

  // ── Inline (grid cell) mode — eski davranis ──
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
  // Inline mode renkleri: secili → yesil, eksik → kirmizi
  var inlineBtnClass = hasValue
    ? 'bg-emerald-100 text-emerald-700 hover:bg-emerald-200 dark:bg-emerald-500/20 dark:text-emerald-300 dark:hover:bg-emerald-500/30'
    : 'bg-rose-100 text-rose-700 hover:bg-rose-200 dark:bg-rose-500/20 dark:text-rose-300 dark:hover:bg-rose-500/30'
  return (
    <>
      <button
        type="button"
        onClick={function() { setOpen(true) }}
        data-comb-btn=""
        data-row-uid={row._uid || ''}
        data-needs-combo={hasValue ? '0' : '1'}
        className={'w-full px-2.5 py-1.5 text-[12px] font-semibold rounded-md transition-colors ' + inlineBtnClass}
        title={hasValue ? ('Seçili kombinasyon: ' + value) : 'Kombinasyon zorunlu — secim yapiniz'}
      >
        {hasValue ? value : 'Seç...'}
      </button>
      {open && (
        <CombinationPickerModal
          materialCode={materialCode}
          currentCode={value}
          currentId={row.combinationId || null}
          currentDetails={row.combinationDetails || []}
          onApply={function(configId, code, details) {
            // fillPatch kullanarak combinationId + combinationCode + combinationDetails'i tek state update'te set et
            onChange('combinationCode', code, { combinationId: configId, combinationDetails: details })
            setOpen(false)
            // Guided workflow chain: kombinasyon secildikten sonra ek alanlar
            // modal'ini grid seviyesinde ac (extras stage).
            if (row && row._uid) {
              requestAnimationFrame(function () {
                try {
                  window.dispatchEvent(new CustomEvent('lineGrid:autoOpenStage', {
                    detail: { rowUid: row._uid, stage: 'extras' }
                  }))
                } catch (_) {}
              })
            }
          }}
          onClose={function() { setOpen(false) }}
        />
      )}
    </>
  )
}

export { CombinationLookupCell }
