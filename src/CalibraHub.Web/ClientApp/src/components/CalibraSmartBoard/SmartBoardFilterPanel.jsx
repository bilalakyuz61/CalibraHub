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
import { useState, useEffect, useRef, useMemo, useCallback } from 'react'
import { Filter, X, Plus, Trash2, RotateCcw, Check, ChevronDown } from 'lucide-react'

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
function saveFilters(boardKey, filters) {
  if (!boardKey || typeof window === 'undefined') return
  try {
    if (!filters || filters.length === 0) {
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
      { id: 'eq',     label: 'Tarih' },
      { id: 'before', label: 'Once' },
      { id: 'after',  label: 'Sonra' },
      { id: 'between', label: 'Arasinda' },
    ]
  }
  if (dt === 'boolean' || dt === 'bool') {
    return [
      { id: 'isTrue',  label: 'Aktif/Evet' },
      { id: 'isFalse', label: 'Pasif/Hayir' },
    ]
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

// Bos filter satiri — yeni eklendiginde
function newFilterRow(masterWidgets) {
  var first = (masterWidgets && masterWidgets[0]) || null
  return {
    id: 'f_' + Date.now() + '_' + Math.floor(Math.random() * 1000),
    fieldId:  first ? first.id : '',
    dataType: first ? (first.dataType || 'text') : 'text',
    label:    first ? (first.label || first.id) : '',
    op:       first ? defaultOperator(first.dataType) : 'contains',
    value:    '',
    value2:   '', // between icin ikinci deger
  }
}

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
  // dropdown/multi-select/lookup/link → text gibi davran (contains/equals)
  return 'text'
}

// Filtrelenebilir mi? group, grid, multi-select tipleri filtrelenmez.
function isFilterableWidget(w) {
  if (!w) return false
  var t = (w.dataType || w.DataType || '').toLowerCase()
  if (t === 'group' || t === 'grid') return false
  if (t === 'multi-select' || t === 'multi_select' || t === 'multiselect') return false
  if (t === 'link') return false
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

    // 1) Standart alanlar (BoardConfig.masterWidgets)
    masterWidgets.forEach(function (w) {
      if (!w || !w.id) return
      var t = (w.type || 'data').toLowerCase()
      if (t !== 'data' && t !== '') return // sadece data tipleri (badge/group atlanir)
      if (byId[w.id]) return
      var entry = {
        id: w.id, label: w.label || w.id,
        dataType: normalizeDataType(w.dataType),
        source: 'standard',
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
        var entry = {
          id: key, label: w.label || w.widgetCode,
          dataType: normalizeDataType(w.dataType),
          source: 'widget',
        }
        byId[key] = entry
        ordered.push(entry)
      })
    }

    return ordered
  }, [masterWidgets, widgetSchema, entities])

  // Local filters state — panel acikken duzenlenir, Apply'da onApply'a gider
  var [filters, setFilters] = useState(function () {
    if (initialFilters !== null) return initialFilters.map(function (f) { return Object.assign({}, f) })
    return loadFilters(boardKey)
  })

  // Open olduğunda initial filters'i sync et (parent disaridan ekleme/silme yaparsa)
  useEffect(function () {
    if (isOpen && initialFilters !== null) {
      setFilters(initialFilters.map(function (f) { return Object.assign({}, f) }))
    }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isOpen])

  // ESC
  useEffect(function () {
    if (!isOpen) return undefined
    function onKey(e) { if (e.key === 'Escape') onClose() }
    document.addEventListener('keydown', onKey)
    return function () { document.removeEventListener('keydown', onKey) }
  }, [isOpen, onClose])

  // ── Filter row CRUD ──
  function addRow() {
    if (filterableFields.length === 0) return
    setFilters(function (prev) { return prev.concat([newFilterRow(filterableFields)]) })
  }
  function removeRow(rowId) {
    setFilters(function (prev) { return prev.filter(function (f) { return f.id !== rowId }) })
  }
  function updateRow(rowId, patch) {
    setFilters(function (prev) {
      return prev.map(function (f) {
        if (f.id !== rowId) return f
        var next = Object.assign({}, f, patch)
        // fieldId degistiyse dataType + op + value reset
        if (patch.fieldId) {
          var w = filterableFields.find(function (x) { return x.id === patch.fieldId })
          if (w) {
            next.dataType = w.dataType || 'text'
            next.label    = w.label || w.id
            // Eski op yeni dataType'da gecerli mi?
            var ops = operatorsFor(next.dataType)
            if (!ops.find(function (o) { return o.id === next.op })) {
              next.op = ops[0].id
            }
            next.value = ''
            next.value2 = ''
          }
        }
        // op degistiginde between'den cikiyorsa value2 temizle
        if (patch.op && patch.op !== 'between') next.value2 = ''
        return next
      })
    })
  }

  function handleApply() {
    // Gecersiz satirlari (deger bos) at — ama between'in yarisinin dolu olmasi gecerli
    var valid = filters.filter(function (f) {
      var dt = (f.dataType || '').toLowerCase()
      if (dt === 'boolean' || dt === 'bool') return true // bool icin deger gerekmez
      if (f.op === 'between') return (f.value !== '' && f.value !== null) || (f.value2 !== '' && f.value2 !== null)
      return f.value !== '' && f.value !== null
    })
    saveFilters(boardKey, valid)
    onApply(valid)
    onClose()
  }

  function handleClearAll() {
    setFilters([])
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
        style={{
          position: 'absolute', top: 0, right: 0, bottom: 0,
          width: 'min(420px, 100vw)',
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
                var filtersTxt = filters.length === 0 ? 'aktif filtre yok' : (filters.length + ' aktif filtre')
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

        {/* Body — filter rows */}
        <div style={{ flex: 1, overflowY: 'auto', padding: '14px 18px' }}>
          {filterableFields.length === 0 ? (
            <div style={{
              padding: 16, textAlign: 'center', fontSize: 12, color: textSubtle, fontStyle: 'italic',
              background: rowBg, border: '1px dashed ' + rowBorder, borderRadius: 10,
            }}>
              Bu listede filtrelenebilecek alan yok.
            </div>
          ) : filters.length === 0 ? (
            <div style={{
              padding: 24, textAlign: 'center', fontSize: 12.5, color: textSubtle, fontStyle: 'italic',
              background: rowBg, border: '1px dashed ' + rowBorder, borderRadius: 12,
            }}>
              Filtre eklemek icin "+ Filtre" butonuna basin
            </div>
          ) : (
            <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
              {filters.map(function (f, idx) {
                var ops = operatorsFor(f.dataType)
                var dt = (f.dataType || 'text').toLowerCase()
                var inputType = 'text'
                if (dt === 'numeric' || dt === 'currency' || dt === 'percent' || dt === 'integer' || dt === 'decimal') inputType = 'number'
                else if (dt === 'date') inputType = 'date'
                else if (dt === 'datetime') inputType = 'datetime-local'
                var isBool = (dt === 'boolean' || dt === 'bool')
                var isBetween = f.op === 'between'

                return (
                  <div
                    key={f.id}
                    style={{
                      padding: 12, borderRadius: 12,
                      background: rowBg, border: '1px solid ' + rowBorder,
                      display: 'flex', flexDirection: 'column', gap: 8,
                    }}
                  >
                    <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                      <span style={{
                        fontSize: 10, fontWeight: 700, color: textSubtle, letterSpacing: '0.04em',
                        textTransform: 'uppercase', flexShrink: 0,
                      }}>
                        {idx + 1}
                      </span>
                      <select
                        value={f.fieldId}
                        onChange={function (e) { updateRow(f.id, { fieldId: e.target.value }) }}
                        style={{
                          flex: 1, padding: '6px 8px', borderRadius: 8,
                          background: inputBg, border: '1px solid ' + inputBorder,
                          color: textPrimary, fontSize: 12, outline: 'none',
                        }}
                      >
                        {(function () {
                          // optgroup: Standart Alanlar (entity kolonlari) + Widget Alanlar (admin)
                          var standardItems = filterableFields.filter(function (x) { return x.source !== 'widget' })
                          var widgetItems   = filterableFields.filter(function (x) { return x.source === 'widget' })
                          var groups = []
                          if (standardItems.length > 0) {
                            groups.push(
                              <optgroup key="g-std" label="Standart Alanlar">
                                {standardItems.map(function (w) {
                                  return <option key={w.id} value={w.id}>{w.label || w.id}</option>
                                })}
                              </optgroup>
                            )
                          }
                          if (widgetItems.length > 0) {
                            groups.push(
                              <optgroup key="g-w" label="Widget Alanlar (form)">
                                {widgetItems.map(function (w) {
                                  return <option key={w.id} value={w.id}>{w.label || w.id}</option>
                                })}
                              </optgroup>
                            )
                          }
                          if (groups.length === 0) {
                            return filterableFields.map(function (w) {
                              return <option key={w.id} value={w.id}>{w.label || w.id}</option>
                            })
                          }
                          return groups
                        })()}
                      </select>
                      <button
                        type="button"
                        onClick={function () { removeRow(f.id) }}
                        style={{
                          padding: 6, borderRadius: 8,
                          background: isDark ? 'rgba(239,68,68,0.1)' : '#fef2f2',
                          border: '1px solid ' + (isDark ? 'rgba(239,68,68,0.25)' : '#fecaca'),
                          color: isDark ? '#fca5a5' : '#dc2626',
                          cursor: 'pointer', flexShrink: 0,
                          display: 'flex', alignItems: 'center', justifyContent: 'center',
                        }}
                        title="Filtreyi kaldir"
                      >
                        <X size={12} />
                      </button>
                    </div>

                    <div style={{ display: 'flex', gap: 6 }}>
                      <select
                        value={f.op}
                        onChange={function (e) { updateRow(f.id, { op: e.target.value }) }}
                        style={{
                          flex: '0 0 130px', padding: '6px 8px', borderRadius: 8,
                          background: inputBg, border: '1px solid ' + inputBorder,
                          color: textPrimary, fontSize: 12, outline: 'none',
                        }}
                      >
                        {ops.map(function (o) {
                          return <option key={o.id} value={o.id}>{o.label}</option>
                        })}
                      </select>

                      {!isBool && (
                        <input
                          type={inputType}
                          value={f.value || ''}
                          onChange={function (e) { updateRow(f.id, { value: e.target.value }) }}
                          placeholder={isBetween ? 'Min' : 'Deger'}
                          style={{
                            flex: 1, padding: '6px 10px', borderRadius: 8,
                            background: inputBg, border: '1px solid ' + inputBorder,
                            color: textPrimary, fontSize: 12, outline: 'none',
                          }}
                        />
                      )}
                      {isBetween && !isBool && (
                        <input
                          type={inputType}
                          value={f.value2 || ''}
                          onChange={function (e) { updateRow(f.id, { value2: e.target.value }) }}
                          placeholder="Max"
                          style={{
                            flex: 1, padding: '6px 10px', borderRadius: 8,
                            background: inputBg, border: '1px solid ' + inputBorder,
                            color: textPrimary, fontSize: 12, outline: 'none',
                          }}
                        />
                      )}
                    </div>
                  </div>
                )
              })}
            </div>
          )}

          {filterableFields.length > 0 && (
            <button
              type="button"
              onClick={addRow}
              style={{
                marginTop: 12, width: '100%', padding: '9px 12px', borderRadius: 10,
                background: isDark ? 'rgba(99,102,241,0.12)' : '#eef2ff',
                border: '1px dashed ' + (isDark ? 'rgba(99,102,241,0.3)' : '#c7d2fe'),
                color: isDark ? '#a5b4fc' : '#4338ca',
                fontSize: 12, fontWeight: 600,
                cursor: 'pointer',
                display: 'inline-flex', alignItems: 'center', justifyContent: 'center', gap: 6,
              }}
            >
              <Plus size={13} />
              Filtre Ekle
            </button>
          )}
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
            disabled={filters.length === 0}
            style={{
              padding: '8px 14px', borderRadius: 9, fontSize: 12, fontWeight: 600,
              background: isDark ? 'rgba(255,255,255,0.04)' : '#f1f5f9',
              border: '1px solid ' + (isDark ? 'rgba(255,255,255,0.08)' : '#cbd5e1'),
              color: filters.length === 0 ? textSubtle : textPrimary,
              cursor: filters.length === 0 ? 'not-allowed' : 'pointer',
              opacity: filters.length === 0 ? 0.6 : 1,
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
  if (!Array.isArray(filters) || filters.length === 0) return true
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
      var b = raw === true || raw === 'true' || raw === 1 || raw === '1'
      if (f.op === 'isTrue') return b
      if (f.op === 'isFalse') return !b
      return true
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
