/**
 * formManagementService — Form Yöneticisi API servisi
 *
 * Backend: FormsController (/api/forms) + DatabaseMetadataController (/api/database)
 *
 * Fonksiyonlar:
 *   getForms()                        → Tüm formları listele
 *   getForm(id)                       → Tek form
 *   createForm(data)                  → Yeni form ekle
 *   updateForm(id, data)              → Formu güncelle
 *   deleteForm(id)                    → Formu sil
 *   getTables()                       → Fiziksel tabloları listele
 *   getTableColumns(tableName)        → Tablonun kolonlarını listele
 */

var FORMS_API = '/api/forms'
var DB_API = '/api/database'

/**
 * Tüm formları getirir.
 * @returns {Promise<Array>}
 */
export async function getForms() {
  var resp = await fetch(FORMS_API, {
    method: 'GET',
    credentials: 'same-origin',
    headers: { 'Accept': 'application/json' },
  })
  if (!resp.ok) throw new Error('getForms HTTP ' + resp.status)
  return await resp.json()
}

/**
 * Tek bir form kaydını getirir.
 * @param {number} id
 * @returns {Promise<object>}
 */
export async function getForm(id) {
  var resp = await fetch(FORMS_API + '/' + id, {
    method: 'GET',
    credentials: 'same-origin',
    headers: { 'Accept': 'application/json' },
  })
  if (!resp.ok) throw new Error('getForm HTTP ' + resp.status)
  return await resp.json()
}

/**
 * Yeni form ekler.
 * @param {object} data - { formCode, formName, module, subModule, sortOrder, isActive, baseTable, baseRecordKey }
 * @returns {Promise<object>}
 */
export async function createForm(data) {
  var resp = await fetch(FORMS_API, {
    method: 'POST',
    credentials: 'same-origin',
    headers: {
      'Accept': 'application/json',
      'Content-Type': 'application/json',
    },
    body: JSON.stringify(data),
  })
  var json = await resp.json()
  if (!resp.ok) return { success: false, message: (json && json.message) || ('createForm HTTP ' + resp.status) }
  return { success: true, data: json }
}

/**
 * Mevcut formu günceller.
 * @param {number} id
 * @param {object} data
 * @returns {Promise<object>}
 */
export async function updateForm(id, data) {
  var resp = await fetch(FORMS_API + '/' + id, {
    method: 'PUT',
    credentials: 'same-origin',
    headers: {
      'Accept': 'application/json',
      'Content-Type': 'application/json',
    },
    body: JSON.stringify(data),
  })
  var json = await resp.json()
  if (!resp.ok) return { success: false, message: (json && json.message) || ('updateForm HTTP ' + resp.status) }
  return { success: true, data: json }
}

/**
 * Formu siler.
 * @param {number} id
 * @returns {Promise<object>}
 */
export async function deleteForm(id) {
  var resp = await fetch(FORMS_API + '/' + id, {
    method: 'DELETE',
    credentials: 'same-origin',
    headers: { 'Accept': 'application/json' },
  })
  var json = await resp.json()
  if (!resp.ok) return { success: false, message: (json && json.message) || ('deleteForm HTTP ' + resp.status) }
  return { success: true }
}

/**
 * Veritabanındaki fiziksel tabloları getirir.
 * @returns {Promise<Array<{schema, tableName, fullName}>>}
 */
export async function getTables() {
  var resp = await fetch(DB_API + '/tables', {
    method: 'GET',
    credentials: 'same-origin',
    headers: { 'Accept': 'application/json' },
  })
  if (!resp.ok) throw new Error('getTables HTTP ' + resp.status)
  return await resp.json()
}

/**
 * Belirtilen tablonun kolon adlarını getirir.
 * @param {string} tableName - "schema.table" veya "table" formatında
 * @returns {Promise<Array<string>>}
 */
export async function getTableColumns(tableName) {
  var resp = await fetch(DB_API + '/tables/' + encodeURIComponent(tableName) + '/columns', {
    method: 'GET',
    credentials: 'same-origin',
    headers: { 'Accept': 'application/json' },
  })
  if (!resp.ok) throw new Error('getTableColumns HTTP ' + resp.status)
  return await resp.json()
}
