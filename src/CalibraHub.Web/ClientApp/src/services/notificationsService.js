/**
 * Notifications API Service — /Notifications endpointleri
 * Shell navbar bell dropdown + ReminderNotificationWorker tarafindan yazilan
 * uygulama ici bildirimler.
 */

var BASE = '/Notifications'

function postJson(url, body) {
  return fetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'same-origin',
    body: JSON.stringify(body || {}),
  }).then(function (r) { return r.json() })
}

export function list(take) {
  var url = BASE + '/ListJson' + (take ? '?take=' + take : '')
  return fetch(url, { credentials: 'same-origin' })
    .then(function (r) {
      if (!r.ok) throw new Error('HTTP ' + r.status)
      return r.json()
    })
}

export function unreadCount() {
  return fetch(BASE + '/UnreadCountJson', { credentials: 'same-origin' })
    .then(function (r) { return r.ok ? r.json() : { unreadCount: 0 } })
    .catch(function () { return { unreadCount: 0 } })
}

export function markRead(id) {
  return postJson(BASE + '/MarkReadJson', { id: id })
}

export function markAllRead() {
  return postJson(BASE + '/MarkAllReadJson', {})
}
