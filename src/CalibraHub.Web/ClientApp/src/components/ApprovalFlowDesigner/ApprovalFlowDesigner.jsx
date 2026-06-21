/**
 * ApprovalFlowDesigner — React Flow tabanlı görsel onay akışı tasarımcısı.
 *
 *   Props:
 *     - apiBase        (string)   API kökü (örn. '/ApprovalFlow')
 *     - flowId         (number)   Akış DB id (0 = yeni)
 *     - initialNodes   (array)    [{ id, type, position, data }]  (React Flow formatı)
 *     - initialEdges   (array)    [{ id, source, target, label, data:{edgeKind,condition} }]
 *     - users          (array)    [{ id, name, email }]
 *     - departments    (array)    [{ id, name }]
 *     - onSave         (function) (payload) => void   — designer state'ini parent'a verir
 *
 *   payload format:
 *     {
 *       nodes: [{ clientId, nodeType, posX, posY, stepName, approverType,
 *                 approverId, approverLabel, nodeDataJson }],
 *       edges: [{ sourceClientId, targetClientId, label, edgeKind, condition, sortOrder }]
 *     }
 *
 *   Mount sayfası (Edit.cshtml) `window._afDesignerPayload`'i okuyarak save'e dahil eder.
 *   Her node/edge değiştirildiğinde onSave otomatik çağrılır (debounced); ayrıca
 *   imperative trigger için window._afDesignerGetPayload() de tanımlanır.
 */
import React, {
  useCallback, useEffect, useMemo, useRef, useState,
} from 'react'
import {
  ReactFlow, ReactFlowProvider,
  Background, Controls, MiniMap, Panel, addEdge,
  applyNodeChanges, applyEdgeChanges,
  MarkerType, useReactFlow,
} from '@xyflow/react'
import '@xyflow/react/dist/style.css'

import { Undo2, Redo2 } from 'lucide-react'

import { nodeTypes } from './nodeTypes.jsx'
import NodePalette from './NodePalette.jsx'
import NodePropertiesPanel from './NodePropertiesPanel.jsx'
import VariablesPanel from './VariablesPanel.jsx'
import { NodeDataCtx, AltKeyCtx } from './nodeDataContext.js'
import './designer.css'

var AFD_MINIMAP_KEY = 'cb-afd-minimap-on'

/* ──────────────────────────────────────────────────────────────
   Helpers
   ────────────────────────────────────────────────────────────── */
function genId() {
  return 'n_' + Date.now().toString(36) + '_' + Math.random().toString(36).slice(2, 6)
}

function defaultDataForType(type) {
  if (type === 'step') {
    return { stepName: 'Yeni Adım', approverType: 'AnyUser', approverId: null, approverLabel: null }
  }
  if (type === 'decision') {
    return { condition: '', conditionRules: [] }
  }
  if (type === 'notification') {
    return {
      notificationType: 'mail',      // mail | whatsapp | both
      recipientMode: 'creator',      // creator | approver | specificUser | department | custom
      recipientId: null,
      recipientLabel: null,
      customEmail: null,
      customPhone: null,
      subject: '',
      body: '',
    }
  }
  if (type === 'parallel') {
    return {
      mode: 'auto',  // auto | split | join — runtime topology'den çıkarır
    }
  }
  if (type === 'integration') {
    return {
      integrationId:   null,    // mevcut Integration tanımının PK'sı
      integrationName: null,    // UI display + Edit reload sırasında label
      recordIdSource:  'entity',// entity = onaydaki belge/iş emri ID'si | custom (sabit)
      customRecordId:  null,
      haltOnError:     true,    // entegrasyon başarısız olursa akışı durdur
    }
  }
  if (type === 'setVariable') {
    return {
      variableName: null,        // hangi değişken (VariablesPanel'de tanımlı)
      expression:   '',          // sabit değer veya "ad + 1" / "ad - 1" gibi basit ifade
    }
  }
  return {}
}

function ensureMinimumNodes(initialNodes) {
  if (Array.isArray(initialNodes) && initialNodes.length > 0) return initialNodes
  // boş başlangıç: bir Start + bir End yerleştir
  return [
    { id: 'start_default', type: 'start', position: { x: 80, y: 80 }, data: {} },
    { id: 'end_default',   type: 'end',   position: { x: 80, y: 280 }, data: {} },
  ]
}

