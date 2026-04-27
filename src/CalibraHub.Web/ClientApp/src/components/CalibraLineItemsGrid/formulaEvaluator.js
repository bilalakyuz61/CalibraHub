/**
 * formulaEvaluator.js — Computed grid hucreleri icin guvenli formul evaluator
 *
 * Kullanim:
 *   evaluate("quantity * unitPrice * (1 - discountRate/100)", { quantity: 10, unitPrice: 50, discountRate: 5 })
 *   → 475
 *
 * Guvenlik notu: `new Function` kullanir. Formul string'i **C#'tan server-side**
 * gelir (BuildDocumentLineGridConfig), kullanici kaynakli degil — XSS riski yok.
 * Yine de 'with' scoping ile yalnizca row objesinin anahtarlarini expose ederiz.
 */

var cache = {}

/**
 * @param {string} formula — Sadece aritmetik + alan adi iceren ifade
 * @param {object} row — Satir verisi (sayisal alanlar number olmali)
 * @returns {number} — NaN/invalid durumda 0
 */
export function evaluate(formula, row) {
  if (!formula || typeof formula !== 'string') return 0

  var fn = cache[formula]
  if (!fn) {
    try {
      // eslint-disable-next-line no-new-func
      fn = new Function('ctx', 'with (ctx) { return (' + formula + '); }')
      cache[formula] = fn
    } catch (e) {
      console.error('[formulaEvaluator] parse error:', formula, e)
      return 0
    }
  }

  try {
    // Null-safe ctx: row alanlarini sayisal yap, missing ise 0
    var ctx = {}
    Object.keys(row || {}).forEach(function(k) {
      var v = row[k]
      if (v == null || v === '') ctx[k] = 0
      else if (typeof v === 'number') ctx[k] = v
      else {
        var n = parseFloat(String(v).replace(',', '.'))
        ctx[k] = isNaN(n) ? 0 : n
      }
    })
    var result = fn(ctx)
    if (typeof result !== 'number' || !isFinite(result)) return 0
    return result
  } catch (e) {
    console.error('[formulaEvaluator] eval error:', formula, e)
    return 0
  }
}
