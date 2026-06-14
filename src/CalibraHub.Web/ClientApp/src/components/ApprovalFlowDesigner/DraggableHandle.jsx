/**
 * DraggableHandle — React Flow Handle wrapper, Alt+drag ile node kenari boyunca tasinabilir.
 *
 * Davranis:
 *   - Normal drag → React Flow default (edge connection) — dokunulmaz
 *   - Alt+drag    → custom hareket: pointer move sirasinda node bounding rect'e gore
 *                   en yakin kenar bulunur, offset normalize edilir (0-1). Pointer up'ta
 *                   onPositionChange(side, offset) cagrilir.
 *
 * Collision: ayni node'daki diger handle'larin pozisyonu props.siblings'ten alinir.
 *   Drag sirasinda ayni side'daki diger handle'la min mesafe (MIN_GAP) korunur — yakin
 *   geldiginde snap edilir, ustune binmesine izin verilmez.
 *
 * Props:
 *   id, type, position           — React Flow Handle props (default pozisyon icin)
 *   pos                          — { side: 'top'|'right'|'bottom'|'left', offset: 0..1 }
 *                                  (varsa pos kullanilir; yoksa position prop'undan eski yola dusulur)
 *   color                        — handle dot rengi (CSS)
 *   siblings                     — diger handle'larin pos listesi: [{ id, side, offset }]
 *   onPositionChange(side, off)  — drag sonu callback
 *   className, style             — ek style
 *
 * Not: Handle DOM'unu React Flow yonetir; biz parent <div> uzerinden ek pointer listener
 * baglariz. React Flow'un kendi connection drag'i altKey ile cakismaz cunku biz altKey
 * down'da event.stopPropagation() yapariz.
 */
import React, { useRef, useState, useEffect, useCallback } from 'react'
import { Handle, Position, useUpdateNodeInternals } from '@xyflow/react'
import { useAltKeyDown } from './nodeDataContext.js'

var SIDE_TO_POSITION = {
  top:    Position.Top,
  right:  Position.Right,
  bottom: Position.Bottom,
  left:   Position.Left,
}

// Aynı kenarda iki handle arasındaki minimum offset farkı (0-1 normalize).
// Node genişliği ~180px → 0.15 ≈ 27px. Handle dot'u 10px, halo ile 18px; 27px güvenli.
var MIN_GAP = 0.15
// Kenarlardan minimum içeri girinti — köşeye yapışmasın.
var EDGE_PAD = 0.08

function clamp(n, lo, hi) { return Math.max(lo, Math.min(hi, n)) }

// Pointer ekran koordinatından node-relative en yakın kenarı ve o kenardaki offset'i çöz.
function pointToSideOffset(node, clientX, clientY) {
  var r = node.getBoundingClientRect()
  if (r.width <= 0 || r.height <= 0) return null
  var x = clientX - r.left
  var y = clientY - r.top
  // En yakın kenara olan mesafe
  var dTop    = y
  var dBottom = r.height - y
  var dLeft   = x
  var dRight  = r.width - x
  var minD = Math.min(dTop, dBottom, dLeft, dRight)
  var side, offset
  if (minD === dTop)         { side = 'top';    offset = x / r.width }
  else if (minD === dBottom) { side = 'bottom'; offset = x / r.width }
  else if (minD === dLeft)   { side = 'left';   offset = y / r.height }
  else                       { side = 'right';  offset = y / r.height }
  return { side: side, offset: clamp(offset, EDGE_PAD, 1 - EDGE_PAD) }
}

// Aynı side'daki diğer handle'larla collision kontrolü. En yakın yasak bölgeden snap.
function resolveCollision(side, offset, siblings) {
  var same = (siblings || []).filter(function (s) { return s && s.side === side })
  if (same.length === 0) return offset
  // Her sibling için forbidden range [s.offset - MIN_GAP, s.offset + MIN_GAP]
  // Offset bu range içindeyse en yakın sınıra it.
  for (var i = 0; i < same.length; i++) {
    var sOff = same[i].offset
    if (offset > sOff - MIN_GAP && offset < sOff + MIN_GAP) {
      // En yakın izinli noktaya snap
      var lo = sOff - MIN_GAP
      var hi = sOff + MIN_GAP
      offset = (Math.abs(offset - lo) < Math.abs(offset - hi)) ? lo : hi
      offset = clamp(offset, EDGE_PAD, 1 - EDGE_PAD)
    }
  }
  return offset
}

// {side, offset} → React Flow Handle style: top/bottom için left%, left/right için top%.
function posToHandleStyle(side, offset) {
  var pct = (clamp(offset, 0, 1) * 100).toFixed(2) + '%'
  if (side === 'top' || side === 'bottom') return { left: pct }
  return { top: pct }
}