function edgeStyleForKind(kind) {
  if (kind === 'true')    return { stroke: '#10b981', strokeWidth: 2 }                            // Onay/Evet
  if (kind === 'false')   return { stroke: '#ef4444', strokeWidth: 2 }                            // Red/Hayır
  if (kind === 'timeout') return { stroke: '#f59e0b', strokeWidth: 2, strokeDasharray: '5 3' }    // Gecikme
  if (kind === 'info')    return { stroke: '#3b82f6', strokeWidth: 2 }                            // Bilgi/Bildirim
  if (kind === 'error')   return { stroke: '#a855f7', strokeWidth: 2, strokeDasharray: '6 3' }    // Hata
  return { stroke: '#94a3b8', strokeWidth: 1.6 }                                                   // Varsayılan
}

function decorateEdge(edge) {
  var kind = (edge.data && edge.data.edgeKind) || 'default'
  return Object.assign({}, edge, {
    type: 'default',
    className: 'afd-edge afd-edge--' + kind,
    markerEnd: { type: MarkerType.ArrowClosed, color: edgeStyleForKind(kind).stroke },
    style: edgeStyleForKind(kind),
    label: edge.label || (edge.data && edge.data.label) || '',
    labelStyle: { fontSize: '11px', fontWeight: 600 },
    labelBgPadding: [4, 4],
    labelBgBorderRadius: 4,
  })
}

/* ──────────────────────────────────────────────────────────────
   Designer Inner — useReactFlow hook gerekli olduğundan
   ReactFlowProvider içinde render edilir.
   ────────────────────────────────────────────────────────────── */
