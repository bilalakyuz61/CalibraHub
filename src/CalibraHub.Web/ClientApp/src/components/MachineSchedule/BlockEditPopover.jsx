import { useEffect, useRef, useState } from 'react'
import { X, Save, Trash2 } from 'lucide-react'
import { BLOCK_TYPE_LABELS, STATUS_LABELS } from './ganttPalette'
import { formatHm } from './timeScale'

/**
 * BlockEditPopover — bloğa tıklanınca açılan küçük düzenleme paneli.
 * Silme, CLAUDE.md "silme onay standardı"na göre önce window.showConfirm()
 * (ekran-ortası custom modal) ile onaylanır — native confirm() kullanılmaz.
 */
export default function BlockEditPopover({ block, clientX, clientY, onClose, onSave, onDelete }) {
  var [blockType, setBlockType] = useState(block.blockType)
  var [status, setStatus] = useState(block.status)
  var [notes, setNotes] = useState(block.notes || '')
  var [saving, setSaving] = useState(false)
  var ref = useRef(null)

  useEffect(function () {
    function onKey(e) { if (e.key === 'Escape') onClose() }
    document.addEventListener('keydown', onKey)
    return function () { document.removeEventListener('keydown', onKey) }
  }, [onClose])

  var style = useComputedStyle(clientX, clientY)

  function handleSave() {
    setSaving(true)
    onSave({ ...block, blockType: Number(blockType), status: Number(status), notes: notes || null })
      .finally(function () { setSaving(false) })
  }

  async function handleDelete() {
    var label = (block.workOrderNo ? block.workOrderNo + ' — ' : '') + (block.operationName || 'Blok')
    var ok = await window.showConfirm({
      title: 'Planı Sil',
      message: `"${label}" planlama bloğunu silmek istediğinize emin misiniz?`,
      okLabel: 'Evet, Sil',
    })
    if (!ok) return
    setSaving(true)
    onDelete(block).finally(function () { setSaving(false) })
  }

  var start = new Date(block.startUtc)
  var end = new Date(block.endUtc)

  return (
    <>
      <div className="ms-popover-backdrop" onClick={onClose} />
      <div className="ms-popover" ref={ref} style={style}>
        <div className="ms-popover-title">
          {block.workOrderNo || 'Blok'}
          <small>{formatHm(start)}–{formatHm(end)}</small>
          <button className="ms-popover-close" onClick={onClose}><X size={15} /></button>
        </div>

        {block.operationName && (
          <div className="ms-field">
            <label>Operasyon</label>
            <div style={{ fontSize: 12.5 }}>{block.operationName}{block.itemName ? ' · ' + block.itemName : ''}</div>
          </div>
        )}

        <div className="ms-field">
          <label>Blok Türü</label>
          <select value={blockType} onChange={function (e) { setBlockType(e.target.value) }}>
            {Object.keys(BLOCK_TYPE_LABELS).map(function (k) {
              return <option key={k} value={k}>{BLOCK_TYPE_LABELS[k]}</option>
            })}
          </select>
        </div>

        <div className="ms-field">
          <label>Durum</label>
          <select value={status} onChange={function (e) { setStatus(e.target.value) }}>
            {Object.keys(STATUS_LABELS).map(function (k) {
              return <option key={k} value={k}>{STATUS_LABELS[k]}</option>
            })}
          </select>
        </div>

        <div className="ms-field">
          <label>Not</label>
          <textarea rows={2} value={notes} onChange={function (e) { setNotes(e.target.value) }} />
        </div>

        <div className="ms-popover-actions">
          <button className="ms-btn ms-btn--danger" onClick={handleDelete} disabled={saving} title="Sil">
            <Trash2 size={14} />
          </button>
          <button className="ms-btn ms-btn--secondary" onClick={onClose} disabled={saving}>Vazgeç</button>
          <button className="ms-btn ms-btn--primary" onClick={handleSave} disabled={saving}>
            <Save size={14} /> Kaydet
          </button>
        </div>
      </div>
    </>
  )
}

function useComputedStyle(clientX, clientY) {
  var w = 268
  var h = 320
  var vw = typeof window !== 'undefined' ? window.innerWidth : 1200
  var vh = typeof window !== 'undefined' ? window.innerHeight : 800
  var left = Math.min(Math.max(8, clientX - w / 2), vw - w - 8)
  var top = Math.min(Math.max(8, clientY + 12), vh - h - 8)
  return { left: left, top: top }
}
