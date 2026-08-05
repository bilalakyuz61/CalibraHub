// Makine Çalışma Takvimi — API istemcisi (Faz 2 admin ekranı).
// API sözleşmesi KİLİTLİ (backend Faz 2 kontratı) — endpoint/parametre adlarını değiştirme.
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

export function getCalendarData() {
  return getJson(BASE + '/MachineCalendarData')
}

// payload: { id, machineId, dayOfWeek, startMinute, endMinute }
export function saveWorkWindow(payload) {
  return postJson(BASE + '/SaveMachineWorkWindow', payload)
}

export function deleteWorkWindow(id) {
  return postJson(BASE + '/DeleteMachineWorkWindow', { id: id })
}

// payload: { id, date:"yyyy-MM-dd", name }
export function saveHoliday(payload) {
  return postJson(BASE + '/SaveHoliday', payload)
}

export function deleteHoliday(id) {
  return postJson(BASE + '/DeleteHoliday', { id: id })
}
