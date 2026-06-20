/**
 * dashboardService — Ana sayfa özelleştirilebilir pano (Dashboard) için
 * fetch + adapter katmanı. Tüm /HomeDashboard/* endpoint'leri yalnızca buradan
 * çağrılır; bileşenler URL bilmez.
 *
 * Rota notu: Backend controller'ı HomeDashboardController (`/HomeDashboard/*`).
 * `/Dashboard` rapor tasarımcısı için ayrılmış; çakışmayı önlemek için ayrı prefix.
 *
 * Backend sözleşmesi (HomeDashboardController, camelCase JSON):
 *   GET  /HomeDashboard/Config
 *        → { ok, config: { pages:[{id,label,widgets:[{type,size,settings,height}]}],
 *                          catalog:[{type,title,description,icon,iconColor,defaultSize,allowMultiple}],
 *                          quickLinkOptions:[{key,label,url,icon,groupLabel}] } }
 *   POST /HomeDashboard/SavePages    body { pages:[{id,label,widgets:[...]}] }  → { ok }
 *   POST /HomeDashboard/ResetPages                                               → { ok, config }
 *   GET  /HomeDashboard/PendingApprovals      → { ok, data:{ totalCount, url } }
 *   GET  /HomeDashboard/ExchangeRates?codes=  → { ok, items:[...] }
 *   GET  /HomeDashboard/RecentDocuments?take= → { ok, items:[...] }
 *   GET  /HomeDashboard/WorkOrderSummary      → { ok, data:{ ... } }
 *   GET  /HomeDashboard/SalesQuoteSummary     → { ok, data:{ ... } }
 *   GET  /HomeDashboard/StockAlerts?take=     → { ok, items:[...], configured }
 */

var BASE = '/HomeDashboard'

/**
 * CSRF token oku — iki kaynaktan dener:
 * 1) Workspace iframe: gizli <input name="__RequestVerificationToken">
 * 2) Shell modu: window.__CALIBRA_SHELL_CONFIG__.antiforgeryToken
 */
function readCsrfToken() {
  try {
    var input = document.querySelector('input[name="__RequestVerificationToken"]')
    if (input && input.value) return input.value
    var shellCfg = window.__CALIBRA_SHELL_CONFIG__
    if (shellCfg && shellCfg.antiforgeryToken) return shellCfg.antiforgeryToken
    return ''
  } catch (e) {
    return ''
  }
}

/** Ortak GET helper — JSON parse + hata yönetimi. */
async function getJson(url) {
  var resp = await fetch(url, {
    credentials: 'same-origin',
    headers: { 'Accept': 'application/json', 'X-Requested-With': 'XMLHttpRequest' },
  })
  if (resp.status === 401 || resp.status === 403) {
    throw new Error('Bu içeriği görüntüleme yetkiniz yok.')
  }
  if (!resp.ok) {
    throw new Error('İstek başarısız (HTTP ' + resp.status + ')')
  }
  var data = await resp.json()
  if (data && data.ok === false) {
    throw new Error(data.error || 'Sunucu hatası')
  }
  return data
}

/** Ortak POST helper — JSON body + CSRF token. */
async function postJson(url, body) {
  var token = readCsrfToken()
  var resp = await fetch(url, {
    method: 'POST',
    credentials: 'same-origin',
    headers: {
      'Content-Type': 'application/json',
      'Accept': 'application/json',
      'X-Requested-With': 'XMLHttpRequest',
      'RequestVerificationToken': token,
      'X-CSRF-TOKEN': token,
    },
    body: JSON.stringify(body || {}),
  })
  if (resp.status === 401 || resp.status === 403) {
    throw new Error('Yetki reddedildi.')
  }
  if (!resp.ok) {
    throw new Error('Kaydetme başarısız (HTTP ' + resp.status + ')')
  }
  var data = await resp.json()
  if (data && data.ok === false) {
    throw new Error(data.error || 'Sunucu hatası')
  }
  return data
}

