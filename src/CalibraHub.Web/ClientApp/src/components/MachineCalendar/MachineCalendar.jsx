import { useCallback, useEffect, useState } from 'react'
import { Loader2, CalendarClock, Clock3, CalendarOff } from 'lucide-react'
import * as api from '../../services/machineCalendarService'
import WorkWindowPanel from './WorkWindowPanel'
import HolidayPanel from './HolidayPanel'
import './MachineCalendar.css'

/**
 * MachineCalendar — Makine Çalışma Takvimi (Faz 2 admin ekranı).
 * İki bölüm: haftalık çalışma penceresi (makine bazlı) + resmi tatil listesi.
 * Bu ekranda kaydedilen veri, Makine Planlama Gantt'ında (Faz 2 gölgeleme) kullanılır.
 */
export default function MachineCalendar() {
  var [loading, setLoading] = useState(true)
  var [busy, setBusy] = useState(false)
  var [machines, setMachines] = useState([])
  var [windows, setWindows] = useState([])
  var [holidays, setHolidays] = useState([])

  var fetchData = useCallback(function () {
    return api.getCalendarData()
      .then(function (res) {
        if (!res || !res.ok) {
          window.CalibraHub?.toast?.('Takvim verisi yüklenemedi.', 'err')
          return
        }
        setMachines(res.machines || [])
        setWindows(res.windows || [])
        setHolidays(res.holidays || [])
      })
      .catch(function (e) {
        console.error('[MachineCalendar] fetch error:', e)
        window.CalibraHub?.toast?.('Takvim verisi yüklenirken hata oluştu.', 'err')
      })
      .finally(function () { setLoading(false) })
  }, [])

  useEffect(function () { fetchData() }, [fetchData])

  function handleAddWindow(payload) {
    setBusy(true)
    api.saveWorkWindow(payload)
      .then(function (res) {
        if (!res || !res.ok) {
          window.CalibraHub?.toast?.((res && res.error) || 'Kaydetme başarısız.', 'err')
          return
        }
        window.CalibraHub?.toast?.('Çalışma penceresi eklendi.', 'ok')
        return fetchData()
      })
      .finally(function () { setBusy(false) })
  }

  function handleDeleteWindow(id) {
    setBusy(true)
    api.deleteWorkWindow(id)
      .then(function (res) {
        if (!res || !res.ok) {
          window.CalibraHub?.toast?.('Silme başarısız.', 'err')
          return
        }
        window.CalibraHub?.toast?.('Çalışma penceresi silindi.', 'ok')
        return fetchData()
      })
      .finally(function () { setBusy(false) })
  }

  function handleAddHoliday(payload) {
    setBusy(true)
    api.saveHoliday(payload)
      .then(function (res) {
        if (!res || !res.ok) {
          window.CalibraHub?.toast?.((res && res.error) || 'Kaydetme başarısız.', 'err')
          return
        }
        window.CalibraHub?.toast?.('Resmi tatil eklendi.', 'ok')
        return fetchData()
      })
      .finally(function () { setBusy(false) })
  }

  function handleDeleteHoliday(id) {
    setBusy(true)
    api.deleteHoliday(id)
      .then(function (res) {
        if (!res || !res.ok) {
          window.CalibraHub?.toast?.('Silme başarısız.', 'err')
          return
        }
        window.CalibraHub?.toast?.('Resmi tatil silindi.', 'ok')
        return fetchData()
      })
      .finally(function () { setBusy(false) })
  }

  if (loading) {
    return (
      <div className="mcal-root">
        <div className="mcal-loading">
          <Loader2 size={20} className="mcal-spin" /> Takvim yükleniyor...
        </div>
      </div>
    )
  }

  return (
    <div className="mcal-root">
      <div className="mcal-header">
        <CalendarClock size={18} />
        <div>
          <div className="mcal-header__title">Makine Çalışma Takvimi</div>
          <div className="mcal-header__subtitle">Haftalık çalışma pencereleri ve resmi tatiller — Makine Planlama Gantt'ında gölgeleme için kullanılır.</div>
        </div>
      </div>

      <div className="mcal-body">
        <section className="mcal-section">
          <div className="mcal-section__title"><Clock3 size={14} /> Haftalık Çalışma Penceresi</div>
          <WorkWindowPanel
            machines={machines}
            windows={windows}
            onAdd={handleAddWindow}
            onDelete={handleDeleteWindow}
            busy={busy}
          />
        </section>

        <section className="mcal-section mcal-section--holidays">
          <div className="mcal-section__title"><CalendarOff size={14} /> Resmi Tatiller</div>
          <HolidayPanel
            holidays={holidays}
            onAdd={handleAddHoliday}
            onDelete={handleDeleteHoliday}
            busy={busy}
          />
        </section>
      </div>
    </div>
  )
}
