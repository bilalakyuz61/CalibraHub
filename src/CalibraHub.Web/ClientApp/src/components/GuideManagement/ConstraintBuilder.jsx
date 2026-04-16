/**
 * ConstraintBuilder — Gorsel kisit olusturucu.
 *
 * Eslestirme modalinde textarea yerine kullanilir.
 * Dropdown ile alan, operator, deger secimi + satirlar arasi VE/VEYA mantigi.
 *
 * Deger alani iki modlu:
 *   - Sabit deger: serbest metin girisi
 *   - Form alani: secili formun alanlarindan dropdown ile secim ({fieldKey} token)
 *
 * Props:
 *   guideCode   — Rehber kodu (schema fetch icin kolon listesi)
 *   value       — Mevcut JSON string ([{field,operator,value,logic}])
 *   onChange    — Degisiklik callback'i (jsonString)
 *   formFields  — Secili formun alanlari ([{fieldKey, fieldLabel, ...}])
 */
import { useState, useEffect, useCallback } from 'react'
import { Plus, X, Loader2, ToggleLeft, ToggleRight } from 'lucide-react'

var OPERATORS = [
  { code: 'eq',   label: 'Esittir' },
  { code: 'neq',  label: 'Esit Degil' },
  { code: 'gt',   label: 'Buyuktur' },
  { code: 'lt',   label: 'Kucuktur' },
  { code: 'like', label: 'Icerir' },
  { code: 'in',   label: 'Listede' },
]

var LOGICS = [
  { code: 'and', label: 'VE' },
  { code: 'or',  label: 'VEYA' },
]

// {fieldKey} token pattern — form alani referansi
var TOKEN_RE = /^\{(\w+)\}$/

function isFieldToken(val) {
  return TOKEN_RE.test(val || '')
}

