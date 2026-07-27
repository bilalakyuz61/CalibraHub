import { useState, useMemo, useEffect, useCallback } from 'react'
import {
  ChevronRight, ChevronDown, Plus, PlusCircle, Edit2, Trash2,
  X, Check, Search, Workflow, Cpu, Settings2, Filter, Download, Loader2,
  Package, Cog, Hash, GripVertical, Timer,
} from 'lucide-react'
import {
  DndContext, closestCenter, PointerSensor, KeyboardSensor, useSensor, useSensors,
} from '@dnd-kit/core'
import {
  arrayMove, SortableContext, verticalListSortingStrategy,
  sortableKeyboardCoordinates, useSortable,
} from '@dnd-kit/sortable'
import { CSS } from '@dnd-kit/utilities'
import SmartBoardConfigPanel from '../CalibraSmartBoard/SmartBoardConfigPanel'
import SmartBoardFilterPanel, { entityMatchesFilters } from '../CalibraSmartBoard/SmartBoardFilterPanel'
import { loadWidgetConfig } from '../../services/widgetConfigService'
import './RoutingTree.css'

function getCsrf() {
  var el = document.querySelector('input[name="__RequestVerificationToken"]')
  return el ? el.value : ''
}
async function apiPost(url, body) {
  var headers = { Accept: 'application/json', RequestVerificationToken: getCsrf() }
  var opts = { method: 'POST', credentials: 'same-origin', headers }
  if (body !== null && body !== undefined) {
    headers['Content-Type'] = 'application/json'
    opts.body = JSON.stringify(body)
  }
  return (await fetch(url, opts)).json()
}
async function apiGet(url) {
  return (await fetch(url, { credentials: 'same-origin', headers: { Accept: 'application/json' } })).json()
}

// ── DurationUnit enum normalize — API JsonStringEnumConverter ile string doner
//   ("Minute"/"Hour"); React tarafinda integer olarak karsilastirilir (bkz. CLAUDE.md
//   "React / Frontend — API'den Enum Yukleme Kurali"). Kaydetmede integer gonderilir,
//   backend allowIntegerValues:true ile kabul eder.
var DURATION_UNIT_NUM   = { Minute: 1, Hour: 2 }
var DURATION_UNIT_LABEL = { 1: 'Dakika', 2: 'Saat' }
function normalizeDurationUnit(v) {
  if (typeof v === 'number') return v
  if (typeof v === 'string' && v in DURATION_UNIT_NUM) return DURATION_UNIT_NUM[v]
  var n = parseInt(v, 10)
  return isNaN(n) ? 1 : n
}
// Decimal alanlari (Quantity/DurationPerUnit) DECIMAL(18,4)'ten geldigi icin "1.0000" gibi
// artik sifirli gelebilir — Number() ile sadelestirilmis gosterim (ondalik-ayari altyapisi
// bu ekranda kullanilmiyor; "sure" kategorisi zaten decimalKind setinde yok, bkz. rapor).
function fmtDec(v) {
  var n = Number(v)
  return isNaN(n) ? v : n.toString()
}

// ── Widget chip — backend'den gelen dynamic widget'lari render eder ────────
//   visibleIds + order verilirse kullanici config'ine gore filtre/sirala.
function WidgetChips({ widgets, size, visibleIds, order }) {
  if (!Array.isArray(widgets) || widgets.length === 0) return null
  var tileClass = size === 'sm' ? 'rt-tile rt-tile--sm' : 'rt-tile'

  var list = widgets
  if (Array.isArray(visibleIds)) {
    var visSet = new Set(visibleIds)
    list = widgets.filter(function (w) { return visSet.has(w.id) })
  }
  if (Array.isArray(order) && order.length > 0) {
    var pos = {}
    order.forEach(function (id, i) { pos[id] = i })
    list = list.slice().sort(function (a, b) {
      var pa = pos[a.id], pb = pos[b.id]
      if (pa == null) pa = 999; if (pb == null) pb = 999
      return pa - pb
    })
  }
  if (list.length === 0) return null

  return (
    <>
      {list.map(function (w, i) {
        var val = w.value
        if (val == null || val === '') val = '—'
        var dt = (w.dataType || '').toLowerCase()
        var detail = w.detail || (dt === 'currency' ? 'TL' : (dt === 'percent' ? '%' : null))
        return (
          <div key={(w.id || 'w') + '_' + i} className={tileClass} title={w.label}>
            <span className="rt-tile__label">{w.label}</span>
            <span className="rt-tile__value">
              {val}
              {detail && <span className="rt-tile__detail"> {detail}</span>}
            </span>
          </div>
        )
      })}
    </>
  )
}

