/**
 * RuleBuilderModal — No-Code Visual Rule Builder
 *
 * Props:
 *   isOpen          bool
 *   onClose()       kapat callback
 *   onSave({visibleIf, disabledIf, formula})  kaydet callback
 *   initialValues   {visibleIf: string, disabledIf: string, formula: string}
 *   availableWidgets [{widgetCode, label, dataType}]
 *
 * 3 Sekme:
 *   1. Görünürlük (visibleIf)   — Query Builder
 *   2. Aktiflik   (disabledIf)  — Query Builder
 *   3. Formül     (formula)     — Formula Editor
 *
 * String format: w_{widgetCode} {op} {value} (backend Faz G uyumlu)
 */
import { useState, useEffect, useRef } from 'react'
import { createPortal } from 'react-dom'
import { motion, AnimatePresence } from 'framer-motion'
import { X, Plus, Trash2, Eye, Lock, Calculator, Wrench, ChevronDown, Check, Palette, Sparkles, Asterisk } from 'lucide-react'

// ─── Tema dedektoru — body.app-theme-light gozlemler ─────────────────
// CalibraHub standart pattern: GuideCustomizationModal, ModuleSelector vb. ile ayni.
function useThemeIsLight() {
  const [light, setLight] = useState(() => {
    if (typeof document === 'undefined') return false
    return document.body.classList.contains('app-theme-light')
  })
  useEffect(() => {
    const obs = new MutationObserver(() => {
      setLight(document.body.classList.contains('app-theme-light'))
    })
    obs.observe(document.body, { attributes: true, attributeFilter: ['class'] })
    return () => obs.disconnect()
  }, [])
  return light
}

// ─── Tema renkleri — isLight'a gore tek noktadan urer ────────────────
// Karanlik mod orijinal degerleri korur (geriye donuk uyum); acik modda
// slate paleti + beyaz arka plan kullanir.
function themePalette(isLight) {
  return {
    // Ana metin
    text:           isLight ? '#1e293b'             : 'rgba(255,255,255,0.85)',
    textStrong:     isLight ? '#0f172a'             : 'rgba(255,255,255,0.92)',
    textMute:       isLight ? '#475569'             : 'rgba(255,255,255,0.65)',
    textMuted:      isLight ? '#64748b'             : 'rgba(255,255,255,0.55)',
    textFaint:      isLight ? '#94a3b8'             : 'rgba(255,255,255,0.4)',
    textPlaceholder:isLight ? '#94a3b8'             : 'rgba(255,255,255,0.3)',
    textGhost:      isLight ? '#cbd5e1'             : 'rgba(255,255,255,0.25)',
    // Yuzeyler
    panel:          isLight ? '#ffffff'             : 'rgba(7,10,20,0.96)',
    surface:        isLight ? '#f8fafc'             : 'rgba(255,255,255,0.05)',
    surfaceSoft:    isLight ? '#f1f5f9'             : 'rgba(255,255,255,0.03)',
    surfaceFaint:   isLight ? '#fafbfc'             : 'rgba(255,255,255,0.015)',
    sidebar:        isLight ? '#f8fafc'             : 'rgba(255,255,255,0.015)',
    inputBg:        isLight ? '#ffffff'             : 'rgba(255,255,255,0.05)',
    dropdownBg:     isLight ? '#ffffff'             : 'rgba(10,14,26,0.97)',
    previewBg:      isLight ? '#f1f5f9'             : 'rgba(0,0,0,0.2)',
    // Kenarlar
    border:         isLight ? '#e2e8f0'             : 'rgba(255,255,255,0.1)',
    borderSoft:     isLight ? '#e2e8f0'             : 'rgba(255,255,255,0.07)',
    borderFaint:    isLight ? '#f1f5f9'             : 'rgba(255,255,255,0.04)',
    divider:        isLight ? '#e2e8f0'             : 'rgba(255,255,255,0.06)',
    // Backdrop
    backdrop:       isLight ? 'rgba(15,23,42,0.45)' : 'rgba(0,0,0,0.72)',
    shadow:         isLight ? '0 24px 80px rgba(15,23,42,0.18)' : '0 24px 80px rgba(0,0,0,0.65)',
    dropdownShadow: isLight ? '0 12px 40px rgba(15,23,42,0.18)' : '0 12px 40px rgba(0,0,0,0.6)',
    // Vurgu (amber sabit kalir — marka rengi)
    accentBg:       isLight ? 'rgba(245,158,11,0.10)' : 'rgba(245,158,11,0.12)',
    accentBgSoft:   isLight ? 'rgba(245,158,11,0.08)' : 'rgba(245,158,11,0.08)',
    accentBorder:   isLight ? 'rgba(245,158,11,0.35)' : 'rgba(245,158,11,0.28)',
    accentBorderSoft:isLight? 'rgba(245,158,11,0.30)' : 'rgba(245,158,11,0.25)',
    accentText:     isLight ? '#b45309'             : '#fbbf24',
    // select dropdown'lar icin color-scheme
    colorScheme:    isLight ? 'light'               : 'dark',
  }
}

// ─── Operatör listesi ────────────────────────────────────────────────
var OPERATORS = [
  { value: '==',         label: '= Eşittir' },
  { value: '!=',         label: '≠ Eşit Değil' },
  { value: '>',          label: '> Büyük' },
  { value: '>=',         label: '≥ Büyük Eşit' },
  { value: '<',          label: '< Küçük' },
  { value: '<=',         label: '≤ Küçük Eşit' },
  { value: 'contains',   label: '∋ İçerir' },
  { value: 'startsWith', label: '⊏ Başlar' },
]

// Numeric data types — value string'e tırnak koymayız
var NUMERIC_TYPES = ['numeric', 'integer', 'decimal', 'number', 'float']

function isNumericType(dataType) {
  return NUMERIC_TYPES.indexOf((dataType || '').toLowerCase()) !== -1
}

function isBooleanType(dataType) {
  return String(dataType || '').toLowerCase() === 'boolean'
}

// Boolean degerini "true" | "false" string'ine normalize et — koşul value alanı
// her zaman string saklar; runtime tarafa tirnaksiz literal cikis veriyoruz.
function normalizeBoolValue(v) {
  if (v === true || v === 1) return 'true'
  if (v === false || v === 0) return 'false'
  var s = String(v == null ? '' : v).trim().toLowerCase()
  if (s === 'true' || s === '1' || s === 'evet' || s === 'yes' || s === 'on') return 'true'
  return 'false'
}

