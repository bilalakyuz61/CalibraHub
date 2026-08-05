// Makine Çalışma Takvimi — saat:dk (HH:mm) <-> gün-ortası dakika (0..1440) dönüşümü.
// Sözleşme (KİLİTLİ): startMinute/endMinute yerel duvar-saati dakikası; dayOfWeek
// 0=Pazar..6=Cumartesi (JS Date.getDay() ile birebir).

export var DAY_ORDER = [1, 2, 3, 4, 5, 6, 0] // Pzt..Paz görüntüleme sırası
export var DAY_LABELS = { 0: 'Pazar', 1: 'Pazartesi', 2: 'Salı', 3: 'Çarşamba', 4: 'Perşembe', 5: 'Cuma', 6: 'Cumartesi' }
export var DAY_LABELS_SHORT = { 0: 'Paz', 1: 'Pzt', 2: 'Sal', 3: 'Çar', 4: 'Per', 5: 'Cum', 6: 'Cmt' }

export function minutesToTime(min) {
  var m = Math.max(0, Math.min(1440, Number(min) || 0))
  var hh = Math.floor(m / 60)
  var mm = m % 60
  return String(hh).padStart(2, '0') + ':' + String(mm).padStart(2, '0')
}

export function timeToMinutes(str) {
  if (!str) return 0
  var parts = str.split(':')
  var h = parseInt(parts[0], 10) || 0
  var m = parseInt(parts[1], 10) || 0
  return h * 60 + m
}
