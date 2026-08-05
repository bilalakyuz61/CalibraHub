import { useCallback, useEffect, useState } from 'react'
import { Loader2, CalendarClock, LayoutGrid, CalendarOff } from 'lucide-react'
import * as api from '../../services/machineCalendarService'
import ScenarioBar from './ScenarioBar'
import ScenarioFormModal from './ScenarioFormModal'
import ScenarioShiftMatrix from './ScenarioShiftMatrix'
import HolidayPanel from './HolidayPanel'
import './MachineCalendar.css'

/**
 * MachineCalendar — "Vardiya Senaryoları" admin ekranı (Faz 1).
 * Bölümler: (1) senaryo yönetimi + seçili senaryo için makine × vardiya × gün atama
 * matrisi, (2) resmi tatil listesi (global, senaryodan bağımsız). Makine saatleri artık
 * serbest pencere yerine vardiya bloklarından türer — Makine Planlama Gantt'ı ve otomatik
 * çizelgeleme seçilen senaryoya göre çalışır (scenarioId parametresi).
 *
 * Not: eski serbest-pencere editörü (WorkWindowPanel / MachineWorkWindow) bu ekrandan
 * kaldırıldı. Boş senaryoda backend otomatik legacy MachineWorkWindow fallback okur —
 * sıfır veri kaybı, kademeli geçiş (bkz. plan Faz 1.4). Legacy `windows` alanı burada
 * kullanılmıyor.
 */
