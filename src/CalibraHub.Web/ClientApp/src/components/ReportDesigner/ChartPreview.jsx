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

  if (type === 'combo') return (
    <svg viewBox="0 0 120 56" preserveAspectRatio="none" style={{ width: '100%', height: h, display: 'block' }}>
      {[22, 38, 14, 52, 32, 44].map((bh, i) => (
        <rect key={i} x={i * 20 + 1} y={56 - bh} width={16} height={bh} fill={soft} rx="2" />
      ))}
      <polyline points="9,26 29,12 49,36 69,8 89,18 109,14" fill="none" stroke={c} strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  )

  if (type === 'waterfall') return (
    <svg viewBox="0 0 120 56" preserveAspectRatio="none" style={{ width: '100%', height: h, display: 'block' }}>
      <rect x="0"  y="36" width="18" height="20" rx="2" fill={soft} />
      <rect x="22" y="26" width="18" height="10" rx="2" fill="rgba(16,185,129,.6)" />
      <rect x="44" y="18" width="18" height="8"  rx="2" fill="rgba(16,185,129,.6)" />
      <rect x="66" y="28" width="18" height="10" rx="2" fill="rgba(239,68,68,.6)" />
      <rect x="88" y="22" width="18" height="6"  rx="2" fill="rgba(16,185,129,.6)" />
      <rect x="110" y="22" width="10" height="34" rx="2" fill={soft} />
    </svg>
  )

  if (type === 'stacked100') return (
    <svg viewBox="0 0 120 56" preserveAspectRatio="none" style={{ width: '100%', height: h, display: 'block' }}>
      {[
        [0,  0, 28, c, 0.8],  [0,  28, 28, '#10b981', 0.7],
        [22, 0, 38, c, 0.8],  [22, 38, 18, '#10b981', 0.7],
        [44, 0, 20, c, 0.8],  [44, 20, 36, '#10b981', 0.7],
        [66, 0, 42, c, 0.8],  [66, 42, 14, '#10b981', 0.7],
        [88, 0, 10, c, 0.8],  [88, 10, 46, '#10b981', 0.7],
      ].map(([x, y, h2, fill, op], i) => (
        <rect key={i} x={x} y={y} width="18" height={h2} rx="1" fill={fill} fillOpacity={op} />
      ))}
    </svg>
  )

  if (type === 'bullet') return (
    <svg viewBox="0 0 120 56" style={{ width: '100%', height: h, display: 'block' }}>
      <rect x="4"  y="22" width="112" height="12" rx="3" fill="rgba(255,255,255,.08)" />
      <rect x="4"  y="22" width="76"  height="12" rx="3" fill={c} fillOpacity=".7" />
      <rect x="88" y="17" width="3"   height="22" rx="1.5" fill="#f59e0b" />
      <text x="60" y="48" textAnchor="middle" fontSize="9" fill="#94a3b8">76 / 88</text>
    </svg>
  )

  if (type === 'heatmap') return (
    <svg viewBox="0 0 120 56" style={{ width: '100%', height: h, display: 'block' }}>
      {[
        [.8,.4,.2,.6],[.3,.9,.7,.3],[.5,.3,.6,.8],[.2,.7,.4,.5]
      ].map((row, ri) =>
        row.map((op, ci) => (
          <rect key={`${ri}-${ci}`} x={ci * 28 + 2} y={ri * 13 + 1} width="25" height="11" rx="2"
                fill={c} fillOpacity={op} />
        ))
      )}
    </svg>
  )

  if (type === 'radar') return (
    <svg viewBox="0 0 120 56" style={{ width: '100%', height: h, display: 'block' }}>
      <g transform="translate(60,30)">
        <polygon points="0,-26 22,-8 14,20 -14,20 -22,-8" fill="none" stroke="rgba(255,255,255,.1)" strokeWidth="1" />
        <polygon points="0,-14 12,-4 8,10 -8,10 -12,-4" fill="none" stroke="rgba(255,255,255,.07)" strokeWidth="1" />
        {['0,-26','22,-8','14,20','-14,20','-22,-8'].map((pt, i) => (
          <line key={i} x1="0" y1="0" x2={pt.split(',')[0]} y2={pt.split(',')[1]} stroke="rgba(255,255,255,.08)" strokeWidth="1" />
        ))}
        <polygon points="0,-20 18,-6 10,16 -10,16 -16,-5" fill={c} fillOpacity=".25" stroke={c} strokeWidth="1.5" />
      </g>
    </svg>
  )

  if (type === 'text') return (
    <div style={{ display: 'flex', flexDirection: 'column', justifyContent: 'center', padding: '4px 8px', height: h, gap: 4 }}>
      {['███████████████', '████████████', '██████████████████', '█████████'].map((t, i) => (
        <div key={i} style={{ fontSize: 6, letterSpacing: 1, color: i === 0 ? '#e2e8f0' : '#475569', opacity: 1 - i * 0.15 }}>{t}</div>
      ))}
    </div>
  )

  if (type === 'scatter') return (
    <svg viewBox="0 0 120 56" style={{ width: '100%', height: h, display: 'block' }}>
      <line x1="8" y1="52" x2="8" y2="4" stroke="rgba(255,255,255,.12)" strokeWidth="1" />
      <line x1="8" y1="52" x2="116" y2="52" stroke="rgba(255,255,255,.12)" strokeWidth="1" />
      {[[22,38],[42,18],[68,28],[84,12],[55,44],[30,22],[96,20],[75,38]].map(([x,y], i) => (
        <circle key={i} cx={x} cy={y} r={i % 3 === 0 ? 3.5 : 2.5} fill={c} fillOpacity={.55 + (i % 3) * .1} />
      ))}
    </svg>
  )

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

  if (type === 'gantt') return (
    <svg viewBox="0 0 120 56" style={{ width: '100%', height: h, display: 'block' }}>
      <rect x="6"  y="8"  width="48" height="7" rx="2" fill={c} />
      <rect x="32" y="20" width="58" height="7" rx="2" fill={`rgba(${rgb[0]},${rgb[1]},${rgb[2]},0.6)`} />
      <rect x="14" y="32" width="38" height="7" rx="2" fill={soft} />
      <rect x="50" y="44" width="44" height="7" rx="2" fill={`rgba(${rgb[0]},${rgb[1]},${rgb[2]},0.45)`} />
    </svg>
  )

  if (type === 'map_tr' || type === 'map_world' || type === 'map_bubble') return (
    <svg viewBox="0 0 120 56" style={{ width: '100%', height: h, display: 'block' }}>
      <path d="M16,26 Q30,12 52,20 Q72,14 96,22 Q108,32 98,42 Q72,52 46,45 Q24,42 16,26 Z" fill={soft} stroke={c} strokeWidth="1.2" />
      {type === 'map_bubble' ? (
        <>
          <circle cx="44" cy="30" r="5" fill={c} fillOpacity="0.6" />
          <circle cx="68" cy="34" r="8" fill={c} fillOpacity="0.5" />
          <circle cx="86" cy="27" r="4" fill={c} fillOpacity="0.6" />
        </>
      ) : (
        <path d="M52,20 Q72,14 96,22 Q108,32 98,42 Q86,46 74,42 Z" fill={`rgba(${rgb[0]},${rgb[1]},${rgb[2]},0.5)`} />
      )}
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
