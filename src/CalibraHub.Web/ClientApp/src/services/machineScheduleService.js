// Makine Planlama (Üretim Çizelgeleme) — API istemcisi.
// API sözleşmesi KİLİTLİ (bkz. plan glittery-finding-comet.md) — endpoint/parametre
// adlarını değiştirme, yalnız backend ekibiyle koordine değişir.
var BASE = '/Production'

function getCsrf() {
  var el = document.querySelector('input[name="__RequestVerificationToken"]')
  return el ? el.value : ''
}

function postJson(url, body) {
  return fetch(url, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'RequestVerificationToken': getCsrf(),
    },
    credentials: 'same-origin',
    body: JSON.stringify(body),
  }).then(function (r) { return r.json() })
}

function getJson(url) {
  return fetch(url, { credentials: 'same-origin' }).then(function (r) { return r.json() })
}

// fromIso / toIso: UTC ISO-8601 string ("...Z")
export function getScheduleData(fromIso, toIso) {
  var qs = '?from=' + encodeURIComponent(fromIso) + '&to=' + encodeURIComponent(toIso)
  return getJson(BASE + '/MachineScheduleData' + qs)
}

// payload: { id, machineId, workOrderOperationId, startUtc, endUtc, blockType, status, notes }
export function saveScheduleBlock(payload) {
  return postJson(BASE + '/SaveScheduleBlock', payload)
}

export function deleteScheduleBlock(id) {
  return postJson(BASE + '/DeleteScheduleBlock', { id: id })
}
