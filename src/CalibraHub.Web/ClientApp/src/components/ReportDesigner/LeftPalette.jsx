import React from 'react'

const CHART_TYPES = [
  {
    k: 'line', label: 'Çizgi grafiği',
    icon: (
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
        <polyline points="22 12 18 12 15 18 9 6 6 12 2 12" />
      </svg>
    ),
  },
  {
    k: 'area', label: 'Alan grafiği',
    icon: (
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
        <path d="M3 13l4-4 4 3 5-7 5 4v12H3z" fill="currentColor" fillOpacity="0.22" stroke="none" />
        <polyline points="3 13 7 9 11 12 16 5 21 9" />
      </svg>
    ),
  },
  {
    k: 'bar', label: 'Bar grafiği',
    icon: (
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
        <line x1="18" y1="20" x2="18" y2="10" /><line x1="12" y1="20" x2="12" y2="4" />
        <line x1="6" y1="20" x2="6" y2="14" /><line x1="2" y1="20" x2="22" y2="20" />
      </svg>
    ),
  },
  {
    k: 'pie', label: 'Pasta grafiği',
    icon: (
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
        <path d="M21.21 15.89A10 10 0 1 1 8 2.83" /><path d="M22 12A10 10 0 0 0 12 2v10z" />
      </svg>
    ),
  },
  {
    k: 'funnel', label: 'Huni grafiği',
    icon: (
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
        <path d="M3 4h18l-7 8v7l-4 2v-9L3 4z" />
      </svg>
    ),
  },
  {
    k: 'combo', label: 'Kombi (Bar+Çizgi)',
    icon: (
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
        <rect x="2" y="10" width="4" height="10" rx="1" fill="currentColor" fillOpacity=".35" stroke="none" />
        <rect x="9" y="6"  width="4" height="14" rx="1" fill="currentColor" fillOpacity=".35" stroke="none" />
        <rect x="16" y="13" width="4" height="7" rx="1" fill="currentColor" fillOpacity=".35" stroke="none" />
        <polyline points="4 8 11 4 18 10" strokeWidth="2" />
      </svg>
    ),
  },
  {
    k: 'waterfall', label: 'Şelale grafiği',
    icon: (
      <svg viewBox="0 0 24 24" fill="none" strokeWidth="2" strokeLinecap="round">
        <line x1="2" y1="20" x2="22" y2="20" stroke="currentColor" />
        <rect x="2"  y="4"  width="4" height="10" rx="1" fill="currentColor" fillOpacity=".5" />
        <rect x="8"  y="10" width="4" height="4"  rx="1" fill="#10b981" fillOpacity=".8" />
        <rect x="14" y="6"  width="4" height="8"  rx="1" fill="#ef4444" fillOpacity=".8" />
        <rect x="20" y="7"  width="2" height="7"  rx="1" fill="currentColor" fillOpacity=".5" />
      </svg>
    ),
  },
  {
    k: 'stacked100', label: '%100 Yığılmış Bar',
    icon: (
      <svg viewBox="0 0 24 24" fill="none" strokeWidth="2" strokeLinecap="round">
        <rect x="3"  y="4" width="5" height="6"  rx="1" fill="currentColor" fillOpacity=".7" />
        <rect x="3"  y="10" width="5" height="10" rx="1" fill="currentColor" fillOpacity=".3" />
        <rect x="10" y="4" width="5" height="9"  rx="1" fill="currentColor" fillOpacity=".7" />
        <rect x="10" y="13" width="5" height="7" rx="1" fill="currentColor" fillOpacity=".3" />
        <rect x="17" y="4" width="5" height="4"  rx="1" fill="currentColor" fillOpacity=".7" />
        <rect x="17" y="8" width="5" height="12" rx="1" fill="currentColor" fillOpacity=".3" />
      </svg>
    ),
  },
  {
    k: 'bullet', label: 'Hedef Göstergesi',
    icon: (
      <svg viewBox="0 0 24 24" fill="none" strokeWidth="2" strokeLinecap="round">
        <rect x="2" y="9"  width="20" height="6" rx="2" fill="currentColor" fillOpacity=".1" stroke="currentColor" strokeWidth="1" />
        <rect x="2" y="10" width="13" height="4" rx="1" fill="currentColor" fillOpacity=".6" />
        <line x1="17" y1="7" x2="17" y2="17" stroke="#f59e0b" strokeWidth="2.5" />
      </svg>
    ),
  },
  {
    k: 'heatmap', label: 'Isı Haritası',
    icon: (
      <svg viewBox="0 0 24 24" fill="none">
        {[[3,3,.8],[8,3,.4],[13,3,.2],[18,3,.55],
          [3,8,.3],[8,8,.9],[13,8,.7],[18,8,.3],
          [3,13,.6],[8,13,.3],[13,13,.55],[18,13,.8],
          [3,18,.2],[8,18,.7],[13,18,.4],[18,18,.5]].map(([x,y,o],i) => (
          <rect key={i} x={x} y={y} width="4" height="4" rx=".5" fill="currentColor" fillOpacity={o} />
        ))}
      </svg>
    ),
  },
  {
    k: 'radar', label: 'Radar Grafiği',
    icon: (
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
        <polygon points="12 2 19.5 7.5 17 17 7 17 4.5 7.5" fill="currentColor" fillOpacity=".12" />
        <polygon points="12 6 16.5 9.5 15 14 9 14 7.5 9.5" fill="currentColor" fillOpacity=".28" />
        <line x1="12" y1="2" x2="12" y2="17" stroke="currentColor" strokeOpacity=".2" />
        <line x1="19.5" y1="7.5" x2="7" y2="17" stroke="currentColor" strokeOpacity=".2" />
        <line x1="4.5" y1="7.5" x2="17" y2="17" stroke="currentColor" strokeOpacity=".2" />
      </svg>
    ),
  },
  {
    k: 'text', label: 'Metin Kartı',
    icon: (
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
        <path d="M4 7V4h16v3" /><path d="M9 20h6" /><path d="M12 4v16" />
      </svg>
    ),
  },
  {
    k: 'scatter', label: 'Dağılım Grafiği',
    icon: (
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
        <line x1="2" y1="20" x2="2" y2="2" /><line x1="2" y1="20" x2="22" y2="20" />
        <circle cx="7"  cy="14" r="2" fill="currentColor" fillOpacity=".6" stroke="none" />
        <circle cx="12" cy="8"  r="2" fill="currentColor" fillOpacity=".6" stroke="none" />
        <circle cx="17" cy="11" r="2" fill="currentColor" fillOpacity=".6" stroke="none" />
        <circle cx="9"  cy="17" r="1.5" fill="currentColor" fillOpacity=".35" stroke="none" />
        <circle cx="15" cy="5"  r="1.5" fill="currentColor" fillOpacity=".35" stroke="none" />
      </svg>
    ),
  },
  {
    k: 'stat', label: 'KPI / Kart',
    icon: (
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
        <polyline points="23 6 13.5 15.5 8.5 10.5 1 18" /><polyline points="17 6 23 6 23 12" />
      </svg>
    ),
  },
  {
    k: 'gauge', label: 'Gösterge',
    icon: (
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
        <path d="M4 18a8 8 0 1 1 16 0" /><line x1="12" y1="18" x2="15.5" y2="11.5" /><circle cx="12" cy="18" r="1.3" fill="currentColor" stroke="none" />
      </svg>
    ),
  },
  {
    k: 'treemap', label: 'Ağaç haritası',
    icon: (
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
        <rect x="3" y="3" width="10" height="18" rx="1" /><rect x="15" y="3" width="6" height="9" rx="1" />
        <rect x="15" y="14" width="6" height="7" rx="1" />
      </svg>
    ),
  },
  {
    k: 'table', label: 'Tablo',
    icon: (
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
        <rect x="3" y="3" width="18" height="18" rx="2" /><line x1="3" y1="9" x2="21" y2="9" />
        <line x1="3" y1="15" x2="21" y2="15" /><line x1="9" y1="9" x2="9" y2="21" />
      </svg>
    ),
  },
  {
    k: 'pivot', label: 'Pivot tablo',
    icon: (
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
        <rect x="3" y="3" width="18" height="18" rx="2" /><line x1="3" y1="8" x2="21" y2="8" />
        <line x1="9" y1="3" x2="9" y2="21" />
      </svg>
    ),
  },
]

