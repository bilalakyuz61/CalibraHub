import { useState, useEffect } from 'react'
import {
  DndContext, closestCenter, PointerSensor, useSensor, useSensors,
  DragOverlay, useDroppable, useDraggable,
} from '@dnd-kit/core'
import {
  ChevronLeft, Save, Loader2, Trash2, GripVertical, Eye,
  Type, AlignLeft, Hash, Calendar, List, ToggleLeft, Paperclip,
  ChevronDown, Plus, X,
} from 'lucide-react'

var BASE = '/BpmForm'

var FIELD_TYPES = [
  { type: 'Text',     label: 'Kısa Metin',  icon: Type },
  { type: 'Textarea', label: 'Uzun Metin',  icon: AlignLeft },
  { type: 'Number',   label: 'Sayı',        icon: Hash },
  { type: 'Date',     label: 'Tarih',       icon: Calendar },
  { type: 'Dropdown', label: 'Liste',       icon: List },
  { type: 'YesNo',    label: 'Evet/Hayır',  icon: ToggleLeft },
  { type: 'File',     label: 'Dosya',       icon: Paperclip },
]
var FIELD_TYPE_MAP = Object.fromEntries(FIELD_TYPES.map(t => [t.type, t]))

var SPAN_OPTIONS = [
  { span: 12, label: '1/1'  },
  { span: 8,  label: '2/3'  },
  { span: 6,  label: '1/2'  },
  { span: 4,  label: '1/3'  },
  { span: 3,  label: '1/4'  },
]

var BAND_COLORS = [
  '#6366f1','#7c3aed','#0284c7','#0891b2','#059669',
  '#d97706','#dc2626','#db2777','#4f46e5','#065f46',
]

function getCsrf() {
  var el = document.querySelector('input[name="__RequestVerificationToken"]')
  return el ? el.value : ''
}
function postJson(url, body) {
  return fetch(BASE + url, {
    method: 'POST', credentials: 'same-origin',
    headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': getCsrf() },
    body: JSON.stringify(body),
  }).then(r => r.json())
}
function getJson(url) {
  return fetch(BASE + url, { credentials: 'same-origin' }).then(r => r.json())
}
function toKey(label) {
  return label.trim().toLowerCase()
    .replace(/ğ/g, 'g').replace(/ü/g, 'u').replace(/ş/g, 's')
    .replace(/ı/g, 'i').replace(/ö/g, 'o').replace(/ç/g, 'c')
    .replace(/[^a-z0-9]+/g, '_').replace(/^_|_$/g, '')
}

/* ── Tool item (left panel, draggable) ────────────────────────────────────── */
function ToolItem({ fieldType }) {
  var meta = FIELD_TYPE_MAP[fieldType]
  var Icon = meta.icon
  var { attributes, listeners, setNodeRef, isDragging } = useDraggable({
    id: 'palette__' + fieldType,
    data: { source: 'palette', fieldType },
  })
  return (
    <div ref={setNodeRef} {...listeners} {...attributes}
      className={'bpmd-tool-item' + (isDragging ? ' bpmd-tool-item--dragging' : '')}>
      <div className="bpmd-tool-item__icon"><Icon size={14} /></div>
      <span className="bpmd-tool-item__label">{meta.label}</span>
    </div>
  )
}

