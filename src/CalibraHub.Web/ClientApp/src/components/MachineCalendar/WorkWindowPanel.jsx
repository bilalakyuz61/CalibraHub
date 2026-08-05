import { useMemo, useState } from 'react'
import { Plus, Trash2, Clock } from 'lucide-react'
import { DAY_ORDER, DAY_LABELS, minutesToTime, timeToMinutes } from './timeMinutes'

/**
 * WorkWindowPanel — makine bazlı haftalık çalışma penceresi editörü.
 * Her gün (Pzt..Paz) için bir/çok pencere ("08:00–17:00" gibi) eklenir/silinir.
 * Silme CLAUDE.md "silme onay standardı"na göre window.showConfirm() ile onaylanır.
 */
export default function WorkWindowPanel({ machines, windows, onAdd, onDelete, busy }) {
  var [selectedMachineId, setSelectedMachineId] = useState(function () {
    return machines.length > 0 ? machines[0].id : null
  })
  var [drafts, setDrafts] = useState({}) // { [dayOfWeek]: { start, end } }

  var machineWindows = useMemo(function () {
    var byDay = {}
    DAY_ORDER.forEach(function (d) { byDay[d] = [] })
    windows
      .filter(function (w) { return w.machineId === selectedMachineId })
      .forEach(function (w) {
        if (!byDay[w.dayOfWeek]) byDay[w.dayOfWeek] = []
        byDay[w.dayOfWeek].push(w)
      })
    Object.keys(byDay).forEach(function (d) {
      byDay[d].sort(function (a, b) { return a.startMinute - b.startMinute })
    })
    return byDay
  }, [windows, selectedMachineId])

  function draftFor(day) {
    return drafts[day] || { start: '08:00', end: '17:00' }
  }
  function setDraft(day, patch) {
    setDrafts(function (prev) {
      var next = Object.assign({}, prev)
      next[day] = Object.assign({}, draftFor(day), patch)
      return next
    })
  }

  function handleAdd(day) {
    var d = draftFor(day)
    var startMinute = timeToMinutes(d.start)
    var endMinute = timeToMinutes(d.end)
    if (endMinute <= startMinute) {
      window.CalibraHub?.toast?.('Bitiş saati başlangıçtan sonra olmalıdır.', 'err')
      return
    }
    onAdd({ id: 0, machineId: selectedMachineId, dayOfWeek: day, startMinute: startMinute, endMinute: endMinute })
  }

  async function handleDelete(w) {
    var ok = await window.showConfirm({
      title: 'Çalışma Penceresini Sil',
      message: DAY_LABELS[w.dayOfWeek] + ' günü ' + minutesToTime(w.startMinute) + '–' + minutesToTime(w.endMinute) + ' çalışma penceresini silmek istediğinize emin misiniz?',
      okLabel: 'Evet, Sil',
      danger: true,
    })
    if (!ok) return
    onDelete(w.id)
  }

  if (machines.length === 0) {
    return <div className="mcal-empty">Tanımlı makine bulunamadı.</div>
  }

  return (
    <div className="mcal-ww-panel">
      <div className="mcal-ww-toolbar">
        <label className="mcal-label">Makine</label>
        <select
          className="mcal-select"
          value={selectedMachineId || ''}
          onChange={function (e) { setSelectedMachineId(parseInt(e.target.value, 10)) }}
        >
          {machines.map(function (m) {
            return <option key={m.id} value={m.id}>{m.code ? m.code + ' — ' + m.name : m.name}</option>
          })}
        </select>
      </div>

      <div className="mcal-ww-days">
        {DAY_ORDER.map(function (day) {
          var dayWindows = machineWindows[day] || []
          var d = draftFor(day)
          return (
            <div key={day} className="mcal-ww-day">
              <div className="mcal-ww-day__title">{DAY_LABELS[day]}</div>

              <div className="mcal-ww-day__list">
                {dayWindows.length === 0 && <div className="mcal-ww-day__empty">Pencere yok</div>}
                {dayWindows.map(function (w) {
                  return (
                    <div key={w.id} className="mcal-ww-chip">
                      <Clock size={12} />
                      <span>{minutesToTime(w.startMinute)}–{minutesToTime(w.endMinute)}</span>
                      <button
                        type="button"
                        className="mcal-ww-chip__del"
                        title="Sil"
                        disabled={busy}
                        onClick={function () { handleDelete(w) }}
                      >
                        <Trash2 size={12} />
                      </button>
                    </div>
                  )
                })}
              </div>

              <div className="mcal-ww-day__add">
                <input
                  type="time"
                  className="mcal-time-input"
                  value={d.start}
                  onChange={function (e) { setDraft(day, { start: e.target.value }) }}
                />
                <span className="mcal-ww-day__sep">–</span>
                <input
                  type="time"
                  className="mcal-time-input"
                  value={d.end}
                  onChange={function (e) { setDraft(day, { end: e.target.value }) }}
                />
                <button
                  type="button"
                  className="mcal-icon-btn"
                  title="Pencere Ekle"
                  disabled={busy || !selectedMachineId}
                  onClick={function () { handleAdd(day) }}
                >
                  <Plus size={14} />
                </button>
              </div>
            </div>
          )
        })}
      </div>
    </div>
  )
}
