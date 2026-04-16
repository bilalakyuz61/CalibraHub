/**
 * widgetConfigService
 *
 * Widget konfigurasyonu icin izole bir servis katmani.
 * Bilesenler DOGRUDAN localStorage KULLANMAZ, sadece bu servisi cagirir.
 *
 * Simdilik localStorage'a yaziyor. Gelecekte (C# backend hazir oldugunda)
 * sadece bu dosyanin icini fetch('/UserPreferences/...') ile degistirecegiz.
 * Bilesen koduna dokunmadan API'ye gecis mumkun olacak.
 *
 * Config yapisi:
 *   {
 *     visibleIds: ['unit', 'type', 'status'],
 *     order:      ['status', 'unit', 'type'],
 *     colors:     { unit: 'cyan', type: 'slate', status: 'emerald' }
 *   }
 * Not: isListableOverrides kaldirildi — widget gorunurlugu artik sadece visibleIds ile yonetilir.
 */

var STORAGE_PREFIX = 'calibra.widgets.'

/**
 * Kaydedilmis widget konfigurasyonunu dondur.
 * @param {string} gridKey - Benzersiz anahtar (ornek: 'logistics-material-cards')
 * @returns {object|null} Config objesi veya kayit yoksa null
 */
export function loadWidgetConfig(gridKey) {
  if (!gridKey) return null
  try {
    var raw = localStorage.getItem(STORAGE_PREFIX + gridKey)
    if (!raw) return null
    var parsed = JSON.parse(raw)
    // Minimal validasyon: visibleIds ve order dizi olmali
    if (!parsed || !Array.isArray(parsed.visibleIds)) return null
    return {
      visibleIds: parsed.visibleIds || [],
      order: Array.isArray(parsed.order) ? parsed.order : parsed.visibleIds,
      colors: (parsed.colors && typeof parsed.colors === 'object') ? parsed.colors : {},
    }
  } catch (e) {
    console.warn('[widgetConfigService] loadWidgetConfig error:', e)
    return null
  }
}

/**
 * Widget konfigurasyonunu kaydet.
 * @param {string} gridKey
 * @param {object} config - { visibleIds, order, colors }
 * @returns {Promise<void>}
 */
export async function saveWidgetConfig(gridKey, config) {
  if (!gridKey || !config) return
  try {
    var payload = {
      visibleIds: Array.isArray(config.visibleIds) ? config.visibleIds : [],
      order: Array.isArray(config.order) ? config.order : [],
      colors: (config.colors && typeof config.colors === 'object') ? config.colors : {},
    }
    localStorage.setItem(STORAGE_PREFIX + gridKey, JSON.stringify(payload))
  } catch (e) {
    console.error('[widgetConfigService] saveWidgetConfig error:', e)
    throw e
  }
}

/**
 * Widget konfigurasyonunu sifirla (default'a don).
 * @param {string} gridKey
 * @returns {Promise<void>}
 */
export async function resetWidgetConfig(gridKey) {
  if (!gridKey) return
  try {
    localStorage.removeItem(STORAGE_PREFIX + gridKey)
  } catch (e) {
    console.error('[widgetConfigService] resetWidgetConfig error:', e)
    throw e
  }
}