/* ── Field input mock (WYSIWYG preview) ──────────────────────────────────── */
function InputMock({ field }) {
  switch (field.fieldType) {
    case 'Textarea':
      return <div className="bpmd-fp-mock bpmd-fp-mock--textarea"><span>{field.placeholder || ''}</span></div>
    case 'Number':
      return <div className="bpmd-fp-mock"><span style={{ fontFamily: 'ui-monospace, Menlo, Consolas, monospace' }}>{field.placeholder || '0'}</span></div>
    case 'Date':
      return <div className="bpmd-fp-mock"><span>gg.aa.yyyy</span><Calendar size={12} style={{ marginLeft: 'auto', opacity: .4 }} /></div>
    case 'Dropdown':
      return <div className="bpmd-fp-mock"><span>{field.placeholder || 'Seçiniz…'}</span><ChevronDown size={12} style={{ marginLeft: 'auto', opacity: .4 }} /></div>
    case 'YesNo':
      return <div className="bpmd-fp-yesno-mock"><span>Evet</span><span>Hayır</span></div>
    case 'File':
      return <div className="bpmd-fp-mock bpmd-fp-mock--file"><Paperclip size={11} /><span>{field.placeholder || 'Dosya seç…'}</span></div>
    default:
      return <div className="bpmd-fp-mock"><span>{field.placeholder || ''}</span></div>
  }
}

/* ── Field preview card (draggable, WYSIWYG) ─────────────────────────────── */
function FieldPreview({ field, selected, onSelect, onDelete }) {
  var { attributes, listeners, setNodeRef, isDragging } = useDraggable({
    id: 'field__' + (field.id || field._tempId),
    data: { source: 'field', field },
  })
  return (
    <div ref={setNodeRef}
      className={'bpmd-field-preview' +
        (selected  ? ' bpmd-field-preview--selected'  : '') +
        (isDragging ? ' bpmd-field-preview--dragging' : '')}
      onClick={() => onSelect(field)}
      style={{ flex: `0 0 ${(field.layoutColSpan / 12 * 100).toFixed(2)}%`,
               maxWidth: `${(field.layoutColSpan / 12 * 100).toFixed(2)}%` }}>
      <span className="bpmd-fp-grip" {...listeners} {...attributes}>
        <GripVertical size={11} />
      </span>
      <div className="bpmd-fp-inner">
        <div className="bpmd-fp-label">
          {field.label || <em style={{ opacity: .5 }}>isimsiz</em>}
          {field.isRequired && <span className="bpmd-fp-req"> *</span>}
        </div>
        <InputMock field={field} />
      </div>
      <button className="bpmd-fp-del" onClick={e => { e.stopPropagation(); onDelete(field) }}>
        <X size={11} />
      </button>
    </div>
  )
}

/* ── Drop zones ──────────────────────────────────────────────────────────── */
function BetweenZone({ rowIdx, colIdx }) {
  var id = 'between__' + rowIdx + '__' + colIdx
  var { setNodeRef, isOver } = useDroppable({ id, data: { rowIdx, colIdx } })
  return <div ref={setNodeRef} className={'bpmd-between' + (isOver ? ' bpmd-between--over' : '')} />
}

function BandDropZone({ rowIdx }) {
  var { setNodeRef, isOver } = useDroppable({ id: 'row__' + rowIdx, data: { rowIdx } })
  return (
    <div ref={setNodeRef} className={'bpmd-band-empty' + (isOver ? ' bpmd-band-empty--over' : '')}>
      Araç panelinden alan sürükleyin veya tıklayın
    </div>
  )
}

/* ── Section separator ───────────────────────────────────────────────────── */
function SectionHead({ children }) {
  return <div className="bpmd-section-head">{children}</div>
}

/* ── Right panel — Form properties ──────────────────────────────────────── */
function FormProps({ defName, defDesc, wfId, wfOptions, onName, onDesc, onWf, dirty }) {
  return (
    <div className="bpmd-props-body">
      <SectionHead>Genel</SectionHead>
      <label className="bpmd-prop-label">Form Adı</label>
      <input className="bpmd-prop-input" value={defName} placeholder="Form adı…"
        onChange={e => onName(e.target.value)} />
      <label className="bpmd-prop-label">Açıklama</label>
      <textarea className="bpmd-prop-input" rows={3} value={defDesc} placeholder="Form açıklaması…"
        onChange={e => onDesc(e.target.value)} />

      <SectionHead style={{ marginTop: 16 }}>Bağlantı</SectionHead>
      <label className="bpmd-prop-label">Workflow</label>
      <select className="bpmd-prop-input" value={wfId} onChange={e => onWf(e.target.value)}>
        <option value="">— Bağlama —</option>
        {wfOptions.map(w => <option key={w.id} value={w.id}>{w.title}</option>)}
      </select>
    </div>
  )
}