// ─── Koşul dizisini string'e çevir ──────────────────────────────────
function conditionsToString(conditions, junction) {
  var parts = []
  for (var i = 0; i < conditions.length; i++) {
    var c = conditions[i]
    var field = c.field ? ('w_' + c.field) : ''
    var op    = c.operator || '=='
    var val   = c.value !== undefined ? String(c.value) : ''
    if (!field || val === '') continue

    var valStr
    if (op === 'contains' || op === 'startsWith') {
      // string fonksiyon formatı: w_field.includes('x') / w_field.startsWith('x')
      var fn = op === 'contains' ? 'includes' : 'startsWith'
      valStr = field + "." + fn + "('" + val.replace(/'/g, "\\'") + "')"
      parts.push(valStr)
    } else if (isBooleanType(c.dataType)) {
      // Boolean: tirnaksiz literal — scope'taki widget degeri gercek bool, expr-eval
      // strict tip kontrolu yaptigi icin 'true'/'false' string ile karsilastirma daima false doner.
      valStr = field + ' ' + op + ' ' + normalizeBoolValue(val)
      parts.push(valStr)
    } else {
      // numeric ise tırnak yok, string ise tırnak var
      var isNum = c.dataType ? isNumericType(c.dataType) : !isNaN(parseFloat(val))
      if (isNum) {
        valStr = field + ' ' + op + ' ' + val
      } else {
        valStr = field + ' ' + op + " '" + val.replace(/'/g, "\\'") + "'"
      }
      parts.push(valStr)
    }
  }
  var sep = junction === 'OR' ? ' || ' : ' && '
  return parts.join(sep)
}

// ─── String'i koşul dizisine parse et (basit, hata toleranslı) ──────
function parseConditionsFromString(str, availableWidgets) {
  if (!str || !str.trim()) return { conditions: [], junction: 'AND' }

  var junction = 'AND'
  // OR mu AND mi?
  if (str.indexOf(' || ') !== -1) junction = 'OR'

  var sep = junction === 'OR' ? ' || ' : ' && '
  var parts = str.split(sep)
  var conditions = []

  for (var i = 0; i < parts.length; i++) {
    var part = parts[i].trim()
    if (!part) continue

    // contains / startsWith pattern: w_field.includes('val') veya w_field.startsWith('val')
    var fnMatch = part.match(/^w_([a-z0-9_]+)\.(includes|startsWith)\(['"](.*)['"][\)]$/)
    if (fnMatch) {
      var wgt = availableWidgets.find(function(w) { return w.widgetCode === fnMatch[1] })
      conditions.push({
        id: Date.now() + '_' + i,
        field: fnMatch[1],
        operator: fnMatch[2] === 'includes' ? 'contains' : 'startsWith',
        value: fnMatch[3],
        dataType: wgt ? wgt.dataType : 'text',
      })
      continue
    }

    // normal: w_field op value
    var normMatch = part.match(/^w_([a-z0-9_]+)\s*(==|!=|>=|<=|>|<)\s*(.+)$/)
    if (normMatch) {
      var fieldCode = normMatch[1]
      var op = normMatch[2]
      var rawVal = normMatch[3].trim()
      // tırnak varsa kaldır
      var cleanVal = rawVal.replace(/^['"]|['"]$/g, '')
      var wgt2 = availableWidgets.find(function(w) { return w.widgetCode === fieldCode })
      conditions.push({
        id: Date.now() + '_' + i,
        field: fieldCode,
        operator: op,
        value: cleanVal,
        dataType: wgt2 ? wgt2.dataType : 'text',
      })
      continue
    }
  }

  return { conditions: conditions, junction: junction }
}

// ─── Boş koşul oluştur ───────────────────────────────────────────────
function emptyCondition() {
  return {
    id: String(Date.now()) + '_' + String(Math.random()).slice(2, 7),
    field: '',
    operator: '==',
    value: '',
    dataType: 'text',
  }
}

// ─── Aritmetik operatorler (formul icin) ─────────────────────────────
var ARITH_OPS = [
  { value: '+', label: '+', hint: 'Topla' },
  { value: '-', label: '−', hint: 'Çıkar' },
  { value: '*', label: '×', hint: 'Çarp'  },
  { value: '/', label: '÷', hint: 'Böl'   },
]

// ─── Formul segmenti olustur ─────────────────────────────────────────
function newSegmentId() {
  return String(Date.now()) + '_' + String(Math.random()).slice(2, 7)
}

function operandToSegment(token, op) {
  if (/^w_/.test(token)) {
    return { id: newSegmentId(), op: op, kind: 'field',   field: token.slice(2), value: '' }
  }
  return   { id: newSegmentId(), op: op, kind: 'literal', field: '',             value: token }
}

function emptyFormulaSegment(op) {
  return { id: newSegmentId(), op: op == null ? null : op, kind: 'field', field: '', value: '' }
}

// ─── Formul string → segment dizisi (basit aritmetik icin) ───────────
// Desteklenen: w_field * w_field, sayi, +, -, *, /
// Desteklenmeyen (fallback textarea): (, ), %, !, ==, !=, >, <, &&, ||, ?:, fn(..)
function parseFormulaSegments(str) {
  if (!str || !str.trim()) return { segments: [], complex: false }
  var s = str.trim()
  if (/[()%!=<>?&|'"`]/.test(s)) return { segments: [], complex: true, raw: str }
  if (/[a-zA-Z_][a-zA-Z0-9_]*\s*\(/.test(s)) return { segments: [], complex: true, raw: str }

  var operandRe = /^(w_[a-zA-Z0-9_]+|-?\d+(?:\.\d+)?)/
  var stepRe    = /^([+\-*/])\s*(w_[a-zA-Z0-9_]+|-?\d+(?:\.\d+)?)/

  var m = s.match(operandRe)
  if (!m) return { segments: [], complex: true, raw: str }
  var segments = [operandToSegment(m[1], null)]
  s = s.slice(m[0].length).replace(/^\s+/, '')
  while (s.length > 0) {
    var m2 = s.match(stepRe)
    if (!m2) return { segments: [], complex: true, raw: str }
    segments.push(operandToSegment(m2[2], m2[1]))
    s = s.slice(m2[0].length).replace(/^\s+/, '')
  }
  return { segments: segments, complex: false }
}

// ─── Segment dizisi → formul string ──────────────────────────────────
function formulaSegmentsToString(segments) {
  var parts = []
  for (var i = 0; i < segments.length; i++) {
    var s = segments[i]
    var operand
    if (s.kind === 'field') {
      if (!s.field) continue
      operand = 'w_' + s.field
    } else {
      if (s.value === '' || s.value == null) continue
      operand = String(s.value)
    }
    if (parts.length === 0 || s.op == null) {
      parts.push(operand)
    } else {
      parts.push(s.op)
      parts.push(operand)
    }
  }
  return parts.join(' ')
}

// ─── Alan Dropdown ────────────────────────────────────────────────────
// Combobox'ta dogrudan etiket adlari listelenir (ornegin "Urun Adi").
// widgetCode (w_urun_adi) alt metin olarak kucuk gosterilir; kayit hala
// widgetCode uzerinden yapilir (backend formul/kosul formati degismez).
function FieldDropdown(props) {
  var value = props.value || ''
  var onChange = props.onChange
  var widgets = props.widgets || []
  var isLight = !!props.isLight
  var t = themePalette(isLight)
  var [open, setOpen] = useState(false)
  var ref = useRef(null)

  useEffect(function() {
    if (!open) return undefined
    function handle(e) {
      if (ref.current && !ref.current.contains(e.target)) setOpen(false)
    }
    document.addEventListener('mousedown', handle)
    return function() { document.removeEventListener('mousedown', handle) }
  }, [open])

  var selected = widgets.find(function(w) { return w.widgetCode === value })

  return (
    <div ref={ref} style={{ position: 'relative', minWidth: 0, flex: 1 }}>
      <button
        type="button"
        onClick={function() { setOpen(function(v) { return !v }) }}
        style={{
          width: '100%',
          height: '34px',
          padding: '0 10px',
          background: t.inputBg,
          border: '1px solid ' + t.border,
          borderRadius: '8px',
          color: selected ? t.textStrong : t.textPlaceholder,
          fontSize: '12px',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          gap: '6px',
          cursor: 'pointer',
          transition: 'border-color 0.15s',
        }}
      >
        <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', display: 'flex', alignItems: 'center', gap: '8px', minWidth: 0 }}>
          {selected ? (
            <>
              <span style={{ fontWeight: 600, overflow: 'hidden', textOverflow: 'ellipsis' }}>
                {selected.label || selected.widgetCode}
              </span>
              {selected._sourceFormLabel && (
                <span
                  style={{
                    fontSize: '9px',
                    padding: '1px 5px',
                    borderRadius: '4px',
                    background: 'rgba(99,102,241,0.15)',
                    border: '1px solid rgba(99,102,241,0.3)',
                    color: isLight ? '#4f46e5' : '#a5b4fc',
                    fontWeight: 600,
                    flexShrink: 0,
                  }}
                >
                  {selected._sourceFormLabel}
                </span>
              )}
              <span style={{ fontFamily: 'monospace', fontSize: '10px', opacity: 0.4, flexShrink: 0 }}>
                w_{selected.widgetCode}
              </span>
            </>
          ) : 'Alan seç...'}
        </span>
        <ChevronDown size={11} style={{ flexShrink: 0, opacity: 0.5 }} />
      </button>
      {open && (
        <div style={{
          position: 'absolute',
          top: '38px',
          left: 0,
          right: 0,
          zIndex: 9999,
          background: t.dropdownBg,
          border: '1px solid ' + t.border,
          borderRadius: '10px',
          boxShadow: t.dropdownShadow,
          overflow: 'hidden',
          maxHeight: '240px',
          overflowY: 'auto',
        }}>
          {widgets.length === 0 && (
            <div style={{ padding: '10px 12px', fontSize: '11px', color: t.textPlaceholder }}>
              Widget bulunamadı
            </div>
          )}
          {widgets.map(function(w) {
            var isSel = value === w.widgetCode
            return (
              <button
                key={(w._sourceFormCode || '_own') + '::' + w.widgetCode}
                type="button"
                onClick={function() { onChange(w.widgetCode, w.dataType); setOpen(false) }}
                style={{
                  width: '100%',
                  padding: '8px 12px',
                  background: isSel ? t.accentBg : 'transparent',
                  border: 'none',
                  color: isSel ? t.accentText : t.text,
                  fontSize: '12px',
                  textAlign: 'left',
                  cursor: 'pointer',
                  display: 'flex',
                  alignItems: 'center',
                  gap: '8px',
                }}
              >
                {isSel && <Check size={10} style={{ flexShrink: 0 }} />}
                <span style={{ flex: 1, minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', fontWeight: 600 }}>
                  {w.label || w.widgetCode}
                </span>
                {w._sourceFormLabel && (
                  <span
                    title={'Üst form: ' + w._sourceFormLabel}
                    style={{
                      fontSize: '9px',
                      padding: '2px 6px',
                      borderRadius: '4px',
                      background: 'rgba(99,102,241,0.15)',
                      border: '1px solid rgba(99,102,241,0.3)',
                      color: isLight ? '#4f46e5' : '#a5b4fc',
                      fontWeight: 600,
                      flexShrink: 0,
                    }}
                  >
                    {w._sourceFormLabel}
                  </span>
                )}
                <span style={{ fontSize: '10px', opacity: 0.45, fontFamily: 'monospace', flexShrink: 0 }}>
                  w_{w.widgetCode}
                </span>
              </button>
            )
          })}
        </div>
      )}
    </div>
  )
}

// ─── Operatör Dropdown ───────────────────────────────────────────────
function OperatorDropdown(props) {
  var value = props.value || '=='
  var onChange = props.onChange
  var isLight = !!props.isLight
  var t = themePalette(isLight)
  var [open, setOpen] = useState(false)
  var ref = useRef(null)

  useEffect(function() {
    if (!open) return undefined
    function handle(e) {
      if (ref.current && !ref.current.contains(e.target)) setOpen(false)
    }
    document.addEventListener('mousedown', handle)
    return function() { document.removeEventListener('mousedown', handle) }
  }, [open])

  var selected = OPERATORS.find(function(o) { return o.value === value })

  return (
    <div ref={ref} style={{ position: 'relative', width: '130px', flexShrink: 0 }}>
      <button
        type="button"
        onClick={function() { setOpen(function(v) { return !v }) }}
        style={{
          width: '100%',
          height: '34px',
          padding: '0 10px',
          background: t.inputBg,
          border: '1px solid ' + t.border,
          borderRadius: '8px',
          color: t.text,
          fontSize: '11px',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          gap: '4px',
          cursor: 'pointer',
        }}
      >
        <span>{selected ? selected.label : value}</span>
        <ChevronDown size={10} style={{ opacity: 0.5, flexShrink: 0 }} />
      </button>
      {open && (
        <div style={{
          position: 'absolute',
          top: '38px',
          left: 0,
          zIndex: 9999,
          background: t.dropdownBg,
          border: '1px solid ' + t.border,
          borderRadius: '10px',
          boxShadow: t.dropdownShadow,
          overflow: 'hidden',
          minWidth: '140px',
        }}>
          {OPERATORS.map(function(op) {
            return (
              <button
                key={op.value}
                type="button"
                onClick={function() { onChange(op.value); setOpen(false) }}
                style={{
                  width: '100%',
                  padding: '7px 12px',
                  background: value === op.value ? t.accentBg : 'transparent',
                  border: 'none',
                  color: value === op.value ? t.accentText : t.textMute,
                  fontSize: '11px',
                  textAlign: 'left',
                  cursor: 'pointer',
                  display: 'flex',
                  alignItems: 'center',
                  gap: '6px',
                }}
              >
                {value === op.value && <Check size={10} />}
                {op.label}
              </button>
            )
          })}
        </div>
      )}
    </div>
  )
}

// ─── Aritmetik Operator Dropdown (formul icin, +,-,*,/) ──────────────
function ArithmeticOperatorDropdown(props) {
  var value = props.value || '+'
  var onChange = props.onChange
  var isLight = !!props.isLight
  var t = themePalette(isLight)
  var [open, setOpen] = useState(false)
  var ref = useRef(null)

  useEffect(function() {
    if (!open) return undefined
    function handle(e) {
      if (ref.current && !ref.current.contains(e.target)) setOpen(false)
    }
    document.addEventListener('mousedown', handle)
    return function() { document.removeEventListener('mousedown', handle) }
  }, [open])

  var selected = ARITH_OPS.find(function(o) { return o.value === value }) || ARITH_OPS[0]

  return (
    <div ref={ref} style={{ position: 'relative', width: '52px', flexShrink: 0 }}>
      <button
        type="button"
        onClick={function() { setOpen(function(v) { return !v }) }}
        title={selected.hint}
        style={{
          width: '100%',
          height: '34px',
          background: t.accentBgSoft,
          border: '1px solid ' + t.accentBorderSoft,
          borderRadius: '8px',
          color: t.accentText,
          fontSize: '16px',
          fontWeight: 700,
          cursor: 'pointer',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
        }}
      >
        {selected.label}
      </button>
      {open && (
        <div style={{
          position: 'absolute',
          top: '38px',
          left: 0,
          zIndex: 9999,
          background: t.dropdownBg,
          border: '1px solid ' + t.border,
          borderRadius: '10px',
          boxShadow: t.dropdownShadow,
          overflow: 'hidden',
          minWidth: '130px',
        }}>
          {ARITH_OPS.map(function(op) {
            var act = value === op.value
            return (
              <button
                key={op.value}
                type="button"
                onClick={function() { onChange(op.value); setOpen(false) }}
                style={{
                  width: '100%',
                  padding: '7px 12px',
                  background: act ? t.accentBg : 'transparent',
                  border: 'none',
                  color: act ? t.accentText : t.textMute,
                  fontSize: '12px',
                  textAlign: 'left',
                  cursor: 'pointer',
                  display: 'flex',
                  alignItems: 'center',
                  gap: '10px',
                }}
              >
                <span style={{ width: 16, textAlign: 'center', fontWeight: 700, fontSize: 14 }}>{op.label}</span>
                <span style={{ fontSize: 11, opacity: 0.7 }}>{op.hint}</span>
              </button>
            )
          })}
        </div>
      )}
    </div>
  )
}

// ─── Query Builder (visibleIf / disabledIf sekmeleri için) ───────────
// Value editor — kondisyonu uretern kaynak alanin veri tipine gore farklilasir.
// boolean → Evet/Hayır toggle; numeric → number input; date → date input;
// dropdown/multi-select/lookup → kaynak alanin options listesinden select;
// text/diger → text input.
function renderValueInput(cond, updateCondition, availableWidgets, isLight) {
  var t = themePalette(!!isLight)
  var dt = String(cond.dataType || '').toLowerCase()
  var optionBg = isLight ? '#ffffff' : '#0a0e1a'
  var optionFg = isLight ? '#1e293b' : '#fff'
  var commonStyle = {
    flex: 1,
    height: '34px',
    padding: '0 10px',
    background: t.inputBg,
    border: '1px solid ' + t.border,
    borderRadius: '8px',
    color: t.text,
    fontSize: '12px',
    outline: 'none',
    minWidth: '80px',
    colorScheme: t.colorScheme,
  }

  // Boolean — Evet/Hayır iki butonlu toggle (tirnaksiz literal cikis)
  if (isBooleanType(dt)) {
    return (
      <div style={{
        flex: 1, display: 'flex', gap: '4px',
        padding: '3px',
        background: t.inputBg,
        border: '1px solid ' + t.border,
        borderRadius: '8px',
        minWidth: '120px',
      }}>
        {[
          { v: 'true',  label: 'Evet',  onColor: '#10b981' },
          { v: 'false', label: 'Hayır', onColor: '#ef4444' },
        ].map(function(opt) {
          var act = cond.value !== '' && normalizeBoolValue(cond.value) === opt.v
          return (
            <button
              key={opt.v}
              type="button"
              onClick={function() { updateCondition(cond.id, { value: opt.v }) }}
              style={{
                flex: 1, height: '26px', padding: '0 10px',
                background: act ? opt.onColor : 'transparent',
                border: 'none', borderRadius: '6px',
                color: act ? '#fff' : t.textMute,
                fontSize: '11px', fontWeight: '600',
                cursor: 'pointer', transition: 'background 0.15s',
              }}
            >
              {opt.label}
            </button>
          )
        })}
      </div>
    )
  }

  // Dropdown / multi-select / lookup — kaynak widget'in options listesi varsa select
  var src = cond.field
    ? (availableWidgets || []).find(function(w) { return w.widgetCode === cond.field })
    : null
  var hasOptions = src && Array.isArray(src.options) && src.options.length > 0
  if ((dt === 'dropdown' || dt === 'multi-select' || dt === 'lookup') && hasOptions) {
    return (
      <select
        value={cond.value}
        onChange={function(e) { updateCondition(cond.id, { value: e.target.value }) }}
        style={commonStyle}
      >
        <option value="" style={{ background: optionBg, color: optionFg }}>— Seç —</option>
        {src.options.map(function(o) {
          var ov = typeof o === 'string' ? o : (o && (o.value != null ? o.value : o.label)) || ''
          var ol = typeof o === 'string' ? o : (o && (o.label != null ? o.label : o.value)) || ''
          return (
            <option key={String(ov)} value={String(ov)} style={{ background: optionBg, color: optionFg }}>
              {String(ol)}
            </option>
          )
        })}
      </select>
    )
  }

  // Numeric — number input
  if (isNumericType(dt)) {
    return (
      <input
        type="number"
        value={cond.value}
        onChange={function(e) { updateCondition(cond.id, { value: e.target.value }) }}
        placeholder="0"
        style={commonStyle}
      />
    )
  }

  // Date / datetime — date input
  if (dt === 'date' || dt === 'datetime') {
    return (
      <input
        type={dt === 'datetime' ? 'datetime-local' : 'date'}
        value={cond.value}
        onChange={function(e) { updateCondition(cond.id, { value: e.target.value }) }}
        style={commonStyle}
      />
    )
  }

  // Text / lookup (options yoksa) / diger — text input
  return (
    <input
      type="text"
      value={cond.value}
      onChange={function(e) { updateCondition(cond.id, { value: e.target.value }) }}
      placeholder="değer..."
      style={commonStyle}
    />
  )
}

function QueryBuilder(props) {
  var conditions = props.conditions
  var junction = props.junction
  var onConditionsChange = props.onConditionsChange
  var onJunctionChange = props.onJunctionChange
  var availableWidgets = props.availableWidgets || []
  var preview = props.preview || ''
  var isLight = !!props.isLight
  var t = themePalette(isLight)

  function addCondition() {
    onConditionsChange(conditions.concat([emptyCondition()]))
  }

  function removeCondition(id) {
    onConditionsChange(conditions.filter(function(c) { return c.id !== id }))
  }

  function updateCondition(id, patch) {
    onConditionsChange(conditions.map(function(c) {
      if (c.id !== id) return c
      return Object.assign({}, c, patch)
    }))
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
      {/* Koşul Satırları */}
      {conditions.length === 0 && (
        <div style={{
          padding: '24px 16px',
          textAlign: 'center',
          color: t.textGhost,
          fontSize: '12px',
          border: '1px dashed ' + t.border,
          borderRadius: '10px',
        }}>
          Henüz koşul tanımlanmadı. "Koşul Ekle" ile başlayın.
        </div>
      )}

      {conditions.map(function(cond, idx) {
        return (
          <div key={cond.id} style={{ display: 'flex', flexDirection: 'column', gap: '6px' }}>
            {/* AND / OR separator (2. satırdan itibaren) */}
            {idx > 0 && (
              <div style={{ display: 'flex', alignItems: 'center', gap: '8px', padding: '4px 0' }}>
                <div style={{ flex: 1, height: '1px', background: t.divider }} />
                <span style={{
                  fontSize: '10px',
                  fontWeight: '700',
                  color: junction === 'OR' ? '#fb923c' : '#818cf8',
                  textTransform: 'uppercase',
                  letterSpacing: '0.08em',
                }}>
                  {junction}
                </span>
                <div style={{ flex: 1, height: '1px', background: t.divider }} />
              </div>
            )}

            {/* Koşul Satırı */}
            <div style={{
              display: 'flex',
              alignItems: 'center',
              gap: '8px',
              padding: '10px 12px',
              background: t.surfaceSoft,
              border: '1px solid ' + t.borderSoft,
              borderRadius: '10px',
            }}>
              <FieldDropdown
                value={cond.field}
                widgets={availableWidgets}
                isLight={isLight}
                onChange={function(code, dtype) {
                  updateCondition(cond.id, { field: code, dataType: dtype || 'text' })
                }}
              />
              <OperatorDropdown
                value={cond.operator}
                isLight={isLight}
                onChange={function(op) {
                  updateCondition(cond.id, { operator: op })
                }}
              />
              {renderValueInput(cond, updateCondition, availableWidgets, isLight)}
              <button
                type="button"
                onClick={function() { removeCondition(cond.id) }}
                title="Koşulu sil"
                style={{
                  width: '34px',
                  height: '34px',
                  background: 'rgba(239,68,68,0.08)',
                  border: '1px solid rgba(239,68,68,0.2)',
                  borderRadius: '8px',
                  color: isLight ? '#dc2626' : 'rgba(239,68,68,0.7)',
                  cursor: 'pointer',
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  flexShrink: 0,
                  transition: 'background 0.15s',
                }}
              >
                <Trash2 size={13} strokeWidth={2} />
              </button>
            </div>
          </div>
        )
      })}

      {/* Ekle + Junction */}
      <div style={{ display: 'flex', alignItems: 'center', gap: '10px', flexWrap: 'wrap' }}>
        <button
          type="button"
          onClick={addCondition}
          style={{
            display: 'flex',
            alignItems: 'center',
            gap: '6px',
            padding: '6px 14px',
            background: t.accentBg,
            border: '1px solid ' + t.accentBorderSoft,
            borderRadius: '8px',
            color: t.accentText,
            fontSize: '12px',
            fontWeight: '600',
            cursor: 'pointer',
            transition: 'background 0.15s',
          }}
        >
          <Plus size={13} strokeWidth={2.5} />
          Koşul Ekle
        </button>

        {conditions.length >= 2 && (
          <div style={{
            display: 'flex',
            alignItems: 'center',
            gap: '6px',
            padding: '4px 6px',
            background: t.surface,
            border: '1px solid ' + t.border,
            borderRadius: '8px',
            fontSize: '11px',
            color: t.textMute,
          }}>
            <span>Koşullar arası:</span>
            <label style={{ display: 'flex', alignItems: 'center', gap: '4px', cursor: 'pointer' }}>
              <input
                type="radio"
                name={'junction_' + props.tabKey}
                value="AND"
                checked={junction === 'AND'}
                onChange={function() { onJunctionChange('AND') }}
                style={{ accentColor: '#818cf8' }}
              />
              <span style={{ color: junction === 'AND' ? '#818cf8' : t.textMuted, fontWeight: '600' }}>AND</span>
            </label>
            <label style={{ display: 'flex', alignItems: 'center', gap: '4px', cursor: 'pointer' }}>
              <input
                type="radio"
                name={'junction_' + props.tabKey}
                value="OR"
                checked={junction === 'OR'}
                onChange={function() { onJunctionChange('OR') }}
                style={{ accentColor: '#fb923c' }}
              />
              <span style={{ color: junction === 'OR' ? '#fb923c' : t.textMuted, fontWeight: '600' }}>OR</span>
            </label>
          </div>
        )}

        {conditions.length > 0 && (
          <button
            type="button"
            onClick={function() { onConditionsChange([]) }}
            style={{
              padding: '6px 12px',
              background: 'transparent',
              border: '1px solid ' + t.border,
              borderRadius: '8px',
              color: t.textFaint,
              fontSize: '11px',
              cursor: 'pointer',
              transition: 'color 0.15s',
            }}
          >
            Temizle
          </button>
        )}
      </div>

      {/* Önizleme */}
      <div style={{
        padding: '10px 12px',
        background: t.previewBg,
        border: '1px solid ' + t.borderSoft,
        borderRadius: '8px',
      }}>
        <div style={{ fontSize: '10px', fontWeight: '700', textTransform: 'uppercase', letterSpacing: '0.07em', color: t.textPlaceholder, marginBottom: '6px' }}>
          Önizleme
        </div>
        <div style={{
          fontFamily: 'monospace',
          fontSize: '12px',
          color: preview ? t.accentText : t.textGhost,
          wordBreak: 'break-all',
          lineHeight: '1.5',
        }}>
          {preview || '(boş — koşul yok)'}
        </div>
      </div>
    </div>
  )
}

// ─── Formul Builder (satir bazli: Alan/Sayi + operator kombo) ─────────
// Gorunurluk/Aktiflik paneli ile ayni kombo mantigi — her satir bir
// operand (Alan secimi veya Sayi literal) + (ilk satir haric) operator.
// Karmasik formuller (parantez, fonksiyon, mantik) textarea fallback ile.
function FormulaBuilder(props) {
  var segments = props.segments || []
  var onSegmentsChange = props.onSegmentsChange
  var availableWidgets = props.availableWidgets || []
  var preview = props.preview || ''
  var rawFallback = props.rawFallback
  var onRawFallbackChange = props.onRawFallbackChange
  var onClearFallback = props.onClearFallback
  var isLight = !!props.isLight
  var t = themePalette(isLight)

  // Fallback (karmasik formul): metin alani
  if (rawFallback != null) {
    return (
      <div style={{ display: 'flex', flexDirection: 'column', gap: '10px' }}>
        <div style={{
          padding: '10px 12px',
          background: 'rgba(239,68,68,0.08)',
          border: '1px solid rgba(239,68,68,0.22)',
          borderRadius: '8px',
          color: isLight ? '#b91c1c' : '#fca5a5',
          fontSize: '11px',
          lineHeight: 1.5,
        }}>
          Karmaşık formül (parantez veya işlev içeriyor) — satır bazlı sihirbazla düzenlenemiyor. Metin alanında devam edebilirsiniz.
        </div>
        <textarea
          value={rawFallback}
          onChange={function(e) { onRawFallbackChange(e.target.value) }}
          placeholder="w_price * w_qty * 1.18"
          rows={3}
          style={{
            width: '100%',
            padding: '10px 12px',
            background: t.inputBg,
            border: '1px solid ' + t.border,
            borderRadius: '8px',
            color: t.text,
            fontSize: '13px',
            fontFamily: 'monospace',
            outline: 'none',
            resize: 'vertical',
            boxSizing: 'border-box',
            lineHeight: '1.5',
          }}
        />
        <div>
          <button
            type="button"
            onClick={onClearFallback}
            style={{
              padding: '6px 12px',
              background: t.accentBgSoft,
              border: '1px solid ' + t.accentBorderSoft,
              borderRadius: '8px',
              color: t.accentText,
              fontSize: '11px',
              fontWeight: 600,
              cursor: 'pointer',
            }}
          >
            Temizle ve sihirbazdan başla
          </button>
        </div>
      </div>
    )
  }

  function addSegment() {
    var op = segments.length === 0 ? null : '*'
    onSegmentsChange(segments.concat([emptyFormulaSegment(op)]))
  }

  function removeSegment(id) {
    var filtered = segments.filter(function(s) { return s.id !== id })
    // Ilk segment silindiyse yeni ilkin operatorunu null yap (sozdizimi bozulmasin).
    if (filtered.length > 0 && filtered[0].op != null) {
      filtered = filtered.slice()
      filtered[0] = Object.assign({}, filtered[0], { op: null })
    }
    onSegmentsChange(filtered)
  }

  function updateSegment(id, patch) {
    onSegmentsChange(segments.map(function(s) {
      if (s.id !== id) return s
      return Object.assign({}, s, patch)
    }))
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '10px' }}>
      {segments.length === 0 && (
        <div style={{
          padding: '24px 16px',
          textAlign: 'center',
          color: t.textGhost,
          fontSize: '12px',
          border: '1px dashed ' + t.border,
          borderRadius: '10px',
        }}>
          Henüz alan eklenmedi. "Alan Ekle" ile başlayın.
        </div>
      )}

      {segments.map(function(seg, idx) {
        return (
          <div key={seg.id} style={{
            display: 'flex',
            alignItems: 'center',
            gap: '8px',
            padding: '10px 12px',
            background: t.surfaceSoft,
            border: '1px solid ' + t.borderSoft,
            borderRadius: '10px',
          }}>
            {/* Operator (ilk satir: placeholder, sonrasi: dropdown) */}
            {idx === 0 ? (
              <span style={{
                width: '52px',
                height: '34px',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                fontSize: '10px',
                color: t.textPlaceholder,
                fontWeight: 700,
                textTransform: 'uppercase',
                letterSpacing: '0.07em',
                flexShrink: 0,
              }}>
                İlk
              </span>
            ) : (
              <ArithmeticOperatorDropdown
                value={seg.op || '+'}
                isLight={isLight}
                onChange={function(op) { updateSegment(seg.id, { op: op }) }}
              />
            )}

            {/* Alan / Sayi secici */}
            <div style={{
              display: 'flex',
              gap: '2px',
              padding: '2px',
              background: t.surface,
              border: '1px solid ' + t.border,
              borderRadius: '7px',
              flexShrink: 0,
            }}>
              {[{ k: 'field', label: 'Alan' }, { k: 'literal', label: 'Sayı' }].map(function(tab) {
                var act = seg.kind === tab.k
                return (
                  <button
                    key={tab.k}
                    type="button"
                    onClick={function() { updateSegment(seg.id, { kind: tab.k, field: '', value: '' }) }}
                    style={{
                      height: '28px',
                      padding: '0 10px',
                      background: act ? (isLight ? 'rgba(245,158,11,0.15)' : 'rgba(245,158,11,0.15)') : 'transparent',
                      border: 'none',
                      borderRadius: '5px',
                      color: act ? t.accentText : t.textMuted,
                      fontSize: '10px',
                      fontWeight: 600,
                      textTransform: 'uppercase',
                      letterSpacing: '0.04em',
                      cursor: 'pointer',
                    }}
                  >
                    {tab.label}
                  </button>
                )
              })}
            </div>

            {/* Operand */}
            {seg.kind === 'field' ? (
              <FieldDropdown
                value={seg.field}
                widgets={availableWidgets}
                isLight={isLight}
                onChange={function(code) { updateSegment(seg.id, { field: code }) }}
              />
            ) : (
              <input
                type="number"
                step="any"
                value={seg.value}
                onChange={function(e) { updateSegment(seg.id, { value: e.target.value }) }}
                placeholder="örn. 1.18"
                style={{
                  flex: 1,
                  height: '34px',
                  padding: '0 10px',
                  background: t.inputBg,
                  border: '1px solid ' + t.border,
                  borderRadius: '8px',
                  color: t.text,
                  fontSize: '12px',
                  outline: 'none',
                  minWidth: '80px',
                  colorScheme: t.colorScheme,
                }}
              />
            )}

            {/* Sil */}
            <button
              type="button"
              onClick={function() { removeSegment(seg.id) }}
              title="Alanı sil"
              style={{
                width: '34px',
                height: '34px',
                background: 'rgba(239,68,68,0.08)',
                border: '1px solid rgba(239,68,68,0.2)',
                borderRadius: '8px',
                color: isLight ? '#dc2626' : 'rgba(239,68,68,0.7)',
                cursor: 'pointer',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                flexShrink: 0,
              }}
            >
              <Trash2 size={13} strokeWidth={2} />
            </button>
          </div>
        )
      })}

      {/* Alan Ekle + Temizle */}
      <div style={{ display: 'flex', alignItems: 'center', gap: '10px', flexWrap: 'wrap' }}>
        <button
          type="button"
          onClick={addSegment}
          style={{
            display: 'flex',
            alignItems: 'center',
            gap: '6px',
            padding: '6px 14px',
            background: t.accentBg,
            border: '1px solid ' + t.accentBorderSoft,
            borderRadius: '8px',
            color: t.accentText,
            fontSize: '12px',
            fontWeight: 600,
            cursor: 'pointer',
          }}
        >
          <Plus size={13} strokeWidth={2.5} />
          Alan Ekle
        </button>
        {segments.length > 0 && (
          <button
            type="button"
            onClick={function() { onSegmentsChange([]) }}
            style={{
              padding: '6px 12px',
              background: 'transparent',
              border: '1px solid ' + t.border,
              borderRadius: '8px',
              color: t.textFaint,
              fontSize: '11px',
              cursor: 'pointer',
            }}
          >
            Temizle
          </button>
        )}
      </div>

      {/* Onizleme */}
      <div style={{
        padding: '10px 12px',
        background: t.previewBg,
        border: '1px solid ' + t.borderSoft,
        borderRadius: '8px',
      }}>
        <div style={{ fontSize: '10px', fontWeight: '700', textTransform: 'uppercase', letterSpacing: '0.07em', color: t.textPlaceholder, marginBottom: '6px' }}>
          Önizleme
        </div>
        <div style={{
          fontFamily: 'monospace',
          fontSize: '12px',
          color: preview ? t.accentText : t.textGhost,
          wordBreak: 'break-all',
          lineHeight: '1.5',
        }}>
          {preview || '(boş — formül yok)'}
        </div>
      </div>
    </div>
  )
}

// ─── Renk token paleti ───────────────────────────────────────────────
var COLOR_TOKENS = [
  { value: '',        label: '— Renk Yok —',       dot: 'rgba(255,255,255,0.15)' },
  { value: 'slate',   label: 'Varsayılan (Gri)',    dot: '#94a3b8' },
  { value: 'blue',    label: 'Bilgi (Mavi)',        dot: '#60a5fa' },
  { value: 'emerald', label: 'Başarılı (Yeşil)',    dot: '#34d399' },
  { value: 'amber',   label: 'Uyarı (Sarı)',        dot: '#fbbf24' },
  { value: 'red',     label: 'Tehlike (Kırmızı)',   dot: '#f87171' },
  { value: 'indigo',  label: 'Vurgu (Çivit)',       dot: '#818cf8' },
]

// ─── Ana Modal ────────────────────────────────────────────────────────
export default function RuleBuilderModal(props) {
  var isOpen           = !!props.isOpen
  var onClose          = props.onClose || function() {}
  var onSave           = props.onSave  || function() {}
  var initialValues    = props.initialValues || {}
  var availableWidgets = Array.isArray(props.availableWidgets) ? props.availableWidgets : []
  // Widget'in dataType'i — Varsayilan Deger sekmesindeki sabit girdi tipini belirler.
  var dataType         = String(props.dataType || 'text').toLowerCase()

  // Tema duyarliligi — body.app-theme-light gozlemler, MutationObserver ile reaktif
  var isLight = useThemeIsLight()
  var t       = themePalette(isLight)

  // Sekme: 'visible' | 'disabled' | 'formula' | 'color'
  var [activeTab, setActiveTab] = useState('visible')

  // visibleIf koşullar
  var [visibleConditions, setVisibleConditions] = useState([])
  var [visibleJunction, setVisibleJunction]     = useState('AND')

  // disabledIf koşullar
  var [disabledConditions, setDisabledConditions] = useState([])
  var [disabledJunction, setDisabledJunction]     = useState('AND')

  // requiredIf koşullar — true ise widget zorunlu (statik IsRequired'i override eder)
  var [requiredConditions, setRequiredConditions] = useState([])
  var [requiredJunction, setRequiredJunction]     = useState('AND')

  // formula — string olarak saklanir (backend kontrati).
  // Builder icin segments ve karmasik durumlar icin rawFallback state'leri.
  var [formula, setFormula] = useState('')
  var [formulaSegments, setFormulaSegments] = useState([])
  var [formulaFallback, setFormulaFallback] = useState(null)  // null = builder modu; string = textarea modu

  // Varsayilan deger — Sabit (literal) ya da Formul modunda. Formda yeni kayit
  // olusurken atanir; kullanici uzerine yazabilir (readonly degildir).
  var [defaultValueKind,     setDefaultValueKind]     = useState('static')  // 'static' | 'formula'
  var [defaultValueStatic,   setDefaultValueStatic]   = useState('')
  var [defaultValueSegments, setDefaultValueSegments] = useState([])
  var [defaultValueFallback, setDefaultValueFallback] = useState(null)

  // Semantik renk: 0=Statik, 1=Dinamik
  var [colorType,  setColorType]  = useState(0)
  var [colorValue, setColorValue] = useState('')

  // Modal açılınca initial değerleri parse et
  useEffect(function() {
    if (!isOpen) return undefined
    var viParsed = parseConditionsFromString(initialValues.visibleIf || '', availableWidgets)
    setVisibleConditions(viParsed.conditions)
    setVisibleJunction(viParsed.junction)

    var diParsed = parseConditionsFromString(initialValues.disabledIf || '', availableWidgets)
    setDisabledConditions(diParsed.conditions)
    setDisabledJunction(diParsed.junction)

    var riParsed = parseConditionsFromString(initialValues.requiredIf || '', availableWidgets)
    setRequiredConditions(riParsed.conditions)
    setRequiredJunction(riParsed.junction)

    var rawFormula = initialValues.formula || ''
    setFormula(rawFormula)
    var parsedF = parseFormulaSegments(rawFormula)
    if (parsedF.complex) {
      setFormulaSegments([])
      setFormulaFallback(parsedF.raw != null ? parsedF.raw : rawFormula)
    } else {
      setFormulaSegments(parsedF.segments)
      setFormulaFallback(null)
    }
    setColorType(initialValues.colorType != null ? initialValues.colorType : 0)
    setColorValue(initialValues.colorValue || '')

    // Varsayilan deger hydration
    var dvKind = initialValues.defaultValueKind || 'static'
    setDefaultValueKind(dvKind)
    var dvVal = initialValues.defaultValue != null ? String(initialValues.defaultValue) : ''
    if (dvKind === 'formula') {
      setDefaultValueStatic('')
      var parsedDv = parseFormulaSegments(dvVal)
      if (parsedDv.complex) {
        setDefaultValueSegments([])
        setDefaultValueFallback(parsedDv.raw != null ? parsedDv.raw : dvVal)
      } else {
        setDefaultValueSegments(parsedDv.segments)
        setDefaultValueFallback(null)
      }
    } else {
      setDefaultValueStatic(dvVal)
      setDefaultValueSegments([])
      setDefaultValueFallback(null)
    }

    setActiveTab('visible')
  }, [isOpen])

  // ESC ile kapat
  useEffect(function() {
    if (!isOpen) return undefined
    function onKey(e) {
      if (e.key === 'Escape') onClose()
    }
    document.addEventListener('keydown', onKey)
    return function() { document.removeEventListener('keydown', onKey) }
  }, [isOpen, onClose])

  var visiblePreview  = conditionsToString(visibleConditions, visibleJunction)
  var disabledPreview = conditionsToString(disabledConditions, disabledJunction)
  var requiredPreview = conditionsToString(requiredConditions, requiredJunction)

  // Varsayilan deger — save edilecek string. Formul modunda segment/fallback'ten
  // serialize; static modda dogrudan literal.
  var dvSaved = defaultValueKind === 'formula'
    ? (defaultValueFallback != null ? defaultValueFallback : formulaSegmentsToString(defaultValueSegments))
    : defaultValueStatic
  var dvHasValue = defaultValueKind === 'formula'
    ? (defaultValueSegments.length > 0 || (defaultValueFallback != null && defaultValueFallback.trim() !== ''))
    : (defaultValueStatic !== '' && String(defaultValueStatic).trim() !== '')

  function handleSave() {
    onSave({
      visibleIf:  visiblePreview  || '',
      disabledIf: disabledPreview || '',
      requiredIf: requiredPreview || '',
      formula:    formula.trim()  || '',
      colorType:  colorType,
      colorValue: colorValue.trim() || null,
      defaultValue:     dvHasValue ? String(dvSaved).trim() : '',
      defaultValueKind: defaultValueKind,
    })
  }

  var TABS = [
    {
      key: 'visible',
      label: 'Görünürlük',
      sublabel: 'visibleIf',
      Icon: Eye,
      hasValue: visibleConditions.length > 0,
    },
    {
      key: 'disabled',
      label: 'Aktiflik',
      sublabel: 'disabledIf',
      Icon: Lock,
      hasValue: disabledConditions.length > 0,
    },
    {
      key: 'required',
      label: 'Zorunluluk',
      sublabel: 'requiredIf',
      Icon: Asterisk,
      hasValue: requiredConditions.length > 0,
    },
    {
      key: 'default',
      label: 'Varsayılan',
      sublabel: 'defaultValue',
      Icon: Sparkles,
      hasValue: dvHasValue,
    },
    {
      key: 'color',
      label: 'Renk',
      sublabel: 'colorType',
      Icon: Palette,
      hasValue: !!colorValue,
    },
  ]

  if (typeof document === 'undefined') return null

  var content = (
    <AnimatePresence>
      {isOpen && (
        <>
          {/* Backdrop */}
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            transition={{ duration: 0.15 }}
            style={{
              position: 'fixed', inset: 0, zIndex: 9998,
              background: t.backdrop,
              backdropFilter: 'blur(8px)',
              WebkitBackdropFilter: 'blur(8px)',
            }}
            onClick={onClose}
          />

          {/* Flex wrapper */}
          <div style={{
            position: 'fixed', inset: 0, zIndex: 9999,
            display: 'flex', alignItems: 'center', justifyContent: 'center',
            padding: '20px',
            pointerEvents: 'none',
          }}>
            <motion.div
              initial={{ opacity: 0, scale: 0.94, y: 24 }}
              animate={{ opacity: 1, scale: 1, y: 0 }}
              exit={{ opacity: 0, scale: 0.97, y: 12 }}
              transition={{ type: 'spring', stiffness: 340, damping: 28 }}
              style={{
                width: '100%',
                maxWidth: '820px',
                maxHeight: '88vh',
                display: 'flex',
                flexDirection: 'column',
                borderRadius: '18px',
                overflow: 'hidden',
                pointerEvents: 'auto',
                background: t.panel,
                border: '1px solid ' + t.border,
                backdropFilter: 'blur(32px)',
                WebkitBackdropFilter: 'blur(32px)',
                boxShadow: t.shadow,
              }}
            >
              {/* Header */}
              <div style={{
                display: 'flex',
                alignItems: 'center',
                gap: '12px',
                padding: '16px 20px',
                borderBottom: '1px solid ' + t.borderSoft,
                flexShrink: 0,
              }}>
                <div style={{
                  width: '38px', height: '38px',
                  background: t.accentBg,
                  border: '1px solid ' + t.accentBorder,
                  borderRadius: '10px',
                  display: 'flex', alignItems: 'center', justifyContent: 'center',
                }}>
                  <Wrench size={16} style={{ color: t.accentText }} strokeWidth={2} />
                </div>
                <div style={{ flex: 1, minWidth: 0 }}>
                  <div style={{ fontSize: '14px', fontWeight: '700', color: t.textStrong, lineHeight: 1.2 }}>
                    Kurallar &amp; Formüller
                  </div>
                  <div style={{ fontSize: '11px', color: t.textPlaceholder, marginTop: '2px' }}>
                    Görünürlük, aktiflik ve hesaplama kurallarını görsel olarak tanımlayın
                  </div>
                </div>
                <button
                  type="button"
                  onClick={onClose}
                  style={{
                    width: '32px', height: '32px',
                    background: t.surface,
                    border: '1px solid ' + t.border,
                    borderRadius: '8px',
                    color: t.textFaint,
                    cursor: 'pointer',
                    display: 'flex', alignItems: 'center', justifyContent: 'center',
                    transition: 'background 0.15s',
                  }}
                  title="Kapat (ESC)"
                >
                  <X size={15} strokeWidth={2} />
                </button>
              </div>

              {/* Body — sol dikey sekme paneli + sag scrollable icerik */}
              <div style={{
                flex: 1,
                display: 'flex',
                minHeight: 0,
                overflow: 'hidden',
              }}>
                {/* Sol: dikey tab sidebar */}
                <div style={{
                  width: '170px',
                  flexShrink: 0,
                  borderRight: '1px solid ' + t.borderSoft,
                  padding: '14px 10px',
                  display: 'flex',
                  flexDirection: 'column',
                  gap: '4px',
                  overflowY: 'auto',
                  background: t.sidebar,
                }}>
                  {TABS.map(function(tab) {
                    var isActive = activeTab === tab.key
                    var TabIcon = tab.Icon
                    return (
                      <button
                        key={tab.key}
                        type="button"
                        onClick={function() { setActiveTab(tab.key) }}
                        style={{
                          position: 'relative',
                          display: 'flex',
                          alignItems: 'center',
                          gap: '10px',
                          padding: '10px 12px',
                          background: isActive ? t.accentBg : 'transparent',
                          border: '1px solid ' + (isActive ? t.accentBorder : t.borderFaint),
                          borderRadius: '8px',
                          color: isActive ? t.accentText : t.textMuted,
                          fontSize: '12px',
                          fontWeight: isActive ? 700 : 500,
                          cursor: 'pointer',
                          textAlign: 'left',
                          transition: 'all 0.15s',
                        }}
                      >
                        <TabIcon size={14} strokeWidth={2} style={{ flexShrink: 0 }} />
                        <span style={{ flex: 1 }}>{tab.label}</span>
                        {tab.hasValue && (
                          <span style={{
                            width: '6px', height: '6px',
                            background: '#f59e0b',
                            borderRadius: '50%',
                            flexShrink: 0,
                          }} />
                        )}
                      </button>
                    )
                  })}
                </div>

                {/* Sag: scrollable icerik */}
                <div style={{
                  flex: 1,
                  overflowY: 'auto',
                  minHeight: 0,
                  padding: '20px',
                }}>
                {activeTab === 'visible' && (
                  <div>
                    <div style={{
                      fontSize: '11px',
                      color: t.textPlaceholder,
                      marginBottom: '16px',
                      lineHeight: '1.5',
                    }}>
                      Bu koşullar <strong style={{ color: t.accentText }}>true</strong> olduğunda widget sayfada <strong style={{ color: t.text }}>görünür</strong>. Koşul yoksa her zaman görünür.
                    </div>
                    <QueryBuilder
                      tabKey="visible"
                      conditions={visibleConditions}
                      junction={visibleJunction}
                      onConditionsChange={setVisibleConditions}
                      onJunctionChange={setVisibleJunction}
                      availableWidgets={availableWidgets}
                      preview={visiblePreview}
                      isLight={isLight}
                    />
                  </div>
                )}

                {activeTab === 'disabled' && (
                  <div>
                    <div style={{
                      fontSize: '11px',
                      color: t.textPlaceholder,
                      marginBottom: '16px',
                      lineHeight: '1.5',
                    }}>
                      Bu koşullar <strong style={{ color: t.accentText }}>true</strong> olduğunda widget <strong style={{ color: t.text }}>salt okunur (readonly)</strong> olur. Koşul yoksa her zaman düzenlenebilir.
                    </div>
                    <QueryBuilder
                      tabKey="disabled"
                      conditions={disabledConditions}
                      junction={disabledJunction}
                      onConditionsChange={setDisabledConditions}
                      onJunctionChange={setDisabledJunction}
                      availableWidgets={availableWidgets}
                      preview={disabledPreview}
                      isLight={isLight}
                    />
                  </div>
                )}

                {activeTab === 'required' && (
                  <div>
                    <div style={{
                      fontSize: '11px',
                      color: t.textPlaceholder,
                      marginBottom: '16px',
                      lineHeight: '1.5',
                    }}>
                      Bu koşullar <strong style={{ color: t.accentText }}>true</strong> olduğunda widget <strong style={{ color: t.text }}>zorunlu</strong> hale gelir. Koşul tanımlıysa formdaki "Zorunlu Alan" toggle'ı devre dışı kalır — koşul kuralı override eder. Koşul yoksa toggle'ın statik değeri geçerli olur.
                    </div>
                    <QueryBuilder
                      tabKey="required"
                      conditions={requiredConditions}
                      junction={requiredJunction}
                      onConditionsChange={setRequiredConditions}
                      onJunctionChange={setRequiredJunction}
                      availableWidgets={availableWidgets}
                      preview={requiredPreview}
                      isLight={isLight}
                    />
                  </div>
                )}

                {activeTab === 'default' && (
                  <div style={{ display: 'flex', flexDirection: 'column', gap: '14px' }}>
                    <div style={{ fontSize: '11px', color: t.textPlaceholder, lineHeight: '1.5' }}>
                      Yeni bir kayıt oluşturulduğunda widget'a atanacak değer. Kullanıcı isterse üzerine yazabilir (readonly değildir).
                    </div>

                    {/* Mode toggle: Sabit / Formul */}
                    <div>
                      <div style={{ fontSize: '10px', fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.07em', color: t.textPlaceholder, marginBottom: '8px' }}>
                        Değer Tipi
                      </div>
                      <div style={{ display: 'flex', gap: '8px' }}>
                        {[{ v: 'static',  label: 'Sabit',  hint: 'Literal değer'    },
                          { v: 'formula', label: 'Formül', hint: 'Hesaplanan ifade' }].map(function(opt) {
                          var act = defaultValueKind === opt.v
                          return (
                            <button
                              key={opt.v}
                              type="button"
                              onClick={function() { setDefaultValueKind(opt.v) }}
                              style={{
                                flex: 1,
                                padding: '10px 14px',
                                borderRadius: '10px',
                                border: act ? '1px solid ' + t.accentBorder : '1px solid ' + t.border,
                                background: act ? t.accentBg : t.surfaceSoft,
                                color: act ? t.accentText : t.textFaint,
                                cursor: 'pointer',
                                textAlign: 'left',
                                transition: 'all 0.15s',
                              }}
                            >
                              <div style={{ fontSize: '12px', fontWeight: 700 }}>{opt.label}</div>
                              <div style={{ fontSize: '10px', opacity: 0.6, marginTop: '2px' }}>{opt.hint}</div>
                            </button>
                          )
                        })}
                      </div>
                    </div>

                    {/* Sabit mod: dataType'a gore ozellesmis girdi */}
                    {defaultValueKind === 'static' && (
                      <div>
                        <div style={{ fontSize: '10px', fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.07em', color: t.textPlaceholder, marginBottom: '8px' }}>
                          {dataType === 'numeric' ? 'Sayısal Değer' :
                           dataType === 'date'    ? 'Tarih Değeri'   :
                           dataType === 'boolean' ? 'Boolean Değer'  :
                                                    'Metin Değeri'}
                        </div>

                        {/* Metin (ve fallback) */}
                        {(dataType === 'text' || dataType === 'dropdown' || dataType === 'link' || dataType === 'lookup' || dataType === 'multi-select') && (
                          <input
                            type="text"
                            value={defaultValueStatic}
                            onChange={function(e) { setDefaultValueStatic(e.target.value) }}
                            placeholder={dataType === 'dropdown' ? 'örn. Aktif (seçenek etiketi)' : 'örn. Varsayılan metin'}
                            style={{
                              width: '100%',
                              height: '36px',
                              padding: '0 12px',
                              background: t.inputBg,
                              border: '1px solid ' + t.border,
                              borderRadius: '8px',
                              color: t.text,
                              fontSize: '12px',
                              boxSizing: 'border-box',
                              outline: 'none',
                            }}
                          />
                        )}

                        {/* Sayisal */}
                        {dataType === 'numeric' && (
                          <input
                            type="number"
                            step="any"
                            value={defaultValueStatic}
                            onChange={function(e) { setDefaultValueStatic(e.target.value) }}
                            placeholder="örn. 0"
                            style={{
                              width: '100%',
                              height: '36px',
                              padding: '0 12px',
                              background: t.inputBg,
                              border: '1px solid ' + t.border,
                              borderRadius: '8px',
                              color: t.text,
                              fontSize: '12px',
                              boxSizing: 'border-box',
                              outline: 'none',
                              colorScheme: t.colorScheme,
                              // Spinner butonlarini gizle
                              appearance: 'textfield',
                              MozAppearance: 'textfield',
                            }}
                          />
                        )}

                        {/* Tarih — input + hizli onset butonlari */}
                        {dataType === 'date' && (
                          <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                            <input
                              type="date"
                              value={/^TODAY(\(\))?$|^YESTERDAY(\(\))?$|^TOMORROW(\(\))?$/i.test(defaultValueStatic) ? '' : defaultValueStatic}
                              onChange={function(e) { setDefaultValueStatic(e.target.value) }}
                              style={{
                                width: '100%',
                                height: '36px',
                                padding: '0 12px',
                                background: t.inputBg,
                                border: '1px solid ' + t.border,
                                borderRadius: '8px',
                                color: t.text,
                                fontSize: '12px',
                                boxSizing: 'border-box',
                                outline: 'none',
                                colorScheme: t.colorScheme,
                              }}
                            />
                            <div style={{ display: 'flex', flexWrap: 'wrap', gap: '6px' }}>
                              {[{ v: 'TODAY',     label: 'Bugün'   },
                                { v: 'YESTERDAY', label: 'Dün'     },
                                { v: 'TOMORROW',  label: 'Yarın'   }].map(function(p) {
                                var act = String(defaultValueStatic).toUpperCase().replace('()', '') === p.v
                                return (
                                  <button
                                    key={p.v}
                                    type="button"
                                    onClick={function() { setDefaultValueStatic(p.v + '()') }}
                                    title={'Runtime\'da ' + p.label.toLowerCase() + ' tarihi olarak çözülür (' + p.v + '())'}
                                    style={{
                                      padding: '4px 10px',
                                      borderRadius: '6px',
                                      border: act ? '1px solid ' + t.accentBorder : '1px solid ' + t.border,
                                      background: act ? t.accentBg : t.surface,
                                      color: act ? t.accentText : t.textMuted,
                                      fontSize: '11px',
                                      cursor: 'pointer',
                                      display: 'inline-flex',
                                      alignItems: 'baseline',
                                      gap: '5px',
                                    }}
                                  >
                                    <span style={{ fontWeight: 600 }}>{p.label}</span>
                                    <span style={{ fontFamily: 'monospace', fontSize: '10px', opacity: 0.55 }}>{p.v}()</span>
                                  </button>
                                )
                              })}
                            </div>
                          </div>
                        )}

                        {/* Boolean toggle */}
                        {dataType === 'boolean' && (
                          <div style={{ display: 'flex', gap: '8px' }}>
                            {[{ v: 'true', label: 'Evet' }, { v: 'false', label: 'Hayır' }, { v: '', label: 'Belirsiz' }].map(function(b) {
                              var act = String(defaultValueStatic) === b.v
                              return (
                                <button
                                  key={b.v || '__empty'}
                                  type="button"
                                  onClick={function() { setDefaultValueStatic(b.v) }}
                                  style={{
                                    flex: 1,
                                    padding: '10px 14px',
                                    borderRadius: '10px',
                                    border: act ? '1px solid ' + t.accentBorder : '1px solid ' + t.border,
                                    background: act ? t.accentBg : t.surfaceSoft,
                                    color: act ? t.accentText : t.textFaint,
                                    cursor: 'pointer',
                                    fontSize: '12px',
                                    fontWeight: 600,
                                  }}
                                >
                                  {b.label}
                                </button>
                              )
                            })}
                          </div>
                        )}

                        {/* Bilinmeyen / grid / group → gri bilgi */}
                        {(dataType === 'group' || dataType === 'grid') && (
                          <div style={{ padding: '10px 12px', background: t.surfaceSoft, border: '1px dashed ' + t.border, borderRadius: '8px', fontSize: '11px', color: t.textPlaceholder }}>
                            Bu tip için varsayılan değer desteklenmiyor.
                          </div>
                        )}

                        {defaultValueStatic !== '' && (
                          <button
                            type="button"
                            onClick={function() { setDefaultValueStatic('') }}
                            style={{
                              marginTop: '8px',
                              padding: '6px 12px',
                              background: 'transparent',
                              border: '1px solid ' + t.border,
                              borderRadius: '8px',
                              color: t.textFaint,
                              fontSize: '11px',
                              cursor: 'pointer',
                            }}
                          >
                            Temizle
                          </button>
                        )}
                      </div>
                    )}

                    {/* Formul mod: FormulaBuilder reuse */}
                    {defaultValueKind === 'formula' && (
                      <FormulaBuilder
                        segments={defaultValueSegments}
                        onSegmentsChange={setDefaultValueSegments}
                        availableWidgets={availableWidgets}
                        preview={defaultValueFallback != null ? defaultValueFallback : formulaSegmentsToString(defaultValueSegments)}
                        rawFallback={defaultValueFallback}
                        onRawFallbackChange={setDefaultValueFallback}
                        isLight={isLight}
                        onClearFallback={function() {
                          setDefaultValueFallback(null)
                          setDefaultValueSegments([])
                        }}
                      />
                    )}
                  </div>
                )}

                {/* Formül sekmesi kaldirildi — Varsayilan'in formul modu zaten ayni
                    isi yapiyor. Eski kayitli rules.formula kullanicinin "Kuralları
                    Kaydet" demesiyle override edilmez (formula state'i korunur). */}

                {activeTab === 'color' && (
                  <div style={{ display: 'flex', flexDirection: 'column', gap: '20px' }}>
                    <div style={{ fontSize: '11px', color: t.textPlaceholder, lineHeight: '1.5' }}>
                      Widget'ın sol kenarlığına ve etiketine uygulanacak rengi seçin.
                      DB'de asla HEX kodu saklanmaz — yalnızca semantik token kelimesi.
                    </div>

                    {/* Mod toggle */}
                    <div>
                      <div style={{ fontSize: '10px', fontWeight: '700', textTransform: 'uppercase', letterSpacing: '0.07em', color: t.textPlaceholder, marginBottom: '10px' }}>
                        Renk Modu
                      </div>
                      <div style={{ display: 'flex', gap: '8px' }}>
                        {[{ v: 0, label: 'Statik', hint: 'Sabit token' }, { v: 1, label: 'Dinamik', hint: 'Başka alandan' }].map(function(opt) {
                          var active = colorType === opt.v
                          return (
                            <button
                              key={opt.v}
                              type="button"
                              onClick={function() { setColorType(opt.v); setColorValue('') }}
                              style={{
                                flex: 1,
                                padding: '10px 14px',
                                borderRadius: '10px',
                                border: active ? '1px solid ' + t.accentBorder : '1px solid ' + t.border,
                                background: active ? t.accentBg : t.surfaceSoft,
                                color: active ? t.accentText : t.textFaint,
                                cursor: 'pointer',
                                textAlign: 'left',
                                transition: 'all 0.15s',
                              }}
                            >
                              <div style={{ fontSize: '12px', fontWeight: '700' }}>{opt.label}</div>
                              <div style={{ fontSize: '10px', opacity: 0.6, marginTop: '2px' }}>{opt.hint}</div>
                            </button>
                          )
                        })}
                      </div>
                    </div>

                    {/* Statik: palet */}
                    {colorType === 0 && (
                      <div>
                        <div style={{ fontSize: '10px', fontWeight: '700', textTransform: 'uppercase', letterSpacing: '0.07em', color: t.textPlaceholder, marginBottom: '10px' }}>
                          Renk Paleti
                        </div>
                        <div style={{ display: 'flex', flexDirection: 'column', gap: '6px' }}>
                          {COLOR_TOKENS.map(function(tok) {
                            var active = colorValue === tok.value
                            // "Renk yok" placeholder light temada gorunur olmasi icin override
                            var dotColor = (!tok.value && isLight) ? '#cbd5e1' : tok.dot
                            return (
                              <button
                                key={tok.value || '__none'}
                                type="button"
                                onClick={function() { setColorValue(tok.value) }}
                                style={{
                                  display: 'flex',
                                  alignItems: 'center',
                                  gap: '12px',
                                  padding: '10px 14px',
                                  borderRadius: '10px',
                                  border: active ? '1px solid ' + dotColor : '1px solid ' + t.borderSoft,
                                  background: active ? t.surface : t.surfaceFaint,
                                  cursor: 'pointer',
                                  transition: 'all 0.15s',
                                  textAlign: 'left',
                                }}
                              >
                                <span style={{
                                  width: '14px', height: '14px', borderRadius: '50%',
                                  background: dotColor, flexShrink: 0,
                                  boxShadow: active ? (isLight ? '0 0 0 3px rgba(15,23,42,0.08)' : '0 0 0 3px rgba(255,255,255,0.1)') : 'none',
                                  transition: 'box-shadow 0.15s',
                                }} />
                                <span style={{ fontSize: '12px', color: active ? t.textStrong : t.textMuted, fontWeight: active ? '600' : '400' }}>
                                  {tok.label}
                                </span>
                                {tok.value && (
                                  <code style={{ marginLeft: 'auto', fontSize: '10px', color: t.textGhost, fontFamily: 'monospace' }}>
                                    {tok.value}
                                  </code>
                                )}
                                {active && <Check size={13} style={{ color: dotColor, marginLeft: tok.value ? 0 : 'auto', flexShrink: 0 }} />}
                              </button>
                            )
                          })}
                        </div>
                      </div>
                    )}

                    {/* Dinamik: widget kodu input */}
                    {colorType === 1 && (
                      <div>
                        <div style={{ fontSize: '10px', fontWeight: '700', textTransform: 'uppercase', letterSpacing: '0.07em', color: t.textPlaceholder, marginBottom: '10px' }}>
                          Renk Taşıyan Alan Kodu
                        </div>
                        <div style={{ fontSize: '11px', color: t.textPlaceholder, marginBottom: '10px', lineHeight: 1.5 }}>
                          Aynı formdaki başka bir widget'ın kodu. O alanın değeri token olarak okunur
                          (örn: <code style={{ color: t.accentText }}>amber</code>, <code style={{ color: t.accentText }}>red</code>).
                        </div>
                        {availableWidgets.length > 0 && (
                          <div style={{ display: 'flex', flexWrap: 'wrap', gap: '6px', marginBottom: '10px' }}>
                            {availableWidgets.map(function(w) {
                              var act = colorValue === w.widgetCode
                              return (
                                <button
                                  key={(w._sourceFormCode || '_own') + '::' + w.widgetCode}
                                  type="button"
                                  onClick={function() { setColorValue(w.widgetCode) }}
                                  title={'w_' + w.widgetCode + (w._sourceFormLabel ? ' [' + w._sourceFormLabel + ']' : '')}
                                  style={{
                                    display: 'inline-flex',
                                    alignItems: 'center',
                                    gap: '6px',
                                    padding: '4px 10px',
                                    borderRadius: '6px',
                                    border: act ? '1px solid ' + t.accentBorder : '1px solid ' + t.border,
                                    background: act ? t.accentBg : t.surface,
                                    color: act ? t.accentText : t.textMuted,
                                    fontSize: '11px',
                                    cursor: 'pointer',
                                    transition: 'all 0.15s',
                                  }}
                                >
                                  <span style={{ fontWeight: 600 }}>{w.label || w.widgetCode}</span>
                                  {w._sourceFormLabel && (
                                    <span
                                      style={{
                                        fontSize: '9px',
                                        padding: '1px 5px',
                                        borderRadius: '4px',
                                        background: 'rgba(99,102,241,0.18)',
                                        border: '1px solid rgba(99,102,241,0.32)',
                                        color: isLight ? '#4f46e5' : '#a5b4fc',
                                        fontWeight: 600,
                                      }}
                                    >
                                      {w._sourceFormLabel}
                                    </span>
                                  )}
                                  <span style={{ fontFamily: 'monospace', fontSize: '10px', opacity: 0.55 }}>
                                    w_{w.widgetCode}
                                  </span>
                                </button>
                              )
                            })}
                          </div>
                        )}
                        <input
                          type="text"
                          value={colorValue}
                          onChange={function(e) { setColorValue(e.target.value) }}
                          placeholder="örn. durum"
                          style={{
                            width: '100%',
                            height: '36px',
                            padding: '0 12px',
                            background: t.inputBg,
                            border: '1px solid ' + t.border,
                            borderRadius: '8px',
                            color: t.text,
                            fontSize: '12px',
                            fontFamily: 'monospace',
                            boxSizing: 'border-box',
                          }}
                        />
                      </div>
                    )}
                  </div>
                )}
                </div>
              </div>

              {/* Footer */}
              <div style={{
                padding: '14px 20px',
                borderTop: '1px solid ' + t.borderSoft,
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'flex-end',
                gap: '8px',
                flexShrink: 0,
              }}>
                <button
                  type="button"
                  onClick={onClose}
                  style={{
                    padding: '8px 20px',
                    background: t.surface,
                    border: '1px solid ' + t.border,
                    borderRadius: '9px',
                    color: t.textMuted,
                    fontSize: '12px',
                    fontWeight: '600',
                    cursor: 'pointer',
                    transition: 'background 0.15s',
                  }}
                >
                  İptal
                </button>
                <button
                  type="button"
                  onClick={handleSave}
                  style={{
                    padding: '8px 24px',
                    background: isLight ? 'rgba(245,158,11,0.15)' : 'rgba(245,158,11,0.18)',
                    border: '1px solid ' + (isLight ? 'rgba(245,158,11,0.5)' : 'rgba(245,158,11,0.4)'),
                    borderRadius: '9px',
                    color: t.accentText,
                    fontSize: '12px',
                    fontWeight: '700',
                    cursor: 'pointer',
                    display: 'flex',
                    alignItems: 'center',
                    gap: '6px',
                    transition: 'background 0.15s',
                  }}
                >
                  <Check size={13} strokeWidth={2.5} />
                  Kuralları Kaydet
                </button>
              </div>
            </motion.div>
          </div>
        </>
      )}
    </AnimatePresence>
  )

  return createPortal(content, document.body)
}
