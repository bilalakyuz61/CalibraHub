// Makine Planlama — Kapasite / Yük Raporu API istemcisi.
// Sözleşme KİLİTLİ (bkz. görev talimatı) — endpoint/parametre adlarını değiştirme,
// yalnız backend ekibiyle koordine değişir.
var BASE = '/Production'

function getJson(url) {
  return fetch(url, { credentials: 'same-origin' }).then(function (r) { return r.json() })
}

// fromIso / toIso: UTC ISO-8601 string ("...Z"); bucket: "day" | "week"
export function getCapacityLoad(fromIso, toIso, bucket) {
  var qs = '?from=' + encodeURIComponent(fromIso) + '&to=' + encodeURIComponent(toIso) +
    '&bucket=' + encodeURIComponent(bucket || 'day')
  return getJson(BASE + '/CapacityLoadData' + qs)
}
