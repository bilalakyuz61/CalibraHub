import { useMemo } from 'react'
import { useDroppable } from '@dnd-kit/core'
import { Stage, Layer, Rect, Line, Text } from 'react-konva'
import ScheduleBlock from './ScheduleBlock'
import { getPalette } from './ganttPalette'
import {
  ROW_HEIGHT, HEADER_HEIGHT, MACHINE_COL_WIDTH,
  dateToX, hoursBetween, formatHm,
} from './timeScale'

function buildTicks(rangeStart, rangeEnd, pxPerHour) {
  var totalHours = Math.max(1, hoursBetween(rangeStart, rangeEnd))
  var stepHours = pxPerHour >= 30 ? 1 : pxPerHour >= 15 ? 3 : 6
  var ticks = []
  var cursor = new Date(rangeStart)
  cursor.setMinutes(0, 0, 0)
  var guard = 0
  while (cursor <= rangeEnd && guard < 2000) {
    guard++
    if (cursor >= rangeStart) {
      var isDayStart = cursor.getHours() === 0
      ticks.push({
        x: dateToX(cursor, rangeStart, pxPerHour),
        isDayStart: isDayStart,
        label: isDayStart
          ? cursor.toLocaleDateString('tr-TR', { day: '2-digit', month: 'short' })
          : formatHm(cursor),
      })
    }
    cursor = new Date(cursor.getTime() + stepHours * 3_600_000)
  }
  return { ticks: ticks, totalHours: totalHours }
}

/**
 * GanttBoard — makine satırları x zaman ekseni Konva görselleştirmesi.
 * Layout: sticky CSS grid (corner / header-stage / machine-col / body-stage)
 * tek scroll container içinde — yatay+dikey scroll tek noktadan yönetilir.
 */
export default function GanttBoard({
  machines, blocks, rangeStart, rangeEnd, pxPerHour, isDark,
  conflictIds, onBlockClick, onBlockMove, onBlockResize, loading,
}) {
  var palette = getPalette(isDark)
  var { setNodeRef: setDropRef } = useDroppable({ id: 'gantt-body' })

  var tickInfo = useMemo(function () {
    return buildTicks(rangeStart, rangeEnd, pxPerHour)
  }, [rangeStart, rangeEnd, pxPerHour])

  var timelineWidth = Math.max(200, tickInfo.totalHours * pxPerHour)
  var bodyHeight = Math.max(ROW_HEIGHT, (machines.length || 0) * ROW_HEIGHT)

  var now = new Date()
  var nowX = now >= rangeStart && now <= rangeEnd ? dateToX(now, rangeStart, pxPerHour) : null

  var machineIndexById = useMemo(function () {
    var map = {}
    machines.forEach(function (m, i) { map[m.id] = i })
    return map
  }, [machines])

  return (
    <div className="ms-gantt-scroll">
      <div className="ms-gantt-grid" style={{ '--ms-col-w': MACHINE_COL_WIDTH + 'px', width: MACHINE_COL_WIDTH + timelineWidth }}>
        {/* Corner */}
        <div className="ms-corner" style={{ width: MACHINE_COL_WIDTH, height: HEADER_HEIGHT }} />

        {/* Header stage (zaman ekseni) */}
        <div className="ms-header-stage-wrap" style={{ width: timelineWidth, height: HEADER_HEIGHT }}>
          <Stage width={timelineWidth} height={HEADER_HEIGHT}>
            <Layer listening={false}>
              <Rect x={0} y={0} width={timelineWidth} height={HEADER_HEIGHT} fill={palette.headerBg} />
              {tickInfo.ticks.map(function (t, i) {
                return (
                  <Line
                    key={'gl' + i}
                    points={[t.x, t.isDayStart ? 8 : 22, t.x, HEADER_HEIGHT]}
                    stroke={palette.gridLine}
                    strokeWidth={t.isDayStart ? 1.4 : 1}
                  />
                )
              })}
              {tickInfo.ticks.map(function (t, i) {
                return (
                  <Text
                    key={'tl' + i}
                    x={t.x + 4}
                    y={t.isDayStart ? 4 : HEADER_HEIGHT - 18}
                    text={t.label}
                    fontSize={t.isDayStart ? 11 : 10}
                    fontStyle={t.isDayStart ? '600' : '400'}
                    fill={t.isDayStart ? palette.text : palette.textMuted}
                  />
                )
              })}
            </Layer>
          </Stage>
        </div>

        {/* Makine adı kolonu */}
        <div className="ms-machine-col" style={{ width: MACHINE_COL_WIDTH, height: bodyHeight }}>
          {machines.map(function (m) {
            return (
              <div key={m.id} className="ms-machine-row" style={{ height: ROW_HEIGHT }}>
                <div className="ms-machine-name" title={m.name}>{m.name}</div>
                {m.code && m.code !== m.name && <div className="ms-machine-sub">{m.code}</div>}
              </div>
            )
          })}
        </div>

        {/* Gantt gövde stage */}
        <div ref={setDropRef} className="ms-body-stage-wrap" style={{ width: timelineWidth, height: bodyHeight }}>
          <Stage width={timelineWidth} height={bodyHeight}>
            <Layer listening={false}>
              {machines.map(function (m, i) {
                return (
                  <Rect
                    key={'row' + m.id}
                    x={0}
                    y={i * ROW_HEIGHT}
                    width={timelineWidth}
                    height={ROW_HEIGHT}
                    fill={i % 2 === 1 ? palette.rowAlt : 'transparent'}
                  />
                )
              })}
              {tickInfo.ticks.filter(function (t) { return t.isDayStart }).map(function (t, i) {
                return (
                  <Line
                    key={'vgl' + i}
                    points={[t.x, 0, t.x, bodyHeight]}
                    stroke={palette.gridLine}
                    strokeWidth={1.2}
                  />
                )
              })}
              {machines.map(function (m, i) {
                return (
                  <Line
                    key={'hgl' + m.id}
                    points={[0, (i + 1) * ROW_HEIGHT, timelineWidth, (i + 1) * ROW_HEIGHT]}
                    stroke={palette.gridLine}
                    strokeWidth={1}
                  />
                )
              })}
              {nowX != null && (
                <Line points={[nowX, 0, nowX, bodyHeight]} stroke={palette.conflict} strokeWidth={1.4} dash={[4, 3]} />
              )}
            </Layer>
            <Layer>
              {blocks.map(function (b) {
                var rowIndex = machineIndexById[b.machineId]
                if (rowIndex == null) return null
                var start = new Date(b.startUtc)
                var end = new Date(b.endUtc)
                var bx = dateToX(start, rangeStart, pxPerHour)
                var bw = Math.max(18, dateToX(end, rangeStart, pxPerHour) - bx)
                return (
                  <ScheduleBlock
                    key={b.id}
                    block={b}
                    x={bx}
                    y={rowIndex * ROW_HEIGHT + 6}
                    width={bw}
                    height={ROW_HEIGHT - 12}
                    minX={0}
                    isDark={isDark}
                    isConflict={conflictIds && conflictIds.has(b.id)}
                    rangeStart={rangeStart}
                    pxPerHour={pxPerHour}
                    onMove={onBlockMove}
                    onResize={onBlockResize}
                    onClick={onBlockClick}
                  />
                )
              })}
            </Layer>
          </Stage>
          {!loading && machines.length === 0 && (
            <div className="ms-empty-hint">Bu aralıkta tanımlı makine bulunamadı.</div>
          )}
        </div>
      </div>
    </div>
  )
}