/* ── Right panel — Field properties ─────────────────────────────────────── */
function FieldProps({ field, onChange }) {
  if (!field) return (
    <div className="bpmd-props-empty">
      Bir alana tıklayarak özelliklerini düzenleyin
    </div>
  )
  function upd(k, v) { onChange({ ...field, [k]: v }) }
  return (
    <div className="bpmd-props-body">
      <SectionHead>Genel</SectionHead>
      <label className="bpmd-prop-label">Etiket</label>
      <input className="bpmd-prop-input" value={field.label}
        onChange={e => { upd('label', e.target.value); upd('key', toKey(e.target.value)) }} />

      <label className="bpmd-prop-label">Değişken Adı</label>
      <input className="bpmd-prop-input bpmd-prop-input--mono" value={field.key}
        onChange={e => upd('key', e.target.value)} />

      <label className="bpmd-prop-label">Alan Tipi</label>
      <select className="bpmd-prop-input" value={field.fieldType}
        onChange={e => upd('fieldType', e.target.value)}>
        {FIELD_TYPES.map(t => <option key={t.type} value={t.type}>{t.label}</option>)}
      </select>

      <SectionHead style={{ marginTop: 14 }}>Boyut</SectionHead>
      <div className="bpmd-span-btns">
        {SPAN_OPTIONS.map(opt => (
          <button key={opt.span}
            className={'bpmd-span-btn' + (field.layoutColSpan === opt.span ? ' bpmd-span-btn--active' : '')}
            onClick={() => upd('layoutColSpan', opt.span)}>
            {opt.label}
          </button>
        ))}
      </div>

      <SectionHead style={{ marginTop: 14 }}>Gelişmiş</SectionHead>
      {field.fieldType === 'Dropdown' && (
        <>
          <label className="bpmd-prop-label">Seçenekler (her satıra bir)</label>
          <textarea className="bpmd-prop-input" rows={4}
            value={field._optionsText || ''}
            onChange={e => {
              upd('_optionsText', e.target.value)
              upd('optionsJson', JSON.stringify(e.target.value.split('\n').map(s => s.trim()).filter(Boolean)))
            }} />
        </>
      )}
      <label className="bpmd-prop-label">Yer Tutucu</label>
      <input className="bpmd-prop-input" value={field.placeholder || ''}
        onChange={e => upd('placeholder', e.target.value)} />
      <label className="bpmd-prop-label">Varsayılan Değer</label>
      <input className="bpmd-prop-input" value={field.defaultValue || ''}
        onChange={e => upd('defaultValue', e.target.value)} />
      <label className="bpmd-prop-switch">
        <input type="checkbox" checked={!!field.isRequired}
          onChange={e => upd('isRequired', e.target.checked)} />
        <span>Zorunlu alan</span>
      </label>
    </div>
  )
}

/* ── Ruler ───────────────────────────────────────────────────────────────── */
function Ruler() {
  var ticks = []
  for (var i = 0; i <= 20; i++) ticks.push(i)
  return (
    <div className="bpmd-ruler">
      <div className="bpmd-ruler-corner" />
      <div className="bpmd-ruler-track">
        {ticks.map(i => (
          <div key={i} className="bpmd-ruler-tick" style={{ left: (i / 20 * 100) + '%' }}>
            {i > 0 && i % 2 === 0 && <span>{i * 10}</span>}
          </div>
        ))}
      </div>
    </div>
  )
}

/* ══════════════════════════════════════════════════════════════════════════
   Main Designer
   ══════════════════════════════════════════════════════════════════════════ */
