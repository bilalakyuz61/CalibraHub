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
