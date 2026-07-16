/**
 * columnConfigService
 *
 * SmartBoard TABLO modu (viewMode:'table') icin "Sutun Ayarlari" (SmartColumnSettings)
 * kalicilik katmani — per-user, BACKEND destekli. widgetConfigService.js'in aksine (o
 * sadece localStorage kullanir — kart modu icin degismeden kalir, bkz. CLAUDE.md
 * "CalibraSmartBoard (C-Grid)" + regresyonsuzluk kurali), bu servis AccountController
 * uzerinden gercek kalicilik saglar.
 *
 * Backend sozlesmesi (AccountController — col-persist paralel is, 2026-07-16 itibariyla
 * TAMAMLANDI):
 *   GET  /Account/GetBoardColumns?boardKey=<key> → { ok, config: string|null }
 *   POST /Account/SaveBoardColumns  body { boardKey, config }   → { ok }
 *   Saklama: UiConfigurationService.GetBoardColumnsAsync/SaveBoardColumnsAsync,
 *   user_settings key = "ui.board.columns.{boardKey}" (per-user, config opak JSON string).
 *
 * Desen shellShortcutsService.js ile BIREBIR ayni (CLAUDE.md'de referans verilen desen):
 *   load → once backend, hata/bos ise localStorage'a dus.
 *   save → localStorage'a HER ZAMAN hemen (garanti), backend'e best-effort POST
 *          (basarisiz olsa bile kullanici deneyimi bozulmaz — bir sonraki basarili
 *          GET'te veya sonraki save'de kendini duzeltir).
 *
 * Config yapisi (SmartColumnSettings.jsx sema — geriye donuk uyumlu: eski
 * {visibleIds, order, colors} sekli de sorunsuz okunur, "columns" yoksa {} varsayilir):
 *   {
 *     visibleIds: ['unit', 'type', ...],
 *     order:      ['type', 'unit', ...],
 *     columns: {
 *       '<id>': { align, width, pin, fontSize, fontWeight, label }
 *     }
 *   }
 */

var BASE = '/Account'
var LOCAL_PREFIX = 'calibra.board-columns.'

/** shellShortcutsService.js ile ayni CSRF cozumleme sirasi (form input → Shell config). */
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

/**
 * Herhangi bir ham objeyi kanonik sekle normalize eder — yeni {visibleIds,order,columns}
 * VEYA eski {visibleIds,order,colors} (colors sessizce yok sayilir) hicbir sekilde
 * crash etmez, eksik/bozuk alanlar guvenli varsayilana duser.
 */
export function normalizeColumnConfig(raw) {
  if (!raw || typeof raw !== 'object') return { visibleIds: [], order: [], columns: {} }

  var visibleIds = Array.isArray(raw.visibleIds)
    ? raw.visibleIds.filter(function (x) { return typeof x === 'string' })
    : []
  var order = Array.isArray(raw.order)
    ? raw.order.filter(function (x) { return typeof x === 'string' })
    : visibleIds.slice()

  var rawColumns = (raw.columns && typeof raw.columns === 'object' && !Array.isArray(raw.columns)) ? raw.columns : {}
  var columns = {}
  Object.keys(rawColumns).forEach(function (id) {
    var c = rawColumns[id]
    if (!c || typeof c !== 'object') return
    var entry = {}
    if (c.align === 'center' || c.align === 'right') entry.align = c.align
    if (typeof c.width === 'number' && isFinite(c.width) && c.width > 0) entry.width = Math.round(c.width)
    if (c.pin === true) entry.pin = true
    if (typeof c.fontSize === 'number' && isFinite(c.fontSize) && c.fontSize > 0) entry.fontSize = Math.round(c.fontSize)
    if (typeof c.fontWeight === 'number' && isFinite(c.fontWeight) && c.fontWeight > 0) entry.fontWeight = Math.round(c.fontWeight)
    if (typeof c.label === 'string' && c.label.trim()) entry.label = c.label.trim()
    if (Object.keys(entry).length > 0) columns[id] = entry
  })

  return { visibleIds: visibleIds, order: order, columns: columns }
}

function readLocal(boardKey) {
  if (!boardKey) return null
  try {
    var raw = localStorage.getItem(LOCAL_PREFIX + boardKey)
    if (!raw) return null
    return normalizeColumnConfig(JSON.parse(raw))
  } catch (e) {
    return null
  }
}

function writeLocal(boardKey, config) {
  if (!boardKey) return
  try { localStorage.setItem(LOCAL_PREFIX + boardKey, JSON.stringify(config)) } catch (e) { /* quota/private — sessiz gec */ }
}

/**
 * Kayitli sutun konfigurasyonunu getirir. Once backend, yoksa/hatta localStorage.
 * @param {string} boardKey
 * @returns {Promise<{visibleIds:string[], order:string[], columns:object}|null>}
 */
export async function loadBoardColumnConfig(boardKey) {
  if (!boardKey) return null
  try {
    var resp = await fetch(BASE + '/GetBoardColumns?boardKey=' + encodeURIComponent(boardKey), {
      credentials: 'same-origin',
      headers: { 'Accept': 'application/json', 'X-Requested-With': 'XMLHttpRequest' },
    })
    if (resp.ok) {
      var data = await resp.json()
      if (data && data.ok !== false && typeof data.config === 'string' && data.config) {
        return normalizeColumnConfig(JSON.parse(data.config))
      }
    }
  } catch (e) { /* backend erisilemedi / ag hatasi — localStorage'a dus */ }
  return readLocal(boardKey)
}

/**
 * Sutun konfigurasyonunu kaydeder — localStorage'a hemen (garanti),
 * backend'e best-effort POST (ShellShortcuts ile ayni desen — fetch hatasi
 * sessizce yutulur, localStorage zaten guncel oldugu icin kullanici kaybi olmaz).
 * @param {string} boardKey
 * @param {{visibleIds:string[], order:string[], columns:object}} config
 * @returns {{visibleIds:string[], order:string[], columns:object}} normalize edilmis config
 */
export function saveBoardColumnConfig(boardKey, config) {
  var normalized = normalizeColumnConfig(config)
  if (!boardKey) return normalized
  writeLocal(boardKey, normalized)
  try {
    fetch(BASE + '/SaveBoardColumns', {
      method: 'POST',
      credentials: 'same-origin',
      headers: {
        'Content-Type': 'application/json',
        'RequestVerificationToken': readCsrfToken(),
      },
      body: JSON.stringify({ boardKey: boardKey, config: JSON.stringify(normalized) }),
    }).catch(function () { /* backend gecici erisilemez — localStorage zaten guncel */ })
  } catch (e) { /* ignore */ }
  return normalized
}

export default {
  loadBoardColumnConfig: loadBoardColumnConfig,
  saveBoardColumnConfig: saveBoardColumnConfig,
  normalizeColumnConfig: normalizeColumnConfig,
}