function DesignerInner(props) {
  var apiBase = props.apiBase || '/ApprovalFlow'
  var flowId = props.flowId || 0
  var users = Array.isArray(props.users) ? props.users : []
  var departments = Array.isArray(props.departments) ? props.departments : []
  var cariGroups = Array.isArray(props.cariGroups) ? props.cariGroups : []
  var materialGroups = (props.materialGroups && typeof props.materialGroups === 'object') ? props.materialGroups : {}
  var sqlQueries = Array.isArray(props.sqlQueries) ? props.sqlQueries : []
  var integrations = Array.isArray(props.integrations) ? props.integrations : []
  // Entity-agnostic plugin: backend registry'den entity tipi kataloğu + aktif tip.
  var entityTypes = Array.isArray(props.entityTypes) ? props.entityTypes : []
  var currentEntityType = typeof props.currentEntityType === 'string' && props.currentEntityType
    ? props.currentEntityType : 'Document'
  // Aktif entity tipinin field listesini bul (DECISION_FIELDS yerine kullanılır).
  var entityTypeFields = (function () {
    var et = entityTypes.find(function (x) { return x && x.code === currentEntityType })
    if (et && Array.isArray(et.fields)) return et.fields
    return []
  })()
  var canUseAdhoc = props.canUseAdhoc !== false
  var onSave = props.onSave

  var [nodes, setNodes] = useState(function () { return ensureMinimumNodes(props.initialNodes) })
  var [edges, setEdges] = useState(function () {
    var initNodes = ensureMinimumNodes(props.initialNodes)
    var nodeTypeById = {}
    initNodes.forEach(function (n) { nodeTypeById[n.id] = n.type })
    return (Array.isArray(props.initialEdges) ? props.initialEdges : []).map(function (e) {
      // 1) Backend'den gelen sourceHandle/targetHandle varsa direkt kullan
      //    (2026-06-15: ApprovalFlowEdge.SourceHandle/TargetHandle kolonlari).
      // 2) Yoksa edgeKind + source node tipinden deduce (geriye uyum, eski kayitlar).
      var srcType = nodeTypeById[e.source]
      var kind = e.data && e.data.edgeKind
      var sh = e.sourceHandle
      var th = e.targetHandle
      if (!sh && kind) {
        if (srcType === 'step') {
          if (kind === 'true')        sh = 'approve'
          else if (kind === 'false')  sh = 'reject'
          else if (kind === 'timeout') sh = 'timeout'
        } else if (srcType === 'decision') {
          if (kind === 'true')       sh = 'true'
          else if (kind === 'false') sh = 'false'
        }
      }
      var patch = {}
      if (sh) patch.sourceHandle = sh
      if (th) patch.targetHandle = th
      var withHandle = (sh || th) ? Object.assign({}, e, patch) : e
      return decorateEdge(withHandle)
    })
  })
  var [rules, setRules] = useState(function () {
    return Array.isArray(props.initialRules) ? props.initialRules.slice() : []
  })
  // 2026-06-14: Surec-scoped degisken tanimlari. VariablesPanel araciligiyla yonetilir,
  // SetVariable node + Decision kosul satirlarinda referans edilir.
  var [variables, setVariables] = useState(function () {
    return Array.isArray(props.initialVariables) ? props.initialVariables.slice() : []
  })
  var [variablesOpen, setVariablesOpen] = useState(false)
  var [selected, setSelected] = useState(null)
  // Alt tusu basili oldugunda tum handle'lar isConnectable=false olur — React Flow
  // connection mekanizmasi devre disi kalir, sadece DraggableHandle Alt+drag mode'u calisir.
  // Bu, istemsiz edge olusumunu (Alt+drag sirasinda yanlislikla baska handle uzerine
  // gelmek) kokunden engeller.
  var [altDown, setAltDown] = useState(false)
  useEffect(function () {
    function down(e) { if (e.altKey && !altDown) setAltDown(true) }
    function up(e)   { if (!e.altKey && altDown) setAltDown(false) }
    function blur()  { setAltDown(false) }
    window.addEventListener('keydown', down)
    window.addEventListener('keyup', up)
    window.addEventListener('blur', blur)
    return function () {
      window.removeEventListener('keydown', down)
      window.removeEventListener('keyup', up)
      window.removeEventListener('blur', blur)
    }
  }, [altDown])
  var [paletteOpen, setPaletteOpen] = useState(true)
  // 2026-05-25: Minimap toggle — varsayilan kapali. localStorage'dan tercih oku.
  var [showMinimap, setShowMinimap] = useState(function () {
    try { return localStorage.getItem(AFD_MINIMAP_KEY) === '1' } catch (_) { return false }
  })

  var reactFlowWrapper = useRef(null)
  var rf = useReactFlow()

  // Initial mount sonrasi fit garanti — `fitView` prop'u bazen CSS layout hazir
  // olmadan tetikleniyor ve zoom yanlis hesaplaniyor. 200ms gecikme ile rf.fitView()
  // cagrisi ekran tam yerlesince butun node'larin viewport'a oturmasini saglar.
  useEffect(function () {
    var t = setTimeout(function () {
      if (rf && typeof rf.fitView === 'function') {
        try { rf.fitView({ padding: 0.2, maxZoom: 1.2, duration: 0 }) } catch (_) {}
      }
    }, 200)
    return function () { clearTimeout(t) }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  /* ── Undo/Redo history ────────────────────────────────────────────────────
     Anlamlı sınırlarda (drop/connect/delete/drag-stop/prop-change) snapshot
     basıyoruz. Sürükleme sırasında pikselik snapshot atmıyor (sadece drag stop).
     Prop değişiklikleri 600ms debounce — kullanıcı tip eder, durunca tek snapshot.
     Ctrl+Z / Ctrl+Y veya toolbar butonları. Max 50 snapshot tutulur (memory cap).
  */
  var nodesRef = useRef(nodes)
  var edgesRef = useRef(edges)
  useEffect(function () { nodesRef.current = nodes }, [nodes])
  useEffect(function () { edgesRef.current = edges }, [edges])

  var historyRef = useRef({ stack: [], index: -1, applying: false })
  var [historyVer, setHistoryVer] = useState(0)
  var propDebounceRef = useRef(null)

  var pushSnapshot = useCallback(function () {
    if (historyRef.current.applying) return
    var h = historyRef.current
    var snap = {
      nodes: JSON.parse(JSON.stringify(nodesRef.current)),
      edges: JSON.parse(JSON.stringify(edgesRef.current)),
    }
    h.stack = h.stack.slice(0, h.index + 1)
    h.stack.push(snap)
    if (h.stack.length > 50) h.stack = h.stack.slice(-50)
    h.index = h.stack.length - 1
    setHistoryVer(function (v) { return v + 1 })
  }, [])

  // schedulePush — setNodes/setEdges sonrası state commit'in tamamlanmasını bekler.
  var schedulePush = useCallback(function () {
    setTimeout(pushSnapshot, 0)
  }, [pushSnapshot])

  // Custom node component'lerinin (StepNode handle drag vb.) node.data'yi parent
  // controlled state'i uzerinden guncelleyebilmesi icin. setNodes + schedulePush.
  var updateNodeData = useCallback(function (nodeId, patch) {
    setNodes(function (ns) {
      return ns.map(function (n) {
        if (n.id !== nodeId) return n
        return Object.assign({}, n, { data: Object.assign({}, n.data, patch) })
      })
    })
    schedulePush()
  }, [schedulePush])

  var scheduleDebouncedPush = useCallback(function () {
    if (propDebounceRef.current) clearTimeout(propDebounceRef.current)
    propDebounceRef.current = setTimeout(function () {
      propDebounceRef.current = null
      pushSnapshot()
    }, 600)
  }, [pushSnapshot])

  var loadSnapshot = useCallback(function (snap) {
    if (!snap) return
    var h = historyRef.current
    h.applying = true
    // Edge'ler decorateEdge çıktısı olarak saklandı (style/markerEnd ile) → restore'da da
    // doğrudan koruyabiliriz; emin olmak için decorateEdge tekrar uygula (idempotent).
    setNodes(snap.nodes)
    setEdges(snap.edges.map(function (e) {
      return e.markerEnd ? e : decorateEdge(e)
    }))
    setSelected(null)
    // applying flag'i microtask ardından sıfırla — React commit + sonraki schedulePush etkisinden korunmak için
    setTimeout(function () { h.applying = false }, 60)
  }, [])

  var undo = useCallback(function () {
    var h = historyRef.current
    if (h.index <= 0) return
    h.index--
    loadSnapshot(h.stack[h.index])
    setHistoryVer(function (v) { return v + 1 })
  }, [loadSnapshot])

  var redo = useCallback(function () {
    var h = historyRef.current
    if (h.index >= h.stack.length - 1) return
    h.index++
    loadSnapshot(h.stack[h.index])
    setHistoryVer(function (v) { return v + 1 })
  }, [loadSnapshot])

  // Initial snapshot — mount'ta bir kere
  useEffect(function () {
    if (historyRef.current.stack.length === 0) pushSnapshot()
  }, [pushSnapshot])

  // Keyboard shortcuts — Ctrl+Z undo, Ctrl+Shift+Z / Ctrl+Y redo. Input/textarea
  // odaktayken devre dışı (kullanıcı yazıyor olabilir).
  useEffect(function () {
    function onKey(e) {
      var tgt = e.target
      var tag = (tgt && tgt.tagName) || ''
      if (tag === 'INPUT' || tag === 'TEXTAREA' || (tgt && tgt.isContentEditable)) return
      var ctrl = e.ctrlKey || e.metaKey
      if (!ctrl) return
      var k = (e.key || '').toLowerCase()
      if (k === 'z' && !e.shiftKey) { e.preventDefault(); undo() }
      else if ((k === 'z' && e.shiftKey) || k === 'y') { e.preventDefault(); redo() }
    }
    window.addEventListener('keydown', onKey)
    return function () { window.removeEventListener('keydown', onKey) }
  }, [undo, redo])

  var canUndo = historyRef.current.index > 0
  var canRedo = historyRef.current.index < historyRef.current.stack.length - 1
  // historyVer referansını okuyalım ki linter unused state demesin + canUndo/canRedo
  // re-compute olsun (historyVer değiştiğinde component re-render).
  void historyVer

  /* ── Build payload (transform nodes/edges to server format) ── */
  var buildPayload = useCallback(function () {
    var payloadNodes = nodes.map(function (n, i) {
      var d = n.data || {}
      return {
        clientId: n.id,
        nodeType: n.type,                                        // start / step / decision / end
        posX: Math.round(n.position.x),
        posY: Math.round(n.position.y),
        stepName: d.stepName || null,
        approverType: d.approverType || null,
        approverId: d.approverId == null ? null : String(d.approverId),
        approverLabel: d.approverLabel || null,
        nodeDataJson: JSON.stringify(d),
        sortOrder: i,
      }
    })
    var payloadEdges = edges.map(function (e, i) {
      var d = e.data || {}
      return {
        sourceClientId: e.source,
        targetClientId: e.target,
        label: (e.label || d.label || null) || null,
        edgeKind: d.edgeKind || 'default',
        condition: d.condition || null,
        sortOrder: i,
        sourceHandle: e.sourceHandle || null,
        targetHandle: e.targetHandle || null,
      }
    })
    return { nodes: payloadNodes, edges: payloadEdges, rules: rules, variables: variables }
  }, [nodes, edges, rules, variables])

  /* onSave callback'i her değişiklik sonrası tetiklenir (parent state'ini taze tutar) */
  useEffect(function () {
    if (typeof onSave === 'function') onSave(buildPayload())
    // Razor sayfası tarafından doSave() içinde sync çağırılabilsin diye:
    window._afDesignerGetPayload = buildPayload
    return function () {
      if (window._afDesignerGetPayload === buildPayload) delete window._afDesignerGetPayload
    }
  }, [buildPayload, onSave])

  /* Toolbar harici butonlar için imperative handle'lar */
  useEffect(function () {
    window._afdToggleMinimap = function () {
      setShowMinimap(function (v) {
        var next = !v
        try { localStorage.setItem(AFD_MINIMAP_KEY, next ? '1' : '0') } catch (_) {}
        return next
      })
    }
    window._afdOpenVariables = function () { setVariablesOpen(true) }
    return function () {
      delete window._afdToggleMinimap
      delete window._afdOpenVariables
    }
  }, [])

  /* ── React Flow change handlers ──
     Delete/Backspace tuşu doğrudan React Flow native remove change'i dispatch eder
     (bizim handleDelete'i bypass eder). Burada 'remove' tipini yakalayıp snapshot
     push ediyoruz, böylece undo çalışır. */
  var onNodesChange = useCallback(function (changes) {
    setNodes(function (ns) { return applyNodeChanges(changes, ns) })
    var hasStructural = changes.some(function (c) { return c.type === 'remove' || c.type === 'add' })
    if (hasStructural) schedulePush()
  }, [schedulePush])
  var onEdgesChange = useCallback(function (changes) {
    setEdges(function (es) { return applyEdgeChanges(changes, es) })
    var hasStructural = changes.some(function (c) { return c.type === 'remove' || c.type === 'add' })
    if (hasStructural) schedulePush()
  }, [schedulePush])
  var onConnect = useCallback(function (params) {
    // Kaynak handle'a göre edge kind belirle (Decision/Step için approve/true vs reject/false)
    var kind = 'default'; var label = ''
    if (params.sourceHandle === 'approve' || params.sourceHandle === 'true') {
      kind = 'true'; label = params.sourceHandle === 'true' ? 'Evet' : 'Onay'
    } else if (params.sourceHandle === 'reject' || params.sourceHandle === 'false') {
      kind = 'false'; label = params.sourceHandle === 'false' ? 'Hayır' : 'Red'
    } else if (params.sourceHandle === 'timeout') {
      kind = 'timeout'; label = 'Gecikme'
    }
    setEdges(function (es) {
      var next = addEdge(Object.assign({}, params, {
        id: 'e_' + Date.now().toString(36),
        label: label,
        data: { edgeKind: kind, label: label, condition: null },
      }), es)
      return next.map(function (e) { return e.markerEnd ? e : decorateEdge(e) })
    })
    schedulePush()
  }, [schedulePush])

  /* ── Drag-drop palette item to canvas ── */
  var onDragOver = useCallback(function (ev) {
    ev.preventDefault()
    ev.dataTransfer.dropEffect = 'move'
  }, [])

  var onDrop = useCallback(function (ev) {
    ev.preventDefault()
    var type = ev.dataTransfer.getData('application/reactflow')
    if (!type) return
    var bounds = reactFlowWrapper.current.getBoundingClientRect()
    var position = rf.screenToFlowPosition({
      x: ev.clientX - bounds.left,
      y: ev.clientY - bounds.top,
    })
    var newNode = {
      id: genId(),
      type: type,
      position: position,
      data: defaultDataForType(type),
    }
    setNodes(function (ns) { return ns.concat([newNode]) })
    setSelected({ kind: 'node', id: newNode.id, type: type, data: newNode.data })
    schedulePush()
  }, [rf, schedulePush])

  /* ── Selection ── */
  function onNodeClick(_evt, node) {
    setSelected({ kind: 'node', id: node.id, type: node.type, data: node.data })
  }
  function onEdgeClick(_evt, edge) {
    setSelected({ kind: 'edge', id: edge.id, type: 'edge', data: edge.data || {}, label: edge.label || '' })
  }
  function onPaneClick() { setSelected(null) }

  /* ── Properties update ── */
  function handlePropChange(patch) {
    if (!selected) return
    scheduleDebouncedPush()
    if (selected.kind === 'node') {
      setNodes(function (ns) {
        return ns.map(function (n) {
          if (n.id !== selected.id) return n
          var newData = Object.assign({}, n.data, patch)
          return Object.assign({}, n, { data: newData })
        })
      })
      // selected state'i guncel data ile freshle (re-render için)
      setSelected(function (s) {
        if (!s) return s
        return Object.assign({}, s, { data: Object.assign({}, s.data, patch) })
      })
    } else if (selected.kind === 'edge') {
      setEdges(function (es) {
        return es.map(function (e) {
          if (e.id !== selected.id) return e
          var newData = Object.assign({}, e.data || {}, patch)
          var newLabel = patch.label != null ? patch.label : e.label
          var kind = newData.edgeKind || 'default'
          return Object.assign({}, e, {
            data: newData,
            label: newLabel || '',
            style: edgeStyleForKind(kind),
            markerEnd: { type: MarkerType.ArrowClosed, color: edgeStyleForKind(kind).stroke },
          })
        })
      })
      setSelected(function (s) {
        if (!s) return s
        return Object.assign({}, s,
          { data: Object.assign({}, s.data, patch),
            label: patch.label != null ? patch.label : s.label })
      })
    }
  }

  function handleDelete() {
    if (!selected) return
    if (selected.kind === 'node') {
      // Start düğümü silinemez (tek Start kuralı)
      var node = nodes.find(function (n) { return n.id === selected.id })
      if (node && node.type === 'start') return
      setNodes(function (ns) { return ns.filter(function (n) { return n.id !== selected.id }) })
      setEdges(function (es) {
        return es.filter(function (e) {
          return e.source !== selected.id && e.target !== selected.id
        })
      })
    } else if (selected.kind === 'edge') {
      setEdges(function (es) { return es.filter(function (e) { return e.id !== selected.id }) })
    }
    setSelected(null)
    schedulePush()
  }

  /* Node sürüklemesi bittiğinde snapshot — sürükleme sırasında her piksel için
     snapshot atmıyoruz, sadece "kullanıcı bıraktı" anında. */
  var onNodeDragStop = useCallback(function () { schedulePush() }, [schedulePush])

  /* ── Render ── */
  // selected node'un en taze data'sını al (props panel re-bind için)
  var selectedView = useMemo(function () {
    if (!selected) return null
    if (selected.kind === 'node') {
      var n = nodes.find(function (x) { return x.id === selected.id })
      if (!n) return null
      return { kind: 'node', id: n.id, type: n.type, data: n.data }
    }
    if (selected.kind === 'edge') {
      var e = edges.find(function (x) { return x.id === selected.id })
      if (!e) return null
      return { kind: 'edge', id: e.id, type: 'edge', data: e.data || {}, label: e.label || '',
               source: e.source, target: e.target, sourceHandle: e.sourceHandle }
    }
    return null
  }, [selected, nodes, edges])

  return (
    <NodeDataCtx.Provider value={updateNodeData}>
    <AltKeyCtx.Provider value={altDown}>
    <div className="afd-root" ref={reactFlowWrapper}>
      {paletteOpen && <NodePalette />}

      <div className="afd-canvas" onDrop={onDrop} onDragOver={onDragOver}>
        <ReactFlow
          nodes={nodes}
          edges={edges}
          onNodesChange={onNodesChange}
          onEdgesChange={onEdgesChange}
          onConnect={onConnect}
          /* Self-loop engeli — aynı node'un kendi handle'ları arasında edge çekilemez. */
          isValidConnection={function (c) { return c && c.source && c.target && c.source !== c.target }}
          onNodeClick={onNodeClick}
          onEdgeClick={onEdgeClick}
          onPaneClick={onPaneClick}
          onNodeDragStop={onNodeDragStop}
          nodeTypes={nodeTypes}
          fitView
          fitViewOptions={{ padding: 0.2, maxZoom: 1.2 }}
          deleteKeyCode={['Delete', 'Backspace']}
          minZoom={0.2}
          maxZoom={2}
          /* 2026-05-25 (UX): handle bağlantısını kolaylaştır —
             connectionRadius: handle'a yakın bir noktada bırakıldığında otomatik snap.
             connectOnClick: handle'a tıkla → 2. handle'a tıkla ile bağlantı (drag gerekmiyor).
             connectionMode='loose': source/target handle ayrımı esnek (yön bağımlılığı zayıf). */
          connectionRadius={48}
          connectOnClick
          connectionMode="loose"
          defaultEdgeOptions={{
            markerEnd: { type: MarkerType.ArrowClosed, color: '#94a3b8' },
            style: { stroke: '#94a3b8', strokeWidth: 1.6 },
          }}
        >
          <Background gap={18} size={1} />
          <Controls showInteractive={false} />
          {/* 2026-05-25: MiniMap parametrik — varsayilan kapali, kullanici toggle ile acar.
              Tercih localStorage'da saklanir, bir sonraki acilista korunur. */}
          <Panel position="top-right" style={{ display: 'flex', gap: 6 }}>
            {/* Undo / Redo — disabled state stack durumuna göre */}
            <button
              type="button"
              title="Geri al (Ctrl+Z)"
              onClick={undo}
              disabled={!canUndo}
              style={{
                padding: '6px 9px', borderRadius: 7,
                background: 'var(--afd-surface, #fff)',
                border: '1px solid var(--afd-border, #cbd5e1)',
                color: canUndo ? 'var(--afd-text, #0f172a)' : 'var(--afd-muted, #94a3b8)',
                cursor: canUndo ? 'pointer' : 'not-allowed',
                opacity: canUndo ? 1 : 0.5,
                display: 'inline-flex', alignItems: 'center', gap: 5,
                fontSize: 11, fontWeight: 600, boxShadow: '0 2px 6px rgba(0,0,0,.08)',
              }}>
              <Undo2 size={13} /> Geri Al
            </button>
            <button
              type="button"
              title="İleri al (Ctrl+Y veya Ctrl+Shift+Z)"
              onClick={redo}
              disabled={!canRedo}
              style={{
                padding: '6px 9px', borderRadius: 7,
                background: 'var(--afd-surface, #fff)',
                border: '1px solid var(--afd-border, #cbd5e1)',
                color: canRedo ? 'var(--afd-text, #0f172a)' : 'var(--afd-muted, #94a3b8)',
                cursor: canRedo ? 'pointer' : 'not-allowed',
                opacity: canRedo ? 1 : 0.5,
                display: 'inline-flex', alignItems: 'center', gap: 5,
                fontSize: 11, fontWeight: 600, boxShadow: '0 2px 6px rgba(0,0,0,.08)',
              }}>
              <Redo2 size={13} /> İleri Al
            </button>
          </Panel>
          {showMinimap && <MiniMap pannable zoomable />}
          {nodes.length === 0 && (
            <Panel position="top-center">
              <div className="afd-empty-hint">
                <div>Soldaki paletten bir düğüm sürükleyerek başlayın.</div>
              </div>
            </Panel>
          )}
        </ReactFlow>
      </div>

      <NodePropertiesPanel
        selected={selectedView}
        edges={edges}
        nodes={nodes}
        users={users}
        departments={departments}
        cariGroups={cariGroups}
        materialGroups={materialGroups}
        sqlQueries={sqlQueries}
        integrations={integrations}
        entityTypeFields={entityTypeFields}
        entityTypeCode={currentEntityType}
        canUseAdhoc={canUseAdhoc}
        rules={rules}
        onRulesChange={function (newRules) { setRules(newRules) }}
        variables={variables}
        onChange={handlePropChange}
        onDelete={handleDelete}
      />

      <VariablesPanel
        open={variablesOpen}
        variables={variables}
        onChange={function (next) { setVariables(next); schedulePush() }}
        onClose={function () { setVariablesOpen(false) }}
      />
    </div>
    </AltKeyCtx.Provider>
    </NodeDataCtx.Provider>
  )
}

/* ──────────────────────────────────────────────────────────────
   Public component — useReactFlow gerektiren child için Provider sarar.
   ────────────────────────────────────────────────────────────── */
export default function ApprovalFlowDesigner(props) {
  return (
    <ReactFlowProvider>
      <DesignerInner {...props} />
    </ReactFlowProvider>
  )
}
