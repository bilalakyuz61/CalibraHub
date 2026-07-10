// İşlem Logları — AuditMonitor + AuditTrailPanel ortak yardımcıları

export const ACTION_META = {
  insert:      { cls: 'insert',      dot: 'insert' },
  update:      { cls: 'update',      dot: 'update' },
  delete:      { cls: 'delete',      dot: 'delete' },
  login:       { cls: 'login',       dot: 'event' },
  loginfailed: { cls: 'loginfailed', dot: 'delete' },
  logout:      { cls: 'logout',      dot: 'event' },
  event:       { cls: 'event',       dot: 'event' },
}

/** UTC ISO → "10.07.2026 21:34" (yerel saat, tr-TR) */
export function formatTs(iso) {
  if (!iso) return ''
  const d = new Date(iso)
  if (isNaN(d.getTime())) return String(iso)
  return d.toLocaleDateString('tr-TR', { day: '2-digit', month: '2-digit', year: 'numeric' }) +
    ' ' + d.toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' })
}

/** Değişiklik listesi → kısa önizleme metni ("Miktar, Birim Fiyat, +2") */
export function changePreview(changes) {
  if (!changes || !changes.length) return ''
  const names = changes.slice(0, 2).map(c => c.label || c.field)
  const rest = changes.length - names.length
  return names.join(', ') + (rest > 0 ? ' +' + rest : '')
}
