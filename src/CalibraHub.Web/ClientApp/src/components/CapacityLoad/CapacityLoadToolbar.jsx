import { Gauge, RefreshCw } from 'lucide-react'

export default function CapacityLoadToolbar({
  bucket, onBucketChange, dateFrom, dateTo, onDateFromChange, onDateToChange,
  onRefresh, refreshing,
}) {
  return (
    <div className="cap-toolbar">
      <div className="cap-toolbar-title">
        <Gauge size={17} /> Kapasite / Yük Raporu
      </div>

      <div className="cap-toolbar-group">
        <span className="cap-toolbar-label">Zaman Kovası</span>
        <div className="cap-bucket-toggle">
          <button
            type="button"
            className={'cap-bucket-btn' + (bucket === 'day' ? ' is-active' : '')}
            onClick={function () { onBucketChange('day') }}
          >
            Gün
          </button>
          <button
            type="button"
            className={'cap-bucket-btn' + (bucket === 'week' ? ' is-active' : '')}
            onClick={function () { onBucketChange('week') }}
          >
            Hafta
          </button>
        </div>
      </div>

      <div className="cap-toolbar-group">
        <span className="cap-toolbar-label">Başlangıç</span>
        <input
          type="date"
          className="cap-date-input"
          value={dateFrom}
          onChange={function (e) { onDateFromChange(e.target.value) }}
        />
        <span className="cap-toolbar-label">Bitiş</span>
        <input
          type="date"
          className="cap-date-input"
          value={dateTo}
          onChange={function (e) { onDateToChange(e.target.value) }}
        />
      </div>

      <button className="cap-icon-btn" onClick={onRefresh} title="Yenile">
        <RefreshCw size={14} className={refreshing ? 'cap-spin' : ''} />
      </button>

      <div className="cap-spacer" />

      <div className="cap-legend">
        <span className="cap-legend-item">
          <span className="cap-legend-swatch cap-legend-swatch--ok" /> 0–50%
        </span>
        <span className="cap-legend-item">
          <span className="cap-legend-swatch cap-legend-swatch--mid" /> 50–85%
        </span>
        <span className="cap-legend-item">
          <span className="cap-legend-swatch cap-legend-swatch--warn" /> 85–100%
        </span>
        <span className="cap-legend-item">
          <span className="cap-legend-swatch cap-legend-swatch--over" /> &gt;100% (Aşım)
        </span>
        <span className="cap-legend-item">
          <span className="cap-legend-swatch cap-legend-swatch--nodata" /> Kapasite Yok
        </span>
      </div>
    </div>
  )
}
