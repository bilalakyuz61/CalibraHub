/**
 * fieldSettingService — Sabit Alan Ayarlari API servisi
 *
 * Backend: FieldSettingsController (/api/field-settings)
 *
 * Fonksiyonlar:
 *   getFieldsByForm(formId)         → Formun alan ayarlari
 *   getFieldsByGuide(guideCode)     → Rehberin eslestirmeleri
 *   upsertField(data)               → Tekil alan ekle/guncelle
 *   bulkMapGuide(data)              → Toplu eslestirme kaydet
 *   deleteField(id)                 → Alan sil
 *   discoverFields(formId)          → Alan kesfi (INFORMATION_SCHEMA)
 *   getRuntimeBindings(formCode)    → Runtime baglantilari
 */

var API = '/api/field-settings'

/**
 * Bir formun tum alan ayarlarini getirir.
 * @param {number} formId
 * @returns {Promise<Array>}
 */
export async function getFieldsByForm(formId) {
  var resp = await fetch(API + '/form/' + formId, {
    method: 'GET',
    credentials: 'same-origin',
    headers: { 'Accept': 'application/json' },
  })
  if (!resp.ok) throw new Error('getFieldsByForm HTTP ' + resp.status)
  return await resp.json()
}

/**
 * Bir rehberle eslesmis tum alanlari getirir.
 * @param {string} guideCode
 * @returns {Promise<Array>}
 */
export async function getFieldsByGuide(guideCode) {
  var resp = await fetch(API + '/guide/' + encodeURIComponent(guideCode), {
    method: 'GET',
    credentials: 'same-origin',
    headers: { 'Accept': 'application/json' },
  })
  if (!resp.ok) throw new Error('getFieldsByGuide HTTP ' + resp.status)
  return await resp.json()
}

/**
 * Tekil alan ayari ekle/guncelle.
 * @param {object} data
 * @returns {Promise<{success:boolean, id?:number, message?:string}>}
 */
export async function upsertField(data) {
  var resp = await fetch(API, {
    method: 'POST',
    credentials: 'same-origin',
    headers: {
      'Content-Type': 'application/json',
      'Accept': 'application/json',
    },
    body: JSON.stringify(data),
  })
  var json = null
  try { json = await resp.json() } catch (e) { /* skip */ }
  if (!resp.ok) {
    var msg = (json && json.message) || ('upsertField HTTP ' + resp.status)
    return { success: false, message: msg }
  }
  return { success: true, id: (json && json.id) || 0 }
}

/**
 * Toplu rehber eslestirme kaydet.
 * @param {{guideCode:string, formId:number, fields:Array}} data
 * @returns {Promise<{success:boolean, message?:string}>}
 */
export async function bulkMapGuide(data) {
  var resp = await fetch(API + '/bulk-map', {
    method: 'POST',
    credentials: 'same-origin',
    headers: {
      'Content-Type': 'application/json',
      'Accept': 'application/json',
    },
    body: JSON.stringify(data),
  })
  var json = null
  try { json = await resp.json() } catch (e) { /* skip */ }
  if (!resp.ok) {
    var msg = (json && json.message) || ('bulkMapGuide HTTP ' + resp.status)
    return { success: false, message: msg }
  }
  return { success: true }
}

/**
 * Alan ayarini sil.
 * @param {number} id
 * @returns {Promise<{success:boolean}>}
 */
export async function deleteField(id) {
  var resp = await fetch(API + '/' + id, {
    method: 'DELETE',
    credentials: 'same-origin',
  })
  var json = null
  try { json = await resp.json() } catch (e) { /* skip */ }
  if (!resp.ok) {
    var msg = (json && json.message) || ('deleteField HTTP ' + resp.status)
    return { success: false, message: msg }
  }
  return { success: true }
}

/**
 * Alan kesfi — BaseTable uzerinden INFORMATION_SCHEMA.COLUMNS.
 * Zaten FldSet'te tanimli olanlari haric tutar.
 * @param {number} formId
 * @returns {Promise<string[]>}
 */
export async function discoverFields(formId, opts) {
  // opts.includeMapped=true → FldSet'e eslesmis kolonlar da dahil edilir
  // (Alan Rehberi widget tanimlama akisi tum kolonlari ister; FldSet
  // eslestirme sayfasi default false ile sadece eslesmemisleri ister).
  var qs = ''
  if (opts && opts.includeMapped === true) qs = '?includeMapped=true'
  var resp = await fetch(API + '/discover/' + formId + qs, {
    method: 'GET',
    credentials: 'same-origin',
    headers: { 'Accept': 'application/json' },
  })
  if (!resp.ok) throw new Error('discoverFields HTTP ' + resp.status)
  return await resp.json()
}

/**
 * Runtime: form icin rehber baglantilari.
 * @param {string} formCode
 * @returns {Promise<Array<{fieldKey,fieldLabel,guideCode,filterJson,isRequired}>>}
 */
export async function getRuntimeBindings(formCode) {
  var resp = await fetch(API + '/runtime/' + encodeURIComponent(formCode), {
    method: 'GET',
    credentials: 'same-origin',
    headers: { 'Accept': 'application/json' },
  })
  if (!resp.ok) throw new Error('getRuntimeBindings HTTP ' + resp.status)
  return await resp.json()
}

/**
 * FormCode + FieldKey ile alan ayari ekle/guncelle (GuideLookupCell Ayarlar paneli).
 * @param {{formCode,fieldKey,fieldLabel,guideCode?,filterJson?,isRequired,formatJson?}} data
 * @returns {Promise<{success:boolean, id?:number, message?:string}>}
 */
export async function upsertFieldByFormCode(data) {
  var resp = await fetch(API + '/by-form-code', {
    method: 'POST',
    credentials: 'same-origin',
    headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
    body: JSON.stringify(data),
  })
  var json = null
  try { json = await resp.json() } catch (e) { /* skip */ }
  if (!resp.ok) {
    var msg = (json && json.message) || ('upsertFieldByFormCode HTTP ' + resp.status)
    return { success: false, message: msg }
  }
  return { success: true, id: (json && json.id) || 0 }
}
