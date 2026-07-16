import { useState, useCallback, useEffect, useRef } from 'react'
import {
  ReactFlow, Background, Controls, MiniMap,
  addEdge, useNodesState, useEdgesState,
  MarkerType, Panel,
} from '@xyflow/react'
import '@xyflow/react/dist/style.css'
import {
  GitMerge, Plus, Save, CheckCircle, Send, Copy,
  AlertTriangle, ChevronLeft, Loader2, Settings2,
  Play, Square, GitBranch, Merge, Zap, X, Library,
} from 'lucide-react'

/* ══════════════════════════════════════════════════════════
   Constants
   ══════════════════════════════════════════════════════════ */
var BASE = '/WorkflowDefinition'

var NODE_TYPES_META = [
  { type: 'Start',        label: 'Başlangıç', icon: Play,      color: '#22c55e', bg: '#dcfce7' },
  { type: 'Task',         label: 'Görev',     icon: Settings2,  color: '#6366f1', bg: '#e0e7ff' },
  { type: 'Decision',     label: 'Karar',     icon: GitBranch,  color: '#f59e0b', bg: '#fef3c7' },
  { type: 'ParallelSplit',label: 'Paralel Ayrım', icon: Zap,   color: '#8b5cf6', bg: '#ede9fe' },
  { type: 'ParallelJoin', label: 'Paralel Birleşim', icon: Merge, color: '#0ea5e9', bg: '#e0f2fe' },
  { type: 'End',          label: 'Bitiş',     icon: Square,     color: '#ef4444', bg: '#fee2e2' },
]

var NODE_TYPE_MAP = Object.fromEntries(NODE_TYPES_META.map(m => [m.type, m]))

function getCsrf() {
  var el = document.querySelector('input[name="__RequestVerificationToken"]')
  return el ? el.value : ''
}

function postJson(url, body) {
  return fetch(BASE + url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': getCsrf() },
    credentials: 'same-origin',
    body: JSON.stringify(body),
  }).then(r => r.json())
}

function getJson(url) {
  return fetch(BASE + url, { credentials: 'same-origin' }).then(r => r.json())
}

/* ══════════════════════════════════════════════════════════
   Custom node renderer for React Flow
   ══════════════════════════════════════════════════════════ */
function WfNode({ data, selected }) {
  var meta = NODE_TYPE_MAP[data.nodeType] || NODE_TYPE_MAP['Task']
  var Icon = meta.icon
  var style = {
    borderRadius: data.nodeType === 'Decision' ? '8px' : '10px',
    padding: '10px 14px',
    minWidth: 130,
    cursor: 'grab',
    display: 'flex', alignItems: 'center', gap: 8,
    fontFamily: 'sans-serif', fontSize: '.82rem', fontWeight: 600,
    transition: 'border-color .15s, box-shadow .15s',
  }
  if (selected) {
    style.background = meta.bg
    style.border = `2px solid ${meta.color}`
    style.boxShadow = `0 0 0 3px ${meta.color}33`
  }
  return (
    <div className="wf-node" style={style}>
      <Icon size={15} style={{ color: meta.color, flexShrink: 0 }} />
      <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', maxWidth: 110 }}>
        {data.label}
      </span>
    </div>
  )
}

var nodeTypes = { wfNode: WfNode }

/* ══════════════════════════════════════════════════════════
   PropertiesPanel
   ══════════════════════════════════════════════════════════ */
