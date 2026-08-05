import { useMemo } from 'react'
import { useDroppable } from '@dnd-kit/core'
import { Stage, Layer, Rect, Line, Text, Group } from 'react-konva'
import ScheduleBlock from './ScheduleBlock'
import { getPalette } from './ganttPalette'
import { buildWorkWindowShades, buildHolidayColumns } from './scheduleShading'
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
  workWindows, holidays, previewProposals,
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

  // ── Faz 2: çalışma-dışı saat + resmi tatil gölgeleme ────────
  var workWindowShades = useMemo(function () {
    return buildWorkWindowShades(machines, workWindows, rangeStart, rangeEnd, pxPerHour, ROW_HEIGHT)
  }, [machines, workWindows, rangeStart, rangeEnd, pxPerHour])

  var holidayCols = useMemo(function () {
    return buildHolidayColumns(holidays, rangeStart, rangeEnd, pxPerHour)
  }, [holidays, rangeStart, rangeEnd, pxPerHour])

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
          {/* Tatil şeridi — HTML overlay (native title tooltip; Konva canvas'ta gerçek tooltip yok) */}
          {holidayCols.map(function (c) {
            return (
              <div
                key={'hol-hdr-' + c.key}
                className="ms-holiday-marker"
                title={c.name}
                style={{ left: c.x, width: c.width, height: HEADER_HEIGHT }}
              />
            )
          })}
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
              {/* Faz 2: resmi tatil — tüm makine satırlarında tam-yükseklik gölge (bloklardan ALTTA) */}
              {holidayCols.map(function (c) {
                return (
                  <Rect
                    key={'holbody-' + c.key}
                    x={c.x}
                    y={0}
                    width={c.width}
                    height={bodyHeight}
                    fill={palette.holiday}
                  />
                )
              })}
              {/* Faz 2: makine çalışma-dışı saatleri — yarı-saydam gölge (bloklardan ALTTA) */}
              {workWindowShades.map(function (s) {
                return (
                  <Rect
                    key={'shade-' + s.key}
                    x={s.x}
                    y={s.y}
                    width={s.width}
                    height={s.height}
                    fill={palette.shade}
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
                    readOnly={b.parentBlockId != null}
                  />
                )
              })}
            </Layer>
            {/* Faz 3: otomatik yerleştir önizleme — ghost öneri blokları (tıklanamaz, mevcut bloklardan görsel ayrı) */}
            {previewProposals && previewProposals.length > 0 && (
              <Layer listening={false}>
                {previewProposals.map(function (p) {
                  var rowIndex = machineIndexById[p.machineId]
                  if (rowIndex == null) return null
                  var pStart = new Date(p.startUtc)
                  var pEnd = new Date(p.endUtc)
                  var px = dateToX(pStart, rangeStart, pxPerHour)
                  var pw = Math.max(18, dateToX(pEnd, rangeStart, pxPerHour) - px)
                  var py = rowIndex * ROW_HEIGHT + 6
                  var ph = ROW_HEIGHT - 12
                  var typeColors = palette.block[p.blockType] || palette.block[1]
                  var label = (p.workOrderNo ? p.workOrderNo + ' · ' : '') + (p.operationName || '')
                  return (
                    <Group key={'preview-' + p.tempId}>
                      <Rect
                        x={px}
                        y={py}
                        width={pw}
                        height={ph}
                        cornerRadius={5}
                        fill={palette.previewFill}
                        stroke={palette.previewStroke}
                        strokeWidth={1.6}
                        dash={[6, 4]}
                      />
                      {pw > 40 && (
                        <Text
                          text={label}
                          x={px + 8}
                          y={py + 5}
                          width={pw - 16}
                          height={14}
                          fontSize={11}
                          fontStyle="600"
                          fill={typeColors.stroke}
                          ellipsis
                          wrap="none"
                        />
                      )}
                      {pw > 48 && (
                        <Text
                          text="ÖNERİ"
                          x={px + pw - 44}
                          y={py + ph - 15}
                          width={40}
                          height={12}
                          fontSize={9}
                          fontStyle="700"
                          fill={palette.previewStroke}
                          align="right"
                        />
                      )}
                    </Group>
                  )
                })}
              </Layer>
            )}
          </Stage>
          {!loading && machines.length === 0 && (
            <div className="ms-empty-hint">Bu aralıkta tanımlı makine bulunamadı.</div>
          )}
        </div>
      </div>
    </div>
  )
}
