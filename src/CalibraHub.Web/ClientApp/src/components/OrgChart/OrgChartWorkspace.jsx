import { useState, useCallback, useRef, useEffect } from 'react'
import {
  DndContext, DragOverlay, PointerSensor, useSensor, useSensors,
  useDraggable, useDroppable,
} from '@dnd-kit/core'
import {
  Network, Plus, Star, Trash2, ChevronDown, ChevronRight,
  Users, Search, X, Loader2, RefreshCw, UserPlus, Building2, User, Briefcase,
} from 'lucide-react'
import * as api from '../../services/orgChartService'

/* ══════════════════════════════════════════════════════════
   Helpers
   ══════════════════════════════════════════════════════════ */
function getInitials(name) {
  if (!name) return '?'
  var parts = name.trim().split(/\s+/)
  if (parts.length >= 2) return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase()
  return parts[0].substring(0, 2).toUpperCase()
}

var AVATAR_COLORS = [
  '#6366f1', '#8b5cf6', '#ec4899', '#f43f5e', '#f97316',
  '#eab308', '#22c55e', '#14b8a6', '#06b6d4', '#3b82f6',
]

function avatarColor(id) {
  var hash = 0
  var s = String(id)
  for (var i = 0; i < s.length; i++) hash = ((hash << 5) - hash + s.charCodeAt(i)) | 0
  return AVATAR_COLORS[Math.abs(hash) % AVATAR_COLORS.length]
}

function nodeTypeIcon(nodeType) {
  if (nodeType === 'Department') return <Building2 size={14} />
  if (nodeType === 'Personnel')  return <Briefcase size={14} />
  if (nodeType === 'Vacant')     return <User size={14} style={{ opacity: .5 }} />
  return null // User — avatar handles it
}

function nodeTypeLabel(nodeType) {
  if (nodeType === 'Department') return 'Departman'
  if (nodeType === 'Personnel')  return 'Personel'
  if (nodeType === 'Vacant')     return 'Boş Kadro'
  return ''
}

function buildTree(nodes) {
  var rootNodes = []
  var childMap = {}

  nodes.forEach(function (n) {
    var key = n.parentNodeId || '__root'
    if (!childMap[key]) childMap[key] = []
    childMap[key].push(n)
  })

  Object.keys(childMap).forEach(function (key) {
    childMap[key].sort(function (a, b) { return a.sortOrder - b.sortOrder })
  })

  return { rootNodes: childMap['__root'] || [], childMap }
}

/* ══════════════════════════════════════════════════════════
   UserPicker
   ══════════════════════════════════════════════════════════ */
function UserPicker({ users, existingUserIds, onSelect, onClose, embedded }) {
  var [search, setSearch] = useState('')
  var searchRef = useRef(null)

  useEffect(function () {
    if (searchRef.current) searchRef.current.focus()
  }, [])

  useEffect(function () {
    function handleClick(e) {
      if (!e.target.closest('.oc-picker')) onClose()
    }
    document.addEventListener('mousedown', handleClick)
    return function () { document.removeEventListener('mousedown', handleClick) }
  }, [onClose])

  var available = users.filter(function (u) {
    if (existingUserIds.has(u.id)) return false
    var q = search.toLowerCase()
    if (!q) return true
    return (u.fullName || '').toLowerCase().indexOf(q) !== -1 ||
           (u.email || '').toLowerCase().indexOf(q) !== -1
  })

  return (
    <div className={'oc-picker' + (embedded ? ' oc-picker--embedded' : '')}>
      <div className="oc-picker-search">
        <Search size={13} />
        <input
          ref={searchRef}
          placeholder="Kullanıcı ara..."
          value={search}
          onChange={function (e) { setSearch(e.target.value) }}
          onKeyDown={function (e) { if (e.key === 'Escape') onClose() }}
        />
      </div>
      <div className="oc-picker-list">
        {available.length === 0 ? (
          <div className="oc-picker-empty">Uygun kullanıcı bulunamadı</div>
        ) : (
          available.slice(0, 20).map(function (u) {
            return (
              <button key={u.id} className="oc-picker-item" onClick={function () { onSelect(u) }}>
                <div className="oc-picker-avatar" style={{ background: avatarColor(u.id) }}>
                  {getInitials(u.fullName)}
                </div>
                <div>
                  <div className="oc-picker-name">{u.fullName}</div>
                  <div className="oc-picker-email">{u.email}</div>
                </div>
              </button>
            )
          })
        )}
      </div>
    </div>
  )
}

