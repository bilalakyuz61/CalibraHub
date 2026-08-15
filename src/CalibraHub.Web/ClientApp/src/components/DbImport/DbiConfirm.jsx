import React from 'react'
import { Trash2 } from 'lucide-react'

/*
 * Silme onay modalı — CalibraHub standardı: ekranın ORTASINDA custom modal.
 * Native confirm()/alert() kullanılmaz (sayfanın üstünde çıkar, tema uyumsuz).
 * Esc / backdrop → iptal, Enter → onay, onay butonu varsayılan focus'ta.
 */
export default function DbiConfirm({ open, title, message, okLabel = 'Sil', onCancel, onConfirm }) {
  const okRef = React.useRef(null)

  React.useEffect(() => {
    if (!open) return undefined
    const t = setTimeout(() => { if (okRef.current) okRef.current.focus() }, 0)
    function onKey(e) {
      if (e.key === 'Escape') { e.preventDefault(); onCancel() }
      else if (e.key === 'Enter') { e.preventDefault(); onConfirm() }
    }
    document.addEventListener('keydown', onKey)
    return () => { clearTimeout(t); document.removeEventListener('keydown', onKey) }
  }, [open, onCancel, onConfirm])

  if (!open) return null

  return (
    <div className="dbi-modal-backdrop" onMouseDown={(e) => { if (e.target === e.currentTarget) onCancel() }}>
      <div className="dbi-modal dbi-modal--sm" role="dialog" aria-modal="true">
        <div className="dbi-modal-body">
          <div className="dbi-confirm-icon"><Trash2 size={20} /></div>
          <div className="dbi-confirm-title">{title}</div>
          <div className="dbi-confirm-msg">{message}</div>
        </div>
        <div className="dbi-modal-foot">
          <button type="button" className="dbi-btn dbi-btn--ghost" onClick={onCancel}>Vazgeç</button>
          <button type="button" ref={okRef} className="dbi-btn dbi-btn--danger" onClick={onConfirm}>{okLabel}</button>
        </div>
      </div>
    </div>
  )
}
