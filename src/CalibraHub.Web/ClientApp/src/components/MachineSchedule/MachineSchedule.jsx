import { useCallback, useEffect, useMemo, useState } from 'react'
import {
  DndContext, DragOverlay, PointerSensor, useSensor, useSensors,
} from '@dnd-kit/core'
import { Loader2 } from 'lucide-react'
import * as api from '../../services/machineScheduleService'
import UnplannedQueue, { OpDragGhost } from './UnplannedQueue'
import GanttToolbar from './GanttToolbar'
import GanttBoard from './GanttBoard'
import BlockEditPopover from './BlockEditPopover'
import {
  ZOOM_LEVELS, DEFAULT_ZOOM_INDEX, ROW_HEIGHT,
  localDateInputToUtcIso, utcIsoToLocalDateInput,
  addMinutes, diffMinutes, xToDate, snapDate, MIN_DURATION_MINUTES,
} from './timeScale'
import './MachineSchedule.css'

function todayInputValue(offsetDays) {
  var d = new Date()
  d.setDate(d.getDate() + (offsetDays || 0))
  return utcIsoToLocalDateInput(d.toISOString())
}

export default function MachineSchedule() {
  var [dateFrom, setDateFrom] = useState(todayInputValue(0))
  var [dateTo, setDateTo] = useState(todayInputValue(3))
  var [zoomIndex, setZoomIndex] = useState(DEFAULT_ZOOM_INDEX)
  var [isDark, setIsDark] = useState(function () { return document.body.classList.contains('app-theme-dark') })

  var [machines, setMachines] = useState([])
  var [blocks, setBlocks] = useState([])
  var [unplanned, setUnplanned] = useState([])
  var [workWindows, setWorkWindows] = useState([])
  var [holidays, setHolidays] = useState([])
  var [loading, setLoading] = useState(true)
  var [refreshing, setRefreshing] = useState(false)
  var [conflictIds, setConflictIds] = useState(function () { return new Set() })

  var [activeOp, setActiveOp] = useState(null)
  var [selected, setSelected] = useState(null) // { block, clientX, clientY }

  var sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 6 } })
  )

  useEffect(function () {
    var obs = new MutationObserver(function () {
      setIsDark(document.body.classList.contains('app-theme-dark'))
    })
    obs.observe(document.body, { attributes: true, attributeFilter: ['class'] })
    return function () { obs.disconnect() }
  }, [])

  var rangeStart = useMemo(function () { return new Date(dateFrom + 'T00:00:00') }, [dateFrom])
  var rangeEnd = useMemo(function () { return new Date(dateTo + 'T23:59:59') }, [dateTo])

  var fetchSchedule = useCallback(function (showSpinner) {
    if (showSpinner) setRefreshing(true)
    var fromIso = localDateInputToUtcIso(dateFrom, false)
    var toIso = localDateInputToUtcIso(dateTo, true)
    return api.getScheduleData(fromIso, toIso)
      .then(function (res) {
        if (!res || !res.ok) {
          window.CalibraHub?.toast?.('Plan verisi yüklenemedi.', 'err')
          return
        }
        setMachines(res.machines || [])
        setBlocks(res.blocks || [])
        setUnplanned(res.unplanned || [])
        setWorkWindows(res.workWindows || [])
        setHolidays(res.holidays || [])
      })
      .catch(function (e) {
        console.error('[MachineSchedule] fetch error:', e)
        window.CalibraHub?.toast?.('Plan verisi yüklenirken hata oluştu.', 'err')
      })
      .finally(function () {
        setLoading(false)
        setRefreshing(false)
      })
  }, [dateFrom, dateTo])

  useEffect(function () {
    setLoading(true)
    fetchSchedule(false)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [dateFrom, dateTo])

  function applySaveResult(res, successMessage) {
    if (!res || !res.ok) {
      window.CalibraHub?.toast?.(res && res.error ? res.error : 'Kaydetme başarısız.', 'err')
      return
    }
    if (res.conflicts && res.conflicts.length > 0) {
      var ids = res.conflicts.map(function (c) { return c.id })
      if (res.id) ids.push(res.id)
      setConflictIds(function (prev) {
        var next = new Set(prev)
        ids.forEach(function (id) { next.add(id) })
        return next
      })
      window.CalibraHub?.toast?.(
        'Kaydedildi ancak bu makinede aynı zaman diliminde çakışan ' + res.conflicts.length + ' plan var.',
        'warn'
      )
    } else if (successMessage) {
      window.CalibraHub?.toast?.(successMessage, 'ok')
    }
    fetchSchedule(true)
  }

  // ── dnd-kit: kuyruktan Gantt'a bırakma ────────────────────
  function handleDragStart(event) {
    var data = event.active.data.current
    if (data && data.kind === 'unplanned') setActiveOp(data.op)
  }

  function handleDragCancel() {
    setActiveOp(null)
  }

  function handleDragEnd(event) {
    setActiveOp(null)
    var active = event.active
    var over = event.over
    var data = active.data.current
    if (!data || data.kind !== 'unplanned') return
    if (!over || over.id !== 'gantt-body') return

    var translated = active.rect.current.translated
    var dropRect = over.rect
    if (!translated || !dropRect) return

    var dropX = (translated.left + translated.width / 2) - dropRect.left
    var dropY = (translated.top + translated.height / 2) - dropRect.top
    if (machines.length === 0) return

    var rowIndex = Math.min(machines.length - 1, Math.max(0, Math.floor(dropY / ROW_HEIGHT)))
    var machine = machines[rowIndex]
    if (!machine) return

    var pxPerHour = ZOOM_LEVELS[zoomIndex]
    var startDate = snapDate(xToDate(Math.max(0, dropX), rangeStart, pxPerHour), MIN_DURATION_MINUTES)
    var durationMinutes = data.op.estimatedMinutes && data.op.estimatedMinutes > 0 ? data.op.estimatedMinutes : 60
    var endDate = addMinutes(startDate, durationMinutes)

    api.saveScheduleBlock({
      id: 0,
      machineId: machine.id,
      workOrderOperationId: data.op.workOrderOperationId,
      startUtc: startDate.toISOString(),
      endUtc: endDate.toISOString(),
      blockType: 1,
      status: 1,
      notes: null,
    }).then(function (res) { applySaveResult(res, 'Operasyon planlandı.') })
  }

  // ── Blok taşı (yatay) ──────────────────────────────────────
  function handleBlockMove(block, newStartDate) {
    var oldStart = new Date(block.startUtc)
    var oldEnd = new Date(block.endUtc)
    var durationMinutes = diffMinutes(oldStart, oldEnd)
    var newEnd = addMinutes(newStartDate, durationMinutes)
    api.saveScheduleBlock({
      id: block.id,
      machineId: block.machineId,
      workOrderOperationId: block.workOrderOperationId,
      startUtc: newStartDate.toISOString(),
      endUtc: newEnd.toISOString(),
      blockType: block.blockType,
      status: block.status,
      notes: block.notes || null,
    }).then(function (res) { applySaveResult(res, null) })
  }

  // ── Blok yeniden boyutlandır ────────────────────────────────
  function handleBlockResize(block, newStartDate, newEndDate) {
    api.saveScheduleBlock({
      id: block.id,
      machineId: block.machineId,
      workOrderOperationId: block.workOrderOperationId,
      startUtc: newStartDate.toISOString(),
      endUtc: newEndDate.toISOString(),
      blockType: block.blockType,
      status: block.status,
      notes: block.notes || null,
    }).then(function (res) { applySaveResult(res, null) })
  }

  // ── Blok tıkla → popover ────────────────────────────────────
  function handleBlockClick(block, clientX, clientY) {
    setSelected({ block: block, clientX: clientX, clientY: clientY })
  }

  function handlePopoverSave(updatedBlock) {
    return api.saveScheduleBlock({
      id: updatedBlock.id,
      machineId: updatedBlock.machineId,
      workOrderOperationId: updatedBlock.workOrderOperationId,
      startUtc: updatedBlock.startUtc,
      endUtc: updatedBlock.endUtc,
      blockType: updatedBlock.blockType,
      status: updatedBlock.status,
      notes: updatedBlock.notes,
    }).then(function (res) {
      applySaveResult(res, 'Blok güncellendi.')
      setSelected(null)
    })
  }

  function handlePopoverDelete(block) {
    return api.deleteScheduleBlock(block.id).then(function (res) {
      if (!res || !res.ok) {
        window.CalibraHub?.toast?.('Silme başarısız.', 'err')
        return
      }
      window.CalibraHub?.toast?.('Plan silindi.', 'ok')
      setSelected(null)
      fetchSchedule(true)
    })
  }

  if (loading) {
    return (
      <div className="ms-root">
        <div className="ms-loading">
          <Loader2 size={20} className="ms-spin" /> Plan yükleniyor...
        </div>
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
      <div className="ms-root">
        <GanttToolbar
          dateFrom={dateFrom}
          dateTo={dateTo}
          onDateFromChange={setDateFrom}
          onDateToChange={setDateTo}
          zoomIndex={zoomIndex}
          onZoomIn={function () { setZoomIndex(function (i) { return Math.min(ZOOM_LEVELS.length - 1, i + 1) }) }}
          onZoomOut={function () { setZoomIndex(function (i) { return Math.max(0, i - 1) }) }}
          onRefresh={function () { fetchSchedule(true) }}
          isDark={isDark}
          refreshing={refreshing}
        />
        <div className="ms-body">
          <UnplannedQueue unplanned={unplanned} loading={refreshing} />
          <div className="ms-gantt">
            <GanttBoard
              machines={machines}
              blocks={blocks}
              rangeStart={rangeStart}
              rangeEnd={rangeEnd}
              pxPerHour={ZOOM_LEVELS[zoomIndex]}
              isDark={isDark}
              conflictIds={conflictIds}
              workWindows={workWindows}
              holidays={holidays}
              onBlockClick={handleBlockClick}
              onBlockMove={handleBlockMove}
              onBlockResize={handleBlockResize}
              loading={refreshing}
            />
          </div>
        </div>
      </div>

      <DragOverlay dropAnimation={{ duration: 160, easing: 'cubic-bezier(.2,.7,.4,1.1)' }}>
        {activeOp ? <OpDragGhost op={activeOp} /> : null}
      </DragOverlay>

      {selected && (
        <BlockEditPopover
          block={selected.block}
          clientX={selected.clientX}
          clientY={selected.clientY}
          onClose={function () { setSelected(null) }}
          onSave={handlePopoverSave}
          onDelete={handlePopoverDelete}
        />
      )}
    </DndContext>
  )
}
