/**
 * shellShortcutsService — Header hızlı-erişim (kısayol) çubuğu için
 * fetch + adapter katmanı. Diğer *Service.js dosyalarıyla aynı desen
 * (bkz. dashboardService.js): getJson/postJson benzeri yardımcılar + CSRF çözümleme.
 *
 * Backend sözleşmesi (AccountController):
 *   GET  /Account/GetShellShortcuts   → { config: string|null }   (config = JSON string)
 *   POST /Account/SaveShellShortcuts  body { config: string }     → { ok: boolean }
 *   Saklama: IUserSettingRepository → UserSettings, key = "ui.shell.shortcuts"
 *   (UiConfigurationService üzerinden; grid kolon tercihleriyle aynı yer/desen).
 *   UserSettings ŞİRKET veritabanındadır → tercih kullanıcı VE şirket bazında ayrıdır.
 *
 * KALICILIK KAYNAĞI SUNUCUDUR. localStorage yalnızca yedek aynadır: kaydetme
 * her zaman önce local'e yazar (garanti), sonra sunucuya best-effort POST atar;
 * okuma önce sunucuyu dener, yalnız sunucuya ulaşılamazsa local'e düşer.
 *
 * Local anahtar ŞİRKET + KULLANICI ile kapsanır (2026-08-25). Aksi halde tek bir
 * genel anahtar tüm şirketler için paylaşılırdı: sunucu erişilemediğinde ya da
 * aynı tarayıcıyı iki kullanıcı kullandığında BAŞKA şirketin/kişinin kısayolları
 * gösterilirdi. Sunucu tarafı bu ayrımı zaten yapıyor; ayna da yapmalı.
 *
 * Config yapısı: { ids: string[], showNames: boolean }
 *   ids = MenuDefinition.MenuNode.Key değerleri (string) — Dashboard'un
 *   QuickLinksWidget'i (settings.items[].key) ile aynı "menü string-key"
 *   yaklaşımı. Menü düğümleri INT PK'li bir DB entity'si değil, sabit
 *   string key ile tanımlanan statik bir katalogdur — ID-tabanlı eşleştirme
 *   kuralının doğal istisnasıdır.
 */

var BASE = '/Account'

/** Kapsamsız ESKİ anahtar — yalnızca tek seferlik devralma için okunur (bkz. adoptLegacyLocal). */
var LEGACY_LOCAL_KEY = 'calibra.shell.shortcuts'

/**
 * Şirket + kullanıcı kapsamlı localStorage anahtarı.
 * Değerler Shell config'inden okunur; şirket değişimi tam sayfa gezinmesi yaptığı
 * için (Shell.jsx → window.location.href = '/') bu değerler her zaman güncel claim'i
 * yansıtır. Kimlik çözülemezse "anon" ile kapsanır — yanlış şirkete yazmaktansa
 * ayrı bir kovada kalması yeğdir.
 */
function localKey() {
  var companyId = 'anon'
  var userKey = 'anon'
  try {
    var cfg = window.__CALIBRA_SHELL_CONFIG__ || {}
    if (cfg.system && cfg.system.companyId) companyId = String(cfg.system.companyId)
    if (cfg.user && cfg.user.userKey) userKey = String(cfg.user.userKey)
  } catch (e) { /* config yok — kapsamsız kovaya düş */ }
  return LEGACY_LOCAL_KEY + '.' + companyId + '.' + userKey
}

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

/**
 * Kapsamlı anahtar YOKken duran eski kapsamsız kaydı bir kez devralır ve eski
 * anahtarı SİLER. Silme şart: aksi halde aynı eski kayıt açılan ikinci şirkete de
 * kopyalanır — düzeltmeye çalıştığımız karışmanın ta kendisi olurdu.
 *
 * Yalnızca sunucuda kayıt bulunamadığında çağrılır; sunucu değeri her zaman kazanır.
 */
function adoptLegacyLocal() {
  try {
    var scoped = localKey()
    if (localStorage.getItem(scoped)) return
    var legacy = localStorage.getItem(LEGACY_LOCAL_KEY)
    if (!legacy) return
    localStorage.setItem(scoped, legacy)
    localStorage.removeItem(LEGACY_LOCAL_KEY)
  } catch (e) { /* quota/private — devralma zorunlu değil, sessiz geç */ }
}

function readLocal() {
  try {
    var raw = localStorage.getItem(localKey())
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
  try { localStorage.setItem(localKey(), JSON.stringify(config)) } catch (e) { /* quota/private — sessiz geç */ }
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
  } catch (e) { /* ağ/oturum hatası — yedek aynaya düş */ }
  // Buraya düşmek "sunucuda kayıt yok VEYA sunucuya ulaşılamadı" demektir; eski
  // kapsamsız kayıt varsa bu şirket/kullanıcı adına devralınır (tek seferlik).
  adoptLegacyLocal()
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
    }).catch(function () { /* ağ hatası — localStorage zaten güncel, sonraki kaydetmede sunucuya yazılır */ })
  } catch (e) { /* ignore */ }
}

export default {
  loadShellShortcuts: loadShellShortcuts,
  saveShellShortcuts: saveShellShortcuts,
}
