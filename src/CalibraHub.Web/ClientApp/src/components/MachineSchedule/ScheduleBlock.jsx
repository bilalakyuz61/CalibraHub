import { useEffect, useRef, useState } from 'react'
import { Group, Rect, Text } from 'react-konva'
import { getPalette } from './ganttPalette'
import { xToDate, snapDate, MIN_DURATION_MINUTES } from './timeScale'

var HANDLE_W = 7
var MIN_W = 18

/**
 * ScheduleBlock — bir MachineScheduleBlock'un Gantt üzerindeki dikdörtgeni.
 * Etkileşimler:
 *  - Yatay sürükle → taşı (y satıra kilitli, sadece x değişir)
 *  - Sol/sağ kenar handle → yeniden boyutlandır (süre değiştir)
 *  - Tıkla (sürüklemeden) → onClick(block, clientX, clientY) — popover açılır
 * Status=2 (Locked) ise taşıma/boyutlandırma devre dışı.
 * readOnly=true (setup child — parentBlockId dolu) ise blok üretim bloğuna bağlıdır:
 * bağımsız sürüklenemez/resize edilemez ve tıklanınca popover AÇILMAZ (salt-görüntü).
 * Kullanıcı üretim bloğunu taşıyınca backend child'ı otomatik günceller (refetch'te yansır).
 * onMove(block, newStartDate) / onResize(block, newStartDate, newEndDate) — piksel
 * yerine Date nesnesi ile çağrılır (15dk snap uygulanmış).
 * onToggleLock(block, newStatus) — blok üstündeki kilit ikonuna tıklanınca çağrılır
 * (status 1↔2). status===3 (Onaylı) veya readOnly (setup child) bloklarda ikon gizlidir.
 */