export default function MachineCalendar() {
  var [loading, setLoading] = useState(true)
  var [busy, setBusy] = useState(false)
  var [machines, setMachines] = useState([])
  var [holidays, setHolidays] = useState([])
  var [scenarios, setScenarios] = useState([])
  var [shifts, setShifts] = useState([])
  var [selectedScenarioId, setSelectedScenarioId] = useState(null)
  var [assignments, setAssignments] = useState([])
  var [matrixLoading, setMatrixLoading] = useState(false)
  var [formModal, setFormModal] = useState(null) // null | { mode:'create' } | { mode:'rename', scenario }
  var [savingScenario, setSavingScenario] = useState(false)

  var fetchData = useCallback(function () {
    return api.getCalendarData()
      .then(function (res) {
        if (!res || !res.ok) {
          window.CalibraHub?.toast?.('Takvim verisi yüklenemedi.', 'err')
          return
        }
        setMachines(res.machines || [])
        setHolidays(res.holidays || [])
        setShifts(res.shifts || [])
        var list = res.scenarios || []
        setScenarios(list)
        setSelectedScenarioId(function (prev) {
          if (prev && list.some(function (s) { return s.id === prev })) return prev
          var def = list.find(function (s) { return s.isDefault })
          return def ? def.id : (list.length > 0 ? list[0].id : null)
        })
      })
      .catch(function (e) {
        console.error('[MachineCalendar] fetch error:', e)
        window.CalibraHub?.toast?.('Takvim verisi yüklenirken hata oluştu.', 'err')
      })
      .finally(function () { setLoading(false) })
  }, [])

  useEffect(function () { fetchData() }, [fetchData])

  var fetchAssignments = useCallback(function (scenarioId) {
    if (!scenarioId) { setAssignments([]); return Promise.resolve() }
    setMatrixLoading(true)
    return api.listScenarioMachineShifts(scenarioId)
      .then(function (res) {
        if (!res || !res.ok) {
          window.CalibraHub?.toast?.('Vardiya ataması yüklenemedi.', 'err')
          return
        }
        setAssignments(res.items || [])
      })
      .catch(function (e) {
        console.error('[MachineCalendar] assignments fetch error:', e)
        window.CalibraHub?.toast?.('Vardiya ataması yüklenirken hata oluştu.', 'err')
      })
      .finally(function () { setMatrixLoading(false) })
  }, [])

  useEffect(function () {
    if (selectedScenarioId) fetchAssignments(selectedScenarioId)
    else setAssignments([])
  }, [selectedScenarioId, fetchAssignments])

  // ── Senaryo CRUD ──────────────────────────────────────────────
  function handleSaveScenarioForm(payload) {
    setSavingScenario(true)
    api.saveScenario(payload)
      .then(function (res) {
        if (!res || !res.ok) {
          window.CalibraHub?.toast?.((res && res.error) || 'Kaydetme başarısız.', 'err')
          return
        }
        window.CalibraHub?.toast?.(payload.id ? 'Senaryo güncellendi.' : 'Senaryo oluşturuldu.', 'ok')
        setFormModal(null)
        if (!payload.id && res.id) setSelectedScenarioId(res.id)
        return fetchData()
      })
      .finally(function () { setSavingScenario(false) })
  }

  function handleSetDefault(scenario) {
    setBusy(true)
    api.saveScenario({ id: scenario.id, name: scenario.name, description: scenario.description || null, isDefault: true })
      .then(function (res) {
        if (!res || !res.ok) {
          window.CalibraHub?.toast?.((res && res.error) || 'İşlem başarısız.', 'err')
          return
        }
        window.CalibraHub?.toast?.('Varsayılan senaryo güncellendi.', 'ok')
        return fetchData()
      })
      .finally(function () { setBusy(false) })
  }

  async function handleDeleteScenario(scenario) {
    var ok = await window.showConfirm({
      title: 'Senaryoyu Sil',
      message: '"' + scenario.name + '" senaryosunu silmek istediğinize emin misiniz? Bu senaryoya ait vardiya atamaları da silinir.',
      okLabel: 'Evet, Sil',
      danger: true,
    })
    if (!ok) return
    setBusy(true)
    api.deleteScenario(scenario.id)
      .then(function (res) {
        if (!res || !res.ok) {
          window.CalibraHub?.toast?.((res && res.error) || 'Silme başarısız.', 'err')
          return
        }
        window.CalibraHub?.toast?.('Senaryo silindi.', 'ok')
        if (selectedScenarioId === scenario.id) setSelectedScenarioId(null)
        return fetchData()
      })
      .finally(function () { setBusy(false) })
  }

  // ── Makine × Vardiya × Gün matrisi ─────────────────────────────
  function handleSaveAssignment(payload) {
    setBusy(true)
    api.saveScenarioMachineShift(payload)
      .then(function (res) {
        if (!res || !res.ok) {
          window.CalibraHub?.toast?.((res && res.error) || 'Kaydetme başarısız.', 'err')
          return
        }
        return fetchAssignments(selectedScenarioId)
      })
      .finally(function () { setBusy(false) })
  }

  function handleDeleteAssignment(id) {
    setBusy(true)
    api.deleteScenarioMachineShift(id)
      .then(function (res) {
        if (!res || !res.ok) {
          window.CalibraHub?.toast?.('Silme başarısız.', 'err')
          return
        }
        window.CalibraHub?.toast?.('Vardiya ataması kaldırıldı.', 'ok')
        return fetchAssignments(selectedScenarioId)
      })
      .finally(function () { setBusy(false) })
  }

  // ── Resmi Tatiller (global, senaryodan bağımsız) ────────────────
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
          <div className="mcal-header__title">Vardiya Senaryoları</div>
          <div className="mcal-header__subtitle">Çalışma senaryoları, makine × vardiya × gün ataması ve resmi tatiller — Makine Planlama Gantt'ında ve otomatik çizelgelemede kullanılır.</div>
        </div>
      </div>

      <div className="mcal-body">
        <section className="mcal-section">
          <div className="mcal-section__title"><LayoutGrid size={14} /> Senaryo ve Vardiya Ataması</div>

          <ScenarioBar
            scenarios={scenarios}
            selectedScenarioId={selectedScenarioId}
            onSelect={setSelectedScenarioId}
            onCreate={function () { setFormModal({ mode: 'create' }) }}
            onRename={function (s) { setFormModal({ mode: 'rename', scenario: s }) }}
            onDelete={handleDeleteScenario}
            onSetDefault={handleSetDefault}
            busy={busy}
          />

          {scenarios.length === 0 ? (
            <div className="mcal-empty">Henüz senaryo tanımlanmamış.</div>
          ) : matrixLoading ? (
            <div className="mcal-loading mcal-loading--inline">
              <Loader2 size={16} className="mcal-spin" /> Vardiya ataması yükleniyor...
            </div>
          ) : (
            <ScenarioShiftMatrix
              machines={machines}
              shifts={shifts}
              assignments={assignments}
              scenarioId={selectedScenarioId}
              onSave={handleSaveAssignment}
              onDelete={handleDeleteAssignment}
              busy={busy}
            />
          )}
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

      {formModal && (
        <ScenarioFormModal
          initial={formModal.mode === 'rename' ? formModal.scenario : null}
          onClose={function () { setFormModal(null) }}
          onSave={handleSaveScenarioForm}
          saving={savingScenario}
        />
      )}
    </div>
  )
}