/* ── Config / layout ────────────────────────────────────────── */

/**
 * Pano konfigürasyonunu yükle. Backend `{ ok, config }` döner; bu fonksiyon
 * Dashboard.jsx'in beklediği düz şekle normalize eder:
 *   { userPages, availableWidgets, quickLinkOptions }
 */
export async function getConfig() {
  var d = await getJson(BASE + '/Config')
  var cfg = (d && d.config) || {}
  return {
    userPages: Array.isArray(cfg.pages) ? cfg.pages : [],
    availableWidgets: Array.isArray(cfg.catalog) ? cfg.catalog : [],
    quickLinkOptions: Array.isArray(cfg.quickLinkOptions) ? cfg.quickLinkOptions : [],
  }
}

/**
 * Kullanıcı sayfa düzenini kaydet.
 * @param {Array<{id:string,label:string,widgets:Array}>} pages
 */
export function savePages(pages) {
  return postJson(BASE + '/SavePages', { pages: Array.isArray(pages) ? pages : [] })
}

/** Sayfa düzenini varsayılana sıfırla; dönüş normalize edilmiş config. */
export async function resetPages() {
  var d = await postJson(BASE + '/ResetPages', {})
  var cfg = (d && d.config) || {}
  return {
    userPages: Array.isArray(cfg.pages) ? cfg.pages : [],
    availableWidgets: Array.isArray(cfg.catalog) ? cfg.catalog : [],
    quickLinkOptions: Array.isArray(cfg.quickLinkOptions) ? cfg.quickLinkOptions : [],
  }
}

/* ── Per-widget data (zarf açılır + düz şekle normalize) ─────── */

export async function getPendingApprovals() {
  var d = await getJson(BASE + '/PendingApprovals')
  var data = (d && d.data) || {}
  return { count: data.totalCount || 0, url: data.url || '/PendingApproval' }
}

/**
 * @param {string[]} codes - örn. ['USD','EUR','GBP']
 */
export async function getExchangeRates(codes) {
  var qs = (Array.isArray(codes) && codes.length > 0)
    ? '?codes=' + encodeURIComponent(codes.join(','))
    : ''
  var d = await getJson(BASE + '/ExchangeRates' + qs)
  return { items: (d && d.items) || [] }
}

export async function getRecentDocuments(take) {
  var n = take && take > 0 ? take : 8
  var d = await getJson(BASE + '/RecentDocuments?take=' + n)
  return { items: (d && d.items) || [] }
}

export async function getWorkOrders() {
  var d = await getJson(BASE + '/WorkOrderSummary')
  var data = (d && d.data) || {}
  return {
    planned: data.planned || 0,
    released: data.released || 0,
    inProgress: data.inProgress || 0,
    completed: data.completed || 0,
    total: data.totalActive || 0,
    url: data.url || '/Production/WorkOrders',
  }
}

export async function getSalesQuotes() {
  var d = await getJson(BASE + '/SalesQuoteSummary')
  var data = (d && d.data) || {}
  return {
    draft: data.draft || 0,
    pending: data.pending || 0,
    approved: data.approved || 0,
    total: data.total || 0,
    openTotal: data.openTotal,
    currency: data.currency,
    url: data.url || '/Sales/Quotes',
  }
}

export async function getStockAlerts(take) {
  var n = take && take > 0 ? take : 8
  var d = await getJson(BASE + '/StockAlerts?take=' + n)
  return { items: (d && d.items) || [], configured: !!(d && d.configured) }
}

export default {
  getConfig: getConfig,
  savePages: savePages,
  resetPages: resetPages,
  getPendingApprovals: getPendingApprovals,
  getExchangeRates: getExchangeRates,
  getRecentDocuments: getRecentDocuments,
  getWorkOrders: getWorkOrders,
  getSalesQuotes: getSalesQuotes,
  getStockAlerts: getStockAlerts,
}
