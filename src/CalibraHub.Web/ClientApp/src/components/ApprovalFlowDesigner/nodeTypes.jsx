/**
 * ApprovalFlowDesigner — custom node renderers for React Flow.
 *
 *   StartNode    — yeşil yuvarlak "Başla"
 *   StepNode     — mavi rounded rect, ad + onaylayıcı özeti, alt handle "Onay"/"Red"
 *   DecisionNode — sarı elmas (rotate 45), koşul ifadesi gösterir, iki output (Evet/Hayır)
 *   EndNode      — kırmızı yuvarlak "Bitir"
 */
import React, { useCallback, useEffect } from 'react'
import { Handle, Position, useUpdateNodeInternals } from '@xyflow/react'
import { Play, Square, GitBranch, CheckSquare, Bell, GitMerge, Zap, Variable } from 'lucide-react'
import DraggableHandle from './DraggableHandle.jsx'
import { useUpdateNodeData } from './nodeDataContext.js'

/**
 * ExtraInputHandles — kullanici sag panelden "Ek Kol" ekledikce her satir icin
 * GORUNUR target handle render eder. Yeni model: data.extraInputs bir DIZI:
 *   [ { id: 'x12345', side: 'right'|'left'|'bottom', offset: 0..1 }, ... ]
 *
 * Geriye uyumluluk: data.extraInputs eski object format ({right:true, ...})
 * geldiyse runtime'da array'e cevrilir (id 'x'+side, offset 0.5).
 *
 * Handle id: 'in-' + item.id (uniq). Alt+drag ile kenar/offset commit edilir.
 * excludeSides param'i artik tasiyici degil (kullanici hangi kenari secerse
 * onun gorunmesi gerekiyor) ama EndNode gibi top'u zaten kullanan node'lar
 * icin top'a ek input eklenmemeli — bu durumda 'top' filtrelenir.
 */
function normalizeExtraInputs(raw) {
  if (Array.isArray(raw)) {
    return raw
      .filter(function (it) { return it && typeof it.side === 'string' })
      .map(function (it) {
        var kind = (it.kind === 'out') ? 'out' : 'in'
        return { id: it.id, side: it.side, offset: it.offset, kind: kind, label: it.label }
      })
  }
  if (raw && typeof raw === 'object') {
    var out = []
    ;['right', 'bottom', 'left'].forEach(function (s) {
      if (raw[s] === true) out.push({ id: 'x' + s, side: s, offset: 0.5, kind: 'in', label: undefined })
    })
    return out
  }
  return []
}
function ExtraInputHandles({ nodeId, data, excludeSides }) {
  excludeSides = excludeSides || []
  var items = normalizeExtraInputs(data && data.extraInputs)
  var updateNodeData = useUpdateNodeData()
  var updateNodeInternals = useUpdateNodeInternals()
  // Items degisince (yeni handle eklendi/silindi/kind degisti) React Flow'a
  // node handle setini yeniden tarat — yoksa eski cache'li set'e gore connection
  // drop hedefi olarak taninmaz ve bagimsiz handle yeni id'sini kabul etmez.
  var signature = items.map(function (it) { return it.id + ':' + it.kind + ':' + it.side }).join('|')
  useEffect(function () {
    if (nodeId && typeof updateNodeInternals === 'function') {
      updateNodeInternals(nodeId)
    }
  }, [nodeId, signature, updateNodeInternals])
  function commit(itemId, side, offset) {
    if (typeof updateNodeData !== 'function') return
    var next = items.map(function (it) {
      return it.id === itemId ? Object.assign({}, it, { side: side, offset: offset }) : it
    })
    updateNodeData(nodeId, { extraInputs: next })
  }
  return items
    .filter(function (it) { return excludeSides.indexOf(it.side) === -1 })
    .map(function (it) {
      var isOut = it.kind === 'out'
      var renderId = (isOut ? 'out-' : 'in-') + it.id
      return React.createElement(DraggableHandle, {
        key: 'extra-' + renderId,
        id: renderId,
        type: isOut ? 'source' : 'target',
        nodeId: nodeId,
        defaultSide: it.side,
        defaultOffset: typeof it.offset === 'number' ? it.offset : 0.5,
        pos: { side: it.side, offset: typeof it.offset === 'number' ? it.offset : 0.5 },
        siblings: [],
        className: isOut ? 'afd-handle--extra afd-handle--extra-out' : 'afd-handle--extra',
        onPositionChange: function (s, o) { commit(it.id, s, o) },
      })
    })
}

