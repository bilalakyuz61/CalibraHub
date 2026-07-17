/**
 * ViewBuilderVisual — Görsel kurucu sekmesi.
 *
 * Temel tablo/view seçimi + join listesi (tip + tablo + ON koşulları) +
 * kolon listesi (kaynak kolon veya hesaplanan alan). Tamamen controlled —
 * tüm değişiklikler onChange(nextDefinition) ile parent'a bildirilir.
 *
 * definition şekli (ViewDefinitionJson ile birebir, camelCase):
 *   { baseObject: {schema,name,alias} | null,
 *     joins: [{ type:'inner'|'left'|'right', schema, name, alias,
 *               on: [{ leftAlias, leftCol, op, rightAlias, rightCol }] }],
 *     columns: [{ kind:'column', sourceAlias, sourceCol, expression:null, alias }
 *              |{ kind:'calc', sourceAlias:null, sourceCol:null, expression, alias }] }
 */
import React from 'react'
import { Table2, GitMerge, Columns3, Plus, Trash2, AlertTriangle, Info, Sparkles } from 'lucide-react'

const JOIN_TYPES = [
  { value: 'inner', label: 'Inner Join' },
  { value: 'left', label: 'Left Join' },
  { value: 'right', label: 'Right Join' },
]
const OP_CHOICES = ['=', '<>', '!=', '<', '>', '<=', '>=']

function updateAt(arr, idx, updater) {
  return (arr || []).map(function (item, i) { return i === idx ? updater(item) : item })
}
function removeAt(arr, idx) {
  return (arr || []).filter(function (_, i) { return i !== idx })
}
function tableKey(schema, name) { return schema + '.' + name }
function nextJoinAlias(joins) {
  var n = (joins || []).length + 1
  var existing = {}
  ;(joins || []).forEach(function (j) { existing[j.alias] = true })
  while (existing['j' + n]) n++
  return 'j' + n
}

