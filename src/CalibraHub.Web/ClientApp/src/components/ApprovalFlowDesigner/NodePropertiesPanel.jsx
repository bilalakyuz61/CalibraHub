/**
 * ApprovalFlowDesigner — sağ panel.
 *
 *   StepNode      → Adım Adı + Onaylayıcı Türü + (User dropdown / Departman chips)
 *   DecisionNode  → Koşul ifadesi textarea + ipuçları
 *   StartNode/EndNode → sabit, sadece sil butonu
 *   Edge          → label input + edge kind seçimi
 */
import React, { useEffect, useState } from 'react'
import { Trash2, Save, Plus, X } from 'lucide-react'
import { useUpdateNodeData } from './nodeDataContext.js'

/* ── Karar (Decision) node için yapısal koşul satırları ── */
// Belge üst bilgisi + kalem (line-item) alanları. Her alan field code + label + type
// + scope taşır:
//   scope='header'    → belge üst bilgisinden tek deger
//   scope='lineAny'   → en az bir kalem koşulu sağlarsa true (herhangi biri)
//   scope='lineAll'   → tüm kalemler koşulu sağlarsa true
//   scope='lineAgg'   → kalem-agrege metrik (örn: kalem sayısı, kalemlerdeki max tutar)
//
// 2026-05-25 (entity-agnostic plugin): Bu sabit liste artık SADECE FALLBACK'tir —
// gerçek alanlar backend ApprovalEntityTypeRegistry'den (Document/WorkOrder/Item/...)
// `entityTypeFields` prop'u ile gelir. Aşağıdaki Document field'ları geriye dönük
// fallback ve build-time IntelliSense için korunur.
var DECISION_FIELDS = [
  // ── Kullanıcı Bilgileri (belgeyi oluşturan kişi / talep eden) ──
  { code: 'user.departmentId', label: 'Departman',          type: 'lookup',  scope: 'header', groupLabel: 'Kullanıcı Bilgileri', lookupSource: 'departments' },
  { code: 'user.userId',       label: 'Belgeyi Oluşturan',  type: 'lookup',  scope: 'header', groupLabel: 'Kullanıcı Bilgileri', lookupSource: 'users' },
  // ── Belge Üst Bilgileri ──
  { code: 'amount',         label: 'Toplam Tutar',          type: 'numeric', scope: 'header', groupLabel: 'Belge Üst Bilgileri' },
  { code: 'documentDate',   label: 'Belge Tarihi',          type: 'date',    scope: 'header', groupLabel: 'Belge Üst Bilgileri' },
  { code: 'taxNo',          label: 'Tedarikçi/Müşteri VKN', type: 'text',    scope: 'header', groupLabel: 'Belge Üst Bilgileri' },
  { code: 'contactName',    label: 'Tedarikçi/Müşteri Adı', type: 'text',    scope: 'header', groupLabel: 'Belge Üst Bilgileri' },
  // 5 ayrı Cari Grubu kategorisi (card_groups cardType=2)
  { code: 'contactGroup1',  label: 'Cari Grubu 1',          type: 'lookup',  scope: 'header', groupLabel: 'Belge Üst Bilgileri', lookupSource: 'cariGroups[1]' },
  { code: 'contactGroup2',  label: 'Cari Grubu 2',          type: 'lookup',  scope: 'header', groupLabel: 'Belge Üst Bilgileri', lookupSource: 'cariGroups[2]' },
  { code: 'contactGroup3',  label: 'Cari Grubu 3',          type: 'lookup',  scope: 'header', groupLabel: 'Belge Üst Bilgileri', lookupSource: 'cariGroups[3]' },
  { code: 'contactGroup4',  label: 'Cari Grubu 4',          type: 'lookup',  scope: 'header', groupLabel: 'Belge Üst Bilgileri', lookupSource: 'cariGroups[4]' },
  { code: 'contactGroup5',  label: 'Cari Grubu 5',          type: 'lookup',  scope: 'header', groupLabel: 'Belge Üst Bilgileri', lookupSource: 'cariGroups[5]' },
  // ── Belge Kalem Bilgileri — herhangi bir kalem koşulu sağlarsa ──
  { code: 'line.itemCode',     label: 'Stok Kodu',      type: 'text',    scope: 'lineAny', groupLabel: 'Belge Kalem Bilgileri' },
  { code: 'line.itemName',     label: 'Stok Adı',       type: 'text',    scope: 'lineAny', groupLabel: 'Belge Kalem Bilgileri' },
  { code: 'line.quantity',     label: 'Miktar',         type: 'numeric', scope: 'lineAny', groupLabel: 'Belge Kalem Bilgileri' },
  { code: 'line.unitPrice',    label: 'Birim Fiyat',    type: 'numeric', scope: 'lineAny', groupLabel: 'Belge Kalem Bilgileri' },
  { code: 'line.lineTotal',    label: 'Satır Tutarı',   type: 'numeric', scope: 'lineAny', groupLabel: 'Belge Kalem Bilgileri' },
  // 5 ayrı Stok Grubu kategorisi
  { code: 'line.materialGroup1', label: 'Stok Grubu 1', type: 'lookup', scope: 'lineAny', groupLabel: 'Belge Kalem Bilgileri', lookupSource: 'materialGroups[1]' },
  { code: 'line.materialGroup2', label: 'Stok Grubu 2', type: 'lookup', scope: 'lineAny', groupLabel: 'Belge Kalem Bilgileri', lookupSource: 'materialGroups[2]' },
  { code: 'line.materialGroup3', label: 'Stok Grubu 3', type: 'lookup', scope: 'lineAny', groupLabel: 'Belge Kalem Bilgileri', lookupSource: 'materialGroups[3]' },
  { code: 'line.materialGroup4', label: 'Stok Grubu 4', type: 'lookup', scope: 'lineAny', groupLabel: 'Belge Kalem Bilgileri', lookupSource: 'materialGroups[4]' },
  { code: 'line.materialGroup5', label: 'Stok Grubu 5', type: 'lookup', scope: 'lineAny', groupLabel: 'Belge Kalem Bilgileri', lookupSource: 'materialGroups[5]' },
  // Kalem agregeleri — aynı grupta (Belge Kalem Bilgileri)
  { code: 'lineCount',      label: 'Kalem Sayısı (toplam)',     type: 'numeric', scope: 'lineAgg', groupLabel: 'Belge Kalem Bilgileri' },
  { code: 'lineMaxTotal',   label: 'En Büyük Satır Tutarı',     type: 'numeric', scope: 'lineAgg', groupLabel: 'Belge Kalem Bilgileri' },
  { code: 'lineSumQty',     label: 'Toplam Miktar',             type: 'numeric', scope: 'lineAgg', groupLabel: 'Belge Kalem Bilgileri' },
  // ── SQL tabanli kosul (kutuphaneden secim veya adhoc SQL) ──
  { code: 'sql.queryResult', label: 'SQL Sorgu Sonucu',         type: 'sql',     scope: 'sql',     groupLabel: 'SQL Tabanlı Koşul' },
]

// lookupSource string → ilgili array (cariGroups[N] | materialGroups[N] | departments | users)
function resolveLookupSource(lookupSource, ctx) {
  if (!lookupSource || !ctx) return []
  if (lookupSource === 'departments') return Array.isArray(ctx.departments) ? ctx.departments : []
  if (lookupSource === 'users') {
    // UsersDto: {id, name, email} → label "name (email)" formatı
    return Array.isArray(ctx.users) ? ctx.users.map(function (u) {
      return { id: u.id, name: u.name + (u.email ? ' (' + u.email + ')' : '') }
    }) : []
  }
  // Geriye donuk: eski 'cariGroups' (array) hala destekleniyor
  if (lookupSource === 'cariGroups')  return Array.isArray(ctx.cariGroups) ? ctx.cariGroups : []
  var m = lookupSource.match(/^materialGroups\[(\d+)\]$/)
  if (m) {
    var arr = ctx.materialGroups && ctx.materialGroups[m[1]]
    return Array.isArray(arr) ? arr : []
  }
  var m2 = lookupSource.match(/^cariGroups\[(\d+)\]$/)
  if (m2) {
    var arr2 = ctx.cariGroups && ctx.cariGroups[m2[1]]
    return Array.isArray(arr2) ? arr2 : []
  }
  return []
}
var DECISION_OPS_BY_TYPE = {
  numeric: [
    { v: 'eq',  l: '= eşit' },
    { v: 'neq', l: '≠ eşit değil' },
    { v: 'gt',  l: '> büyük' },
    { v: 'gte', l: '≥ büyük eşit' },
    { v: 'lt',  l: '< küçük' },
    { v: 'lte', l: '≤ küçük eşit' },
    { v: 'between', l: 'arasında (min,max)' },
  ],
  text: [
    { v: 'eq',         l: '= eşit' },
    { v: 'neq',        l: '≠ eşit değil' },
    { v: 'contains',   l: 'içerir' },
    { v: 'startswith', l: 'ile başlar' },
  ],
  date: [
    { v: 'eq',     l: '= eşit' },
    { v: 'before', l: 'önce' },
    { v: 'after',  l: 'sonra' },
    { v: 'between',l: 'arasında' },
  ],
  lookup: [
    { v: 'eq', l: '= eşit' },
    { v: 'in', l: 'şu departmanlardan biri' },
  ],
  sql: [
    { v: 'eq',  l: '= eşit' },
    { v: 'neq', l: '≠ eşit değil' },
    { v: 'gt',  l: '> büyük' },
    { v: 'gte', l: '≥ büyük eşit' },
    { v: 'lt',  l: '< küçük' },
    { v: 'lte', l: '≤ küçük eşit' },
  ],
}
// 2026-05-25 (entity-agnostic): findField artık prop ile gelen dinamik field listesi
// kullanır. Liste boşsa eski DOC fallback'ine düşer.
function findField(code, fields) {
  var list = (fields && fields.length > 0) ? fields : DECISION_FIELDS
  return list.find(function (f) { return f.code === code }) || list[0] || DECISION_FIELDS[0]
}
function ruleToExpr(r, departments, fields) {
  if (!r || !r.field) return ''
  var f = findField(r.field, fields)
  // Kapsam etiketi — "Kalem(?):" prefix kalem alanlari icin
  var prefix = ''
  if (f.scope === 'lineAny') prefix = 'Kalem(?): '
  else if (f.scope === 'lineAll') prefix = 'Kalem(∀): '
  // lineAgg ve header icin prefix yok (label kendini anlatir)

  // SQL tabanli kosul — kutuphaneden secilmis veya adhoc
  if (f.type === 'sql') {
    var op2 = (DECISION_OPS_BY_TYPE.sql || []).find(function (o) { return o.v === r.op })
    var opLbl2 = op2 ? op2.l.replace(/^[=≠><≥≤]\s*/, '') : r.op
    var label
    if (r.sqlMode === 'library' && r.sqlQueryId) {
      label = 'SQL[' + (r.sqlQueryName || ('#' + r.sqlQueryId)) + ']'
    } else if (r.sqlMode === 'adhoc' && r.sqlText) {
      label = 'SQL[özel]'
    } else {
      label = 'SQL[?]'
    }
    return label + ' ' + opLbl2 + ' ' + (r.value || '')
  }

  if (f.type === 'lookup' && (r.op === 'eq' || r.op === 'in') && r.value) {
    var ids = String(r.value).split(',').filter(Boolean)
    var names = ids.map(function (id) {
      var d = (departments || []).find(function (x) { return String(x.id) === String(id) })
      return d ? d.name : id
    })
    return prefix + f.label + (r.op === 'in' ? ' ∈ [' : ' = ') + names.join(', ') + (r.op === 'in' ? ']' : '')
  }
  var op = (DECISION_OPS_BY_TYPE[f.type] || []).find(function (o) { return o.v === r.op })
  var opLbl = op ? op.l.replace(/^[=≠><≥≤]\s*/, '') : r.op
  return prefix + f.label + ' ' + opLbl + ' ' + (r.value || '')
}