export default function ScheduleBlock({
  block, x, y, width, height, isDark, isConflict,
  onMove, onResize, onClick, onToggleLock, minX, rangeStart, pxPerHour, readOnly,
}) {
  var palette = getPalette(isDark)
  var typeColors = palette.block[block.blockType] || palette.block[1]
  var showLockIcon = block.status === 2
  var locked = showLockIcon || !!readOnly
  var canToggleLock = !readOnly && block.status !== 3

  var [localX, setLocalX] = useState(x)
  var [localW, setLocalW] = useState(width)
  var movedRef = useRef(false)
  var groupRef = useRef(null)

  useEffect(function () {
    setLocalX(x)
    setLocalW(width)
  }, [x, width])

  function clampX(nx) {
    return Math.max(minX != null ? minX : 0, nx)
  }

  function handleDragStart() {
    movedRef.current = false
  }

  function handleDragMove(e) {
    movedRef.current = true
    var node = e.target
    node.y(0) // y satıra göre grup zaten konumlanır — grup içi hep 0
    var nx = clampX(node.x())
    node.x(nx)
    setLocalX(nx)
  }

  function handleDragEnd(e) {
    var nx = clampX(e.target.x())
    e.target.x(nx)
    if (movedRef.current && typeof onMove === 'function') {
      var newStart = snapDate(xToDate(nx, rangeStart, pxPerHour), MIN_DURATION_MINUTES)
      onMove(block, newStart)
    }
  }

  function handleClick(e) {
    if (movedRef.current) { movedRef.current = false; return }
    if (readOnly) return // setup child — düzenleme/silme popover'ı açılmaz (salt-görüntü)
    var evt = e.evt
    if (typeof onClick === 'function') onClick(block, evt.clientX, evt.clientY)
  }

  function handleLeftResize(e) {
    var node = e.target
    var dx = node.x()
    var nx = clampX(localX + dx)
    var nw = Math.max(MIN_W, localW - (nx - localX))
    node.x(0) // handle görsel olarak yerinde sabit kalır, gerçek konum grup x/width'ten hesaplanır
    node.y(0)
    setLocalX(nx)
    setLocalW(nw)
  }

  function handleLeftResizeEnd() {
    if (typeof onResize === 'function') {
      var newStart = snapDate(xToDate(localX, rangeStart, pxPerHour), MIN_DURATION_MINUTES)
      var newEnd = snapDate(xToDate(localX + localW, rangeStart, pxPerHour), MIN_DURATION_MINUTES)
      onResize(block, newStart, newEnd)
    }
  }

  function handleRightResize(e) {
    var node = e.target
    var dx = node.x()
    var nw = Math.max(MIN_W, localW + dx)
    node.x(0)
    node.y(0)
    setLocalW(nw)
  }

  function handleRightResizeEnd() {
    if (typeof onResize === 'function') {
      var newStart = snapDate(xToDate(localX, rangeStart, pxPerHour), MIN_DURATION_MINUTES)
      var newEnd = snapDate(xToDate(localX + localW, rangeStart, pxPerHour), MIN_DURATION_MINUTES)
      onResize(block, newStart, newEnd)
    }
  }

  function handleLockToggle(e) {
    e.cancelBubble = true // Konva event'i Group'a (popover onClick) sızmasın
    if (typeof onToggleLock === 'function') {
      onToggleLock(block, block.status === 2 ? 1 : 2)
    }
  }

  var label = readOnly ? 'Hazırlık' : (block.workOrderNo ? block.workOrderNo + ' · ' : '') + (block.operationName || '')
  var sub = readOnly ? (block.operationName || '') : (block.itemName || '')

  return (
    <Group
      ref={groupRef}
      x={localX}
      y={y}
      draggable={!locked}
      dragBoundFunc={function (pos) { return { x: clampX(pos.x), y: y } }}
      onDragStart={handleDragStart}
      onDragMove={handleDragMove}
      onDragEnd={handleDragEnd}
      onClick={handleClick}
      onTap={handleClick}
    >
      <Rect
        width={localW}
        height={height}
        cornerRadius={5}
        fill={typeColors.fill}
        stroke={isConflict ? palette.conflict : typeColors.stroke}
        strokeWidth={isConflict ? 2.5 : 1}
        dash={isConflict ? [5, 3] : undefined}
        shadowColor="rgba(0,0,0,0.35)"
        shadowBlur={3}
        shadowOpacity={0.25}
        opacity={locked ? 0.85 : 1}
      />
      {localW > 34 && (
        <Text
          text={label}
          x={8}
          y={sub ? 6 : height / 2 - 7}
          width={localW - 16}
          height={14}
          fontSize={11.5}
          fontStyle="600"
          fill={typeColors.text}
          ellipsis
          wrap="none"
        />
      )}
      {localW > 34 && sub && (
        <Text
          text={sub}
          x={8}
          y={22}
          width={localW - 16}
          height={13}
          fontSize={10.5}
          fill={typeColors.text}
          opacity={0.85}
          ellipsis
          wrap="none"
        />
      )}
      {/* Resize handles */}
      {!locked && (
        <Rect
          x={0}
          y={0}
          width={HANDLE_W}
          height={height}
          fill="transparent"
          draggable
          onDragStart={function () { movedRef.current = true }}
          onDragMove={handleLeftResize}
          onDragEnd={handleLeftResizeEnd}
          onMouseEnter={function (e) { e.target.getStage().container().style.cursor = 'ew-resize' }}
          onMouseLeave={function (e) { e.target.getStage().container().style.cursor = 'default' }}
        />
      )}
      {!locked && (
        <Rect
          x={localW - HANDLE_W}
          y={0}
          width={HANDLE_W}
          height={height}
          fill="transparent"
          draggable
          onDragStart={function () { movedRef.current = true }}
          onDragMove={handleRightResize}
          onDragEnd={handleRightResizeEnd}
          onMouseEnter={function (e) { e.target.getStage().container().style.cursor = 'ew-resize' }}
          onMouseLeave={function (e) { e.target.getStage().container().style.cursor = 'default' }}
        />
      )}

      {/* Hızlı kilit toggle — status 1(Planlı)↔2(Kilitli). status===3(Onaylı)/readOnly'de gizli.
          Resize handle'lardan SONRA render edilir → sağ üst köşedeki örtüşen bölgede
          Konva hit-test önceliği bu ikonda kalır (üstte olan kazanır). */}
      {canToggleLock && localW > 30 && (
        <Group
          x={localW - 20}
          y={2}
          onClick={handleLockToggle}
          onTap={handleLockToggle}
          onMouseEnter={function (e) { e.target.getStage().container().style.cursor = 'pointer' }}
          onMouseLeave={function (e) { e.target.getStage().container().style.cursor = 'default' }}
        >
          <Rect width={18} height={16} fill="transparent" />
          <Text
            text={showLockIcon ? '🔒' : '🔓'}
            x={1}
            y={1}
            fontSize={11}
            opacity={showLockIcon ? 1 : 0.55}
          />
        </Group>
      )}
    </Group>
  )
}
