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
  // Seri/lot izleme bayraklari — standart rehber view'inde YOKTUR; snapshot'tan tamamla ki
  // rehberden malzeme secilince satis siparisi grid'inde "Seri" kolonu (visibleWhenKey=trackSerial)
  // gorunsun ve otomatik seri modali acilsin. Snapshot GetMaterials'tan boolean gelir (=== true calisir).
  fillIfMissing('trackSerial',       readCaseInsensitive(m, 'trackSerial'))
  fillIfMissing('trackLot',          readCaseInsensitive(m, 'trackLot'))
  fillIfMissing('autoSerial',        readCaseInsensitive(m, 'autoSerial'))
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

  // ── Serial Entry (Seri Girişi / Seçimi) ────────────
  if (column.type === 'serial-entry') {
    return <SerialEntryCell column={column} row={row} value={value} onChange={onChange} />
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
    triggerSilentLineSave(patch)
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
      triggerSilentLineSave(enriched)
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
  // patch: onChange'e verilen patch objesi. trackCombinations=true ama
  // combinationId henuz secilmediyse backend validasyonu atmasin diye
  // kaydi erteleriz; kombinasyon secilince onApply zaten save triggerlar.
  function triggerSilentLineSave(patch) {
    if (column.saveOnResolve === false) return
    if (typeof window === 'undefined' || typeof window.sqSave !== 'function') return
    var p = patch || {}
    var tracks = p.trackCombinations === true
    var hasCombination = p.combinationId != null && p.combinationId !== '' && parseInt(p.combinationId, 10) > 0
    if (tracks && !hasCombination) return
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
    // Rehberden basarili secim → sessiz satir kaydi (KITT yok).
    // trackCombinations=true ve henüz combinationId seçilmediyse kayit ertele;
    // kombinasyon seçilince CombinationLookupCell.onApply zaten save triggerlar.
    var _saveTracksComb = patch.trackCombinations === true
    var _saveHasComb = patch.combinationId != null && patch.combinationId !== '' && parseInt(patch.combinationId, 10) > 0
    if (column.saveOnResolve !== false && !(_saveTracksComb && !_saveHasComb) &&
        typeof window !== 'undefined' && typeof window.sqSave === 'function') {
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
              // Material seciminde save ertelenmisti (combo bekleniyor); artik combo
              // secildi, sessiz kayit tetikle (validasyon artik gecmeli).
              if (column.saveOnResolve !== false && typeof window !== 'undefined' && typeof window.sqSave === 'function') {
                setTimeout(function() {
                  try { window.sqSave({ silent: true }) } catch (_) {}
                }, 150)
              }
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

/* ══════════════════════════════════════════════════════════════
   SerialEntryCell — Seri girişi/seçimi butonu + modal (Seri takibi Faz 2)

   column: { serialMode: 'entry' (giriş — serbest yazım/okutma) | 'pick'
             (çıkış/transfer — stoktaki InStock serilerden seçim, serialsUrl) }
   row.trackSerial: stok seri-takipli mi (lookupFillMap ile malzeme seçiminde dolar;
   yüklenmiş satırda serials listesi doluysa da aktif kabul edilir).
   row.autoSerial: girişte liste boş bırakılabilir — sunucu üretir (amber "Oto" rozeti).
   Değer: row.serials — string[] (kaydetme payload'ına aynen gider).
   ══════════════════════════════════════════════════════════════ */
function splitSerialText(text) {
  return String(text || '')
    .split(/[\n\r,;\t|]+/)
    .map(function(s) { return s.trim() })
    .filter(function(s) { return s.length > 0 })
}

function SerialEntryCell(props) {
  var column = props.column
  var row = props.row
  var onChange = props.onChange
  var isLight = useIsLight()
  var [open, setOpen] = useState(false)

  var serials = Array.isArray(props.value) ? props.value : (Array.isArray(row.serials) ? row.serials : [])
  var qty = parseNumber(row.quantity)
  var qtyInt = (qty != null && qty > 0 && qty === Math.trunc(qty)) ? qty : null
  var trackable = row.trackSerial === true || serials.length > 0
  var autoSerial = row.autoSerial === true
  var isEntry = column.serialMode !== 'pick'

  if (!trackable) {
    return (
      <div className="w-full px-2.5 py-2 text-center text-[13px] text-slate-300 dark:text-white/25" title="Bu stokta seri takibi yok">—</div>
    )
  }

  var ok = qtyInt != null && serials.length === qtyInt
  var autoPending = isEntry && autoSerial && serials.length === 0
  var btnClass
  if (ok) {
    btnClass = 'text-emerald-700 bg-emerald-100 hover:bg-emerald-200 dark:text-emerald-300 dark:bg-emerald-500/20 dark:hover:bg-emerald-500/30'
  } else if (autoPending) {
    btnClass = 'text-amber-700 bg-amber-100 hover:bg-amber-200 dark:text-amber-300 dark:bg-amber-500/20 dark:hover:bg-amber-500/30'
  } else {
    btnClass = 'text-rose-600 bg-rose-100 hover:bg-rose-200 dark:text-rose-300 dark:bg-rose-500/20 dark:hover:bg-rose-500/30'
  }
  var label = autoPending ? 'Oto' : (serials.length + '/' + (qtyInt != null ? qtyInt : '?'))
  var title = autoPending
    ? 'Seri listesi boş — kayıtta otomatik üretilecek (elle girmek için tıklayın)'
    : (isEntry ? 'Seri no girişi — ' : 'Stoktan seri seçimi — ') + serials.length + ' seri' + (qtyInt != null ? ' / ' + qtyInt + ' adet' : '')

  return (
    <>
      <button
        type="button"
        onClick={function() { setOpen(true) }}
        className={'mx-auto h-7 min-w-[52px] px-2 rounded-lg flex items-center justify-center gap-1 text-[11px] font-mono font-semibold transition-colors ' + btnClass}
        title={title}
      >
        {label}
      </button>
      {open && (
        <SerialEntryModal
          isLight={isLight}
          isEntry={isEntry}
          row={row}
          column={column}
          qtyInt={qtyInt}
          autoSerial={autoSerial}
          serials={serials}
          onApply={function(list) {
            onChange(column.key, list)
            setOpen(false)
          }}
          onClose={function() { setOpen(false) }}
        />
      )}
    </>
  )
}

function SerialEntryModal(props) {
  var isLight = props.isLight
  var isEntry = props.isEntry
  var qtyInt = props.qtyInt
  var [text, setText] = useState(props.serials.join('\n'))
  var [selected, setSelected] = useState(function() {
    var m = {}
    props.serials.forEach(function(s) { m[s.toLowerCase()] = s })
    return m
  })
  var [filter, setFilter] = useState('')

  // Pick modunda stoktaki (InStock) serileri getir — URL row token'larıyla çözülür
  var lookup = useLookup(!isEntry ? props.column.serialsUrl : null, props.row)

  useEffect(function() {
    function onKey(e) { if (e.key === 'Escape') props.onClose() }
    document.addEventListener('keydown', onKey)
    return function() { document.removeEventListener('keydown', onKey) }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  var entryList = splitSerialText(text)
  var entryDupes = entryList.length !== entryList
    .map(function(s) { return s.toLowerCase() })
    .filter(function(s, i, arr) { return arr.indexOf(s) === i }).length

  var pickedCount = Object.keys(selected).length
  var currentCount = isEntry ? entryList.length : pickedCount
  var countOk = qtyInt != null && currentCount === qtyInt
  var countClass = countOk
    ? 'text-emerald-600 dark:text-emerald-300'
    : 'text-rose-600 dark:text-rose-300'

  var options = (lookup.options || []).filter(function(o) {
    if (!filter) return true
    var q = filter.toLowerCase()
    return String(o.serialNo || '').toLowerCase().indexOf(q) !== -1
        || String(o.lotNo || '').toLowerCase().indexOf(q) !== -1
  })

  function toggle(serialNo) {
    var key = serialNo.toLowerCase()
    var next = Object.assign({}, selected)
    if (next[key]) delete next[key]
    else next[key] = serialNo
    setSelected(next)
  }

  // FIFO/FEFO otomatik doldurma (yalnızca çıkış/pick modu). Gereken adet (qtyInt) kadar
  // seriyi yöntemine göre sıralayıp seçer; mevcut seçimi bununla değiştirir. Sessiz otomatik
  // YOK — kullanıcı butona basınca çalışır, sonucu görüp gerekirse düzeltir.
  // FIFO: en eski giriş (ItemSerial.Created) önce. FEFO: en yakın SKT (Lot.ExpiryDate) önce, SKT'siz en sona.
  var canFill = !isEntry && qtyInt != null && qtyInt > 0
  var hasExpiry = (lookup.options || []).some(function(o) { return !!o.expiryDate })
  function fillByMethod(method) {
    if (!canFill) return
    var avail = (lookup.options || []).slice()
    avail.sort(function(a, b) {
      if (method === 'fefo') {
        var ea = a.expiryDate ? Date.parse(a.expiryDate) : Infinity   // SKT'siz seri en sona
        var eb = b.expiryDate ? Date.parse(b.expiryDate) : Infinity
        if (ea !== eb) return ea - eb
      }
      var ca = a.created ? Date.parse(a.created) : Infinity            // FIFO / FEFO tie-break: en eski önce
      var cb = b.created ? Date.parse(b.created) : Infinity
      if (ca !== cb) return ca - cb
      return String(a.serialNo || '').localeCompare(String(b.serialNo || ''))
    })
    var next = {}
    avail.slice(0, qtyInt).forEach(function(o) { next[String(o.serialNo).toLowerCase()] = o.serialNo })
    setSelected(next)
  }

  var panelStyle = isLight
    ? { background: '#ffffff', border: '1px solid #e2e8f0', boxShadow: '0 24px 64px rgba(0,0,0,0.22)' }
    : { background: 'rgba(15,20,35,0.97)', backdropFilter: 'blur(24px)', WebkitBackdropFilter: 'blur(24px)', border: '1px solid rgba(255,255,255,0.12)', boxShadow: '0 24px 64px rgba(0,0,0,0.5)' }

  return createPortal(
    <div
      style={{ position: 'fixed', inset: 0, zIndex: 10000, background: 'rgba(2,6,23,0.55)', backdropFilter: 'blur(3px)', display: 'flex', alignItems: 'center', justifyContent: 'center' }}
      onMouseDown={function(e) { if (e.target === e.currentTarget) props.onClose() }}
    >
      <div style={Object.assign({ width: 'min(480px, 92vw)', borderRadius: '14px', overflow: 'hidden' }, panelStyle)}>
        <div className="px-4 pt-3.5 pb-2.5 flex items-center justify-between">
          <div>
            <div className="text-[13px] font-semibold text-slate-800 dark:text-white/90">
              {isEntry ? 'Seri No Girişi' : 'Stoktan Seri Seçimi'}
            </div>
            <div className="text-[11px] text-slate-500 dark:text-white/45 font-mono">
              {(props.row.materialCode || '') + (props.row.materialName ? ' · ' + props.row.materialName : '')}
            </div>
          </div>
          <div className={'text-[12px] font-mono font-bold tabular-nums ' + countClass}>
            {currentCount + ' / ' + (qtyInt != null ? qtyInt : '?')}
          </div>
        </div>

        {isEntry ? (
          <div className="px-4 pb-2">
            <textarea
              autoFocus
              value={text}
              onChange={function(e) { setText(e.target.value) }}
              rows={9}
              placeholder={'Her satıra bir seri no (barkod okutucuyla art arda okutabilirsiniz)'}
              className="w-full rounded-lg px-3 py-2 text-[12.5px] font-mono outline-none border border-slate-200 bg-slate-50 text-slate-800 placeholder:text-slate-400 focus:ring-2 focus:ring-indigo-400/60 dark:border-white/10 dark:bg-white/[0.05] dark:text-white/85 dark:placeholder:text-white/35"
            />
            {entryDupes && <div className="mt-1 text-[11px] text-rose-600 dark:text-rose-300">Tekrarlanan seri no var.</div>}
            {props.autoSerial && (
              <div className="mt-1 text-[11px] text-amber-700 dark:text-amber-300">
                Bu stokta giriş serisi otomatik: listeyi tamamen boş bırakırsanız seri no'lar kayıtta üretilir.
              </div>
            )}
          </div>
        ) : (
          <div className="px-4 pb-2">
            <div className="relative mb-2">
              <Search size={13} className="absolute left-2.5 top-1/2 -translate-y-1/2 text-slate-400 dark:text-white/35" />
              <input
                autoFocus
                value={filter}
                onChange={function(e) { setFilter(e.target.value) }}
                placeholder="Seri / lot ara..."
                className="w-full rounded-lg pl-8 pr-3 py-1.5 text-[12px] outline-none border border-slate-200 bg-slate-50 text-slate-800 placeholder:text-slate-400 focus:ring-2 focus:ring-indigo-400/60 dark:border-white/10 dark:bg-white/[0.05] dark:text-white/85 dark:placeholder:text-white/35"
              />
            </div>
            <div className="flex items-center gap-1.5 mb-2">
              <span className="text-[10.5px] text-slate-400 dark:text-white/40">Otomatik doldur:</span>
              <button
                type="button"
                disabled={!canFill}
                onClick={function() { fillByMethod('fifo') }}
                title="İlk Giren İlk Çıkar — en eski girişli serileri seçer"
                className={'px-2 py-0.5 rounded-md text-[11px] font-semibold border transition-colors ' +
                  (canFill
                    ? 'border-indigo-300 text-indigo-600 hover:bg-indigo-50 dark:border-indigo-400/40 dark:text-indigo-300 dark:hover:bg-indigo-500/15'
                    : 'border-slate-200 text-slate-300 dark:border-white/10 dark:text-white/25 cursor-not-allowed')}
              >
                FIFO
              </button>
              <button
                type="button"
                disabled={!canFill || !hasExpiry}
                onClick={function() { fillByMethod('fefo') }}
                title={hasExpiry ? 'İlk Son Kullanma İlk Çıkar — en yakın SKT’li serileri seçer' : 'Bu stokta son kullanma tarihi (lot) bilgisi yok'}
                className={'px-2 py-0.5 rounded-md text-[11px] font-semibold border transition-colors ' +
                  (canFill && hasExpiry
                    ? 'border-emerald-300 text-emerald-600 hover:bg-emerald-50 dark:border-emerald-400/40 dark:text-emerald-300 dark:hover:bg-emerald-500/15'
                    : 'border-slate-200 text-slate-300 dark:border-white/10 dark:text-white/25 cursor-not-allowed')}
              >
                FEFO
              </button>
              {!canFill && <span className="text-[10px] text-slate-400 dark:text-white/30">(önce miktar girin)</span>}
            </div>
            <div className="max-h-[280px] overflow-y-auto rounded-lg border border-slate-200 dark:border-white/10">
              {lookup.loading && <div className="px-3 py-3 text-[11px] text-slate-400 dark:text-white/40">Yükleniyor…</div>}
              {!lookup.loading && options.length === 0 && (
                <div className="px-3 py-3 text-[11px] text-slate-400 dark:text-white/40">Stokta seçilebilir seri yok.</div>
              )}
              {options.map(function(o) {
                var checked = !!selected[String(o.serialNo).toLowerCase()]
                return (
                  <button
                    key={o.serialNo}
                    type="button"
                    onClick={function() { toggle(o.serialNo) }}
                    className={'w-full flex items-center gap-2.5 px-3 py-1.5 text-left transition-colors ' +
                      (checked
                        ? 'bg-indigo-50 dark:bg-indigo-500/15'
                        : 'hover:bg-slate-50 dark:hover:bg-white/[0.05]')}
                  >
                    <span className={'w-3.5 h-3.5 rounded border flex items-center justify-center text-[9px] font-bold ' +
                      (checked
                        ? 'bg-indigo-500 border-indigo-500 text-white'
                        : 'border-slate-300 dark:border-white/25 text-transparent')}>✓</span>
                    <span className="text-[12px] font-mono text-slate-800 dark:text-white/85">{o.serialNo}</span>
                    {o.lotNo && <span className="text-[10.5px] font-mono text-slate-400 dark:text-white/35">Lot: {o.lotNo}</span>}
                    {o.expiryDate && <span className="ml-auto text-[10.5px] font-mono text-amber-600 dark:text-amber-300/70">SKT: {String(o.expiryDate).slice(0, 10)}</span>}
                  </button>
                )
              })}
            </div>
          </div>
        )}

        <div className="px-4 py-3 flex items-center justify-end gap-2 border-t border-slate-100 dark:border-white/[0.07]">
          <button
            type="button"
            onClick={props.onClose}
            className="px-3.5 py-1.5 rounded-lg text-[12px] font-medium text-slate-600 hover:bg-slate-100 dark:text-white/60 dark:hover:bg-white/[0.07] transition-colors"
          >
            Vazgeç
          </button>
          <button
            type="button"
            onClick={function() {
              props.onApply(isEntry ? entryList : Object.keys(selected).map(function(k) { return selected[k] }))
            }}
            className="px-3.5 py-1.5 rounded-lg text-[12px] font-semibold text-white bg-indigo-500 hover:bg-indigo-600 transition-colors"
          >
            Uygula
          </button>
        </div>
      </div>
    </div>,
    document.body
  )
}

/* ══════════════════════════════════════════════════════════════
 * Sayım İzlenebilirlik — tek "Lot / Seri" butonu → amaca özel modal.
 * Seri-takipli kalem: seri tara/gir (adet = Sayılan Miktar).
 * Lot-takipli kalem: çoklu lot kırılımı (Lot No + miktar), toplam = Sayılan Miktar.
 * (Sayım kalem tablosunda satır-içi lot/seri kolonu yerine kullanılır.)
 * ══════════════════════════════════════════════════════════════ */
function LotBreakdownModal(props) {
  var isLight = props.isLight
  var [rows, setRows] = useState(function() {
    var v = Array.isArray(props.value) ? props.value : []
    return v.length > 0
      ? v.map(function(r) { return { lotNo: r.lotNo || '', qty: (r.qty != null ? String(r.qty) : '') } })
      : [{ lotNo: '', qty: '' }]
  })
  var lookup = useLookup(props.column && props.column.lotUrl ? props.column.lotUrl : null, props.row)

  useEffect(function() {
    function onKey(e) { if (e.key === 'Escape') props.onClose() }
    document.addEventListener('keydown', onKey)
    return function() { document.removeEventListener('keydown', onKey) }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  function setCell(i, key, val) { var n = rows.slice(); n[i] = Object.assign({}, n[i]); n[i][key] = val; setRows(n) }
  function addRow() { setRows(rows.concat([{ lotNo: '', qty: '' }])) }
  function removeRow(i) { var n = rows.slice(); n.splice(i, 1); setRows(n.length ? n : [{ lotNo: '', qty: '' }]) }

  var valid = rows.filter(function(r) { return String(r.lotNo).trim() && parseFloat(r.qty) > 0 })
  var total = valid.reduce(function(s, r) { return s + (parseFloat(r.qty) || 0) }, 0)
  var lotSuggest = (lookup.options || [])
  var dupLot = (function() { var seen = {}, d = false; valid.forEach(function(r) { var k = String(r.lotNo).trim().toLowerCase(); if (seen[k]) d = true; seen[k] = 1 }); return d })()

  var panelStyle = isLight
    ? { background: '#ffffff', border: '1px solid #e2e8f0', boxShadow: '0 24px 64px rgba(0,0,0,0.22)' }
    : { background: 'rgba(15,20,35,0.97)', backdropFilter: 'blur(24px)', WebkitBackdropFilter: 'blur(24px)', border: '1px solid rgba(255,255,255,0.12)', boxShadow: '0 24px 64px rgba(0,0,0,0.5)' }

  return createPortal(
    <div style={{ position: 'fixed', inset: 0, zIndex: 10000, background: 'rgba(2,6,23,0.55)', backdropFilter: 'blur(3px)', display: 'flex', alignItems: 'center', justifyContent: 'center' }}
      onMouseDown={function(e) { if (e.target === e.currentTarget) props.onClose() }}>
      <div style={Object.assign({ width: 'min(520px, 94vw)', borderRadius: '14px', overflow: 'hidden' }, panelStyle)}>
        <div className="px-4 pt-3.5 pb-2.5 flex items-center justify-between">
          <div>
            <div className="text-[13px] font-semibold text-slate-800 dark:text-white/90">Lot Kırılımı (Sayım)</div>
            <div className="text-[11px] text-slate-500 dark:text-white/45 font-mono">{(props.row.materialCode || '') + (props.row.materialName ? ' · ' + props.row.materialName : '')}</div>
          </div>
          {(function () {
            var target = parseNumber(props.qtyTarget)
            var match = target == null || Math.abs(total - target) < 0.0001
            return (
              <div className={'text-[12px] font-mono font-bold tabular-nums ' + (match ? 'text-emerald-600 dark:text-emerald-300' : 'text-rose-600 dark:text-rose-300')}>
                Toplam: {total}{target != null ? ' / ' + target : ''}
              </div>
            )
          })()}
        </div>
        <div className="px-4 pb-2 max-h-[320px] overflow-y-auto">
          {rows.map(function(r, i) {
            return (
              <div key={i} className="flex items-center gap-2 mb-1.5">
                <input list={'lotsg-' + i} value={r.lotNo} onChange={function(e) { setCell(i, 'lotNo', e.target.value) }} placeholder="Lot / Parti No"
                  className="flex-1 rounded-lg px-2.5 py-1.5 text-[12px] font-mono outline-none border border-slate-200 bg-slate-50 text-slate-800 placeholder:text-slate-400 focus:ring-2 focus:ring-indigo-400/60 dark:border-white/10 dark:bg-white/[0.05] dark:text-white/85" />
                <datalist id={'lotsg-' + i}>
                  {lotSuggest.map(function(o) { return <option key={o.lotNo} value={o.lotNo}>{o.label || o.lotNo}</option> })}
                </datalist>
                <input type="number" value={r.qty} min="0" step="any" onChange={function(e) { setCell(i, 'qty', e.target.value) }} placeholder="Miktar"
                  className="w-24 rounded-lg px-2.5 py-1.5 text-[12px] text-right font-mono outline-none border border-slate-200 bg-slate-50 text-slate-800 placeholder:text-slate-400 focus:ring-2 focus:ring-indigo-400/60 dark:border-white/10 dark:bg-white/[0.05] dark:text-white/85" />
                <button type="button" onClick={function() { removeRow(i) }} title="Satırı sil"
                  className="w-7 h-7 rounded-lg flex items-center justify-center text-[15px] leading-none text-rose-500 hover:bg-rose-100 dark:hover:bg-rose-500/15">×</button>
              </div>
            )
          })}
          <button type="button" onClick={addRow} className="mt-1 text-[11.5px] font-medium text-indigo-600 hover:text-indigo-700 dark:text-indigo-300">+ Lot Ekle</button>
          {dupLot && <div className="mt-1 text-[11px] text-rose-600 dark:text-rose-300">Aynı lot birden fazla girildi.</div>}
        </div>
        <div className="px-4 py-3 flex items-center justify-end gap-2 border-t border-slate-100 dark:border-white/[0.07]">
          <button type="button" onClick={props.onClose} className="px-3.5 py-1.5 rounded-lg text-[12px] font-medium text-slate-600 hover:bg-slate-100 dark:text-white/60 dark:hover:bg-white/[0.07]">Vazgeç</button>
          <button type="button" disabled={dupLot}
            onClick={function() { props.onApply(valid.map(function(r) { return { lotNo: String(r.lotNo).trim(), qty: parseFloat(r.qty) } }), total) }}
            className={'px-3.5 py-1.5 rounded-lg text-[12px] font-semibold text-white transition-colors ' + (dupLot ? 'bg-slate-300 dark:bg-white/15 cursor-not-allowed' : 'bg-indigo-500 hover:bg-indigo-600')}>Uygula</button>
        </div>
      </div>
    </div>,
    document.body
  )
}

/* ══════════════════════════════════════════════════════════════
 * Sayım — Zengin Seri Kırılımı: Seri No | SKT | Açıklama | Miktar tablosu.
 * Seri = parti (miktar serbest); toplam = Sayılan Miktar. Değer: row.serialBreakdown
 * ([{serialNo, expiryDate, description, qty}]). Uygula'da backend SKT→Lot, açıklama→ItemSerial'a yansıtır.
 * ══════════════════════════════════════════════════════════════ */
function SerialBreakdownModal(props) {
  var isLight = props.isLight
  var [rows, setRows] = useState(function() {
    var v = Array.isArray(props.value) ? props.value : []
    // Seri takibinde her seri = 1 fiziksel adet → miktar DAİMA 1 (sabit, değiştirilemez).
    // (Lot takibinde miktar serbesttir; o LotBreakdownModal'da ele alınır.)
    return v.length > 0
      ? v.map(function(r) { return { serialNo: r.serialNo || '', expiryDate: (r.expiryDate ? String(r.expiryDate).slice(0, 10) : ''), description: r.description || '', qty: '1' } })
      : [{ serialNo: '', expiryDate: '', description: '', qty: '1' }]
  })
  // Stoktaki seriler (SKT ile) — öneri + seri seçilince SKT otomatik dolar
  var lookup = useLookup(props.column && props.column.serialsUrl ? props.column.serialsUrl : null, props.row)
  var stockByNo = {}
  ;(lookup.options || []).forEach(function(o) { if (o && o.serialNo) stockByNo[String(o.serialNo).toLowerCase()] = o })

  useEffect(function() {
    function onKey(e) { if (e.key === 'Escape') props.onClose() }
    document.addEventListener('keydown', onKey)
    return function() { document.removeEventListener('keydown', onKey) }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  function setCell(i, key, val) {
    var n = rows.slice(); n[i] = Object.assign({}, n[i]); n[i][key] = val
    // Seri no bilinen bir stok serisiyse ve SKT boşsa, stok SKT'sini otomatik doldur
    if (key === 'serialNo') {
      var hit = stockByNo[String(val).trim().toLowerCase()]
      if (hit && hit.expiryDate && !n[i].expiryDate) n[i].expiryDate = String(hit.expiryDate).slice(0, 10)
    }
    setRows(n)
  }
  function addRow() { setRows(rows.concat([{ serialNo: '', expiryDate: '', description: '', qty: '1' }])) }
  function removeRow(i) { var n = rows.slice(); n.splice(i, 1); setRows(n.length ? n : [{ serialNo: '', expiryDate: '', description: '', qty: '1' }]) }

  // Seri = 1 adet (sabit) → geçerli satır sayısı = toplam.
  var valid = rows.filter(function(r) { return String(r.serialNo).trim() })
  var total = valid.length
  var serialSuggest = (lookup.options || [])
  var dup = (function() { var seen = {}, d = false; valid.forEach(function(r) { var k = String(r.serialNo).trim().toLowerCase(); if (seen[k]) d = true; seen[k] = 1 }); return d })()

  var panelStyle = isLight
    ? { background: '#ffffff', border: '1px solid #e2e8f0', boxShadow: '0 24px 64px rgba(0,0,0,0.22)' }
    : { background: 'rgba(15,20,35,0.97)', backdropFilter: 'blur(24px)', WebkitBackdropFilter: 'blur(24px)', border: '1px solid rgba(255,255,255,0.12)', boxShadow: '0 24px 64px rgba(0,0,0,0.5)' }
  var inCls = 'rounded-lg px-2.5 py-1.5 text-[12px] outline-none border border-slate-200 bg-slate-50 text-slate-800 placeholder:text-slate-400 focus:ring-2 focus:ring-indigo-400/60 dark:border-white/10 dark:bg-white/[0.05] dark:text-white/85'

  return createPortal(
    <div style={{ position: 'fixed', inset: 0, zIndex: 10000, background: 'rgba(2,6,23,0.55)', backdropFilter: 'blur(3px)', display: 'flex', alignItems: 'center', justifyContent: 'center' }}
      onMouseDown={function(e) { if (e.target === e.currentTarget) props.onClose() }}>
      <div style={Object.assign({ width: 'min(720px, 96vw)', borderRadius: '14px', overflow: 'hidden' }, panelStyle)}>
        <div className="px-4 pt-3.5 pb-2.5 flex items-center justify-between">
          <div>
            <div className="text-[13px] font-semibold text-slate-800 dark:text-white/90">Seri Kırılımı (Sayım)</div>
            <div className="text-[11px] text-slate-500 dark:text-white/45 font-mono">{(props.row.materialCode || '') + (props.row.materialName ? ' · ' + props.row.materialName : '')}</div>
          </div>
          {(function () {
            var target = parseNumber(props.qtyTarget)
            var match = target == null || Math.abs(total - target) < 0.0001
            return (
              <div className={'text-[12px] font-mono font-bold tabular-nums ' + (match ? 'text-emerald-600 dark:text-emerald-300' : 'text-rose-600 dark:text-rose-300')}>
                Toplam: {total}{target != null ? ' / ' + target : ''}
              </div>
            )
          })()}
        </div>
        <div className="px-4 pb-2 max-h-[340px] overflow-y-auto">
          <div className="flex items-center gap-2 mb-1 px-0.5 text-[10px] font-semibold uppercase tracking-wide text-slate-400 dark:text-white/35">
            <span className="flex-1">Seri No</span>
            <span className="w-36">SKT</span>
            <span className="flex-1">Açıklama</span>
            <span className="w-20 text-right">Miktar</span>
            <span className="w-7" />
          </div>
          {rows.map(function(r, i) {
            return (
              <div key={i} className="flex items-center gap-2 mb-1.5">
                <input list={'sbsg-' + i} value={r.serialNo} onChange={function(e) { setCell(i, 'serialNo', e.target.value) }} placeholder="Seri / parti no"
                  className={'flex-1 font-mono ' + inCls} />
                <datalist id={'sbsg-' + i}>
                  {serialSuggest.map(function(o) { return <option key={o.serialNo} value={o.serialNo}>{(o.lotNo ? 'Lot: ' + o.lotNo : '') + (o.expiryDate ? ' · SKT: ' + String(o.expiryDate).slice(0, 10) : '')}</option> })}
                </datalist>
                <input type="date" value={r.expiryDate} onChange={function(e) { setCell(i, 'expiryDate', e.target.value) }} title="Son Kullanma Tarihi"
                  className={'w-36 font-mono ' + inCls} />
                <input value={r.description} onChange={function(e) { setCell(i, 'description', e.target.value) }} placeholder="Açıklama"
                  className={'flex-1 ' + inCls} />
                <input type="number" value="1" readOnly tabIndex={-1} title="Seri takibinde her seri = 1 adet — değiştirilemez"
                  className={'w-20 text-right font-mono opacity-60 cursor-not-allowed ' + inCls} />
                <button type="button" onClick={function() { removeRow(i) }} title="Satırı sil"
                  className="w-7 h-7 rounded-lg flex items-center justify-center text-[15px] leading-none text-rose-500 hover:bg-rose-100 dark:hover:bg-rose-500/15">×</button>
              </div>
            )
          })}
          <button type="button" onClick={addRow} className="mt-1 text-[11.5px] font-medium text-indigo-600 hover:text-indigo-700 dark:text-indigo-300">+ Seri Ekle</button>
          {dup && <div className="mt-1 text-[11px] text-rose-600 dark:text-rose-300">Aynı seri birden fazla girildi.</div>}
        </div>
        <div className="px-4 py-3 flex items-center justify-end gap-2 border-t border-slate-100 dark:border-white/[0.07]">
          <button type="button" onClick={props.onClose} className="px-3.5 py-1.5 rounded-lg text-[12px] font-medium text-slate-600 hover:bg-slate-100 dark:text-white/60 dark:hover:bg-white/[0.07]">Vazgeç</button>
          <button type="button" disabled={dup}
            onClick={function() { props.onApply(valid.map(function(r) { return { serialNo: String(r.serialNo).trim(), expiryDate: r.expiryDate || null, description: (r.description || '').trim() || null, qty: 1 } }), total) }}
            className={'px-3.5 py-1.5 rounded-lg text-[12px] font-semibold text-white transition-colors ' + (dup ? 'bg-slate-300 dark:bg-white/15 cursor-not-allowed' : 'bg-indigo-500 hover:bg-indigo-600')}>Uygula</button>
        </div>
      </div>
    </div>,
    document.body
  )
}

// İŞLEM (sol aksiyon) alanında kompakt buton — modal grid seviyesinde render edilir
// (miktar girişinden sonra otomatik açılabilmesi için). Seri/Lot izlenebilirliğine göre
// renk + adet gösterir; onOpen ile grid'e satırı bildirir.
function TraceEntryCell(props) {
  var row = props.row
  var isSerial = row.trackSerial === true
  var isLot = row.trackLot === true
  if (!isSerial && !isLot) return null

  var qty = parseNumber(row.quantity)
  var count, ok
  if (isSerial) {
    var sb = Array.isArray(row.serialBreakdown) ? row.serialBreakdown : []
    if (sb.length > 0) {
      // Zengin seri kırılımı (seri=parti): satır sayısı rozette, miktar toplamı doğrular
      var stot = sb.reduce(function (s, r) { return s + (parseFloat(r.qty) || 0) }, 0)
      count = sb.length
      ok = count > 0 && (qty == null || Math.abs(stot - qty) < 0.0001)
    } else {
      var serials = Array.isArray(row.serials) ? row.serials : []
      count = serials.length
      ok = count > 0 && (qty == null || count === qty)
    }
  } else {
    var bd = Array.isArray(row.lotBreakdown) ? row.lotBreakdown : []
    var total = bd.reduce(function(s, r) { return s + (parseFloat(r.qty) || 0) }, 0)
    count = bd.length
    ok = count > 0 && (qty == null || Math.abs(total - qty) < 0.0001)
  }
  var cls = ok
    ? 'text-emerald-600 bg-emerald-50 hover:bg-emerald-100 dark:text-emerald-300 dark:bg-emerald-500/15 dark:hover:bg-emerald-500/25'
    : (count > 0
        ? 'text-rose-600 bg-rose-50 hover:bg-rose-100 dark:text-rose-300 dark:bg-rose-500/15 dark:hover:bg-rose-500/25'
        : 'text-slate-400 bg-slate-100 hover:bg-slate-200 dark:text-white/40 dark:bg-white/10 dark:hover:bg-white/[0.15]')
  return (
    <button
      type="button"
      onClick={function() { if (props.onOpen) props.onOpen(row) }}
      className={'w-7 h-7 rounded-lg flex items-center justify-center transition-colors text-[10px] font-mono font-bold ' + cls}
      title={(isSerial ? 'Seri' : 'Lot') + ' girişi' + (count > 0 ? ' — ' + count : '')}
      aria-label={(isSerial ? 'Seri' : 'Lot') + ' girişi'}
    >
      {count > 0 ? count : (isSerial ? 'S' : 'L')}
    </button>
  )
}

export { CombinationLookupCell, SerialEntryModal, LotBreakdownModal, SerialBreakdownModal, TraceEntryCell }