/* ── SqlConditionEditor — sql tipi alan icin ozel renderer ─────────────
   Mode toggle: Kutuphaneden Sec / Ozel SQL (canUseAdhoc=false ise Ozel SQL gizli).
   Library:  dropdown (sqlQueries) — auto-token parametre kullanir (documentId, contactId, amount, userId vs).
   Adhoc:    textarea (monospace), token destekli {documentId} {contactId} {amount} {userId}.
   Validate butonu — POST /Admin/SqlQueryLibrary/Validate. Backend yoksa toast hatasi sessiz.
*/
function SqlConditionEditor({ rule, sqlQueries, canUseAdhoc, onChange }) {
  var queries = Array.isArray(sqlQueries) ? sqlQueries : []
  var mode = rule.sqlMode || 'library'
  var canAdhoc = canUseAdhoc !== false
  var [validating, setValidating] = useState(false)

  function selectedQuery() {
    if (mode !== 'library' || !rule.sqlQueryId) return null
    return queries.find(function (q) { return String(q.id) === String(rule.sqlQueryId) }) || null
  }

  function handleModeChange(newMode) {
    if (newMode === 'adhoc' && !canAdhoc) return
    onChange({
      sqlMode: newMode,
      sqlQueryId: newMode === 'library' ? rule.sqlQueryId : null,
      sqlQueryName: newMode === 'library' ? rule.sqlQueryName : null,
      sqlText: newMode === 'adhoc' ? (rule.sqlText || '') : null,
    })
  }

  function handleQueryPick(e) {
    var id = e.target.value
    if (!id) {
      onChange({ sqlQueryId: null, sqlQueryName: null })
      return
    }
    var q = queries.find(function (x) { return String(x.id) === String(id) })
    onChange({ sqlQueryId: id ? parseInt(id, 10) : null, sqlQueryName: q ? q.name : null })
  }

  async function handleValidate() {
    var sql = mode === 'adhoc' ? (rule.sqlText || '') : ''
    if (mode === 'library') {
      var q = selectedQuery()
      sql = q ? (q.sqlText || '') : ''
    }
    if (!sql || !sql.trim()) {
      if (window.CalibraHub && window.CalibraHub.toast) window.CalibraHub.toast('Doğrulanacak SQL boş.', 'warn')
      return
    }
    setValidating(true)
    try {
      var token = (document.querySelector('input[name="__RequestVerificationToken"]') || {}).value || ''
      var res = await fetch('/Admin/SqlQueryLibrary/Validate', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token },
        body: JSON.stringify(sql),
      })
      var ct = res.headers.get('content-type') || ''
      if (!res.ok || ct.indexOf('json') < 0) {
        if (window.CalibraHub && window.CalibraHub.toast) {
          window.CalibraHub.toast('Doğrulama servisine ulaşılamadı (backend hazır olmayabilir).', 'err')
        }
        return
      }
      var data = await res.json()
      if (data && data.ok) {
        if (window.CalibraHub && window.CalibraHub.toast) window.CalibraHub.toast('SQL geçerli.', 'ok')
      } else {
        if (window.CalibraHub && window.CalibraHub.toast) window.CalibraHub.toast('SQL hatası: ' + ((data && data.error) || 'bilinmeyen'), 'err')
      }
    } catch (ex) {
      if (window.CalibraHub && window.CalibraHub.toast) window.CalibraHub.toast('Doğrulama hatası: ' + ex.message, 'err')
    } finally {
      setValidating(false)
    }
  }

  return (
    <div style={{ marginTop: 6, display: 'flex', flexDirection: 'column', gap: 6 }}>
      {/* Mode toggle */}
      <div style={{ display: 'flex', gap: 6 }}>
        <button
          type="button"
          className={'afd-props__input' + (mode === 'library' ? ' is-on' : '')}
          style={{
            flex: 1, padding: '6px 8px', cursor: 'pointer', fontSize: '.72rem', fontWeight: 600,
            borderColor: mode === 'library' ? 'var(--afd-accent, #6366f1)' : undefined,
            background: mode === 'library' ? 'rgba(99,102,241,.12)' : undefined,
          }}
          onClick={function () { handleModeChange('library') }}
        >
          Kütüphaneden Seç
        </button>
        {canAdhoc && (
          <button
            type="button"
            className={'afd-props__input' + (mode === 'adhoc' ? ' is-on' : '')}
            style={{
              flex: 1, padding: '6px 8px', cursor: 'pointer', fontSize: '.72rem', fontWeight: 600,
              borderColor: mode === 'adhoc' ? 'var(--afd-accent, #6366f1)' : undefined,
              background: mode === 'adhoc' ? 'rgba(99,102,241,.12)' : undefined,
            }}
            onClick={function () { handleModeChange('adhoc') }}
          >
            Özel SQL
          </button>
        )}
      </div>

      {/* Library mode */}
      {mode === 'library' && (
        <>
          <select
            className="afd-props__input"
            value={rule.sqlQueryId == null ? '' : String(rule.sqlQueryId)}
            onChange={handleQueryPick}
          >
            <option value="">— Sorgu seçin —</option>
            {queries.length === 0 && (
              <option value="" disabled>Kütüphanede sorgu yok</option>
            )}
            {queries.map(function (q) {
              return <option key={q.id} value={q.id}>{q.name}{q.description ? ' — ' + q.description : ''}</option>
            })}
          </select>
          {selectedQuery() && (
            <div className="afd-props__hint" style={{ marginTop: 0, fontSize: '.7rem' }}>
              <strong>Parametreler:</strong>{' '}
              {(function () {
                var q = selectedQuery()
                if (!q || !q.parameters) return 'otomatik (documentId, contactId, amount, userId)'
                var parsed
                try { parsed = typeof q.parameters === 'string' ? JSON.parse(q.parameters) : q.parameters }
                catch (e) { parsed = null }
                if (!Array.isArray(parsed) || parsed.length === 0) return 'otomatik'
                return parsed.map(function (p) { return p.name + ':' + p.type }).join(', ')
              })()}
              <div style={{ marginTop: 4, opacity: .85 }}>
                Parametreler runtime'da belgenin alanlarından otomatik bind edilir.
              </div>
            </div>
          )}
        </>
      )}

      {/* Adhoc mode */}
      {mode === 'adhoc' && (
        <>
          <textarea
            className="afd-props__input afd-props__input--ta"
            rows={5}
            style={{ fontFamily: 'ui-monospace, Menlo, Consolas, monospace', fontSize: '.78rem' }}
            value={rule.sqlText || ''}
            onChange={function (e) { onChange({ sqlText: e.target.value }) }}
            placeholder={"SELECT COUNT(*) FROM dbo.SalesOrder WHERE ContactId = {contactId} AND IsActive = 1"}
          />
          <div className="afd-props__hint" style={{ marginTop: 0, fontSize: '.7rem' }}>
            <strong>Token'lar:</strong> <code>{'{documentId}'}</code> <code>{'{contactId}'}</code>{' '}
            <code>{'{amount}'}</code> <code>{'{userId}'}</code>
          </div>
        </>
      )}

      <button
        type="button"
        className="afd-props__input"
        style={{ cursor: 'pointer', padding: '6px 8px', fontSize: '.72rem', fontWeight: 600 }}
        disabled={validating}
        onClick={handleValidate}
      >
        {validating ? 'Doğrulanıyor…' : 'Doğrula'}
      </button>
    </div>
  )
}

// Tek koşul satırı: [Alan] [Op] [Değer] [✕]
function DecisionRuleRow({ rule, fields, users, departments, cariGroups, materialGroups, sqlQueries, canUseAdhoc, onChange, onRemove }) {
  var availableFields = (fields && fields.length > 0) ? fields : DECISION_FIELDS
  var fld = findField(rule.field, availableFields)
  var ops = DECISION_OPS_BY_TYPE[fld.type] || []
  var op = rule.op || ops[0].v
  var isBetween = op === 'between'
  var lookupCtx = { users: users, departments: departments, cariGroups: cariGroups, materialGroups: materialGroups }

  function renderValueInput() {
    if (fld.type === 'sql') {
      return (
        <>
          <SqlConditionEditor
            rule={rule}
            sqlQueries={sqlQueries}
            canUseAdhoc={canUseAdhoc}
            onChange={onChange}
          />
          <input
            type="number"
            className="afd-props__input"
            style={{ marginTop: 4 }}
            value={rule.value || ''}
            placeholder="Karşılaştırma değeri (sayısal)"
            onChange={function (e) { onChange({ value: e.target.value }) }}
          />
        </>
      )
    }
    if (fld.type === 'lookup') {
      // lookupSource'a göre uygun listeyi al (Cari Grubu / Departman / Stok Grubu kategorisi)
      var options = resolveLookupSource(fld.lookupSource, lookupCtx)
      var selectedIds = String(rule.value || '')
        .split(',').map(function (x) { return x.trim() }).filter(Boolean)
      return (
        <div className="afd-props__chips" style={{ marginTop: 6 }}>
          {options.length === 0 && (
            <span className="afd-props__hint-italic">Aktif tanım yok</span>
          )}
          {options.map(function (d) {
            var on = selectedIds.indexOf(String(d.id)) !== -1
            return (
              <label key={d.id} className={'afd-props__chip' + (on ? ' is-on' : '')}>
                <input
                  type="checkbox"
                  checked={on}
                  onChange={function (e) {
                    var ids = selectedIds.slice()
                    if (e.target.checked) {
                      if (ids.indexOf(String(d.id)) === -1) ids.push(String(d.id))
                    } else {
                      ids = ids.filter(function (x) { return x !== String(d.id) })
                    }
                    onChange({ value: ids.join(',') })
                  }}
                />
                <span>{d.name}</span>
              </label>
            )
          })}
        </div>
      )
    }
    var inputType = fld.type === 'numeric' ? 'number'
                  : fld.type === 'date' ? 'date'
                  : 'text'
    if (isBetween) {
      var parts = String(rule.value || '').split(',')
      return (
        <div style={{ display: 'flex', gap: 4, marginTop: 4 }}>
          <input
            type={inputType}
            className="afd-props__input"
            style={{ flex: 1, minWidth: 0 }}
            value={parts[0] || ''}
            placeholder="Min"
            onChange={function (e) { onChange({ value: e.target.value + ',' + (parts[1] || '') }) }}
          />
          <input
            type={inputType}
            className="afd-props__input"
            style={{ flex: 1, minWidth: 0 }}
            value={parts[1] || ''}
            placeholder="Max"
            onChange={function (e) { onChange({ value: (parts[0] || '') + ',' + e.target.value }) }}
          />
        </div>
      )
    }
    return (
      <input
        type={inputType}
        className="afd-props__input"
        style={{ marginTop: 4 }}
        value={rule.value || ''}
        placeholder="Değer"
        onChange={function (e) { onChange({ value: e.target.value }) }}
      />
    )
  }

  return (
    <div className="afd-rule-row">
      <div className="afd-rule-row__top">
        <select
          className="afd-rule-row__type"
          value={rule.field}
          onChange={function (e) {
            // alan değişince op'u uyumla, value'yu sıfırla
            var newFld = findField(e.target.value, availableFields)
            var newOps = DECISION_OPS_BY_TYPE[newFld.type] || []
            onChange({ field: e.target.value, op: (newOps[0] && newOps[0].v) || 'eq', value: '' })
          }}>
          {(function () {
            // Field'lari groupLabel'a gore optgroup'la
            var groups = {}
            var order = []
            availableFields.forEach(function (f) {
              var g = f.groupLabel || 'Alanlar'
              if (!groups[g]) { groups[g] = []; order.push(g) }
              groups[g].push(f)
            })
            return order.map(function (g) {
              return (
                <optgroup key={g} label={g}>
                  {groups[g].map(function (f) {
                    return <option key={f.code} value={f.code}>{f.label}</option>
                  })}
                </optgroup>
              )
            })
          })()}
        </select>
        <select
          className="afd-rule-row__type"
          style={{ flex: '0 0 140px' }}
          value={op}
          onChange={function (e) { onChange({ op: e.target.value, value: '' }) }}>
          {ops.map(function (o) {
            return <option key={o.v} value={o.v}>{o.l}</option>
          })}
        </select>
        <button
          type="button"
          className="afd-props__del"
          onClick={onRemove}
          title="Koşulu kaldır">
          <X size={12} />
        </button>
      </div>
      {renderValueInput()}
    </div>
  )
}

/* ── Start node tetikleme koşulları için kural tipleri ── */
var RULE_TYPES = [
  { v: 'Always',      l: 'Her Zaman (koşulsuz)' },
  { v: 'MinAmount',   l: 'Min Tutar (≥)' },
  { v: 'MaxAmount',   l: 'Maks Tutar (≤)' },
  { v: 'AmountRange', l: 'Tutar Aralığı' },
  { v: 'SenderTaxNo', l: 'VKN (gönderici)' },
  { v: 'Department',  l: 'Departman' },
]

function rulePlaceholder(type) {
  return type === 'AmountRange' ? 'min,max (örn: 10000,50000)'
       : type === 'SenderTaxNo' ? 'VKN (10 hane)'
       : type === 'Always'      ? ''
       : 'Tutar (TL)'
}

