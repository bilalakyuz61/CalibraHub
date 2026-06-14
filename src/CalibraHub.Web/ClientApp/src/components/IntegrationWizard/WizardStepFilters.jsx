/**
 * Wizard Step — Kısıt Kuralları (Pre-flight Filter Builder).
 *
 * 2026-05-22: Backend SourceFilterJson kolonu hazır (Integration.SourceFilterJson),
 * runner + queue tarafında tek-noktadan uygulaniyor.
 *
 * 2026-05-24: UI yeniden tasarlandı. ARTIK "+ Kural Ekle" butonu YOK — formun TÜM
 * header alanları aşağıda bir liste hâlinde görünür. Her satırda:
 *   [Alan Adı]   [Operator dropdown]   [Değer inputu]
 * Kullanıcı operatör + değer doldurduğu alanlar filtreye dahil olur, diğerleri
 * yok sayılır. Boş bırakılan alan = "filtre uygulanmıyor". Tüm aktif filtreler
 * AND ile birleştirilip backend'e gönderilir.
 *
 * Kayıt formatı (Backend `IntegrationFilterEngine.Parse` ile uyumlu):
 *   [
 *     { "field": "Status",            "op": "eq",      "value": "Approved" },
 *     { "field": "GrandTotal",        "op": "gte",     "value": "1000"     },
 *     { "field": "widget:OzelKod",    "op": "eq",      "value": "VIP"      },
 *     { "field": "ContactId",         "op": "notnull"                       }
 *   ]
 *
 * Field listesi: `/Integrations/api/forms/{code}/fields` endpoint'inden çekilir,
 * yalnızca `section==="Header"` olan alanlar gösterilir (kalem/kombinasyon alanları
 * pre-flight context'inde anlamsız — kayıt başına karar). Widget alanları otomatik
 * `widget:fieldKey` prefix'i ile saklanır.
 */
import React, { useEffect, useState, useMemo } from 'react'
import { Filter, AlertCircle, Loader2, Search } from 'lucide-react'

const OPERATORS = [
  { value: '',           label: '— (filtre yok)',            needsValue: false, skip: true },
  { value: 'eq',         label: '= (eşit)',                  needsValue: true  },
  { value: 'neq',        label: '≠ (eşit değil)',            needsValue: true  },
  { value: 'gt',         label: '> (büyük)',                 needsValue: true  },
  { value: 'gte',        label: '≥ (büyük eşit)',            needsValue: true  },
  { value: 'lt',         label: '< (küçük)',                 needsValue: true  },
  { value: 'lte',        label: '≤ (küçük eşit)',            needsValue: true  },
  { value: 'contains',   label: 'içerir',                    needsValue: true  },
  { value: 'startswith', label: 'ile başlar',                needsValue: true  },
  { value: 'in',         label: 'şu listede (virgülle)',     needsValue: true  },
  { value: 'between',    label: 'arasında (min,max)',        needsValue: true  },
  { value: 'isnull',     label: 'boş (NULL)',                needsValue: false },
  { value: 'notnull',    label: 'dolu (NOT NULL)',           needsValue: false },
]

/** Backend JSON → field key → { op, value } map. */
function parseFilterJson(json) {
  if (!json) return {}
  try {
    const arr = JSON.parse(json)
    if (!Array.isArray(arr)) return {}
    const map = {}
    for (const r of arr) {
      if (!r || !r.field) continue
      map[r.field] = {
        op: (r.op || 'eq').toLowerCase(),
        value: r.value ?? '',
      }
    }
    return map
  } catch { return {} }
}

