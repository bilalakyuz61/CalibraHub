// Makine Planlama — Kapasite / Yük Raporu — ısı haritası yardımcıları.

// Doluluk % → renk bandı. utilizationPct null ise "kapasite yok" (gri) bandı döner.
export function utilizationBand(pct) {
  if (pct === null || pct === undefined) return 'nodata'
  if (pct > 100) return 'over'
  if (pct >= 85) return 'warn'
  if (pct >= 50) return 'mid'
  return 'ok'
}

export function formatPct(pct) {
  if (pct === null || pct === undefined) return '—'
  return Math.round(pct) + '%'
}

export function minutesToHm(minutes) {
  if (minutes === null || minutes === undefined) return '0dk'
  var m = Math.round(minutes)
  var h = Math.floor(m / 60)
  var rest = m % 60
  if (h <= 0) return rest + 'dk'
  if (rest === 0) return h + 'sa'
  return h + 'sa ' + rest + 'dk'
}

export function cellTooltip(cell) {
  if (!cell) return ''
  var lines = []
  if (cell.utilizationPct === null || cell.utilizationPct === undefined) {
    lines.push('Kapasite tanımlı değil')
  } else {
    lines.push('Doluluk: ' + formatPct(cell.utilizationPct))
  }
  lines.push('Yük: ' + minutesToHm(cell.loadMinutes) + ' / Kapasite: ' + minutesToHm(cell.capacityMinutes))
  if (cell.overloadMinutes && cell.overloadMinutes > 0) {
    lines.push('Aşım: ' + minutesToHm(cell.overloadMinutes))
  }
  return lines.join('\n')
}

export function todayInputValue(offsetDays) {
  var d = new Date()
  d.setDate(d.getDate() + (offsetDays || 0))
  var y = d.getFullYear()
  var m = String(d.getMonth() + 1).padStart(2, '0')
  var day = String(d.getDate()).padStart(2, '0')
  return y + '-' + m + '-' + day
}

export function localDateInputToUtcIso(dateStr, endOfDay) {
  if (!dateStr) return null
  var d = new Date(dateStr + (endOfDay ? 'T23:59:59' : 'T00:00:00'))
  return d.toISOString()
}