/* ── Bildirim node token listesi — entityTypeFields'den türetilir ── */
var NOTIF_SYSTEM_TOKENS = [
  { key: 'flowName',        label: 'Akış Adı' },
  { key: 'currentStepName', label: 'Güncel Adım' },
  { key: 'requesterName',   label: 'Oluşturan' },
  { key: 'approveLink',     label: 'Onay Linki' },
  { key: 'rejectLink',      label: 'Red Linki' },
  // Mail'de stilli Onayla/Reddet butonları, WhatsApp'ta emoji'li link satırları üretir.
  { key: 'approvalButtons', label: 'Onay/Red Butonları' },
]
function buildNotifTokens(entityTypeFields) {
  var entityTokens = (Array.isArray(entityTypeFields) ? entityTypeFields : [])
    .filter(function (f) { return f && f.scope === 'header' && f.type !== 'lookup' && f.type !== 'sql' })
    .map(function (f) { return { key: f.code, label: f.label } })
  var covered = entityTokens.reduce(function (s, t) { s[t.key] = true; return s }, {})
  var extra = NOTIF_SYSTEM_TOKENS.filter(function (t) { return !covered[t.key] })
  return entityTokens.concat(extra)
}

export default function NodePropertiesPanel({
  selected,        // { kind:'node'|'edge', id, data, type? }
  edges,           // canvas edge listesi — SLA hedef (timeout edge) tespiti icin
  nodes,           // canvas node listesi — SLA hedef adi icin
  users,
  departments,
  cariGroups,      // [{id, name}] — Karar koşulları için
  materialGroups,  // { "1": [{id, name}], "2": [...], ... "5": [...] } — Karar koşulları için
  sqlQueries,      // [{id, name, description, sqlText, parameters, resultType}] — Karar SQL koşulu için
  integrations,    // [{id, name, sourceFormCode, sourceFormLabel, endpointName, hasEndpoint, isActive}] — Entegrasyon node için
  entityTypeFields, // entity-agnostic plugin: aktif entity tipinin field listesi (backend registry'den)
  entityTypeCode,   // aktif entity type code (Document/WorkOrder/Item/Contact/ProductionRecord)
  canUseAdhoc,     // bool — adhoc SQL textarea kullanim izni (ileride rol bazli)
  rules,           // Start node için: [{ id, ruleType, ruleValue, isActive }]
  onRulesChange,   // (newRules) => void
  variables,       // Surec-scoped degisken tanim listesi: [{ name, typeCode, defaultValue, description }]
  onChange,        // (newData) => void
  onDelete,        // () => void
}) {
  // entityTypeFields prop'u boş gelirse Document fallback (DECISION_FIELDS) kullanılır.
  var availableFields = (Array.isArray(entityTypeFields) && entityTypeFields.length > 0)
    ? entityTypeFields : DECISION_FIELDS
  // Edge panel'inde Gecikme edge'i secildiginde kaynak adimin SLA verisini guncellemek icin.
  var updateNodeData = useUpdateNodeData()
  // Form state'i seçim değişince re-init et
  var [stepName, setStepName] = useState('')
  var [approverType, setApproverType] = useState('AnyUser')
  var [approverId, setApproverId] = useState('')
  var [approverLabel, setApproverLabel] = useState('')
  var [condition, setCondition] = useState('')
  var [edgeLabel, setEdgeLabel] = useState('')
  var [edgeKind, setEdgeKind] = useState('default')
  // Notification @ token picker
  var [tokenPicker, setTokenPicker] = useState({ open: false, field: null, atPos: -1, filter: '' })
  var [tokenPickerIdx, setTokenPickerIdx] = useState(0)
  var bodyRef    = React.useRef(null)
  var subjectRef = React.useRef(null)

  useEffect(function () {
    if (!selected) return
    var d = selected.data || {}
    if (selected.kind === 'node') {
      setStepName(d.stepName || '')
      setApproverType(d.approverType || 'AnyUser')
      setApproverId(d.approverId == null ? '' : String(d.approverId))
      setApproverLabel(d.approverLabel || '')
      setCondition(d.condition || '')
    } else if (selected.kind === 'edge') {
      setEdgeLabel(selected.label || d.label || '')
      setEdgeKind(d.edgeKind || 'default')
      setCondition(d.condition || '')
    }
  }, [selected])

  if (!selected) {
    return null
  }

  /* ── helpers ── */
  function commitNode(patch) {
    if (typeof onChange === 'function') onChange(patch)
  }

  /* ── Notification @ token picker helpers ── */
  function handleTokenInput(e, fieldName, commitFn) {
    var val = e.target.value
    var pos = e.target.selectionStart
    var search = val.slice(0, pos)
    var atIdx = search.lastIndexOf('@')
    if (atIdx !== -1 && !/\s/.test(search.slice(atIdx + 1))) {
      var filter = search.slice(atIdx + 1).toLowerCase()
      setTokenPicker({ open: true, field: fieldName, atPos: atIdx, filter: filter })
      setTokenPickerIdx(0)
      commitFn(val)
      return
    }
    if (tokenPicker.open) setTokenPicker({ open: false, field: null, atPos: -1, filter: '' })
    commitFn(val)
  }
  function insertToken(token, currentVal, commitFn, ref) {
    var before = currentVal.slice(0, tokenPicker.atPos)
    var after  = currentVal.slice(tokenPicker.atPos + 1 + tokenPicker.filter.length)
    var newVal = before + '{' + token.key + '}' + after
    commitFn(newVal)
    setTokenPicker({ open: false, field: null, atPos: -1, filter: '' })
    var np = tokenPicker.atPos + token.key.length + 2
    if (ref && ref.current) setTimeout(function () {
      if (!ref.current) return
      ref.current.focus()
      ref.current.setSelectionRange(np, np)
    }, 20)
  }
  function filteredTokens() {
    var all = buildNotifTokens(entityTypeFields)
    var f = tokenPicker.filter
    if (!f) return all
    return all.filter(function (t) {
      return t.label.toLowerCase().indexOf(f) !== -1 || t.key.indexOf(f) !== -1
    })
  }
  function closeTokenPicker() {
    setTimeout(function () { setTokenPicker({ open: false, field: null, atPos: -1, filter: '' }) }, 150)
  }
  function tokenKeyDown(e, currentVal, commitFn, ref) {
    if (!tokenPicker.open) return
    var ft = filteredTokens()
    if (e.key === 'ArrowDown') { e.preventDefault(); setTokenPickerIdx(function (i) { return Math.min(i + 1, ft.length - 1) }) }
    else if (e.key === 'ArrowUp') { e.preventDefault(); setTokenPickerIdx(function (i) { return Math.max(i - 1, 0) }) }
    else if (e.key === 'Enter' && ft.length > 0) { e.preventDefault(); insertToken(ft[tokenPickerIdx], currentVal, commitFn, ref) }
    else if (e.key === 'Escape') { e.preventDefault(); setTokenPicker({ open: false, field: null, atPos: -1, filter: '' }) }
  }

  /* ── STEP node ── */
  if (selected.kind === 'node' && selected.type === 'step') {
    return (
      <div className="afd-props">
        <div className="afd-props__head">
          <span className="afd-props__head-badge afd-props__head-badge--step">Adım</span>
          <button className="afd-props__del" onClick={onDelete} title="Sil">
            <Trash2 size={14} />
          </button>
        </div>

        <label className="afd-props__label">Adım Adı</label>
        <input
          className="afd-props__input"
          value={stepName}
          onChange={function (e) {
            setStepName(e.target.value)
            commitNode({ stepName: e.target.value })
          }}
          placeholder="Örn: Müdür Onayı"
        />

        <label className="afd-props__label">Onaylayıcı Türü</label>
        <select
          className="afd-props__input"
          value={approverType}
          onChange={function (e) {
            var v = e.target.value
            setApproverType(v)
            setApproverId(''); setApproverLabel('')
            commitNode({ approverType: v, approverId: null, approverLabel: null })
          }}
        >
          <option value="AnyUser">Herhangi Kullanıcı</option>
          <option value="SpecificUser">Belirli Kullanıcı</option>
          <option value="Department">Departman</option>
          <option value="ManagerOfRequester">Kişinin Amiri</option>
        </select>

        {approverType === 'SpecificUser' && (
          <>
            <label className="afd-props__label">Kullanıcı</label>
            <select
              className="afd-props__input"
              value={approverId}
              onChange={function (e) {
                var v = e.target.value
                var opt = e.target.options[e.target.selectedIndex]
                var lbl = opt && opt.value ? opt.textContent : null
                setApproverId(v); setApproverLabel(lbl)
                commitNode({ approverId: v || null, approverLabel: lbl })
              }}
            >
              <option value="">— Kullanıcı seçin —</option>
              {users.map(function (u) {
                return (
                  <option key={u.id} value={u.id}>
                    {u.name}{u.email ? ' (' + u.email + ')' : ''}
                  </option>
                )
              })}
            </select>
          </>
        )}

        {approverType === 'Department' && (
          <>
            <label className="afd-props__label">Departman(lar)</label>
            <div className="afd-props__chips">
              {departments.length === 0 && (
                <span className="afd-props__hint-italic">
                  Aktif departman tanımı yok.
                </span>
              )}
              {departments.map(function (d) {
                var selectedIds = String(approverId || '')
                  .split(',').map(function (x) { return x.trim() }).filter(Boolean)
                var on = selectedIds.indexOf(String(d.id)) !== -1
                return (
                  <label
                    key={d.id}
                    className={'afd-props__chip' + (on ? ' is-on' : '')}
                  >
                    <input
                      type="checkbox"
                      checked={on}
                      onChange={function (e) {
                        var ids = selectedIds.slice()
                        if (e.target.checked) {
                          if (ids.indexOf(String(d.id)) === -1) ids.push(String(d.id))
                        } else {
                          ids = ids.filter(function (x) { return x !== String(d.id) })
                        }
                        var newId = ids.join(',')
                        var newLabel = departments
                          .filter(function (x) { return ids.indexOf(String(x.id)) !== -1 })
                          .map(function (x) { return x.name })
                          .join(', ')
                        setApproverId(newId); setApproverLabel(newLabel)
                        commitNode({
                          approverId: newId || null,
                          approverLabel: newLabel || null,
                        })
                      }}
                    />
                    <span>{d.name}</span>
                  </label>
                )
              })}
            </div>
          </>
        )}

        {/* SLA/Gecikme yonetimi tamamen edge paneline tasindi (Gecikme edge'i
            secildiginde sure/uyari/mesaj orada). Adim paneli yalnizca onaylayici
            kismi ile ilgilenir. */}
        <ExtraInputsToggleBlock node={selected} onChange={commitNode} />
      </div>
    )
  }

  /* ── DECISION node ── */
  // 2026-05-25: Karar = yapısal koşul satırları (Alan / Operatör / Değer). Free-form
  // ifade yerine belge üst bilgileri üzerinde kolay seçim. Onaylayıcı YOK — koşul
  // sistem tarafından otomatik değerlendirilir, sonraki Adım node'larında insan onayı alınır.
  if (selected.kind === 'node' && selected.type === 'decision') {
    var decisionData = selected.data || {}
    var cRules = Array.isArray(decisionData.conditionRules) ? decisionData.conditionRules : []

    // Surec degiskenleri Karar koşul alanları olarak da seçilebilir.
    // field code: "var:<ad>" — runtime executor "var:" prefix'i ile per-instance
    // variable degerini cozer.
    function varTypeToFieldType(tc) {
      if (tc === 'int' || tc === 'decimal') return 'numeric'
      if (tc === 'date') return 'date'
      return 'text' // bool + string → text op'lari kullan (eq/neq/contains)
    }
    var variableFields = (Array.isArray(variables) ? variables : []).map(function (v) {
      // Karar koşulunda kullanıcıya sadece okunabilir ETIKET göster (description).
      // Description yoksa name'e (tekn. ad) düş. Tooltip'te ham ad da görünür.
      return {
        code: 'var:' + v.name,
        label: v.description || v.name,
        title: v.name,
        type: varTypeToFieldType(v.typeCode),
        scope: 'variable',
        groupLabel: 'Süreç Değişkenleri',
      }
    })
    var decisionFields = variableFields.length > 0
      ? availableFields.concat(variableFields)
      : availableFields

    function commitDecisionRules(newRules) {
      // condition display text de generate et (geri-uyumluluk + debug)
      var exprText = newRules
        .filter(function (r) { return r && r.field && r.op })
        .map(function (r) { return ruleToExpr(r, departments, decisionFields) })
        .filter(Boolean)
        .join(' AND ')
      commitNode({ conditionRules: newRules, condition: exprText || null })
    }

    function addDecisionRule() {
      // Entity tipine göre varsayılan ilk alan + op seç (Document'ta amount/gt,
      // diğer tiplerde listenin ilk field'ı + ilk uygun operatör).
      var defField = decisionFields[0] || { code: 'amount', type: 'numeric' }
      var defOps = DECISION_OPS_BY_TYPE[defField.type] || []
      var next = cRules.slice()
      next.push({ field: defField.code, op: (defOps[0] && defOps[0].v) || 'eq', value: '' })
      commitDecisionRules(next)
    }
    function updateDecisionRule(idx, patch) {
      var next = cRules.map(function (r, i) { return i === idx ? Object.assign({}, r, patch) : r })
      commitDecisionRules(next)
    }
    function removeDecisionRule(idx) {
      var next = cRules.filter(function (_, i) { return i !== idx })
      commitDecisionRules(next)
    }

    return (
      <div className="afd-props">
        <div className="afd-props__head">
          <span className="afd-props__head-badge afd-props__head-badge--decision">Karar</span>
          <button className="afd-props__del" onClick={onDelete} title="Sil">
            <Trash2 size={14} />
          </button>
        </div>

        <label className="afd-props__label">Koşullar (VE)</label>
        {cRules.length === 0 && (
          <div className="afd-props__hint" style={{ marginTop: 0 }}>
            Henüz koşul yok. Her zaman <em>Evet</em> dalı işlenir.
          </div>
        )}
        {cRules.map(function (r, idx) {
          return (
            <DecisionRuleRow
              key={idx}
              rule={r}
              fields={decisionFields}
              users={users}
              departments={departments}
              cariGroups={cariGroups}
              materialGroups={materialGroups}
              sqlQueries={sqlQueries}
              canUseAdhoc={canUseAdhoc}
              onChange={function (patch) { updateDecisionRule(idx, patch) }}
              onRemove={function () { removeDecisionRule(idx) }}
            />
          )
        })}
        <button
          type="button"
          className="afd-rule-add"
          onClick={addDecisionRule}
          style={{ marginTop: 6 }}>
          <Plus size={12} /> Koşul Ekle
        </button>

        <div className="afd-props__hint">
          Tüm koşullar sağlanırsa <em>Evet</em> kolu, aksi halde <em>Hayır</em> kolu işlenir.
          Her dal sonraki <em>Adım</em> node'una yönlendirilir; onay/red kararını orada atanan kullanıcı verir.
        </div>
      </div>
    )
  }

  /* ── PARALLEL node ── Split + Join gateway (BPMN). Sadece bilgi paneli, ayar gerektirmez */
  if (selected.kind === 'node' && selected.type === 'parallel') {
    return (
      <div className="afd-props">
        <div className="afd-props__head">
          <span className="afd-props__head-badge" style={{ background: 'rgba(139,92,246,.14)', color: '#6d28d9' }}>Paralel</span>
          <button className="afd-props__del" onClick={onDelete} title="Sil">
            <Trash2 size={14} />
          </button>
        </div>

        <div className="afd-props__hint">
          <strong>Paralel Kapı (Split &amp; Join)</strong> — BPMN paralel gateway.
          <ul>
            <li><strong>Split</strong>: Bir giriş, çoklu çıkış. Tüm dallar AYNI ANDA başlar.</li>
            <li><strong>Join</strong>: Çoklu giriş, bir çıkış. TÜM dallar tamamlanınca devam.</li>
          </ul>
          Topolojiyi runtime executor çıkarır — manuel mod seçimi gerekmez.
          <br/><br/>
          <strong>Kullanım:</strong> "Belge farklı stok gruplarına ait kalemler içeriyor → ilgili her grup
          sahibi paralel onaylar" gibi senaryolarda. Paralel Split sonrası her dala bir Adım node'u
          bağlayın, sonra Paralel Join'de birleştirip Bitir'e gidin.
        </div>
      </div>
    )
  }

  /* ── NOTIFICATION node ── Mail / WhatsApp / Both gönderir, akış akmaya devam eder */
  if (selected.kind === 'node' && selected.type === 'notification') {
    var nData = selected.data || {}
    var notifyType    = nData.notificationType || 'mail'
    var recipientMode = nData.recipientMode || 'creator'
    var subject       = nData.subject || ''
    var body          = nData.body || ''
    var recipientId   = nData.recipientId == null ? '' : String(nData.recipientId)
    var customEmail   = nData.customEmail || ''
    var customPhone   = nData.customPhone || ''
    var attachPdf     = nData.attachPdf === true
    var ftokens       = filteredTokens()

    function commitN(patch) { commitNode(patch) }

    return (
      <div className="afd-props">
        <div className="afd-props__head">
          <span className="afd-props__head-badge" style={{ background: 'rgba(6,182,212,.14)', color: '#0e7490' }}>Bildirim</span>
          <button className="afd-props__del" onClick={onDelete} title="Sil">
            <Trash2 size={14} />
          </button>
        </div>

        {/* Tür */}
        <div className="afd-props__row">
          <span className="afd-props__row-lbl">Tür</span>
          <select className="afd-props__input" value={notifyType}
                  onChange={function (e) { commitN({ notificationType: e.target.value }) }}>
            <option value="mail">📧 Mail</option>
            <option value="whatsapp">💬 WhatsApp</option>
            <option value="both">📧 + 💬 Mail + WhatsApp</option>
          </select>
        </div>

        {/* Alıcı */}
        <div className="afd-props__row">
          <span className="afd-props__row-lbl">Alıcı</span>
          <select className="afd-props__input" value={recipientMode}
                  onChange={function (e) {
                    commitN({ recipientMode: e.target.value, recipientId: null, recipientLabel: null,
                              customEmail: null, customPhone: null })
                  }}>
            <option value="creator">Belgeyi Oluşturan</option>
            <option value="managerofcreator">Kişinin Amiri</option>
            <option value="approver">Önceki Adımın Onaylayıcısı</option>
            <option value="specificUser">Belirli Kullanıcı</option>
            <option value="department">Departman</option>
            <option value="custom">Manuel (mail / telefon)</option>
          </select>
        </div>

        {recipientMode === 'specificUser' && (
          <div className="afd-props__row">
            <span className="afd-props__row-lbl">Kullanıcı</span>
            <select className="afd-props__input" value={recipientId}
                    onChange={function (e) {
                      var opt = e.target.options[e.target.selectedIndex]
                      var lbl = opt && opt.value ? opt.textContent : null
                      commitN({ recipientId: e.target.value || null, recipientLabel: lbl })
                    }}>
              <option value="">— Seçin —</option>
              {(users || []).map(function (u) {
                return <option key={u.id} value={u.id}>{u.name}{u.email ? ' (' + u.email + ')' : ''}</option>
              })}
            </select>
          </div>
        )}

        {recipientMode === 'department' && (
          <div className="afd-props__row afd-props__row--top">
            <span className="afd-props__row-lbl" style={{ paddingTop: 4 }}>Dept.</span>
            <div className="afd-props__chips" style={{ flex: 1, margin: 0 }}>
              {(departments || []).length === 0 && (
                <span className="afd-props__hint-italic">Aktif departman yok</span>
              )}
              {(departments || []).map(function (d) {
                var selectedIds = String(recipientId || '').split(',').map(function (x) { return x.trim() }).filter(Boolean)
                var on = selectedIds.indexOf(String(d.id)) !== -1
                return (
                  <label key={d.id} className={'afd-props__chip' + (on ? ' is-on' : '')}>
                    <input type="checkbox" checked={on} onChange={function (e) {
                      var ids = selectedIds.slice()
                      if (e.target.checked) { if (ids.indexOf(String(d.id)) === -1) ids.push(String(d.id)) }
                      else { ids = ids.filter(function (x) { return x !== String(d.id) }) }
                      var nid = ids.join(',')
                      var nlbl = (departments || []).filter(function (x) { return ids.indexOf(String(x.id)) !== -1 })
                        .map(function (x) { return x.name }).join(', ')
                      commitN({ recipientId: nid || null, recipientLabel: nlbl || null })
                    }} />
                    <span>{d.name}</span>
                  </label>
                )
              })}
            </div>
          </div>
        )}

        {recipientMode === 'custom' && (notifyType === 'mail' || notifyType === 'both') && (
          <div className="afd-props__row">
            <span className="afd-props__row-lbl">E-posta</span>
            <input className="afd-props__input" type="email" value={customEmail}
                   onChange={function (e) { commitN({ customEmail: e.target.value }) }}
                   placeholder="ornek@firma.com" />
          </div>
        )}
        {recipientMode === 'custom' && (notifyType === 'whatsapp' || notifyType === 'both') && (
          <div className="afd-props__row">
            <span className="afd-props__row-lbl">Telefon</span>
            <input className="afd-props__input" type="tel" value={customPhone}
                   onChange={function (e) { commitN({ customPhone: e.target.value }) }}
                   placeholder="+90 5XX XXX XX XX" />
          </div>
        )}

        {/* Konu — sadece mail */}
        {(notifyType === 'mail' || notifyType === 'both') && (
          <div className="afd-props__row">
            <span className="afd-props__row-lbl">Konu</span>
            <div style={{ flex: 1, position: 'relative' }}>
              <input ref={subjectRef} className="afd-props__input" value={subject}
                     onChange={function (e) { handleTokenInput(e, 'subject', function (v) { commitN({ subject: v }) }) }}
                     onKeyDown={function (e) { tokenKeyDown(e, subject, function (v) { commitN({ subject: v }) }, subjectRef) }}
                     onBlur={closeTokenPicker}
                     placeholder="Onay bekliyor: @belge" />
              {tokenPicker.open && tokenPicker.field === 'subject' && ftokens.length > 0 && (
                <div className="afd-token-picker">
                  {ftokens.map(function (t, i) {
                    return (
                      <div key={t.key}
                           className={'afd-token-picker__item' + (i === tokenPickerIdx ? ' is-active' : '')}
                           onMouseDown={function (e) { e.preventDefault(); insertToken(t, subject, function (v) { commitN({ subject: v }) }, subjectRef) }}>
                        <span>{t.label}</span>
                        <span className="afd-token-picker__key">{'{' + t.key + '}'}</span>
                      </div>
                    )
                  })}
                </div>
              )}
            </div>
          </div>
        )}

        {/* Mesaj — @ ile token */}
        <div className="afd-props__row afd-props__row--top">
          <span className="afd-props__row-lbl" style={{ paddingTop: 8 }}>Mesaj</span>
          <div style={{ flex: 1, position: 'relative' }}>
            <textarea ref={bodyRef} className="afd-props__input afd-props__input--ta" rows={5} value={body}
                      onChange={function (e) { handleTokenInput(e, 'body', function (v) { commitN({ body: v }) }) }}
                      onKeyDown={function (e) { tokenKeyDown(e, body, function (v) { commitN({ body: v }) }, bodyRef) }}
                      onBlur={closeTokenPicker}
                      placeholder={'Merhaba,\n\n@ yazarak token ekleyin.\nÖrn: @belge → {documentNumber}'} />
            {tokenPicker.open && tokenPicker.field === 'body' && ftokens.length > 0 && (
              <div className="afd-token-picker">
                {ftokens.map(function (t, i) {
                  return (
                    <div key={t.key}
                         className={'afd-token-picker__item' + (i === tokenPickerIdx ? ' is-active' : '')}
                         onMouseDown={function (e) { e.preventDefault(); insertToken(t, body, function (v) { commitN({ body: v }) }, bodyRef) }}>
                      <span>{t.label}</span>
                      <span className="afd-token-picker__key">{'{' + t.key + '}'}</span>
                    </div>
                  )
                })}
              </div>
            )}
          </div>
        </div>

        <label className="afd-switch" style={{ marginTop: 10 }}>
          <input type="checkbox" checked={attachPdf}
                 onChange={function (e) { commitN({ attachPdf: e.target.checked }) }} />
          <span className="afd-switch__track"></span>
          <span style={{ marginLeft: 8, fontSize: '.85rem' }}>📎 Belge PDF'ini ekle</span>
        </label>
        {attachPdf && (
          <div className="afd-props__hint" style={{ marginTop: 6 }}>
            Belgenin PDF'i mevcut <strong>Doküman Dizayn Kuralları</strong> (DocLayoutRule) kullanılarak otomatik
            render edilir. (Tasarım → Doküman Tasarım Kuralları)
          </div>
        )}

        <div className="afd-props__hint" style={{ marginTop: 8 }}>
          <strong>@ token:</strong> Mesaj veya Konu alanında <code>@</code> yazın — Türkçe ismiyle seçin, token otomatik eklenir.
          Bildirim node'u akışı durdurmaz.
        </div>
      </div>
    )
  }

  /* ── INTEGRATION node ── Mevcut Integration tanımını tetikler (örn. satış
     siparişi onaylandıktan sonra ERP'ye aktar). Fire-and-forget mantığı —
     haltOnError işaretliyse hata akışı durdurur, aksi halde sonraki adıma geçer. */
  if (selected.kind === 'node' && selected.type === 'integration') {
    var iData = selected.data || {}
    var iId = iData.integrationId == null ? '' : String(iData.integrationId)
    var iRecSrc = iData.recordIdSource || 'entity'
    var iCustomRec = iData.customRecordId || ''
    var iHalt = iData.haltOnError !== false
    var integList = Array.isArray(integrations) ? integrations : []

    function commitI(patch) { commitNode(patch) }

    return (
      <div className="afd-props">
        <div className="afd-props__head">
          <span className="afd-props__head-badge" style={{ background: 'rgba(139,92,246,.14)', color: '#6d28d9' }}>Entegrasyon</span>
          <button className="afd-props__del" onClick={onDelete} title="Sil">
            <Trash2 size={14} />
          </button>
        </div>

        <label className="afd-props__label">Entegrasyon Tanımı</label>
        {integList.length === 0 ? (
          <div className="afd-props__hint" style={{ color: '#dc2626', background: 'rgba(220,38,38,.06)', padding: '10px 12px', borderRadius: 8 }}>
            Henüz aktif bir entegrasyon tanımlanmamış. <br />
            <strong>Entegrasyonlar</strong> menüsünden yeni bir entegrasyon
            tanımlayıp aktif edin, sonra buraya geri dönün.
          </div>
        ) : (
          <select className="afd-props__input" value={iId}
                  onChange={function (e) {
                    var v = e.target.value
                    var sel = integList.find(function (x) { return String(x.id) === v })
                    commitI({
                      integrationId:   v ? parseInt(v, 10) : null,
                      integrationName: sel ? sel.name : null,
                    })
                  }}>
            <option value="">— Entegrasyon seçin —</option>
            {integList.map(function (it) {
              var lbl = it.name
              if (it.sourceFormLabel) lbl += ' · ' + it.sourceFormLabel
              if (!it.hasEndpoint) lbl += ' (yalnız prosedür)'
              return <option key={it.id} value={it.id}>{lbl}</option>
            })}
          </select>
        )}

        <label className="afd-props__label" style={{ marginTop: 12 }}>Kayıt ID Kaynağı</label>
        <select className="afd-props__input" value={iRecSrc}
                onChange={function (e) { commitI({ recordIdSource: e.target.value, customRecordId: null }) }}>
          <option value="entity">Onaydaki Belge / Kayıt</option>
          <option value="custom">Sabit (manuel ID)</option>
        </select>
        {iRecSrc === 'custom' && (
          <>
            <label className="afd-props__label">Sabit Kayıt ID</label>
            <input className="afd-props__input" value={iCustomRec}
                   onChange={function (e) { commitI({ customRecordId: e.target.value }) }}
                   placeholder="Örn. 12345 (test amaçlı)" />
          </>
        )}

        <label className="afd-switch" style={{ marginTop: 14 }}>
          <input type="checkbox" checked={iHalt}
                 onChange={function (e) { commitI({ haltOnError: e.target.checked }) }} />
          <span className="afd-switch__track"></span>
          <span style={{ marginLeft: 8, fontSize: '.85rem' }}>Entegrasyon başarısız olursa akışı durdur</span>
        </label>

        <div className="afd-props__hint">
          Bu düğüme akış geldiğinde seçili entegrasyon tetiklenir
          (<code>IntegrationRunner.RunAsync</code>, trigger=Cascade). Sonuç
          <strong> Entegrasyonlar → Run Log</strong>'unda görüntülenebilir.
          Başarılıysa veya <em>"hata durdur"</em> kapalıysa akış sonraki
          düğümle devam eder.
        </div>
      </div>
    )
  }

  /* ── SET VARIABLE node ── */
  if (selected.kind === 'node' && selected.type === 'setVariable') {
    var setData = selected.data || {}
    var availableVars = Array.isArray(variables) ? variables : []
    var selectedVar = availableVars.find(function (v) { return v.name === setData.variableName })
    var isSqlVar = selectedVar && selectedVar.valueSource === 'sql'
    return (
      <div className="afd-props">
        <div className="afd-props__head">
          <span className="afd-props__head-badge" style={{ background: 'rgba(100,116,139,.18)', color: '#475569' }}>Değişken Ata</span>
          <button className="afd-props__del" onClick={onDelete} title="Sil">
            <Trash2 size={14} />
          </button>
        </div>

        <label className="afd-props__label">Hedef Değişken</label>
        {availableVars.length === 0 ? (
          <div className="afd-props__hint-italic" style={{
            padding: '8px 10px', borderRadius: 6,
            background: 'rgba(245,158,11,.10)', border: '1px solid rgba(245,158,11,.4)',
            color: '#b45309',
          }}>
            Henüz değişken tanımı yok. Üst toolbar'daki <strong>𝑥 Değişkenler</strong> butonundan ekleyin.
          </div>
        ) : (
          <select className="afd-props__input"
                  value={setData.variableName || ''}
                  onChange={function (e) { commitNode({ variableName: e.target.value || null }) }}>
            <option value="">— değişken seçin —</option>
            {availableVars.map(function (v) {
              return (
                <option key={v.name} value={v.name}>
                  {v.name} ({v.typeCode}){v.valueSource === 'sql' ? ' · SQL' : ''}
                </option>
              )
            })}
          </select>
        )}

        {isSqlVar ? (
          <div className="afd-props__hint" style={{ marginTop: 8, background: 'rgba(99,102,241,.08)', border: '1px solid rgba(99,102,241,.2)', borderRadius: 6, padding: '8px 10px' }}>
            <strong>SQL değişkeni</strong> — sorgu <em>𝑥 Değişkenler</em> panelinde tanımlı.
            Bu düğüm çalıştığında sorgu çalıştırılır ve sonuç <code>{'{var.' + (setData.variableName || '?') + '}'}</code> olarak akışa yazılır.
          </div>
        ) : (
          <>
            <label className="afd-props__label" style={{ marginTop: 8 }}>İfade</label>
            <input className="afd-props__input"
                   value={setData.expression || ''}
                   placeholder={
                     selectedVar && selectedVar.typeCode === 'int'    ? 'örn: ' + (setData.variableName || 'x') + ' + 1'
                   : selectedVar && selectedVar.typeCode === 'bool'   ? 'örn: true / false'
                   : selectedVar && selectedVar.typeCode === 'string' ? 'örn: "tamam"'
                   : selectedVar && selectedVar.typeCode === 'date'   ? 'örn: 2026-06-30'
                   : 'sabit veya basit aritmetik (x + 1, y - 2)'
                   }
                   onChange={function (e) { commitNode({ expression: e.target.value }) }} />
            <div className="afd-props__hint">
              Akış çalışırken <code>{setData.variableName || '?'}</code> değişkenine ifadenin değeri yazılır.
            </div>
          </>
        )}
      </div>
    )
  }

  /* ── START / END node ── */
  if (selected.kind === 'node' && (selected.type === 'start' || selected.type === 'end')) {
    var isStart = selected.type === 'start'

    // END node — sade panel
    if (!isStart) {
      return (
        <div className="afd-props">
          <div className="afd-props__head">
            <span className="afd-props__head-badge afd-props__head-badge--end">Bitir</span>
            <button className="afd-props__del" onClick={onDelete} title="Sil">
              <Trash2 size={14} />
            </button>
          </div>
          <div className="afd-props__hint">
            Akışın bitiş noktası. Tüm dallar nihayetinde bir Bitir düğümüne ulaşmalıdır.
          </div>
        </div>
      )
    }

    // START node — Tetikleme Koşulları editörü
    var activeRules = Array.isArray(rules)
      ? rules.filter(function (r) { return r.isActive !== false })
      : []

    function commitRules(updater) {
      if (typeof onRulesChange !== 'function') return
      var current = Array.isArray(rules) ? rules.slice() : []
      onRulesChange(updater(current))
    }

    function handleAddRule() {
      commitRules(function (arr) {
        arr.push({ id: 0, ruleType: 'Always', ruleValue: '', isActive: true })
        return arr
      })
    }

    function handleRuleTypeChange(realIdx, newType) {
      commitRules(function (arr) {
        var r = Object.assign({}, arr[realIdx], { ruleType: newType, ruleValue: '' })
        arr[realIdx] = r
        return arr
      })
    }

    function handleRuleValueChange(realIdx, newValue) {
      commitRules(function (arr) {
        arr[realIdx] = Object.assign({}, arr[realIdx], { ruleValue: newValue })
        return arr
      })
    }

    function handleRuleDelete(realIdx) {
      commitRules(function (arr) {
        arr[realIdx] = Object.assign({}, arr[realIdx], { isActive: false })
        return arr
      })
    }

    function handleDeptToggle(realIdx, deptId, checked) {
      commitRules(function (arr) {
        var r = arr[realIdx]
        var ids = String(r.ruleValue || '')
          .split(',').map(function (x) { return x.trim() }).filter(Boolean)
        var key = String(deptId)
        if (checked) {
          if (ids.indexOf(key) === -1) ids.push(key)
        } else {
          ids = ids.filter(function (x) { return x !== key })
        }
        arr[realIdx] = Object.assign({}, r, { ruleValue: ids.join(',') })
        return arr
      })
    }

    // map activeRules → real index in rules array (for mutation)
    function realIndexOf(r) {
      if (!Array.isArray(rules)) return -1
      return rules.indexOf(r)
    }

    return (
      <div className="afd-props">
        <div className="afd-props__head">
          <span className="afd-props__head-badge afd-props__head-badge--start">Başla</span>
        </div>

        <div style={{ fontSize: '.85rem', fontWeight: 700, color: 'var(--afd-text)', marginBottom: 4 }}>
          Tetikleme Koşulları
        </div>
        <div style={{ fontSize: '.72rem', color: 'var(--afd-muted)', marginBottom: 12, lineHeight: 1.45 }}>
          Bu akış hangi belgelerde tetiklenir? Koşul tanımlanmazsa her belgeye uygulanır.
          Birden fazla koşul varsa hepsi sağlanmalıdır (VE mantığı).
        </div>

        {activeRules.length === 0 && (
          <div className="afd-props__hint-italic" style={{ padding: '8px 0' }}>
            Henüz koşul yok — akış tüm belgelerde tetiklenir.
          </div>
        )}

        {activeRules.map(function (r) {
          var realIdx = realIndexOf(r)
          return (
            <div key={realIdx + '_' + r.ruleType} className="afd-rule-row">
              <div className="afd-rule-row__top">
                <select
                  className="afd-props__input afd-rule-row__type"
                  value={r.ruleType}
                  onChange={function (e) { handleRuleTypeChange(realIdx, e.target.value) }}
                >
                  {RULE_TYPES.map(function (rt) {
                    return <option key={rt.v} value={rt.v}>{rt.l}</option>
                  })}
                </select>
                <button
                  className="afd-props__del"
                  onClick={function () { handleRuleDelete(realIdx) }}
                  title="Koşulu Sil"
                  type="button"
                >
                  <X size={14} />
                </button>
              </div>

              {r.ruleType === 'Always' && (
                <div className="afd-props__hint-italic" style={{ marginTop: 6 }}>
                  Bu kural her zaman geçerli
                </div>
              )}

              {r.ruleType !== 'Always' && r.ruleType !== 'Department' && (
                <input
                  className="afd-props__input"
                  style={{ marginTop: 6 }}
                  value={r.ruleValue || ''}
                  placeholder={rulePlaceholder(r.ruleType)}
                  onChange={function (e) { handleRuleValueChange(realIdx, e.target.value) }}
                />
              )}

              {r.ruleType === 'Department' && (
                <div className="afd-props__chips" style={{ marginTop: 6 }}>
                  {departments.length === 0 && (
                    <span className="afd-props__hint-italic">
                      Aktif departman tanımı yok.
                    </span>
                  )}
                  {departments.map(function (d) {
                    var ids = String(r.ruleValue || '')
                      .split(',').map(function (x) { return x.trim() }).filter(Boolean)
                    var on = ids.indexOf(String(d.id)) !== -1
                    return (
                      <label
                        key={d.id}
                        className={'afd-props__chip' + (on ? ' is-on' : '')}
                      >
                        <input
                          type="checkbox"
                          checked={on}
                          onChange={function (e) { handleDeptToggle(realIdx, d.id, e.target.checked) }}
                        />
                        <span>{d.name}</span>
                      </label>
                    )
                  })}
                </div>
              )}
            </div>
          )
        })}

        <button
          type="button"
          className="afd-rule-add"
          onClick={handleAddRule}
        >
          <Plus size={13} />
          Koşul Ekle
        </button>

        <div className="afd-props__hint" style={{ marginTop: 14 }}>
          <strong>Her Zaman:</strong> Koşulsuz uygula &nbsp;|&nbsp;
          <strong>Min/Maks Tutar:</strong> ≥ / ≤ X TL &nbsp;|&nbsp;
          <strong>Tutar Aralığı:</strong> X ≤ tutar ≤ Y &nbsp;|&nbsp;
          <strong>VKN:</strong> Belirli gönderici &nbsp;|&nbsp;
          <strong>Departman:</strong> Çoklu seçim (VEYA)
        </div>
      </div>
    )
  }

  /* ── EDGE ── */
  if (selected.kind === 'edge') {
    var isTimeout = edgeKind === 'timeout'
    // Timeout edge ise kaynak adimin SLA verisini buradan yonet (senkron — adim panelindeki
    // SLA editor'u ile ayni veri).
    var srcNode = null
    if (isTimeout && selected.source && Array.isArray(nodes)) {
      srcNode = nodes.find(function (n) { return n.id === selected.source }) || null
    }
    var srcData    = (srcNode && srcNode.data) || {}
    var sHours     = srcData.slaHours == null ? 24 : Number(srcData.slaHours)
    var sUnit      = srcData.slaTimeUnit || 'hours'
    var sAction    = srcData.slaAction || 'escalate'
    var sWarnHrs   = srcData.slaReminderHoursBefore == null ? '' : String(srcData.slaReminderHoursBefore)
    var sMsg       = srcData.slaMessageTemplate || ''
    var sReason    = srcData.slaRejectReason || ''
    function patchSource(patch) {
      if (!srcNode || typeof updateNodeData !== 'function') return
      updateNodeData(srcNode.id, patch)
    }

    return (
      <div className="afd-props">
        <div className="afd-props__head">
          <span className="afd-props__head-badge afd-props__head-badge--edge">Bağlantı</span>
          <button className="afd-props__del" onClick={onDelete} title="Sil">
            <Trash2 size={14} />
          </button>
        </div>

        <label className="afd-props__label">Etiket</label>
        <input
          className="afd-props__input"
          value={edgeLabel}
          onChange={function (e) {
            setEdgeLabel(e.target.value)
            commitNode({ label: e.target.value })
          }}
          placeholder="Örn: Onay, Red, Evet, Hayır, Gecikme"
        />

        <label className="afd-props__label">Bağlantı Türü</label>
        {isTimeout ? (
          /* Timeout edge tipi sourceHandle'dan turetilir; elle degistirilmez — read-only rozet. */
          <div style={{
            padding: '6px 10px', borderRadius: 6,
            background: 'rgba(245,158,11,.14)',
            border: '1px solid rgba(245,158,11,.45)',
            color: '#b45309', fontSize: '.78rem', fontWeight: 700,
            display: 'flex', alignItems: 'center', gap: 6,
          }}>
            ⏱ Gecikme
            <span style={{ fontWeight: 500, opacity: .85, fontSize: '.72rem' }}>
              (sarı "Gecikme" handle bağlantısı — tür sabit)
            </span>
          </div>
        ) : (
          (function () {
            var EDGE_KINDS = [
              { id: 'default', label: 'Varsayılan', color: '#94a3b8', dashed: false, hint: 'Koşulsuz geçiş' },
              { id: 'true',    label: 'Onay / Evet',color: '#10b981', dashed: false, hint: 'Onaylandı / Decision true' },
              { id: 'false',   label: 'Red / Hayır',color: '#ef4444', dashed: false, hint: 'Reddedildi / Decision false' },
            ]
            return (
              <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
                {EDGE_KINDS.map(function (k) {
                  var on = edgeKind === k.id
                  return (
                    <button key={k.id} type="button" title={k.hint}
                      onClick={function () { setEdgeKind(k.id); commitNode({ edgeKind: k.id }) }}
                      style={{
                        display: 'flex', alignItems: 'center', gap: 8,
                        padding: '6px 10px', borderRadius: 6, cursor: 'pointer',
                        border: '1px solid ' + (on ? k.color : 'var(--afd-border, #e2e8f0)'),
                        background: on ? (k.color + '1a') : 'var(--afd-bg, transparent)',
                        color: on ? k.color : 'var(--afd-text, #334155)',
                        fontSize: 12, fontWeight: on ? 700 : 500,
                        transition: 'all 120ms ease-out',
                      }}>
                      <span style={{
                        display: 'inline-block', width: 22, height: 3, borderRadius: 2,
                        background: k.dashed
                          ? 'repeating-linear-gradient(90deg,' + k.color + ' 0 5px, transparent 5px 8px)'
                          : k.color,
                      }} />
                      <span style={{ flex: '1 1 auto' }}>{k.label}</span>
                      {on && <span style={{ fontSize: 11, opacity: .8 }}>✓</span>}
                    </button>
                  )
                })}
              </div>
            )
          })()
        )}

        {isTimeout && srcNode && (
          <div style={{ marginTop: 10, paddingTop: 10, borderTop: '1px dashed var(--afd-border, #cbd5e1)' }}>
            <div style={{ fontSize: '.72rem', color: 'var(--afd-muted)', marginBottom: 6 }}>
              Bu bağlantı tetiklendiğinde, kaynak adımın <strong>{srcData.stepName || 'Adım'}</strong> SLA verisi kullanılır.
              Aynı veri adım panelinde de görünür — buradan veya oradan düzenleyebilirsin.
            </div>

            <label className="afd-props__label">Süre</label>
            <div style={{ display: 'flex', gap: 6 }}>
              <input type="number" min="1" className="afd-props__input" style={{ flex: '0 0 80px' }}
                     value={sHours}
                     onChange={function (e) {
                       var v = parseInt(e.target.value, 10) || 0
                       patchSource({ slaEnabled: true, slaHours: v })
                     }} />
              <select className="afd-props__input" style={{ flex: 1 }}
                      value={sUnit}
                      onChange={function (e) { patchSource({ slaEnabled: true, slaTimeUnit: e.target.value }) }}>
                <option value="minutes">dakika</option>
                <option value="hours">saat</option>
                <option value="days">gün (24sa)</option>
                <option value="businessDays">iş günü (Pzt-Cum)</option>
              </select>
            </div>

            <label className="afd-props__label">Önceden uyarı (opsiyonel, saat)</label>
            <input type="number" min="0" className="afd-props__input"
                   value={sWarnHrs} placeholder="örn: 4 → süre dolmadan 4sa önce ön-uyarı"
                   onChange={function (e) {
                     var raw = e.target.value
                     patchSource({ slaEnabled: true, slaAction: 'escalate',
                                   slaReminderHoursBefore: raw === '' ? null : parseInt(raw, 10) || 0 })
                   }} />
            <label className="afd-props__label">Ön-uyarı mesajı (opsiyonel)</label>
            <textarea className="afd-props__input afd-props__input--ta" rows={2} value={sMsg}
                      onChange={function (e) { patchSource({ slaEnabled: true, slaAction: 'escalate',
                                                              slaMessageTemplate: e.target.value }) }}
                      placeholder="Belgeyi {documentNumber} onay için bekliyor — süre {dueDate}'da doluyor." />
          </div>
        )}

      </div>
    )
  }

  /* ── TIMER node ── */
  if (selected.kind === 'node' && selected.type === 'timer') {
    var tData    = selected.data || {}
    var tName    = tData.stepName || ''
    var tValue   = tData.waitValue == null ? '' : String(tData.waitValue)
    var tUnit    = tData.waitUnit  || 'hours'
    function commitTimer(patch) {
      if (typeof onChange === 'function') onChange(patch)
    }
    return (
      <div className="afd-props">
        <div className="afd-props__head">
          <span className="afd-props__head-badge" style={{ background: '#f59e0b', color: '#fff' }}>Bekleme</span>
          <button className="afd-props__del" onClick={onDelete} title="Sil"><Trash2 size={14} /></button>
        </div>
        <div className="afd-props__hint">
          Akışı belirtilen süre bekletir. Süre dolunca otomatik olarak devam eder.
        </div>
        <label className="afd-props__label">Düğüm Adı</label>
        <input className="afd-props__input" type="text" value={tName} maxLength={80}
          placeholder="örn. 24 Saat Bekleme"
          onChange={function (e) { commitTimer({ stepName: e.target.value }) }} />
        <label className="afd-props__label">Bekleme Süresi</label>
        <div style={{ display: 'flex', gap: 6 }}>
          <input className="afd-props__input" type="number" min="1" style={{ flex: '0 0 80px' }}
            value={tValue} placeholder="örn. 24"
            onChange={function (e) {
              var v = parseInt(e.target.value, 10) || 1
              commitTimer({ waitValue: v })
            }} />
          <select className="afd-props__input" style={{ flex: 1 }} value={tUnit}
            onChange={function (e) { commitTimer({ waitUnit: e.target.value }) }}>
            <option value="minutes">Dakika</option>
            <option value="hours">Saat</option>
            <option value="days">Gün (24 sa)</option>
            <option value="businessDays">İş Günü (Pzt–Cum)</option>
          </select>
        </div>
        <ExtraInputsToggleBlock node={selected} onChange={commitTimer} />
      </div>
    )
  }

  /* ── VOTE node ── */
  if (selected.kind === 'node' && selected.type === 'vote') {
    var vData       = selected.data || {}
    var vName       = vData.stepName || ''
    var vType       = vData.votingType || 'majority'
    var vApprIds    = Array.isArray(vData.approverIds)    ? vData.approverIds    : []
    var vApprLabels = Array.isArray(vData.approverLabels) ? vData.approverLabels : []
    function commitVote(patch) {
      if (typeof onChange === 'function') onChange(patch)
    }
    return (
      <div className="afd-props">
        <div className="afd-props__head">
          <span className="afd-props__head-badge" style={{ background: '#0d9488', color: '#fff' }}>Oylama</span>
          <button className="afd-props__del" onClick={onDelete} title="Sil"><Trash2 size={14} /></button>
        </div>
        <div className="afd-props__hint">
          Birden fazla kişi oy kullanır. Sonuca göre <em>Kabul</em> veya <em>Red</em> koluna gidilir.
        </div>
        <label className="afd-props__label">Düğüm Adı</label>
        <input className="afd-props__input" type="text" value={vName} maxLength={80}
          placeholder="örn. Yönetim Kurulu Oylaması"
          onChange={function (e) { commitVote({ stepName: e.target.value }) }} />
        <label className="afd-props__label">Oylama Türü</label>
        <div style={{ display: 'flex', borderRadius: 6, overflow: 'hidden', border: '1px solid var(--afd-border, #e2e8f0)', marginBottom: 4 }}>
          {[
            { key: 'any',        label: 'İlk Oy',   hint: 'Herhangi biri onaylarsa kabul' },
            { key: 'majority',   label: 'Çoğunluk', hint: '>50% oranında onay gerekir' },
            { key: 'unanimous',  label: 'Tüm Oylar',hint: 'Herkes onaylamalı' },
          ].map(function (vk, vi, arr) {
            var on = vType === vk.key
            return (
              <button key={vk.key} type="button" title={vk.hint}
                onClick={function () { commitVote({ votingType: vk.key }) }}
                style={{
                  flex: '1 1 0', padding: '5px 0', fontSize: 11, fontWeight: on ? 700 : 500,
                  border: 'none',
                  borderRight: vi < arr.length - 1 ? '1px solid var(--afd-border, #e2e8f0)' : 'none',
                  background: on ? '#0d9488' : 'var(--afd-bg, transparent)',
                  color: on ? '#fff' : 'var(--afd-muted, #64748b)',
                  cursor: 'pointer', transition: 'all .12s',
                }}>{vk.label}</button>
            )
          })}
        </div>
        <label className="afd-props__label" style={{ marginTop: 8 }}>Oylayıcılar</label>
        {(users || []).length === 0 ? (
          <div style={{ fontSize: 11.5, color: 'var(--afd-muted, #64748b)', padding: '6px 0' }}>
            Kullanıcı listesi yükleniyor…
          </div>
        ) : (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 4, maxHeight: 180, overflowY: 'auto', padding: '2px 0' }}>
            {(users || []).map(function (u) {
              var uid = String(u.id)
              var checked = vApprIds.indexOf(uid) !== -1
              return (
                <label key={uid} style={{ display: 'flex', alignItems: 'center', gap: 8, cursor: 'pointer', padding: '3px 0' }}>
                  <input type="checkbox" checked={checked}
                    onChange={function (e) {
                      var next = checked
                        ? vApprIds.filter(function (x) { return x !== uid })
                        : vApprIds.concat([uid])
                      var nextLabels = checked
                        ? vApprLabels.filter(function (x) { return x !== u.fullName })
                        : vApprLabels.concat([u.fullName])
                      commitVote({ approverIds: next, approverLabels: nextLabels })
                    }} />
                  <span style={{ fontSize: 12, color: 'var(--afd-text, #334155)' }}>{u.fullName}</span>
                </label>
              )
            })}
          </div>
        )}
        <ExtraInputsToggleBlock node={selected} onChange={commitVote} />
      </div>
    )
  }

  /* ── SUBPROCESS node ── */
  if (selected.kind === 'node' && selected.type === 'subprocess') {
    var spData     = selected.data || {}
    var spName     = spData.stepName   || ''
    var spFlowId   = spData.subFlowId  || ''
    var spFlowName = spData.subFlowName|| ''
    function commitSP(patch) {
      if (typeof onChange === 'function') onChange(patch)
    }
    return (
      <div className="afd-props">
        <div className="afd-props__head">
          <span className="afd-props__head-badge" style={{ background: '#4f46e5', color: '#fff' }}>Alt Süreç</span>
          <button className="afd-props__del" onClick={onDelete} title="Sil"><Trash2 size={14} /></button>
        </div>
        <div className="afd-props__hint">
          Başka bir onay akışını alt süreç olarak başlatır. Alt süreç tamamlandıktan sonra akış devam eder.
        </div>
        <label className="afd-props__label">Düğüm Adı</label>
        <input className="afd-props__input" type="text" value={spName} maxLength={80}
          placeholder="örn. Fatura Onayı Alt Süreci"
          onChange={function (e) { commitSP({ stepName: e.target.value }) }} />
        <label className="afd-props__label">Alt Akış ID</label>
        <input className="afd-props__input" type="number" min="1" value={spFlowId}
          placeholder="Onay akışının ID numarası (ör. 3)"
          onChange={function (e) { commitSP({ subFlowId: e.target.value }) }} />
        <label className="afd-props__label">Alt Akış Adı (gösterim)</label>
        <input className="afd-props__input" type="text" value={spFlowName}
          placeholder="ör. Fatura Onayı"
          onChange={function (e) { commitSP({ subFlowName: e.target.value }) }} />
        <ExtraInputsToggleBlock node={selected} onChange={commitSP} />
      </div>
    )
  }

  /* ── WEBHOOK node ── */
  if (selected.kind === 'node' && selected.type === 'webhook') {
    var whData    = selected.data || {}
    var whName    = whData.stepName          || ''
    var whUrl     = whData.url               || ''
    var whMethod  = whData.method            || 'POST'
    var whHeaders = whData.headersJson       || ''
    var whBody    = whData.bodyTemplate      || ''
    var whCodes   = whData.successStatusCodes|| '200,201,204'
    var whTimeout = whData.timeoutSeconds == null ? '30' : String(whData.timeoutSeconds)
    function commitWH(patch) {
      if (typeof onChange === 'function') onChange(patch)
    }
    return (
      <div className="afd-props">
        <div className="afd-props__head">
          <span className="afd-props__head-badge" style={{ background: '#e11d48', color: '#fff' }}>Webhook</span>
          <button className="afd-props__del" onClick={onDelete} title="Sil"><Trash2 size={14} /></button>
        </div>
        <div className="afd-props__hint">
          Dış sisteme HTTP isteği gönderir. Yanıt koduna göre <em>Başarı</em> veya <em>Hata</em> koluna gidilir.
        </div>
        <label className="afd-props__label">Düğüm Adı</label>
        <input className="afd-props__input" type="text" value={whName} maxLength={80}
          placeholder="örn. ERP Bildir"
          onChange={function (e) { commitWH({ stepName: e.target.value }) }} />
        <label className="afd-props__label">URL</label>
        <input className="afd-props__input" type="text" value={whUrl}
          placeholder="https://api.example.com/approval-callback"
          onChange={function (e) { commitWH({ url: e.target.value }) }} />
        <label className="afd-props__label">Yöntem</label>
        <div style={{ display: 'flex', borderRadius: 6, overflow: 'hidden', border: '1px solid var(--afd-border, #e2e8f0)', marginBottom: 4 }}>
          {['POST', 'PUT', 'PATCH', 'GET'].map(function (m, mi, arr) {
            var on = whMethod === m
            return (
              <button key={m} type="button" onClick={function () { commitWH({ method: m }) }}
                style={{
                  flex: '1 1 0', padding: '5px 0', fontSize: 11, fontWeight: on ? 700 : 500,
                  border: 'none',
                  borderRight: mi < arr.length - 1 ? '1px solid var(--afd-border, #e2e8f0)' : 'none',
                  background: on ? '#e11d48' : 'var(--afd-bg, transparent)',
                  color: on ? '#fff' : 'var(--afd-muted, #64748b)',
                  cursor: 'pointer', transition: 'all .12s',
                }}>{m}</button>
            )
          })}
        </div>
        <label className="afd-props__label">Başarı Durum Kodları</label>
        <input className="afd-props__input" type="text" value={whCodes}
          placeholder="200,201,204"
          onChange={function (e) { commitWH({ successStatusCodes: e.target.value }) }} />
        <label className="afd-props__label">Zaman Aşımı (saniye)</label>
        <input className="afd-props__input" type="number" min="1" max="300" value={whTimeout}
          onChange={function (e) { commitWH({ timeoutSeconds: parseInt(e.target.value, 10) || 30 }) }} />
        <label className="afd-props__label">İstek Gövdesi (JSON şablon, token destekli)</label>
        <textarea className="afd-props__input afd-props__input--ta" rows={4} value={whBody}
          placeholder={'{\n  "entityId": "{entityId}",\n  "requester": "{requesterName}"\n}'}
          onChange={function (e) { commitWH({ bodyTemplate: e.target.value }) }} />
        <label className="afd-props__label">İstek Başlıkları (JSON, opsiyonel)</label>
        <textarea className="afd-props__input afd-props__input--ta" rows={2} value={whHeaders}
          placeholder={'{"Authorization":"Bearer TOKEN","X-Source":"CalibraHub"}'}
          onChange={function (e) { commitWH({ headersJson: e.target.value }) }} />
        <ExtraInputsToggleBlock node={selected} onChange={commitWH} />
      </div>
    )
  }

  return null
}

