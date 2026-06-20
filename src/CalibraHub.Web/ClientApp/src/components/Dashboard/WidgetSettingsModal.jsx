/**
 * WidgetSettingsModal — Widget başına özel ayarlar modali.
 *
 * Alan tipleri:
 *   text        → text input
 *   multiselect → toggle switch listesi (CLAUDE.md: boolean = switch, checkbox değil)
 *   select      → tek seçim toggle switch listesi (radio benzeri)
 *
 * Her widget'te ortak: customTitle (başlık özelleştirme).
 * Widget-spesifik alanlar schema prop'undan gelir.
 *
 * Props:
 *   { open, widget: { type, title, settings }, schema, onApply(nextSettings), onClose }
 */
import { useState, useEffect } from 'react'
import { createPortal } from 'react-dom'
import { X, Settings2 } from 'lucide-react'

export default function WidgetSettingsModal(props) {
  var open = props.open
  var schema = Array.isArray(props.schema) ? props.schema : []

  var [values, setValues] = useState({})

  useEffect(function() {
    if (!open) return
    var settings = (props.widget && props.widget.settings) || {}
    var defaults = {}
    schema.forEach(function(f) { if (f.default !== undefined) defaults[f.key] = f.default })
    setValues(Object.assign({}, defaults, settings))
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open])

  useEffect(function() {
    if (!open) return undefined
    function onKey(e) { if (e.key === 'Escape' && props.onClose) props.onClose() }
    document.addEventListener('keydown', onKey)
    return function() { document.removeEventListener('keydown', onKey) }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open])

  if (!open) return null

  function setField(key, val) {
    setValues(function(prev) { return Object.assign({}, prev, { [key]: val }) })
  }

  function toggleMulti(key, val) {
    var cur = Array.isArray(values[key]) ? values[key] : []
    var next = cur.indexOf(val) === -1 ? cur.concat([val]) : cur.filter(function(v) { return v !== val })
    setField(key, next)
  }

  function handleApply() { if (props.onApply) props.onApply(values) }

  var displayTitle = (props.widget && props.widget.title) || 'Widget'

  return createPortal(
    <div className="dash-modal-backdrop" onClick={function() { if (props.onClose) props.onClose() }}>
      <div className="dash-modal dash-modal--sm" onClick={function(e) { e.stopPropagation() }} role="dialog" aria-modal="true">
        <div className="dash-modal__header">
          <Settings2 size={17} style={{ color: '#6366f1' }} />
          <span className="dash-modal__title">{displayTitle} — Ayarlar</span>
          <button type="button" className="dash-icon-btn" onClick={props.onClose} aria-label="Kapat">
            <X size={16} />
          </button>
        </div>

        <div className="dash-modal__body">
          {/* customTitle — her widget'te ortak */}
          <div className="dash-settings-field">
            <label className="dash-settings-label">Başlık</label>
            <input
              type="text"
              className="dash-settings-input"
              value={values.customTitle || ''}
              onChange={function(e) { setField('customTitle', e.target.value) }}
              placeholder={displayTitle}
            />
          </div>

          {/* Widget-specific alanlar */}
          {schema.filter(function(f) { return f.key !== 'customTitle' }).map(function(field) {
            if (field.type === 'text') {
              return (
                <div key={field.key} className="dash-settings-field">
                  <label className="dash-settings-label">{field.label}</label>
                  <input
                    type="text"
                    className="dash-settings-input"
                    value={values[field.key] || ''}
                    onChange={function(e) { setField(field.key, e.target.value) }}
                    placeholder={field.placeholder || ''}
                  />
                </div>
              )
            }

            if (field.type === 'multiselect' && Array.isArray(field.options)) {
              var cur = Array.isArray(values[field.key]) ? values[field.key] : (field.default || [])
              return (
                <div key={field.key} className="dash-settings-field">
                  <label className="dash-settings-label">{field.label}</label>
                  <div style={{ display: 'flex', flexDirection: 'column', gap: 2, marginTop: 4 }}>
                    {field.options.map(function(opt) {
                      var on = cur.indexOf(opt.value) !== -1
                      return (
                        <div key={opt.value} className="dash-picker-row" onClick={function() { toggleMulti(field.key, opt.value) }} style={{ cursor: 'pointer' }}>
                          <span style={{ flex: '1 1 auto', fontSize: 13, color: 'var(--dash-text-primary)' }}>{opt.label}</span>
                          <button type="button" className={'dash-switch' + (on ? ' dash-switch--on' : '')}
                            onClick={function(e) { e.stopPropagation(); toggleMulti(field.key, opt.value) }}
                            role="switch" aria-checked={on}>
                            <span className="dash-switch__thumb" />
                          </button>
                        </div>
                      )
                    })}
                  </div>
                </div>
              )
            }

            if (field.type === 'select' && Array.isArray(field.options)) {
              var selVal = values[field.key] !== undefined ? values[field.key] : field.default
              return (
                <div key={field.key} className="dash-settings-field">
                  <label className="dash-settings-label">{field.label}</label>
                  <div style={{ display: 'flex', flexDirection: 'column', gap: 2, marginTop: 4 }}>
                    {field.options.map(function(opt) {
                      var on = selVal === opt.value
                      return (
                        <div key={String(opt.value)} className="dash-picker-row" onClick={function() { setField(field.key, opt.value) }} style={{ cursor: 'pointer' }}>
                          <span style={{ flex: '1 1 auto', fontSize: 13, color: 'var(--dash-text-primary)' }}>{opt.label}</span>
                          <button type="button" className={'dash-switch' + (on ? ' dash-switch--on' : '')}
                            onClick={function(e) { e.stopPropagation(); setField(field.key, opt.value) }}
                            role="switch" aria-checked={on}>
                            <span className="dash-switch__thumb" />
                          </button>
                        </div>
                      )
                    })}
                  </div>
                </div>
              )
            }

            return null
          })}
        </div>

        <div className="dash-modal__footer">
          <button type="button" className="dash-btn dash-btn--ghost" onClick={props.onClose}>Vazgeç</button>
          <button type="button" className="dash-btn dash-btn--primary" onClick={handleApply}>Uygula</button>
        </div>
      </div>
    </div>,
    document.body
  )
}
