/**
 * WidgetGrid — react-grid-layout tabanlı widget canvas.
 *
 * Boyut sistemi (48-kolon, 4px row) — maksimum hassasiyet:
 *   Yatay: 48 kolon → ~29px/kolon adımı
 *   Dikey: 4px satır → 4px adımı
 *   Eski size "sm" → w=16, "md" → w=32, "lg" → w=48
 *   Eski height 1 → h=50 (~200px), 2 → h=85 (~340px), 3 → h=125 (~500px)
 * Taşıma: .dash-drag-handle grip noktaları (RGL draggableHandle)
 * Resize:  sağ-alt köşe (SE handle, react-resizable)
 * Edit mod yokken isDraggable=false, isResizable=false.
 */
import { useState } from 'react'
import GridLayout, { WidthProvider } from 'react-grid-layout'
import 'react-grid-layout/css/styles.css'
import 'react-resizable/css/styles.css'
import WidgetFrame from './WidgetFrame'
import WidgetSettingsModal from './WidgetSettingsModal'
import { getWidgetMeta } from './widgetRegistry'

var RGL = WidthProvider(GridLayout)

var GRID_COLS  = 48   // ~29px/kolon adımı
var ROW_HEIGHT = 4    // 4px dikey adım

// size string → RGL w (48-kolon)
function sizeToW(size) {
  var n = parseInt(size, 10)
  if (!isNaN(n) && n >= 1 && n <= 6) return n * 8  // 1→8, 2→16, 3→24, 4→32, 5→40, 6→48
  if (size === 'sm') return 16
  if (size === 'lg') return 48
  return 32  // md / varsayılan
}

// height 1|2|3 → RGL h (4px/satır)
function heightToH(height) {
  var h = parseInt(height, 10) || 1
  if (h === 1) return 50   // ~200px
  if (h === 3) return 125  // ~500px
  return 85                // ~340px
}

// Widget dizisinden RGL layout dizisi üret.
// layout: {x,y,w,h} saklıysa onu kullan; yoksa sıraya göre otomatik paketle.
function buildRGLLayout(widgets) {
  var curX = 0, curY = 0, rowH = 0
  return widgets.map(function(w, i) {
    var ww, wh, wx, wy
    if (w.layout && Number.isFinite(w.layout.x)) {
      wx = w.layout.x; wy = w.layout.y
      ww = w.layout.w; wh = w.layout.h
    } else {
      ww = sizeToW(w.size); wh = heightToH(w.height)
      if (curX + ww > GRID_COLS) { curX = 0; curY += rowH; rowH = 0 }
      wx = curX; wy = curY
      curX += ww; rowH = Math.max(rowH, wh)
    }
    return { i: String(i), x: wx, y: wy, w: ww, h: wh, minW: 1, minH: 1 }
  })
}

export default function WidgetGrid(props) {
  var layout   = Array.isArray(props.layout) ? props.layout : []
  var editMode = !!props.editMode
  var [settingsOpenIdx, setSettingsOpenIdx] = useState(null)
  // 2026-06-19: Quick-links widget'ının çark ikonu, ortak WidgetSettingsModal yerine
  // QuickLinksPickerModal'ı açar. Bu state hangi instance'ın picker'ının açılacağını
  // tutar (null = hiçbiri). QuickLinksWidget editIntent prop'u olarak okur.
  var [quickLinksEditIdx, setQuickLinksEditIdx] = useState(null)

  var rglLayout = buildRGLLayout(layout)

  function handleLayoutChange(newLayout) {
    if (!props.onLayoutChange) return
    props.onLayoutChange(newLayout.map(function(item) {
      return {
        idx: parseInt(item.i, 10),
        layout: { x: item.x, y: item.y, w: item.w, h: item.h },
      }
    }))
  }

  function handleSettingsApply(nextSettings) {
    var idx = settingsOpenIdx
    setSettingsOpenIdx(null)
    if (props.onWidgetSettingsChange) props.onWidgetSettingsChange(idx, nextSettings)
  }

  var settingsMeta   = settingsOpenIdx !== null && layout[settingsOpenIdx] ? getWidgetMeta(layout[settingsOpenIdx].type) : null
  var settingsWidget = settingsOpenIdx !== null ? layout[settingsOpenIdx] : null

  return (
    <>
      <RGL
        layout={rglLayout}
        cols={GRID_COLS}
        rowHeight={ROW_HEIGHT}
        margin={[14, 14]}
        containerPadding={[0, 0]}
        isDraggable={editMode}
        isResizable={editMode}
        compactType="vertical"
        draggableHandle=".dash-drag-handle"
        draggableCancel=".dash-icon-btn--danger,.dash-card__edit-tools,.dash-icon-btn:not(.dash-drag-handle)"
        resizeHandles={['se']}
        onDragStop={handleLayoutChange}
        onResizeStop={handleLayoutChange}
      >
        {layout.map(function(w, idx) {
          var meta = getWidgetMeta(w.type)
          if (!meta) return <div key={String(idx)} />
          var WidgetComp = meta.component

          var displayTitle = (w.settings && w.settings.customTitle) || meta.title

          var widgetProps = {
            size: w.size,
            height: w.height,
            settings: w.settings || {},
            isDark: props.isDark,
            lang: props.lang,
            editMode: editMode,
          }
          if (w.type === 'welcome-card') {
            widgetProps.user = props.user
            widgetProps.system = props.system
          }
          if (w.type === 'quick-links') {
            widgetProps.quickLinkOptions = props.quickLinkOptions
            widgetProps.onSettingsChange = function(next) {
              if (props.onWidgetSettingsChange) props.onWidgetSettingsChange(idx, next)
            }
            // Çark ikonu (WidgetFrame) → bu widget'ın picker'ını aç komutu.
            widgetProps.editIntent = quickLinksEditIdx === idx
            widgetProps.onEditIntentConsumed = function () { setQuickLinksEditIdx(null) }
          }
          if (w.type === 'recent-documents') {
            widgetProps.take = (w.settings && w.settings.take) || 8
          }
          if (w.type === 'calendar') {
            widgetProps.fullPage = false
          }

          return (
            <div key={String(idx)} className="dash-rgl-cell">
              <WidgetFrame
                type={w.type}
                title={displayTitle}
                icon={meta.icon}
                iconColor={meta.iconColor}
                editMode={editMode}
                isDark={props.isDark}
                index={idx}
                total={layout.length}
                onRemove={function() { if (props.onRemoveRequest) props.onRemoveRequest(idx) }}
                onSettingsOpen={
                  w.type === 'quick-links'
                    ? function () { setQuickLinksEditIdx(idx) }
                    : function () { setSettingsOpenIdx(idx) }
                }
              >
                <WidgetComp {...widgetProps} />
              </WidgetFrame>
            </div>
          )
        })}
      </RGL>

      {settingsOpenIdx !== null && settingsMeta && settingsWidget && (
        <WidgetSettingsModal
          open={true}
          widget={{
            type: settingsWidget.type,
            title: settingsMeta.title,
            settings: settingsWidget.settings || {},
          }}
          schema={settingsMeta.settingsSchema || []}
          onApply={handleSettingsApply}
          onClose={function() { setSettingsOpenIdx(null) }}
        />
      )}
    </>
  )
}
