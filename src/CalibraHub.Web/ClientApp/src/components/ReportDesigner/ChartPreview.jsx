import React from 'react'

export default function ChartPreview({ type, color, height = 56 }) {
  const isFull = height === 'full' || height === '100%'
  const h   = isFull ? '100%' : Math.max(56, height)
  const c   = color || '#6366f1'
  const hex = c.replace('#', '')
  const rgb = hex.length === 6
    ? [parseInt(hex.slice(0, 2), 16), parseInt(hex.slice(2, 4), 16), parseInt(hex.slice(4, 6), 16)]
    : [99, 102, 241]
  const soft = `rgba(${rgb[0]},${rgb[1]},${rgb[2]},0.18)`

  if (type === 'bar') return (
    <svg viewBox="0 0 120 56" preserveAspectRatio="none" style={{ width: '100%', height: h, display: 'block' }}>
      {[22, 38, 14, 52, 32, 44, 26, 48].map((bh, i) => (
        <rect key={i} x={i * 15 + 1} y={56 - bh} width={12} height={bh} fill={i % 2 === 0 ? soft : c} rx="2" />
      ))}
    </svg>
  )

  if (type === 'pie') return (
    <svg viewBox="0 0 120 56" style={{ width: '100%', height: h, display: 'block' }}>
      <circle cx="60" cy="28" r="22" fill="none" stroke={c}    strokeWidth="14"
              strokeDasharray="83 55" strokeDashoffset="0" />
      <circle cx="60" cy="28" r="22" fill="none" stroke={soft} strokeWidth="14"
              strokeDasharray="35 103" strokeDashoffset="-83" />
      <circle cx="60" cy="28" r="22" fill="none" stroke={`rgba(${rgb[0]},${rgb[1]},${rgb[2]},0.5)`} strokeWidth="14"
              strokeDasharray="20 118" strokeDashoffset="-118" />
    </svg>
  )

  if (type === 'funnel') return (
    <svg viewBox="0 0 120 56" style={{ width: '100%', height: h, display: 'block' }}>
      <polygon points="10,8 110,8 92,20 28,20" fill={c} />
      <polygon points="30,23 90,23 78,35 42,35" fill={`rgba(${rgb[0]},${rgb[1]},${rgb[2]},0.55)`} />
      <polygon points="44,38 76,38 68,50 52,50" fill={soft} />
    </svg>
  )

  if (type === 'stat') return (
    <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', height: h, gap: 3 }}>
      <div style={{ fontSize: (isFull || h > 100) ? 36 : 22, fontWeight: 600, color: '#f1f5f9', letterSpacing: '-0.03em', lineHeight: 1 }}>
        ₺2.4M
      </div>
      <div style={{ fontSize: 10, color: '#10b981', display: 'flex', alignItems: 'center', gap: 3 }}>
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" style={{ width: 10, height: 10 }}>
          <polyline points="23 6 13.5 15.5 8.5 10.5 1 18" /><polyline points="17 6 23 6 23 12" />
        </svg>
        12.4%
      </div>
    </div>
  )

  if (type === 'table') return (
    <div style={{ padding: '2px 0', height: h, overflow: 'hidden' }}>
      {[['Satış', '₺120K'], ['Üretim', '₺85K'], ['Stok', '₺42K'], ['Lojistik', '₺28K'], ['Kalite', '₺18K']].map(([k, v], i) => (
        <div key={i} style={{
          display: 'flex', justifyContent: 'space-between',
          padding: '4px 6px', borderRadius: 3,
          background: i % 2 === 0 ? 'rgba(255,255,255,.04)' : 'transparent',
        }}>
          <span style={{ fontSize: 10, color: '#94a3b8' }}>{k}</span>
          <span style={{ fontSize: 10, color: '#e2e8f0' }}>{v}</span>
        </div>
      ))}
    </div>
  )

  // default: line
  return (
    <svg viewBox="0 0 120 56" preserveAspectRatio="none" style={{ width: '100%', height: h, display: 'block' }}>
      <defs>
        <linearGradient id={`rdg_${hex}`} x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stopColor={c} stopOpacity="0.28" />
          <stop offset="100%" stopColor={c} stopOpacity="0" />
        </linearGradient>
      </defs>
      <path d="M0,44 C18,38 36,30 54,23 C72,16 90,28 120,10 L120,56 L0,56 Z"
            fill={`url(#rdg_${hex})`} />
      <path d="M0,44 C18,38 36,30 54,23 C72,16 90,28 120,10"
            fill="none" stroke={c} strokeWidth="2.2" strokeLinecap="round" />
    </svg>
  )
}