/**
 * useNodeHandles — node'un handle pozisyonlarini data.handlePos uzerinden cozer
 * ve commit callback'i verir. defaults: { handleKey: { side, offset } }.
 *
 * Donus: { resolved: { handleKey: {side,offset} }, commit(key, side, offset),
 *          siblings(selfKey) → [{id, side, offset}] }
 */
function useNodeHandles(nodeId, data, defaults) {
  var updateNodeData = useUpdateNodeData()
  var hp = (data && data.handlePos) || {}
  var resolved = {}
  Object.keys(defaults).forEach(function (k) {
    resolved[k] = hp[k] || defaults[k]
  })
  var commit = useCallback(function (key, side, offset) {
    if (typeof updateNodeData !== 'function') return
    var prev = (data && data.handlePos) || {}
    var next = Object.assign({}, prev)
    next[key] = { side: side, offset: offset }
    updateNodeData(nodeId, { handlePos: next })
  }, [nodeId, data, updateNodeData])
  var siblings = function (selfKey) {
    var list = []
    Object.keys(resolved).forEach(function (k) {
      if (k === selfKey) return
      list.push({ id: k, side: resolved[k].side, offset: resolved[k].offset })
    })
    return list
  }
  return { resolved: resolved, commit: commit, siblings: siblings }
}

/* ──────────────────────────────────────────────────────────────
   StartNode — sadece bir bottom handle (source).
   ────────────────────────────────────────────────────────────── */
var START_DEFAULTS = { out: { side: 'bottom', offset: 0.5 } }
export function StartNode({ id, data, selected }) {
  var h = useNodeHandles(id, data, START_DEFAULTS)
  return (
    <div className={'afd-node afd-node--start' + (selected ? ' is-selected' : '')}>
      <Play size={14} strokeWidth={2.5} />
      <span>Başla</span>
      <DraggableHandle id="out" type="source" nodeId={id}
        defaultSide={START_DEFAULTS.out.side} defaultOffset={START_DEFAULTS.out.offset}
        pos={h.resolved.out} siblings={[]}
        onPositionChange={function (s, o) { h.commit('out', s, o) }} />
    </div>
  )
}

/* ──────────────────────────────────────────────────────────────
   StepNode — adım adı + onaylayıcı özeti.
   Top: target handle (in). Bottom: 3 source handle (approve/reject/timeout).
   Handle pozisyonları data.handlePos'tan okunur (yoksa default dağılım).
   Kullanıcı Alt+drag ile handle'ları node kenarı boyunca taşıyabilir.
   ────────────────────────────────────────────────────────────── */
var STEP_DEFAULT_HANDLES = {
  in:      { side: 'top',    offset: 0.5 },
  approve: { side: 'bottom', offset: 0.22 },
  reject:  { side: 'bottom', offset: 0.50 },
  timeout: { side: 'bottom', offset: 0.78 },
}

