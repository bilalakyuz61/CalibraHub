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
