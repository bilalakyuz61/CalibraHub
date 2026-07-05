/**
 * decimalSettings — form bazında ondalık ayarlarının frontend runtime'ı.
 *
 * Backend kaynağı: GET /Decimals/Effective?formCode=X (DecimalRuntimeController).
 * Çözümleme backend'de yapılır: form kaydı → şirket varsayılanı ('*') → fallback.
 * Ayara ulaşılamazsa güvenli fallback (2,2,2,2,4) döner — ekran asla bloklanmaz.
 *
 * Kullanım (yeni ekran yazarken bu modül yeterlidir — otomatik dahil olma
 * mekanizması budur):
 *   import { loadDecimalSettings, roundTo } from '../../utils/decimalSettings'
 *   loadDecimalSettings('SALES_QUOTE').then(dec => {
 *     const tutar = roundTo(qty * price, dec.amount)
 *   })
 *
 * Razor/vanilla sayfalardan: window.CalibraHub.decimals.load(formCode).then(...)
 * (mount.jsx global expose eder).
 */

var FALLBACK = { formCode: '*', quantity: 2, unitPrice: 2, fxUnitPrice: 4, amount: 2, rate: 2, exchangeRate: 4, source: 'fallback' }
var cache = {} // formCode -> Promise<settings>
var changeListeners = []

/* Ondalık Ayarları ekranı kaydedince 'calibra-decimals-refresh' kanalına yayın
   yapar — tüm açık workspace iframe'lerinde cache düşer, aboneler tazelenir.
   Böylece ayar değişikliği AÇIK ekranlara da yeniden yükleme gerektirmeden yansır. */
try {
  var __bc = new BroadcastChannel('calibra-decimals-refresh')
  __bc.onmessage = function () {
    cache = {}
    changeListeners.forEach(function (fn) { try { fn() } catch (e) { /* ignore */ } })
  }
} catch (e) { /* BroadcastChannel yok — yeni açılan ekranlar yine güncel alır */ }

/** Ayar değişim aboneliği — unsubscribe fonksiyonu döner. */
export function onDecimalSettingsChanged(cb) {
  changeListeners.push(cb)
  return function () {
    var i = changeListeners.indexOf(cb)
    if (i >= 0) changeListeners.splice(i, 1)
  }
}

export function loadDecimalSettings(formCode) {
  var code = formCode || '*'
  if (!cache[code]) {
    cache[code] = fetch('/Decimals/Effective?formCode=' + encodeURIComponent(code), { credentials: 'same-origin' })
      .then(function (r) { return r.ok ? r.json() : null })
      .then(function (d) {
        if (d && d.ok) {
          return {
            formCode: d.formCode, quantity: d.quantity, unitPrice: d.unitPrice,
            fxUnitPrice: d.fxUnitPrice != null ? d.fxUnitPrice : 4,
            amount: d.amount, rate: d.rate, exchangeRate: d.exchangeRate, source: d.source,
          }
        }
        return Object.assign({}, FALLBACK, { formCode: code })
      })
      .catch(function () { return Object.assign({}, FALLBACK, { formCode: code }) })
  }
  return cache[code]
}

/* Yarıda-yukarı (ticari) yuvarlama — 0.5 her zaman yukarı. Backend
   Math.Round(AwayFromZero) ile eşleşir; toFixed'in banker's-rounding
   sürprizlerinden kaçınır. */
export function roundTo(value, decimals) {
  var n = typeof value === 'number' ? value : parseFloat(String(value).replace(',', '.'))
  if (isNaN(n) || !isFinite(n)) return 0
  var d = decimals == null ? 2 : decimals
  var f = Math.pow(10, d)
  return Math.sign(n) * Math.round(Math.abs(n) * f + 1e-9) / f
}

/**
 * Grid kolonu → ondalık kategorisi eşleme. Öncelik:
 *   1) col.decimalKind ('quantity'|'unitPrice'|'fxUnitPrice'|'amount'|'rate'|'exchangeRate')
 *      — C# grid config'i açıkça bildirebilir (yeni ekranlar için önerilen yol)
 *   2) col.type + key heuristics: number→quantity, percent→rate,
 *      currency→(computed/total→amount, döviz fiyatı→fxUnitPrice, değilse unitPrice),
 *      kur→exchangeRate
 * Eşleşme yoksa null döner — kolonun kendi precision'ı korunur.
 */
export function resolveColumnDecimals(col, dec) {
  if (!dec || !col) return null
  var kind = col.decimalKind
  if (!kind) {
    var type = String(col.type || '').toLowerCase()
    var key = String(col.key || '').toLowerCase()
    if (/exchange|kur/.test(key)) kind = 'exchangeRate'
    else if (/fxprice|fx_price|dovizfiyat|doviz_fiyat|foreignprice|currencyprice/.test(key)) kind = 'fxUnitPrice'
    else if (type === 'percent' || /rate|oran|iskonto|kdv/.test(key)) kind = 'rate'
    else if (type === 'currency') kind = (col.computed || /total|amount|tutar|toplam/.test(key)) ? 'amount' : 'unitPrice'
    else if (type === 'number' && /qty|quantity|miktar|adet/.test(key)) kind = 'quantity'
    else if (type === 'number') kind = 'quantity' // satır gridlerinde çıplak number = miktar
  }
  switch (kind) {
    case 'quantity':     return dec.quantity
    case 'unitPrice':    return dec.unitPrice
    case 'fxUnitPrice':  return dec.fxUnitPrice
    case 'amount':       return dec.amount
    case 'rate':         return dec.rate
    case 'exchangeRate': return dec.exchangeRate
    default:             return null
  }
}
