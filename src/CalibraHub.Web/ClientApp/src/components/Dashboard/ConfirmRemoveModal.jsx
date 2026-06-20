/**
 * ConfirmRemoveModal — Ekran ortasinda custom onay modali (CLAUDE.md silme
 * onay standardi). Browser native confirm() KULLANILMAZ.
 *
 * - Tam ekran backdrop (yari saydam koyu, blur)
 * - Ortalanmis card: danger ikon + baslik + mesaj
 * - Iki buton: Vazgeç (ghost) + onay (danger varsayilan, primary opsiyonel)
 * - Esc / backdrop → iptal, Enter → onay
 * - Onay butonu varsayilan focus
 *
 * Props:
 *   { open, title, message, okLabel, variant ('danger'|'primary'),
 *     onConfirm, onCancel }
 */
import { useEffect, useRef } from 'react'
import { createPortal } from 'react-dom'
import { AlertTriangle, Trash2 } from 'lucide-react'

export default function ConfirmRemoveModal(props) {
  var open = props.open
  var okBtnRef = useRef(null)
  var variant = props.variant || 'danger'

  useEffect(function () {
    if (!open) return undefined
    function onKey(e) {
      if (e.key === 'Escape') { e.preventDefault(); if (props.onCancel) props.onCancel() }
      else if (e.key === 'Enter') { e.preventDefault(); if (props.onConfirm) props.onConfirm() }
    }
    document.addEventListener('keydown', onKey)
    // Onay butonuna focus
    var t = setTimeout(function () { if (okBtnRef.current) okBtnRef.current.focus() }, 20)
    return function () {
      document.removeEventListener('keydown', onKey)
      clearTimeout(t)
    }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open])

  if (!open) return null

  var Icon = variant === 'danger' ? Trash2 : AlertTriangle
  var iconBg = variant === 'danger' ? 'rgba(244,63,94,0.14)' : 'rgba(99,102,241,0.14)'
  var iconColor = variant === 'danger' ? '#e11d48' : '#4f46e5'

  return createPortal(
    <div
      className="dash-modal-backdrop"
      onClick={function () { if (props.onCancel) props.onCancel() }}
    >
      <div
        className="dash-modal dash-modal--sm"
        onClick={function (e) { e.stopPropagation() }}
        role="dialog"
        aria-modal="true"
      >
        <div className="dash-modal__body" style={{ textAlign: 'center', paddingTop: 26, paddingBottom: 8 }}>
          <div
            style={{
              width: 56, height: 56, borderRadius: '50%', margin: '0 auto 14px',
              display: 'flex', alignItems: 'center', justifyContent: 'center',
              background: iconBg,
            }}
          >
            <Icon size={28} strokeWidth={2.2} style={{ color: iconColor }} />
          </div>
          <h3 style={{ fontSize: 16, fontWeight: 700, margin: '0 0 6px' }} className="dash-row__title">
            {props.title || 'Emin misiniz?'}
          </h3>
          {props.message && (
            <p className="dash-row__sub" style={{ fontSize: 13, whiteSpace: 'normal', lineHeight: 1.5 }}>
              {props.message}
            </p>
          )}
        </div>
        <div className="dash-modal__footer" style={{ justifyContent: 'center', borderTop: 'none', paddingTop: 4 }}>
          <button type="button" className="dash-btn dash-btn--ghost" onClick={props.onCancel}>
            Vazgeç
          </button>
          <button
            ref={okBtnRef}
            type="button"
            className={'dash-btn ' + (variant === 'danger' ? 'dash-btn--danger' : 'dash-btn--primary')}
            onClick={props.onConfirm}
          >
            {props.okLabel || 'Sil'}
          </button>
        </div>
      </div>
    </div>,
    document.body
  )
}
