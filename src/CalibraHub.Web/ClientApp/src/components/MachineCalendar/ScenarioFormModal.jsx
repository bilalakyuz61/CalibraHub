import { useState } from 'react'
import { X, Save, Loader2 } from 'lucide-react'

/**
 * ScenarioFormModal — "Yeni Senaryo" / "Yeniden Adlandır" ekran-ortası custom modal
 * (Ad + Açıklama). CLAUDE.md silme onay standardındaki modal deseniyle tutarlı
 * (backdrop + ortalanmış card); native prompt() kullanılmaz.
 */
export default function ScenarioFormModal({ initial, onClose, onSave, saving }) {
  var [name, setName] = useState(initial ? initial.name : '')
  var [description, setDescription] = useState(initial && initial.description ? initial.description : '')

  function handleSave() {
    if (!name.trim()) {
      window.CalibraHub?.toast?.('Senaryo adı zorunludur.', 'err')
      return
    }
    onSave({
      id: initial ? initial.id : 0,
      name: name.trim(),
      description: description.trim() || null,
      isDefault: initial ? !!initial.isDefault : false,
    })
  }

  function onKey(e) {
    if (e.key === 'Escape') onClose()
  }

  return (
    <>
      <div className="mcal-modal-backdrop" onClick={onClose} />
      <div className="mcal-modal" role="dialog" aria-modal="true" onKeyDown={onKey}>
        <div className="mcal-modal__header">
          <div className="mcal-modal__title">{initial ? 'Senaryoyu Yeniden Adlandır' : 'Yeni Senaryo'}</div>
          <button className="mcal-modal__close" onClick={onClose} title="Kapat"><X size={16} /></button>
        </div>
        <div className="mcal-modal__body">
          <label className="mcal-label">Senaryo Adı</label>
          <input
            type="text"
            className="mcal-text-input mcal-text-input--full"
            value={name}
            autoFocus
            onChange={function (e) { setName(e.target.value) }}
            onKeyDown={function (e) { if (e.key === 'Enter') handleSave() }}
          />
          <label className="mcal-label mcal-label--block">Açıklama</label>
          <textarea
            className="mcal-textarea"
            value={description}
            rows={3}
            onChange={function (e) { setDescription(e.target.value) }}
          />
        </div>
        <div className="mcal-modal__footer">
          <button type="button" className="mcal-btn mcal-btn--ghost" disabled={saving} onClick={onClose}>Vazgeç</button>
          <button type="button" className="mcal-btn mcal-btn--primary" disabled={saving} onClick={handleSave} autoFocus={false}>
            {saving ? <Loader2 size={14} className="mcal-spin" /> : <Save size={14} />}
            Kaydet
          </button>
        </div>
      </div>
    </>
  )
}