// ── Delete modal ───────────────────────────────────────────────────────────
function DeleteModal({ target, onConfirm, onCancel }) {
  useEffect(() => {
    function onKey(e) { if (e.key === 'Escape') onCancel() }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [onCancel])

  var title = target.type === 'routing' ? 'Rotayı Sil'
    : target.type === 'machineTime' ? 'Makine Süresini Sil'
    : 'Operasyonu Kaldır'

  return (
    <div className="rt-del-backdrop" onClick={e => { if (e.target === e.currentTarget) onCancel() }}>
      <div className="rt-del-modal">
        <div className="rt-del-modal__head">
          <div className="rt-del-modal__icon"><Trash2 size={20} style={{ color: '#ef4444' }} /></div>
          <div>
            <div className="rt-del-modal__title">{title}</div>
            <div className="rt-del-modal__label">{target.label}</div>
          </div>
        </div>
        <p className="rt-del-modal__body">
          Bu işlem geri alınamaz.{target.type === 'routing' && ' Rotaya ait tüm operasyon adımları da silinecektir.'}
        </p>
        <div className="rt-del-modal__foot">
          <button className="rt-btn rt-btn--ghost" onClick={onCancel} autoFocus>Vazgeç</button>
          <button className="rt-btn rt-btn--danger" onClick={onConfirm}>Sil</button>
        </div>
      </div>
    </div>
  )
}

// ── Generic seçici modal (operasyon / makine / stok / kart grubu) ──────────
//   filterIds verilirse (Set<number>) liste o id'lerle sınırlanır (ör. rota ürünleri, Seq 46) —
//   kullanıcı "Tümünü Göster" ile filtreyi o oturumluk bypass edebilir.
function PickerModal({ lookupUrl, title, placeholder, onSelect, onClose, queryParam, filterIds, filterHint }) {
  var [list, setList]       = useState([])
  var [search, setSearch]   = useState('')
  var [loading, setLoading] = useState(true)
  var [showAll, setShowAll] = useState(false)

  // Server-side ararken queryParam var (ornek StockLookup ?q=)
  useEffect(() => {
    var url = lookupUrl
    if (queryParam && search) url = lookupUrl + (lookupUrl.indexOf('?') > -1 ? '&' : '?') + queryParam + '=' + encodeURIComponent(search)
    apiGet(url).then(d => {
      var items = Array.isArray(d) ? d : (Array.isArray(d?.items) ? d.items : [])
      setList(items); setLoading(false)
    })
    function onKey(e) { if (e.key === 'Escape') onClose() }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [lookupUrl, onClose, queryParam, search])

  // Id bazli kapsam filtresi (ör. rota ürünleri) — showAll ile bypass edilebilir.
  var scoped = useMemo(() => {
    if (!filterIds || showAll) return list
    return list.filter(it => filterIds.has(it.id))
  }, [list, filterIds, showAll])

  // Server-side query yoksa client-side filter
  var filtered = useMemo(() => {
    if (queryParam || !search) return scoped
    var q = search.toLowerCase()
    return scoped.filter(it => (it.code || '').toLowerCase().includes(q) ||
      (it.name || '').toLowerCase().includes(q) ||
      (it.machineCode || '').toLowerCase().includes(q) ||
      (it.machineName || '').toLowerCase().includes(q) ||
      (it.description || '').toLowerCase().includes(q))
  }, [scoped, search, queryParam])

  // Field normalize — code/name unified (CardGroupDto icin "name" yerine "description" gelir)
  function fieldsOf(it) {
    return {
      id:   it.id,
      code: it.code || it.machineCode || '',
      name: it.name || it.machineName || it.description || '',
    }
  }

  return (
    <div className="rt-picker-backdrop" onClick={e => { if (e.target === e.currentTarget) onClose() }}>
      <div className="rt-picker">
        <div className="rt-picker__head">
          <Search size={14} style={{ color: '#64748b', flexShrink: 0 }} />
          <input autoFocus className="rt-picker__search" value={search}
            onChange={e => setSearch(e.target.value)} placeholder={placeholder || 'Ara...'} />
          <button className="rt-picker__close" onClick={onClose}><X size={15} /></button>
        </div>
        {filterIds && !showAll && (
          <div className="rt-picker__filter-hint">
            <span>{filterHint || 'Kapsam sınırlı'}</span>
            <button type="button" onClick={() => setShowAll(true)}>Tümünü Göster</button>
          </div>
        )}
        <div className="rt-picker__list">
          {loading && <div className="rt-picker__info">Yükleniyor...</div>}
          {!loading && filtered.length === 0 && <div className="rt-picker__info">{title || 'Kayıt'} bulunamadı</div>}
          {filtered.map(it => {
            var f = fieldsOf(it)
            return (
              <button key={f.id} className="rt-picker__item" onClick={() => onSelect({ ...it, ...f })}>
                <span className="rt-picker__code">{f.code}</span>
                <span className="rt-picker__name">{f.name}</span>
              </button>
            )
          })}
        </div>
      </div>
    </div>
  )
}

// ── Geriye doğru uyum — eski ad
function OpPickerModal(props) {
  return <PickerModal {...props} title="Operasyon" placeholder="Operasyon ara..." />
}

// ── Sortable operasyon kartı ───────────────────────────────────────────────
function SortableOpCard({ op, routing, opUserCfg, onDelete, onAssignMachine, onEditMachineTimes }) {
  var sortable = useSortable({ id: op.id })
  var style = {
    transform: CSS.Transform.toString(sortable.transform),
    transition: sortable.transition,
    zIndex: sortable.isDragging ? 30 : 1,
    opacity: sortable.isDragging ? 0.85 : 1,
  }
  var dragClass = sortable.isDragging ? ' rt-row-wrap--dragging' : ''
  return (
    <div ref={sortable.setNodeRef} style={style}
      className={'rt-row-wrap rt-row-wrap--op' + dragClass}>
      <div className="rt-row rt-row--op">
        <button
          {...sortable.attributes}
          {...sortable.listeners}
          className="rt-drag-handle"
          title="Sıralamak için sürükle"
          onClick={e => e.stopPropagation()}
        >
          <GripVertical size={14} />
        </button>

        <span className="rt-seq-badge" title="Sıra no">{op.sequence}</span>

        <div className="rt-row__avatar rt-row__avatar--op">
          <Cog size={16} />
        </div>

        <div className="rt-row__main">
          <div className="rt-row__code">{op.operationCode}</div>
          <div className="rt-row__name">{op.operationName || '—'}</div>
          {op.notes && <div className="rt-row__desc">{op.notes}</div>}
        </div>

        <div className="rt-row__divider" />

        <div className="rt-row__tiles">
          {op.machineCode ? (
            <div className="rt-tile rt-tile--cyan" title={op.machineName || ''}>
              <span className="rt-tile__label">Makine</span>
              <span className="rt-tile__value">{op.machineCode}</span>
            </div>
          ) : (
            <div className="rt-tile rt-tile--muted">
              <span className="rt-tile__label">Makine</span>
              <span className="rt-tile__value rt-tile__value--muted">— atanmadı</span>
            </div>
          )}
          <WidgetChips widgets={op.widgets} size="sm"
            visibleIds={opUserCfg?.visibleIds}
            order={opUserCfg?.order} />
        </div>

        <div className="rt-row__actions">
          <button className="rt-act rt-act--machine" title="Makine Eşleştir"
            onClick={() => onAssignMachine(routing, op)}>
            <Cpu size={13} />
          </button>
          <button className="rt-act rt-act--times" title="Makine Süreleri"
            onClick={() => onEditMachineTimes(routing, op)}>
            <Timer size={13} />
          </button>
          <button className="rt-act rt-act--del" title="Sil"
            onClick={() => onDelete(routing, op)}>
            <Trash2 size={13} />
          </button>
        </div>
      </div>
    </div>
  )
}

// ── Rota inline form ───────────────────────────────────────────────────────
function RoutingForm({ initial, onSave, onCancel, saving }) {
  var [code, setCode]     = useState(initial?.code || '')
  var [name, setName]     = useState(initial?.name || '')
  var [desc, setDesc]     = useState(initial?.description || '')
  var [active, setActive] = useState(initial?.isActive ?? true)

  function submit(e) {
    e.preventDefault()
    if (!code.trim() || !name.trim()) return
    onSave({ code: code.trim(), name: name.trim(), description: desc.trim() || null, isActive: active })
  }

  return (
    <form onSubmit={submit} className="rt-inline-form">
      <input autoFocus className="rt-fi rt-fi--code" value={code}
        onChange={e => setCode(e.target.value)} placeholder="Kod *" required />
      <input className="rt-fi rt-fi--name" value={name}
        onChange={e => setName(e.target.value)} placeholder="Ad *" required />
      <input className="rt-fi rt-fi--desc" value={desc}
        onChange={e => setDesc(e.target.value)} placeholder="Açıklama" />
      <label className="rt-fi rt-fi--check">
        <input type="checkbox" checked={active} onChange={e => setActive(e.target.checked)} />
        <span>Aktif</span>
      </label>
      <button type="submit" disabled={saving} className="rt-act rt-act--save" title="Kaydet">
        {saving ? '…' : <Check size={14} />}
      </button>
      <button type="button" onClick={onCancel} className="rt-act rt-act--cancel" title="Vazgeç">
        <X size={14} />
      </button>
    </form>
  )
}

// ── Operasyon ekleme formu ─────────────────────────────────────────────────
function OpAddForm({ nextSeq, lookupUrl, onAdd, onCancel, saving }) {
  var [seq, setSeq]           = useState(nextSeq)
  var [selectedOp, setOp]     = useState(null)
  var [notes, setNotes]       = useState('')
  var [showPicker, setPicker] = useState(false)

  function submit(e) {
    e.preventDefault()
    if (!selectedOp) return
    onAdd({ sequence: parseInt(seq, 10) || nextSeq, operationId: selectedOp.id, notes: notes.trim() || null })
  }

  return (
    <>
      <form onSubmit={submit} className="rt-op-form">
        <input className="rt-fi rt-fi--seq" type="number" value={seq}
          onChange={e => setSeq(e.target.value)} min="1" title="Sıra no" />
        <div className="rt-fi rt-fi--picker" onClick={() => setPicker(true)}>
          {selectedOp
            ? <span><b style={{ color: '#818cf8' }}>{selectedOp.code}</b> {selectedOp.name}</span>
            : <span style={{ color: '#64748b' }}>Operasyon seç...</span>}
          <Search size={12} style={{ color: '#64748b', flexShrink: 0 }} />
        </div>
        <input className="rt-fi rt-fi--notes" value={notes}
          onChange={e => setNotes(e.target.value)} placeholder="Notlar" />
        <button type="submit" disabled={saving || !selectedOp} className="rt-act rt-act--save">
          {saving ? '…' : <Check size={14} />}
        </button>
        <button type="button" onClick={onCancel} className="rt-act rt-act--cancel">
          <X size={14} />
        </button>
      </form>
      {showPicker && (
        <OpPickerModal lookupUrl={lookupUrl}
          onSelect={op => { setOp(op); setPicker(false) }}
          onClose={() => setPicker(false)} />
      )}
    </>
  )
}

// ── Makine süresi satırı ekle/düzenle formu — inline (rt-mt-form-row içinde) ──
//   Hibrit kapsam (Seq 45/46): satır RoutingId taşır — boş ise tüm rotalarda ortak, doluysa
//   yalnızca açılan rotaya özeldir (varsayılan: Bu Rota). Makine tarafı spesifik makine YA DA
//   makine grubu (XOR); stok tarafı yok / spesifik stok / stok grubu (üçlü). Ölçü birimi yalnız
//   spesifik stok seçilince görünür ve opsiyoneldir (boş = ürünün baz birimi).
function MachineTimeRowForm({ initial, urls, routingId, allowedItemIds, onSave, onCancel, saving }) {
  var [machineType, setMachineType]           = useState(initial?.machineGroupId ? 'group' : 'machine')
  var [machineId, setMachineId]               = useState(initial?.machineId || null)
  var [machineCode, setMachineCode]           = useState(initial?.machineCode || '')
  var [machineName, setMachineName]           = useState(initial?.machineName || '')
  var [machineGroupId, setMachineGroupId]     = useState(initial?.machineGroupId || null)
  var [machineGroupCode, setMachineGroupCode] = useState(initial?.machineGroupCode || '')
  var [machineGroupDesc, setMachineGroupDesc] = useState('')

  var [itemMode, setItemMode]           = useState(initial?.itemGroupId ? 'group' : (initial?.itemId ? 'item' : 'none'))
  var [itemId, setItemId]               = useState(initial?.itemId || null)
  var [itemCode, setItemCode]           = useState(initial?.itemCode || '')
  var [itemName, setItemName]           = useState(initial?.itemName || '')
  var [itemGroupId, setItemGroupId]     = useState(initial?.itemGroupId || null)
  var [itemGroupCode, setItemGroupCode] = useState(initial?.itemGroupCode || '')
  var [itemGroupDesc, setItemGroupDesc] = useState('')
  var [unitId, setUnitId]               = useState(initial?.unitId || null)
  var [unitOptions, setUnitOptions]     = useState([])

  var [quantity, setQuantity]         = useState(initial?.quantity ?? 1)
  var [duration, setDuration]         = useState(initial?.durationPerUnit ?? 0)
  var [durationUnit, setDurationUnit] = useState(initial?.durationUnit || 1)
  var [active, setActive]             = useState(initial?.isActive ?? true)
  var [scope, setScope]               = useState(initial && initial.routingId == null ? 'all' : 'this')
  var [pickerOpen, setPickerOpen]     = useState(null)   // null | 'machine' | 'machineGroup' | 'item' | 'itemGroup'

  // Seçilen ürünün ölçü birimi seçenekleri — GetItemUnits ürünün kendi taban birim ID'sini
  // dönmüyor (yalnız alternatiflerini + sistemdeki tüm birimleri), bu yüzden "Baz Birim" boş
  // değer (unitId=null) ile temsil edilir; seçenekler yalnızca tanımlı alternatiflerden gelir.
  useEffect(() => {
    if (itemMode !== 'item' || !itemId) { setUnitOptions([]); return }
    var cancelled = false
    apiGet('/Logistics/GetItemUnits?itemId=' + itemId).then(d => {
      if (cancelled) return
      var avail = Array.isArray(d?.availableUnits) ? d.availableUnits : []
      var byId = {}
      avail.forEach(u => { byId[u.id] = u })
      var conv = Array.isArray(d?.conversions) ? d.conversions : []
      var opts = conv
        .map(c => { var u = byId[c.unitId]; return { id: c.unitId, code: u?.unitCode || '', name: u?.unitName || '' } })
        .filter(o => o.id)
      setUnitOptions(opts)
    }).catch(() => { if (!cancelled) setUnitOptions([]) })  // sessiz — birim listesi bos kalir, "Baz Birim" secili kalir (uc olmayan/erisilemeyen durumda derece bozulur ama form kullanilabilir kalir)
    return () => { cancelled = true }
  }, [itemMode, itemId])

  function selectMachineType(t) {
    setMachineType(t)
    if (t === 'machine') { setMachineGroupId(null); setMachineGroupCode(''); setMachineGroupDesc('') }
    else { setMachineId(null); setMachineCode(''); setMachineName('') }
  }
  function selectItemMode(m) {
    setItemMode(m)
    if (m !== 'item') { setItemId(null); setItemCode(''); setItemName(''); setUnitId(null) }
    if (m !== 'group') { setItemGroupId(null); setItemGroupCode(''); setItemGroupDesc('') }
  }

  var canSubmit = (machineType === 'machine' ? !!machineId : !!machineGroupId) &&
    (itemMode !== 'item' || !!itemId) && (itemMode !== 'group' || !!itemGroupId)

  function submit(e) {
    e.preventDefault()
    if (!canSubmit) return
    var qty = parseFloat(quantity)
    if (!qty || qty <= 0) return
    var dur = parseFloat(duration)
    if (isNaN(dur) || dur < 0) return
    onSave({
      id: initial?.id || 0,
      routingId: scope === 'all' ? null : routingId,
      machineId: machineType === 'machine' ? machineId : null,
      machineGroupId: machineType === 'group' ? machineGroupId : null,
      itemId: itemMode === 'item' ? itemId : null,
      itemGroupId: itemMode === 'group' ? itemGroupId : null,
      unitId: itemMode === 'item' ? unitId : null,
      quantity: qty, durationPerUnit: dur, durationUnit: durationUnit, isActive: active,
    })
  }

  return (
    <>
      <form onSubmit={submit} className="rt-mt-form">

        {/* Makine — spesifik makine YA DA makine grubu */}
        <div className="rt-mt-form__row">
          <div className="rt-seg rt-seg--sm">
            <button type="button" className={'rt-seg__btn' + (machineType === 'machine' ? ' rt-seg__btn--active' : '')}
              onClick={() => selectMachineType('machine')}>Makine</button>
            <button type="button" className={'rt-seg__btn' + (machineType === 'group' ? ' rt-seg__btn--active' : '')}
              onClick={() => selectMachineType('group')}>Makine Grubu</button>
          </div>
          {machineType === 'machine' ? (
            <div className="rt-fi rt-fi--picker rt-mt-fi--machine" onClick={() => setPickerOpen('machine')}>
              {machineId
                ? <span><b style={{ color: '#67e8f9' }}>{machineCode}</b> {machineName}</span>
                : <span style={{ color: '#64748b' }}>Makine seç... *</span>}
              <Search size={12} style={{ color: '#64748b', flexShrink: 0 }} />
            </div>
          ) : (
            <div className="rt-fi rt-fi--picker rt-mt-fi--machine" onClick={() => setPickerOpen('machineGroup')}>
              {machineGroupId
                ? <span><b style={{ color: '#67e8f9' }}>{machineGroupCode}</b> {machineGroupDesc}</span>
                : <span style={{ color: '#64748b' }}>Makine grubu seç... *</span>}
              <Search size={12} style={{ color: '#64748b', flexShrink: 0 }} />
            </div>
          )}
        </div>

        {/* Stok — yok / spesifik stok / stok grubu (+ olcu birimi yalniz spesifik stokta) */}
        <div className="rt-mt-form__row">
          <div className="rt-seg rt-seg--sm">
            <button type="button" className={'rt-seg__btn' + (itemMode === 'none' ? ' rt-seg__btn--active' : '')}
              onClick={() => selectItemMode('none')}>Yok</button>
            <button type="button" className={'rt-seg__btn' + (itemMode === 'item' ? ' rt-seg__btn--active' : '')}
              onClick={() => selectItemMode('item')}>Stok</button>
            <button type="button" className={'rt-seg__btn' + (itemMode === 'group' ? ' rt-seg__btn--active' : '')}
              onClick={() => selectItemMode('group')}>Stok Grubu</button>
          </div>
          {itemMode === 'item' && (
            <div className="rt-fi rt-fi--picker rt-mt-fi--item" onClick={() => setPickerOpen('item')}>
              {itemId
                ? <span><b style={{ color: '#93c5fd' }}>{itemCode}</b> {itemName}</span>
                : <span style={{ color: '#64748b' }}>Stok seç... *</span>}
              <Search size={12} style={{ color: '#64748b', flexShrink: 0 }} />
            </div>
          )}
          {itemMode === 'group' && (
            <div className="rt-fi rt-fi--picker rt-mt-fi--item" onClick={() => setPickerOpen('itemGroup')}>
              {itemGroupId
                ? <span><b style={{ color: '#93c5fd' }}>{itemGroupCode}</b> {itemGroupDesc}</span>
                : <span style={{ color: '#64748b' }}>Stok grubu seç... *</span>}
              <Search size={12} style={{ color: '#64748b', flexShrink: 0 }} />
            </div>
          )}
          {itemMode === 'item' && (
            <select className="rt-fi rt-mt-fi--itemunit" value={unitId || ''}
              onChange={e => setUnitId(e.target.value ? parseInt(e.target.value, 10) : null)}
              title="Ölçü birimi — boş = ürünün baz birimi">
              <option value="">Baz Birim</option>
              {unitOptions.map(o => (
                <option key={o.id} value={o.id}>{o.code}{o.name ? ' — ' + o.name : ''}</option>
              ))}
            </select>
          )}
        </div>

        {/* Miktar / süre / kapsam / aktif */}
        <div className="rt-mt-form__row">
          <input className="rt-fi rt-mt-fi--qty" type="number" min="0" step="any" value={quantity}
            onChange={e => setQuantity(e.target.value)} title="Miktar — süre bu miktar içindir" placeholder="Miktar *" required />

          <input className="rt-fi rt-mt-fi--dur" type="number" min="0" step="any" value={duration}
            onChange={e => setDuration(e.target.value)} title="Süre" placeholder="Süre *" required />

          <select className="rt-fi rt-mt-fi--unit" value={durationUnit}
            onChange={e => setDurationUnit(parseInt(e.target.value, 10))} title="Süre birimi">
            <option value={1}>Dakika</option>
            <option value={2}>Saat</option>
          </select>

          <div className="rt-seg rt-seg--sm" title="Bu satır yalnızca bu rotada mı, yoksa operasyonun tüm rotalarında mı geçerli?">
            <button type="button" className={'rt-seg__btn' + (scope === 'this' ? ' rt-seg__btn--active' : '')}
              onClick={() => setScope('this')}>Bu Rota</button>
            <button type="button" className={'rt-seg__btn' + (scope === 'all' ? ' rt-seg__btn--active' : '')}
              onClick={() => setScope('all')}>Tüm Rotalar</button>
          </div>

          <label className="rt-toggle" title={active ? 'Aktif' : 'Pasif'}>
            <input type="checkbox" checked={active} onChange={e => setActive(e.target.checked)} />
            <span className="rt-toggle__slider" />
          </label>
        </div>

        <div className="rt-mt-form__actions">
          <button type="submit" disabled={saving || !canSubmit} className="rt-act rt-act--save" title="Kaydet">
            {saving ? '…' : <Check size={14} />}
          </button>
          <button type="button" onClick={onCancel} className="rt-act rt-act--cancel" title="Vazgeç">
            <X size={14} />
          </button>
        </div>
      </form>

      {pickerOpen === 'machine' && (
        <PickerModal
          lookupUrl={urls.machinesLookup || '/Logistics/GetAllMachines'}
          title="Makine" placeholder="Makine ara..."
          onSelect={m => { setMachineId(m.id); setMachineCode(m.code); setMachineName(m.name); setPickerOpen(null) }}
          onClose={() => setPickerOpen(null)}
        />
      )}
      {pickerOpen === 'machineGroup' && (
        <PickerModal
          lookupUrl="/Definitions/GetAllCardGroups?cardType=3"
          title="Makine Grubu" placeholder="Makine grubu ara..."
          onSelect={g => { setMachineGroupId(g.id); setMachineGroupCode(g.code); setMachineGroupDesc(g.description || ''); setPickerOpen(null) }}
          onClose={() => setPickerOpen(null)}
        />
      )}
      {pickerOpen === 'item' && (
        <PickerModal
          lookupUrl={urls.itemsLookup || '/Logistics/StockLookup'}
          title="Mamul / Stok" placeholder="Stok ara (kod, ad)..." queryParam="q"
          filterIds={allowedItemIds} filterHint="Rota ürünleriyle sınırlı"
          onSelect={it => { setItemId(it.id); setItemCode(it.code); setItemName(it.name); setUnitId(null); setPickerOpen(null) }}
          onClose={() => setPickerOpen(null)}
        />
      )}
      {pickerOpen === 'itemGroup' && (
        <PickerModal
          lookupUrl="/Definitions/GetAllCardGroups?cardType=1"
          title="Stok Grubu" placeholder="Stok grubu ara..."
          onSelect={g => { setItemGroupId(g.id); setItemGroupCode(g.code); setItemGroupDesc(g.description || ''); setPickerOpen(null) }}
          onClose={() => setPickerOpen(null)}
        />
      )}
    </>
  )
}

// ── Makine Süreleri modal — operasyon × makine/makine grubu (× opsiyonel ürün/ürün grubu)
//   süre eşleştirme ── Kilitli karar (Seq 41): mevcut OperationMachineTime altyapısına bağlanır.
//   op.operationId (master Operation.Id) kullanılır — op.id (RoutingOperation satır id'si) DEĞİL;
//   OperationMachineTime.OperationId bu tabloya FK'lidir ve rota-bağımsızdır (aynı operasyon farklı
//   rotalarda kullanılsa bile makine süreleri tektir) — ancak SATIR bazında artık hibrit kapsam var
//   (Seq 45/46): RoutingId boşsa satır tüm rotalarda ortak, doluysa yalnız o rotada geçerlidir.
function MachineTimesModal({ op, routing, urls, onClose }) {
  var [rows, setRows]         = useState([])
  var [loading, setLoading]   = useState(true)
  var [formOpen, setFormOpen] = useState(false)   // false | true (yeni satır) | <rowId> (düzenle)
  var [saving, setSaving]     = useState(false)
  var [delRow, setDelRow]     = useState(null)

  // Seq 46 — spesifik stok seçicisi rota ürünleriyle sınırlanır: ana mamul (routing.itemId) ∪
  // RoutingItemMaps(routingId). İkisi de boşsa (şablon rota) filtre uygulanmaz (null = tümü).
  var [allowedItemIds, setAllowedItemIds] = useState(null)

  var load = useCallback(async () => {
    setLoading(true)
    try {
      var list = await apiGet('/Production/OperationMachineTimes?operationId=' + op.operationId + '&routingId=' + routing.id)
      var arr = Array.isArray(list) ? list : []
      setRows(arr.map(r => ({
        id: r.id,
        routingId: r.routingId ?? null,
        machineId: r.machineId || null,
        machineCode: r.machineCode || r.code || '', machineName: r.machineName || r.name || '',
        machineGroupId: r.machineGroupId || null, machineGroupCode: r.machineGroupCode || '',
        itemId: r.itemId || null, itemCode: r.itemCode || '', itemName: r.itemName || '',
        itemGroupId: r.itemGroupId || null, itemGroupCode: r.itemGroupCode || '',
        unitId: r.unitId || null, unitCode: r.unitCode || '', unitName: r.unitName || '',
        quantity: r.quantity, durationPerUnit: r.durationPerUnit,
        durationUnit: normalizeDurationUnit(r.durationUnit),
        isActive: r.isActive,
      })))
    } catch { /* sessiz — bos liste kalir, kullanici tekrar acinca yeniden dener */ }
    finally { setLoading(false) }
  }, [op.operationId, routing.id])

  useEffect(() => { load() }, [load])

  useEffect(() => {
    var cancelled = false
    async function loadAllowedItems() {
      var ids = new Set()
      if (routing.itemId) ids.add(routing.itemId)
      try {
        var maps = await apiGet('/Production/RoutingItemMaps?routingId=' + routing.id)
        if (Array.isArray(maps)) maps.forEach(m => { if (m && m.itemId) ids.add(m.itemId) })
      } catch { /* sessiz — filtre uygulanamazsa tumu gosterilir (asagida size 0 ise zaten null olur) */ }
      if (!cancelled) setAllowedItemIds(ids.size > 0 ? ids : null)
    }
    loadAllowedItems()
    return () => { cancelled = true }
  }, [routing.id, routing.itemId])

  useEffect(() => {
    function onKey(e) { if (e.key === 'Escape' && !formOpen && !delRow) onClose() }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [onClose, formOpen, delRow])

  async function handleSaveRow(data) {
    setSaving(true)
    try {
      var res = await apiPost('/Production/SaveOperationMachineTime', {
        id: data.id, operationId: op.operationId, routingId: data.routingId,
        machineId: data.machineId, machineGroupId: data.machineGroupId,
        itemId: data.itemId, itemGroupId: data.itemGroupId, unitId: data.unitId,
        quantity: data.quantity, durationPerUnit: data.durationPerUnit,
        durationUnit: data.durationUnit, isActive: data.isActive,
      })
      if (res.ok) { await load(); setFormOpen(false) }
      else window.CalibraHub?.toast(res.error || 'Makine süresi kaydedilemedi', 'err')
    } finally { setSaving(false) }
  }

  async function handleDeleteRow(row) {
    setSaving(true)
    try {
      var res = await apiPost('/Production/DeleteOperationMachineTime?id=' + row.id, null)
      if (res.ok) await load()
      else window.CalibraHub?.toast(res.error || 'Makine süresi silinemedi', 'err')
    } finally { setSaving(false); setDelRow(null) }
  }

  async function handleToggleRow(row) {
    setSaving(true)
    try {
      var res = await apiPost('/Production/SaveOperationMachineTime', {
        id: row.id, operationId: op.operationId, routingId: row.routingId ?? null,
        machineId: row.machineId || null, machineGroupId: row.machineGroupId || null,
        itemId: row.itemId || null, itemGroupId: row.itemGroupId || null, unitId: row.unitId || null,
        quantity: row.quantity, durationPerUnit: row.durationPerUnit,
        durationUnit: row.durationUnit, isActive: !row.isActive,
      })
      if (res.ok) await load()
      else window.CalibraHub?.toast(res.error || 'Durum güncellenemedi', 'err')
    } finally { setSaving(false) }
  }

  return (
    <div className="rt-mt-backdrop"
      onClick={e => { if (e.target === e.currentTarget && !formOpen && !delRow) onClose() }}>
      <div className="rt-mt-modal">
        <div className="rt-mt-modal__head">
          <div className="rt-mt-modal__icon"><Timer size={18} /></div>
          <div className="rt-mt-modal__id">
            <div className="rt-mt-modal__title">Makine Süreleri</div>
            <div className="rt-mt-modal__sub">{op.operationCode} — {op.operationName || '—'} · {routing.code}</div>
          </div>
          <button className="rt-picker__close" onClick={onClose}><X size={16} /></button>
        </div>

        <div className="rt-mt-modal__hint">
          Bu operasyonun farklı makinelerde (veya makine gruplarında) — istenirse belirli bir ürün
          ya da ürün grubu için — aldığı süreyi tanımlayın. Süre, girilen miktar içindir (örn. "100
          adet için 45 dakika"). Kapsamı "Bu Rota" olan satırlar yalnızca burada, "Tüm Rotalar"
          olanlar bu operasyonun kullanıldığı her rotada geçerli olur.
        </div>

        <div className="rt-mt-modal__body">
          {loading && <div className="rt-picker__info">Yükleniyor...</div>}

          {!loading && (
            <div className="rt-mt-list">
              {rows.length === 0 && formOpen !== true && (
                <div className="rt-mt-empty">Henüz makine süresi tanımlanmamış</div>
              )}

              {rows.map(row => (
                formOpen === row.id ? (
                  <div className="rt-mt-form-row" key={row.id}>
                    <MachineTimeRowForm initial={row} urls={urls} saving={saving}
                      routingId={routing.id} allowedItemIds={allowedItemIds}
                      onSave={handleSaveRow} onCancel={() => setFormOpen(false)} />
                  </div>
                ) : (
                  <div className="rt-mt-row" key={row.id}>
                    {row.machineGroupId ? (
                      <div className="rt-tile rt-tile--cyan" title="Makine Grubu">
                        <span className="rt-tile__label">Makine Grubu</span>
                        <span className="rt-tile__value">{row.machineGroupCode}</span>
                      </div>
                    ) : (
                      <div className="rt-tile rt-tile--cyan" title={row.machineName || ''}>
                        <span className="rt-tile__label">Makine</span>
                        <span className="rt-tile__value">{row.machineCode || row.machineName || '—'}</span>
                      </div>
                    )}

                    {row.itemGroupId ? (
                      <div className="rt-tile rt-tile--blue" title="Stok Grubu">
                        <span className="rt-tile__label">Stok Grubu</span>
                        <span className="rt-tile__value">{row.itemGroupCode}</span>
                      </div>
                    ) : row.itemId ? (
                      <div className="rt-tile rt-tile--blue" title={row.itemName || ''}>
                        <span className="rt-tile__label">Ürün</span>
                        <span className="rt-tile__value">{row.itemCode}</span>
                      </div>
                    ) : (
                      <div className="rt-tile rt-tile--muted">
                        <span className="rt-tile__label">Ürün</span>
                        <span className="rt-tile__value rt-tile__value--muted">Yok</span>
                      </div>
                    )}

                    {row.unitId ? (
                      <div className="rt-tile">
                        <span className="rt-tile__label">Ölçü Birimi</span>
                        <span className="rt-tile__value">{row.unitCode || row.unitName}</span>
                      </div>
                    ) : null}

                    <div className="rt-tile">
                      <span className="rt-tile__label">Miktar</span>
                      <span className="rt-tile__value">{fmtDec(row.quantity)}</span>
                    </div>
                    <div className="rt-tile rt-tile--indigo">
                      <span className="rt-tile__label">Süre</span>
                      <span className="rt-tile__value">
                        {fmtDec(row.durationPerUnit)}
                        <span className="rt-tile__detail">{DURATION_UNIT_LABEL[row.durationUnit] || ''}</span>
                      </span>
                    </div>

                    <span className={'rt-mt-scope rt-mt-scope--' + (row.routingId ? 'this' : 'all')}>
                      {row.routingId ? 'Bu Rota' : 'Tüm Rotalar'}
                    </span>

                    <div className="rt-mt-row__spacer" />

                    <label className="rt-toggle" title={row.isActive ? 'Pasife Al' : 'Aktife Al'}
                      onClick={() => handleToggleRow(row)}>
                      <input type="checkbox" readOnly checked={row.isActive} />
                      <span className="rt-toggle__slider" />
                    </label>

                    <div className="rt-row__actions">
                      <button className="rt-act rt-act--edit" title="Düzenle" onClick={() => setFormOpen(row.id)}>
                        <Edit2 size={13} />
                      </button>
                      <button className="rt-act rt-act--del" title="Sil" onClick={() => setDelRow(row)}>
                        <Trash2 size={13} />
                      </button>
                    </div>
                  </div>
                )
              ))}

              {formOpen === true && (
                <div className="rt-mt-form-row">
                  <MachineTimeRowForm urls={urls} saving={saving}
                    routingId={routing.id} allowedItemIds={allowedItemIds}
                    onSave={handleSaveRow} onCancel={() => setFormOpen(false)} />
                </div>
              )}
            </div>
          )}

          {!loading && !formOpen && (
            <button className="rt-ops__action-btn rt-mt-add-btn" onClick={() => setFormOpen(true)}>
              <Plus size={13} /> Makine Süresi Ekle
            </button>
          )}
        </div>

        <div className="rt-mt-modal__foot">
          <button className="rt-btn rt-btn--ghost" onClick={onClose}>Kapat</button>
        </div>
      </div>

      {delRow && (
        <DeleteModal
          target={{
            type: 'machineTime',
            label: `${delRow.machineGroupId ? (delRow.machineGroupCode + ' (Grup)') : (delRow.machineCode || delRow.machineName || 'Makine')} — ${fmtDec(delRow.quantity)} adet / ${fmtDec(delRow.durationPerUnit)} ${DURATION_UNIT_LABEL[delRow.durationUnit] || ''}`,
          }}
          onCancel={() => setDelRow(null)}
          onConfirm={() => handleDeleteRow(delRow)}
        />
      )}
    </div>
  )
}

// ── RoutingTree — ana bileşen ──────────────────────────────────────────────
export default function RoutingTree({ config }) {
  var urls = config.urls || {}
  var routingMasterWidgets = Array.isArray(config.routingMasterWidgets) ? config.routingMasterWidgets : []
  var opMasterWidgets      = Array.isArray(config.opMasterWidgets) ? config.opMasterWidgets : []
  var routingBoardKey      = 'production-routings-tree'
  var opBoardKey           = 'production-routings-tree-ops'

  var [routings, setRoutings]           = useState(config.routings || [])
  var [expandedIds, setExpandedIds]     = useState(new Set())
  var [search, setSearch]               = useState('')
  var [addingRouting, setAddingRouting] = useState(false)
  var [editingId, setEditingId]         = useState(null)
  var [addingOpFor, setAddingOpFor]     = useState(null)
  var [deleteTarget, setDeleteTarget]   = useState(null)
  var [saving, setSaving]               = useState(false)

  // Widget config panelleri + kullanici tercihleri (visibleIds / order)
  var [routingConfigOpen, setRoutingConfigOpen] = useState(false)
  var [opConfigOpen, setOpConfigOpen]           = useState(false)
  var [routingUserCfg, setRoutingUserCfg]       = useState(function () { return loadWidgetConfig(routingBoardKey) })
  var [opUserCfg, setOpUserCfg]                 = useState(function () { return loadWidgetConfig(opBoardKey) })

  // C-Grid standart: filter panel + excel export
  var [filterOpen, setFilterOpen] = useState(false)
  var [filters, setFilters]       = useState([])
  var [exporting, setExporting]   = useState(false)

  // F8 → Yeni Rota (standart SmartBoard listeleriyle tutarli "yeni kayit" kisayolu)
  useEffect(function () {
    function onKey(e) {
      if (e.defaultPrevented) return   // aksiyon seridi ( or. WorkOrderEdit) F8'i onceden yakaladiysa cakisma
      if (e.altKey || e.ctrlKey || e.metaKey || e.shiftKey) return
      if (e.key !== 'F8' && e.keyCode !== 119) return
      e.preventDefault()
      setAddingRouting(true); setEditingId(null)
    }
    document.addEventListener('keydown', onKey)
    return function () { document.removeEventListener('keydown', onKey) }
  }, [])

  // dnd-kit sensors (operasyon kartlarini surukle-birak)
  var dndSensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 5 } }),
    useSensor(KeyboardSensor, { coordinateGetter: sortableKeyboardCoordinates }),
  )

  // Makine / stok eşleştirme — per-operation + per-routing
  var [machineAssignOpId, setMachineAssignOpId] = useState(null)   // { routing, op }
  var [itemAssignFor, setItemAssignFor]         = useState(null)   // routing
  var [machineTimesFor, setMachineTimesFor]     = useState(null)   // { routing, op } — Makine Süreleri modalı

  // Routing → entity-like (filter panel master widgets icin uyumlu yapida)
  var routingsAsEntities = useMemo(() => routings.map(r => ({
    id: r.id, title: r.name, subtitle: r.code,
    description: r.description, widgets: r.widgets || [],
  })), [routings])

  var filtered = useMemo(() => {
    var list = routings
    if (search) {
      var q = search.toLowerCase()
      list = list.filter(r =>
        (r.code || '').toLowerCase().includes(q) || (r.name || '').toLowerCase().includes(q))
    }
    if (filters && filters.length > 0) {
      var byId = {}
      routingsAsEntities.forEach(e => { byId[e.id] = e })
      list = list.filter(r => entityMatchesFilters(byId[r.id], filters))
    }
    return list
  }, [routings, search, filters, routingsAsEntities])

  var refresh = useCallback(async () => {
    try { var d = await apiGet(urls.refresh); setRoutings(d.routings || []) } catch { /* sessiz */ }
  }, [urls.refresh])

  // F6 → Yenile (standart SmartBoard listeleriyle tutarli in-place refresh)
  useEffect(function () {
    function onKey(e) {
      if (e.defaultPrevented) return
      if (e.altKey || e.ctrlKey || e.metaKey || e.shiftKey) return
      if (e.key !== 'F6' && e.keyCode !== 117) return
      e.preventDefault()
      refresh()
    }
    document.addEventListener('keydown', onKey)
    return function () { document.removeEventListener('keydown', onKey) }
  }, [refresh])

  // ── C-Grid standart Excel export (rota seviyesi) ────────────────────────
  var handleExportExcel = useCallback(async () => {
    if (exporting) return
    try {
      setExporting(true)
      var rows = filtered.map(r => {
        var obj = {
          __code: r.code || '',
          __name: r.name || '',
          __status: r.isActive ? 'Aktif' : 'Pasif',
          __ops: (r.operations || []).length,
        }
        if (Array.isArray(r.widgets)) {
          r.widgets.forEach(w => { if (w && w.id) obj[w.id] = w.value })
        }
        return obj
      })
      if (rows.length === 0) {
        // Rapor §6.6 — toast fallback
        if (window.CalibraHub && window.CalibraHub.toast) window.CalibraHub.toast('Aktarılacak rota yok.', 'warn')
        else window.alert('Aktarılacak rota yok.')
        return
      }

      var seen = {}
      var widgetCols = []
      routingMasterWidgets.forEach(w => { if (w && w.id && !seen[w.id]) { seen[w.id] = true; widgetCols.push({ id: w.id, label: w.label || w.id }) } })
      filtered.forEach(r => (r.widgets || []).forEach(w => {
        if (w && w.id && !seen[w.id]) { seen[w.id] = true; widgetCols.push({ id: w.id, label: w.label || w.id }) }
      }))

      var headers = [
        { id: '__code',   label: 'Kod' },
        { id: '__name',   label: 'Ad' },
        { id: '__status', label: 'Durum' },
        { id: '__ops',    label: 'Operasyon Adedi' },
      ].concat(widgetCols)

      var ts = new Date()
      var pad = n => n < 10 ? '0' + n : String(n)
      var stamp = ts.getFullYear() + pad(ts.getMonth()+1) + pad(ts.getDate()) + '_' +
                  pad(ts.getHours()) + pad(ts.getMinutes()) + pad(ts.getSeconds())

      var payload = {
        fileName: 'rota-tanimlari_' + stamp + '.xlsx',
        sheetName: 'Rota Tanimlari',
        headers, rows,
      }

      var token = ''
      var ti = document.querySelector('input[name="__RequestVerificationToken"]')
      if (ti) token = ti.value || ''

      var form = document.createElement('form')
      form.method = 'POST'; form.action = '/api/export/smartboard-excel'
      form.target = '_self'; form.style.display = 'none'

      var hidden = document.createElement('textarea')
      hidden.name = 'payload'; hidden.value = JSON.stringify(payload)
      form.appendChild(hidden)
      if (token) {
        var ti2 = document.createElement('input')
        ti2.type = 'hidden'; ti2.name = '__RequestVerificationToken'; ti2.value = token
        form.appendChild(ti2)
      }
      document.body.appendChild(form)
      form.submit()
      setTimeout(() => { if (form.parentNode) form.parentNode.removeChild(form) }, 1500)
    } catch (e) {
      console.error('[RoutingTree] export', e)
      var em = 'Aktarma hatasi: ' + (e.message || e)
      if (window.CalibraHub && window.CalibraHub.toast) window.CalibraHub.toast(em, 'err')
      else window.alert(em)
    } finally {
      setExporting(false)
    }
  }, [exporting, filtered, routingMasterWidgets])

  function toggle(id) {
    setExpandedIds(prev => { var n = new Set(prev); n.has(id) ? n.delete(id) : n.add(id); return n })
  }
  function expandId(id) {
    setExpandedIds(prev => { var n = new Set(prev); n.add(id); return n })
  }

  function opsToLines(ops) {
    return (ops || []).map(o => ({
      sequence: o.sequence, operationId: o.operationId,
      machineId: o.machineId || null, overrideDuration: o.overrideDuration || null,
      durationUnit: o.durationUnit || 1, notes: o.notes || null,
    }))
  }

  async function handleSaveRouting(formData, existing) {
    setSaving(true)
    try {
      var res = await apiPost(urls.save, {
        id: existing?.id || 0, code: formData.code, name: formData.name,
        description: formData.description || null, isActive: formData.isActive,
        itemId: existing?.itemId || null,
        operations: opsToLines(existing?.operations),
      })
      if (res.ok) { await refresh(); setAddingRouting(false); setEditingId(null); if (!existing) expandId(res.id) }
      else window.CalibraHub?.toast(res.error || 'Kayıt hatası', 'err')
    } finally { setSaving(false) }
  }

  async function handleDeleteRouting(routing) {
    setSaving(true)
    try {
      var res = await apiPost(`${urls.delete}/${routing.id}`, null)
      if (res.ok) {
        setRoutings(prev => prev.filter(r => r.id !== routing.id))
        setExpandedIds(prev => { var n = new Set(prev); n.delete(routing.id); return n })
      } else window.CalibraHub?.toast(res.error || 'Silme hatası', 'err')
    } finally { setSaving(false); setDeleteTarget(null) }
  }

  async function handleToggleActive(routing) {
    setSaving(true)
    try {
      var res = await apiPost(`${urls.toggle}?id=${routing.id}&enabled=${!routing.isActive}`, null)
      if (res.ok) await refresh()
      else window.CalibraHub?.toast(res.error || 'Durum hatası', 'err')
    } finally { setSaving(false) }
  }

  // Tek bir operasyona makine atar (yeni picker modal'dan).
  async function handleAssignMachine(routing, opId, machineId) {
    setSaving(true)
    try {
      var ops = (routing.operations || []).map(o => ({
        sequence: o.sequence, operationId: o.operationId,
        machineId: o.id === opId
          ? (machineId ? parseInt(machineId, 10) : null)
          : (o.machineId || null),
        overrideDuration: o.overrideDuration || null,
        durationUnit: o.durationUnit || 1, notes: o.notes || null,
      }))
      var res = await apiPost(urls.save, {
        id: routing.id, code: routing.code, name: routing.name,
        description: routing.description || null, isActive: routing.isActive,
        itemId: routing.itemId || null,
        operations: ops,
      })
      if (res.ok) { await refresh(); setMachineAssignOpId(null) }
      else window.CalibraHub?.toast(res.error || 'Makine atama hatası', 'err')
    } finally { setSaving(false) }
  }

  // Operasyonları yeniden sıralar (drag & drop sonrasi).
  // Yeni indekse göre 10, 20, 30, ... sequence atar ve kaydeder.
  // OPTIMISTIC: UI'yi hemen güncelle; save başarısızsa server'dan rollback.
  async function handleReorderOps(routing, newOrderIds) {
    var byId = {}
    ;(routing.operations || []).forEach(o => { byId[o.id] = o })
    var reordered = newOrderIds.map(id => byId[id]).filter(Boolean)

    // Optimistic update — kartlar yeni sırada hemen render edilsin
    var optimistic = reordered.map((o, i) => ({ ...o, sequence: i + 1 }))
    setRoutings(prev => prev.map(r => r.id === routing.id ? { ...r, operations: optimistic } : r))

    setSaving(true)
    try {
      var ops = reordered.map((o, i) => ({
        sequence: i + 1,
        operationId: o.operationId,
        machineId: o.machineId || null,
        overrideDuration: o.overrideDuration || null,
        durationUnit: o.durationUnit || 1,
        notes: o.notes || null,
      }))
      var res = await apiPost(urls.save, {
        id: routing.id, code: routing.code, name: routing.name,
        description: routing.description || null, isActive: routing.isActive,
        itemId: routing.itemId || null,
        operations: ops,
      })
      if (res.ok) {
        await refresh()
      } else {
        await refresh()  // rollback — server'dan gerçek state
        window.CalibraHub?.toast(res.error || 'Sıralama kaydedilemedi', 'err')
      }
    } catch (e) {
      await refresh()
      window.CalibraHub?.toast('Sıralama hatası: ' + (e.message || e), 'err')
    } finally { setSaving(false) }
  }

  // Rotaya stok (item) atar.
  async function handleAssignItem(routing, itemId) {
    setSaving(true)
    try {
      var res = await apiPost(urls.save, {
        id: routing.id, code: routing.code, name: routing.name,
        description: routing.description || null, isActive: routing.isActive,
        itemId: itemId ? parseInt(itemId, 10) : null,
        operations: opsToLines(routing.operations),
      })
      if (res.ok) { await refresh(); setItemAssignFor(null) }
      else window.CalibraHub?.toast(res.error || 'Stok eşleştirme hatası', 'err')
    } finally { setSaving(false) }
  }

  async function handleAddOp(routingId, line) {
    var routing = routings.find(r => r.id === routingId)
    if (!routing) return
    setSaving(true)
    try {
      var allOps = [
        ...opsToLines(routing.operations),
        { sequence: line.sequence, operationId: line.operationId, machineId: null, overrideDuration: null, durationUnit: 1, notes: line.notes || null },
      ]
      var res = await apiPost(urls.save, {
        id: routingId, code: routing.code, name: routing.name,
        description: routing.description || null, isActive: routing.isActive,
        itemId: routing.itemId || null,
        operations: allOps,
      })
      if (res.ok) { await refresh(); setAddingOpFor(null); expandId(routingId) }
      else window.CalibraHub?.toast(res.error || 'Operasyon eklenemedi', 'err')
    } finally { setSaving(false) }
  }

  async function handleDeleteOp(routing, opId) {
    setSaving(true)
    try {
      var remainingOps = opsToLines((routing.operations || []).filter(o => o.id !== opId))
      var res = await apiPost(urls.save, {
        id: routing.id, code: routing.code, name: routing.name,
        description: routing.description || null, isActive: routing.isActive,
        itemId: routing.itemId || null,
        operations: remainingOps,
      })
      if (res.ok) { await refresh(); expandId(routing.id) }
      else window.CalibraHub?.toast(res.error || 'Operasyon kaldırılamadı', 'err')
    } finally { setSaving(false); setDeleteTarget(null) }
  }

  return (
    <div className="rt-root">

      {/* ── Header ── */}
      <div className="rt-header">
        <div className="rt-header__id">
          <div className="rt-header__icon"><Workflow size={17} /></div>
          <div>
            <div className="rt-header__title">Rota Tanımlamaları</div>
            <div className="rt-header__sub">{filtered.length} rota</div>
          </div>
        </div>
        <div className="rt-header__search">
          <Search size={13} />
          <input value={search} onChange={e => setSearch(e.target.value)} placeholder="Kod veya ada göre ara..." />
          {search && <button className="rt-header__clear" onClick={() => setSearch('')}><X size={12} /></button>}
        </div>
        <button
          className={`rt-icon-btn${filters.length > 0 ? ' rt-icon-btn--active' : ''}`}
          title={filters.length > 0 ? `${filters.length} filtre aktif` : 'Filtreleme'}
          onClick={() => setFilterOpen(true)}
        >
          <Filter size={15} />
          {filters.length > 0 && (
            <span className="rt-icon-btn__badge">{filters.length}</span>
          )}
        </button>
        <button
          className="rt-icon-btn"
          title={exporting ? 'Aktarılıyor…' : "Excel'e Aktar"}
          onClick={handleExportExcel}
          disabled={exporting}
        >
          {exporting ? <Loader2 size={15} className="rt-spin" /> : <Download size={15} />}
        </button>
        <button className="rt-icon-btn" title="Widget Ayarları (Rota)"
          onClick={() => setRoutingConfigOpen(true)}>
          <Settings2 size={15} />
        </button>
        <button className="rt-btn rt-btn--primary"
          onClick={() => { setAddingRouting(true); setEditingId(null) }}>
          <Plus size={15} /> Yeni Rota
        </button>
      </div>

      {/* ── List ── */}
      <div className="rt-list">

        {/* Yeni rota form satırı */}
        {addingRouting && (
          <div className="rt-form-row">
            <RoutingForm
              onSave={data => handleSaveRouting(data, null)}
              onCancel={() => setAddingRouting(false)}
              saving={saving}
            />
          </div>
        )}

        {filtered.length === 0 && !addingRouting && (
          <div className="rt-empty">
            <Workflow size={32} />
            <span>{search ? 'Arama sonucu bulunamadı' : 'Henüz rota tanımlanmamış'}</span>
          </div>
        )}

        {filtered.map(routing => {
          var expanded  = expandedIds.has(routing.id)
          var isEditing = editingId === routing.id
          var ops = routing.operations || []

          return (
            <div key={routing.id} className={`rt-row-wrap${expanded ? ' rt-row-wrap--open' : ''}`}>

              {/* ── Rota satırı ── */}
              {isEditing ? (
                <div className="rt-form-row">
                  <RoutingForm
                    initial={routing}
                    onSave={data => handleSaveRouting(data, routing)}
                    onCancel={() => setEditingId(null)}
                    saving={saving}
                  />
                </div>
              ) : (
                <div className="rt-row" onClick={() => toggle(routing.id)}>
                  <button className={`rt-row__chevron${expanded ? ' rt-row__chevron--open' : ''}`}
                    onClick={e => { e.stopPropagation(); toggle(routing.id) }}>
                    <ChevronRight size={15} />
                  </button>

                  <div className="rt-row__avatar">
                    <Workflow size={18} />
                  </div>

                  <div className="rt-row__main">
                    <div className="rt-row__code">{routing.code}</div>
                    <div className="rt-row__name">{routing.name}</div>
                    {routing.description && (
                      <div className="rt-row__desc">{routing.description}</div>
                    )}
                  </div>

                  <div className="rt-row__divider" />

                  <div className="rt-row__tiles">
                    <div className="rt-tile rt-tile--indigo">
                      <span className="rt-tile__label">Operasyon</span>
                      <span className="rt-tile__value">{ops.length} adım</span>
                    </div>
                    {routing.itemCode ? (
                      <div className="rt-tile rt-tile--blue" title={routing.itemName || ''}>
                        <span className="rt-tile__label">Mamul</span>
                        <span className="rt-tile__value">{routing.itemCode}</span>
                      </div>
                    ) : null}
                    <WidgetChips widgets={routing.widgets}
                      visibleIds={routingUserCfg?.visibleIds}
                      order={routingUserCfg?.order} />
                  </div>

                  <span className={`rt-status rt-status--${routing.isActive ? 'active' : 'passive'}`}>
                    <span className="rt-status__dot" />
                    {routing.isActive ? 'Aktif' : 'Pasif'}
                  </span>

                  <label className="rt-toggle" title={routing.isActive ? 'Pasife Al' : 'Aktife Al'}
                    onClick={e => { e.stopPropagation(); handleToggleActive(routing) }}>
                    <input type="checkbox" readOnly checked={routing.isActive} />
                    <span className="rt-toggle__slider" />
                  </label>

                  <div className="rt-row__actions" onClick={e => e.stopPropagation()}>
                    <button className="rt-act rt-act--stock" title="Stok ile Eşleştir"
                      onClick={() => setItemAssignFor(routing)}>
                      <Package size={14} />
                    </button>
                    <button className="rt-act rt-act--addop" title="Operasyon Ekle"
                      onClick={() => { expandId(routing.id); setAddingOpFor(routing.id) }}>
                      <PlusCircle size={14} />
                    </button>
                    <button className="rt-act rt-act--edit" title="Düzenle"
                      onClick={() => { setEditingId(routing.id); expandId(routing.id) }}>
                      <Edit2 size={14} />
                    </button>
                    <button className="rt-act rt-act--del" title="Sil"
                      onClick={() => setDeleteTarget({ type: 'routing', label: `${routing.code} — ${routing.name}`, routing })}>
                      <Trash2 size={14} />
                    </button>
                  </div>
                </div>
              )}

              {/* ── Operasyonlar (genişlemiş — kart yapısında) ── */}
              {expanded && (
                <div className="rt-ops">
                  {/* Mini başlık + widget ayar butonu */}
                  <div className="rt-ops__bar">
                    <span className="rt-ops__title">Operasyonlar</span>
                    <button className="rt-icon-btn rt-icon-btn--xs" title="Widget Ayarları (Operasyon)"
                      onClick={(e) => { e.stopPropagation(); setOpConfigOpen(true) }}>
                      <Settings2 size={12} />
                    </button>
                  </div>

                  {/* Yeni operasyon ekleme formu — üstte */}
                  {addingOpFor === routing.id && (
                    <div className="rt-ops__add-form">
                      <OpAddForm
                        nextSeq={ops.length + 1}
                        lookupUrl={urls.operationsLookup}
                        onAdd={line => handleAddOp(routing.id, line)}
                        onCancel={() => setAddingOpFor(null)}
                        saving={saving}
                      />
                    </div>
                  )}

                  {ops.length === 0 && addingOpFor !== routing.id && (
                    <div className="rt-ops__empty">Henüz operasyon eklenmemiş</div>
                  )}

                  {/* Operasyon kart listesi (drag & drop ile yeniden siralanabilir) */}
                  <DndContext
                    sensors={dndSensors}
                    collisionDetection={closestCenter}
                    onDragEnd={(event) => {
                      var { active, over } = event
                      if (!over || active.id === over.id) return
                      var oldIndex = ops.findIndex(o => o.id === active.id)
                      var newIndex = ops.findIndex(o => o.id === over.id)
                      if (oldIndex === -1 || newIndex === -1) return
                      var newIds = arrayMove(ops, oldIndex, newIndex).map(o => o.id)
                      handleReorderOps(routing, newIds)
                    }}
                  >
                    <SortableContext items={ops.map(o => o.id)} strategy={verticalListSortingStrategy}>
                      <div className="rt-ops__list">
                        {ops.map(op => (
                          <SortableOpCard
                            key={op.id}
                            op={op}
                            routing={routing}
                            opUserCfg={opUserCfg}
                            onAssignMachine={(r, o) => setMachineAssignOpId({ routing: r, op: o })}
                            onEditMachineTimes={(r, o) => setMachineTimesFor({ routing: r, op: o })}
                            onDelete={(r, o) => setDeleteTarget({
                              type: 'op', label: `${o.operationCode} — ${o.operationName}`,
                              routing: r, opId: o.id,
                            })}
                          />
                        ))}
                      </div>
                    </SortableContext>
                  </DndContext>
                </div>
              )}
            </div>
          )
        })}
      </div>

      {/* ── Widget config panels ── */}
      <SmartBoardConfigPanel
        isOpen={routingConfigOpen}
        onClose={() => setRoutingConfigOpen(false)}
        boardKey={routingBoardKey}
        masterWidgets={routingMasterWidgets}
        onSaved={() => setRoutingUserCfg(loadWidgetConfig(routingBoardKey))}
      />
      <SmartBoardConfigPanel
        isOpen={opConfigOpen}
        onClose={() => setOpConfigOpen(false)}
        boardKey={opBoardKey}
        masterWidgets={opMasterWidgets}
        onSaved={() => setOpUserCfg(loadWidgetConfig(opBoardKey))}
      />

      {/* ── Filter panel (rota seviyesi) ── */}
      <SmartBoardFilterPanel
        isOpen={filterOpen}
        onClose={() => setFilterOpen(false)}
        boardKey={routingBoardKey}
        formCode={config.routingFormCode || 'ROUTING_EDIT'}
        masterWidgets={routingMasterWidgets}
        entities={routingsAsEntities}
        filters={filters}
        onApply={(next) => setFilters(next)}
      />

      {/* ── Stok ile Eşleştir modal (rota seviyesi) ── */}
      {itemAssignFor && (
        <PickerModal
          lookupUrl={urls.itemsLookup || '/Logistics/StockLookup'}
          title="Mamul / Stok"
          placeholder="Stok ara (kod, ad)..."
          queryParam="q"
          onSelect={(item) => handleAssignItem(itemAssignFor, item.id)}
          onClose={() => setItemAssignFor(null)}
        />
      )}

      {/* ── Makine Eşleştir modal (operasyon seviyesi) ── */}
      {machineAssignOpId && (
        <PickerModal
          lookupUrl={urls.machinesLookup || '/Logistics/GetAllMachines'}
          title="Makine"
          placeholder="Makine ara..."
          onSelect={(m) => handleAssignMachine(machineAssignOpId.routing, machineAssignOpId.op.id, m.id)}
          onClose={() => setMachineAssignOpId(null)}
        />
      )}

      {/* ── Makine Süreleri modal (operasyon × makine/grup × opsiyonel ürün/grup süre eşleştirme) ── */}
      {machineTimesFor && (
        <MachineTimesModal
          op={machineTimesFor.op}
          routing={machineTimesFor.routing}
          urls={urls}
          onClose={() => setMachineTimesFor(null)}
        />
      )}

      {/* ── Delete modal ── */}
      {deleteTarget && (
        <DeleteModal
          target={deleteTarget}
          onCancel={() => setDeleteTarget(null)}
          onConfirm={() => {
            if (deleteTarget.type === 'routing') handleDeleteRouting(deleteTarget.routing)
            else handleDeleteOp(deleteTarget.routing, deleteTarget.opId)
          }}
        />
      )}
    </div>
  )
}
