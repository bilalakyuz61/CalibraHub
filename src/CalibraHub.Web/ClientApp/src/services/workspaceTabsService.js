/**
 * workspaceTabsService — Shell açık-sekme (workspace tabs) kalıcılığı için
 * fetch katmanı. PageComment Seq 1123: "kullanıcı ve şirket bazında açık
 * sayfalar veri tabanında tutulmalı, kullanıcı aynı şirkete tekrar girişte
 * veya şirket değişikliği ile ilgili şirkete geçince açık ekranlarını aynen
 * bulmalı". Desen shellShortcutsService.js ile birebir aynı (bkz. o dosyadaki
 * yorum): KALICILIK KAYNAĞI SUNUCUDUR, localStorage yalnızca hızlı ilk boyama
 * + sunucu erişilemezse çalışmaya devam etmek için yerel ayna.
 *
 * Backend sözleşmesi (UiConfigController — bkz. Controllers/UiConfigController.cs):
 *   GET  /UiConfig/WorkspaceTabs   → { ok:true, saved:false }                (hiç kayıt yok)
 *                                   veya { ok:true, saved:true, tabs:[...] } (kayıt var, boş dizi olabilir)
 *   POST /UiConfig/WorkspaceTabs   body { tabs:[...] } → { ok:true }
 *   Saklama şirket veritabanında, kullanıcı bazlı — bu yüzden şirket değişince
 *   (per-company DB) sunucu zaten doğal olarak ayrışır; local ayna da aynı
 *   ayrımı ŞİRKET+KULLANICI anahtarıyla yapar (bkz. localKey()).
 *
 * "saved:false" ile "saved:true, tabs:[]" farkı Shell.jsx tarafında KORUNUR:
 * ilki "hiç kayıt yok / ilk ziyaret", ikincisi "kullanıcı bilerek tüm
 * sekmeleri kapattı" anlamına gelir. Bu servis farkı olduğu gibi taşır.
 */

var BASE = '/UiConfig'

/** Şirket + kullanıcı kapsamlı localStorage anahtarı üretir. */
function localKey() {
  var companyId = 'anon'
  var userKey = 'anon'
  try {
    var cfg = window.__CALIBRA_SHELL_CONFIG__ || {}
    if (cfg.system && cfg.system.companyId) companyId = String(cfg.system.companyId)
    if (cfg.user && cfg.user.userKey) userKey = String(cfg.user.userKey)
  } catch (e) { /* config yok — kapsamsız kovaya düş */ }
  return 'calibra.workspace.tabs.' + companyId + '.' + encodeURIComponent(userKey)
}

/** dashboardService.js / shellShortcutsService.js ile aynı CSRF çözümleme sırası. */
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

/** Şirket+kullanıcı kapsamlı anahtardır — Shell.jsx zaten aynı hesabı kendi tabsStorageKey'inde tutuyor. */
export function workspaceTabsLocalKey() {
  return localKey()
}

/**
 * Sunucudaki kayıtlı sekme durumunu getirir.
 * @returns {Promise<{ saved: boolean, tabs: Array }>} saved=false → hiç kayıt yok (ilk ziyaret ayrımı Shell'de yapılır)
 */
export async function fetchWorkspaceTabs() {
  var resp = await fetch(BASE + '/WorkspaceTabs', {
    credentials: 'same-origin',
    headers: { 'Accept': 'application/json', 'X-Requested-With': 'XMLHttpRequest' },
  })
  if (!resp.ok) throw new Error('WorkspaceTabs GET ' + resp.status)
  var data = await resp.json()
  if (!data || data.ok !== true) throw new Error('WorkspaceTabs GET beklenmeyen yanıt')
  return {
    saved: !!data.saved,
    tabs: Array.isArray(data.tabs) ? data.tabs : [],
  }
}

/**
 * Açık sekme listesini sunucuya yazar (best-effort, çağıran taraf debounce eder).
 * @param {Array} tabs
 * @returns {Promise<boolean>} ok mi
 */
export async function saveWorkspaceTabs(tabs) {
  var resp = await fetch(BASE + '/WorkspaceTabs', {
    method: 'POST',
    credentials: 'same-origin',
    headers: {
      'Content-Type': 'application/json',
      'RequestVerificationToken': readCsrfToken(),
    },
    body: JSON.stringify({ tabs: Array.isArray(tabs) ? tabs : [] }),
  })
  if (!resp.ok) throw new Error('WorkspaceTabs POST ' + resp.status)
  var data = await resp.json()
  return !!(data && data.ok)
}

export default {
  workspaceTabsLocalKey: workspaceTabsLocalKey,
  fetchWorkspaceTabs: fetchWorkspaceTabs,
  saveWorkspaceTabs: saveWorkspaceTabs,
}
