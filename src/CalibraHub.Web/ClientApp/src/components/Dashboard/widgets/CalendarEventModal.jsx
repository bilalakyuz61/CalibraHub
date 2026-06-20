/**
 * CalendarEventModal — Kişisel takvim etkinliği ekleme/düzenleme modali.
 * CLAUDE.md: custom modal (browser confirm() yok), toggle switch (checkbox yok).
 *
 * Props:
 *   { open, initialDate, event: CalendarEventDto|null,
 *     onSave(data), onDelete(id)|null, onClose }
 */
import { useState, useEffect, useRef } from 'react'
import { createPortal } from 'react-dom'
import { X, CalendarDays, Trash2 } from 'lucide-react'
import ConfirmRemoveModal from '../ConfirmRemoveModal'

var COLOR_OPTIONS = [
  { id: 'indigo', dot: '#6366f1', label: 'İndigo' },
  { id: 'emerald', dot: '#10b981', label: 'Yeşil' },
  { id: 'rose', dot: '#f43f5e', label: 'Kırmızı' },
  { id: 'amber', dot: '#f59e0b', label: 'Sarı' },
  { id: 'blue', dot: '#3b82f6', label: 'Mavi' },
  { id: 'violet', dot: '#8b5cf6', label: 'Mor' },
]

