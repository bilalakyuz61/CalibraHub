import { useState, useMemo, useEffect, useCallback } from 'react'
import {
  ChevronRight, ChevronDown, Plus, PlusCircle, Edit2, Trash2,
  X, Check, Search, Workflow, Cpu, Settings2, Filter, Download, Loader2,
  Package, Cog, Hash, GripVertical,
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

  return (
    <div className="rt-del-backdrop" onClick={e => { if (e.target === e.currentTarget) onCancel() }}>
      <div className="rt-del-modal">
        <div className="rt-del-modal__head">
          <div className="rt-del-modal__icon"><Trash2 size={20} style={{ color: '#ef4444' }} /></div>
          <div>
            <div className="rt-del-modal__title">{target.type === 'routing' ? 'Rotayı Sil' : 'Operasyonu Kaldır'}</div>
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

// ── Generic seçici modal (operasyon / makine / stok) ───────────────────────
function PickerModal({ lookupUrl, title, placeholder, onSelect, onClose, queryParam }) {
  var [list, setList]       = useState([])
  var [search, setSearch]   = useState('')
  var [loading, setLoading] = useState(true)

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

  // Server-side query yoksa client-side filter
  var filtered = useMemo(() => {
    if (queryParam || !search) return list
    var q = search.toLowerCase()
    return list.filter(it => (it.code || '').toLowerCase().includes(q) ||
      (it.name || '').toLowerCase().includes(q) ||
      (it.machineCode || '').toLowerCase().includes(q) ||
      (it.machineName || '').toLowerCase().includes(q))
  }, [list, search, queryParam])

  // Field normalize — code/name unified
  function fieldsOf(it) {
    return {
      id:   it.id,
      code: it.code || it.machineCode || '',
      name: it.name || it.machineName || '',
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
function SortableOpCard({ op, routing, opUserCfg, onDelete, onAssignMachine }) {
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