export default function ConstraintBuilder(props) {
  var guideCode  = props.guideCode
  var value      = props.value || ''
  var onChange    = props.onChange
  var formFields = Array.isArray(props.formFields) ? props.formFields : []

  var [columns, setColumns] = useState([])
  var [loading, setLoading] = useState(false)
  var [rows, setRows] = useState([])

  // ── Schema fetch — rehber kolon listesi (sol: alan dropdown) ──
  useEffect(function () {
    if (!guideCode) return
    var alive = true
    setLoading(true)
    fetch('/api/guides/' + encodeURIComponent(guideCode) + '/schema', {
      method: 'GET', credentials: 'same-origin',
      headers: { 'Accept': 'application/json' },
    })
      .then(function (r) { return r.ok ? r.json() : null })
      .then(function (data) {
        if (alive && data && Array.isArray(data.columns)) {
          setColumns(data.columns)
        }
      })
      .catch(function () { /* sessizce devam */ })
      .finally(function () { if (alive) setLoading(false) })
    return function () { alive = false }
  }, [guideCode])

  // ── JSON parse → rows ──
  useEffect(function () {
    if (!value || !value.trim()) {
      setRows([])
      return
    }
    try {
      var parsed = JSON.parse(value)
      if (Array.isArray(parsed)) {
        setRows(parsed.map(function (item, idx) {
          var val = item.value || ''
          return {
            field: item.field || '',
            operator: item.operator || 'eq',
            value: val,
            logic: item.logic || 'and',
            useFieldRef: isFieldToken(val),
            _key: Date.now() + '_' + idx,
          }
        }))
      }
    } catch (e) {
      setRows([])
    }
  }, []) // Sadece ilk yukleme

  // ── Rows → JSON serialize + onChange ──
  var emitChange = useCallback(function (newRows) {
    if (newRows.length === 0) {
      onChange('')
      return
    }
    var json = JSON.stringify(newRows.map(function (r) {
      return {
        field: r.field,
        operator: r.operator,
        value: r.value,
        logic: r.logic || 'and',
      }
    }))
    onChange(json)
  }, [onChange])

  function addRow() {
    var newRows = rows.concat([{
      field: columns.length > 0 ? columns[0] : '',
      operator: 'eq',
      value: '',
      logic: 'and',
      useFieldRef: false,
      _key: Date.now() + '_' + rows.length,
    }])
    setRows(newRows)
    emitChange(newRows)
  }

  function removeRow(idx) {
    var newRows = rows.filter(function (_, i) { return i !== idx })
    setRows(newRows)
    emitChange(newRows)
  }

  function updateRow(idx, key, val) {
    var newRows = rows.map(function (r, i) {
      if (i !== idx) return r
      var copy = {
        field: r.field, operator: r.operator,
        value: r.value, logic: r.logic,
        useFieldRef: r.useFieldRef, _key: r._key,
      }
      copy[key] = val
      return copy
    })
    setRows(newRows)
    emitChange(newRows)
  }

  // Deger modu degistir (sabit ↔ alan referansi)
  function toggleValueMode(idx) {
    var newRows = rows.map(function (r, i) {
      if (i !== idx) return r
      var newUseRef = !r.useFieldRef
      var newValue = ''
      if (newUseRef && formFields.length > 0) {
        newValue = '{' + formFields[0].fieldKey + '}'
      }
      return {
        field: r.field, operator: r.operator,
        value: newValue, logic: r.logic,
        useFieldRef: newUseRef, _key: r._key,
      }
    })
    setRows(newRows)
    emitChange(newRows)
  }

  // Form alani dropdown degistiginde token formatinda kaydet
  function setFieldRefValue(idx, fieldKey) {
    updateRow(idx, 'value', '{' + fieldKey + '}')
  }

  if (loading) {
    return (
      <div className="gm-cb-loading">
        <Loader2 size={14} className="gm-spin" /> Kolon listesi yukleniyor...
      </div>
    )
  }

  return (
    <div className="gm-cb-root">
      {rows.map(function (row, idx) {
        // Mevcut token'dan fieldKey cikart (dropdown icin)
        var tokenMatch = TOKEN_RE.exec(row.value || '')
        var refFieldKey = tokenMatch ? tokenMatch[1] : ''

        return (
          <div key={row._key} className="gm-cb-row-wrap">
            {/* VE/VEYA satirlar arasi */}
            {idx > 0 && (
              <div className="gm-cb-logic-row">
                <select
                  className="gm-cb-logic"
                  value={row.logic || 'and'}
                  onChange={function (e) { updateRow(idx, 'logic', e.target.value) }}
                >
                  {LOGICS.map(function (l) {
                    return <option key={l.code} value={l.code}>{l.label}</option>
                  })}
                </select>
              </div>
            )}

            <div className="gm-cb-row">
              {/* Alan dropdown (rehber kolonlari) */}
              <select
                className="gm-cb-select gm-cb-select--field"
                value={row.field}
                onChange={function (e) { updateRow(idx, 'field', e.target.value) }}
              >
                <option value="">Alan sec...</option>
                {columns.map(function (c) {
                  return <option key={c} value={c}>{c}</option>
                })}
              </select>

              {/* Operator dropdown */}
              <select
                className="gm-cb-select gm-cb-select--op"
                value={row.operator}
                onChange={function (e) { updateRow(idx, 'operator', e.target.value) }}
              >
                {OPERATORS.map(function (op) {
                  return <option key={op.code} value={op.code}>{op.label}</option>
                })}
              </select>

              {/* Deger: mod toggle + input/dropdown */}
              <div className="gm-cb-value-wrap">
                {formFields.length > 0 && (
                  <button
                    type="button"
                    className={'gm-cb-mode-btn' + (row.useFieldRef ? ' gm-cb-mode-btn--active' : '')}
                    onClick={function () { toggleValueMode(idx) }}
                    title={row.useFieldRef ? 'Sabit degere gec' : 'Form alanindan sec'}
                  >
                    {row.useFieldRef
                      ? <ToggleRight size={16} />
                      : <ToggleLeft size={16} />}
                  </button>
                )}

                {row.useFieldRef ? (
                  <select
                    className="gm-cb-select gm-cb-select--ref"
                    value={refFieldKey}
                    onChange={function (e) { setFieldRefValue(idx, e.target.value) }}
                  >
                    <option value="">Form alani sec...</option>
                    {formFields.map(function (ff) {
                      return <option key={ff.fieldKey} value={ff.fieldKey}>
                        {ff.fieldLabel} ({ff.fieldKey})
                      </option>
                    })}
                  </select>
                ) : (
                  <input
                    type="text"
                    className="gm-cb-input"
                    value={row.value}
                    onChange={function (e) { updateRow(idx, 'value', e.target.value) }}
                    placeholder={row.operator === 'in' ? 'Deger1,Deger2,...' : 'Deger'}
                  />
                )}
              </div>

              {/* Sil butonu */}
              <button
                type="button"
                className="gm-cb-remove"
                onClick={function () { removeRow(idx) }}
                title="Kisiti kaldir"
              >
                <X size={14} />
              </button>
            </div>
          </div>
        )
      })}

      <button type="button" className="gm-cb-add" onClick={addRow}>
        <Plus size={13} /> Kisit Ekle
      </button>
    </div>
  )
}