/* ─────────────────────────────────────────────────────────────────
 * StepSlaEditor — Adım node properties paneline gömülü SLA + Gecikme Aksiyonu UI.
 * Adımda max bekleme süresi tanımlanır; süre dolunca tetiklenecek aksiyon seçilir:
 *   - reminder      : Hatırlatma gönder (kime: onayciya / belgeyi olusturana)
 *   - escalate      : Eskale et — hedef 3. kol (sarı "Süre" handle) edge'inden çözülür
 *   - autoApprove   : Otomatik onayla
 *   - autoReject    : Otomatik reddet (gerekçe ile)
 * Veri Step.NodeData JSON içine sla* prefix'le yazılır.
 * Worker (CalibraHub.Worker.SlaCheckerService) periyodik tarar; eskale hedefi
 * Edge tablosundan EdgeKind='timeout' kaydından alınır.
 * ────────────────────────────────────────────────────────────────── */
function StepSlaEditor({ data, nodeId, edges, nodes, users, departments, onChange }) {
  var d = data || {}
  var slaEnabled  = d.slaEnabled === true
  var slaHours    = d.slaHours == null ? 24 : Number(d.slaHours)
  var slaUnit     = d.slaTimeUnit || 'hours'  // hours | days | businessDays
  var slaWarnHrs  = d.slaReminderHoursBefore == null ? '' : String(d.slaReminderHoursBefore)
  var slaMsg      = d.slaMessageTemplate || ''

  // 3. kol (timeout) bu node'dan çıkıyor mu? Hedef node'un adi?
  var timeoutEdge = (edges || []).find(function (e) {
    return e.source === nodeId && e.sourceHandle === 'timeout'
  })
  var timeoutTargetName = null
  if (timeoutEdge) {
    var tn = (nodes || []).find(function (n) { return n.id === timeoutEdge.target })
    if (tn) {
      timeoutTargetName = (tn.data && tn.data.stepName)
        || (tn.type === 'notification' ? 'Bildirim'
          : tn.type === 'decision'     ? 'Karar'
          : tn.type === 'integration'  ? 'Entegrasyon'
          : tn.type === 'parallel'     ? 'Paralel'
          : tn.type === 'end'          ? 'Bitir' : 'Adım')
    }
  }

  function commit(patch) { if (typeof onChange === 'function') onChange(patch) }

  return (
    <div style={{ marginTop: 12, paddingTop: 10, borderTop: '1px dashed var(--afe-border, #cbd5e1)' }}>
      <label className="afd-props__label" style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
        <span>⏱ Gecikme (SLA)</span>
        <label className="afe-switch" style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
          <input type="checkbox" checked={slaEnabled}
                 onChange={function (e) { commit({ slaEnabled: e.target.checked, slaAction: 'escalate' }) }} />
          <span className="afe-switch__track"></span>
        </label>
      </label>

      {slaEnabled && (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 8, marginTop: 6 }}>
          <label className="afd-props__label">Süre</label>
          <div style={{ display: 'flex', gap: 6 }}>
            <input type="number" min="1" className="afd-props__input" style={{ flex: '0 0 80px' }}
                   value={slaHours}
                   onChange={function (e) { commit({ slaHours: parseInt(e.target.value, 10) || 0 }) }} />
            <select className="afd-props__input" style={{ flex: 1 }}
                    value={slaUnit}
                    onChange={function (e) { commit({ slaTimeUnit: e.target.value }) }}>
              <option value="minutes">dakika</option>
              <option value="hours">saat</option>
              <option value="days">gün (24sa)</option>
              <option value="businessDays">iş günü (Pzt-Cum)</option>
            </select>
          </div>

          <div style={{
            padding: '8px 10px', borderRadius: 6,
            background: timeoutEdge ? 'rgba(245,158,11,.12)' : 'rgba(239,68,68,.10)',
            border: '1px solid ' + (timeoutEdge ? 'rgba(245,158,11,.45)' : 'rgba(239,68,68,.4)'),
            fontSize: '.78rem', lineHeight: 1.5,
          }}>
            {timeoutEdge ? (
              <>
                <strong style={{ color: '#d97706' }}>Hedef bağlı:</strong>{' '}
                <span>{timeoutTargetName || '(adsız node)'}</span>
                <div style={{ marginTop: 4, opacity: .8 }}>
                  Süre dolunca akış sarı "Gecikme" kolundan bu node'a yönlendirilir.
                </div>
              </>
            ) : (
              <>
                <strong style={{ color: '#b91c1c' }}>Hedef seçilmedi.</strong>{' '}
                Adım kartının altındaki <strong>sarı "Gecikme"</strong> handle'ından
                bir node'a (Bildirim, Karar, Adım…) çizgi çekin.
              </>
            )}
          </div>

          <label className="afd-props__label">Önceden uyarı (opsiyonel, saat)</label>
          <input type="number" min="0" className="afd-props__input"
                 value={slaWarnHrs} placeholder="örn: 4 → süre dolmadan 4sa önce ön-uyarı"
                 onChange={function (e) { commit({ slaReminderHoursBefore: e.target.value === '' ? null : parseInt(e.target.value, 10) || 0 }) }} />
          <label className="afd-props__label">Ön-uyarı mesajı (opsiyonel)</label>
          <textarea className="afd-props__input afd-props__input--ta" rows={2} value={slaMsg}
                    onChange={function (e) { commit({ slaMessageTemplate: e.target.value }) }}
                    placeholder="Belgeyi {documentNumber} onay için bekliyor — süre {dueDate}'da doluyor." />

          <div className="afd-props__hint">
            <strong>Nasıl çalışır:</strong> Adım aktif olduğu andan itibaren süre sayılmaya başlar.
            Süre dolunca akış 3. kol (Gecikme) bağlantısındaki node'a yönlendirilir; o node Bildirim,
            Karar veya başka bir Adım olabilir. "Süre dolunca ne olacak" sorusunun cevabı = bağladığın hedef.
          </div>
        </div>
      )}
    </div>
  )
}

