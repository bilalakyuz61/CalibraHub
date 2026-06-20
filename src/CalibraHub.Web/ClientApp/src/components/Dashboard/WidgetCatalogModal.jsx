/**
 * WidgetCatalogModal — "Widget Ekle" modali. Backend'in yetkiye gore filtreledigi
 * eklenebilir widget katalogunu kart olarak listeler. Halihazirda panoda olan
 * (ve AllowMultiple=false) widget'lar pasif + onay isareti ile gosterilir.
 *
 * Props:
 *   {
 *     open,
 *     catalog: [{ type, title, description, icon, iconColor, defaultSize, allowMultiple }],
 *     placedTypes: string[],   // panoda zaten olan tipler
 *     onAdd(catalogItem),
 *     onClose
 *   }
 */
import { useEffect } from 'react'
import { createPortal } from 'react-dom'
import { X, Plus, Check, LayoutGrid } from 'lucide-react'
import { resolveIcon, resolveColorForTheme } from '../CalibraSmartBoard/DynamicWidgetFactory'

export default function WidgetCatalogModal(props) {
  var open = props.open
  var catalog = Array.isArray(props.catalog) ? props.catalog : []
  var placed = props.placedTypes || []
  var isDark = !!props.isDark

  useEffect(function () {
    if (!open) return undefined
    function onKey(e) { if (e.key === 'Escape') { if (props.onClose) props.onClose() } }
    document.addEventListener('keydown', onKey)
    return function () { document.removeEventListener('keydown', onKey) }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open])

  if (!open) return null

  return createPortal(
    <div className="dash-modal-backdrop" onClick={function () { if (props.onClose) props.onClose() }}>
      <div className="dash-modal" onClick={function (e) { e.stopPropagation() }} role="dialog" aria-modal="true">
        <div className="dash-modal__header">
          <LayoutGrid size={17} style={{ color: '#6366f1' }} />
          <span className="dash-modal__title">Widget Ekle</span>
          <button type="button" className="dash-icon-btn" onClick={props.onClose} aria-label="Kapat">
            <X size={16} />
          </button>
        </div>

        <div className="dash-modal__body">
          {catalog.length === 0 ? (
            <div className="dash-widget-empty">Eklenebilecek başka widget yok.</div>
          ) : (
            <div className="dash-catalog-grid">
              {catalog.map(function (c) {
                var Icon = resolveIcon(c.icon)
                var palette = resolveColorForTheme(c.iconColor || 'indigo', null, isDark)
                var already = !c.allowMultiple && placed.indexOf(c.type) !== -1
                return (
                  <button
                    key={c.type}
                    type="button"
                    className={'dash-catalog-card' + (already ? ' dash-catalog-card--disabled' : '')}
                    disabled={already}
                    onClick={function () { if (!already && props.onAdd) props.onAdd(c) }}
                  >
                    <div
                      className="dash-catalog-card__icon"
                      style={{ background: palette.bg, border: '1px solid ' + palette.border }}
                    >
                      <Icon size={17} style={{ color: palette.icon }} />
                    </div>
                    <div style={{ flex: '1 1 auto', minWidth: 0 }}>
                      <div className="dash-catalog-card__title" style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
                        {c.title}
                        {already && <Check size={13} style={{ color: '#059669' }} />}
                      </div>
                      {c.description && <div className="dash-catalog-card__desc">{c.description}</div>}
                    </div>
                    {!already && (
                      <span style={{ flexShrink: 0, color: '#6366f1', alignSelf: 'center' }}>
                        <Plus size={16} />
                      </span>
                    )}
                  </button>
                )
              })}
            </div>
          )}
        </div>

        <div className="dash-modal__footer">
          <button type="button" className="dash-btn dash-btn--ghost" onClick={props.onClose}>Kapat</button>
        </div>
      </div>
    </div>,
    document.body
  )
}