export default function ViewBuilderVisual({ definition, onChange, schemaTables, columnsCache, aliasMap, disabled }) {
  var def = definition || { baseObject: null, joins: [], columns: [] }
  var joins = def.joins || []
  var columns = def.columns || []
  var aliases = Object.keys(aliasMap || {})

  function emit(next) {
    if (disabled) return
    onChange(Object.assign({ baseObject: null, joins: [], columns: [] }, def, next))
  }

  function colsForAlias(alias) {
    var ref = (aliasMap || {})[alias]
    if (!ref) return []
    var cached = (columnsCache || {})[tableKey(ref.schema, ref.name)]
    return Array.isArray(cached) ? cached : []
  }

  // ── Taban nesne ──────────────────────────────────────────────────────
  function handleBaseObjectSelect(value) {
    if (!value) { emit({ baseObject: null }); return }
    var parts = value.split('|')
    var schema = parts[0], name = parts[1]
    var alias = (def.baseObject && def.baseObject.alias) || 'base'
    emit({ baseObject: { schema: schema, name: name, alias: alias } })
  }
  function handleBaseAliasChange(alias) {
    if (!def.baseObject) return
    emit({ baseObject: Object.assign({}, def.baseObject, { alias: alias }) })
  }

  // ── Join'ler ─────────────────────────────────────────────────────────
  function addJoin() {
    var alias = nextJoinAlias(joins)
    emit({ joins: joins.concat([{ type: 'inner', schema: '', name: '', alias: alias, on: [] }]) })
  }
  function updateJoin(idx, patch) {
    emit({ joins: updateAt(joins, idx, function (j) { return Object.assign({}, j, patch) }) })
  }
  function removeJoin(idx) {
    emit({ joins: removeAt(joins, idx) })
  }
  function handleJoinTableSelect(idx, value) {
    if (!value) { updateJoin(idx, { schema: '', name: '' }); return }
    var parts = value.split('|')
    updateJoin(idx, { schema: parts[0], name: parts[1] })
  }
  function addOnCondition(joinIdx) {
    var j = joins[joinIdx]
    var leftDefault = aliases.length > 0 ? aliases[0] : ''
    updateJoin(joinIdx, {
      on: (j.on || []).concat([{ leftAlias: leftDefault, leftCol: '', op: '=', rightAlias: j.alias, rightCol: '' }]),
    })
  }
  function updateOnCondition(joinIdx, onIdx, patch) {
    var j = joins[joinIdx]
    updateJoin(joinIdx, { on: updateAt(j.on, onIdx, function (o) { return Object.assign({}, o, patch) }) })
  }
  function removeOnCondition(joinIdx, onIdx) {
    var j = joins[joinIdx]
    updateJoin(joinIdx, { on: removeAt(j.on, onIdx) })
  }

  // ── Kolonlar ─────────────────────────────────────────────────────────
  function addColumn(kind) {
    if (kind === 'calc') {
      emit({ columns: columns.concat([{ kind: 'calc', sourceAlias: null, sourceCol: null, expression: '', alias: '' }]) })
    } else {
      var firstAlias = aliases.length > 0 ? aliases[0] : ''
      emit({ columns: columns.concat([{ kind: 'column', sourceAlias: firstAlias, sourceCol: '', expression: null, alias: '' }]) })
    }
  }
  function updateColumn(idx, patch) {
    emit({ columns: updateAt(columns, idx, function (c) { return Object.assign({}, c, patch) }) })
  }
  function removeColumn(idx) {
    emit({ columns: removeAt(columns, idx) })
  }
  function addAllColumnsFor(alias) {
    var cached = colsForAlias(alias)
    if (cached.length === 0) return
    var usedAliases = {}
    columns.forEach(function (c) { if (c.alias) usedAliases[c.alias] = true })
    var additions = []
    cached.forEach(function (col) {
      var already = columns.some(function (c) { return c.kind === 'column' && c.sourceAlias === alias && c.sourceCol === col.name })
      if (already) return
      var outAlias = col.name
      var n = 2
      while (usedAliases[outAlias]) { outAlias = col.name + '_' + alias + (n > 2 ? n : ''); n++ }
      usedAliases[outAlias] = true
      additions.push({ kind: 'column', sourceAlias: alias, sourceCol: col.name, expression: null, alias: outAlias })
    })
    if (additions.length > 0) emit({ columns: columns.concat(additions) })
  }

  var tableOptions = (schemaTables || []).slice().sort(function (a, b) {
    var ka = a.schema + '.' + a.name, kb = b.schema + '.' + b.name
    return ka < kb ? -1 : ka > kb ? 1 : 0
  })
  var baseValue = def.baseObject ? (def.baseObject.schema + '|' + def.baseObject.name) : ''

  return (
    <div>
      {/* ── Temel Tablo / View ─────────────────────────────────────── */}
      <div className="vb-section">
        <div className="vb-section-head">
          <div className="vb-section-title"><Table2 size={14} /> Temel Tablo / View</div>
        </div>
        <div className="vb-field-row">
          <div className="vb-field vb-field-wide">
            <label>Kaynak</label>
            <select className="vb-select" disabled={disabled} value={baseValue}
                    onChange={function (e) { handleBaseObjectSelect(e.target.value) }}>
              <option value="">Seçiniz…</option>
              {tableOptions.map(function (t) {
                var v = t.schema + '|' + t.name
                return <option key={v} value={v}>{t.schema}.{t.name} ({t.kind === 'view' ? 'view' : 'tablo'})</option>
              })}
            </select>
          </div>
          <div className="vb-field vb-field-narrow">
            <label>Takma Ad</label>
            <input className="vb-input vb-input-mono" disabled={disabled || !def.baseObject}
                   value={(def.baseObject && def.baseObject.alias) || ''}
                   onChange={function (e) { handleBaseAliasChange(e.target.value) }} />
          </div>
          {def.baseObject && (
            <button type="button" className="vb-btn vb-btn-sm" disabled={disabled}
                    onClick={function () { addAllColumnsFor(def.baseObject.alias) }}
                    title="Bu kaynağın tüm kolonlarını Kolonlar listesine ekler">
              <Sparkles size={12} /> Tüm Kolonları Getir
            </button>
          )}
        </div>
      </div>

      {/* ── Join'ler ───────────────────────────────────────────────── */}
      <div className="vb-section">
        <div className="vb-section-head">
          <div className="vb-section-title"><GitMerge size={14} /> Join'ler</div>
          <div className="vb-section-help">{joins.length} join</div>
        </div>

        {joins.map(function (j, idx) {
          var tv = j.schema && j.name ? (j.schema + '|' + j.name) : ''
          return (
            <div className="vb-join-card" key={idx}>
              <div className="vb-join-head">
                <div className="vb-field vb-field-narrow">
                  <label>Tip</label>
                  <select className="vb-select" disabled={disabled} value={j.type}
                          onChange={function (e) { updateJoin(idx, { type: e.target.value }) }}>
                    {JOIN_TYPES.map(function (t) { return <option key={t.value} value={t.value}>{t.label}</option> })}
                  </select>
                </div>
                <div className="vb-field vb-field-wide">
                  <label>Tablo / View</label>
                  <select className="vb-select" disabled={disabled} value={tv}
                          onChange={function (e) { handleJoinTableSelect(idx, e.target.value) }}>
                    <option value="">Seçiniz…</option>
                    {tableOptions.map(function (t) {
                      var v = t.schema + '|' + t.name
                      return <option key={v} value={v}>{t.schema}.{t.name} ({t.kind === 'view' ? 'view' : 'tablo'})</option>
                    })}
                  </select>
                </div>
                <div className="vb-field vb-field-narrow">
                  <label>Takma Ad</label>
                  <input className="vb-input vb-input-mono" disabled={disabled} value={j.alias || ''}
                         onChange={function (e) { updateJoin(idx, { alias: e.target.value }) }} />
                </div>
                {j.schema && j.name && (
                  <button type="button" className="vb-btn vb-btn-sm" disabled={disabled}
                          onClick={function () { addAllColumnsFor(j.alias) }}
                          title="Bu join'in tüm kolonlarını Kolonlar listesine ekler">
                    <Sparkles size={12} /> Kolonları Getir
                  </button>
                )}
                <button type="button" className="vb-remove-btn" disabled={disabled}
                        onClick={function () { removeJoin(idx) }} title="Join'i kaldır">
                  <Trash2 size={14} />
                </button>
              </div>

              <div className="vb-on-list">
                {(j.on || []).map(function (o, onIdx) {
                  var leftUnknown = o.leftAlias && !aliasMap[o.leftAlias]
                  var rightUnknown = o.rightAlias && !aliasMap[o.rightAlias]
                  return (
                    <div className="vb-on-row" key={onIdx}>
                      <div className="vb-field vb-field-narrow">
                        <label className={leftUnknown ? 'vb-unknown-alias' : ''}>Sol Alias</label>
                        <select className="vb-select" disabled={disabled} value={o.leftAlias || ''}
                                onChange={function (e) { updateOnCondition(idx, onIdx, { leftAlias: e.target.value, leftCol: '' }) }}>
                          <option value="">Seçiniz…</option>
                          {aliases.map(function (a) { return <option key={a} value={a}>{a}</option> })}
                        </select>
                      </div>
                      <div className="vb-field vb-field-narrow">
                        <label>Sol Kolon</label>
                        <select className="vb-select" disabled={disabled} value={o.leftCol || ''}
                                onChange={function (e) { updateOnCondition(idx, onIdx, { leftCol: e.target.value }) }}>
                          <option value="">Seçiniz…</option>
                          {colsForAlias(o.leftAlias).map(function (c) { return <option key={c.name} value={c.name}>{c.name}</option> })}
                        </select>
                      </div>
                      <div className="vb-field" style={{ minWidth: 70, flex: '0 0 70px' }}>
                        <label>Op</label>
                        <select className="vb-select" disabled={disabled} value={o.op || '='}
                                onChange={function (e) { updateOnCondition(idx, onIdx, { op: e.target.value }) }}>
                          {OP_CHOICES.map(function (op) { return <option key={op} value={op}>{op}</option> })}
                        </select>
                      </div>
                      <div className="vb-field vb-field-narrow">
                        <label className={rightUnknown ? 'vb-unknown-alias' : ''}>Sağ Alias</label>
                        <select className="vb-select" disabled={disabled} value={o.rightAlias || ''}
                                onChange={function (e) { updateOnCondition(idx, onIdx, { rightAlias: e.target.value, rightCol: '' }) }}>
                          <option value="">Seçiniz…</option>
                          {aliases.map(function (a) { return <option key={a} value={a}>{a}</option> })}
                        </select>
                      </div>
                      <div className="vb-field vb-field-narrow">
                        <label>Sağ Kolon</label>
                        <select className="vb-select" disabled={disabled} value={o.rightCol || ''}
                                onChange={function (e) { updateOnCondition(idx, onIdx, { rightCol: e.target.value }) }}>
                          <option value="">Seçiniz…</option>
                          {colsForAlias(o.rightAlias).map(function (c) { return <option key={c.name} value={c.name}>{c.name}</option> })}
                        </select>
                      </div>
                      <button type="button" className="vb-remove-btn" disabled={disabled}
                              onClick={function () { removeOnCondition(idx, onIdx) }} title="Koşulu kaldır">
                        <Trash2 size={13} />
                      </button>
                    </div>
                  )
                })}

                <button type="button" className="vb-add-row vb-add-row-inline" disabled={disabled}
                        onClick={function () { addOnCondition(idx) }}>
                  <Plus size={13} /> ON Koşulu Ekle
                </button>

                {(!j.on || j.on.length === 0) && (
                  <div className="vb-on-warning">
                    <AlertTriangle size={13} /> Bu join için ON koşulu yok — kartezyen çarpım riski.
                  </div>
                )}
              </div>
            </div>
          )
        })}

        <button type="button" className="vb-add-row" disabled={disabled} onClick={addJoin}>
          <Plus size={14} /> Join Ekle
        </button>
      </div>

      {/* ── Kolonlar ───────────────────────────────────────────────── */}
      <div className="vb-section">
        <div className="vb-section-head">
          <div className="vb-section-title"><Columns3 size={14} /> Kolonlar</div>
          <div className="vb-section-help">{columns.length} kolon</div>
        </div>

        {columns.map(function (c, idx) {
          var isCalc = c.kind === 'calc'
          return (
            <div className="vb-column-row" key={idx}>
              <div className="vb-field vb-field-narrow vb-column-kind-badge">
                <label>Tip</label>
                <select className="vb-select" disabled={disabled} value={c.kind}
                        onChange={function (e) {
                          var kind = e.target.value
                          if (kind === 'calc') updateColumn(idx, { kind: 'calc', sourceAlias: null, sourceCol: null })
                          else updateColumn(idx, { kind: 'column', expression: null, sourceAlias: aliases[0] || '' })
                        }}>
                  <option value="column">Kolon</option>
                  <option value="calc">Hesaplanan Alan</option>
                </select>
              </div>

              {!isCalc && (
                <>
                  <div className="vb-field vb-field-narrow">
                    <label>Alias</label>
                    <select className="vb-select" disabled={disabled} value={c.sourceAlias || ''}
                            onChange={function (e) { updateColumn(idx, { sourceAlias: e.target.value, sourceCol: '' }) }}>
                      <option value="">Seçiniz…</option>
                      {aliases.map(function (a) { return <option key={a} value={a}>{a}</option> })}
                    </select>
                  </div>
                  <div className="vb-field vb-field-narrow">
                    <label>Kaynak Kolon</label>
                    <select className="vb-select" disabled={disabled} value={c.sourceCol || ''}
                            onChange={function (e) {
                              var col = e.target.value
                              updateColumn(idx, { sourceCol: col, alias: c.alias || col })
                            }}>
                      <option value="">Seçiniz…</option>
                      {colsForAlias(c.sourceAlias).map(function (col) { return <option key={col.name} value={col.name}>{col.name}</option> })}
                    </select>
                  </div>
                </>
              )}

              {isCalc && (
                <div className="vb-field vb-field-wide">
                  <label>İfade</label>
                  <input className="vb-input vb-input-mono" disabled={disabled}
                         placeholder="örn. base.Miktar * j1.BirimFiyat"
                         value={c.expression || ''}
                         onChange={function (e) { updateColumn(idx, { expression: e.target.value }) }} />
                </div>
              )}

              <div className="vb-field vb-field-narrow">
                <label>Çıkış Adı</label>
                <input className="vb-input vb-input-mono" disabled={disabled} value={c.alias || ''}
                       onChange={function (e) { updateColumn(idx, { alias: e.target.value }) }} />
              </div>

              <button type="button" className="vb-remove-btn" disabled={disabled}
                      onClick={function () { removeColumn(idx) }} title="Kolonu kaldır">
                <Trash2 size={14} />
              </button>
            </div>
          )
        })}

        <div style={{ display: 'flex', gap: 8, marginTop: 6, flexWrap: 'wrap' }}>
          <button type="button" className="vb-add-row" style={{ flex: 1 }} disabled={disabled} onClick={function () { addColumn('column') }}>
            <Plus size={14} /> Kolon Ekle
          </button>
          <button type="button" className="vb-add-row" style={{ flex: 1 }} disabled={disabled} onClick={function () { addColumn('calc') }}>
            <Plus size={14} /> Hesaplanan Alan Ekle
          </button>
        </div>

        {columns.length === 0 && (
          <div className="vb-help-text"><Info size={13} /> En az bir kolon eklemelisiniz.</div>
        )}
        <div className="vb-help-text">
          <Info size={13} />
          Hesaplanan alan ifadelerinde tanımlı takma adları kullanın (örn. <code>base.Miktar * j1.BirimFiyat</code>). Geçersiz bir alias/kolon referansı önizlemede hata olarak görünür.
        </div>
      </div>
    </div>
  )
}
