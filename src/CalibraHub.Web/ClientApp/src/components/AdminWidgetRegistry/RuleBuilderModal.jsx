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
import { X, Plus, Trash2, Eye, Lock, Calculator, Wrench, ChevronDown, Check, Palette } from 'lucide-react'

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

// ─── Alan Dropdown ────────────────────────────────────────────────────
function FieldDropdown(props) {
  var value = props.value || ''
  var onChange = props.onChange
  var widgets = props.widgets || []
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
          background: 'rgba(255,255,255,0.05)',
          border: '1px solid rgba(255,255,255,0.1)',
          borderRadius: '8px',
          color: selected ? 'rgba(255,255,255,0.85)' : 'rgba(255,255,255,0.3)',
          fontSize: '11px',
          fontFamily: 'monospace',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          gap: '6px',
          cursor: 'pointer',
          transition: 'border-color 0.15s',
        }}
      >
        <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
          {selected ? ('w_' + selected.widgetCode) : 'Alan seç...'}
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
          background: 'rgba(10,14,26,0.97)',
          border: '1px solid rgba(255,255,255,0.12)',
          borderRadius: '10px',
          boxShadow: '0 12px 40px rgba(0,0,0,0.6)',
          overflow: 'hidden',
          maxHeight: '200px',
          overflowY: 'auto',
        }}>
          {widgets.length === 0 && (
            <div style={{ padding: '10px 12px', fontSize: '11px', color: 'rgba(255,255,255,0.3)' }}>
              Widget bulunamadı
            </div>
          )}
          {widgets.map(function(w) {
            return (
              <button
                key={w.widgetCode}
                type="button"
                onClick={function() { onChange(w.widgetCode, w.dataType); setOpen(false) }}
                style={{
                  width: '100%',
                  padding: '8px 12px',
                  background: value === w.widgetCode ? 'rgba(245,158,11,0.12)' : 'transparent',
                  border: 'none',
                  color: value === w.widgetCode ? '#fbbf24' : 'rgba(255,255,255,0.75)',
                  fontSize: '11px',
                  fontFamily: 'monospace',
                  textAlign: 'left',
                  cursor: 'pointer',
                  display: 'flex',
                  alignItems: 'center',
                  gap: '8px',
                }}
              >
                {value === w.widgetCode && <Check size={10} />}
                <span style={{ flex: 1 }}>w_{w.widgetCode}</span>
                <span style={{ fontSize: '9px', opacity: 0.4, fontFamily: 'sans-serif' }}>
                  {w.label}
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
          background: 'rgba(255,255,255,0.05)',
          border: '1px solid rgba(255,255,255,0.1)',
          borderRadius: '8px',
          color: 'rgba(255,255,255,0.8)',
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
          background: 'rgba(10,14,26,0.97)',
          border: '1px solid rgba(255,255,255,0.12)',
          borderRadius: '10px',
          boxShadow: '0 12px 40px rgba(0,0,0,0.6)',
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
                  background: value === op.value ? 'rgba(245,158,11,0.12)' : 'transparent',
                  border: 'none',
                  color: value === op.value ? '#fbbf24' : 'rgba(255,255,255,0.75)',
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

// ─── Query Builder (visibleIf / disabledIf sekmeleri için) ───────────
function QueryBuilder(props) {
  var conditions = props.conditions
  var junction = props.junction
  var onConditionsChange = props.onConditionsChange
  var onJunctionChange = props.onJunctionChange
  var availableWidgets = props.availableWidgets || []
  var preview = props.preview || ''

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
          color: 'rgba(255,255,255,0.25)',
          fontSize: '12px',
          border: '1px dashed rgba(255,255,255,0.1)',
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
                <div style={{ flex: 1, height: '1px', background: 'rgba(255,255,255,0.06)' }} />
                <span style={{
                  fontSize: '10px',
                  fontWeight: '700',
                  color: junction === 'OR' ? '#fb923c' : '#818cf8',
                  textTransform: 'uppercase',
                  letterSpacing: '0.08em',
                }}>
                  {junction}
                </span>
                <div style={{ flex: 1, height: '1px', background: 'rgba(255,255,255,0.06)' }} />
              </div>
            )}

            {/* Koşul Satırı */}
            <div style={{
              display: 'flex',
              alignItems: 'center',
              gap: '8px',
              padding: '10px 12px',
              background: 'rgba(255,255,255,0.03)',
              border: '1px solid rgba(255,255,255,0.07)',
              borderRadius: '10px',
            }}>
              <FieldDropdown
                value={cond.field}
                widgets={availableWidgets}
                onChange={function(code, dtype) {
                  updateCondition(cond.id, { field: code, dataType: dtype || 'text' })
                }}
              />
              <OperatorDropdown
                value={cond.operator}
                onChange={function(op) {
                  updateCondition(cond.id, { operator: op })
                }}
              />
              <input
                type="text"
                value={cond.value}
                onChange={function(e) {
                  updateCondition(cond.id, { value: e.target.value })
                }}
                placeholder={isNumericType(cond.dataType) ? '0' : 'değer...'}
                style={{
                  flex: 1,
                  height: '34px',
                  padding: '0 10px',
                  background: 'rgba(255,255,255,0.05)',
                  border: '1px solid rgba(255,255,255,0.1)',
                  borderRadius: '8px',
                  color: 'rgba(255,255,255,0.85)',
                  fontSize: '12px',
                  outline: 'none',
                  minWidth: '80px',
                }}
              />
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
                  color: 'rgba(239,68,68,0.7)',
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
            background: 'rgba(245,158,11,0.1)',
            border: '1px solid rgba(245,158,11,0.25)',
            borderRadius: '8px',
            color: '#fbbf24',
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
            background: 'rgba(255,255,255,0.04)',
            border: '1px solid rgba(255,255,255,0.08)',
            borderRadius: '8px',
            fontSize: '11px',
            color: 'rgba(255,255,255,0.6)',
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
              <span style={{ color: junction === 'AND' ? '#818cf8' : 'rgba(255,255,255,0.5)', fontWeight: '600' }}>AND</span>
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
              <span style={{ color: junction === 'OR' ? '#fb923c' : 'rgba(255,255,255,0.5)', fontWeight: '600' }}>OR</span>
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
              border: '1px solid rgba(255,255,255,0.08)',
              borderRadius: '8px',
              color: 'rgba(255,255,255,0.35)',
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
        background: 'rgba(0,0,0,0.2)',
        border: '1px solid rgba(255,255,255,0.07)',
        borderRadius: '8px',
      }}>
        <div style={{ fontSize: '10px', fontWeight: '700', textTransform: 'uppercase', letterSpacing: '0.07em', color: 'rgba(255,255,255,0.3)', marginBottom: '6px' }}>
          Önizleme
        </div>
        <div style={{
          fontFamily: 'monospace',
          fontSize: '12px',
          color: preview ? '#fbbf24' : 'rgba(255,255,255,0.2)',
          wordBreak: 'break-all',
          lineHeight: '1.5',
        }}>
          {preview || '(boş — koşul yok)'}
        </div>
      </div>
    </div>
  )
}

