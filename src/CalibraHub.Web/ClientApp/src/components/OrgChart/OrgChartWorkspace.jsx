import { useState, useCallback, useRef, useEffect } from 'react'
import {
  Network, Plus, Star, Trash2, MoreHorizontal, ChevronDown, ChevronRight,
  Users, Search, X, Loader2, RefreshCw, UserPlus
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

function avatarColor(userId) {
  var hash = 0
  var s = String(userId)
  for (var i = 0; i < s.length; i++) hash = ((hash << 5) - hash + s.charCodeAt(i)) | 0
  return AVATAR_COLORS[Math.abs(hash) % AVATAR_COLORS.length]
}

function buildTree(nodes, users) {
  var userMap = {}
  users.forEach(function (u) { userMap[u.id] = u })

  var rootNodes = []
  var childMap = {}

  nodes.forEach(function (n) {
    var key = n.parentUserId || '__root'
    if (!childMap[key]) childMap[key] = []
    childMap[key].push(n)
  })

  // Sort children by sortOrder
  Object.keys(childMap).forEach(function (key) {
    childMap[key].sort(function (a, b) { return a.sortOrder - b.sortOrder })
  })

  return { rootNodes: childMap['__root'] || [], childMap: childMap, userMap: userMap }
}

/* ══════════════════════════════════════════════════════════
   OrgTreeNode — recursive card + children
   ══════════════════════════════════════════════════════════ */
function OrgTreeNode(props) {
  var node = props.node
  var childMap = props.childMap
  var userMap = props.userMap
  var deptMap = props.deptMap
  var users = props.users
  var existingUserIds = props.existingUserIds
  var onRemove = props.onRemove
  var onAddChild = props.onAddChild

  var user = userMap[node.userId]
  var dept = user ? deptMap[user.departmentId] : null
  var children = childMap[node.userId] || []

  var [expanded, setExpanded] = useState(true)
  var [pickerOpen, setPickerOpen] = useState(false)

  if (!user) return null

  return (
    <div className="oc-branch">
      {/* Card */}
      <div className="oc-card">
        <div className="oc-avatar" style={{ background: avatarColor(user.id) }}>
          {getInitials(user.fullName)}
        </div>
        <div className="oc-card-info">
          <div className="oc-card-name">{user.fullName}</div>
          <div className="oc-card-title">{node.positionTitle || user.role || ''}</div>
          {dept && <div className="oc-card-dept">{dept.name}</div>}
        </div>
        {children.length > 0 && (
          <button className="oc-card-toggle" onClick={function () { setExpanded(!expanded) }}>
            {expanded ? <ChevronDown size={14} /> : <ChevronRight size={14} />}
          </button>
        )}
        {/* Alt kullanici ekle — bu kartin cocuklarini artirir */}
        <div style={{ position: 'relative' }}>
          <button className="oc-card-add-child" onClick={function () { setPickerOpen(!pickerOpen) }} title="Bu kullanicinin altina ekle">
            <UserPlus size={12} />
          </button>
          {pickerOpen && (
            <UserPicker
              users={users}
              existingUserIds={existingUserIds}
              onSelect={function (picked) {
                setPickerOpen(false)
                onAddChild(user.id, picked)
              }}
              onClose={function () { setPickerOpen(false) }}
            />
          )}
        </div>
        <button className="oc-card-remove" onClick={function () { onRemove(node.id) }} title="Cikar">
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
                userMap={userMap}
                deptMap={deptMap}
                users={users}
                existingUserIds={existingUserIds}
                onRemove={onRemove}
                onAddChild={onAddChild}
              />
            )
          })}
        </div>
      )}
    </div>
  )
}

/* ══════════════════════════════════════════════════════════
   UserPicker — add user to chart
   ══════════════════════════════════════════════════════════ */
