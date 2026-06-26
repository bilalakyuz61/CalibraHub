import React from 'react'
import PanelChart from './PanelChart'

export default function PanelCard({ panel, selected, onSelect, onDelete, onColumns, onData, activeFilters, onFilterChange, dragHandleProps, fillHeight, viewFields, pageSource }) {
  const eff = pageSource ? { ...panel, ...pageSource } : panel
  const metaLeft  = eff.sourceType === 'saved'
    ? (eff.sourceName || `Kaynak #${eff.sourceId}`)
    : eff.sourceType === 'sql'
      ? 'Özel SQL'
      : (eff.sourceLabel || eff.source || '—')
  const metaRight = eff.sourceType === 'sql' || eff.sourceType === 'saved'
    ? null
    : (panel.metric || null)

  const heightMode  = panel.panelHeight || (panel.tall ? 'tall' : 'normal')
  const chartHeight = fillHeight ? 'full' : heightMode === 'full' ? 'full' : heightMode === 'tall' ? 240 : 110

  return (
    <div
      className={`rd-panel${selected ? ' rd-panel--sel' : ''}`}
      style={{ '--panel-accent': panel.color || '#6366f1' }}
      onClick={onSelect}
      role="button"
      tabIndex={0}
      onKeyDown={e => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); onSelect() } }}
    >
      <div className="rd-panel__head">
        {dragHandleProps && (
          <span
            className="rd-panel__grip"
            {...dragHandleProps}
            onClick={e => e.stopPropagation()}
            title="Taşımak için sürükleyin"
          >
            <svg viewBox="0 0 24 24" width="12" height="12" fill="currentColor">
              <circle cx="9" cy="6" r="1.4" /><circle cx="15" cy="6" r="1.4" />
              <circle cx="9" cy="12" r="1.4" /><circle cx="15" cy="12" r="1.4" />
              <circle cx="9" cy="18" r="1.4" /><circle cx="15" cy="18" r="1.4" />
            </svg>
          </span>
        )}
        <div className="rd-panel__titles">
          <span className="rd-panel__title" style={{
            textAlign: panel.titleAlign || 'left',
            color: panel.titleColor || undefined,
            fontSize: Number.isFinite(panel.titleSize) ? panel.titleSize : undefined,
          }}>{panel.title || 'Başlıksız Panel'}</span>
          {panel.subtitle && (
            <span className="rd-panel__subtitle" style={{
              textAlign: panel.subtitleAlign || 'left',
              color: panel.subtitleColor || undefined,
              fontSize: Number.isFinite(panel.subtitleSize) ? panel.subtitleSize : undefined,
            }}>{panel.subtitle}</span>
          )}
        </div>
        <button
          type="button"
          className="rd-panel__del"
          title="Paneli sil"
          onClick={e => { e.stopPropagation(); onDelete(panel.id) }}
        >
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" style={{ width: 11, height: 11 }}>
            <path d="M18 6 6 18M6 6l12 12" />
          </svg>
        </button>
      </div>

      <div className="rd-panel__preview">
        <PanelChart panel={eff} chartHeight={chartHeight} onColumns={onColumns} onData={onData} activeFilters={activeFilters} onFilterChange={onFilterChange} viewFields={viewFields} />
      </div>

      <div className="rd-panel__footer">
        <span className="rd-panel__meta-item">{metaLeft}</span>
        {metaRight && <>
          <span className="rd-panel__meta-sep">·</span>
          <span className="rd-panel__meta-item">{metaRight}</span>
        </>}
      </div>
    </div>
  )
}