/* ─────────────────────────────────────────────────────────────────
 * ExtraInputsToggleBlock — node'a istenildigi kadar (max EXTRA_INPUT_MAX) ek
 * giris kolu ekler. data.extraInputs DIZI olarak tutulur:
 *   [ { id: 'x12345', side: 'right'|'left'|'bottom', offset: 0..1 }, ... ]
 *
 * UI: "+ Ek Kol Ekle" butonu + her satir icin kenar dropdown (sag/sol/alt)
 * + "Sil" butonu. Yon degisirse offset 0.5'e reset edilir (yeni kenarda eski
 * offset'in fiziksel anlami olmadigi icin). Alt+surukle ile node uzerinde
 * konumlandirma yapilir, ayar yine extraInputs[i].offset'a yazilir.
 *
 * Geriye uyumluluk: eski {right:true,...} object format gelirse otomatik
 * array'e cevrilir (id 'x'+side).
 * ────────────────────────────────────────────────────────────────── */
var EXTRA_INPUT_MAX = 6
function normalizeExtraInputsArray(raw) {
  if (Array.isArray(raw)) {
    return raw
      .filter(function (it) { return it && typeof it.side === 'string' })
      .map(function (it) {
        var kind = (it.kind === 'out') ? 'out' : 'in'
        return { id: it.id, side: it.side, offset: it.offset, kind: kind, label: it.label, color: it.color, edgeKind: it.edgeKind || null }
      })
  }
  if (raw && typeof raw === 'object') {
    var out = []
    ;['right', 'bottom', 'left'].forEach(function (s) {
      if (raw[s] === true) out.push({ id: 'x' + s, side: s, offset: 0.5, kind: 'in' })
    })
    return out
  }
  return []
}
function makeExtraInputId(existing) {
  var used = {}
  existing.forEach(function (it) { used[it.id] = true })
  for (var i = 0; i < 50; i++) {
    var candidate = 'x' + Math.floor(Math.random() * 1000000).toString(36)
    if (!used[candidate]) return candidate
  }
  return 'x' + existing.length + '_' + Math.floor(Math.random() * 1000000).toString(36)
}
function ExtraInputsToggleBlock({ node, onChange }) {
  if (!node || node.kind !== 'node') return null
  if (node.type === 'start') return null
  // Bitir (end) node icin cikis (source) anlamsiz — sadece giris alir.
  var endOnly = node.type === 'end'
  var data = node.data || {}
  var items = normalizeExtraInputsArray(data.extraInputs)
  var maxed = items.length >= EXTRA_INPUT_MAX
  function commit(next) {
    if (typeof onChange !== 'function') return
    onChange({ extraInputs: next })
  }
  function add(kind) {
    if (maxed) return
    commit(items.concat([{ id: makeExtraInputId(items), side: 'right', offset: 0.5, kind: kind }]))
  }
  function remove(itemId) {
    commit(items.filter(function (it) { return it.id !== itemId }))
  }
  var SIDE_META = {
    right:  { label: 'Sağ', icon: '→' },
    left:   { label: 'Sol', icon: '←' },
    bottom: { label: 'Alt', icon: '↓' },
    top:    { label: 'Üst', icon: '↑' },
  }
  return (
    <div style={{ marginTop: 12, paddingTop: 10, borderTop: '1px dashed var(--afe-border, #cbd5e1)' }}>
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 6 }}>
        <label className="afd-props__label" style={{ margin: 0, display: 'flex', alignItems: 'center', gap: 6 }}>
          <span>➕ Ek Bağlantı Kolları</span>
          {items.length > 0 && (
            <span style={{
              fontSize: '.62rem', padding: '1px 6px', borderRadius: 10,
              background: 'var(--afd-accent-s, rgba(99,102,241,.15))',
              color: 'var(--afd-accent, #6366f1)', fontWeight: 700,
            }}>{items.length}/{EXTRA_INPUT_MAX}</span>
          )}
        </label>
        <div style={{ display: 'inline-flex', gap: 4 }}>
          <button type="button" onClick={function () { add('in') }} disabled={maxed}
            title={maxed ? 'En fazla ' + EXTRA_INPUT_MAX + ' kol eklenebilir' : 'Yeni giriş kolu ekle (başka düğümden buraya bağlantı gelir)'}
            style={{
              padding: '5px 10px', borderRadius: 6,
              border: '1px solid var(--afd-accent, #6366f1)',
              background: maxed ? 'var(--afd-bg, transparent)' : 'var(--afd-accent, #6366f1)',
              color: maxed ? 'var(--afd-muted, #64748b)' : '#fff',
              fontSize: 11.5, fontWeight: 600, cursor: maxed ? 'not-allowed' : 'pointer',
              opacity: maxed ? 0.6 : 1, display: 'inline-flex', alignItems: 'center', gap: 4,
            }}>
            + Giriş
          </button>
          {!endOnly && (
            <button type="button" onClick={function () { add('out') }} disabled={maxed}
              title={maxed ? 'En fazla ' + EXTRA_INPUT_MAX + ' kol eklenebilir' : 'Yeni çıkış kolu ekle (buradan başka düğüme bağlantı çıkar)'}
              style={{
                padding: '5px 10px', borderRadius: 6,
                border: '1px solid var(--afd-success, #10b981)',
                background: maxed ? 'var(--afd-bg, transparent)' : 'var(--afd-success, #10b981)',
                color: maxed ? 'var(--afd-muted, #64748b)' : '#fff',
                fontSize: 11.5, fontWeight: 600, cursor: maxed ? 'not-allowed' : 'pointer',
                opacity: maxed ? 0.6 : 1, display: 'inline-flex', alignItems: 'center', gap: 4,
              }}>
              + Çıkış
            </button>
          )}
        </div>
      </div>
      <div style={{ fontSize: '.72rem', color: 'var(--afd-muted, #64748b)', marginBottom: 8, lineHeight: 1.5 }}>
        Uzaktan gelen bir bağlantı için <strong>Giriş</strong>, başka bir düğüme bağlantı çıkarmak için
        <strong> Çıkış</strong> kolu ekleyin. Konumunu <strong>Alt+sürükle</strong> ile node kenarları
        boyunca istediğiniz yere taşıyabilirsiniz.
      </div>
      {items.length === 0 ? (
        <div style={{
          padding: '8px 10px', borderRadius: 6,
          background: 'var(--afd-bg-s, rgba(148,163,184,.08))',
          color: 'var(--afd-muted, #64748b)', fontSize: 11.5, fontStyle: 'italic', textAlign: 'center',
        }}>
          Henüz ek kol yok. "+ Giriş" veya "+ Çıkış" ile yeni bir bağlantı kolu ekleyin.
        </div>
      ) : (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
          {items.map(function (it, idx) {
            var meta = SIDE_META[it.side] || { label: it.side, icon: '•' }
            var isOut = it.kind === 'out'
            var kindLabel = isOut ? 'Çıkış' : 'Giriş'
            var kindColor = isOut ? 'var(--afd-success, #10b981)' : 'var(--afd-accent, #6366f1)'
            var kindBg = isOut ? 'rgba(16,185,129,.15)' : 'rgba(99,102,241,.15)'
            var EK_COLOR = { default: '#94a3b8', true: '#10b981', false: '#ef4444' }
            var accentColor = isOut ? (EK_COLOR[it.edgeKind || 'default'] || '#94a3b8') : '#6366f1'
            return (
              <div key={it.id} style={{
                display: 'flex', flexDirection: 'column', gap: 6,
                padding: '8px 10px 8px 12px', borderRadius: 8,
                background: 'var(--afd-bg-s, rgba(148,163,184,.06))',
                border: '1px solid var(--afd-border, #e2e8f0)',
                borderLeft: '3px solid ' + accentColor,
              }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                  <span style={{
                    display: 'inline-flex', alignItems: 'center', justifyContent: 'center',
                    width: 22, height: 22, borderRadius: 4,
                    background: kindBg, color: kindColor,
                    fontSize: 11, fontWeight: 700,
                  }}>{idx + 1}</span>
                  <span style={{
                    display: 'inline-flex', alignItems: 'center', gap: 3,
                    padding: '2px 6px', borderRadius: 10,
                    background: kindBg, color: kindColor,
                    fontSize: 10.5, fontWeight: 700, letterSpacing: '.02em',
                  }}>{kindLabel}</span>
                  <span style={{
                    flex: '1 1 auto',
                    display: 'inline-flex', alignItems: 'center', gap: 6,
                    fontSize: 11.5, color: 'var(--afd-text, #334155)', fontWeight: 600,
                  }} title={'Şu an ' + meta.label + ' kenarında. Alt+sürükle ile taşıyın.'}>
                    <span style={{ fontSize: 14, color: kindColor }}>{meta.icon}</span>
                    <span style={{ color: 'var(--afd-muted, #64748b)', fontWeight: 500 }}>{meta.label}</span>
                  </span>
                  <button type="button" onClick={function () { remove(it.id) }} title="Bu kolu sil" style={{
                    padding: '4px 8px', borderRadius: 4,
                    border: '1px solid var(--afd-danger, #ef4444)',
                    background: 'transparent', color: 'var(--afd-danger, #ef4444)',
                    fontSize: 11.5, fontWeight: 700, cursor: 'pointer',
                  }}>×</button>
                </div>
                {isOut && (
                  <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
                    <input
                      type="text"
                      value={it.label || ''}
                      placeholder="Buton etiketi (ör. Onayla / Acil Onayla)…"
                      maxLength={60}
                      onChange={function (e) {
                        commit(items.map(function (x) {
                          return x.id === it.id ? Object.assign({}, x, { label: e.target.value }) : x
                        }))
                      }}
                      style={{
                        width: '100%', fontSize: 11.5,
                        padding: '4px 8px', borderRadius: 4,
                        border: '1px solid var(--afd-border, #e2e8f0)',
                        background: 'var(--afd-bg, transparent)',
                        color: 'var(--afd-text, #334155)',
                      }}
                    />
                    <div style={{ display: 'grid', gridTemplateColumns: '38px 1fr', alignItems: 'center', gap: '8px 8px' }}>
                      <span style={{ fontSize: 10.5, color: 'var(--afd-muted, #64748b)', fontWeight: 600 }}>Renk:</span>
                      <div style={{ display: 'flex', alignItems: 'center', gap: 4 }}>
                        {[
                          { key: 'indigo',  bg: '#6366f1' },
                          { key: 'emerald', bg: '#10b981' },
                          { key: 'rose',    bg: '#ef4444' },
                          { key: 'amber',   bg: '#f59e0b' },
                          { key: 'blue',    bg: '#3b82f6' },
                          { key: 'violet',  bg: '#8b5cf6' },
                          { key: 'slate',   bg: '#64748b' },
                        ].map(function (c) {
                          var active = (it.color || 'indigo') === c.key
                          return (
                            <button key={c.key} type="button" title={c.key}
                              onClick={function () {
                                commit(items.map(function (x) {
                                  return x.id === it.id ? Object.assign({}, x, { color: c.key }) : x
                                }))
                              }}
                              style={{
                                width: 18, height: 18,
                                borderRadius: '50%', background: c.bg, border: 'none',
                                cursor: 'pointer', padding: 0, flexShrink: 0,
                                boxShadow: active ? '0 0 0 2px var(--afd-surface,#fff), 0 0 0 4px ' + c.bg : 'none',
                                transition: 'box-shadow .15s',
                              }}
                            />
                          )
                        })}
                      </div>
                      {node.type === 'step' && (
                        <div style={{ display: 'contents' }}>
                          <span style={{ fontSize: 10.5, color: 'var(--afd-muted, #64748b)', fontWeight: 600 }}>Tür:</span>
                          <div style={{ display: 'flex', borderRadius: 6, overflow: 'hidden', border: '1px solid var(--afd-border, #e2e8f0)' }}>
                            {[
                              { key: 'default', label: 'Varsayılan', color: '#94a3b8' },
                              { key: 'true',    label: 'Onay',       color: '#10b981' },
                              { key: 'false',   label: 'Red',        color: '#ef4444' },
                            ].map(function (ek, ei, arr) {
                              var active = (it.edgeKind || 'default') === ek.key
                              return (
                                <button key={ek.key} type="button" title={ek.label}
                                  onClick={function () {
                                    commit(items.map(function (x) {
                                      return x.id === it.id ? Object.assign({}, x, { edgeKind: ek.key }) : x
                                    }))
                                  }}
                                  style={{
                                    flex: '1 1 0', padding: '4px 0', fontSize: 10.5,
                                    fontWeight: active ? 700 : 500,
                                    border: 'none',
                                    borderRight: ei < arr.length - 1 ? '1px solid var(--afd-border, #e2e8f0)' : 'none',
                                    background: active ? ek.color : 'var(--afd-bg, transparent)',
                                    color: active ? '#fff' : 'var(--afd-muted, #64748b)',
                                    cursor: 'pointer', transition: 'all .12s',
                                  }}
                                >{ek.label}</button>
                              )
                            })}
                          </div>
                        </div>
                      )}
                    </div>
                  </div>
                )}
              </div>
            )
          })}
        </div>
      )}
    </div>
  )
}
