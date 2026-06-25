/**
 * SmartBoardFilterPanel — Hayalet modunda lüks filtre paneli.
 *
 * Tasarim:
 *  - Sagdan slide-in panel (380px), backdrop opacity dusuk → arkadaki kartlar gorunur kalir
 *  - Panel kendisi glass+blur (backdrop-filter)
 *  - Master widget listesinden field secimi → operator → deger
 *  - Aktif filtreler localStorage'a kaydedilir (boardKey scope)
 *  - Apply → onChange callback ile SmartBoard'a iletir
 *
 * Operatorler (dataType bazli):
 *  - text/string:  contains, equals, startsWith
 *  - numeric/currency/percent: equals, gte, lte, between
 *  - date/datetime: equals, before, after, between
 *  - boolean: isTrue, isFalse
 */
import { useState, useEffect, useMemo } from 'react'
import { Filter, X, RotateCcw, Check } from 'lucide-react'

var STORAGE_KEY_PREFIX = 'cb-sb-filters:'

function loadFilters(boardKey) {
  if (!boardKey || typeof window === 'undefined') return []
  try {
    var raw = window.localStorage.getItem(STORAGE_KEY_PREFIX + boardKey)
    if (!raw) return []
    var arr = JSON.parse(raw)
    return Array.isArray(arr) ? arr : []
  } catch (e) { return [] }
}
// 2026-05-24: Filtre satiri "aktif" mi? Bool icin op='isTrue/isFalse' aktif sayilir,
// options icin secili eleman varsa aktif, diger tipler icin value (veya value2) dolu olmali.
function isActiveFilter(f) {
  if (!f) return false
  var dt = (f.dataType || '').toLowerCase()
  if (dt === 'boolean' || dt === 'bool') return f.op === 'isTrue' || f.op === 'isFalse'
  if (dt === 'options') {
    var sel = parseOptionsValue(f.value)
    return sel.length > 0
  }
  return (f.value !== '' && f.value != null) || (f.value2 !== '' && f.value2 != null)
}

// Options dataType value'su comma-separated string olarak saklanir (localStorage uyumlu).
// Parse → array of string codes.
function parseOptionsValue(v) {
  if (!v) return []
  if (Array.isArray(v)) return v.filter(Boolean)
  return String(v).split(',').map(function(s){ return s.trim() }).filter(Boolean)
}

function saveFilters(boardKey, filters) {
  if (!boardKey || typeof window === 'undefined') return
  try {
    if (!filters || !filters.some(isActiveFilter)) {
      window.localStorage.removeItem(STORAGE_KEY_PREFIX + boardKey)
    } else {
      window.localStorage.setItem(STORAGE_KEY_PREFIX + boardKey, JSON.stringify(filters))
    }
  } catch (e) { /* ignore */ }
}

// Operator listesi — dataType'a gore
function operatorsFor(dataType) {
  var dt = (dataType || 'text').toLowerCase()
  if (dt === 'numeric' || dt === 'currency' || dt === 'percent' || dt === 'integer' || dt === 'decimal') {
    return [
      { id: 'eq',  label: 'Esit' },
      { id: 'gte', label: 'En az' },
      { id: 'lte', label: 'En fazla' },
      { id: 'between', label: 'Arasinda' },
    ]
  }
  if (dt === 'date' || dt === 'datetime') {
    return [
      { id: 'eq',     label: 'Eşit' },
      { id: 'before', label: 'Once' },
      { id: 'after',  label: 'Sonra' },
      { id: 'between', label: 'Arasinda' },
    ]
  }
  if (dt === 'boolean' || dt === 'bool') {
    // 2026-05-24: 'any' = filtre uygulanmiyor (default). Radio buttonlar ile gosterilir.
    return [
      { id: 'any',     label: 'Tümü' },
      { id: 'isTrue',  label: 'Evet' },
      { id: 'isFalse', label: 'Hayır' },
    ]
  }
  if (dt === 'options') {
    // 2026-05-24: 'in' = secili listeden biri (multi-select). Operator dropdown gosterilmez,
    // chip-toggle UI direkt secimi yapar.
    return [ { id: 'in', label: 'Seçili' } ]
  }
  // text/string default
  return [
    { id: 'contains',   label: 'Icerir' },
    { id: 'eq',         label: 'Esit' },
    { id: 'startsWith', label: 'Baslar' },
  ]
}

function defaultOperator(dataType) {
  return operatorsFor(dataType)[0].id
}

// 2026-05-24: newFilterRow KALDIRILDI — artik kullanici manuel satir eklemiyor,
// tum alanlar acik. (buildDefaultFilterRows alanlardan otomatik uretir.)

// Widget DataType (DB) → Filter Panel datatype normalizasyonu.
// dbo.WidgetMas.DataType serbest string oldugundan filter operatorlerine map'liyoruz.
function normalizeDataType(dt) {
  var t = (dt || '').toLowerCase()
  if (t === 'numeric' || t === 'number' || t === 'integer' || t === 'decimal' ||
      t === 'currency' || t === 'percent' || t === 'money')
    return 'numeric'
  if (t === 'date') return 'date'
  if (t === 'datetime' || t === 'datetime-local' || t === 'timestamp') return 'datetime'
  if (t === 'boolean' || t === 'bool' || t === 'switch' || t === 'checkbox') return 'boolean'
  // 2026-05-24: 'options' — backend tanimli enum/picker, multi-select filtre.
  // dropdown / multi-select / coklu-secim / secim-listesi → hepsi options gibi davranir.
  if (t === 'options' || t === 'enum') return 'options'
  if (t === 'dropdown' || t === 'multi-select' || t === 'multi_select' || t === 'multiselect') return 'options'
  // lookup/link → text gibi davran (contains/equals)
  return 'text'
}

// Filtrelenebilir mi? group, grid, rehber/lookup, link tipleri filtrelenmez.
// 2026-05-24: dropdown / multi-select ARTIK filterable — options multi-select olarak gosterilir.
// Rehber/Lookup widget'lari (guide-list, lookup) yine filtre panelinde gozukmez.
function isFilterableWidget(w) {
  if (!w) return false
  var t = (w.dataType || w.DataType || '').toLowerCase()
  if (t === 'group' || t === 'grid') return false
  if (t === 'link') return false
  if (t === 'guide-list' || t === 'guide_list' || t === 'guidelist' || t === 'lookup') return false
  // IsActive=false olanlari da atla (admin pasif yapmis)
  if (w.isActive === false || w.IsActive === false) return false
  return true
}

// /api/widgets/forms/{formCode}/schema cache — ayni form icin tekrar tekrar fetch yapma.
var _schemaCache = {}