// Topbar'da "Görseller" dropdown'ı (panel türü seçici) — Filtre butonunun sağında.
export default function LeftPalette({ open, activeType, hasSelection, onPick, onToggle }) {
  return (
    <div className="rd-palette-dd">
      <button
        type="button"
        className={`rd-topbar__tool rd-topbar__tool--icon${open ? ' rd-topbar__tool--active' : ''}`}
        onClick={onToggle}
        title="Görseller — panel türü"
        aria-label="Görseller"
      >
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
          <rect x="3" y="3" width="7" height="7" rx="1.5" /><rect x="14" y="3" width="7" height="7" rx="1.5" />
          <rect x="14" y="14" width="7" height="7" rx="1.5" /><rect x="3" y="14" width="7" height="7" rx="1.5" />
        </svg>
      </button>

      {open && (
        <>
          <div className="rd-palette-dd__backdrop" onClick={onToggle} />
          <div className="rd-palette-dd__pop" role="menu">
            <div className="rd-palette-dd__hint">
              {hasSelection ? 'Seçili panelin türünü değiştir' : 'Panel eklemek için bir tür seçin'}
            </div>
            <div className="rd-palette-dd__grid">
              {CHART_TYPES.map(ct => (
                <button
                  key={ct.k}
                  type="button"
                  className={`rd-palette-dd__item${hasSelection && activeType === ct.k ? ' rd-palette-dd__item--on' : ''}`}
                  onClick={() => onPick(ct.k)}
                  title={ct.label}
                >
                  <span className="rd-palette-dd__icon">{ct.icon}</span>
                  <span className="rd-palette-dd__label">{ct.label}</span>
                </button>
              ))}
            </div>
          </div>
        </>
      )}
    </div>
  )
}