function PropertiesPanel({ selected, onUpdate, onDelete }) {
  var [localName, setLocalName] = useState('')
  var [localCond, setLocalCond] = useState('')
  var [actorType, setActorType] = useState('User')
  var [actorRef, setActorRef] = useState('')
  var [timeout, setTimeout2] = useState('')
  var [onReject, setOnReject] = useState('Cancel')
  var [joinTokens, setJoinTokens] = useState('')
  var isNode = selected && selected.kind === 'node'
  var isEdge = selected && selected.kind === 'edge'

  useEffect(function () {
    if (!selected) return
    if (isNode) {
      setLocalName(selected.data.label || '')
      setActorType(selected.data.actorType || 'User')
      setActorRef(selected.data.actorRefId || '')
      setTimeout2(selected.data.timeoutHours || '')
      setOnReject(selected.data.onRejectPolicy || 'Cancel')
      setJoinTokens(selected.data.joinExpectedTokens || '')
    }
    if (isEdge) setLocalCond(selected.data.condition || '')
  }, [selected])

  if (!selected) {
    return (
      <div className="wf-props-empty">
        <GitMerge size={28} style={{ opacity: .2 }} />
        <span>Node veya bağlantı seçin</span>
      </div>
    )
  }

  function saveNode() {
    onUpdate({
      ...selected,
      data: {
        ...selected.data,
        label: localName,
        actorType: actorType,
        actorRefId: actorRef,
        timeoutHours: timeout ? parseInt(timeout) : null,
        onRejectPolicy: onReject,
        joinExpectedTokens: joinTokens ? parseInt(joinTokens) : null,
      },
    })
  }

  function saveEdge() {
    onUpdate({ ...selected, data: { ...selected.data, condition: localCond } })
  }

  return (
    <div className="wf-props-panel">
      <div className="wf-props-title">{isNode ? 'Node Özellikleri' : 'Bağlantı Özellikleri'}</div>

      {isNode && (
        <>
          <div className="wf-props-type-badge" style={{ background: (NODE_TYPE_MAP[selected.data.nodeType] || {}).bg }}>
            {(NODE_TYPE_MAP[selected.data.nodeType] || {}).label || selected.data.nodeType}
          </div>
          <label className="wf-label">Ad</label>
          <input className="wf-input" value={localName} onChange={e => setLocalName(e.target.value)} />

          {selected.data.nodeType === 'Task' && (
            <>
              <label className="wf-label">Aktör Türü</label>
              <select className="wf-input" value={actorType} onChange={e => setActorType(e.target.value)}>
                <option value="User">Kullanıcı</option>
                <option value="Role">Rol</option>
                <option value="Department">Departman</option>
                <option value="Expression">NCalc İfade</option>
              </select>
              <label className="wf-label">Aktör Referans / İfade</label>
              <input className="wf-input" value={actorRef} onChange={e => setActorRef(e.target.value)}
                     placeholder={actorType === 'Expression' ? 'OrgChart.SupervisorOf(userId)' : 'ID veya değer'} />
              <label className="wf-label">Zaman Aşımı (saat)</label>
              <input className="wf-input" type="number" min="1" value={timeout} onChange={e => setTimeout2(e.target.value)} />
              <label className="wf-label">Red Politikası</label>
              <select className="wf-input" value={onReject} onChange={e => setOnReject(e.target.value)}>
                <option value="Cancel">İptal Et</option>
                <option value="Return">Geri Gönder</option>
              </select>
            </>
          )}

          {selected.data.nodeType === 'ParallelJoin' && (
            <>
              <label className="wf-label">Beklenen Token Sayısı</label>
              <input className="wf-input" type="number" min="1" value={joinTokens} onChange={e => setJoinTokens(e.target.value)} />
            </>
          )}

          <div style={{ display: 'flex', gap: 8, marginTop: 12 }}>
            <button className="wf-btn wf-btn--primary" onClick={saveNode} style={{ flex: 1 }}>Kaydet</button>
            <button className="wf-btn wf-btn--danger" onClick={() => onDelete(selected)}>
              <X size={14} />
            </button>
          </div>
        </>
      )}

      {isEdge && (
        <>
          <label className="wf-label">Koşul (NCalc)</label>
          <textarea className="wf-input" rows={3} value={localCond}
                    onChange={e => setLocalCond(e.target.value)}
                    placeholder="Amount > 10000" />
          <button className="wf-btn wf-btn--primary" onClick={saveEdge} style={{ marginTop: 8, width: '100%' }}>
            Kaydet
          </button>
        </>
      )}
    </div>
  )
}

/* ══════════════════════════════════════════════════════════
   WorkflowEditor — Main Component
   ══════════════════════════════════════════════════════════ */