export default function SmartBoardFilterPanel(props) {
  var isOpen = !!props.isOpen
  var onClose = props.onClose || function () {}
  var onApply = props.onApply || function () {}
  var boardKey = props.boardKey || 'default'
  var formCode = props.formCode || ''
  var masterWidgets = Array.isArray(props.masterWidgets) ? props.masterWidgets : []
  // Entity listesi — sistem widget'larini (controller'larin direkt entity'ye
  // yazdigi w_status, w_planned_qty gibi) keşfetmek icin kullaniliyor. Master
  // listede olmayan ama her entity'nin widget'larinda yer alan alanlar
  // filtrelenebilir alan olarak listeye dahil edilir.
  var entities = Array.isArray(props.entities) ? props.entities : []
  var initialFilters = Array.isArray(props.filters) ? props.filters : null

  // ── Form widget schema (admin tanimlamis dinamik alanlar) ──
  // Backend: GET /api/widgets/forms/{formCode}/schema → WidgetFormSchemaDto
  // Standart alanlar (entity kolonlari) zaten masterWidgets ile geliyor; biz bunun
  // uzerine widget alanlarini ekliyoruz. Ayni id'de cakisma olursa master kazanir.
  var [widgetSchema, setWidgetSchema] = useState(function () {
    return formCode && _schemaCache[formCode] ? _schemaCache[formCode] : null
  })
  var [schemaLoading, setSchemaLoading] = useState(false)
  useEffect(function () {
    if (!isOpen || !formCode) return undefined
    if (_schemaCache[formCode]) {
      setWidgetSchema(_schemaCache[formCode])
      return undefined
    }
    setSchemaLoading(true)
    fetch('/api/widgets/forms/' + encodeURIComponent(formCode) + '/schema',
      { credentials: 'same-origin' })
      .then(function (r) {
        if (!r.ok) throw new Error('schema fetch failed: ' + r.status)
        return r.json()
      })
      .then(function (data) {
        var widgets = (data && Array.isArray(data.widgets)) ? data.widgets : []
        _schemaCache[formCode] = widgets
        setWidgetSchema(widgets)
      })
      .catch(function (err) {
        // Sessiz fallback — masterWidgets yine de calisir
        console.warn('[FilterPanel] Form schema yuklenemedi:', err.message)
        _schemaCache[formCode] = []
        setWidgetSchema([])
      })
      .finally(function () { setSchemaLoading(false) })
  }, [isOpen, formCode])

  // Theme
  var [isDark, setIsDark] = useState(function () {
    if (typeof document === 'undefined') return true
    return document.body.classList.contains('app-theme-dark') ||
           document.documentElement.classList.contains('dark')
  })
  useEffect(function () {
    function sync() {
      setIsDark(
        document.body.classList.contains('app-theme-dark') ||
        document.documentElement.classList.contains('dark')
      )
    }
    var obs = new MutationObserver(sync)
    obs.observe(document.body, { attributes: true, attributeFilter: ['class'] })
    obs.observe(document.documentElement, { attributes: true, attributeFilter: ['class'] })
    return function () { obs.disconnect() }
  }, [])

  // Filterable field listesi — masterWidgets (standart/built-in) + widgetSchema
  // (admin tanimlamis) + entities[0].widgets (controller'in entity'ye direkt yazdigi
  // sistem widget'lari; ornek WorkOrders'ta w_status, w_planned_qty vb.)
  // Sira: STANDARD ilk, sonra WIDGET. Cakisma olursa master kazanir.
  // Her field'a `source` etiketi ekliyoruz: 'standard' | 'widget' (group label icin).
  var filterableFields = useMemo(function () {
    var byId = {}
    var ordered = []

    // 1) BoardConfig.masterWidgets — backend'in source bilgisini taşıyabilir.
    //    Ornek: LogisticsController BuildItemsMasterWidgetsFromSchema admin form
    //    widget'larini source='widget' olarak işaretliyor → filtre panelinde
    //    "Widget Alanlari" grubunda gösterilir. Source yoksa varsayilan 'standard'.
    //    Rehber/Lookup widget'lari (guide-list, lookup) filtrelenmez — isFilterableWidget.
    masterWidgets.forEach(function (w) {
      if (!w || !w.id) return
      var t = (w.type || 'data').toLowerCase()
      if (t !== 'data' && t !== '') return // sadece data tipleri (badge/group atlanir)
      if (!isFilterableWidget(w)) return    // guide-list / lookup / multi-select vs.
      if (byId[w.id]) return
      var entry = {
        id: w.id, label: w.label || w.id,
        dataType: normalizeDataType(w.dataType),
        source: w.source === 'widget' ? 'widget' : 'standard',
        options: Array.isArray(w.options) ? w.options : null,  // ← options dataType icin
        group: w.group || null,             // ← collapsible alt-grup (Ornek: 'features')
        groupLabel: w.groupLabel || null,
      }
      byId[w.id] = entry
      ordered.push(entry)
    })

    // 2) Sistem widget'lari — entities[0].widgets'tan kesfedilen, master'da olmayan
    // alanlar. Backend bazi controller'larda sistem widget'larini master'a koymadan
    // dogrudan entity widget arrayine yaziyor (ornek WorkOrders); bu yuzden bu
    // sirada otomatik kesfedilir ve filtre panelinde 'standart' olarak gorunur.
    if (Array.isArray(entities) && entities.length > 0) {
      var first = entities[0]
      var firstWidgets = (first && Array.isArray(first.widgets)) ? first.widgets : []
      firstWidgets.forEach(function (w) {
        if (!w || !w.id) return
        var t = (w.type || 'data').toLowerCase()
        if (t !== 'data' && t !== '') return
        if (!isFilterableWidget(w)) return   // rehber/lookup/multi-select skip
        if (byId[w.id]) return
        var entry = {
          id: w.id, label: w.label || w.id,
          dataType: normalizeDataType(w.dataType),
          source: 'standard',
        }
        byId[w.id] = entry
        ordered.push(entry)
      })
    }

    // 3) Widget alanlar (dbo.WidgetMas, WidgetFormSchemaDto.widgets)
    if (Array.isArray(widgetSchema)) {
      widgetSchema.forEach(function (w) {
        if (!w || !w.widgetCode) return
        if (!isFilterableWidget(w)) return
        var key = w.widgetCode
        if (byId[key]) return // master kazanir
        // 2026-05-24: dropdown / multi-select widget'lari icin Options array'ini
        // {value,label} formuna donustur — combobox icin gerekli.
        var dtRaw = (w.dataType || '').toLowerCase()
        var optsForFilter = null
        if ((dtRaw === 'dropdown' || dtRaw === 'multi-select' || dtRaw === 'multi_select' || dtRaw === 'multiselect')
            && Array.isArray(w.options)) {
          optsForFilter = w.options.map(function (s) { return { value: s, label: s } })
        }
        var entry = {
          id: key, label: w.label || w.widgetCode,
          dataType: normalizeDataType(w.dataType),
          source: 'widget',
          options: optsForFilter,
        }
        byId[key] = entry
        ordered.push(entry)
      })
    }

    return ordered
  }, [masterWidgets, widgetSchema, entities])

  // 2026-05-23: Tum filtrelenebilir alanlar default olarak satir halinde gelir.
  // Kullanici "+ Filtre Ekle" demek zorunda kalmaz — sadece deger girer ve uygular.
  // Halen mevcut filtreyi koruma onceligi: saved/initial varsa onu kullan, yoksa
  // her field icin bos bir satir uret.
  function buildDefaultFilterRows(fields) {
    if (!Array.isArray(fields) || fields.length === 0) return []
    return fields.map(function (f, idx) {
      return {
        id: 'f_default_' + f.id + '_' + idx,
        fieldId:  f.id,
        dataType: f.dataType || 'text',
        label:    f.label || f.id,
        op:       defaultOperator(f.dataType),
        value:    '',
        value2:   '',
      }
    })
  }

  // Local filters state — panel acikken duzenlenir, Apply'da onApply'a gider
  var [filters, setFilters] = useState(function () {
    if (initialFilters !== null && initialFilters.length > 0) {
      return initialFilters.map(function (f) { return Object.assign({}, f) })
    }
    var saved = loadFilters(boardKey)
    if (saved && saved.length > 0) return saved
    return [] // filterableFields henuz hazir degil; useEffect'te dolduracagiz
  })

  // 2026-05-24: Tek noktada sync — asagidaki diger useEffect (filterableFields ile
  // birebir hizalama) artik tum durumlari ele aliyor (initial / saved / bos).

  // ESC
  useEffect(function () {
    if (!isOpen) return undefined
    function onKey(e) { if (e.key === 'Escape') onClose() }
    document.addEventListener('keydown', onKey)
    return function () { document.removeEventListener('keydown', onKey) }
  }, [isOpen, onClose])

  // 2026-05-24: Combobox <details> elementlerini dis tikla ile kapat.
  // (collapsible alt-gruplar `.cb-combobox` class'ina sahip degil, etkilenmez.)
  useEffect(function () {
    if (!isOpen) return undefined
    function onDocDown(e) {
      var openCbs = document.querySelectorAll('details.cb-combobox[open]')
      openCbs.forEach(function (d) {
        if (!d.contains(e.target)) d.removeAttribute('open')
      })
    }
    document.addEventListener('mousedown', onDocDown)
    return function () { document.removeEventListener('mousedown', onDocDown) }
  }, [isOpen])

  // ── Filter row CRUD ──
  // 2026-05-24: addRow/removeRow KALDIRILDI. Her alan icin sabit bir satir var
  // (filterableFields ile birebir). Kullanici sadece op + value duzenler.
  function updateRow(rowId, patch) {
    setFilters(function (prev) {
      return prev.map(function (f) {
        if (f.id !== rowId) return f
        var next = Object.assign({}, f, patch)
        // op degistiginde between'den cikiyorsa value2 temizle
        if (patch.op && patch.op !== 'between') next.value2 = ''
        return next
      })
    })
  }

  // Panel acildiginda + filterableFields hazir oldugunda → satirlari senkronla.
  // 1) initialFilters varsa onu seed kabul et (parent disardan basliyor)
  // 2) Yoksa localStorage'den (boardKey scope) yukle
  // 3) Her durumda filterableFields ile birebir hizala: her alan icin TEK satir,
  //    mevcut degerleri koru, eksik alanlar icin bos satir ekle, fazla alanlari at.
  useEffect(function () {
    if (!isOpen || filterableFields.length === 0) return undefined
    var seed = []
    if (initialFilters !== null && initialFilters.length > 0) {
      seed = initialFilters
    } else {
      var saved = loadFilters(boardKey)
      if (saved && saved.length > 0) seed = saved
    }
    var byFieldId = {}
    seed.forEach(function (f) {
      if (f && f.fieldId && !byFieldId[f.fieldId]) byFieldId[f.fieldId] = f
    })
    var aligned = filterableFields.map(function (field, idx) {
      var existing = byFieldId[field.id]
      var ops = operatorsFor(field.dataType)
      var common = {
        id: 'f_' + field.id + '_' + idx,
        fieldId:  field.id,
        dataType: field.dataType || 'text',
        label:    field.label || field.id,
        options:  field.options || null,
        group:    field.group || null,
        groupLabel: field.groupLabel || null,
      }
      if (existing) {
        var op = ops.find(function (o) { return o.id === existing.op }) ? existing.op : ops[0].id
        return Object.assign({}, common, {
          op: op,
          value:  existing.value  || '',
          value2: existing.value2 || '',
          matchMode: existing.matchMode || 'any',
        })
      }
      return Object.assign({}, common, {
        op:     defaultOperator(field.dataType),
        value:  '',
        value2: '',
        matchMode: 'any',
      })
    })
    setFilters(aligned)
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isOpen, filterableFields])

  function handleApply() {
    // Gecersiz satirlari (deger bos) at — ama between'in yarisinin dolu olmasi gecerli.
    // 2026-05-24: bool icin op='any' → filtre yok; options icin secim bos → filtre yok.
    var valid = filters.filter(isActiveFilter)
    saveFilters(boardKey, valid)
    onApply(valid)
    onClose()
  }

  function handleClearAll() {
    // 2026-05-23: "Sifirla" — default satirlari geri uretir (degerler bos),
    // savedFilters'i ve aktif filtreleri temizler.
    var freshRows = buildDefaultFilterRows(filterableFields)
    setFilters(freshRows)
    saveFilters(boardKey, [])
    onApply([])
  }

  // ── Stiller (theme aware) ──
  var bgPanel = isDark ? 'rgba(15, 23, 42, 0.92)' : 'rgba(255, 255, 255, 0.95)'
  var bgBackdrop = isDark ? 'rgba(7, 11, 22, 0.18)' : 'rgba(15, 23, 42, 0.10)'  // ← HAYALET: cok dusuk opacity
  var border = isDark ? 'rgba(255,255,255,0.08)' : 'rgba(15,23,42,0.08)'
  var textPrimary = isDark ? '#f1f5f9' : '#0f172a'
  var textMuted = isDark ? 'rgba(255,255,255,0.55)' : '#64748b'
  var textSubtle = isDark ? 'rgba(255,255,255,0.4)' : '#94a3b8'
  var rowBg = isDark ? 'rgba(255,255,255,0.04)' : '#f8fafc'
  var rowBorder = isDark ? 'rgba(255,255,255,0.06)' : '#e2e8f0'
  var inputBg = isDark ? 'rgba(255,255,255,0.04)' : '#ffffff'
  var inputBorder = isDark ? 'rgba(255,255,255,0.1)' : '#cbd5e1'

  // Panel her zaman DOM'da — animasyon transform/opacity ile
  return (
    <div
      style={{
        position: 'fixed', inset: 0, zIndex: 9990,
        pointerEvents: isOpen ? 'auto' : 'none',
      }}
      aria-hidden={!isOpen}
    >
      {/* Backdrop — hayalet (dusuk opacity, hafif blur) */}
      <div
        onClick={onClose}
        style={{
          position: 'absolute', inset: 0,
          background: bgBackdrop,
          backdropFilter: 'blur(2px)',
          WebkitBackdropFilter: 'blur(2px)',
          opacity: isOpen ? 1 : 0,
          transition: 'opacity 220ms cubic-bezier(.23,1,.32,1)',
        }}
      />

      {/* Panel — sagdan slide-in */}
      <aside
        data-nodirty
        style={{
          position: 'absolute', top: 0, right: 0, bottom: 0,
          width: 'min(520px, 100vw)',
          background: bgPanel,
          backdropFilter: 'blur(24px) saturate(140%)',
          WebkitBackdropFilter: 'blur(24px) saturate(140%)',
          borderLeft: '1px solid ' + border,
          boxShadow: isOpen ? '-24px 0 64px rgba(0,0,0,0.35)' : 'none',
          transform: isOpen ? 'translateX(0)' : 'translateX(100%)',
          transition: 'transform 280ms cubic-bezier(.23,1,.32,1), box-shadow 280ms',
          display: 'flex', flexDirection: 'column', overflow: 'hidden',
        }}
        onClick={function (e) { e.stopPropagation() }}
      >
        {/* Header */}
        <div style={{
          display: 'flex', alignItems: 'center', gap: 10, padding: '14px 18px',
          borderBottom: '1px solid ' + border, flexShrink: 0,
        }}>
          <div style={{
            width: 32, height: 32, borderRadius: 10,
            display: 'flex', alignItems: 'center', justifyContent: 'center',
            background: isDark ? 'rgba(99,102,241,0.18)' : '#eef2ff',
            border: '1px solid ' + (isDark ? 'rgba(99,102,241,0.3)' : '#c7d2fe'),
            color: isDark ? '#a5b4fc' : '#4338ca',
          }}>
            <Filter size={15} />
          </div>
          <div style={{ flex: 1, minWidth: 0 }}>
            <div style={{ fontSize: 13, fontWeight: 700, color: textPrimary, lineHeight: 1.2 }}>
              Filtreleme
            </div>
            <div style={{ fontSize: 10.5, color: textMuted, marginTop: 1 }}>
              {(function () {
                var stdCount = filterableFields.filter(function (x) { return x.source !== 'widget' }).length
                var wCount   = filterableFields.filter(function (x) { return x.source === 'widget' }).length
                var fieldsTxt
                if (schemaLoading) {
                  fieldsTxt = stdCount + ' standart alan · widget alanlar yukleniyor...'
                } else if (wCount > 0) {
                  fieldsTxt = stdCount + ' standart + ' + wCount + ' widget alan'
                } else {
                  fieldsTxt = stdCount + ' alan'
                }
                // 2026-05-23: Default'ta tum satirlar acik gelir; "aktif" sayisi sadece
                // gercekten deger giren satirlari (veya boolean) sayar.
                var activeCount = filters.filter(function (f) {
                  var dt2 = (f.dataType || '').toLowerCase()
                  if (dt2 === 'boolean' || dt2 === 'bool') {
                    // 2026-05-24: bool icin op='isTrue' veya 'isFalse' secildiyse aktif,
                    // 'any' (Tümü) default → filtre yok.
                    return f.op === 'isTrue' || f.op === 'isFalse'
                  }
                  if (f.op === 'between') return (f.value !== '' && f.value != null) || (f.value2 !== '' && f.value2 != null)
                  return f.value !== '' && f.value != null
                }).length
                var filtersTxt = activeCount === 0 ? 'aktif filtre yok' : (activeCount + ' aktif filtre')
                return fieldsTxt + ' · ' + filtersTxt
              })()}
            </div>
          </div>
          <button
            type="button"
            onClick={onClose}
            style={{
              padding: 6, borderRadius: 8, background: 'transparent',
              border: '1px solid transparent', color: textMuted, cursor: 'pointer',
              display: 'flex', alignItems: 'center', justifyContent: 'center',
            }}
            title="Kapat (Esc)"
          >
            <X size={15} />
          </button>
        </div>

        {/* Body — tum alanlar her zaman acik liste halinde.
            2026-05-24: Field dropdown + X (sil) + "+ Filtre Ekle" KALDIRILDI.
            Her alan icin sabit bir satir, kullanici sadece op + value duzenler. */}
        <div style={{ flex: 1, overflowY: 'auto', padding: '14px 18px' }}>
          {filterableFields.length === 0 ? (
            <div style={{
              padding: 16, textAlign: 'center', fontSize: 12, color: textSubtle, fontStyle: 'italic',
              background: rowBg, border: '1px dashed ' + rowBorder, borderRadius: 10,
            }}>
              Bu listede filtrelenebilecek alan yok.
            </div>
          ) : (() => {
              // Gruplandirma: Standart Alanlar + Widget Alanlar (varsa).
              // Aktif satir (deger girilmis) indigo highlight ile gosterilir.
              var groupOf = function (f) {
                var field = filterableFields.find(function (x) { return x.id === f.fieldId })
                return (field && field.source) || 'standard'
              }
              var standardRows = filters.filter(function (f) { return groupOf(f) !== 'widget' })
              var widgetRows   = filters.filter(function (f) { return groupOf(f) === 'widget' })

              var renderRow = function (f) {
                var ops = operatorsFor(f.dataType)
                var dt = (f.dataType || 'text').toLowerCase()
                var inputType = 'text'
                if (dt === 'numeric' || dt === 'currency' || dt === 'percent' || dt === 'integer' || dt === 'decimal') inputType = 'number'
                else if (dt === 'date') inputType = 'date'
                else if (dt === 'datetime') inputType = 'datetime-local'
                var isBool = (dt === 'boolean' || dt === 'bool')
                var isOptions = (dt === 'options')
                var isBetween = f.op === 'between'
                var isActive = isActiveFilter(f)

                // 2026-05-24: Options dataType — chip-toggle (<=6 seçenek) veya combobox (>6).
                // Field options array'inden seceneklerden cogunu secebilir.
                // Hicbiri secili degilse → filtre uygulanmaz (Tumu mantigi).
                if (isOptions) {
                  var opts = Array.isArray(f.options) ? f.options : []
                  var sel  = parseOptionsValue(f.value)
                  var selSet = {}; sel.forEach(function(v){ selSet[v] = true })
                  // 2026-05-24: Tum options widget'larinda combobox (Olcu Birimi, Gruplar,
                  // Ozellikler hepsi ayni gorunum). Chip-toggle artik kullanilmaz.
                  var useCombobox = true

                  if (useCombobox) {
                    // Combobox: <details><summary> tetikleyici + checkbox listesi
                    var selectedLabels = opts.filter(function(o){
                      return o && selSet[String(o.value)]
                    }).map(function(o){ return o.label || o.value })
                    var summaryText = sel.length === 0
                      ? 'Tümü'
                      : (sel.length === 1
                          ? (selectedLabels[0] || sel[0])
                          : (sel.length + ' seçili'))
                    return (
                      <div
                        key={f.id}
                        style={{
                          padding: '7px 10px', borderRadius: 9,
                          background: isActive
                            ? (isDark ? 'rgba(99,102,241,0.14)' : '#eef2ff')
                            : rowBg,
                          border: '1px solid ' + (isActive
                            ? (isDark ? 'rgba(99,102,241,0.35)' : '#c7d2fe')
                            : rowBorder),
                          transition: 'background 0.15s, border-color 0.15s',
                          display: 'grid',
                          gridTemplateColumns: 'minmax(0, 1fr) minmax(0, 1.6fr)',
                          gap: 8, alignItems: 'center',
                        }}
                      >
                        <div
                          title={f.label}
                          style={{
                            fontSize: 11.5, color: textPrimary,
                            fontWeight: isActive ? 600 : 500,
                            overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap',
                          }}>
                          {f.label}
                        </div>
                        <div style={{
                          display: 'grid',
                          gridTemplateColumns: '70px 1fr',  // toggle alani sabit, combobox kalan
                          gap: 4, alignItems: 'stretch',
                        }}>
                          {/* matchMode toggle — her zaman ayni yer kaplar, 2+ secim olmadiginda gizli */}
                          <button
                            type="button"
                            onClick={function () {
                              var next = f.matchMode === 'all' ? 'any' : 'all'
                              updateRow(f.id, { matchMode: next })
                            }}
                            title={f.matchMode === 'all'
                              ? 'Tum secili degerler eslesmeli (AND). Tikla → Herhangi (OR).'
                              : 'En az biri eslesmeli (OR). Tikla → Tumu (AND).'}
                            style={{
                              padding: '0 8px', borderRadius: 6,
                              background: f.matchMode === 'all'
                                ? (isDark ? 'rgba(99,102,241,0.35)' : '#6366f1')
                                : inputBg,
                              border: '1px solid ' + (f.matchMode === 'all'
                                ? (isDark ? 'rgba(165,180,252,0.6)' : '#4f46e5')
                                : inputBorder),
                              color: f.matchMode === 'all' ? '#fff' : textPrimary,
                              fontSize: 10, fontWeight: 600,
                              cursor: sel.length >= 2 ? 'pointer' : 'default',
                              visibility: sel.length >= 2 ? 'visible' : 'hidden',
                              pointerEvents: sel.length >= 2 ? 'auto' : 'none',
                            }}>
                            {f.matchMode === 'all' ? 'Tümü' : 'Herhangi'}
                          </button>
                        <details className="cb-combobox" style={{ position: 'relative', minWidth: 0 }}>
                          <summary style={{
                            cursor: 'pointer', listStyle: 'none',
                            padding: '5px 10px', borderRadius: 7,
                            background: inputBg, border: '1px solid ' + inputBorder,
                            color: textPrimary, fontSize: 11,
                            display: 'flex', alignItems: 'center', justifyContent: 'space-between',
                            outline: 'none',
                          }}>
                            <span style={{
                              overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap',
                              flex: 1, fontWeight: isActive ? 600 : 400,
                            }}>{summaryText}</span>
                            <span style={{ marginLeft: 6, fontSize: 9, opacity: 0.6 }}>▾</span>
                          </summary>
                          <div style={{
                            position: 'absolute', top: '100%', left: 0, right: 0, zIndex: 50,
                            marginTop: 4, maxHeight: 240, overflowY: 'auto',
                            background: isDark ? '#0f172a' : '#fff',
                            border: '1px solid ' + inputBorder, borderRadius: 7,
                            boxShadow: '0 8px 20px rgba(0,0,0,0.25)',
                            padding: 4,
                          }}>
                            {opts.map(function (o) {
                              var v = (o && o.value != null) ? String(o.value) : ''
                              var lab = (o && o.label) ? o.label : v
                              var selected = !!selSet[v]
                              return (
                                <label
                                  key={v || lab}
                                  style={{
                                    display: 'flex', alignItems: 'center', gap: 6,
                                    padding: '5px 8px', borderRadius: 5,
                                    cursor: 'pointer',
                                    fontSize: 11, color: textPrimary,
                                    background: selected
                                      ? (isDark ? 'rgba(99,102,241,0.18)' : '#eef2ff')
                                      : 'transparent',
                                  }}>
                                  <input
                                    type="checkbox"
                                    checked={selected}
                                    onChange={function () {
                                      var next = selected ? sel.filter(function(x){ return x !== v }) : sel.concat([v])
                                      updateRow(f.id, { value: next.join(',') })
                                    }}
                                    style={{ accentColor: '#6366f1', cursor: 'pointer' }}
                                  />
                                  <span style={{
                                    overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap',
                                    flex: 1,
                                  }}>{lab}</span>
                                </label>
                              )
                            })}
                            {opts.length === 0 && (
                              <div style={{ padding: 8, color: textSubtle, fontSize: 11, fontStyle: 'italic' }}>
                                (tanımlı seçenek yok)
                              </div>
                            )}
                          </div>
                        </details>
                        </div>
                      </div>
                    )
                  }

                  // Chip-toggle: az seçenek olduğunda (≤6)
                  return (
                    <div
                      key={f.id}
                      style={{
                        padding: '7px 10px', borderRadius: 9,
                        background: isActive
                          ? (isDark ? 'rgba(99,102,241,0.14)' : '#eef2ff')
                          : rowBg,
                        border: '1px solid ' + (isActive
                          ? (isDark ? 'rgba(99,102,241,0.35)' : '#c7d2fe')
                          : rowBorder),
                        transition: 'background 0.15s, border-color 0.15s',
                      }}
                    >
                      <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: opts.length > 0 ? 6 : 0 }}>
                        <div
                          title={f.label}
                          style={{
                            flex: 1, minWidth: 0,
                            fontSize: 11.5, color: textPrimary,
                            fontWeight: isActive ? 600 : 500,
                            overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap',
                          }}>
                          {f.label}
                        </div>
                        <span style={{ fontSize: 10, color: textSubtle, fontStyle: 'italic' }}>
                          {sel.length === 0 ? 'Tümü' : (sel.length + ' seçili')}
                        </span>
                      </div>
                      {opts.length === 0 ? (
                        <div style={{ fontSize: 10.5, color: textSubtle, fontStyle: 'italic' }}>
                          (tanımlı seçenek yok)
                        </div>
                      ) : (
                        <div style={{ display: 'flex', flexWrap: 'wrap', gap: 4 }}>
                          {opts.map(function (o) {
                            var v = (o && (o.value != null)) ? String(o.value) : ''
                            var lab = (o && o.label) ? o.label : v
                            var selected = !!selSet[v]
                            return (
                              <button
                                key={v || lab}
                                type="button"
                                onClick={function () {
                                  var next = selected ? sel.filter(function(x){ return x !== v }) : sel.concat([v])
                                  updateRow(f.id, { value: next.join(',') })
                                }}
                                title={lab}
                                style={{
                                  padding: '3px 8px', borderRadius: 5,
                                  background: selected
                                    ? (isDark ? 'rgba(99,102,241,0.35)' : '#6366f1')
                                    : inputBg,
                                  border: '1px solid ' + (selected
                                    ? (isDark ? 'rgba(165,180,252,0.6)' : '#4f46e5')
                                    : inputBorder),
                                  color: selected ? '#fff' : textPrimary,
                                  fontSize: 10.5, fontWeight: selected ? 600 : 500,
                                  cursor: 'pointer',
                                  maxWidth: 220,
                                  overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap',
                                  transition: 'all 0.12s',
                                }}>
                                {lab}
                              </button>
                            )
                          })}
                        </div>
                      )}
                    </div>
                  )
                }

                // 2026-05-24: Bool icin tek-satir radio group (Tümü / Evet / Hayır) —
                // operator dropdown ve value alani yerine. Default = 'any' (filtre yok).
                if (isBool) {
                  return (
                    <div
                      key={f.id}
                      style={{
                        display: 'grid',
                        gridTemplateColumns: 'minmax(0, 1fr) minmax(0, 1.6fr)',
                        gap: 8, alignItems: 'center',
                        padding: '7px 10px', borderRadius: 9,
                        background: isActive
                          ? (isDark ? 'rgba(99,102,241,0.14)' : '#eef2ff')
                          : rowBg,
                        border: '1px solid ' + (isActive
                          ? (isDark ? 'rgba(99,102,241,0.35)' : '#c7d2fe')
                          : rowBorder),
                        transition: 'background 0.15s, border-color 0.15s',
                      }}
                    >
                      <div
                        title={f.label}
                        style={{
                          fontSize: 11.5, color: textPrimary,
                          fontWeight: isActive ? 600 : 500,
                          overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap',
                        }}>
                        {f.label}
                      </div>
                      <div style={{ display: 'flex', gap: 4, justifyContent: 'flex-end' }}>
                        {ops.map(function (o) {
                          var selected = f.op === o.id
                          return (
                            <button
                              key={o.id}
                              type="button"
                              onClick={function () { updateRow(f.id, { op: o.id }) }}
                              style={{
                                padding: '5px 10px', borderRadius: 6,
                                background: selected
                                  ? (isDark ? 'rgba(99,102,241,0.35)' : '#6366f1')
                                  : inputBg,
                                border: '1px solid ' + (selected
                                  ? (isDark ? 'rgba(165,180,252,0.6)' : '#4f46e5')
                                  : inputBorder),
                                color: selected ? '#fff' : textPrimary,
                                fontSize: 11, fontWeight: selected ? 600 : 500,
                                cursor: 'pointer', minWidth: 50,
                                transition: 'all 0.12s',
                              }}>
                              {o.label}
                            </button>
                          )
                        })}
                      </div>
                    </div>
                  )
                }

                // 2026-05-24: 'Arasında' icin alt satira tam genislik iki input — date'ler sıgmıyordu.
                if (isBetween) {
                  return (
                    <div
                      key={f.id}
                      style={{
                        padding: '7px 10px', borderRadius: 9,
                        background: isActive
                          ? (isDark ? 'rgba(99,102,241,0.14)' : '#eef2ff')
                          : rowBg,
                        border: '1px solid ' + (isActive
                          ? (isDark ? 'rgba(99,102,241,0.35)' : '#c7d2fe')
                          : rowBorder),
                        transition: 'background 0.15s, border-color 0.15s',
                      }}
                    >
                      <div style={{
                        display: 'grid',
                        gridTemplateColumns: 'minmax(0, 1fr) 110px',
                        gap: 6, alignItems: 'center', marginBottom: 6,
                      }}>
                        <div title={f.label} style={{
                          fontSize: 11.5, color: textPrimary,
                          fontWeight: isActive ? 600 : 500,
                          overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap',
                        }}>{f.label}</div>
                        <select
                          value={f.op}
                          onChange={function (e) { updateRow(f.id, { op: e.target.value }) }}
                          style={{
                            width: '100%', padding: '5px 6px', borderRadius: 7,
                            background: inputBg, border: '1px solid ' + inputBorder,
                            color: textPrimary, fontSize: 11, outline: 'none',
                          }}>
                          {ops.map(function (o) {
                            return <option key={o.id} value={o.id}>{o.label}</option>
                          })}
                        </select>
                      </div>
                      <div style={{ display: 'flex', gap: 6 }}>
                        <input
                          type={inputType}
                          value={f.value || ''}
                          onChange={function (e) { updateRow(f.id, { value: e.target.value }) }}
                          placeholder="Min"
                          style={{
                            flex: 1, minWidth: 0, padding: '5px 8px', borderRadius: 7,
                            background: inputBg, border: '1px solid ' + inputBorder,
                            color: textPrimary, fontSize: 11, outline: 'none',
                          }}
                        />
                        <input
                          type={inputType}
                          value={f.value2 || ''}
                          onChange={function (e) { updateRow(f.id, { value2: e.target.value }) }}
                          placeholder="Max"
                          style={{
                            flex: 1, minWidth: 0, padding: '5px 8px', borderRadius: 7,
                            background: inputBg, border: '1px solid ' + inputBorder,
                            color: textPrimary, fontSize: 11, outline: 'none',
                          }}
                        />
                      </div>
                    </div>
                  )
                }

                return (
                  <div
                    key={f.id}
                    style={{
                      display: 'grid',
                      gridTemplateColumns: 'minmax(0, 1.2fr) 110px minmax(0, 1.4fr)',
                      gap: 6, alignItems: 'center',
                      padding: '7px 10px', borderRadius: 9,
                      background: isActive
                        ? (isDark ? 'rgba(99,102,241,0.14)' : '#eef2ff')
                        : rowBg,
                      border: '1px solid ' + (isActive
                        ? (isDark ? 'rgba(99,102,241,0.35)' : '#c7d2fe')
                        : rowBorder),
                      transition: 'background 0.15s, border-color 0.15s',
                    }}
                  >
                    {/* Field label (read-only) */}
                    <div
                      title={f.label}
                      style={{
                        fontSize: 11.5, color: textPrimary,
                        fontWeight: isActive ? 600 : 500,
                        overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap',
                      }}>
                      {f.label}
                    </div>

                    {/* Op dropdown */}
                    <select
                      value={f.op}
                      onChange={function (e) { updateRow(f.id, { op: e.target.value }) }}
                      style={{
                        width: '100%', padding: '5px 6px', borderRadius: 7,
                        background: inputBg, border: '1px solid ' + inputBorder,
                        color: textPrimary, fontSize: 11, outline: 'none',
                      }}>
                      {ops.map(function (o) {
                        return <option key={o.id} value={o.id}>{o.label}</option>
                      })}
                    </select>

                    {/* Value input — tek input (between asagidaki branch'te) */}
                    <input
                      type={inputType}
                      value={f.value || ''}
                      onChange={function (e) { updateRow(f.id, { value: e.target.value }) }}
                      placeholder="Deger"
                      style={{
                        width: '100%', padding: '5px 8px', borderRadius: 7,
                        background: inputBg, border: '1px solid ' + inputBorder,
                        color: textPrimary, fontSize: 11, outline: 'none',
                      }}
                    />
                  </div>
                )
              }

              // 2026-05-24: Alt-grup desteği — `f.group` dolu satirlar tek bir
              // collapsible (<details>) altinda toplanir. group olmayanlar normal satir olarak gosterilir.
              var renderGroup = function (title, rows) {
                if (rows.length === 0) return null
                var ungrouped = []
                var subgroups = {}  // groupKey → { label, rows[] }
                var subgroupOrder = []
                rows.forEach(function (r) {
                  if (r.group) {
                    if (!subgroups[r.group]) {
                      subgroups[r.group] = { label: r.groupLabel || r.group, rows: [] }
                      subgroupOrder.push(r.group)
                    }
                    subgroups[r.group].rows.push(r)
                  } else {
                    ungrouped.push(r)
                  }
                })
                // 2026-05-24: Tum satirlar collapsible icindeyse dis basligi gizle
                // (çift "Standart Alanlar" görünmesin).
                var showTitle = ungrouped.length > 0 || subgroupOrder.length === 0
                return (
                  <div key={title} style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
                    {showTitle && (
                      <div style={{
                        fontSize: 10, fontWeight: 700, color: textSubtle,
                        letterSpacing: '0.06em', textTransform: 'uppercase',
                        padding: '2px 4px',
                      }}>
                        {title}
                      </div>
                    )}
                    {ungrouped.map(renderRow)}
                    {subgroupOrder.map(function (key) {
                      var grp = subgroups[key]
                      var activeInGrp = grp.rows.filter(isActiveFilter).length
                      return (
                        <details key={key} style={{
                          marginTop: 4, borderRadius: 9,
                          border: '1px solid ' + (activeInGrp > 0
                            ? (isDark ? 'rgba(99,102,241,0.35)' : '#c7d2fe')
                            : rowBorder),
                          background: activeInGrp > 0
                            ? (isDark ? 'rgba(99,102,241,0.07)' : 'rgba(238,242,255,0.5)')
                            : rowBg,
                          transition: 'background 0.15s, border-color 0.15s',
                        }}>
                          <summary style={{
                            cursor: 'pointer',
                            padding: '8px 12px',
                            display: 'flex', alignItems: 'center', gap: 8,
                            fontSize: 12, fontWeight: 600, color: textPrimary,
                            outline: 'none',
                            listStyle: 'revert',
                          }}>
                            <span style={{ flex: 1 }}>{grp.label}</span>
                            <span style={{
                              fontSize: 10, fontWeight: 600,
                              padding: '2px 8px', borderRadius: 10,
                              background: activeInGrp > 0
                                ? (isDark ? 'rgba(99,102,241,0.3)' : '#6366f1')
                                : (isDark ? 'rgba(255,255,255,0.06)' : '#e2e8f0'),
                              color: activeInGrp > 0 ? '#fff' : textSubtle,
                            }}>
                              {activeInGrp > 0 ? (activeInGrp + ' aktif · ') : ''}{grp.rows.length}
                            </span>
                          </summary>
                          <div style={{
                            padding: '4px 8px 10px',
                            display: 'flex', flexDirection: 'column', gap: 6,
                          }}>
                            {grp.rows.map(renderRow)}
                          </div>
                        </details>
                      )
                    })}
                  </div>
                )
              }

              return (
                <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
                  {renderGroup('Standart Alanlar', standardRows)}
                  {renderGroup('Widget Alanlar (form)', widgetRows)}
                </div>
              )
            })()
          }
        </div>

        {/* Footer */}
        <div style={{
          display: 'flex', gap: 8, padding: '12px 18px',
          borderTop: '1px solid ' + border, flexShrink: 0,
          background: isDark ? 'rgba(0,0,0,0.18)' : 'rgba(248,250,252,0.7)',
        }}>
          <button
            type="button"
            onClick={handleClearAll}
            disabled={!filters.some(isActiveFilter)}
            style={{
              padding: '8px 14px', borderRadius: 9, fontSize: 12, fontWeight: 600,
              background: isDark ? 'rgba(255,255,255,0.04)' : '#f1f5f9',
              border: '1px solid ' + (isDark ? 'rgba(255,255,255,0.08)' : '#cbd5e1'),
              color: !filters.some(isActiveFilter) ? textSubtle : textPrimary,
              cursor: !filters.some(isActiveFilter) ? 'not-allowed' : 'pointer',
              opacity: !filters.some(isActiveFilter) ? 0.6 : 1,
              display: 'inline-flex', alignItems: 'center', gap: 5,
            }}
            title="Tumunu sifirla"
          >
            <RotateCcw size={12} />
            Sifirla
          </button>
          <div style={{ flex: 1 }} />
          <button
            type="button"
            onClick={onClose}
            style={{
              padding: '8px 14px', borderRadius: 9, fontSize: 12, fontWeight: 600,
              background: 'transparent',
              border: '1px solid ' + (isDark ? 'rgba(255,255,255,0.1)' : '#cbd5e1'),
              color: textPrimary, cursor: 'pointer',
            }}
          >
            Vazgec
          </button>
          <button
            type="button"
            onClick={handleApply}
            style={{
              padding: '8px 16px', borderRadius: 9, fontSize: 12, fontWeight: 700,
              background: 'linear-gradient(135deg, #6366f1, #4f46e5)',
              border: 'none', color: '#fff',
              cursor: 'pointer', boxShadow: '0 4px 12px rgba(99,102,241,0.35)',
              display: 'inline-flex', alignItems: 'center', gap: 5,
            }}
          >
            <Check size={12} />
            Uygula
          </button>
        </div>
      </aside>
    </div>
  )
}

