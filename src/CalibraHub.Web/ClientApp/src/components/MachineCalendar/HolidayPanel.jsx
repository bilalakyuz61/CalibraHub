import { useState } from 'react'
import { Plus, Trash2, CalendarOff } from 'lucide-react'

function todayInputValue() {
  var d = new Date()
  var y = d.getFullYear()
  var m = String(d.getMonth() + 1).padStart(2, '0')
  var day = String(d.getDate()).padStart(2, '0')
  return y + '-' + m + '-' + day
}

/**
 * HolidayPanel — resmi tatil listesi (tarih + ad) ekle/sil.
 * Silme CLAUDE.md "silme onay standardı"na göre window.showConfirm() ile onaylanır.
 */
export default function HolidayPanel({ holidays, onAdd, onDelete, busy }) {
  var [date, setDate] = useState(todayInputValue())
  var [name, setName] = useState('')

  var sorted = holidays.slice().sort(function (a, b) { return a.date < b.date ? -1 : a.date > b.date ? 1 : 0 })

  function handleAdd() {
    if (!date) {
      window.CalibraHub?.toast?.('Tarih seçimi zorunludur.', 'err')
      return
    }
    if (!name.trim()) {
      window.CalibraHub?.toast?.('Tatil adı zorunludur.', 'err')
      return
    }
    onAdd({ id: 0, date: date, name: name.trim() })
    setName('')
  }

  async function handleDelete(h) {
    var ok = await window.showConfirm({
      title: 'Resmi Tatili Sil',
      message: '"' + h.name + '" (' + h.date + ') tatil kaydını silmek istediğinize emin misiniz?',
      okLabel: 'Evet, Sil',
      danger: true,
    })
    if (!ok) return
    onDelete(h.id)
  }

  return (
    <div className="mcal-hol-panel">
      <div className="mcal-hol-add">
        <input
          type="date"
          className="mcal-date-input"
          value={date}
          onChange={function (e) { setDate(e.target.value) }}
        />
        <input
          type="text"
          className="mcal-text-input"
          placeholder="Tatil adı..."
          value={name}
          onChange={function (e) { setName(e.target.value) }}
          onKeyDown={function (e) { if (e.key === 'Enter') handleAdd() }}
        />
        <button type="button" className="mcal-icon-btn" title="Ekle" disabled={busy} onClick={handleAdd}>
          <Plus size={14} />
        </button>
      </div>

      <div className="mcal-hol-list">
        {sorted.length === 0 && (
          <div className="mcal-empty">
            <CalendarOff size={16} /> Henüz resmi tatil tanımlanmamış.
          </div>
        )}
        {sorted.map(function (h) {
          return (
            <div key={h.id} className="mcal-hol-row">
              <span className="mcal-hol-row__date">{h.date}</span>
              <span className="mcal-hol-row__name">{h.name}</span>
              <button
                type="button"
                className="mcal-ww-chip__del"
                title="Sil"
                disabled={busy}
                onClick={function () { handleDelete(h) }}
              >
                <Trash2 size={12} />
              </button>
            </div>
          )
        })}
      </div>
    </div>
  )
}
