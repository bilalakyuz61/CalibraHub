/**
 * QuickLinksPickerModal — Kullanicinin kisayol olarak ekleyecegi menu
 * ekranlarini sectigi modal. Secenekler `options` (yetkili menu yapraklari,
 * Dashboard config'inden gelir) GroupLabel'a gore gruplanir.
 *
 * Boolean secim (acik/kapali) CLAUDE.md kuralina gore switch ile gosterilir —
 * native checkbox kullanilmaz.
 *
 * Props:
 *   {
 *     open,
 *     options: [{ key, label, url, icon, groupLabel }],
 *     selectedKeys: string[],
 *     onApply(items),   // items = secilen option objeleri (key,label,url,icon)
 *     onClose
 *   }
 */
import { useState, useEffect, useMemo } from 'react'
import { createPortal } from 'react-dom'
import { X, Zap, Search } from 'lucide-react'
import { resolveIcon } from '../../CalibraSmartBoard/DynamicWidgetFactory'

export default function QuickLinksPickerModal(props) {
  var open = props.open
  var options = useMemo(function () {
    return Array.isArray(props.options) ? props.options : []
  }, [props.options])

  var [selected, setSelected] = useState(function () { return new Set(props.selectedKeys || []) })
  var [search, setSearch] = useState('')

  useEffect(function () {
    if (open) {
      setSelected(new Set(props.selectedKeys || []))
      setSearch('')
    }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open])

  useEffect(function () {
    if (!open) return undefined
    function onKey(e) { if (e.key === 'Escape') { if (props.onClose) props.onClose() } }
    document.addEventListener('keydown', onKey)
    return function () { document.removeEventListener('keydown', onKey) }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open])

  // Arama filtresi + gruplama
  var grouped = useMemo(function () {
    var q = search.trim().toLowerCase()
    var filtered = q
      ? options.filter(function (o) {
          return (o.label && o.label.toLowerCase().indexOf(q) !== -1) ||
                 (o.groupLabel && o.groupLabel.toLowerCase().indexOf(q) !== -1)
        })
      : options
    var map = {}
    var order = []
    filtered.forEach(function (o) {
      var g = o.groupLabel || 'Diğer'
      if (!map[g]) { map[g] = []; order.push(g) }
      map[g].push(o)
    })
    return order.map(function (g) { return { label: g, items: map[g] } })
  }, [options, search])

  if (!open) return null

  function toggle(key) {
    setSelected(function (prev) {
      var next = new Set(prev)
      if (next.has(key)) next.delete(key); else next.add(key)
      return next
    })
  }

  function handleApply() {
    var items = options.filter(function (o) { return selected.has(o.key) })
      .map(function (o) { return { key: o.key, label: o.label, url: o.url, icon: o.icon || null } })
    if (props.onApply) props.onApply(items)
  }

  return createPortal(
    <div className="dash-modal-backdrop" onClick={function () { if (props.onClose) props.onClose() }}>
      <div className="dash-modal" onClick={function (e) { e.stopPropagation() }} role="dialog" aria-modal="true">
        <div className="dash-modal__header">
          <Zap size={17} style={{ color: '#d97706' }} />
          <span className="dash-modal__title">Kısayol Seç</span>
          <button type="button" className="dash-icon-btn" onClick={props.onClose} aria-label="Kapat">
            <X size={16} />
          </button>
        </div>

        <div style={{ padding: '12px 18px 0', flexShrink: 0 }}>
          <div style={{ position: 'relative' }}>
            <Search size={14} style={{ position: 'absolute', left: 11, top: '50%', transform: 'translateY(-50%)', color: 'var(--dash-text-muted)' }} />
            <input
              type="text"
              value={search}
              onChange={function (e) { setSearch(e.target.value) }}
              placeholder="Ekran ara…"
              style={{
                width: '100%', padding: '8px 12px 8px 32px', borderRadius: 10, fontSize: 13,
                background: 'var(--dash-card-bg)', border: '1px solid var(--dash-card-border)',
                color: 'var(--dash-text-primary)', outline: 'none',
              }}
            />
          </div>
        </div>

        <div className="dash-modal__body">
          {grouped.length === 0 && (
            <div className="dash-widget-empty">Eşleşen ekran bulunamadı.</div>
          )}
          {grouped.map(function (grp) {
            return (
              <div key={grp.label}>
                <div className="dash-picker-group-label">{grp.label}</div>
                {grp.items.map(function (o) {
                  var Icon = resolveIcon(o.icon)
                  var on = selected.has(o.key)
                  return (
                    <div
                      key={o.key}
                      className="dash-picker-row"
                      onClick={function () { toggle(o.key) }}
                      style={{ cursor: 'pointer' }}
                    >
                      <Icon size={15} style={{ color: 'var(--dash-text-secondary)', flexShrink: 0 }} />
                      <span className="dash-row__title" style={{ flex: '1 1 auto' }}>{o.label}</span>
                      <button
                        type="button"
                        className={'dash-switch' + (on ? ' dash-switch--on' : '')}
                        onClick={function (e) { e.stopPropagation(); toggle(o.key) }}
                        role="switch"
                        aria-checked={on}
                        aria-label={o.label}
                      >
                        <span className="dash-switch__thumb" />
                      </button>
                    </div>
                  )
                })}
              </div>
            )
          })}
        </div>

        <div className="dash-modal__footer">
          <span className="dash-row__sub" style={{ marginRight: 'auto' }}>{selected.size} seçili</span>
          <button type="button" className="dash-btn dash-btn--ghost" onClick={props.onClose}>Vazgeç</button>
          <button type="button" className="dash-btn dash-btn--primary" onClick={handleApply}>Uygula</button>
        </div>
      </div>
    </div>,
    document.body
  )
}
