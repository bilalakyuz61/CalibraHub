/**
 * safeExpr — kural/formül ifadeleri için TEK ve sertleştirilmiş expr-eval kapısı.
 *
 * NEDEN (2026-08-24 güvenlik denetimi, ORTA):
 * `expr-eval@2.x` prototype pollution zafiyeti taşıyor (GHSA-8gw3-rxh4-v6jx) ve
 * paketin yayımlanmış bir düzeltmesi YOK. İstismarın yolu ÜYE ERİŞİMİ:
 * `a.constructor.prototype.x = ...` gibi bir ifade, nokta operatörü açık olduğu
 * sürece scope nesnesinin prototipine yazabiliyor.
 *
 * CalibraHub'da kural ifadeleri düz (noktasız) alan adlarıyla çalışır — scope
 * `{ fieldKey: value }` biçiminde kurulur, iç içe nesne yoktur. Dolayısıyla
 * `member` operatörünü kapatmak hiçbir meşru ifadeyi kırmaz ama istismar yolunu
 * tamamen keser. İkinci savunma katmanı olarak tehlikeli tanımlayıcılar metin
 * düzeyinde de reddedilir.
 *
 * Kullanım: `evaluateBool(expr, scope, fallback)` / `parseSafe(expr)`.
 * Doğrudan `new Parser()` KULLANMAYIN — yeni bir kural motoru yazarken bu modülü içe aktarın.
 */
import { Parser } from 'expr-eval'

// Yalnızca ihtiyaç duyulan operatörler. `member: false` = zafiyetin kapatıldığı yer.
var OPERATORS = {
  add: true, concatenate: false, conditional: true,
  divide: true, factorial: false, multiply: true,
  power: true, remainder: true, subtract: true,
  logical: true, comparison: true,
  'in': false, assignment: false,
  member: false,
}

var parser = new Parser({ operators: OPERATORS })

// Metin düzeyinde savunma — parser sürümü değişse bile bu isimler geçmemeli.
var DANGEROUS = /(__proto__|constructor|prototype)/i

export function parseSafe(expr) {
  var text = String(expr == null ? '' : expr)
  if (DANGEROUS.test(text)) throw new Error('Ifade guvenli olmayan tanimlayici iceriyor.')
  return parser.parse(text)
}

/** Kural değerlendirme — hata durumunda `fallback` döner (fail-open sözleşmesi korunur). */
export function evaluateBool(expr, scope, fallback) {
  if (!expr) return fallback
  try {
    return parseSafe(expr).evaluate(scope) === true
  } catch (e) {
    return fallback
  }
}

/** Sayısal/serbest sonuç için değerlendirme; hata olursa `undefined`. */
export function evaluate(expr, scope) {
  try { return parseSafe(expr).evaluate(scope) } catch (e) { return undefined }
}

export default { parseSafe, evaluateBool, evaluate }