// ── Yardimci: aktif filtre etiketi ──
// Chip strip'te gosterilecek ozet metin (label · op · value)
export function describeFilter(f) {
  if (!f) return ''
  var labelMap = {
    contains: 'icerir', eq: '=', startsWith: 'baslar',
    gte: '>=', lte: '<=', between: 'arasinda',
    before: 'once', after: 'sonra',
    isTrue: 'evet', isFalse: 'hayir',
  }
  var opTxt = labelMap[f.op] || f.op
  var dt = (f.dataType || '').toLowerCase()
  if (dt === 'boolean' || dt === 'bool') return (f.label || f.fieldId) + ': ' + opTxt
  if (f.op === 'between') {
    var a = f.value === '' || f.value == null ? '?' : String(f.value)
    var b = f.value2 === '' || f.value2 == null ? '?' : String(f.value2)
    return (f.label || f.fieldId) + ': ' + a + ' — ' + b
  }
  return (f.label || f.fieldId) + ' ' + opTxt + ' "' + (f.value || '') + '"'
}

// ── Client-side filter matcher ──
// Verilen entity'nin widget value'larini filter[]'e gore degerlendirir.
// True donerse entity gorunur kalir.
export function entityMatchesFilters(entity, filters) {
  if (!Array.isArray(filters) || !filters.some(isActiveFilter)) return true
  if (!entity || !Array.isArray(entity.widgets)) {
    // Widget yoksa sadece title/subtitle/description'da text-contains'a izin ver
    return filters.every(function (f) {
      if (f.op !== 'contains') return true
      var hay = ((entity && entity.title) || '') + ' ' +
                ((entity && entity.subtitle) || '') + ' ' +
                ((entity && entity.description) || '')
      return hay.toLowerCase().indexOf(String(f.value || '').toLowerCase()) !== -1
    })
  }
  var widgetByIdMap = {}
  entity.widgets.forEach(function (w) { if (w && w.id) widgetByIdMap[w.id] = w })

  return filters.every(function (f) {
    var w = widgetByIdMap[f.fieldId]
    var raw = w ? w.value : null
    var dt = (f.dataType || 'text').toLowerCase()

    if (dt === 'boolean' || dt === 'bool') {
      // 2026-05-24: op='any' (veya bilinmeyen) → filtre yok, tüm kayıtlar geçer.
      if (f.op !== 'isTrue' && f.op !== 'isFalse') return true
      var b = raw === true || raw === 'true' || raw === 1 || raw === '1'
      if (f.op === 'isTrue') return b
      return !b
    }

    if (dt === 'options') {
      // 2026-05-24: Multi-select. matchMode='all' → tum secili degerler entity'de
      // olmali (AND), default 'any' → en az biri eslesmeli (OR).
      var selList = parseOptionsValue(f.value)
      if (selList.length === 0) return true
      var entityVals = parseOptionsValue(raw)
      if (entityVals.length === 0) return false
      var mode = (f.matchMode === 'all') ? 'all' : 'any'
      if (mode === 'all') {
        for (var k = 0; k < selList.length; k++) {
          if (entityVals.indexOf(selList[k]) === -1) return false
        }
        return true
      }
      for (var i = 0; i < entityVals.length; i++) {
        if (selList.indexOf(entityVals[i]) !== -1) return true
      }
      return false
    }

    if (dt === 'numeric' || dt === 'currency' || dt === 'percent' || dt === 'integer' || dt === 'decimal') {
      var num = raw == null || raw === '' ? null : parseFloat(String(raw).replace(',', '.'))
      var v1  = f.value === '' || f.value == null ? null : parseFloat(String(f.value).replace(',', '.'))
      var v2  = f.value2 === '' || f.value2 == null ? null : parseFloat(String(f.value2).replace(',', '.'))
      if (num == null) return false
      if (f.op === 'eq')  return v1 != null && num === v1
      if (f.op === 'gte') return v1 != null && num >= v1
      if (f.op === 'lte') return v1 != null && num <= v1
      if (f.op === 'between') {
        if (v1 != null && num < v1) return false
        if (v2 != null && num > v2) return false
        return v1 != null || v2 != null
      }
      return true
    }

    if (dt === 'date' || dt === 'datetime') {
      var d  = raw ? new Date(raw) : null
      var d1 = f.value  ? new Date(f.value)  : null
      var d2 = f.value2 ? new Date(f.value2) : null
      if (!d || isNaN(d.getTime())) return false
      if (f.op === 'eq')     return d1 != null && d.toDateString() === d1.toDateString()
      if (f.op === 'before') return d1 != null && d.getTime() < d1.getTime()
      if (f.op === 'after')  return d1 != null && d.getTime() > d1.getTime()
      if (f.op === 'between') {
        if (d1 != null && d.getTime() < d1.getTime()) return false
        if (d2 != null && d.getTime() > d2.getTime()) return false
        return d1 != null || d2 != null
      }
      return true
    }

    // text/string
    var sRaw = raw == null ? '' : String(raw).toLowerCase()
    var sVal = String(f.value || '').toLowerCase()
    if (f.op === 'contains')   return sRaw.indexOf(sVal) !== -1
    if (f.op === 'eq')         return sRaw === sVal
    if (f.op === 'startsWith') return sRaw.indexOf(sVal) === 0
    return true
  })
}
