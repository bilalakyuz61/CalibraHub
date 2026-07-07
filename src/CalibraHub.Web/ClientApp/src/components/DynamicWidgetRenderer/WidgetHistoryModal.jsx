/**
 * WidgetHistoryModal — Alan bazli degisiklik gecmisi (audit) goruntuleyici.
 *
 * GET /api/widgets/forms/{formCode}/records/{recordId}/history endpoint'inden
 * WidgetValueLogDto[] ceker ve yeni→eski sirali listeler: hangi alan, eski
 * deger → yeni deger, kim, ne zaman. Grid kalemi degisikliklerinde childRecordId
 * chip'i gosterilir.
 *
 * Tema: widgetField.css icindeki --wfh-* token'lari (light default + dark
 * override). ESC veya backdrop tiklamasiyla kapanir.
 *
 * Props:
 *   isOpen    boolean
 *   onClose   fn
 *   formCode  string — 'ITEMS', 'CONTACTS', ...
 *   recordId  string — business key
 */
import { useState, useEffect } from 'react'
import { createPortal } from 'react-dom'
import { History, X } from 'lucide-react'
import { getRecordHistory } from './dynamicWidgetService'

/* Ham degeri okunur metne cevir: boolean → Evet/Hayir, JSON array → virgullu,
   null/bos → em-dash. Uzun degerler CSS ellipsis ile kirpilir (title'da tam hali). */
function formatValue(raw) {
  if (raw == null || raw === '') return '—'
  var s = String(raw)
  if (s === 'true') return 'Evet'
  if (s === 'false') return 'Hayır'
  if (s.length > 1 && s[0] === '[') {
    try {
      var arr = JSON.parse(s)
      if (Array.isArray(arr)) return arr.join(', ')
    } catch (e) { /* JSON degil — ham goster */ }
  }
  return s
}

function formatWhen(iso) {
  if (!iso) return ''
  try {
    var d = new Date(iso)
    if (isNaN(d.getTime())) return String(iso)
    return d.toLocaleDateString('tr-TR', { day: '2-digit', month: '2-digit', year: 'numeric' }) +
      ' ' + d.toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' })
  } catch (e) { return String(iso) }
}

export default function WidgetHistoryModal(props) {
  var isOpen   = props.isOpen
  var onClose  = props.onClose
  var formCode = props.formCode
  var recordId = props.recordId

  var [rows, setRows]       = useState([])
  var [loading, setLoading] = useState(false)
  var [error, setError]     = useState(null)

  // Acilista gecmisi cek
  useEffect(function () {
    if (!isOpen) return undefined
    var cancelled = false
    setLoading(true)
    setError(null)
    getRecordHistory(formCode, recordId)
      .then(function (list) { if (!cancelled) setRows(list) })
      .catch(function (e) { if (!cancelled) setError(e.message || 'Geçmiş yüklenemedi') })
      .finally(function () { if (!cancelled) setLoading(false) })
    return function () { cancelled = true }
  }, [isOpen, formCode, recordId])

  // ESC ile kapat
  useEffect(function () {
    if (!isOpen) return undefined
    function onKey(e) { if (e.key === 'Escape') onClose && onClose() }
    document.addEventListener('keydown', onKey)
    return function () { document.removeEventListener('keydown', onKey) }
  }, [isOpen, onClose])

  if (!isOpen) return null

  return createPortal(
    <div className="wf-history-backdrop" onClick={function () { onClose && onClose() }}>
      <div className="wf-history-card" onClick={function (e) { e.stopPropagation() }} role="dialog" aria-modal="true">
        <div className="wf-history-head">
          <History size={17} style={{ color: 'var(--wfh-chip-text)', flexShrink: 0 }} />
          <div className="wf-history-head-title">
            Değişiklik Geçmişi
            <div className="wf-history-head-sub">{recordId}</div>
          </div>
          <button type="button" className="wf-history-close" onClick={onClose} aria-label="Kapat">
            <X size={15} />
          </button>
        </div>
        <div className="wf-history-body">
          {loading && <div className="wf-history-empty">Yükleniyor…</div>}
          {!loading && error && <div className="wf-history-empty">{error}</div>}
          {!loading && !error && rows.length === 0 && (
            <div className="wf-history-empty">
              Bu kayıt için henüz değişiklik geçmişi yok.
              <br />
              <span style={{ fontSize: 11 }}>Ek alanlarda yapılan her değişiklik bundan sonra burada listelenecek.</span>
            </div>
          )}
          {!loading && !error && rows.map(function (r) {
            var oldText = formatValue(r.oldValue)
            var newText = formatValue(r.newValue)
            return (
              <div key={r.id} className="wf-history-row">
                <div className="wf-history-row-top">
                  <span className="wf-history-label">{r.label || r.widgetCode}</span>
                  {r.childRecordId && (
                    <span className="wf-history-chip" title="Kalem satırı değişikliği">{r.childRecordId}</span>
                  )}
                  <span className="wf-history-meta">
                    {r.changedBy ? r.changedBy + ' · ' : ''}{formatWhen(r.changedAt)}
                  </span>
                </div>
                <div className="wf-history-values">
                  <span className="wf-history-old" title={oldText}>{oldText}</span>
                  <span className="wf-history-arrow">→</span>
                  <span className="wf-history-new" title={newText}>{newText}</span>
                </div>
              </div>
            )
          })}
        </div>
      </div>
    </div>,
    document.body
  )
}
