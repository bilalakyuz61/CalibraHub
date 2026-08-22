import { CalendarSync, Loader2, AlertTriangle, Check, X } from 'lucide-react'

/**
 * ReschedulePanel — "Yeniden Çizelgele" önizleme çubuğu.
 * Gantt panelinin üstünde sticky bar olarak gösterilir; GanttBoard'daki
 * previewProposals (Faz 3 ghost overlay mekanizması) ile aynı katmanı paylaşır —
 * bu bileşen yalnız özet metni + Uygula/Vazgeç aksiyonlarını taşır.
 * Vazgeç destrüktif değildir (yalnız ghost temizlenir, hiçbir şey persist edilmez)
 * → native confirm YOK, doğrudan iptal.
 */
export default function ReschedulePanel({ loading, result, applying, onApply, onCancel }) {
  if (loading) {
    return (
      <div className="ms-reschedule-bar">
        <Loader2 size={14} className="ms-spin ms-reschedule-icon" />
        <span className="ms-reschedule-loading-text">Yeniden çizelgeleme önizlemesi hesaplanıyor...</span>
      </div>
    )
  }

  if (!result) return null

  var proposals = result.proposals || []
  var unplaceable = result.unplaceable || []
  var releasedCount = result.releasedCount || 0

  return (
    <div className="ms-reschedule-bar">
      <CalendarSync size={14} className="ms-reschedule-icon" />
      <div className="ms-reschedule-summary">
        <span className="ms-reschedule-stat">{proposals.length} blok yerleştirilecek</span>
        <span className="ms-reschedule-stat">{releasedCount} blok serbest bırakıldı</span>
        {unplaceable.length > 0 && (
          <span className="ms-reschedule-stat ms-reschedule-stat--warn">
            <AlertTriangle size={12} /> {unplaceable.length} operasyon yerleşemedi
          </span>
        )}
      </div>

      {unplaceable.length > 0 && (
        <div
          className="ms-reschedule-unplaceable"
          title={unplaceable.map(function (u) { return u.workOrderNo + ' · ' + u.operationName + ': ' + u.reason }).join('\n')}
        >
          {unplaceable.slice(0, 3).map(function (u, i) {
            return <span key={i} className="ms-reschedule-unplaceable-item">{u.workOrderNo} · {u.reason}</span>
          })}
          {unplaceable.length > 3 && (
            <span className="ms-reschedule-unplaceable-more">+{unplaceable.length - 3}</span>
          )}
        </div>
      )}

      <div className="ms-reschedule-actions">
        <button className="ms-btn ms-btn--secondary ms-reschedule-btn" onClick={onCancel} disabled={applying}>
          <X size={13} /> Vazgeç
        </button>
        <button
          className="ms-btn ms-btn--primary ms-reschedule-btn"
          onClick={onApply}
          disabled={applying || proposals.length === 0}
        >
          {applying ? <Loader2 size={13} className="ms-spin" /> : <Check size={13} />} Uygula
        </button>
      </div>
    </div>
  )
}
