import { CalendarRange, ZoomIn, ZoomOut, RefreshCw, GanttChartSquare } from 'lucide-react'
import { getPalette, BLOCK_TYPE_LABELS } from './ganttPalette'
import { ZOOM_LEVELS } from './timeScale'

export default function GanttToolbar({
  dateFrom, dateTo, onDateFromChange, onDateToChange,
  zoomIndex, onZoomIn, onZoomOut, onRefresh, isDark, refreshing,
}) {
  var palette = getPalette(isDark)

  return (
    <div className="ms-toolbar">
      <div className="ms-toolbar-title">
        <GanttChartSquare size={17} /> Makine Planlama
      </div>

      <div className="ms-toolbar-group">
        <CalendarRange size={13} className="ms-toolbar-label" />
        <span className="ms-toolbar-label">Başlangıç</span>
        <input
          type="date"
          className="ms-date-input"
          value={dateFrom}
          onChange={function (e) { onDateFromChange(e.target.value) }}
        />
        <span className="ms-toolbar-label">Bitiş</span>
        <input
          type="date"
          className="ms-date-input"
          value={dateTo}
          onChange={function (e) { onDateToChange(e.target.value) }}
        />
      </div>

      <div className="ms-toolbar-group">
        <button className="ms-zoom-btn" onClick={onZoomOut} disabled={zoomIndex === 0} title="Uzaklaştır (gün görünümü)">
          <ZoomOut size={14} />
        </button>
        <span className="ms-zoom-label">{ZOOM_LEVELS[zoomIndex]} px/sa</span>
        <button className="ms-zoom-btn" onClick={onZoomIn} disabled={zoomIndex === ZOOM_LEVELS.length - 1} title="Yakınlaştır (saat görünümü)">
          <ZoomIn size={14} />
        </button>
      </div>

      <button className="ms-icon-btn" onClick={onRefresh} title="Yenile">
        <RefreshCw size={14} className={refreshing ? 'ms-spin' : ''} />
      </button>

      <div className="ms-spacer" />

      <div className="ms-legend">
        {[1, 2, 3, 4].map(function (t) {
          return (
            <span key={t} className="ms-legend-item">
              <span className="ms-legend-dot" style={{ background: palette.block[t].fill }} />
              {BLOCK_TYPE_LABELS[t]}
            </span>
          )
        })}
        <span className="ms-legend-item">
          <span className="ms-legend-dot" style={{ background: palette.shade }} />
          Çalışma Dışı
        </span>
        <span className="ms-legend-item">
          <span className="ms-legend-dot" style={{ background: palette.holiday }} />
          Resmi Tatil
        </span>
      </div>
    </div>
  )
}