export default function CalendarEventModal(props) {
  var open = props.open
  var isEdit = !!(props.event && props.event.id)
  var titleRef = useRef(null)

  var [title, setTitle] = useState('')
  var [desc, setDesc] = useState('')
  var [startDate, setStartDate] = useState('')
  var [endDate, setEndDate] = useState('')
  var [isAllDay, setIsAllDay] = useState(true)
  var [startTime, setStartTime] = useState('09:00')
  var [endTime, setEndTime] = useState('10:00')
  var [color, setColor] = useState('indigo')
  var [saving, setSaving] = useState(false)
  var [error, setError] = useState(null)
  var [confirmDel, setConfirmDel] = useState(false)

  // Init on open
  useEffect(function() {
    if (!open) return
    var ev = props.event
    if (ev) {
      setTitle(ev.title || '')
      setDesc(ev.description || '')
      setStartDate(ev.startDate ? ev.startDate.substring(0, 10) : '')
      setEndDate(ev.endDate ? ev.endDate.substring(0, 10) : '')
      setIsAllDay(ev.isAllDay !== false)
      setStartTime(ev.startTime || '09:00')
      setEndTime(ev.endTime || '10:00')
      setColor(ev.color || 'indigo')
    } else {
      setTitle('')
      setDesc('')
      setStartDate(props.initialDate || '')
      setEndDate('')
      setIsAllDay(true)
      setStartTime('09:00')
      setEndTime('10:00')
      setColor('indigo')
    }
    setError(null)
    setSaving(false)
    setConfirmDel(false)
    // Focus title after paint
    setTimeout(function() { if (titleRef.current) titleRef.current.focus() }, 30)
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open])

  // Keyboard: Esc closes
  useEffect(function() {
    if (!open) return undefined
    function onKey(e) {
      if (e.key === 'Escape' && !confirmDel) { e.preventDefault(); props.onClose() }
    }
    document.addEventListener('keydown', onKey)
    return function() { document.removeEventListener('keydown', onKey) }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, confirmDel])

  if (!open) return null

  async function handleSave() {
    if (!title.trim()) { setError('Başlık zorunludur.'); return }
    if (!startDate) { setError('Başlangıç tarihi seçiniz.'); return }
    setSaving(true); setError(null)
    try {
      await props.onSave({
        id: isEdit ? props.event.id : null,
        title: title.trim(),
        description: desc.trim() || null,
        startDate: startDate,
        endDate: endDate || null,
        isAllDay: isAllDay,
        startTime: isAllDay ? null : (startTime || null),
        endTime: isAllDay ? null : (endTime || null),
        color: color,
      })
    } catch (ex) {
      setError(ex.message || 'Kayıt başarısız')
      setSaving(false)
    }
  }

  return createPortal(
    <>
      <div
        className="dash-modal-backdrop"
        onClick={function() { if (!confirmDel) props.onClose() }}
      >
        <div
          className="dash-modal"
          onClick={function(e) { e.stopPropagation() }}
          role="dialog"
          aria-modal="true"
          style={{ maxWidth: 400 }}
        >
          {/* Header */}
          <div className="dash-modal__header">
            <CalendarDays size={17} style={{ color: '#8b5cf6' }} />
            <span className="dash-modal__title">
              {isEdit ? 'Etkinliği Düzenle' : 'Yeni Etkinlik'}
            </span>
            <button type="button" className="dash-icon-btn" onClick={props.onClose} aria-label="Kapat">
              <X size={16} />
            </button>
          </div>

          {/* Body */}
          <div className="dash-modal__body" style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>

            {/* Title */}
            <div className="dash-settings-field">
              <label className="dash-settings-label">Başlık *</label>
              <input
                ref={titleRef}
                type="text"
                className="dash-settings-input"
                value={title}
                onChange={function(e) { setTitle(e.target.value) }}
                placeholder="Etkinlik başlığı"
                maxLength={200}
              />
            </div>

            {/* Color picker */}
            <div className="dash-settings-field">
              <label className="dash-settings-label">Renk</label>
              <div style={{ display: 'flex', gap: 8, marginTop: 4 }}>
                {COLOR_OPTIONS.map(function(c) {
                  var selected = color === c.id
                  return (
                    <button
                      key={c.id}
                      type="button"
                      title={c.label}
                      onClick={function() { setColor(c.id) }}
                      style={{
                        width: 26, height: 26, borderRadius: '50%', border: 'none',
                        cursor: 'pointer', background: c.dot, padding: 0,
                        outline: selected ? ('3px solid ' + c.dot) : 'none',
                        outlineOffset: '2px',
                        transform: selected ? 'scale(1.2)' : 'scale(1)',
                        transition: 'transform .12s, outline .12s',
                        boxShadow: selected ? ('0 0 0 2px var(--dash-card-bg), 0 0 0 4px ' + c.dot) : 'none',
                      }}
                    />
                  )
                })}
              </div>
            </div>

            {/* Date range */}
            <div className="dash-settings-field">
              <label className="dash-settings-label">Tarih</label>
              <div style={{ display: 'flex', gap: 8, marginTop: 4 }}>
                <input
                  type="date"
                  className="dash-settings-input"
                  style={{ flex: 1 }}
                  value={startDate}
                  onChange={function(e) { setStartDate(e.target.value) }}
                />
                <input
                  type="date"
                  className="dash-settings-input"
                  style={{ flex: 1 }}
                  value={endDate}
                  min={startDate || undefined}
                  placeholder="Bitiş"
                  onChange={function(e) { setEndDate(e.target.value) }}
                />
              </div>
            </div>

            {/* All-day toggle */}
            <div className="dash-picker-row" onClick={function() { setIsAllDay(function(v) { return !v }) }} style={{ cursor: 'pointer' }}>
              <span style={{ flex: '1 1 auto', fontSize: 13, color: 'var(--dash-text-primary)' }}>Tüm gün</span>
              <button
                type="button"
                className={'dash-switch' + (isAllDay ? ' dash-switch--on' : '')}
                onClick={function(e) { e.stopPropagation(); setIsAllDay(function(v) { return !v }) }}
                role="switch"
                aria-checked={isAllDay}
              >
                <span className="dash-switch__thumb" />
              </button>
            </div>

            {/* Time inputs (visible when not all-day) */}
            {!isAllDay && (
              <div className="dash-settings-field">
                <label className="dash-settings-label">Saat</label>
                <div style={{ display: 'flex', gap: 8, alignItems: 'center', marginTop: 4 }}>
                  <input
                    type="time"
                    className="dash-settings-input"
                    style={{ flex: 1 }}
                    value={startTime}
                    onChange={function(e) { setStartTime(e.target.value) }}
                  />
                  <span style={{ color: 'var(--dash-text-muted)', fontSize: 13 }}>–</span>
                  <input
                    type="time"
                    className="dash-settings-input"
                    style={{ flex: 1 }}
                    value={endTime}
                    onChange={function(e) { setEndTime(e.target.value) }}
                  />
                </div>
              </div>
            )}

            {/* Description */}
            <div className="dash-settings-field">
              <label className="dash-settings-label">Açıklama</label>
              <textarea
                className="dash-settings-input"
                style={{ minHeight: 64, resize: 'vertical', fontFamily: 'inherit', marginTop: 4 }}
                value={desc}
                onChange={function(e) { setDesc(e.target.value) }}
                placeholder="İsteğe bağlı…"
                maxLength={1000}
              />
            </div>

            {/* Error */}
            {error && (
              <div style={{ fontSize: 12, color: '#e11d48', padding: '6px 10px', borderRadius: 8, background: 'rgba(244,63,94,0.1)' }}>
                {error}
              </div>
            )}
          </div>

          {/* Footer */}
          <div className="dash-modal__footer">
            {/* Delete (only for existing personal events) */}
            {isEdit && props.onDelete && (
              <button
                type="button"
                className="dash-btn dash-btn--danger"
                style={{ marginRight: 'auto' }}
                onClick={function() { setConfirmDel(true) }}
                disabled={saving}
              >
                <Trash2 size={14} />
                Sil
              </button>
            )}
            <button type="button" className="dash-btn dash-btn--ghost" onClick={props.onClose} disabled={saving}>
              Vazgeç
            </button>
            <button type="button" className="dash-btn dash-btn--primary" onClick={handleSave} disabled={saving}>
              {saving ? 'Kaydediliyor…' : 'Kaydet'}
            </button>
          </div>
        </div>
      </div>

      {/* Delete confirmation */}
      <ConfirmRemoveModal
        open={confirmDel}
        title="Etkinliği Sil"
        message={title ? '"' + title + '" etkinliğini silmek istediğinize emin misiniz?' : 'Bu etkinliği silmek istediğinize emin misiniz?'}
        okLabel="Evet, Sil"
        variant="danger"
        onConfirm={function() { setConfirmDel(false); if (props.onDelete) props.onDelete(props.event.id) }}
        onCancel={function() { setConfirmDel(false) }}
      />
    </>,
    document.body
  )
}
