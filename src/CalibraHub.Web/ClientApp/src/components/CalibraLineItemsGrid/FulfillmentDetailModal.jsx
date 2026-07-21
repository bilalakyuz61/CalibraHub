/**
 * FulfillmentDetailModal — Karsilama Detayi (PageComment Seq 18, 2026-07-21)
 *
 * Ihtiyac Kaydi (alis_talebi) kalem grid'inin "İşlemler" (satir kisayol) menusunden acilir.
 * Secili kalemin karsilama defteri (DocumentLineFulfillment) kayitlarini — hangi belge ne
 * kadarini karsiladi, ne zaman, hala aktif mi (ters cevrilmemis mi) — salt-okunur listeler.
 *
 * Props:
 *   isOpen        : bool
 *   onClose       : fn()
 *   lineId        : number  (zorunlu — DocumentLine.Id / RequestLineId)
 *   materialCode  : string? (baslikta gosterim icin)
 *   materialName  : string? (baslikta gosterim icin)
 *
 * Backend: GET /Purchase/GetLineFulfillmentEntriesJson?lineId=  (PurchaseController, yeni —
 * kucuk salt-okunur endpoint; FormCodes.PurchaseFulfillment ile ayni yetki kapisi).
 */
import { useState, useEffect, useCallback } from 'react'
import { createPortal } from 'react-dom'
import { Layers, X, Loader2, AlertCircle } from 'lucide-react'
import './FulfillmentDetailModal.css'

function fmtQty(n) {
  if (n == null || isNaN(n)) return '0,00'
  return Number(n).toLocaleString('tr-TR', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
}

function fmtDate(iso) {
  if (!iso) return '—'
  try {
    var d = new Date(iso)
    if (isNaN(d.getTime())) return '—'
    return d.toLocaleDateString('tr-TR')
  } catch (e) { return '—' }
}

export default function FulfillmentDetailModal(props) {
  var isOpen       = !!props.isOpen
  var onClose      = props.onClose || function () {}
  var lineId       = props.lineId || null
  var materialCode = props.materialCode || ''
  var materialName = props.materialName || ''

  var [entries, setEntries] = useState([])
  var [loading, setLoading] = useState(false)
  var [error, setError]     = useState(null)

  var fetchEntries = useCallback(function () {
    if (!lineId) { setEntries([]); return }
    setLoading(true)
    setError(null)
    fetch('/Purchase/GetLineFulfillmentEntriesJson?lineId=' + encodeURIComponent(lineId), { credentials: 'same-origin' })
      .then(function (r) {
        if (r.status === 403) throw new Error('Bu bilgiyi görüntüleme yetkiniz yok.')
        if (!r.ok) throw new Error('HTTP ' + r.status)
        return r.json()
      })
      .then(function (d) { setEntries(Array.isArray(d) ? d : []) })
      .catch(function (e) { setError(e.message || 'Karşılama kayıtları alınamadı.'); setEntries([]) })
      .finally(function () { setLoading(false) })
  }, [lineId])

  useEffect(function () {
    if (!isOpen) return
    fetchEntries()
  }, [isOpen, fetchEntries])

  // Esc ile kapat
  useEffect(function () {
    if (!isOpen) return undefined
    function onKey(e) { if (e.key === 'Escape') onClose() }
    document.addEventListener('keydown', onKey)
    return function () { document.removeEventListener('keydown', onKey) }
  }, [isOpen, onClose])

  if (!isOpen) return null

  var activeTotal = entries
    .filter(function (e) { return e.isActive })
    .reduce(function (sum, e) { return sum + (Number(e.quantity) || 0) }, 0)

  var titleSuffix = materialCode
    ? (' — ' + materialCode + (materialName ? ' (' + materialName + ')' : ''))
    : ''

  var modalContent = (
    <div className="fdm-overlay" onClick={onClose}>
      <div className="fdm-panel" onClick={function (e) { e.stopPropagation() }}>
        {/* Header */}
        <div className="fdm-header">
          <span className="fdm-header-icon"><Layers size={16} /></span>
          <div style={{ flex: 1, minWidth: 0 }}>
            <div className="fdm-header-title">Karşılama Detayı{titleSuffix}</div>
            <div className="fdm-header-sub">Bu kalemi karşılayan belgeler ve miktarlar</div>
          </div>
          <button type="button" className="fdm-close-btn" onClick={onClose} title="Kapat">
            <X size={18} />
          </button>
        </div>

        {/* Body */}
        <div className="fdm-body">
          {loading && (
            <div className="fdm-state">
              <Loader2 size={18} className="fdm-spin" /> Karşılama kayıtları yükleniyor…
            </div>
          )}
          {!loading && error && (
            <div className="fdm-error"><AlertCircle size={14} /> {error}</div>
          )}
          {!loading && !error && entries.length === 0 && (
            <div className="fdm-state">Bu kalem için henüz karşılama kaydı yok.</div>
          )}
          {!loading && !error && entries.length > 0 && (
            <table className="fdm-table">
              <thead>
                <tr>
                  <th>Karşılayan Belge</th>
                  <th>Tür</th>
                  <th>Tarih</th>
                  <th className="fdm-col-right">Miktar</th>
                  <th>Durum</th>
                </tr>
              </thead>
              <tbody>
                {entries.map(function (e) {
                  return (
                    <tr key={e.id} className={e.isActive ? '' : 'fdm-row--inactive'}>
                      <td>
                        <div className="fdm-doc-number">{e.documentNumber || 'Taban değer'}</div>
                        {e.contactName && <div className="fdm-doc-sub">{e.contactName}</div>}
                        {e.notes && <div className="fdm-doc-sub">{e.notes}</div>}
                      </td>
                      <td>
                        <span className="fdm-badge">{e.kindLabel || '—'}</span>
                      </td>
                      <td>{fmtDate(e.documentDate || e.created)}</td>
                      <td className="fdm-col-right fdm-qty">{fmtQty(e.quantity)}</td>
                      <td>
                        {e.isActive
                          ? <span className="fdm-badge">{e.documentStatus || 'Aktif'}</span>
                          : <span className="fdm-badge fdm-badge--off">İptal Edildi</span>}
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          )}
        </div>

        {/* Footer */}
        <div className="fdm-footer">
          <span className="fdm-footer-total-label">Aktif Toplam</span>
          <span className="fdm-footer-total-value">{fmtQty(activeTotal)}</span>
          <button type="button" className="fdm-footer-btn" onClick={onClose}>Kapat</button>
        </div>
      </div>
    </div>
  )

  if (typeof document === 'undefined') return null
  return createPortal(modalContent, document.body)
}
