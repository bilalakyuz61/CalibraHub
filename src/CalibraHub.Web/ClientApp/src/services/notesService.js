/**
 * Notes API Service — NotesController JSON endpointleri
 */

var BASE = '/Notes'

function postJson(url, body) {
  return fetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'same-origin',
    body: JSON.stringify(body),
  }).then(function (r) { return r.json() })
}

export function getAll() {
  return fetch(BASE + '/GetAllJson', { credentials: 'same-origin' })
    .then(function (r) {
      if (!r.ok) throw new Error('HTTP ' + r.status)
      return r.json()
    })
}

export function saveNote(note) {
  return postJson(BASE + '/SaveJson', {
    id: note.id || null,
    folderId: note.folderId || null,
    title: note.title || '',
    content: note.content || '',
    // Mod 2 E2E: sifreli not ise content alani JSON payload tutar
    isFullyEncrypted: !!note.isFullyEncrypted,
    encryptionHint:   note.encryptionHint || null,
  })
}

export function deleteNote(id) {
  return postJson(BASE + '/DeleteJson', { id: id })
}

export function saveFolder(name, parentFolderId) {
  return postJson(BASE + '/SaveFolderJson', {
    name: name,
    parentFolderId: parentFolderId || null,
  })
}

export function renameFolder(id, name) {
  return postJson(BASE + '/RenameFolderJson', { id: id, name: name })
}

export function deleteFolder(id) {
  return postJson(BASE + '/DeleteFolderJson', { id: id })
}

export function togglePin(id) {
  return postJson(BASE + '/TogglePinJson', { id: id })
}