/* ══════════════════════════════════════════════════════════
   DragGhostCard — DragOverlay icin imleci takip eden klon kart
   ══════════════════════════════════════════════════════════ */
function DragGhostCard({ node }) {
  if (!node) return null
  var displayName = node.displayName || node.positionTitle || '—'
  var isUser = node.nodeType === 'User' || !node.nodeType
  return (
    <div className="oc-card oc-card--ghost">
      {isUser ? (
        <div className="oc-avatar" style={{ background: avatarColor(node.userId || node.id) }}>
          {getInitials(displayName)}
        </div>
      ) : (
        <div className="oc-avatar" style={{ background: avatarColor(node.id), display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
          {nodeTypeIcon(node.nodeType)}
        </div>
      )}
      <div className="oc-card-info">
        <div className="oc-card-name">{displayName}</div>
        {node.positionTitle && <div className="oc-card-title">{node.positionTitle}</div>}
      </div>
    </div>
  )
}

/* ══════════════════════════════════════════════════════════
   OrgTreeNode — draggable + droppable card
   ══════════════════════════════════════════════════════════ */
function OrgTreeNode({ node, childMap, users, departments, existingUserIds, onAddChild, chartId, onRefresh }) {
  var children = childMap[node.id] || []
  var [expanded, setExpanded] = useState(true)
  var [pickerOpen, setPickerOpen] = useState(false)
  var [addChildModal, setAddChildModal] = useState(false)
  var [dragError, setDragError] = useState(null)

  var { attributes, listeners, setNodeRef: setDragRef, transform, isDragging } =
    useDraggable({ id: node.id, data: { nodeId: node.id, chartId } })

  var { isOver, setNodeRef: setDropRef, active: dndActive } =
    useDroppable({ id: node.id })

  var setRef = useCallback(function (el) {
    setDragRef(el)
    setDropRef(el)
  }, [setDragRef, setDropRef])

  // Display name: use pre-resolved displayName from API
  var displayName = node.displayName || node.positionTitle || '—'
  var isUser = node.nodeType === 'User' || !node.nodeType
  // Üzerine sürükleniyor ama kendisi mi? (kendi üstüne bırakmanın anlamı yok)
  var isOverSelf = isOver && dndActive && dndActive.id === node.id
  var showDropHint = isOver && !isOverSelf

  function handleRemoveNode() {
    window.showConfirm({
      title: 'Node\'u Kaldır',
      message: `"${displayName}" node'unu kaldırmak istiyor musunuz? Alt node'lar da silinecek.`,
      okLabel: 'Kaldır',
    }).then(function (ok) {
      if (!ok) return
      api.removeNode({ chartId, nodeId: node.id, cascade: true })
        .then(function () { onRefresh() })
    })
  }

  return (
    <div className="oc-branch">
      {/* Card */}
      <div
        ref={setRef}
        className={'oc-card' + (isDragging ? ' oc-card--dragging' : '') + (showDropHint ? ' oc-card--drop-target' : '')}
        {...listeners}
        {...attributes}
      >
        {showDropHint && (
          <div className="oc-drop-hint">
            <UserPlus size={12} /> Alt node olarak ekle
          </div>
        )}
        {isUser ? (
          <div className="oc-avatar" style={{ background: avatarColor(node.userId || node.id) }}>
            {getInitials(displayName)}
          </div>
        ) : (
          <div className="oc-avatar" style={{ background: avatarColor(node.id), fontSize: 18, display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
            {nodeTypeIcon(node.nodeType)}
          </div>
        )}
        <div className="oc-card-info">
          <div className="oc-card-name">{displayName}</div>
          {node.positionTitle && <div className="oc-card-title">{node.positionTitle}</div>}
          {node.nodeType && node.nodeType !== 'User' && (
            <div className="oc-card-title" style={{ color: '#94a3b8', fontSize: '.72rem' }}>
              {nodeTypeLabel(node.nodeType)}
            </div>
          )}
        </div>

        {children.length > 0 && (
          <button className="oc-card-toggle" onClick={function (e) { e.stopPropagation(); setExpanded(!expanded) }}>
            {expanded ? <ChevronDown size={14} /> : <ChevronRight size={14} />}
          </button>
        )}

        <div style={{ position: 'relative' }}>
          <button
            className="oc-card-add-child"
            title="Alt node ekle"
            onClick={function (e) { e.stopPropagation(); setAddChildModal(true) }}
          >
            <UserPlus size={12} />
          </button>
          {addChildModal && (
            <AddNodeModal
              chartId={chartId}
              parentNodeId={node.id}
              users={users}
              departments={departments}
              existingUserIds={existingUserIds}
              onClose={function () { setAddChildModal(false) }}
              onAdded={function () { setAddChildModal(false); onRefresh() }}
            />
          )}
        </div>

        <button className="oc-card-remove" onClick={function (e) { e.stopPropagation(); handleRemoveNode() }} title="Kaldır">
          <X size={12} />
        </button>
      </div>

      {/* Children */}
      {children.length > 0 && expanded && (
        <div className="oc-children">
          {children.map(function (child) {
            return (
              <OrgTreeNode
                key={child.id}
                node={child}
                childMap={childMap}
                users={users}
                departments={departments}
                existingUserIds={existingUserIds}
                onAddChild={onAddChild}
                chartId={chartId}
                onRefresh={onRefresh}
              />
            )
          })}
        </div>
      )}
    </div>
  )
}

/* ══════════════════════════════════════════════════════════
   AddNodeModal — node türü seçimi + entity seçimi
   ══════════════════════════════════════════════════════════ */
function AddNodeModal({ chartId, parentNodeId, users, departments, existingUserIds, onClose, onAdded }) {
  var [step, setStep] = useState('type') // 'type' | 'user' | 'dept' | 'vacant'
  var [positionTitle, setPositionTitle] = useState('')
  var [saving, setSaving] = useState(false)

  function addUserNode(user) {
    setSaving(true)
    api.addNode({
      chartId,
      nodeType: 'User',
      refId: user.id,
      parentNodeId: parentNodeId || null,
      positionTitle: user.role || null,
      sortOrder: 0,
    }).then(function () { onAdded() }).finally(function () { setSaving(false) })
  }

  function addDeptNode(dept) {
    setSaving(true)
    api.addNode({
      chartId,
      nodeType: 'Department',
      intRefId: dept.id,
      parentNodeId: parentNodeId || null,
      positionTitle: dept.name,
      sortOrder: 0,
    }).then(function () { onAdded() }).finally(function () { setSaving(false) })
  }

  function addVacantNode() {
    if (!positionTitle.trim()) return
    setSaving(true)
    api.addNode({
      chartId,
      nodeType: 'Vacant',
      parentNodeId: parentNodeId || null,
      positionTitle: positionTitle.trim(),
      sortOrder: 0,
    }).then(function () { onAdded() }).finally(function () { setSaving(false) })
  }

  return (
    <div className="oc-modal-backdrop" onClick={onClose}>
      <div className="oc-modal-card" onClick={function (e) { e.stopPropagation() }}>
        <div className="oc-modal-header">
          <span>{step === 'type' ? 'Node Türü Seç' : step === 'user' ? 'Kullanıcı Seç' : step === 'dept' ? 'Departman Seç' : 'Boş Kadro'}</span>
          <button className="oc-modal-close" onClick={onClose}><X size={16} /></button>
        </div>

        {step === 'type' && (
          <div className="oc-modal-body">
            <div className="oc-type-grid">
              <button className="oc-type-btn" onClick={function () { setStep('user') }}>
                <User size={22} /> Kullanıcı
              </button>
              <button className="oc-type-btn" onClick={function () { setStep('dept') }}>
                <Building2 size={22} /> Departman
              </button>
              <button className="oc-type-btn" onClick={function () { setStep('vacant') }}>
                <Briefcase size={22} /> Boş Kadro
              </button>
            </div>
          </div>
        )}

        {step === 'user' && (
          <div className="oc-modal-body" style={{ padding: 0, overflow: 'hidden' }}>
            <UserPicker
              embedded
              users={users}
              existingUserIds={existingUserIds}
              onSelect={addUserNode}
              onClose={onClose}
            />
          </div>
        )}

        {step === 'dept' && (
          <div className="oc-modal-body">
            <div className="oc-picker-list" style={{ maxHeight: 240, overflowY: 'auto' }}>
              {departments.length === 0 && <div className="oc-picker-empty">Departman bulunamadı</div>}
              {departments.map(function (d) {
                return (
                  <button key={d.id} className="oc-picker-item" disabled={saving} onClick={function () { addDeptNode(d) }}>
                    <div className="oc-picker-avatar" style={{ background: avatarColor(String(d.id)) }}>
                      <Building2 size={14} />
                    </div>
                    <div className="oc-picker-name">{d.name}</div>
                  </button>
                )
              })}
            </div>
          </div>
        )}

        {step === 'vacant' && (
          <div className="oc-modal-body">
            <label style={{ fontSize: '.8rem', fontWeight: 600, color: '#64748b' }}>Pozisyon Adı</label>
            <input
              autoFocus
              className="oc-input"
              placeholder="örn. Satış Müdürü..."
              value={positionTitle}
              onChange={function (e) { setPositionTitle(e.target.value) }}
              onKeyDown={function (e) { if (e.key === 'Enter') addVacantNode() }}
              style={{ marginTop: 6, width: '100%' }}
            />
            <div style={{ marginTop: 12, display: 'flex', gap: 8, justifyContent: 'flex-end' }}>
              <button className="oc-btn oc-btn--secondary" onClick={onClose}>İptal</button>
              <button className="oc-btn oc-btn--primary" disabled={!positionTitle.trim() || saving} onClick={addVacantNode}>
                {saving ? <Loader2 size={14} className="nw-spin" /> : 'Ekle'}
              </button>
            </div>
          </div>
        )}
      </div>
    </div>
  )
}

/* ══════════════════════════════════════════════════════════
   NewChartModal — prompt() yerine custom modal
   ══════════════════════════════════════════════════════════ */
function NewChartModal({ onClose, onCreated }) {
  var [name, setName] = useState('')
  var [saving, setSaving] = useState(false)
  var inputRef = useRef(null)

  useEffect(function () { if (inputRef.current) inputRef.current.focus() }, [])

  function handleCreate() {
    if (!name.trim()) return
    setSaving(true)
    api.saveChart({ name: name.trim() })
      .then(function (res) {
        if (res.success) onCreated(res.id, name.trim())
        else onClose()
      })
      .finally(function () { setSaving(false) })
  }

  return (
    <div className="oc-modal-backdrop" onClick={onClose}>
      <div className="oc-modal-card" onClick={function (e) { e.stopPropagation() }}>
        <div className="oc-modal-header">
          <span>Yeni Şema</span>
          <button className="oc-modal-close" onClick={onClose}><X size={16} /></button>
        </div>
        <div className="oc-modal-body">
          <label style={{ fontSize: '.8rem', fontWeight: 600, color: '#64748b' }}>Şema Adı</label>
          <input
            ref={inputRef}
            className="oc-input"
            placeholder="Organizasyon Şeması..."
            value={name}
            onChange={function (e) { setName(e.target.value) }}
            onKeyDown={function (e) { if (e.key === 'Enter') handleCreate(); if (e.key === 'Escape') onClose() }}
            style={{ marginTop: 6, width: '100%' }}
          />
          <div style={{ marginTop: 12, display: 'flex', gap: 8, justifyContent: 'flex-end' }}>
            <button className="oc-btn oc-btn--secondary" onClick={onClose}>İptal</button>
            <button className="oc-btn oc-btn--primary" disabled={!name.trim() || saving} onClick={handleCreate}>
              {saving ? <Loader2 size={14} className="nw-spin" /> : 'Oluştur'}
            </button>
          </div>
        </div>
      </div>
    </div>
  )
}

/* ══════════════════════════════════════════════════════════
   OrgChartWorkspace — Main Component
   ══════════════════════════════════════════════════════════ */
export default function OrgChartWorkspace() {
  var [charts, setCharts] = useState([])
  var [activeChartId, setActiveChartId] = useState(null)
  var [nodes, setNodes] = useState([])
  var [users, setUsers] = useState([])
  var [departments, setDepartments] = useState([])
  var [loading, setLoading] = useState(true)
  var [detailLoading, setDetailLoading] = useState(false)
  var [newChartModal, setNewChartModal] = useState(false)
  var [addRootModal, setAddRootModal] = useState(false)
  var [dragError, setDragError] = useState(null)
  var [validationWarnings, setValidationWarnings] = useState([])
  // DragOverlay icin: aktif olarak surüklenen node — handleDragStart'ta set,
  // handleDragEnd/Cancel'da clear. DragGhostCard bu node'u render eder.
  var [activeDragNode, setActiveDragNode] = useState(null)

  var sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 8 } })
  )

  // ── Data loading ────────────────────────────────────────

  useEffect(function () {
    api.getCharts()
      .then(function (data) {
        setCharts(data || [])
        var def = (data || []).find(function (c) { return c.isDefault })
        if (def) setActiveChartId(def.id)
        else if (data && data.length > 0) setActiveChartId(data[0].id)
      })
      .catch(function (e) { console.error('[OrgChart] load charts error:', e) })
      .finally(function () { setLoading(false) })
  }, [])

  var loadChartDetail = useCallback(function (chartId) {
    if (!chartId) return
    setDetailLoading(true)
    api.getChartDetail(chartId)
      .then(function (data) {
        if (!data.success) return
        setNodes(data.nodes || [])
        setUsers(data.users || [])
        setDepartments(data.departments || [])
      })
      .catch(function (e) { console.error('[OrgChart] load detail error:', e) })
      .finally(function () { setDetailLoading(false) })
  }, [])

  useEffect(function () {
    if (activeChartId) loadChartDetail(activeChartId)
  }, [activeChartId, loadChartDetail])

  var activeChart = charts.find(function (c) { return c.id === activeChartId }) || null

  // ── Handlers ────────────────────────────────────────────

  var handleChartCreated = useCallback(function (id, name) {
    setCharts(function (prev) { return prev.concat([{ id, name, isDefault: false }]) })
    setNewChartModal(false)
    setActiveChartId(id)
  }, [])

  var handleDeleteChart = useCallback(async function (id) {
    var chartName = charts.find(function (c) { return c.id === id })?.name || ''
    var ok = await window.showConfirm({
      title: 'Şemayı Sil',
      message: `"${chartName}" şemasını silmek istediğinizden emin misiniz?`,
      okLabel: 'Evet, Sil',
    })
    if (!ok) return
    api.deleteChart(id).then(function () {
      setCharts(function (prev) { return prev.filter(function (c) { return c.id !== id }) })
      if (activeChartId === id) { setActiveChartId(null); setNodes([]) }
    })
  }, [activeChartId, charts])

  var handleSetDefault = useCallback(function (id) {
    api.setDefaultChart(id).then(function () {
      setCharts(function (prev) {
        return prev.map(function (c) { return { ...c, isDefault: c.id === id } })
      })
    })
  }, [])

  var handleGenerateDefault = useCallback(function () {
    api.generateDefaultChart().then(function (res) {
      if (!res.success) return
      api.getCharts().then(function (data) {
        setCharts(data || [])
        setActiveChartId(res.id)
      })
    })
  }, [])

  var handleDragStart = useCallback(function ({ active }) {
    var n = (nodes || []).find(function (x) { return x.id === active.id })
    setActiveDragNode(n || null)
  }, [nodes])

  var handleDragCancel = useCallback(function () {
    setActiveDragNode(null)
  }, [])

  var handleDragEnd = useCallback(function ({ active, over }) {
    setActiveDragNode(null)
    setDragError(null)
    if (!over || active.id === over.id) return

    var newParentNodeId = over.id === '__root-drop' ? null : over.id
    api.moveNode({
      chartId: activeChartId,
      nodeId: active.id,
      newParentNodeId: newParentNodeId,
      newSortOrder: 0,
    }).then(function (res) {
      if (res.success) {
        loadChartDetail(activeChartId)
      } else {
        setDragError(res.message || 'Taşıma işlemi başarısız.')
        setTimeout(function () { setDragError(null) }, 3000)
      }
    })
  }, [activeChartId, loadChartDetail])

  // ── Derived state ────────────────────────────────────────

  var tree = buildTree(nodes)
  var existingUserIds = new Set(
    nodes.filter(function (n) { return n.nodeType === 'User' || !n.nodeType })
         .map(function (n) { return n.userId })
         .filter(Boolean)
  )

  // ── Root drop zone ───────────────────────────────────────

  function RootDropZone() {
    var { isOver, setNodeRef } = useDroppable({ id: '__root-drop' })
    return (
      <div
        ref={setNodeRef}
        style={{
          minHeight: 40,
          borderRadius: 8,
          border: isOver ? '2px dashed #6366f1' : '2px dashed transparent',
          margin: '8px 0',
          transition: 'border-color .15s',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          color: isOver ? '#6366f1' : 'transparent',
          fontSize: '.78rem',
        }}
      >
        {isOver ? 'Kök seviyeye bırak' : ''}
      </div>
    )
  }

  // ── Render ──────────────────────────────────────────────

  if (loading) {
    return (
      <div className="w-full h-full flex items-center justify-center gap-2 text-slate-400">
        <Loader2 size={22} className="nw-spin" /> Yükleniyor...
      </div>
    )
  }

  return (
    <DndContext
      sensors={sensors}
      onDragStart={handleDragStart}
      onDragEnd={handleDragEnd}
      onDragCancel={handleDragCancel}
    >
      <DragOverlay dropAnimation={{ duration: 180, easing: 'cubic-bezier(.2,.7,.4,1.1)' }}>
        {activeDragNode ? <DragGhostCard node={activeDragNode} /> : null}
      </DragOverlay>
      <div className="w-full h-full flex flex-row overflow-hidden font-sans text-sm text-slate-800 dark:text-slate-200">

        {/* ═══ Left: Chart List ═══ */}
        <div className="oc-sidebar">
          <div className="oc-sidebar-header">
            <h4 className="oc-sidebar-title">
              <Network size={15} /> Şemalar
            </h4>
            <button className="oc-sidebar-add" onClick={function () { setNewChartModal(true) }} title="Yeni şema">
              <Plus size={14} />
            </button>
          </div>
          <div className="oc-sidebar-list">
            {charts.length === 0 ? (
              <div className="oc-sidebar-empty">
                <Users size={28} style={{ opacity: .2 }} />
                <span>Henüz şema yok</span>
                <button className="oc-gen-btn" onClick={handleGenerateDefault}>
                  <RefreshCw size={13} /> Varsayılandan Oluştur
                </button>
              </div>
            ) : (
              charts.map(function (c) {
                var active = c.id === activeChartId
                return (
                  <div
                    key={c.id}
                    className={'oc-chart-item' + (active ? ' oc-chart-item--active' : '')}
                    onClick={function () { setActiveChartId(c.id) }}
                  >
                    <Network size={15} className="oc-chart-item-ico" />
                    <span className="oc-chart-item-name">{c.name}</span>
                    {c.isDefault && <Star size={12} className="oc-chart-item-star" />}
                    <div className="oc-chart-item-actions">
                      {!c.isDefault && (
                        <button title="Varsayılan yap" onClick={function (e) { e.stopPropagation(); handleSetDefault(c.id) }}>
                          <Star size={12} />
                        </button>
                      )}
                      <button title="Sil" onClick={function (e) { e.stopPropagation(); handleDeleteChart(c.id) }}>
                        <Trash2 size={12} />
                      </button>
                    </div>
                  </div>
                )
              })
            )}
          </div>
          {charts.length > 0 && (
            <div className="oc-sidebar-footer">
              <button className="oc-gen-btn" onClick={handleGenerateDefault}>
                <RefreshCw size={12} /> Varsayılandan Oluştur
              </button>
            </div>
          )}
        </div>

        {/* ═══ Right: Tree View ═══ */}
        <div className="oc-main">
          {!activeChart ? (
            <div className="oc-main-empty">
              <Network size={48} style={{ opacity: .1 }} />
              <span>Bir şema seçin veya yeni şema oluşturun</span>
            </div>
          ) : detailLoading ? (
            <div className="oc-main-empty">
              <Loader2 size={22} className="nw-spin" /> Yükleniyor...
            </div>
          ) : (
            <>
              {/* Toolbar */}
              <div className="oc-toolbar">
                <h3 className="oc-toolbar-title">{activeChart.name}</h3>
                <div className="oc-toolbar-actions">
                  <button className="oc-btn oc-btn--secondary" onClick={function () { setAddRootModal(true) }}>
                    <UserPlus size={14} /> Node Ekle
                  </button>
                </div>
              </div>

              {/* Validation warnings */}
              {validationWarnings.length > 0 && (
                <div style={{ background: 'rgba(245,158,11,.1)', borderRadius: 8, padding: '8px 14px', margin: '0 16px 8px', fontSize: '.8rem', color: '#b45309' }}>
                  {validationWarnings.map(function (w, i) { return <div key={i}>⚠ {w}</div> })}
                </div>
              )}

              {/* Drag error */}
              {dragError && (
                <div style={{ background: 'rgba(239,68,68,.1)', borderRadius: 8, padding: '8px 14px', margin: '0 16px 8px', fontSize: '.8rem', color: '#dc2626' }}>
                  ✕ {dragError}
                </div>
              )}

              {/* Tree */}
              <div className="oc-tree-scroll">
                {tree.rootNodes.length === 0 ? (
                  <div className="oc-main-empty">
                    <Users size={36} style={{ opacity: .15 }} />
                    <span>Şemaya node ekleyin</span>
                  </div>
                ) : (
                  <div className="oc-tree">
                    <RootDropZone />
                    {tree.rootNodes.map(function (node) {
                      return (
                        <OrgTreeNode
                          key={node.id}
                          node={node}
                          childMap={tree.childMap}
                          users={users}
                          departments={departments}
                          existingUserIds={existingUserIds}
                          onAddChild={function () { }}
                          chartId={activeChartId}
                          onRefresh={function () { loadChartDetail(activeChartId) }}
                        />
                      )
                    })}
                  </div>
                )}
              </div>

              {/* Root-level add node modal */}
              {addRootModal && (
                <AddNodeModal
                  chartId={activeChartId}
                  parentNodeId={null}
                  users={users}
                  departments={departments}
                  existingUserIds={existingUserIds}
                  onClose={function () { setAddRootModal(false) }}
                  onAdded={function () { setAddRootModal(false); loadChartDetail(activeChartId) }}
                />
              )}
            </>
          )}
        </div>
      </div>

      {/* New chart modal (replaces prompt()) */}
      {newChartModal && (
        <NewChartModal
          onClose={function () { setNewChartModal(false) }}
          onCreated={handleChartCreated}
        />
      )}
    </DndContext>
  )
}
