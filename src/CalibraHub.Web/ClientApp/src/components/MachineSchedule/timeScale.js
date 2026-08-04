// Makine Planlama — zaman <-> piksel dönüşüm yardımcıları.
// Sözleşme: API her zaman UTC ISO-8601 ("...Z") döner/bekler. Ekranda YEREL saat
// gösterilir (JS Date zaten yerel saat dilimine göre render eder); dönüşümde
// elle offset hesaplamaya gerek yok — new Date(isoZ) ve date.toISOString() yeterli.

export var SNAP_MINUTES = 15
export var MIN_DURATION_MINUTES = 15
export var ROW_HEIGHT = 56
export var HEADER_HEIGHT = 44
export var MACHINE_COL_WIDTH = 176

export var ZOOM_LEVELS = [10, 20, 40, 80, 160] // px / saat
export var DEFAULT_ZOOM_INDEX = 2 // 40 px/saat

export function hoursBetween(a, b) {
  return (b.getTime() - a.getTime()) / 3_600_000
}

export function dateToX(date, rangeStart, pxPerHour) {
  return hoursBetween(rangeStart, date) * pxPerHour
}

export function xToDate(x, rangeStart, pxPerHour) {
  return new Date(rangeStart.getTime() + (x / pxPerHour) * 3_600_000)
}

export function snapDate(date, snapMinutes) {
  var ms = (snapMinutes || SNAP_MINUTES) * 60_000
  var rounded = Math.round(date.getTime() / ms) * ms
  return new Date(rounded)
}

export function localDateInputToUtcIso(dateStr, endOfDay) {
  // dateStr: "YYYY-MM-DD" (input type=date değeri, yerel gün)
  if (!dateStr) return null
  var d = new Date(dateStr + (endOfDay ? 'T23:59:59' : 'T00:00:00'))
  return d.toISOString()
}

export function utcIsoToLocalDateInput(iso) {
  if (!iso) return ''
  var d = new Date(iso)
  var y = d.getFullYear()
  var m = String(d.getMonth() + 1).padStart(2, '0')
  var day = String(d.getDate()).padStart(2, '0')
  return y + '-' + m + '-' + day
}

export function formatHm(date) {
  var h = String(date.getHours()).padStart(2, '0')
  var m = String(date.getMinutes()).padStart(2, '0')
  return h + ':' + m
}

export function formatDayLabel(date) {
  return date.toLocaleDateString('tr-TR', { day: '2-digit', month: 'short', weekday: 'short' })
}

export function addMinutes(date, minutes) {
  return new Date(date.getTime() + minutes * 60_000)
}

export function diffMinutes(a, b) {
  return (b.getTime() - a.getTime()) / 60_000
}