export function StepNode({ id, data, selected }) {
  var approverHint =
    data.approverType === 'SpecificUser' ? (data.approverLabel || 'Kullanıcı seçilmedi')
      : data.approverType === 'Department' ? (data.approverLabel || 'Departman seçilmedi')
      : data.approverType === 'ManagerOfRequester' ? 'Kişinin Amiri'
      : 'Herhangi Kullanıcı'

  var h = useNodeHandles(id, data, STEP_DEFAULT_HANDLES)
  var posIn = h.resolved.in, posApprove = h.resolved.approve, posReject = h.resolved.reject, posTimeout = h.resolved.timeout
  // Source handle'lar arasi collision (in target handle'i hariç tutulur)
  var sourceSiblings = function (selfKey) {
    return h.siblings(selfKey).filter(function (s) { return s.id !== 'in' })
  }

  return (
    <div className={'afd-node afd-node--step' + (selected ? ' is-selected' : '')}>
      <DraggableHandle
        id="in" type="target" nodeId={id}
        defaultSide={STEP_DEFAULT_HANDLES.in.side} defaultOffset={STEP_DEFAULT_HANDLES.in.offset}
        pos={posIn}
        siblings={[]}
        onPositionChange={function (side, offset) { h.commit('in', side, offset) }}
      />
      <div className="afd-node__head">
        <CheckSquare size={13} strokeWidth={2.4} />
        <span className="afd-node__title">{data.stepName || 'Adım'}</span>
      </div>
      <div className="afd-node__meta">{approverHint}</div>
      <DraggableHandle
        id="approve" type="source" nodeId={id}
        defaultSide={STEP_DEFAULT_HANDLES.approve.side} defaultOffset={STEP_DEFAULT_HANDLES.approve.offset}
        pos={posApprove}
        siblings={sourceSiblings('approve')}
        className="afd-handle--approve"
        label="Onay" labelClassName="afd-foot--ok"
        onPositionChange={function (side, offset) { h.commit('approve', side, offset) }}
      />
      <DraggableHandle
        id="reject" type="source" nodeId={id}
        defaultSide={STEP_DEFAULT_HANDLES.reject.side} defaultOffset={STEP_DEFAULT_HANDLES.reject.offset}
        pos={posReject}
        siblings={sourceSiblings('reject')}
        className="afd-handle--reject"
        label="Red" labelClassName="afd-foot--no"
        onPositionChange={function (side, offset) { h.commit('reject', side, offset) }}
      />
      <DraggableHandle
        id="timeout" type="source" nodeId={id}
        defaultSide={STEP_DEFAULT_HANDLES.timeout.side} defaultOffset={STEP_DEFAULT_HANDLES.timeout.offset}
        pos={posTimeout}
        siblings={sourceSiblings('timeout')}
        className="afd-handle--timeout"
        label="Gecikme" labelClassName="afd-foot--tm"
        onPositionChange={function (side, offset) { h.commit('timeout', side, offset) }}
      />
      <ExtraInputHandles nodeId={id} data={data} />
    </div>
  )
}

/* ──────────────────────────────────────────────────────────────
   DecisionNode — koşul ifadesi (örn. amount > 100000).
   Top handle (target), bottom-left "Evet" + bottom-right "Hayır" (source).
   ────────────────────────────────────────────────────────────── */
var DECISION_DEFAULTS = {
  in:   { side: 'top',    offset: 0.5 },
  true: { side: 'bottom', offset: 0.30 },
  false:{ side: 'bottom', offset: 0.70 },
}
export function DecisionNode({ id, data, selected }) {
  var h = useNodeHandles(id, data, DECISION_DEFAULTS)
  var srcSib = function (selfKey) { return h.siblings(selfKey).filter(function (s) { return s.id !== 'in' }) }
  return (
    <div className={'afd-node afd-node--decision' + (selected ? ' is-selected' : '')}>
      <DraggableHandle id="in" type="target" nodeId={id}
        defaultSide={DECISION_DEFAULTS.in.side} defaultOffset={DECISION_DEFAULTS.in.offset}
        pos={h.resolved.in} siblings={[]}
        onPositionChange={function (s, o) { h.commit('in', s, o) }} />
      <div className="afd-node__head">
        <GitBranch size={13} strokeWidth={2.4} />
        <span className="afd-node__title">Karar</span>
      </div>
      <div className="afd-node__cond" title={data.condition || ''}>
        {data.condition ? data.condition : <em>koşul tanımlanmadı</em>}
      </div>
      <DraggableHandle id="true" type="source" nodeId={id}
        defaultSide={DECISION_DEFAULTS.true.side} defaultOffset={DECISION_DEFAULTS.true.offset}
        pos={h.resolved.true} siblings={srcSib('true')}
        className="afd-handle--approve"
        label="Evet" labelClassName="afd-foot--ok"
        onPositionChange={function (s, o) { h.commit('true', s, o) }} />
      <DraggableHandle id="false" type="source" nodeId={id}
        defaultSide={DECISION_DEFAULTS.false.side} defaultOffset={DECISION_DEFAULTS.false.offset}
        pos={h.resolved.false} siblings={srcSib('false')}
        className="afd-handle--reject"
        label="Hayır" labelClassName="afd-foot--no"
        onPositionChange={function (s, o) { h.commit('false', s, o) }} />
      <ExtraInputHandles nodeId={id} data={data} />
    </div>
  )
}

