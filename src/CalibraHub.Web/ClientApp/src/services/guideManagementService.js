/**
 * guideManagementService — Rehber Yönetimi API servisi
 *
 * Backend: GuidesController (/api/guides)
 *
 * Fonksiyonlar:
 *   getCatalog()                  → Tüm rehberleri listele
 *   listGuideViews()              → cbv_Guide_% view'larını listele
 *   getViewColumns(viewName)      → Bir view'ın kolon listesi
 *   upsertGuide(data)             → Rehber ekle/güncelle
 *   deleteGuide(id)               → Rehberi devre dışı bırak
 */

var API = '/api/guides'

/**
 * Tüm aktif rehberlerin kataloğunu getirir.
 * @returns {Promise<Array<{id,guideCode,guideLabel,valueColumn,displayColumn,columns}>>}
 */
export async function getCatalog() {
  var resp = await fetch(API, {
    method: 'GET',
    credentials: 'same-origin',
    headers: { 'Accept': 'application/json' },
  })
  if (!resp.ok) throw new Error('getCatalog HTTP ' + resp.status)
  return await resp.json()
}

/**
 * cbv_Guide_% pattern'ine uyan SQL view'larını listeler.
 * Her view için kolon listesi de gelir.
 * @returns {Promise<Array<{viewName, schemaName, columns: string[]}>>}
 */
export async function listGuideViews() {
  var resp = await fetch(API + '/views', {
    method: 'GET',
    credentials: 'same-origin',
    headers: { 'Accept': 'application/json' },
  })
  if (!resp.ok) throw new Error('listGuideViews HTTP ' + resp.status)
  return await resp.json()
}

/**
 * Belirli bir view'ın kolon adlarını getirir.
 * @param {string} viewName
 * @returns {Promise<string[]>}
 */
export async function getViewColumns(viewName) {
  if (!viewName) throw new Error('viewName gerekli')
  var resp = await fetch(API + '/views/' + encodeURIComponent(viewName) + '/columns', {
    method: 'GET',
    credentials: 'same-origin',
    headers: { 'Accept': 'application/json' },
  })
  if (!resp.ok) throw new Error('getViewColumns HTTP ' + resp.status)
  return await resp.json()
}

/**
 * Rehber ekle (id=0) veya güncelle (id>0).
 * @param {{id,guideLabel,viewName,valueColumn,displayColumn,gridColumns,defaultSortColumn,guideCode}} data
 * @returns {Promise<{success:true,id:number}>}
 */
export async function upsertGuide(data) {
  if (!data) throw new Error('data gerekli')
  var resp = await fetch(API, {
    method: 'POST',
    credentials: 'same-origin',
    headers: {
      'Content-Type': 'application/json',
      'Accept': 'application/json',
    },
    body: JSON.stringify({
      id: data.id || 0,
      guideLabel: data.guideLabel,
      viewName: data.viewName,
      valueColumn: data.valueColumn,
      displayColumn: data.displayColumn,
      gridColumns: Array.isArray(data.gridColumns) ? data.gridColumns : [],
      defaultSortColumn: data.defaultSortColumn || null,
      guideCode: data.guideCode || null,
    }),
  })
  var json = null
  try { json = await resp.json() } catch (e) { /* skip */ }
  if (!resp.ok) {
    var msg = (json && json.message) || ('upsertGuide HTTP ' + resp.status)
    return { success: false, message: msg }
  }
  return { success: true, id: (json && json.id) || 0 }
}

/**
 * Rehber içinde kayıt arar — GuideTryModal tarafından kullanılır.
 * @param {string} guideCode
 * @param {{search?:string, page?:number, pageSize?:number}} params
 * @returns {Promise<{rows,columns,page,pageSize,hasMore}>}
 */
export async function searchGuide(guideCode, params) {
  if (!guideCode) throw new Error('guideCode gerekli')
  params = params || {}
  var qs = new URLSearchParams()
  if (params.search)   qs.set('search',    params.search)
  if (params.page)     qs.set('page',      String(params.page))
  if (params.pageSize) qs.set('pageSize',  String(params.pageSize))
  var url = API + '/' + encodeURIComponent(guideCode) + '?' + qs.toString()
  var resp = await fetch(url, {
    method: 'GET',
    credentials: 'same-origin',
    headers: { 'Accept': 'application/json' },
  })
  if (!resp.ok) throw new Error('searchGuide HTTP ' + resp.status)
  return await resp.json()
}

/**
 * Rehberi devre dışı bırakır (soft-delete).
 * @param {number} id
 * @returns {Promise<{success:boolean}>}
 */
export async function deleteGuide(id) {
  if (!id || id <= 0) throw new Error('id gerekli')
  var resp = await fetch(API + '/' + id, {
    method: 'DELETE',
    credentials: 'same-origin',
  })
  var json = null
  try { json = await resp.json() } catch (e) { /* skip */ }
  if (!resp.ok) {
    var msg = (json && json.message) || ('deleteGuide HTTP ' + resp.status)
    return { success: false, message: msg }
  }
  return { success: true }
}
