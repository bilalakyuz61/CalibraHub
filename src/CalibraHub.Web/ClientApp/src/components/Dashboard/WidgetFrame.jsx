/**
 * WidgetFrame — Her widget'i saran cam (glass) kart cercevesi.
 *
 * Boyut değişimi S/M/L butonlarından değil köşe resize handle'dan yapılır.
 * Taşıma: sol üst grip noktaları (GripVertical) ile sürükle-bırak.
 * Ayarlar: Settings2 ikonu — edit mode bağımsız her zaman görünür.
 *
 * Props:
 *   {
 *     type, title, icon, iconColor,
 *     editMode, isDark,
 *     index, total,
 *     onMoveUp, onMoveDown, onRemove, onSettingsOpen,
 *     draggable, onDragStart, onDragOver, onDragEnd, onDrop,
 *     children
 *   }
 */
import React from 'react'
import { X, GripVertical, AlertTriangle, Settings2 } from 'lucide-react'
import { resolveIcon, resolveColorForTheme } from '../CalibraSmartBoard/DynamicWidgetFactory'

class WidgetErrorBoundary extends React.Component {
  constructor(props) {
    super(props)
    this.state = { hasError: false }
  }
  static getDerivedStateFromError() { return { hasError: true } }
  componentDidCatch(error, info) {
    console.error('[Dashboard][widget:' + (this.props.type || '?') + ']', error, info && info.componentStack)
  }
  render() {
    if (this.state.hasError) {
      return (
        <div className="dash-widget-error">
          <AlertTriangle size={22} strokeWidth={1.8} style={{ color: '#f59e0b' }} />
          <span>Bu bileşen yüklenemedi.</span>
        </div>
      )
    }
    return this.props.children
  }
}

export default function WidgetFrame(props) {
  var editMode = !!props.editMode
  var isDark = !!props.isDark
  var Icon = resolveIcon(props.icon)
  var palette = resolveColorForTheme(props.iconColor || 'indigo', null, isDark)
  var index = props.index || 0
  var total = props.total || 0

  return (
    <div
      className={'dash-card' + (editMode ? ' dash-card--edit' : '')}
      draggable={props.draggable}
      onDragStart={props.onDragStart}
      onDragOver={props.onDragOver}
      onDragEnd={props.onDragEnd}
      onDrop={props.onDrop}
    >
      <div className="dash-card__header">
        {/* Taşıma tutamacı — sol üst, sadece edit mode */}
        {editMode && (
          <span className="dash-drag-handle dash-icon-btn" title="Sürükleyerek taşı" aria-hidden="true">
            <GripVertical size={14} />
          </span>
        )}
        <div
          className="dash-card__icon"
          style={{ background: palette.bg, border: '1px solid ' + palette.border }}
        >
          <Icon size={15} style={{ color: palette.icon }} />
        </div>
        <span className="dash-card__title">{props.title}</span>

        {/* Ayarlar butonu — edit mode bağımsız, her zaman görünür */}
        {props.onSettingsOpen && (
          <button
            type="button"
            className="dash-icon-btn"
            onClick={function(e) { e.stopPropagation(); props.onSettingsOpen() }}
            title="Widget Ayarları"
            aria-label="Widget Ayarları"
          >
            <Settings2 size={14} />
          </button>
        )}

        {/* Edit mod araçları: sadece "Sil". Taşıma sol grip ile drag-drop; boyut köşeden resize. */}
        {editMode && (
          <div className="dash-card__edit-tools">
            <button type="button" className="dash-icon-btn dash-icon-btn--danger"
              onClick={function() { if (props.onRemove) props.onRemove() }} title="Panodan kaldır" aria-label="Panodan kaldır">
              <X size={15} strokeWidth={2.4} />
            </button>
          </div>
        )}
      </div>

      <div className="dash-card__body">
        <WidgetErrorBoundary type={props.type}>
          {props.children}
        </WidgetErrorBoundary>
      </div>
    </div>
  )
}