/* ──────────────────────────────────────────────────────────────
   ParallelNode — paralel kapı (split & join). Top handle (target) + Bottom (source).
   Tek handle'dan çoklu edge çizilebilir (React Flow native). Birden çok input gelirse
   Join, tek input + çoklu output ise Split modu — runtime executor topolojiye bakarak karar verir.
   ────────────────────────────────────────────────────────────── */
var PARALLEL_DEFAULTS = { in: { side:'top', offset:0.5 }, out: { side:'bottom', offset:0.5 } }
export function ParallelNode({ id, data, selected }) {
  var h = useNodeHandles(id, data, PARALLEL_DEFAULTS)
  return (
    <div className={'afd-node afd-node--parallel' + (selected ? ' is-selected' : '')}>
      <DraggableHandle id="in" type="target" nodeId={id}
        defaultSide={PARALLEL_DEFAULTS.in.side} defaultOffset={PARALLEL_DEFAULTS.in.offset}
        pos={h.resolved.in} siblings={[]}
        onPositionChange={function (s, o) { h.commit('in', s, o) }} />
      <div className="afd-node__head">
        <GitMerge size={13} strokeWidth={2.4} />
        <span className="afd-node__title">Paralel</span>
      </div>
      <div className="afd-node__cond">
        {data.modeHint || 'Çoklu dal: hepsi tamamlanınca devam'}
      </div>
      <DraggableHandle id="out" type="source" nodeId={id}
        defaultSide={PARALLEL_DEFAULTS.out.side} defaultOffset={PARALLEL_DEFAULTS.out.offset}
        pos={h.resolved.out} siblings={[]}
        onPositionChange={function (s, o) { h.commit('out', s, o) }} />
      <ExtraInputHandles nodeId={id} data={data} />
    </div>
  )
}

/* ──────────────────────────────────────────────────────────────
   NotificationNode — mail/whatsapp bildirim. Top handle (target) + bottom (source).
   Akışı durdurmaz, fire-and-forget: bildirim gönderilir, sonraki node'a otomatik geçer.
   ────────────────────────────────────────────────────────────── */
var NOTIF_DEFAULTS = { in: { side:'top', offset:0.5 }, out: { side:'bottom', offset:0.5 } }
export function NotificationNode({ id, data, selected }) {
  var h = useNodeHandles(id, data, NOTIF_DEFAULTS)
  var typeLabel = data.notificationType === 'whatsapp' ? 'WhatsApp'
                : data.notificationType === 'both' ? 'Mail + WhatsApp'
                : 'Mail'
  var hint = data.recipientLabel || data.subject || 'Bildirim ayarlanmadı'
  return (
    <div className={'afd-node afd-node--notification' + (selected ? ' is-selected' : '')}>
      <DraggableHandle id="in" type="target" nodeId={id}
        defaultSide={NOTIF_DEFAULTS.in.side} defaultOffset={NOTIF_DEFAULTS.in.offset}
        pos={h.resolved.in} siblings={[]}
        onPositionChange={function (s, o) { h.commit('in', s, o) }} />
      <div className="afd-node__head">
        <Bell size={13} strokeWidth={2.4} />
        <span className="afd-node__title">{typeLabel}</span>
      </div>
      <div className="afd-node__meta" title={hint}>{hint}</div>
      <DraggableHandle id="out" type="source" nodeId={id}
        defaultSide={NOTIF_DEFAULTS.out.side} defaultOffset={NOTIF_DEFAULTS.out.offset}
        pos={h.resolved.out} siblings={[]}
        onPositionChange={function (s, o) { h.commit('out', s, o) }} />
      <ExtraInputHandles nodeId={id} data={data} />
    </div>
  )
}

/* ──────────────────────────────────────────────────────────────
   IntegrationNode — onay sürecinin bir noktasında mevcut bir entegrasyonu
   tetikler (örn. satış siparişi onaylandıktan sonra ERP'ye aktar). Top
   handle (target) + bottom (source). Akış akmaya devam eder — entegrasyon
   sonucu loglanır; haltOnError işaretli ise hata akışı durdurur.
   ────────────────────────────────────────────────────────────── */
