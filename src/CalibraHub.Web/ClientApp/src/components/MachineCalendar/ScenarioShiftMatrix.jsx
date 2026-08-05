import { useMemo, useState } from 'react'
import { Plus, Trash2 } from 'lucide-react'
import { DAY_ORDER, DAY_LABELS_SHORT } from './timeMinutes'

/**
 * ScenarioShiftMatrix — seçili senaryo için makine × vardiya × gün atama matrisi.
 * Her makine bir bölüm; altında o senaryoya atanmış vardiya satırları (7 gün toggle =
 * DaysMask bit'i, bit0=Pazar..bit6=Cumartesi). "+ Vardiya Ata" yeni satır oluşturur
 * (gün seçimi olmadan), gün toggle'ları anlık kaydedilir. Aynı senaryo+makine için
 * birden çok vardiya satırı olabilir (çift vardiya).
 */
export default function ScenarioShiftMatrix({ machines, shifts, assignments, scenarioId, onSave, onDelete, busy }) {
  var [addingForMachine, setAddingForMachine] = useState(null)
  var [draftShiftId, setDraftShiftId] = useState(function () { return shifts.length > 0 ? shifts[0].id : null })

  var byMachine = useMemo(function () {
    var map = {}
    machines.forEach(function (m) { map[m.id] = [] })
    assignments.forEach(function (a) {
      if (!map[a.machineId]) map[a.machineId] = []
      map[a.machineId].push(a)
    })
    return map
  }, [machines, assignments])

  function shiftById(id) {
    return shifts.find(function (s) { return s.id === id })
  }

  function dayChecked(mask, day) {
    return ((mask || 0) & (1 << day)) !== 0
  }

  function toggleDay(row, day) {
    var newMask = (row.daysMask || 0) ^ (1 << day)
    onSave({ id: row.id, scenarioId: scenarioId, machineId: row.machineId, shiftId: row.shiftId, daysMask: newMask })
  }

  function handleStartAdd(machineId) {
    setAddingForMachine(machineId)
    setDraftShiftId(shifts.length > 0 ? shifts[0].id : null)
  }

  function handleConfirmAdd(machineId) {
    if (!draftShiftId) {
      window.CalibraHub?.toast?.('Vardiya seçimi zorunludur.', 'err')
      return
    }
    onSave({ id: 0, scenarioId: scenarioId, machineId: machineId, shiftId: draftShiftId, daysMask: 0 })
    setAddingForMachine(null)
  }

  async function handleDeleteRow(row) {
    var shift = shiftById(row.shiftId)
    var ok = await window.showConfirm({
      title: 'Vardiya Atamasını Sil',
      message: (shift ? shift.name : (row.shiftName || 'Vardiya')) + ' atamasını bu makineden kaldırmak istediğinize emin misiniz?',
      okLabel: 'Evet, Sil',
      danger: true,
    })
    if (!ok) return
    onDelete(row.id)
  }

  if (machines.length === 0) {
    return <div className="mcal-empty">Tanımlı makine bulunamadı.</div>
  }
  if (shifts.length === 0) {
    return <div className="mcal-empty">Önce Vardiyalar ekranından en az bir vardiya tanımlayın.</div>
  }

  return (
    <div className="mcal-matrix">
      {machines.map(function (m) {
        var rows = byMachine[m.id] || []
        return (
          <div key={m.id} className="mcal-matrix__machine">
            <div className="mcal-matrix__machine-head">
              <span className="mcal-matrix__machine-name">{m.code ? m.code + ' — ' + m.name : m.name}</span>
              <button
                type="button"
                className="mcal-icon-btn"
                title="Vardiya Ata"
                disabled={busy}
                onClick={function () { handleStartAdd(m.id) }}
              >
                <Plus size={14} />
              </button>
            </div>

            {rows.length === 0 && addingForMachine !== m.id && (
              <div className="mcal-ww-day__empty mcal-matrix__empty-row">Vardiya atanmamış</div>
            )}

            <div className="mcal-matrix__rows">
              {rows.map(function (row) {
                var shift = shiftById(row.shiftId)
                return (
                  <div key={row.id} className="mcal-matrix__row">
                    <span className="mcal-shift-badge" style={{ '--mcal-shift-color': (shift && shift.colorHex) || 'var(--mcal-accent)' }}>
                      {(shift && shift.name) || row.shiftName || '—'}
                      {shift && <span className="mcal-shift-badge__time">{shift.startTime}–{shift.endTime}</span>}
                    </span>
                    <div className="mcal-matrix__days">
                      {DAY_ORDER.map(function (day) {
                        var checked = dayChecked(row.daysMask, day)
                        return (
                          <label
                            key={day}
                            className={'mcal-day-check' + (checked ? ' mcal-day-check--on' : '')}
                            title={DAY_LABELS_SHORT[day]}
                          >
                            <input
                              type="checkbox"
                              checked={checked}
                              disabled={busy}
                              onChange={function () { toggleDay(row, day) }}
                            />
                            <span>{DAY_LABELS_SHORT[day]}</span>
                          </label>
                        )
                      })}
                    </div>
                    <button
                      type="button"
                      className="mcal-ww-chip__del"
                      title="Sil"
                      disabled={busy}
                      onClick={function () { handleDeleteRow(row) }}
                    >
                      <Trash2 size={13} />
                    </button>
                  </div>
                )
              })}

              {addingForMachine === m.id && (
                <div className="mcal-matrix__row mcal-matrix__row--draft">
                  <select
                    className="mcal-select mcal-select--sm"
                    value={draftShiftId || ''}
                    onChange={function (e) { setDraftShiftId(parseInt(e.target.value, 10)) }}
                  >
                    {shifts.map(function (s) {
                      return <option key={s.id} value={s.id}>{s.name} ({s.startTime}–{s.endTime})</option>
                    })}
                  </select>
                  <button
                    type="button"
                    className="mcal-icon-btn"
                    title="Ekle"
                    disabled={busy}
                    onClick={function () { handleConfirmAdd(m.id) }}
                  >
                    <Plus size={14} />
                  </button>
                  <button type="button" className="mcal-link-btn" onClick={function () { setAddingForMachine(null) }}>Vazgeç</button>
                </div>
              )}
            </div>
          </div>
        )
      })}
    </div>
  )
}
