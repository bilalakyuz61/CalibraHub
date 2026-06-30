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

// İçerik olmadan metadata listesi — ilk yükleme için hızlı
export function getList() {
  return fetch(BASE + '/GetListJson', { credentials: 'same-origin' })
    .then(function (r) {
      if (!r.ok) throw new Error('HTTP ' + r.status)
      return r.json()
    })
}

// Tek notun içeriğini lazy-load ile çeker
export function getContent(noteId) {
  return fetch(BASE + '/GetContentJson?noteId=' + encodeURIComponent(noteId), { credentials: 'same-origin' })
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
    tags: note.tags && note.tags.length > 0 ? JSON.stringify(note.tags) : null,
    linkedEntityType:  note.linkedEntityType  || null,
    linkedEntityId:    note.linkedEntityId    || null,
    linkedEntityLabel: note.linkedEntityLabel || null,
    visibility:        note.visibility        || 0,
  })
}

export function cloneNote(noteId) {
  return postJson(BASE + '/CloneNoteJson', { noteId: noteId })
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

export function getReminders(noteId) {
  return fetch(BASE + '/RemindersJson?noteId=' + encodeURIComponent(noteId), { credentials: 'same-origin' })
    .then(function (r) {
      if (!r.ok) throw new Error('HTTP ' + r.status)
      return r.json()
    })
}

export function addReminder(noteId, remindAtIso, recurrenceType, recurrenceData, deliveryChannel, targetUserIds) {
  return postJson(BASE + '/AddReminderJson', {
    noteId:          noteId,
    remindAt:        remindAtIso,
    recurrenceType:  recurrenceType || 0,
    recurrenceData:  recurrenceData || null,
    deliveryChannel: deliveryChannel || 0,  // 0=InApp, 1=Email, 2=Both
    targetUserIds:   Array.isArray(targetUserIds) ? targetUserIds : [],
  })
}

export function deleteReminder(reminderId, noteId) {
  return postJson(BASE + '/DeleteReminderJson', { reminderId: reminderId, noteId: noteId })
}

export function getCompanyUsers() {
  return fetch(BASE + '/CompanyUsersJson', { credentials: 'same-origin' })
    .then(function (r) {
      if (!r.ok) throw new Error('HTTP ' + r.status)
      return r.json()
    })
}

export function getAttachments(noteId) {
  return fetch(BASE + '/GetAttachments?noteId=' + encodeURIComponent(noteId), { credentials: 'same-origin' })
    .then(function (r) { if (!r.ok) throw new Error('HTTP ' + r.status); return r.json() })
}

export function uploadAttachment(noteId, file, description) {
  var fd = new FormData()
  fd.append('noteId', noteId)
  fd.append('file', file)
  if (description) fd.append('description', description)
  return fetch(BASE + '/UploadAttachment', { method: 'POST', credentials: 'same-origin', body: fd })
    .then(function (r) { if (!r.ok) throw new Error('HTTP ' + r.status); return r.json() })
}

export function deleteAttachment(id) {
  return fetch(BASE + '/DeleteAttachment?id=' + encodeURIComponent(id), { method: 'POST', credentials: 'same-origin' })
    .then(function (r) { if (!r.ok) throw new Error('HTTP ' + r.status); return r.json() })
}

export function getTrashed() {
  return fetch(BASE + '/TrashedJson', { credentials: 'same-origin' })
    .then(function (r) { if (!r.ok) throw new Error('HTTP ' + r.status); return r.json() })
}

export function restoreNote(id) {
  return postJson(BASE + '/RestoreNoteJson', { id: id })
}

export function permanentDeleteNote(id) {
  return postJson(BASE + '/PermanentDeleteNoteJson', { id: id })
}

export function entitySearch(type, q) {
  return fetch('/Notes/EntitySearchJson?type=' + encodeURIComponent(type) + '&q=' + encodeURIComponent(q), { credentials: 'same-origin' })
    .then(function (r) { if (!r.ok) throw new Error('HTTP ' + r.status); return r.json() })
}

export function importEvernote(file, folderId) {
  var fd = new FormData()
  fd.append('file', file)
  if (folderId) fd.append('folderId', folderId)
  return fetch(BASE + '/ImportEvernote', {
    method: 'POST',
    credentials: 'same-origin',
    body: fd,
  }).then(function (r) {
    if (!r.ok) throw new Error('HTTP ' + r.status)
    return r.json()
  })
}
