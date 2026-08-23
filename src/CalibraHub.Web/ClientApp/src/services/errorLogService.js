// Hata Logları — /Admin/ErrorLogData API istemcisi.
// Sözleşme KİLİTLİ (bkz. görev talimatı) — endpoint/parametre adlarını değiştirme,
// yalnız backend ekibiyle koordine değişir.
var BASE = '/Admin'

function getJson(url) {
  return fetch(url, { credentials: 'same-origin' }).then(function (r) { return r.json() })
}

// params: { from, to, level, q, page, pageSize } — from/to "yyyy-MM-dd", level "all|error|critical"
export function getErrorLogs(params) {
  params = params || {}
  var qs = new URLSearchParams()
  if (params.from) qs.set('from', params.from)
  if (params.to) qs.set('to', params.to)
  qs.set('level', params.level || 'all')
  if (params.q) qs.set('q', params.q)
  qs.set('page', String(params.page || 1))
  qs.set('pageSize', String(params.pageSize || 50))
  return getJson(BASE + '/ErrorLogData?' + qs.toString())
}
