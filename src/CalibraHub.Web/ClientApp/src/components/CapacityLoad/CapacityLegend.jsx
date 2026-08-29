/**
 * Renk efsanesi — şeritten AYRILDI (2026-08-29). C-Grid standardında şerit sağ ucu
 * araçlara (Filtre / Excel / ana eylem) ayrılmıştır; efsane orada durunca araçlar
 * sıkışıyor ve sıra ekranlar arasında tutarsızlaşıyordu.
 */
export default function CapacityLegend() {
  return (
    <div className="cap-legend-bar">
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
  )
}