var INTEG_DEFAULTS = { in: { side:'top', offset:0.5 }, out: { side:'bottom', offset:0.5 } }
export function IntegrationNode({ id, data, selected }) {
  var h = useNodeHandles(id, data, INTEG_DEFAULTS)
  var label = data.integrationName || 'Entegrasyon seçilmedi'
  var halt  = data.haltOnError === true
  return (
    <div className={'afd-node afd-node--integration' + (selected ? ' is-selected' : '')}>
      <DraggableHandle id="in" type="target" nodeId={id}
        defaultSide={INTEG_DEFAULTS.in.side} defaultOffset={INTEG_DEFAULTS.in.offset}
        pos={h.resolved.in} siblings={[]}
        onPositionChange={function (s, o) { h.commit('in', s, o) }} />
      <div className="afd-node__head">
        <Zap size={13} strokeWidth={2.4} />
        <span className="afd-node__title">Entegrasyon</span>
      </div>
      <div className="afd-node__meta" title={label}>
        {label}{halt ? ' · hata durdur' : ''}
      </div>
      <DraggableHandle id="out" type="source" nodeId={id}
        defaultSide={INTEG_DEFAULTS.out.side} defaultOffset={INTEG_DEFAULTS.out.offset}
        pos={h.resolved.out} siblings={[]}
        onPositionChange={function (s, o) { h.commit('out', s, o) }} />
      <ExtraInputHandles nodeId={id} data={data} />
    </div>
  )
}

/* ──────────────────────────────────────────────────────────────
   SetVariableNode — surec degiskenini atayan / guncelleyen node.
   Akisi durdurmaz: variable = expression, sonraki node'a gecer.
   ────────────────────────────────────────────────────────────── */
var SETVAR_DEFAULTS = { in: { side:'top', offset:0.5 }, out: { side:'bottom', offset:0.5 } }
export function SetVariableNode({ id, data, selected }) {
  var h = useNodeHandles(id, data, SETVAR_DEFAULTS)
  var name = data.variableName || '?'
  var expr = (typeof data.expression === 'string' && data.expression.length > 0)
    ? data.expression
    : '— ifade tanımlanmadı —'
  return (
    <div className={'afd-node afd-node--setvar' + (selected ? ' is-selected' : '')}>
      <DraggableHandle id="in" type="target" nodeId={id}
        defaultSide={SETVAR_DEFAULTS.in.side} defaultOffset={SETVAR_DEFAULTS.in.offset}
        pos={h.resolved.in} siblings={[]}
        onPositionChange={function (s, o) { h.commit('in', s, o) }} />
      <div className="afd-node__head">
        <Variable size={13} strokeWidth={2.4} />
        <span className="afd-node__title">Değişken Ata</span>
      </div>
      <div className="afd-node__meta" style={{ fontFamily: 'ui-monospace, Menlo, Consolas, monospace' }}>
        {name} = {expr}
      </div>
      <DraggableHandle id="out" type="source" nodeId={id}
        defaultSide={SETVAR_DEFAULTS.out.side} defaultOffset={SETVAR_DEFAULTS.out.offset}
        pos={h.resolved.out} siblings={[]}
        onPositionChange={function (s, o) { h.commit('out', s, o) }} />
      <ExtraInputHandles nodeId={id} data={data} />
    </div>
  )
}

/* ──────────────────────────────────────────────────────────────
   EndNode — sadece top handle (target).
   ────────────────────────────────────────────────────────────── */
var END_DEFAULTS = { in: { side:'top', offset:0.5 } }
export function EndNode({ id, data, selected }) {
  var h = useNodeHandles(id, data, END_DEFAULTS)
  return (
    <div className={'afd-node afd-node--end' + (selected ? ' is-selected' : '')}>
      <DraggableHandle id="in" type="target" nodeId={id}
        defaultSide={END_DEFAULTS.in.side} defaultOffset={END_DEFAULTS.in.offset}
        pos={h.resolved.in} siblings={[]}
        onPositionChange={function (s, o) { h.commit('in', s, o) }} />
      <ExtraInputHandles nodeId={id} data={data} />
      <Square size={14} strokeWidth={2.5} />
      <span>Bitir</span>
    </div>
  )
}

export var nodeTypes = {
  start: StartNode,
  step: StepNode,
  decision: DecisionNode,
  parallel: ParallelNode,
  notification: NotificationNode,
  integration: IntegrationNode,
  setVariable: SetVariableNode,
  end: EndNode,
}
