// Makine Planlama Faz 2 — çalışma-dışı saat + resmi tatil gölgeleme hesaplayıcıları.
// Sözleşme (KİLİTLİ): dayOfWeek 0=Pazar..6=Cumartesi (JS Date.getDay() ile birebir);
// startMinute/endMinute yerel gün-ortası dakikası 0..1440; holiday.date="yyyy-MM-dd" (yerel).
import { dateToX } from './timeScale'

function startOfLocalDay(date) {
  var d = new Date(date)
  d.setHours(0, 0, 0, 0)
  return d
}

function addDays(date, days) {
  var d = new Date(date)
  d.setDate(d.getDate() + days)
  return d
}

export function localDateKey(date) {
  var y = date.getFullYear()
  var m = String(date.getMonth() + 1).padStart(2, '0')
  var day = String(date.getDate()).padStart(2, '0')
  return y + '-' + m + '-' + day
}

/**
 * Her makine satırı için çalışma-dışı gölge dikdörtgenleri üretir.
 * Karar (kilitli — parent görev talimatı): bir makinenin workWindows dizisinde HİÇ kaydı
 * yoksa o makine 7/24 çalışıyor varsayılır → gölgeleme atlanır. En az bir gün için pencere
 * tanımlıysa, o makinenin penceresi olmayan diğer günleri de tümüyle çalışma-dışı sayılır.
 */
export function buildWorkWindowShades(machines, workWindows, rangeStart, rangeEnd, pxPerHour, rowHeight) {
  if (!machines || machines.length === 0) return []
  var byMachine = {}
  ;(workWindows || []).forEach(function (w) {
    if (!byMachine[w.machineId]) byMachine[w.machineId] = []
    byMachine[w.machineId].push(w)
  })

  var rects = []
  machines.forEach(function (m, rowIndex) {
    var windows = byMachine[m.id]
    if (!windows || windows.length === 0) return // hiç pencere tanımlı değil → 7/24 varsay, gölge yok

    var byDay = {}
    windows.forEach(function (w) {
      if (!byDay[w.dayOfWeek]) byDay[w.dayOfWeek] = []
      byDay[w.dayOfWeek].push(w)
    })

    var cursor = startOfLocalDay(rangeStart)
    var guard = 0
    while (cursor <= rangeEnd && guard < 400) {
      guard++
      var dayStart = cursor
      var dow = dayStart.getDay()
      var dayWindows = (byDay[dow] || []).slice().sort(function (a, b) { return a.startMinute - b.startMinute })

      var gaps = []
      if (dayWindows.length === 0) {
        gaps.push([0, 1440])
      } else {
        var cursorMin = 0
        dayWindows.forEach(function (w) {
          if (w.startMinute > cursorMin) gaps.push([cursorMin, w.startMinute])
          if (w.endMinute > cursorMin) cursorMin = w.endMinute
        })
        if (cursorMin < 1440) gaps.push([cursorMin, 1440])
      }

      gaps.forEach(function (g) {
        var gStart = new Date(dayStart.getTime() + g[0] * 60000)
        var gEnd = new Date(dayStart.getTime() + g[1] * 60000)
        if (gEnd <= rangeStart || gStart >= rangeEnd) return
        var clippedStart = gStart < rangeStart ? rangeStart : gStart
        var clippedEnd = gEnd > rangeEnd ? rangeEnd : gEnd
        var x1 = dateToX(clippedStart, rangeStart, pxPerHour)
        var x2 = dateToX(clippedEnd, rangeStart, pxPerHour)
        if (x2 <= x1) return
        rects.push({
          key: m.id + '_' + localDateKey(dayStart) + '_' + g[0],
          x: x1,
          y: rowIndex * rowHeight,
          width: x2 - x1,
          height: rowHeight,
        })
      })

      cursor = addDays(cursor, 1)
    }
  })
  return rects
}

/** Aralıktaki tatil günlerinin x/genişlik + ad bilgisi — header şeridi + gövde gölgesi ortak kaynağı. */
export function buildHolidayColumns(holidays, rangeStart, rangeEnd, pxPerHour) {
  if (!holidays || holidays.length === 0) return []
  var byDate = {}
  holidays.forEach(function (h) { byDate[h.date] = h })

  var cols = []
  var cursor = startOfLocalDay(rangeStart)
  var guard = 0
  while (cursor <= rangeEnd && guard < 400) {
    guard++
    var key = localDateKey(cursor)
    var h = byDate[key]
    if (h) {
      var dayStart = cursor
      var dayEnd = addDays(cursor, 1)
      var clippedStart = dayStart < rangeStart ? rangeStart : dayStart
      var clippedEnd = dayEnd > rangeEnd ? rangeEnd : dayEnd
      var x1 = dateToX(clippedStart, rangeStart, pxPerHour)
      var x2 = dateToX(clippedEnd, rangeStart, pxPerHour)
      if (x2 > x1) cols.push({ key: key, x: x1, width: x2 - x1, name: h.name || 'Resmi Tatil' })
    }
    cursor = addDays(cursor, 1)
  }
  return cols
}