export default function BpmFormDesigner({ formId }) {
  var [defName, setDefName]        = useState('')
  var [defDesc, setDefDesc]        = useState('')
  var [wfId, setWfId]              = useState('')
  var [fields, setFields]          = useState([])
  var [selectedField, setSelected] = useState(null)
  var [loading, setLoading]        = useState(!!formId)
  var [saving, setSaving]          = useState(false)
  var [dirty, setDirty]            = useState(false)
  var [savedDefId, setSavedDefId]  = useState(formId)
  var [wfOptions, setWfOptions]    = useState([])
  var [tempId, setTempId]          = useState(-1)
  var [rowCount, setRowCount]      = useState(1)
  var [activeItem, setActiveItem]  = useState(null)
  var [rightTab, setRightTab]      = useState('form') // 'form' | 'field'

  var sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 6 } }))

  useEffect(function () {
    fetch('/WorkflowDefinition/DefinitionsBoardConfig', { credentials: 'same-origin' })
      .then(r => r.json()).then(cfg => setWfOptions(cfg.entities || [])).catch(() => {})

    if (!formId) { setLoading(false); return }
    getJson('/GetDefinitionJson?id=' + formId)
      .then(function (res) {
        if (!res.success) return
        var d = res.data
        setDefName(d.definition.name)
        setDefDesc(d.definition.description || '')
        setWfId(d.definition.workflowDefinitionId || '')
        setFields(d.fields.map(f => ({
          ...f,
          _optionsText: f.optionsJson ? JSON.parse(f.optionsJson).join('\n') : '',
        })))
        setSavedDefId(d.definition.id)
        var maxRow = Math.max(0, ...d.fields.map(f => f.layoutRow || 0))
        setRowCount(maxRow + 1)
      })
      .catch(e => console.error('[BpmFormDesigner] load', e))
      .finally(() => setLoading(false))
  }, [formId])

  function buildVisualRows(flds, minRows) {
    var rowMap = {}
    flds.forEach(function (f) {
      var r = f.layoutRow || 0
      if (!rowMap[r]) rowMap[r] = []
      rowMap[r].push(f)
    })
    var maxRow = flds.length === 0 ? -1 : Math.max(...flds.map(f => f.layoutRow || 0))
    var total  = Math.max(maxRow + 1, minRows)
    var result = []
    for (var i = 0; i < total; i++)
      result.push((rowMap[i] || []).sort((a, b) => (a.layoutCol || 0) - (b.layoutCol || 0)))
    return result
  }

  function handleAddRow() {
    setRowCount(rc => rc + 1)
    setDirty(true)
  }

  function handleDeleteRow(rowIdx) {
    var inRow = fields.filter(f => (f.layoutRow || 0) === rowIdx)
    if (inRow.length > 0) {
      window.showConfirm?.({
        title: 'Satırı Sil',
        message: `Satır ${rowIdx + 1} içindeki ${inRow.length} alan da silinecek. Devam edilsin mi?`,
        okLabel: 'Evet, Sil',
      }).then(function (ok) {
        if (!ok) return
        inRow.forEach(async f => { if (f.id) await postJson('/DeleteFieldJson', { fieldId: f.id }) })
        setFields(fs => fs
          .filter(f => (f.layoutRow || 0) !== rowIdx)
          .map(f => (f.layoutRow || 0) > rowIdx ? { ...f, layoutRow: (f.layoutRow || 0) - 1 } : f))
        setRowCount(rc => Math.max(1, rc - 1))
        if (selectedField && (selectedField.layoutRow || 0) === rowIdx) setSelected(null)
        setDirty(true)
      })
    } else {
      setRowCount(rc => Math.max(1, rc - 1))
    }
  }

  function handleAddField(fieldType, rowIdx, colIdx) {
    var id   = tempId
    setTempId(id - 1)
    var rows = buildVisualRows(fields, rowCount)
    var row  = rows[rowIdx] || []
    var col  = colIdx !== undefined ? colIdx : row.length
    var f = {
      id: 0, _tempId: id, formDefinitionId: savedDefId || 0,
      key: '', label: '', fieldType,
      isRequired: false, sortOrder: fields.length,
      optionsJson: null, _optionsText: '', placeholder: '', defaultValue: '',
      layoutRow: rowIdx, layoutCol: col, layoutColSpan: 12,
    }
    setFields(function (fs) {
      var shifted = fs.map(x =>
        (x.layoutRow || 0) === rowIdx && (x.layoutCol || 0) >= col
          ? { ...x, layoutCol: (x.layoutCol || 0) + 1 } : x)
      return [...shifted, f]
    })
    setSelected(f)
    setRightTab('field')
    setDirty(true)
  }

  function handleDragStart(event) { setActiveItem(event.active.data.current) }

  function handleDragEnd(event) {
    setActiveItem(null)
    var { active, over } = event
    if (!over) return
    var src  = active.data.current
    var dest = over.data.current
    if (!dest) return
    var targetRow = dest.rowIdx !== undefined ? dest.rowIdx : 0
    var targetCol = dest.colIdx !== undefined ? dest.colIdx : 0

    if (src.source === 'palette') {
      handleAddField(src.fieldType, targetRow, targetCol)
    } else if (src.source === 'field') {
      var df    = src.field
      var dragId = df.id || df._tempId
      setFields(function (fs) {
        var oldRow = df.layoutRow || 0
        var oldCol = df.layoutCol || 0
        var reindexed = fs.map(x => {
          if ((x.id || x._tempId) === dragId) return null
          if ((x.layoutRow || 0) === oldRow && (x.layoutCol || 0) > oldCol)
            return { ...x, layoutCol: (x.layoutCol || 0) - 1 }
          return x
        }).filter(Boolean)
        reindexed = reindexed.map(x =>
          (x.layoutRow || 0) === targetRow && (x.layoutCol || 0) >= targetCol
            ? { ...x, layoutCol: (x.layoutCol || 0) + 1 } : x)
        reindexed.push({ ...df, layoutRow: targetRow, layoutCol: targetCol })
        return reindexed
      })
      setDirty(true)
    }
  }

  function handleFieldUpdate(updated) {
    setSelected(updated)
    setFields(fs => fs.map(x =>
      (x.id || x._tempId) === (updated.id || updated._tempId) ? updated : x))
    setDirty(true)
  }

  async function handleDeleteField(field) {
    if (field.id) {
      var ok = await window.showConfirm?.({ title: 'Alan Sil', message: `"${field.label}" silinsin mi?` })
      if (!ok) return
      await postJson('/DeleteFieldJson', { fieldId: field.id })
    }
    var deletedId  = field.id || field._tempId
    var deletedRow = field.layoutRow || 0
    var deletedCol = field.layoutCol || 0
    setFields(fs => fs
      .filter(x => (x.id || x._tempId) !== deletedId)
      .map(x =>
        (x.layoutRow || 0) === deletedRow && (x.layoutCol || 0) > deletedCol
          ? { ...x, layoutCol: (x.layoutCol || 0) - 1 } : x))
    if (selectedField && (selectedField.id || selectedField._tempId) === deletedId)
      setSelected(null)
    setDirty(true)
  }

  function handleSelectField(f) {
    setSelected(f)
    setRightTab('field')
  }

  async function handleSave() {
    setSaving(true)
    try {
      var defRes = await postJson('/SaveDefinitionJson', {
        id: savedDefId || null,
        name: defName,
        description: defDesc,
        workflowDefinitionId: wfId ? parseInt(wfId) : null,
        isActive: true,
      })
      if (!defRes.success) { alert(defRes.message); return }
      var defId = defRes.id
      setSavedDefId(defId)

      var sorted = [...fields].sort((a, b) =>
        ((a.layoutRow || 0) - (b.layoutRow || 0)) || ((a.layoutCol || 0) - (b.layoutCol || 0)))
      var newFields = []
      for (var i = 0; i < sorted.length; i++) {
        var f = { ...sorted[i], sortOrder: i, formDefinitionId: defId }
        var fRes = await postJson('/SaveFieldJson', {
          id: f.id || null, formDefinitionId: defId,
          key: f.key || toKey(f.label), label: f.label,
          fieldType: f.fieldType, isRequired: f.isRequired, sortOrder: f.sortOrder,
          optionsJson: f.optionsJson, placeholder: f.placeholder, defaultValue: f.defaultValue,
          layoutRow: f.layoutRow || 0, layoutCol: f.layoutCol || 0, layoutColSpan: f.layoutColSpan || 12,
        })
        newFields.push(fRes.success ? { ...f, id: fRes.id } : f)
      }
      setFields(newFields)
      setDirty(false)
      if (!formId && defId) window.location.href = '/BpmForm/FormDesigner?id=' + defId
    } catch (ex) {
      alert('Kayıt hatası: ' + ex.message)
    } finally { setSaving(false) }
  }

  if (loading) return (
    <div className="bpmd-loading"><Loader2 size={22} className="nw-spin" /> Yükleniyor…</div>
  )

  var visualRows = buildVisualRows(fields, rowCount)

  return (
    <DndContext sensors={sensors} collisionDetection={closestCenter}
      onDragStart={handleDragStart} onDragEnd={handleDragEnd}>

      <div className="bpmd-root">

        {/* ── Toolbar ──────────────────────────────────────────────────────── */}
        <div className="bpmd-toolbar">
          <button className="bpmd-toolbar-back"
            onClick={() => window.location.href = '/BpmForm/Forms'}>
            <ChevronLeft size={15} /> Liste
          </button>
          <div className="bpmd-toolbar-sep" />
          <input className="bpmd-toolbar-name" value={defName} placeholder="Form Adı…"
            onChange={e => { setDefName(e.target.value); setDirty(true) }} />
          {dirty && <span className="bpmd-toolbar-dirty" title="Kaydedilmemiş değişiklik" />}
          <div style={{ flex: 1 }} />
          <div className="bpmd-zoom-ctrl">
            <span className="bpmd-zoom-label">100%</span>
          </div>
          {savedDefId && (
            <a href={`/BpmForm/FormFill?id=${savedDefId}`} target="_blank"
              className="bpmd-toolbar-btn">
              <Eye size={14} /> Önizle
            </a>
          )}
          <button className="bpmd-toolbar-btn bpmd-toolbar-btn--primary"
            onClick={handleSave} disabled={saving}>
            {saving ? <Loader2 size={14} className="nw-spin" /> : <Save size={14} />} Kaydet
          </button>
        </div>

        <div className="bpmd-body">

          {/* ── Left panel ───────────────────────────────────────────────── */}
          <div className="bpmd-left">
            <SectionHead>Araçlar</SectionHead>
            <div className="bpmd-tool-grid">
              {FIELD_TYPES.map(t => <ToolItem key={t.type} fieldType={t.type} />)}
            </div>

            <SectionHead style={{ marginTop: 20 }}>Form Yapısı</SectionHead>
            <div className="bpmd-structure-list">
              {visualRows.map((rowFields, rowIdx) => (
                <div key={rowIdx} className="bpmd-structure-item">
                  <div className="bpmd-structure-dot"
                    style={{ background: BAND_COLORS[rowIdx % BAND_COLORS.length] }} />
                  <span className="bpmd-structure-label">Satır {rowIdx + 1}</span>
                  <span className="bpmd-structure-count">{rowFields.length} alan</span>
                  <button className="bpmd-structure-del"
                    onClick={() => handleDeleteRow(rowIdx)} title="Satırı sil">
                    <X size={10} />
                  </button>
                </div>
              ))}
            </div>
            <button className="bpmd-add-band-btn" onClick={handleAddRow}>
              <Plus size={12} /> Satır Ekle
            </button>
          </div>

          {/* ── Center canvas ────────────────────────────────────────────── */}
          <div className="bpmd-center">
            <Ruler />
            <div className="bpmd-paper-wrap">
              <div className="bpmd-paper">

                {/* Form title band */}
                <div className="bpmd-band bpmd-band--title">
                  <div className="bpmd-band-head">
                    <div className="bpmd-band-color-bar" style={{ background: '#6366f1' }} />
                    <span className="bpmd-band-name">FORM BAŞLIĞI</span>
                    <span className="bpmd-band-tag">{defName || '—'}</span>
                  </div>
                  <div className="bpmd-band-title-body">
                    <div className="bpmd-title-preview">{defName || <em style={{ opacity: .4 }}>Form Adı</em>}</div>
                    {defDesc && <div className="bpmd-desc-preview">{defDesc}</div>}
                  </div>
                </div>

                {/* Row bands */}
                {visualRows.map((rowFields, rowIdx) => {
                  var color = BAND_COLORS[rowIdx % BAND_COLORS.length]
                  return (
                    <div key={rowIdx} className="bpmd-band">
                      <div className="bpmd-band-head">
                        <div className="bpmd-band-color-bar" style={{ background: color }} />
                        <span className="bpmd-band-name">SATIR {rowIdx + 1}</span>
                        {rowFields.length > 0 && (
                          <span className="bpmd-band-tag" style={{ background: color + '22', color }}>
                            {rowFields.length} alan
                          </span>
                        )}
                      </div>
                      <div className="bpmd-band-body">
                        <BetweenZone rowIdx={rowIdx} colIdx={0} />
                        {rowFields.map((f, colIdx) => (
                          <div key={f.id || f._tempId} style={{
                            display: 'flex', alignItems: 'stretch',
                            flex: `0 0 ${(f.layoutColSpan / 12 * 100).toFixed(2)}%`,
                            maxWidth: `${(f.layoutColSpan / 12 * 100).toFixed(2)}%`,
                          }}>
                            <FieldPreview
                              field={f}
                              selected={selectedField &&
                                (selectedField.id || selectedField._tempId) === (f.id || f._tempId)}
                              onSelect={handleSelectField}
                              onDelete={handleDeleteField}
                            />
                            <BetweenZone rowIdx={rowIdx} colIdx={colIdx + 1} />
                          </div>
                        ))}
                        {rowFields.length === 0 && <BandDropZone rowIdx={rowIdx} />}
                      </div>
                    </div>
                  )
                })}

              </div>
            </div>
          </div>

          {/* ── Right panel ──────────────────────────────────────────────── */}
          <div className="bpmd-right">
            <div className="bpmd-tabs">
              <button className={'bpmd-tab' + (rightTab === 'form'  ? ' bpmd-tab--active' : '')}
                onClick={() => setRightTab('form')}>FORM</button>
              <button className={'bpmd-tab' + (rightTab === 'field' ? ' bpmd-tab--active' : '')}
                onClick={() => { setRightTab('field') }}
                style={{ opacity: selectedField ? 1 : .4 }}>
                ALAN
              </button>
            </div>

            {rightTab === 'form' ? (
              <FormProps
                defName={defName} defDesc={defDesc} wfId={wfId}
                wfOptions={wfOptions} dirty={dirty}
                onName={v => { setDefName(v); setDirty(true) }}
                onDesc={v => { setDefDesc(v); setDirty(true) }}
                onWf={v => { setWfId(v); setDirty(true) }}
              />
            ) : (
              <FieldProps field={selectedField} onChange={handleFieldUpdate} />
            )}
          </div>

        </div>
      </div>

      {/* Drag overlay */}
      <DragOverlay>
        {activeItem?.source === 'palette' && (
          <div className="bpmd-drag-overlay">
            {FIELD_TYPE_MAP[activeItem.fieldType]?.label}
          </div>
        )}
        {activeItem?.source === 'field' && (
          <div className="bpmd-drag-overlay">
            {activeItem.field.label || '(isimsiz)'}
          </div>
        )}
      </DragOverlay>

    </DndContext>
  )
}