// ─── Formül Editörü ──────────────────────────────────────────────────
function FormulaEditor(props) {
  var formula = props.formula
  var onFormulaChange = props.onFormulaChange
  var availableWidgets = props.availableWidgets || []
  var textareaRef = useRef(null)

  function insertAtCursor(text) {
    var el = textareaRef.current
    if (!el) {
      onFormulaChange(formula + text)
      return
    }
    var start = el.selectionStart
    var end = el.selectionEnd
    var newVal = formula.slice(0, start) + text + formula.slice(end)
    onFormulaChange(newVal)
    // cursor'ı insert sonrasına taşı
    setTimeout(function() {
      el.focus()
      el.setSelectionRange(start + text.length, start + text.length)
    }, 0)
  }

  var quickOps = ['+', '-', '*', '/', '(', ')']

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
      {/* Kullanılabilir alanlar */}
      <div>
        <div style={{
          fontSize: '10px',
          fontWeight: '700',
          textTransform: 'uppercase',
          letterSpacing: '0.07em',
          color: 'rgba(255,255,255,0.3)',
          marginBottom: '8px',
        }}>
          Kullanılabilir Alanlar
        </div>
        {availableWidgets.length === 0 ? (
          <div style={{ fontSize: '11px', color: 'rgba(255,255,255,0.2)' }}>
            Formdaki diğer widget'lar burada listelenir.
          </div>
        ) : (
          <div style={{ display: 'flex', flexWrap: 'wrap', gap: '6px' }}>
            {availableWidgets.map(function(w) {
              return (
                <button
                  key={w.widgetCode}
                  type="button"
                  onClick={function() { insertAtCursor('w_' + w.widgetCode) }}
                  title={w.label + ' (' + (w.dataType || 'text') + ')'}
                  style={{
                    padding: '4px 10px',
                    background: 'rgba(245,158,11,0.1)',
                    border: '1px solid rgba(245,158,11,0.25)',
                    borderRadius: '6px',
                    color: '#fbbf24',
                    fontSize: '11px',
                    fontFamily: 'monospace',
                    cursor: 'pointer',
                    transition: 'background 0.15s',
                  }}
                >
                  w_{w.widgetCode}
                </button>
              )
            })}
          </div>
        )}
      </div>

      {/* Hızlı Operatörler */}
      <div style={{ display: 'flex', alignItems: 'center', gap: '6px', flexWrap: 'wrap' }}>
        <span style={{ fontSize: '10px', color: 'rgba(255,255,255,0.3)', fontWeight: '600', textTransform: 'uppercase', letterSpacing: '0.07em' }}>
          Hızlı:
        </span>
        {quickOps.map(function(op) {
          return (
            <button
              key={op}
              type="button"
              onClick={function() { insertAtCursor(' ' + op + ' ') }}
              style={{
                width: '32px',
                height: '28px',
                background: 'rgba(255,255,255,0.06)',
                border: '1px solid rgba(255,255,255,0.1)',
                borderRadius: '6px',
                color: 'rgba(255,255,255,0.7)',
                fontSize: '14px',
                fontFamily: 'monospace',
                cursor: 'pointer',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
              }}
            >
              {op}
            </button>
          )
        })}
      </div>

      {/* Formül textarea */}
      <div>
        <div style={{
          fontSize: '10px',
          fontWeight: '700',
          textTransform: 'uppercase',
          letterSpacing: '0.07em',
          color: 'rgba(255,255,255,0.3)',
          marginBottom: '6px',
        }}>
          Formül
        </div>
        <textarea
          ref={textareaRef}
          value={formula}
          onChange={function(e) { onFormulaChange(e.target.value) }}
          placeholder="w_price * w_qty * 1.18"
          rows={3}
          style={{
            width: '100%',
            padding: '10px 12px',
            background: 'rgba(255,255,255,0.05)',
            border: '1px solid rgba(255,255,255,0.1)',
            borderRadius: '8px',
            color: 'rgba(255,255,255,0.85)',
            fontSize: '13px',
            fontFamily: 'monospace',
            outline: 'none',
            resize: 'vertical',
            boxSizing: 'border-box',
            lineHeight: '1.5',
          }}
        />
      </div>

      {/* Temizle */}
      {formula && (
        <div>
          <button
            type="button"
            onClick={function() { onFormulaChange('') }}
            style={{
              padding: '6px 12px',
              background: 'transparent',
              border: '1px solid rgba(255,255,255,0.08)',
              borderRadius: '8px',
              color: 'rgba(255,255,255,0.35)',
              fontSize: '11px',
              cursor: 'pointer',
            }}
          >
            Temizle
          </button>
        </div>
      )}

      {/* Önizleme */}
      <div style={{
        padding: '10px 12px',
        background: 'rgba(0,0,0,0.2)',
        border: '1px solid rgba(255,255,255,0.07)',
        borderRadius: '8px',
      }}>
        <div style={{ fontSize: '10px', fontWeight: '700', textTransform: 'uppercase', letterSpacing: '0.07em', color: 'rgba(255,255,255,0.3)', marginBottom: '6px' }}>
          Önizleme
        </div>
        <div style={{
          fontFamily: 'monospace',
          fontSize: '12px',
          color: formula ? '#fbbf24' : 'rgba(255,255,255,0.2)',
          wordBreak: 'break-all',
        }}>
          {formula || '(boş — formül yok)'}
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

  // Sekme: 'visible' | 'disabled' | 'formula' | 'color'
  var [activeTab, setActiveTab] = useState('visible')

  // visibleIf koşullar
  var [visibleConditions, setVisibleConditions] = useState([])
  var [visibleJunction, setVisibleJunction]     = useState('AND')

  // disabledIf koşullar
  var [disabledConditions, setDisabledConditions] = useState([])
  var [disabledJunction, setDisabledJunction]     = useState('AND')

  // formula
  var [formula, setFormula] = useState('')

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

    setFormula(initialValues.formula || '')
    setColorType(initialValues.colorType != null ? initialValues.colorType : 0)
    setColorValue(initialValues.colorValue || '')
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

  function handleSave() {
    onSave({
      visibleIf:  visiblePreview  || '',
      disabledIf: disabledPreview || '',
      formula:    formula.trim()  || '',
      colorType:  colorType,
      colorValue: colorValue.trim() || null,
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
      key: 'formula',
      label: 'Formül',
      sublabel: 'formula',
      Icon: Calculator,
      hasValue: !!formula.trim(),
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
              background: 'rgba(0,0,0,0.72)',
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
                maxWidth: '680px',
                maxHeight: '88vh',
                display: 'flex',
                flexDirection: 'column',
                borderRadius: '18px',
                overflow: 'hidden',
                pointerEvents: 'auto',
                background: 'rgba(7,10,20,0.96)',
                border: '1px solid rgba(255,255,255,0.1)',
                backdropFilter: 'blur(32px)',
                WebkitBackdropFilter: 'blur(32px)',
                boxShadow: '0 24px 80px rgba(0,0,0,0.65)',
              }}
            >
              {/* Header */}
              <div style={{
                display: 'flex',
                alignItems: 'center',
                gap: '12px',
                padding: '16px 20px',
                borderBottom: '1px solid rgba(255,255,255,0.07)',
                flexShrink: 0,
              }}>
                <div style={{
                  width: '38px', height: '38px',
                  background: 'rgba(245,158,11,0.12)',
                  border: '1px solid rgba(245,158,11,0.28)',
                  borderRadius: '10px',
                  display: 'flex', alignItems: 'center', justifyContent: 'center',
                }}>
                  <Wrench size={16} style={{ color: '#fbbf24' }} strokeWidth={2} />
                </div>
                <div style={{ flex: 1, minWidth: 0 }}>
                  <div style={{ fontSize: '14px', fontWeight: '700', color: 'rgba(255,255,255,0.92)', lineHeight: 1.2 }}>
                    Kurallar &amp; Formüller
                  </div>
                  <div style={{ fontSize: '11px', color: 'rgba(255,255,255,0.35)', marginTop: '2px' }}>
                    Görünürlük, aktiflik ve hesaplama kurallarını görsel olarak tanımlayın
                  </div>
                </div>
                <button
                  type="button"
                  onClick={onClose}
                  style={{
                    width: '32px', height: '32px',
                    background: 'rgba(255,255,255,0.04)',
                    border: '1px solid rgba(255,255,255,0.08)',
                    borderRadius: '8px',
                    color: 'rgba(255,255,255,0.4)',
                    cursor: 'pointer',
                    display: 'flex', alignItems: 'center', justifyContent: 'center',
                    transition: 'background 0.15s',
                  }}
                  title="Kapat (ESC)"
                >
                  <X size={15} strokeWidth={2} />
                </button>
              </div>

              {/* Tab Bar */}
              <div style={{
                display: 'flex',
                gap: '4px',
                padding: '10px 20px 0',
                borderBottom: '1px solid rgba(255,255,255,0.07)',
                flexShrink: 0,
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
                        display: 'flex',
                        alignItems: 'center',
                        gap: '7px',
                        padding: '8px 16px',
                        background: isActive ? 'rgba(245,158,11,0.1)' : 'transparent',
                        border: 'none',
                        borderBottom: isActive ? '2px solid #f59e0b' : '2px solid transparent',
                        borderRadius: '8px 8px 0 0',
                        color: isActive ? '#fbbf24' : 'rgba(255,255,255,0.45)',
                        fontSize: '12px',
                        fontWeight: isActive ? '700' : '500',
                        cursor: 'pointer',
                        transition: 'color 0.15s, background 0.15s',
                        marginBottom: '-1px',
                        position: 'relative',
                      }}
                    >
                      <TabIcon size={13} strokeWidth={2} />
                      {tab.label}
                      {tab.hasValue && (
                        <span style={{
                          width: '6px', height: '6px',
                          background: '#f59e0b',
                          borderRadius: '50%',
                          position: 'absolute',
                          top: '6px', right: '6px',
                        }} />
                      )}
                    </button>
                  )
                })}
              </div>

              {/* Body — scrollable */}
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
                      color: 'rgba(255,255,255,0.35)',
                      marginBottom: '16px',
                      lineHeight: '1.5',
                    }}>
                      Bu koşullar <strong style={{ color: '#fbbf24' }}>true</strong> olduğunda widget sayfada <strong style={{ color: 'rgba(255,255,255,0.7)' }}>görünür</strong>. Koşul yoksa her zaman görünür.
                    </div>
                    <QueryBuilder
                      tabKey="visible"
                      conditions={visibleConditions}
                      junction={visibleJunction}
                      onConditionsChange={setVisibleConditions}
                      onJunctionChange={setVisibleJunction}
                      availableWidgets={availableWidgets}
                      preview={visiblePreview}
                    />
                  </div>
                )}

                {activeTab === 'disabled' && (
                  <div>
                    <div style={{
                      fontSize: '11px',
                      color: 'rgba(255,255,255,0.35)',
                      marginBottom: '16px',
                      lineHeight: '1.5',
                    }}>
                      Bu koşullar <strong style={{ color: '#fbbf24' }}>true</strong> olduğunda widget <strong style={{ color: 'rgba(255,255,255,0.7)' }}>salt okunur (readonly)</strong> olur. Koşul yoksa her zaman düzenlenebilir.
                    </div>
                    <QueryBuilder
                      tabKey="disabled"
                      conditions={disabledConditions}
                      junction={disabledJunction}
                      onConditionsChange={setDisabledConditions}
                      onJunctionChange={setDisabledJunction}
                      availableWidgets={availableWidgets}
                      preview={disabledPreview}
                    />
                  </div>
                )}

                {activeTab === 'formula' && (
                  <div>
                    <div style={{
                      fontSize: '11px',
                      color: 'rgba(255,255,255,0.35)',
                      marginBottom: '16px',
                      lineHeight: '1.5',
                    }}>
                      Formül tanımlandığında widget'ın değeri bu ifadeden <strong style={{ color: 'rgba(255,255,255,0.7)' }}>otomatik hesaplanır</strong> ve salt okunur olur.
                      Operatörler: <code style={{ color: '#fbbf24', fontSize: '11px' }}>+ - * / % == != &gt; &lt; &amp;&amp; || ! ?:</code>
                    </div>
                    <FormulaEditor
                      formula={formula}
                      onFormulaChange={setFormula}
                      availableWidgets={availableWidgets}
                    />
                  </div>
                )}

                {activeTab === 'color' && (
                  <div style={{ display: 'flex', flexDirection: 'column', gap: '20px' }}>
                    <div style={{ fontSize: '11px', color: 'rgba(255,255,255,0.35)', lineHeight: '1.5' }}>
                      Widget'ın sol kenarlığına ve etiketine uygulanacak rengi seçin.
                      DB'de asla HEX kodu saklanmaz — yalnızca semantik token kelimesi.
                    </div>

                    {/* Mod toggle */}
                    <div>
                      <div style={{ fontSize: '10px', fontWeight: '700', textTransform: 'uppercase', letterSpacing: '0.07em', color: 'rgba(255,255,255,0.3)', marginBottom: '10px' }}>
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
                                border: active ? '1px solid rgba(245,158,11,0.45)' : '1px solid rgba(255,255,255,0.08)',
                                background: active ? 'rgba(245,158,11,0.12)' : 'rgba(255,255,255,0.03)',
                                color: active ? '#fbbf24' : 'rgba(255,255,255,0.4)',
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
                        <div style={{ fontSize: '10px', fontWeight: '700', textTransform: 'uppercase', letterSpacing: '0.07em', color: 'rgba(255,255,255,0.3)', marginBottom: '10px' }}>
                          Renk Paleti
                        </div>
                        <div style={{ display: 'flex', flexDirection: 'column', gap: '6px' }}>
                          {COLOR_TOKENS.map(function(tok) {
                            var active = colorValue === tok.value
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
                                  border: active ? '1px solid ' + tok.dot : '1px solid rgba(255,255,255,0.07)',
                                  background: active ? 'rgba(255,255,255,0.05)' : 'rgba(255,255,255,0.02)',
                                  cursor: 'pointer',
                                  transition: 'all 0.15s',
                                  textAlign: 'left',
                                }}
                              >
                                <span style={{
                                  width: '14px', height: '14px', borderRadius: '50%',
                                  background: tok.dot, flexShrink: 0,
                                  boxShadow: active ? '0 0 0 3px rgba(255,255,255,0.1)' : 'none',
                                  transition: 'box-shadow 0.15s',
                                }} />
                                <span style={{ fontSize: '12px', color: active ? 'rgba(255,255,255,0.9)' : 'rgba(255,255,255,0.55)', fontWeight: active ? '600' : '400' }}>
                                  {tok.label}
                                </span>
                                {tok.value && (
                                  <code style={{ marginLeft: 'auto', fontSize: '10px', color: 'rgba(255,255,255,0.25)', fontFamily: 'monospace' }}>
                                    {tok.value}
                                  </code>
                                )}
                                {active && <Check size={13} style={{ color: tok.dot, marginLeft: tok.value ? 0 : 'auto', flexShrink: 0 }} />}
                              </button>
                            )
                          })}
                        </div>
                      </div>
                    )}

                    {/* Dinamik: widget kodu input */}
                    {colorType === 1 && (
                      <div>
                        <div style={{ fontSize: '10px', fontWeight: '700', textTransform: 'uppercase', letterSpacing: '0.07em', color: 'rgba(255,255,255,0.3)', marginBottom: '10px' }}>
                          Renk Taşıyan Alan Kodu
                        </div>
                        <div style={{ fontSize: '11px', color: 'rgba(255,255,255,0.3)', marginBottom: '10px', lineHeight: 1.5 }}>
                          Aynı formdaki başka bir widget'ın kodu. O alanın değeri token olarak okunur
                          (örn: <code style={{ color: '#fbbf24' }}>amber</code>, <code style={{ color: '#fbbf24' }}>red</code>).
                        </div>
                        {availableWidgets.length > 0 && (
                          <div style={{ display: 'flex', flexWrap: 'wrap', gap: '6px', marginBottom: '10px' }}>
                            {availableWidgets.map(function(w) {
                              return (
                                <button
                                  key={w.widgetCode}
                                  type="button"
                                  onClick={function() { setColorValue(w.widgetCode) }}
                                  style={{
                                    padding: '4px 10px',
                                    borderRadius: '6px',
                                    border: colorValue === w.widgetCode ? '1px solid rgba(245,158,11,0.5)' : '1px solid rgba(255,255,255,0.1)',
                                    background: colorValue === w.widgetCode ? 'rgba(245,158,11,0.12)' : 'rgba(255,255,255,0.04)',
                                    color: colorValue === w.widgetCode ? '#fbbf24' : 'rgba(255,255,255,0.55)',
                                    fontSize: '11px',
                                    fontFamily: 'monospace',
                                    cursor: 'pointer',
                                    transition: 'all 0.15s',
                                  }}
                                >
                                  w_{w.widgetCode}
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
                            background: 'rgba(255,255,255,0.05)',
                            border: '1px solid rgba(255,255,255,0.12)',
                            borderRadius: '8px',
                            color: 'rgba(255,255,255,0.85)',
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

              {/* Footer */}
              <div style={{
                padding: '14px 20px',
                borderTop: '1px solid rgba(255,255,255,0.07)',
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
                    background: 'rgba(255,255,255,0.04)',
                    border: '1px solid rgba(255,255,255,0.08)',
                    borderRadius: '9px',
                    color: 'rgba(255,255,255,0.5)',
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
                    background: 'rgba(245,158,11,0.18)',
                    border: '1px solid rgba(245,158,11,0.4)',
                    borderRadius: '9px',
                    color: '#fbbf24',
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
