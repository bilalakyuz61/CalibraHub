// Konva canvas'a CSS değişkeni doğrudan verilemez (canvas context, DOM'dan bağımsız
// çizim yapar) — bu yüzden CLAUDE.md'nin izin verdiği ikinci yol izlenir: light/dark
// için iki ayrı JS paleti, aktif tema class'ına göre seçilir. Değerler MachineSchedule.css
// içindeki --ms-block-* değişkenleriyle senkron tutulmalı.

export var LIGHT_PALETTE = {
  bg: '#f4f6fa',
  headerBg: '#eef1f8',
  surface: '#ffffff',
  rowAlt: '#f8fafc',
  gridLine: '#e5e9f0',
  border: '#e2e8f0',
  text: '#0f172a',
  textMuted: '#64748b',
  accent: '#6366f1',
  block: {
    1: { fill: '#6366f1', text: '#ffffff', stroke: '#4338ca' }, // Production
    2: { fill: '#f59e0b', text: '#3a2405', stroke: '#b45309' }, // Setup
    3: { fill: '#8b5cf6', text: '#ffffff', stroke: '#6d28d9' }, // Maintenance
    4: { fill: '#94a3b8', text: '#1e293b', stroke: '#64748b' }, // Down
  },
  conflict: '#ef4444',
}

export var DARK_PALETTE = {
  bg: '#0b1220',
  headerBg: '#0f1a34',
  surface: '#111a2c',
  rowAlt: '#101a30',
  gridLine: '#1c2947',
  border: '#223050',
  text: '#e2e8f0',
  textMuted: '#8b9bbd',
  accent: '#818cf8',
  block: {
    1: { fill: '#6366f1', text: '#ffffff', stroke: '#a5b4fc' },
    2: { fill: '#d97706', text: '#fff7ed', stroke: '#fbbf24' },
    3: { fill: '#a78bfa', text: '#1e1240', stroke: '#ddd6fe' },
    4: { fill: '#475569', text: '#e2e8f0', stroke: '#94a3b8' },
  },
  conflict: '#f87171',
}

export function getPalette(isDark) {
  return isDark ? DARK_PALETTE : LIGHT_PALETTE
}

export var BLOCK_TYPE_LABELS = {
  1: 'Üretim',
  2: 'Hazırlık',
  3: 'Bakım',
  4: 'Duruş',
}

export var STATUS_LABELS = {
  1: 'Planlandı',
  2: 'Kilitli',
  3: 'Onaylandı',
}