export default function WizardStepFilters({ apiBase, state, update }) {
  const [fields, setFields]      = useState([])
  const [fieldsLoading, setFL]   = useState(false)
  const [error, setError]        = useState(null)
  const [search, setSearch]      = useState('')

  // Field key (örn. "Status" veya "widget:OzelKod") → { op, value }.
  // Backend JSON'dan parse edilir, kullanıcı düzenledikçe in-place güncellenir.
  const [filters, setFilters] = useState(() => parseFilterJson(state.sourceFilterJson))

  // Edit-mode'da backend'den state.sourceFilterJson geldiğinde filters'ı senkronla.
  const lastSyncedRef = React.useRef(state.sourceFilterJson)
  useEffect(() => {
    if (state.sourceFilterJson !== lastSyncedRef.current) {
      lastSyncedRef.current = state.sourceFilterJson
      setFilters(parseFilterJson(state.sourceFilterJson))
    }
  }, [state.sourceFilterJson])

  // Alanları çek — sadece form seçiliyse
  useEffect(() => {
    if (!state.sourceFormCode) { setFields([]); return }
    let cancelled = false
    setFL(true)
    fetch(`${apiBase}/forms/${state.sourceFormCode}/fields`, { credentials: 'same-origin' })
      .then(r => r.json())
      .then(d => {
        if (cancelled) return
        if (d?.success && Array.isArray(d.fields)) {
          // Pre-flight: sadece Header section alanları
          const headerOnly = d.fields.filter(f => (f.section || 'Header') === 'Header')
          setFields(headerOnly)
        }
      })
      .catch(e => { if (!cancelled) setError(e.message) })
      .finally(() => { if (!cancelled) setFL(false) })
    return () => { cancelled = true }
  }, [apiBase, state.sourceFormCode])

  /**
   * Filters map → backend JSON serialize.
   * - op boşsa veya geçersizse → atla
   * - needsValue=true op'lar için value boşsa → atla (kullanıcı henüz girmemiş)
   */
  const writeFilters = (nextMap) => {
    setFilters(nextMap)
    const cleaned = []
    for (const [fieldKey, entry] of Object.entries(nextMap)) {
      if (!entry || !entry.op) continue
      const opDef = OPERATORS.find(o => o.value === entry.op)
      if (!opDef || opDef.skip) continue
      if (opDef.needsValue && (entry.value === '' || entry.value == null)) continue
      const out = { field: fieldKey, op: entry.op }
      if (opDef.needsValue) out.value = String(entry.value)
      cleaned.push(out)
    }
    const serialized = cleaned.length === 0 ? null : JSON.stringify(cleaned)
    lastSyncedRef.current = serialized
    update({ sourceFilterJson: serialized })
  }

  const setOp = (fieldKey, op) => {
    const cur = filters[fieldKey] || { op: '', value: '' }
    writeFilters({ ...filters, [fieldKey]: { ...cur, op } })
  }
  const setVal = (fieldKey, value) => {
    const cur = filters[fieldKey] || { op: 'eq', value: '' }
    writeFilters({ ...filters, [fieldKey]: { ...cur, value } })
  }
  const clearAll = () => writeFilters({})

  const plainFields  = useMemo(() => fields.filter(f => f.isPlainField),  [fields])
  const widgetFields = useMemo(() => fields.filter(f => !f.isPlainField), [fields])

  const matchesSearch = (f) => {
    if (!search.trim()) return true
    const q = search.trim().toLowerCase()
    return (f.label || '').toLowerCase().includes(q) || (f.code || '').toLowerCase().includes(q)
  }

  // Aktif filtre sayısı (boş satırlar hariç)
  const activeCount = Object.values(filters).reduce((n, entry) => {
    if (!entry || !entry.op) return n
    const opDef = OPERATORS.find(o => o.value === entry.op)
    if (!opDef || opDef.skip) return n
    if (opDef.needsValue && (entry.value === '' || entry.value == null)) return n
    return n + 1
  }, 0)

  if (!state.sourceFormCode) {
    return (
      <div style={{ flex: 1, padding: 16, overflow: 'auto' }}>
        <div style={{
          padding: '20px', borderRadius: 10,
          background: 'var(--iw-amber-bg)', border: '1px solid var(--iw-amber-color)',
          color: 'var(--iw-amber-color)', fontSize: 13,
          display: 'flex', alignItems: 'center', gap: 10,
        }}>
          <AlertCircle size={20} />
          <span>Önce üstteki <strong>Form</strong> seçiciden bir kaynak form seçin.</span>
        </div>
      </div>
    )
  }

  const renderRow = (field) => {
    const isWidget = !field.isPlainField
    const fieldKey = isWidget ? `widget:${field.code}` : field.code
    const entry = filters[fieldKey] || { op: '', value: '' }
    const opDef = OPERATORS.find(o => o.value === entry.op) || OPERATORS[0]
    const isActive = entry.op && !opDef.skip
      && (!opDef.needsValue || (entry.value !== '' && entry.value != null))

    return (
      <div key={fieldKey} style={{
        display: 'grid',
        gridTemplateColumns: 'minmax(200px, 1.4fr) minmax(170px, 1fr) minmax(180px, 1.6fr)',
        alignItems: 'center', gap: 10,
        padding: '7px 12px',
        borderBottom: '1px dashed var(--iw-border)',
        background: isActive ? 'var(--iw-indigo-bg)' : 'transparent',
        transition: 'background 0.15s',
      }}>
        {/* Field label */}
        <div style={{
          fontSize: 12, color: 'var(--iw-text)',
          display: 'flex', alignItems: 'center', gap: 6,
          minWidth: 0,
        }}>
          {isWidget && (
            <span title="Widget alanı (form tasarımcısı)"
                  style={{ color: '#8b5cf6', fontSize: 12 }}>◈</span>
          )}
          <span style={{
            fontWeight: isActive ? 600 : 400,
            overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap',
          }} title={field.label || field.code}>
            {field.label || field.code}
          </span>
          {field.isRequired && (
            <span style={{ color: 'var(--iw-rose-color)', fontSize: 10 }}>*</span>
          )}
        </div>

        {/* Operator dropdown */}
        <select value={entry.op}
                onChange={e => setOp(fieldKey, e.target.value)}
                style={{
                  fontSize: 11, padding: '4px 6px',
                  border: '1px solid var(--iw-border)', borderRadius: 4,
                  background: 'var(--iw-bg)', color: 'var(--iw-text)',
                }}>
          {OPERATORS.map(o => (
            <option key={o.value || '__none'} value={o.value}>{o.label}</option>
          ))}
        </select>

        {/* Değer inputu / placeholder */}
        {opDef.needsValue ? (
          <input type="text" value={entry.value || ''}
                 onChange={e => setVal(fieldKey, e.target.value)}
                 placeholder={
                   entry.op === 'in'      ? 'A, B, C' :
                   entry.op === 'between' ? 'min, max' :
                   'Değer…'}
                 style={{
                   fontSize: 11, padding: '4px 6px',
                   border: '1px solid var(--iw-border)', borderRadius: 4,
                   background: 'var(--iw-bg)', color: 'var(--iw-text)',
                 }} />
        ) : entry.op && !opDef.skip ? (
          <span style={{
            padding: '4px 8px', fontSize: 11,
            color: 'var(--iw-muted)', fontStyle: 'italic',
          }}>
            (değer gerekmez)
          </span>
        ) : (
          <span style={{
            padding: '4px 8px', fontSize: 11,
            color: 'var(--iw-muted)', fontStyle: 'italic', opacity: 0.6,
          }}>
            — filtre uygulanmıyor —
          </span>
        )}
      </div>
    )
  }

  const filteredPlain  = plainFields.filter(matchesSearch)
  const filteredWidget = widgetFields.filter(matchesSearch)

  return (
    <div style={{ flex: 1, padding: 16, overflow: 'auto', display: 'flex', flexDirection: 'column', gap: 14 }}>
      {/* Açıklama paneli */}
      <div style={{
        padding: '12px 14px', borderRadius: 8,
        background: 'var(--iw-indigo-bg)', border: '1px solid var(--iw-indigo-color)',
        fontSize: 12, color: 'var(--iw-text)', lineHeight: 1.6,
      }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 8, fontWeight: 600, color: 'var(--iw-indigo-color)', marginBottom: 4 }}>
          <Filter size={14} /> Kısıt Kuralları (Pre-flight Filter)
        </div>
        <div>
          Tüm filtre alanları aşağıda listelenmiştir. Kullanmak istediğiniz alan için
          <strong> operatör</strong> seçip <strong>değer</strong> girin — boş bıraktığınız
          alanlar atlanır. Aktif tüm filtreler <strong>AND</strong> ile birleştirilir;
          koşulları sağlamayan kayıtlar entegrasyon kuyruğunda <strong>"Atlandı"</strong>
          olarak işaretlenir.
        </div>
      </div>

      {/* Toolbar: arama + sayaç + temizle */}
      <div style={{
        display: 'flex', alignItems: 'center', gap: 8,
        padding: '8px 12px', borderRadius: 8,
        border: '1px solid var(--iw-border)', background: 'var(--iw-slate-bg)',
      }}>
        <Search size={14} style={{ color: 'var(--iw-muted)' }} />
        <input type="text" value={search}
               onChange={e => setSearch(e.target.value)}
               placeholder="Alan ara…"
               style={{
                 flex: 1, fontSize: 12, padding: '4px 8px',
                 border: '1px solid var(--iw-border)', borderRadius: 4,
                 background: 'var(--iw-bg)', color: 'var(--iw-text)',
               }} />
        <span style={{ fontSize: 11, color: 'var(--iw-muted)' }}>
          <strong style={{ color: 'var(--iw-indigo-color)', fontSize: 13 }}>{activeCount}</strong> aktif filtre · {fields.length} alan
        </span>
        {activeCount > 0 && (
          <button type="button" onClick={clearAll}
                  className="iw-btn-ghost"
                  style={{ padding: '4px 10px', fontSize: 11 }}>
            Tümünü Temizle
          </button>
        )}
      </div>

      {fieldsLoading && (
        <div style={{ padding: 30, textAlign: 'center', color: 'var(--iw-muted)', fontSize: 12 }}>
          <Loader2 size={16} className="iw-spin" /> Alanlar yükleniyor…
        </div>
      )}

      {!fieldsLoading && error && (
        <div style={{ padding: 16, color: 'var(--iw-rose-color)', fontSize: 12 }}>
          ⚠ {error}
        </div>
      )}

      {!fieldsLoading && !error && fields.length === 0 && (
        <div style={{ padding: '30px 16px', textAlign: 'center', color: 'var(--iw-muted)', fontSize: 12 }}>
          Bu form için kayıtlı alan bulunamadı.
        </div>
      )}

      {!fieldsLoading && !error && filteredPlain.length === 0 && filteredWidget.length === 0 && fields.length > 0 && (
        <div style={{ padding: '20px 16px', textAlign: 'center', color: 'var(--iw-muted)', fontSize: 12 }}>
          Aramayla eşleşen alan yok.
        </div>
      )}

      {/* Plain (tablo) alanları */}
      {!fieldsLoading && !error && filteredPlain.length > 0 && (
        <div style={{
          border: '1px solid var(--iw-border)', borderRadius: 10,
          background: 'var(--iw-bg)', overflow: 'hidden',
        }}>
          <div style={{
            padding: '8px 14px', borderBottom: '1px solid var(--iw-border)',
            background: 'var(--iw-slate-bg)', fontSize: 12, fontWeight: 600,
            color: 'var(--iw-text)',
            display: 'flex', alignItems: 'center', gap: 6,
          }}>
            Tablo Alanları
            <span style={{ fontSize: 10, color: 'var(--iw-muted)', fontWeight: 400 }}>
              ({filteredPlain.length}{search ? ` / ${plainFields.length}` : ''})
            </span>
          </div>
          <div>
            {filteredPlain.map(renderRow)}
          </div>
        </div>
      )}

      {/* Widget alanları */}
      {!fieldsLoading && !error && filteredWidget.length > 0 && (
        <div style={{
          border: '1px solid var(--iw-border)', borderRadius: 10,
          background: 'var(--iw-bg)', overflow: 'hidden',
        }}>
          <div style={{
            padding: '8px 14px', borderBottom: '1px solid var(--iw-border)',
            background: 'var(--iw-slate-bg)', fontSize: 12, fontWeight: 600,
            color: 'var(--iw-text)',
            display: 'flex', alignItems: 'center', gap: 6,
          }}>
            <span style={{ color: '#8b5cf6' }}>◈</span>
            Widget Alanları
            <span style={{ fontSize: 10, color: 'var(--iw-muted)', fontWeight: 400 }}>
              ({filteredWidget.length}{search ? ` / ${widgetFields.length}` : ''})
            </span>
          </div>
          <div>
            {filteredWidget.map(renderRow)}
          </div>
        </div>
      )}

      {/* JSON Preview (debug) */}
      {activeCount > 0 && (
        <details style={{
          border: '1px solid var(--iw-border)', borderRadius: 8, padding: '8px 12px',
          background: 'var(--iw-slate-bg)', fontSize: 11,
        }}>
          <summary style={{ cursor: 'pointer', color: 'var(--iw-muted)', fontWeight: 600 }}>
            Saklanan JSON ({state.sourceFilterJson?.length || 0} karakter)
          </summary>
          <pre style={{
            margin: '8px 0 0', padding: 8, borderRadius: 4,
            background: 'var(--iw-bg)', color: 'var(--iw-text)',
            fontSize: 10, overflow: 'auto', maxHeight: 200,
          }}>
            {state.sourceFilterJson ? JSON.stringify(JSON.parse(state.sourceFilterJson), null, 2) : '(boş)'}
          </pre>
        </details>
      )}
    </div>
  )
}
