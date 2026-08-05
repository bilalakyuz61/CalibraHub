import { useEffect, useState } from 'react'
import { X, Wand2, Loader2, CheckSquare, Square, AlertTriangle, Check, ArrowLeft } from 'lucide-react'
import * as api from '../../services/machineScheduleService'
import './AutoScheduleWizard.css'

function formatDateLocal(iso) {
  if (!iso) return '-'
  var d = new Date(iso)
  return d.toLocaleDateString('tr-TR', { day: '2-digit', month: 'short', year: 'numeric' })
}

function formatMinutes(total) {
  if (!total) return '-'
  var h = Math.floor(total / 60)
  var m = Math.round(total % 60)
  return h > 0 ? h + 's ' + m + 'dk' : m + 'dk'
}

/**
 * AutoScheduleWizard — Faz 3 "Otomatik Yerleştir" sihirbazı.
 * Adım 1: aday iş emri seçimi (opt-out, varsayılan hepsi seçili).
 * Adım 2: önizleme (öneriler Gantt'ta ghost blok olarak overlay edilir — bkz.
 * GanttBoard previewProposals prop'u; bu bileşen sadece listeyi yönetir).
 * Persist YOK — yalnız "Uygula" backend'e aynı girdiyi POST eder.
 */
export default function AutoScheduleWizard({ fromUtc, scenarioId, scenarioName, onClose, onApplied, onPreviewChange }) {
  var [step, setStep] = useState(1)
  var [loadingCandidates, setLoadingCandidates] = useState(true)
  var [candidates, setCandidates] = useState([])
  var [selectedIds, setSelectedIds] = useState(function () { return new Set() })
  var [computing, setComputing] = useState(false)
  var [applying, setApplying] = useState(false)
  var [proposals, setProposals] = useState([])
  var [unplaceable, setUnplaceable] = useState([])

  useEffect(function () {
    api.getAutoScheduleCandidates()
      .then(function (res) {
        if (!res || !res.ok) {
          window.CalibraHub?.toast?.('Aday iş emirleri yüklenemedi.', 'err')
          setCandidates([])
          return
        }
        var list = res.workOrders || []
        setCandidates(list)
        setSelectedIds(new Set(list.map(function (w) { return w.workOrderId })))
      })
      .catch(function (e) {
        console.error('[AutoScheduleWizard] candidates error:', e)
        window.CalibraHub?.toast?.('Aday iş emirleri yüklenirken hata oluştu.', 'err')
      })
      .finally(function () { setLoadingCandidates(false) })
  }, [])

  useEffect(function () {
    function onKey(e) { if (e.key === 'Escape') handleClose() }
    document.addEventListener('keydown', onKey)
    return function () { document.removeEventListener('keydown', onKey) }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  function toggle(id) {
    setSelectedIds(function (prev) {
      var next = new Set(prev)
      if (next.has(id)) next.delete(id); else next.add(id)
      return next
    })
  }

  function toggleAll() {
    setSelectedIds(function (prev) {
      if (candidates.length > 0 && prev.size === candidates.length) return new Set()
      return new Set(candidates.map(function (w) { return w.workOrderId }))
    })
  }

  function handleCompute() {
    var includedWorkOrderIds = Array.from(selectedIds)
    if (includedWorkOrderIds.length === 0) {
      window.CalibraHub?.toast?.('En az bir iş emri seçin.', 'warn')
      return
    }
    setComputing(true)
    api.autoSchedulePreview({ includedWorkOrderIds: includedWorkOrderIds, fromUtc: fromUtc, scenarioId: scenarioId })
      .then(function (res) {
        if (!res || !res.ok) {
          window.CalibraHub?.toast?.('Öneri hesaplanamadı.', 'err')
          return
        }
        var props = res.proposals || []
        setProposals(props)
        setUnplaceable(res.unplaceable || [])
        onPreviewChange(props)
        setStep(2)
      })
      .catch(function (e) {
        console.error('[AutoScheduleWizard] preview error:', e)
        window.CalibraHub?.toast?.('Öneri hesaplanırken hata oluştu.', 'err')
      })
      .finally(function () { setComputing(false) })
  }

  function handleApply() {
    var includedWorkOrderIds = Array.from(selectedIds)
    setApplying(true)
    api.autoScheduleApply({ includedWorkOrderIds: includedWorkOrderIds, fromUtc: fromUtc, scenarioId: scenarioId })
      .then(function (res) {
        if (!res || !res.ok) {
          window.CalibraHub?.toast?.('Otomatik yerleştirme uygulanamadı.', 'err')
          return
        }
        var msg = res.createdCount + ' plan oluşturuldu' +
          (res.unplaceableCount ? ', ' + res.unplaceableCount + ' operasyon yerleştirilemedi.' : '.')
        window.CalibraHub?.toast?.(msg, res.unplaceableCount ? 'warn' : 'ok')
        onPreviewChange([])
        onApplied()
      })
      .catch(function (e) {
        console.error('[AutoScheduleWizard] apply error:', e)
        window.CalibraHub?.toast?.('Otomatik yerleştirme sırasında hata oluştu.', 'err')
      })
      .finally(function () { setApplying(false) })
  }

  function handleBackToStep1() {
    setProposals([])
    setUnplaceable([])
    onPreviewChange([])
    setStep(1)
  }

  function handleClose() {
    onPreviewChange([])
    onClose()
  }

  var allSelected = candidates.length > 0 && selectedIds.size === candidates.length

  return (
    <>
      <div className="asw-backdrop" onClick={handleClose} />
      <div className="asw-modal" role="dialog" aria-modal="true">
        <div className="asw-header">
          <div className="asw-title"><Wand2 size={17} /> Otomatik Yerleştir</div>
          <button className="asw-close" onClick={handleClose} title="Kapat"><X size={16} /></button>
        </div>

        <div className="asw-steps">
          <span className={'asw-step' + (step === 1 ? ' asw-step--active' : '')}>1. Aday Seçimi</span>
          <span className={'asw-step' + (step === 2 ? ' asw-step--active' : '')}>2. Önizleme</span>
          {scenarioName && <span className="asw-scenario-badge">{scenarioName} senaryosuna göre planlanacak</span>}
        </div>

        {step === 1 && (
          <div className="asw-body">
            {loadingCandidates ? (
              <div className="asw-loading"><Loader2 size={18} className="asw-spin" /> Aday iş emirleri yükleniyor...</div>
            ) : candidates.length === 0 ? (
              <div className="asw-empty">Planlanacak açık operasyonu olan iş emri bulunamadı.</div>
            ) : (
              <>
                <div className="asw-toolbar">
                  <button className="asw-link-btn" onClick={toggleAll}>
                    {allSelected ? <CheckSquare size={14} /> : <Square size={14} />}
                    {allSelected ? 'Tümünü Kaldır' : 'Tümünü Seç'}
                  </button>
                  <span className="asw-count">{selectedIds.size} / {candidates.length} seçili</span>
                </div>
                <div className="asw-list">
                  {candidates.map(function (w) {
                    var checked = selectedIds.has(w.workOrderId)
                    return (
                      <label key={w.workOrderId} className={'asw-row' + (checked ? ' asw-row--checked' : '')}>
                        <input type="checkbox" checked={checked} onChange={function () { toggle(w.workOrderId) }} />
                        <div className="asw-row-main">
                          <div className="asw-row-title">
                            <span className="asw-wo">{w.workOrderNo}</span>
                            <span className="asw-item" title={w.itemName}>{w.itemName}</span>
                          </div>
                          <div className="asw-row-meta">
                            <span>Öncelik: {w.priority}</span>
                            <span>Termin: {formatDateLocal(w.plannedEndDate)}</span>
                            <span>{w.unplannedOpCount} operasyon</span>
                            <span>{formatMinutes(w.totalMinutes)}</span>
                          </div>
                        </div>
                      </label>
                    )
                  })}
                </div>
              </>
            )}
          </div>
        )}

        {step === 2 && (
          <div className="asw-body">
            <div className="asw-summary">
              <span className="asw-summary-ok"><Check size={13} /> {proposals.length} operasyon için öneri hesaplandı.</span>
              {unplaceable.length > 0 && (
                <span className="asw-summary-warn"><AlertTriangle size={13} /> {unplaceable.length} operasyon yerleştirilemedi.</span>
              )}
            </div>
            <div className="asw-hint">Öneriler Gantt üzerinde kesikli çerçeveli "ÖNERİ" rozetiyle işaretlendi. Uygulamak için aşağıdan "Uygula"ya basın.</div>
            {unplaceable.length > 0 && (
              <div className="asw-unplaceable-list">
                {unplaceable.map(function (u, i) {
                  return (
                    <div key={i} className="asw-unplaceable-row">
                      <AlertTriangle size={13} />
                      <span className="asw-wo">{u.workOrderNo}</span>
                      <span className="asw-item">{u.operationName}</span>
                      <span className="asw-reason">{u.reason}</span>
                    </div>
                  )
                })}
              </div>
            )}
          </div>
        )}

        <div className="asw-footer">
          {step === 1 && (
            <>
              <button className="asw-btn asw-btn--secondary" onClick={handleClose}>Vazgeç</button>
              <button
                className="asw-btn asw-btn--primary"
                onClick={handleCompute}
                disabled={computing || loadingCandidates || selectedIds.size === 0}
              >
                {computing ? <Loader2 size={14} className="asw-spin" /> : <Wand2 size={14} />}
                Öneri Hesapla
              </button>
            </>
          )}
          {step === 2 && (
            <>
              <button className="asw-btn asw-btn--secondary" onClick={handleBackToStep1} disabled={applying}>
                <ArrowLeft size={14} /> Geri
              </button>
              <button className="asw-btn asw-btn--ghost" onClick={handleClose} disabled={applying}>Vazgeç</button>
              <button className="asw-btn asw-btn--primary" onClick={handleApply} disabled={applying || proposals.length === 0}>
                {applying ? <Loader2 size={14} className="asw-spin" /> : <Check size={14} />}
                Uygula
              </button>
            </>
          )}
        </div>
      </div>
    </>
  )
}