export default function WorkflowEditor({ definitionId }) {
  var [definition, setDefinition] = useState(null)
  var [nodes, setNodes, onNodesChange] = useNodesState([])
  var [edges, setEdges, onEdgesChange] = useEdgesState([])
  var [selected, setSelected] = useState(null)
  var [loading, setLoading] = useState(!!definitionId)
  var [saving, setSaving] = useState(false)
  var [validating, setValidating] = useState(false)
  var [warnings, setWarnings] = useState([])
  var [dirty, setDirty] = useState(false)
  var [defName, setDefName] = useState('')
  var [defDesc, setDefDesc] = useState('')
  var [addTypeOpen, setAddTypeOpen] = useState(false)

  // ── Template picker (new workflow only) ──────────────────
  var [showTemplatePicker, setShowTemplatePicker] = useState(!definitionId)
  var [templates, setTemplates] = useState([])
  var [templateName, setTemplateName] = useState('')
  var [applyingTemplate, setApplyingTemplate] = useState(false)

  useEffect(function () {
    if (!showTemplatePicker) return
    getJson('/GetTemplates')
      .then(function (res) { if (Array.isArray(res)) setTemplates(res) })
      .catch(function () {})
  }, [showTemplatePicker])

  async function handleApplyTemplate(templateId) {
    var name = templateName.trim()
    if (!name) { alert('Lütfen workflow için bir ad girin.'); return }
    setApplyingTemplate(true)
    try {
      var res = await postJson('/ApplyTemplateJson', { templateId, name })
      if (res.ok) window.location.href = '/WorkflowDefinition/DefinitionEdit?id=' + res.definitionId
      else alert(res.error || 'Şablon uygulanamadı.')
    } catch (ex) {
      alert('Hata: ' + ex.message)
    } finally {
      setApplyingTemplate(false)
    }
  }

  function handleStartBlank() {
    setDefName(templateName.trim() || 'Yeni Workflow')
    setShowTemplatePicker(false)
  }

  // ── Load existing definition ─────────────────────────────

  useEffect(function () {
    if (!definitionId) { setLoading(false); return }
    getJson('/GetDefinitionJson?id=' + definitionId)
      .then(function (res) {
        if (!res.success) return
        var d = res.data
        setDefinition(d.definition)
        setDefName(d.definition.name)
        setDefDesc(d.definition.description || '')
        setNodes(d.nodes.map(n => toFlowNode(n)))
        setEdges(d.transitions.map(t => toFlowEdge(t)))
      })
      .catch(e => console.error('[WFEditor] load error', e))
      .finally(() => setLoading(false))
  }, [definitionId])

  // ── Helpers ──────────────────────────────────────────────

  function toFlowNode(n) {
    return {
      id: String(n.id),
      type: 'wfNode',
      position: { x: n.positionX, y: n.positionY },
      data: {
        label: n.name,
        nodeType: n.nodeType,
        dbId: n.id,
        actorType: n.actorType,
        actorRefId: n.actorRefId,
        actorExpression: n.actorExpression,
        timeoutHours: n.timeoutHours,
        onRejectPolicy: n.onRejectPolicy,
        joinExpectedTokens: n.joinExpectedTokens,
      },
    }
  }

  function toFlowEdge(t) {
    return {
      id: String(t.id),
      source: String(t.fromNodeId),
      target: String(t.toNodeId),
      label: t.condition || t.label || '',
      data: { dbId: t.id, condition: t.condition, label: t.label, isDefault: t.isDefault },
      markerEnd: { type: MarkerType.ArrowClosed },
      style: t.isDefault ? { strokeDasharray: '6 3' } : {},
    }
  }

  // ── Connect ──────────────────────────────────────────────

  var onConnect = useCallback(function (params) {
    setEdges(eds => addEdge({
      ...params,
      markerEnd: { type: MarkerType.ArrowClosed },
      data: { condition: '', isDefault: false },
    }, eds))
    setDirty(true)
  }, [setEdges])

  // ── Selection ────────────────────────────────────────────

  function onNodeClick(_, node) {
    setSelected({ kind: 'node', id: node.id, data: node.data })
  }
  function onEdgeClick(_, edge) {
    setSelected({ kind: 'edge', id: edge.id, data: edge.data || {} })
  }
  function onPaneClick() { setSelected(null) }

  // ── Add node ─────────────────────────────────────────────

  function addNode(nodeType) {
    setAddTypeOpen(false)
    var meta = NODE_TYPE_MAP[nodeType]
    var id = 'new-' + Date.now()
    var newNode = {
      id,
      type: 'wfNode',
      position: { x: 200 + Math.random() * 200, y: 100 + Math.random() * 200 },
      data: { label: meta.label, nodeType, dbId: null },
    }
    setNodes(ns => [...ns, newNode])
    setDirty(true)
  }

  // ── Properties update ────────────────────────────────────

  function handleUpdate(updated) {
    if (updated.kind === 'node') {
      setNodes(ns => ns.map(n => n.id === updated.id ? { ...n, data: updated.data } : n))
    } else {
      setEdges(es => es.map(e => e.id === updated.id ? { ...e, data: updated.data, label: updated.data.condition || '' } : e))
    }
    setSelected(updated)
    setDirty(true)
  }

  function handleDelete(sel) {
    if (sel.kind === 'node') setNodes(ns => ns.filter(n => n.id !== sel.id))
    else setEdges(es => es.filter(e => e.id !== sel.id))
    setSelected(null)
    setDirty(true)
  }

  // ── Save ─────────────────────────────────────────────────

  async function handleSave() {
    setSaving(true)
    try {
      var defRes = await postJson('/SaveDefinitionJson', {
        id: definition?.id || null,
        name: defName,
        description: defDesc,
        documentTypeId: definition?.documentTypeId || null,
        isActive: true,
      })
      if (!defRes.success) { alert(defRes.message); return }
      var defId = defRes.id

      // Save all nodes
      var nodeIdMap = {}
      for (var n of nodes) {
        var nodeReq = {
          id: n.data.dbId || null,
          definitionId: defId,
          nodeType: n.data.nodeType,
          name: n.data.label,
          positionX: Math.round(n.position.x),
          positionY: Math.round(n.position.y),
          actorType: n.data.actorType || null,
          actorRefId: n.data.actorRefId || null,
          actorExpression: n.data.actorExpression || null,
          timeoutHours: n.data.timeoutHours || null,
          onRejectPolicy: n.data.onRejectPolicy || null,
          joinExpectedTokens: n.data.joinExpectedTokens || null,
        }
        var nRes = await postJson('/SaveNodeJson', nodeReq)
        if (nRes.success) nodeIdMap[n.id] = nRes.id
      }

      // Save all edges
      for (var e of edges) {
        var fromId = nodeIdMap[e.source]
        var toId = nodeIdMap[e.target]
        if (!fromId || !toId) continue
        await postJson('/SaveTransitionJson', {
          id: e.data?.dbId || null,
          definitionId: defId,
          fromNodeId: fromId,
          toNodeId: toId,
          label: e.data?.label || null,
          condition: e.data?.condition || null,
          priority: 0,
          isDefault: e.data?.isDefault || false,
        })
      }

      setDirty(false)
      if (!definition) window.location.href = '/WorkflowDefinition/DefinitionEdit?id=' + defId
    } catch (ex) {
      alert('Kayıt hatası: ' + ex.message)
    } finally {
      setSaving(false)
    }
  }

  // ── Validate ─────────────────────────────────────────────

  async function handleValidate() {
    if (!definition?.id) { alert('Önce kaydedin.'); return }
    setValidating(true)
    try {
      var res = await getJson('/ValidateDefinitionJson?id=' + definition.id)
      setWarnings(res.errors || [])
    } finally { setValidating(false) }
  }

  // ── Publish ──────────────────────────────────────────────

  async function handlePublish() {
    if (!definition?.id) { alert('Önce kaydedin.'); return }
    var res = await postJson('/PublishDefinitionJson', { id: definition.id })
    if (res.success) { setDefinition(d => ({ ...d, isPublished: true })); setWarnings([]) }
    else alert(res.message)
  }

  // ── Render ───────────────────────────────────────────────

  // ── Template picker screen ───────────────────────────────
  if (showTemplatePicker) {
    return (
      <div className="wf-template-picker">
        <div className="wf-template-picker__card">
          <div className="wf-template-picker__header">
            <Library size={28} style={{ color: '#6366f1' }} />
            <h2>Yeni Workflow Oluştur</h2>
          </div>

          <div className="wf-template-picker__name-row">
            <label className="wf-label">Workflow Adı</label>
            <input
              className="wf-input"
              autoFocus
              value={templateName}
              onChange={e => setTemplateName(e.target.value)}
              placeholder="ör. Satış Teklifi Onayı"
              onKeyDown={e => e.key === 'Enter' && handleStartBlank()}
            />
          </div>

          <div className="wf-template-picker__section-title">Hazır Şablondan Başla</div>
          <div className="wf-template-picker__grid">
            {templates.map(t => (
              <button
                key={t.id}
                className="wf-template-card"
                onClick={() => handleApplyTemplate(t.id)}
                disabled={applyingTemplate}
              >
                {applyingTemplate
                  ? <Loader2 size={16} className="nw-spin" />
                  : <GitMerge size={16} style={{ color: '#6366f1' }} />
                }
                <div>
                  <div className="wf-template-card__name">{t.name}</div>
                  <div className="wf-template-card__desc">{t.description}</div>
                </div>
              </button>
            ))}
          </div>

          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginTop: 24 }}>
            <button
              className="wf-btn"
              onClick={() => window.location.href = '/WorkflowDefinition/Definitions'}
            >
              <ChevronLeft size={14} /> İptal
            </button>
            <button className="wf-btn wf-btn--primary" onClick={handleStartBlank}>
              Boş Başla
            </button>
          </div>
        </div>
      </div>
    )
  }

  if (loading) {
    return (
      <div className="wf-loading">
        <Loader2 size={24} className="nw-spin" /> Yükleniyor...
      </div>
    )
  }

  return (
    <div className="wf-editor-root">

      {/* ═══ Toolbar ═══ */}
      <div className="wf-toolbar">
        <button className="wf-toolbar-back" onClick={() => window.location.href = '/WorkflowDefinition/Definitions'}>
          <ChevronLeft size={16} />
        </button>
        <div className="wf-toolbar-title">
          <input
            className="wf-toolbar-name"
            value={defName}
            onChange={e => { setDefName(e.target.value); setDirty(true) }}
            placeholder="Workflow adı..."
          />
          {dirty && <span className="wf-dirty-dot" title="Kaydedilmemiş değişiklikler" />}
        </div>
        <div style={{ flex: 1 }} />

        {warnings.length > 0 && (
          <div className="wf-warn-chip">
            <AlertTriangle size={13} /> {warnings.length} uyarı
          </div>
        )}
        {definition?.isPublished && (
          <div className="wf-pub-chip"><CheckCircle size={13} /> Yayında</div>
        )}

        <button className="wf-toolbar-btn" onClick={() => setAddTypeOpen(o => !o)} title="Node ekle">
          <Plus size={16} />
        </button>
        <button className="wf-toolbar-btn" onClick={handleValidate} disabled={validating} title="Doğrula">
          {validating ? <Loader2 size={16} className="nw-spin" /> : <CheckCircle size={16} />}
        </button>
        <button className="wf-toolbar-btn" onClick={handlePublish} title="Yayınla">
          <Send size={16} />
        </button>
        <button className="wf-toolbar-btn wf-toolbar-btn--primary" onClick={handleSave} disabled={saving} title="Kaydet">
          {saving ? <Loader2 size={16} className="nw-spin" /> : <Save size={16} />}
          Kaydet
        </button>
      </div>

      {/* Add node type picker */}
      {addTypeOpen && (
        <div className="wf-add-palette">
          {NODE_TYPES_META.map(m => {
            var Icon = m.icon
            return (
              <button key={m.type} className="wf-palette-btn" onClick={() => addNode(m.type)}
                      style={{ borderColor: m.color }}>
                <Icon size={16} style={{ color: m.color }} />
                {m.label}
              </button>
            )
          })}
        </div>
      )}

      {/* Validation warnings */}
      {warnings.length > 0 && (
        <div className="wf-warnings">
          {warnings.map((w, i) => (
            <div key={i} className="wf-warning-item"><AlertTriangle size={13} /> {w}</div>
          ))}
        </div>
      )}

      <div className="wf-canvas-area">
        {/* ═══ React Flow Canvas ═══ */}
        <div className="wf-canvas">
          <ReactFlow
            nodes={nodes}
            edges={edges}
            onNodesChange={c => { onNodesChange(c); setDirty(true) }}
            onEdgesChange={c => { onEdgesChange(c); setDirty(true) }}
            onConnect={onConnect}
            onNodeClick={onNodeClick}
            onEdgeClick={onEdgeClick}
            onPaneClick={onPaneClick}
            nodeTypes={nodeTypes}
            fitView
            deleteKeyCode="Delete"
          >
            <Background />
            <Controls />
            <MiniMap />
            {nodes.length === 0 && (
              <Panel position="top-center">
                <div className="wf-empty-hint">
                  Node eklemek için + butonuna tıklayın
                </div>
              </Panel>
            )}
          </ReactFlow>
        </div>

        {/* ═══ Properties Panel ═══ */}
        <div className="wf-sidebar">
          <div className="wf-sidebar-section">
            <label className="wf-label">Açıklama</label>
            <textarea className="wf-input" rows={2} value={defDesc}
                      onChange={e => { setDefDesc(e.target.value); setDirty(true) }}
                      placeholder="Workflow açıklaması..." />
          </div>
          <PropertiesPanel selected={selected} onUpdate={handleUpdate} onDelete={handleDelete} />
        </div>
      </div>
    </div>
  )
}