export default function DraggableHandle(props) {
  var id            = props.id
  var nodeId        = props.nodeId          // React Flow node id — updateNodeInternals icin
  var type          = props.type            // 'source' | 'target'
  var defaultSide   = props.defaultSide || 'bottom'
  var defaultOffset = typeof props.defaultOffset === 'number' ? props.defaultOffset : 0.5
  var pos           = props.pos             // { side, offset } | undefined
  var siblings      = props.siblings || []  // [{ id, side, offset }]
  var color         = props.color
  var className     = props.className || ''
  var onPositionChange = props.onPositionChange

  var side   = (pos && pos.side)   || defaultSide
  var offset = (pos && typeof pos.offset === 'number') ? pos.offset : defaultOffset

  var [drag, setDrag] = useState(null) // { side, offset } — live preview during drag
  var nodeElRef = useRef(null)
  var pointerIdRef = useRef(null)
  var updateNodeInternals = useUpdateNodeInternals()
  // Alt tusu basili oldugunda Handle isConnectable=false — React Flow connection
  // mekanizmasi devre disi kalir, istemsiz edge olusumu engellenir.
  var altDown = useAltKeyDown()

  // Handle DOM'una pointer down: Alt tutuluyorsa custom drag, değilse React Flow default
  var onPointerDown = useCallback(function (ev) {
    if (!ev.altKey) return                 // normal davranış — React Flow connection
    // React synthetic stopPropagation yetmez — React Flow handle'a native listener bagliyor;
    // istemsiz connection initiation'a yol acabilir. Native event'i de kapatiyoruz.
    ev.stopPropagation()
    ev.preventDefault()
    if (ev.nativeEvent && typeof ev.nativeEvent.stopImmediatePropagation === 'function') {
      ev.nativeEvent.stopImmediatePropagation()
    }
    // En yakın .afd-node parent'ı bul (handle'ın render edildiği node DOM kutusu)
    var n = ev.currentTarget
    while (n && !(n.classList && n.classList.contains('afd-node'))) n = n.parentElement
    if (!n) return
    nodeElRef.current = n
    pointerIdRef.current = ev.pointerId
    try { ev.currentTarget.setPointerCapture(ev.pointerId) } catch (_) {}
    setDrag({ side: side, offset: offset })
  }, [side, offset])

  var onPointerMove = useCallback(function (ev) {
    if (pointerIdRef.current == null) return
    if (!nodeElRef.current) return
    var p = pointToSideOffset(nodeElRef.current, ev.clientX, ev.clientY)
    if (!p) return
    p.offset = resolveCollision(p.side, p.offset, siblings)
    setDrag(p)
    // Live edge takibi — drag sirasinda da bagli edge handle'in yeni konumuna kaysin
    if (nodeId && typeof updateNodeInternals === 'function') {
      updateNodeInternals(nodeId)
    }
  }, [siblings, nodeId, updateNodeInternals])

  var onPointerUp = useCallback(function (ev) {
    if (pointerIdRef.current == null) return
    // Drop sirasinda React Flow connection finalize edilmesin
    ev.stopPropagation()
    ev.preventDefault()
    if (ev.nativeEvent && typeof ev.nativeEvent.stopImmediatePropagation === 'function') {
      ev.nativeEvent.stopImmediatePropagation()
    }
    try { ev.currentTarget.releasePointerCapture(pointerIdRef.current) } catch (_) {}
    pointerIdRef.current = null
    var finalPos = drag
    setDrag(null)
    if (finalPos && typeof onPositionChange === 'function') {
      onPositionChange(finalPos.side, finalPos.offset)
    }
    // Commit sonrasi edge'lerin yeni handle position'a kesin snap'i
    if (nodeId && typeof updateNodeInternals === 'function') {
      updateNodeInternals(nodeId)
    }
  }, [drag, onPositionChange, nodeId, updateNodeInternals])

  // Drag öncesi node DOM cache'ini temizle (re-render durumunda yeniden ölç)
  useEffect(function () { return function () { nodeElRef.current = null } }, [])

  var liveSide   = drag ? drag.side   : side
  var liveOffset = drag ? drag.offset : offset
  var style = Object.assign({}, posToHandleStyle(liveSide, liveOffset), props.style || {})
  if (color) style.background = color
  if (drag) style.cursor = 'grabbing'
  else      style.cursor = 'grab'

  // Label: handle ile birlikte tasinan kenar disi etiket (Onay/Red/Gecikme vb.)
  var label = props.label
  var labelClassName = props.labelClassName || ''
  var labelStyle = null
  if (label) {
    var pct = (clamp(liveOffset, 0, 1) * 100).toFixed(2) + '%'
    var ls = { position: 'absolute', pointerEvents: 'none', whiteSpace: 'nowrap' }
    if (liveSide === 'bottom') {
      ls.left = pct; ls.top = '100%'
      ls.transform = 'translate(-50%, 4px)'
    } else if (liveSide === 'top') {
      ls.left = pct; ls.bottom = '100%'
      ls.transform = 'translate(-50%, -4px)'
    } else if (liveSide === 'left') {
      ls.top = pct; ls.right = '100%'
      ls.transform = 'translate(-4px, -50%)'
    } else { // right
      ls.top = pct; ls.left = '100%'
      ls.transform = 'translate(4px, -50%)'
    }
    labelStyle = ls
  }

  return (
    <>
      <Handle
        id={id}
        type={type}
        position={SIDE_TO_POSITION[liveSide] || Position.Bottom}
        className={className}
        style={style}
        isConnectable={!altDown}
        onPointerDown={onPointerDown}
        onPointerMove={onPointerMove}
        onPointerUp={onPointerUp}
        onPointerCancel={onPointerUp}
        title="Alt + sürükle: bu çıkışı node kenarı boyunca taşı"
      />
      {label && (
        <span className={'afd-handle-label ' + labelClassName} style={labelStyle}>{label}</span>
      )}
    </>
  )
}

// Default değerleri helper'la dışarıya açıyoruz — StepNode varsayılan dağılım hesaplamada kullanır.
DraggableHandle.MIN_GAP  = MIN_GAP
DraggableHandle.EDGE_PAD = EDGE_PAD
