/**
 * shellShortcutsService — Header hızlı-erişim (kısayol) çubuğu için
 * fetch + adapter katmanı. Diğer *Service.js dosyalarıyla aynı desen
 * (bkz. dashboardService.js): getJson/postJson benzeri yardımcılar + CSRF çözümleme.
 *
 * Backend sözleşmesi (AccountController — 2026-07-16 itibarıyla HENÜZ YOK,
 * bkz. görev raporu / backend flag):
 *   GET  /Account/GetShellShortcuts   → { config: string|null }   (config = JSON string)
 *   POST /Account/SaveShellShortcuts  body { config: string }     → { ok: boolean }
 *   Önerilen saklama: IUserSettingRepository, key = "ui.shell.shortcuts"
 *   (UiConfigurationService üzerinden — GetGridColumnPreferencesAsync/
 *   SaveGridColumnPreferencesAsync ile aynı yerde, aynı pattern; sutun-paneli
 *   SKILL.md'deki GetColConfig/SaveColConfig ile birebir aynı yaklaşım).
 *
 * Backend hazır olana kadar: GET 404/hata dönerse localStorage'a düşer;
 * saveShellShortcuts HER ZAMAN localStorage'a hemen yazar + backend'e
 * best-effort POST atar (varsa kalıcı olur, yoksa sessizce yutulur). Böylece
 * özellik bugün de tam çalışır; backend eklenince bileşen kodu değişmeden
 * kullanıcı-bazlı DB kalıcılığına geçilir (widgetConfigService.js'teki ile
 * aynı "önce local, sonra API" geçiş stratejisi).
 *
 * Config yapısı: { ids: string[], showNames: boolean }
 *   ids = MenuDefinition.MenuNode.Key değerleri (string) — Dashboard'un
 *   QuickLinksWidget'i (settings.items[].key) ile aynı "menü string-key"
 *   yaklaşımı. Menü düğümleri INT PK'li bir DB entity'si değil, sabit
 *   string key ile tanımlanan statik bir katalogdur — ID-tabanlı eşleştirme
 *   kuralının doğal istisnasıdır (bkz. CLAUDE.md "Kullanıcı tarafından
 *   girilen kod alanı yok kuralı" → standart kod alanları benzeri durum).
 */

var BASE = '/Account'
var LOCAL_KEY = 'calibra.shell.shortcuts'

/** dashboardService.js ile aynı CSRF çözümleme sırası (form input → Shell config). */
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

function readLocal() {
  try {
    var raw = localStorage.getItem(LOCAL_KEY)
    if (!raw) return null
    var parsed = JSON.parse(raw)
    if (!parsed || typeof parsed !== 'object') return null
    return {
      ids: Array.isArray(parsed.ids) ? parsed.ids : [],
      showNames: !!parsed.showNames,
    }
  } catch (e) {
    return null
  }
}

function writeLocal(config) {
  try { localStorage.setItem(LOCAL_KEY, JSON.stringify(config)) } catch (e) { /* quota/private — sessiz geç */ }
}

/**
 * Kayıtlı kısayol konfigürasyonunu getirir. Önce backend, yoksa/hatalıysa localStorage.
 * @returns {Promise<{ids: string[], showNames: boolean}>}
 */
export async function loadShellShortcuts() {
  try {
    var resp = await fetch(BASE + '/GetShellShortcuts', {
      credentials: 'same-origin',
      headers: { 'Accept': 'application/json', 'X-Requested-With': 'XMLHttpRequest' },
    })
    if (resp.ok) {
      var data = await resp.json()
      if (data && typeof data.config === 'string' && data.config) {
        var parsed = JSON.parse(data.config)
        return {
          ids: Array.isArray(parsed.ids) ? parsed.ids : [],
          showNames: !!parsed.showNames,
        }
      }
    }
  } catch (e) { /* backend endpoint henüz yok / ağ hatası — localStorage'a düş */ }
  return readLocal() || { ids: [], showNames: false }
}

/**
 * Kısayol konfigürasyonunu kaydeder — localStorage'a hemen (garanti),
 * backend'e best-effort (endpoint eklenince otomatik kalıcı hale gelir).
 * @param {{ids: string[], showNames: boolean}} config
 */
export function saveShellShortcuts(config) {
  var normalized = {
    ids: Array.isArray(config.ids) ? config.ids : [],
    showNames: !!config.showNames,
  }
  writeLocal(normalized)
  try {
    fetch(BASE + '/SaveShellShortcuts', {
      method: 'POST',
      credentials: 'same-origin',
      headers: {
        'Content-Type': 'application/json',
        'RequestVerificationToken': readCsrfToken(),
      },
      body: JSON.stringify({ config: JSON.stringify(normalized) }),
    }).catch(function () { /* backend endpoint henüz yok / ağ hatası — localStorage zaten güncel */ })
  } catch (e) { /* ignore */ }
}

export default {
  loadShellShortcuts: loadShellShortcuts,
  saveShellShortcuts: saveShellShortcuts,
}