function UserPicker(props) {
  var users = props.users
  var existingUserIds = props.existingUserIds
  var onSelect = props.onSelect
  var onClose = props.onClose
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
    return u.fullName.toLowerCase().indexOf(q) !== -1 ||
           u.email.toLowerCase().indexOf(q) !== -1
  })

  return (
    <div className="oc-picker">
      <div className="oc-picker-search">
        <Search size={13} />
        <input
          ref={searchRef}
          placeholder="Kullanici ara..."
          value={search}
          onChange={function (e) { setSearch(e.target.value) }}
          onKeyDown={function (e) { if (e.key === 'Escape') onClose() }}
        />
      </div>
      <div className="oc-picker-list">
        {available.length === 0 ? (
          <div className="oc-picker-empty">Uygun kullanici bulunamadi</div>
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
  var [pickerOpen, setPickerOpen] = useState(false)
  var [dirty, setDirty] = useState(false)

  // Load chart list
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

  // Load chart detail when active chart changes
  useEffect(function () {
    if (!activeChartId) return
    setDetailLoading(true)
    api.getChartDetail(activeChartId)
      .then(function (data) {
        if (!data.success) return
        setNodes(data.nodes || [])
        setUsers(data.users || [])
        setDepartments(data.departments || [])
        setDirty(false)
      })
      .catch(function (e) { console.error('[OrgChart] load detail error:', e) })
      .finally(function () { setDetailLoading(false) })
  }, [activeChartId])

  var activeChart = charts.find(function (c) { return c.id === activeChartId }) || null

  // ── Handlers ───────────────────────────────────────

  var handleNewChart = useCallback(function () {
    var name = prompt('Sema adi:')
    if (!name || !name.trim()) return
    api.saveChart({ name: name.trim() })
      .then(function (res) {
        if (!res.success) return
        setCharts(function (prev) { return prev.concat([{ id: res.id, name: name.trim(), isDefault: false }]) })
        setActiveChartId(res.id)
      })
  }, [])

  var handleDeleteChart = useCallback(async function (id) {
    // Rapor §6.6 — CalibraAlert.confirm fallback
    var ok = window.CalibraAlert && window.CalibraAlert.confirm
      ? await window.CalibraAlert.confirm('Bu semayi silmek istediginizden emin misiniz?',
          { title: 'Şemayı Sil', okText: 'Evet, Sil', cancelText: 'Vazgeç', danger: true })
      : confirm('Bu semayi silmek istediginizden emin misiniz?')
    if (!ok) return
    api.deleteChart(id)
      .then(function () {
        setCharts(function (prev) { return prev.filter(function (c) { return c.id !== id }) })
        if (activeChartId === id) {
          setActiveChartId(null)
          setNodes([])
        }
      })
  }, [activeChartId])

  var handleSetDefault = useCallback(function (id) {
    api.setDefaultChart(id)
      .then(function () {
        setCharts(function (prev) {
          return prev.map(function (c) { return { ...c, isDefault: c.id === id } })
        })
      })
  }, [])

  var handleGenerateDefault = useCallback(function () {
    api.generateDefaultChart()
      .then(function (res) {
        if (!res.success) return
        // Reload charts
        api.getCharts().then(function (data) {
          setCharts(data || [])
          setActiveChartId(res.id)
        })
      })
  }, [])

  var handleAddUser = useCallback(function (user) {
    // Toolbar'daki "Kullanici Ekle" → ROOT seviyede eklenir (en ust)
    setPickerOpen(false)
    var newNode = {
      id: crypto.randomUUID ? crypto.randomUUID() : 'n-' + Date.now(),
      userId: user.id,
      parentUserId: null,
      positionTitle: user.role || '',
      sortOrder: nodes.length,
    }
    setNodes(function (prev) { return prev.concat([newNode]) })
    setDirty(true)
  }, [nodes])

  var handleAddChild = useCallback(function (parentUserId, user) {
    // Kart icindeki "+ Alt Bagla" → belirtilen node'un ALTINA eklenir
    var newNode = {
      id: crypto.randomUUID ? crypto.randomUUID() : 'n-' + Date.now(),
      userId: user.id,
      parentUserId: parentUserId,
      positionTitle: user.role || '',
      sortOrder: nodes.filter(function (n) { return n.parentUserId === parentUserId }).length,
    }
    setNodes(function (prev) { return prev.concat([newNode]) })
    setDirty(true)
  }, [nodes])

  var handleRemoveNode = useCallback(function (nodeId) {
    setNodes(function (prev) { return prev.filter(function (n) { return n.id !== nodeId }) })
    setDirty(true)
  }, [])

  var handleSaveNodes = useCallback(function () {
    if (!activeChartId) return
    api.saveNodes(activeChartId, nodes)
      .then(function () { setDirty(false) })
      .catch(function (e) { console.error('[OrgChart] save error:', e) })
  }, [activeChartId, nodes])

  // Build tree
  var deptMap = {}
  departments.forEach(function (d) { deptMap[d.id] = d })
  var tree = buildTree(nodes, users)
  var existingUserIds = new Set(nodes.map(function (n) { return n.userId }))

  // ── Render ─────────────────────────────────────────

  if (loading) {
    return (
      <div className="w-full h-full flex items-center justify-center gap-2 text-slate-400">
        <Loader2 size={22} className="nw-spin" /> Yukleniyor...
      </div>
    )
  }

  return (
    <div className="w-full h-full flex flex-row overflow-hidden font-sans text-sm text-slate-800 dark:text-slate-200">

      {/* ═══ Left: Chart List ═══ */}
      <div className="oc-sidebar">
        <div className="oc-sidebar-header">
          <h4 className="oc-sidebar-title">
            <Network size={15} /> Semalar
          </h4>
          <button className="oc-sidebar-add" onClick={handleNewChart} title="Yeni sema">
            <Plus size={14} />
          </button>
        </div>
        <div className="oc-sidebar-list">
          {charts.length === 0 ? (
            <div className="oc-sidebar-empty">
              <Users size={28} style={{ opacity: .2 }} />
              <span>Henuz sema yok</span>
              <button className="oc-gen-btn" onClick={handleGenerateDefault}>
                <RefreshCw size={13} /> Varsayilandan Olustur
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
                      <button title="Varsayilan yap" onClick={function (e) { e.stopPropagation(); handleSetDefault(c.id) }}>
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
              <RefreshCw size={12} /> Varsayilandan Olustur
            </button>
          </div>
        )}
      </div>

      {/* ═══ Right: Tree View ═══ */}
      <div className="oc-main">
        {!activeChart ? (
          <div className="oc-main-empty">
            <Network size={48} style={{ opacity: .1 }} />
            <span>Bir sema secin veya yeni bir sema olusturun</span>
          </div>
        ) : detailLoading ? (
          <div className="oc-main-empty">
            <Loader2 size={22} className="nw-spin" /> Yukleniyor...
          </div>
        ) : (
          <>
            {/* Toolbar */}
            <div className="oc-toolbar">
              <h3 className="oc-toolbar-title">{activeChart.name}</h3>
              <div className="oc-toolbar-actions">
                <div style={{ position: 'relative' }}>
                  <button className="oc-btn oc-btn--secondary" onClick={function () { setPickerOpen(!pickerOpen) }}>
                    <UserPlus size={14} /> Kullanici Ekle
                  </button>
                  {pickerOpen && (
                    <UserPicker
                      users={users}
                      existingUserIds={existingUserIds}
                      onSelect={handleAddUser}
                      onClose={function () { setPickerOpen(false) }}
                    />
                  )}
                </div>
                {dirty && (
                  <button className="oc-btn oc-btn--primary" onClick={handleSaveNodes}>
                    Kaydet
                  </button>
                )}
              </div>
            </div>

            {/* Tree */}
            <div className="oc-tree-scroll">
              {tree.rootNodes.length === 0 ? (
                <div className="oc-main-empty">
                  <Users size={36} style={{ opacity: .15 }} />
                  <span>Semaya kullanici ekleyin</span>
                </div>
              ) : (
                <div className="oc-tree">
                  {tree.rootNodes.map(function (node) {
                    return (
                      <OrgTreeNode
                        key={node.id}
                        node={node}
                        childMap={tree.childMap}
                        userMap={tree.userMap}
                        deptMap={deptMap}
                        users={users}
                        existingUserIds={existingUserIds}
                        onRemove={handleRemoveNode}
                        onAddChild={handleAddChild}
                      />
                    )
                  })}
                </div>
              )}
            </div>
          </>
        )}
      </div>
    </div>
  )
}
