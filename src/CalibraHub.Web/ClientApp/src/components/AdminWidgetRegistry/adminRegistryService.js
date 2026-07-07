/**
 * adminRegistryService — Faz B
 *
 * Eski FormData + /Admin/SaveMaterialCardDynamicField akisi YERINE yeni JSON
 * API'sini (/api/widgets/*) kullanir.
 *
 * Backend kontrat:
 *   GET    /api/widgets/forms                        → FormCatalogItemDto[]
 *   GET    /api/widgets/forms/{formCode}/schema      → WidgetFormSchemaDto
 *   POST   /api/widgets/widgets                      → UpsertWidgetResponse { id }
 *   DELETE /api/widgets/widgets/{widgetId}           → { success }
 *
 * "Her Sey JSON" — CSRF token yok ([IgnoreAntiforgeryToken] controller'da).
 */

var API_BASE = '/api/widgets'

/**
 * Whitelist'teki formlari listeler. Admin module selector'unu besler.
 */
export async function listForms() {
  var resp = await fetch(API_BASE + '/forms', {
    method: 'GET',
    credentials: 'same-origin',
    headers: { 'Accept': 'application/json' },
  })
  if (!resp.ok) throw new Error('listForms HTTP ' + resp.status)
  return await resp.json()
}

/**
 * Bir formun widget tanimlarini getirir.
 * @param {string} formCode — 'ITEMS', 'CONTACTS', ...
 * @returns {Promise<{formId, formCode, formLabel, widgets: []}>}
 */
export async function getSchema(formCode) {
  if (!formCode) throw new Error('formCode bos olamaz')
  var resp = await fetch(API_BASE + '/forms/' + encodeURIComponent(formCode) + '/schema', {
    method: 'GET',
    credentials: 'same-origin',
    headers: { 'Accept': 'application/json' },
  })
  if (!resp.ok) throw new Error('getSchema HTTP ' + resp.status)
  return await resp.json()
}

/**
 * Widget olusturur veya gunceller.
 * @param {object} payload - UpsertWidgetRequest:
 *   { id?, formId, parentId?, widgetCode, label, dataType,
 *     maxLength?, sortOrder, options?, isActive }
 * @returns {Promise<{ success:true, id:number }>} veya throw
 */
export async function upsertWidget(payload) {
  if (!payload) throw new Error('payload bos olamaz')
  var resp = await fetch(API_BASE + '/widgets', {
    method: 'POST',
    credentials: 'same-origin',
    headers: {
      'Content-Type': 'application/json',
      'Accept': 'application/json',
    },
    body: JSON.stringify(payload),
  })
  var data = null
  try { data = await resp.json() } catch (e) { /* not json */ }
  if (!resp.ok) {
    var msg = (data && data.message) || ('upsertWidget HTTP ' + resp.status)
    return { success: false, message: msg }
  }
  return { success: true, id: (data && data.id) || 0 }
}

/**
 * Widget'i siler (baglı WidgetTra satirlari da cascade temizlenir — repository transaction).
 */
export async function deleteWidget(widgetId) {
  if (!widgetId || widgetId <= 0) throw new Error('widgetId gecersiz')
  var resp = await fetch(API_BASE + '/widgets/' + widgetId, {
    method: 'DELETE',
    credentials: 'same-origin',
  })
  var data = null
  try { data = await resp.json() } catch (e) { /* not json */ }
  if (!resp.ok) {
    var msg = (data && data.message) || ('deleteWidget HTTP ' + resp.status)
    return { success: false, message: msg }
  }
  return { success: true }
}

/**
 * Yalnizca siralama gunceller — OptionsJSON/RulesJSON dahil diger alanlara
 * dokunmaz. Reorder icin tam upsertWidget cagirmak lookup/grid/rehber
 * metadata'sinin kayipli yeniden insasini gerektiriyordu; bu endpoint o
 * hata sinifini ortadan kaldirir.
 * @param {Array<{id:number, sortOrder:number}>} items
 */
export async function patchSortOrders(items) {
  if (!Array.isArray(items) || items.length === 0) throw new Error('items bos olamaz')
  var resp = await fetch(API_BASE + '/widgets/sort-orders', {
    method: 'PATCH',
    credentials: 'same-origin',
    headers: {
      'Content-Type': 'application/json',
      'Accept': 'application/json',
    },
    body: JSON.stringify(items),
  })
  var data = null
  try { data = await resp.json() } catch (e) { /* not json */ }
  if (!resp.ok) {
    var msg = (data && data.message) || ('patchSortOrders HTTP ' + resp.status)
    return { success: false, message: msg }
  }
  return { success: true }
}

/**
 * Yalnizca aktif/pasif durumunu gunceller — diger alanlara dokunmaz.
 */
export async function patchWidgetActive(widgetId, isActive) {
  if (!widgetId || widgetId <= 0) throw new Error('widgetId gecersiz')
  var resp = await fetch(API_BASE + '/widgets/' + widgetId + '/active', {
    method: 'PATCH',
    credentials: 'same-origin',
    headers: {
      'Content-Type': 'application/json',
      'Accept': 'application/json',
    },
    body: JSON.stringify({ isActive: isActive === true }),
  })
  var data = null
  try { data = await resp.json() } catch (e) { /* not json */ }
  if (!resp.ok) {
    var msg = (data && data.message) || ('patchWidgetActive HTTP ' + resp.status)
    return { success: false, message: msg }
  }
  return { success: true }
}

/**
 * Yalnizca "Sade alan" (LabelStyle inline<->standard) toggle'i — backend
 * LabelStyle otoriter alanini gunceller, IsPlainField'i senkron tutar.
 */
export async function patchIsPlainField(widgetId, isPlainField) {
  if (!widgetId || widgetId <= 0) throw new Error('widgetId gecersiz')
  var resp = await fetch(API_BASE + '/widgets/' + widgetId + '/is-plain-field', {
    method: 'PATCH',
    credentials: 'same-origin',
    headers: {
      'Content-Type': 'application/json',
      'Accept': 'application/json',
    },
    body: JSON.stringify({ isPlainField: isPlainField === true }),
  })
  var data = null
  try { data = await resp.json() } catch (e) { /* not json */ }
  if (!resp.ok) {
    var msg = (data && data.message) || ('patchIsPlainField HTTP ' + resp.status)
    return { success: false, message: msg }
  }
  return { success: true }
}

// ═══════════════════════════════════════════════════════════
// Legacy exports — artik kullanilmayan ama import kiran olmayalim
// AdminWidgetRegistryPanel tarafinda referanslari Faz B'de temizliyoruz.
// ═══════════════════════════════════════════════════════════

/** @deprecated — Faz B'de upsertWidget ile degistirildi. */
export async function saveField() {
  throw new Error('saveField kaldirildi — upsertWidget kullanin.')
}

/** @deprecated */
export async function updateField() {
  throw new Error('updateField kaldirildi — upsertWidget kullanin.')
}

/** @deprecated */
export async function saveGroup() {
  throw new Error('saveGroup kaldirildi — upsertWidget (dataType=group) kullanin.')
}
