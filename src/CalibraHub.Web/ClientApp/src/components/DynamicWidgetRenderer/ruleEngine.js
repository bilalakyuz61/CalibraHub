/**
 * ruleEngine.js — Faz G Kural ve Formul Motoru (saf JS, React'tan bagimsiz)
 *
 * Sorumluluklar:
 *   1. buildRuleGraph(widgets) → ifadeleri parse et, bagimlilik haritasi kur,
 *      cycle detection (DFS 3-color), topological sort.
 *   2. recomputeAll(widgets, graph, values) → initial pass; tum formula/
 *      visibility/disabled alanlarini hesaplayip patch doner.
 *   3. propagateChange(changedCode, newValue, widgets, graph, state) → edit
 *      anli; etkilenen kapanimi BFS ile bul, topo sirali guncelle, batch patch.
 *
 * Katmanli dongu korumasi:
 *   - Compile-time: DFS cycle detection mount'ta → cycle varsa graph.cycle set edilir,
 *     renderer form'u cizmez (cycle banner gosterir).
 *   - Runtime: propagateChange icinde MAX_ITERATIONS guard.
 *
 * Guvenlik: expr-eval AST walker kullanir (no eval, no new Function).
 * Backend SanitizeRules() regex whitelist + yasakli kelime filtrelemesi yapmis.
 */
import { Parser } from 'expr-eval'

// Parser — sadece ihtiyacimiz olan operatorler aktif (guvenlik + netlik).
var parser = new Parser({
  operators: {
    add: true, concatenate: false, conditional: true,
    divide: true, factorial: false, multiply: true,
    power: true, remainder: true, subtract: true,
    logical: true, comparison: true,
    'in': false, assignment: false,
  },
})

var MAX_ITERATIONS = 500

// ════════════════════════════════════════════════════════════
// Tip coercion yardimcilari
// ════════════════════════════════════════════════════════════

/**
 * Widget value'sunu (UI'dan gelen ham deger) expr-eval icin uygun tipe cevirir.
 * expr-eval tip duyarlidir: "5" * "3" → NaN. Bu yuzden buradaki coerce kritik.
 */
function coerceForScope(raw, dataType) {
  if (raw == null || raw === '') {
    switch (dataType) {
      case 'numeric':     return 0
      case 'boolean':     return false
      case 'multi-select': return []
      default:            return ''
    }
  }
  switch (dataType) {
    case 'numeric': {
      var n = typeof raw === 'number' ? raw : parseFloat(raw)
      return Number.isFinite(n) ? n : 0
    }
    case 'boolean': {
      if (typeof raw === 'boolean') return raw
      var s = String(raw).toLowerCase()
      return s === 'true' || s === '1' || s === 'on' || s === 'yes'
    }
    case 'multi-select':
      return Array.isArray(raw) ? raw : (typeof raw === 'string' && raw ? raw.split(',') : [])
    case 'date':
    default:
      return String(raw)
  }
}

/**
 * Formul sonucunu widget'in dataType'ina gore string'e cevirir (WidgetTra
 * persistence formati). null/undefined → '' (widget bos).
 */
export function coerceResult(value, dataType) {
  if (value == null) return ''
  switch (dataType) {
    case 'numeric':
      if (typeof value === 'number') {
        if (!Number.isFinite(value)) return ''
        // Trailing zero olmadan: 5 → "5", 5.5 → "5.5", 5.00 → "5"
        return String(value)
      }
      var n = parseFloat(value)
      return Number.isFinite(n) ? String(n) : ''
    case 'boolean':
      return value ? 'true' : 'false'
    case 'date':
      return String(value)
    default:
      return String(value)
  }
}

/**
 * Tum widget'lar + current values → expr-eval scope objesi.
 * Sadece field widget'larini scope'a alir (grup/grid haric).
 */
export function buildScope(widgets, values) {
  var scope = {}
  for (var i = 0; i < widgets.length; i++) {
    var w = widgets[i]
    var dt = String(w.dataType || '').toLowerCase()
    if (dt === 'group' || dt === 'grid') continue
    var coerced = coerceForScope(values[w.widgetId], dt)
    scope[w.widgetId] = coerced
    // Formul editoru w_ prefix ekler (w_en, w_boy...) — hem prefix'li hem prefix'siz aliaslar
    if (w.widgetId.slice(0, 2) !== 'w_') {
      scope['w_' + w.widgetId] = coerced
    }
  }
  return scope
}

// ════════════════════════════════════════════════════════════
// buildRuleGraph — mount-time setup
// ════════════════════════════════════════════════════════════

/**
 * Widget listesinden kural graph'i uretir. Cycle varsa graph.cycle doldurulur;
 * bu durumda renderer form'u render etmemeli (banner gostermeli).
 *
 * Donus:
 *   {
 *     widgets,              // orijinal widget listesi
 *     widgetByCode,         // { code → widget }
 *     parsed,               // { code → { formula?, visibleIf?, disabledIf?, _fexpr, _vexpr, _dexpr } }
 *     depMap,               // { sourceCode → Set<targetCode> } → source degisince targetleri recompute et
 *     reverseDeps,          // { targetCode → Set<sourceCode> }
 *     topoOrder,            // [code, code, ...] — topological sira
 *     cycle,                // null veya [code, code, ...] — cycle rotasi
 *     parseErrors,          // { code → "Parse hatasi: ..." }
 *     fatalErrors,          // string[] — mount-blocking hatalar
 *     hasRules,             // herhangi bir widget'in kurali var mi?
 *   }
 */
export function buildRuleGraph(widgets) {
  var widgetByCode = {}
  var parsed = {}
  var depMap = {}            // source → Set<target>
  var reverseDeps = {}       // target → Set<source>
  var parseErrors = {}
  var fatalErrors = []
  var hasRules = false

  for (var i = 0; i < widgets.length; i++) {
    var w = widgets[i]
    widgetByCode[w.widgetId] = w
  }

  // Formul editoru w_ prefix ekleyerek referans uretir (w_en, w_boy...).
  // Gercek widget kodlari prefix'siz olabilir (en, boy...).
  // Her iki formati da validCodes'a ekle ki "tanimsiz alan" hatasi olmasin.
  var rawKeys = Object.keys(widgetByCode)
  for (var ai = 0; ai < rawKeys.length; ai++) {
    var ak = rawKeys[ai]
    if (ak.slice(0, 2) !== 'w_' && !widgetByCode['w_' + ak]) {
      widgetByCode['w_' + ak] = widgetByCode[ak]
    }
  }

  var validCodes = new Set(Object.keys(widgetByCode))

  // 1) Her widget'in kurallarini parse et + variable extract et
  for (var j = 0; j < widgets.length; j++) {
    var w2 = widgets[j]
    var rules = w2.rules
    if (!rules) continue

    var entry = { _fexpr: null, _vexpr: null, _dexpr: null }
    var allVars = new Set()

    function parseSlot(key, src, errLabel) {
      if (!src) return null
      try {
        var expr = parser.parse(String(src))
        var vars = expr.variables()
        for (var k = 0; k < vars.length; k++) {
          var v = vars[k]
          if (!validCodes.has(v)) {
            fatalErrors.push(
              w2.widgetId + '.' + errLabel + ": tanimsiz alan referansi '" + v + "'")
            return null
          }
          // Canonical kodu kullan: w_en → en (gercek widget kodu) — depMap/propagateChange uyumu
          var resolvedWidget = widgetByCode[v]
          var canonicalCode = resolvedWidget ? resolvedWidget.widgetId : v
          allVars.add(canonicalCode)
        }
        return expr
      } catch (e) {
        parseErrors[w2.widgetId] = (parseErrors[w2.widgetId] || '')
          + errLabel + ' parse hatasi: ' + (e.message || e) + '; '
        return null
      }
    }

    entry._fexpr = parseSlot('_fexpr', rules.formula,    'formula')
    entry._vexpr = parseSlot('_vexpr', rules.visibleIf,  'visibleIf')
    entry._dexpr = parseSlot('_dexpr', rules.disabledIf, 'disabledIf')

    if (entry._fexpr || entry._vexpr || entry._dexpr) {
      parsed[w2.widgetId] = entry
      hasRules = true

      // Bu widget'in bagimliliklari: allVars icindeki her bir alan
      if (!reverseDeps[w2.widgetId]) reverseDeps[w2.widgetId] = new Set()
      allVars.forEach(function (srcCode) {
        if (srcCode === w2.widgetId) return   // self-ref safety (self-loop)
        reverseDeps[w2.widgetId].add(srcCode)
        if (!depMap[srcCode]) depMap[srcCode] = new Set()
        depMap[srcCode].add(w2.widgetId)
      })
    }
  }

  // 2) Cycle detection (DFS 3-color)
  // color: 0=white, 1=gray (stack), 2=black (done)
  var color = {}
  var stack = []
  var cycle = null

  function dfs(node) {
    if (cycle) return
    color[node] = 1
    stack.push(node)

    var targets = depMap[node]
    if (targets) {
      var it = targets.values()
      var step
      while (!(step = it.next()).done) {
        if (cycle) return
        var dep = step.value
        if (color[dep] === 1) {
          var idx = stack.indexOf(dep)
          cycle = stack.slice(idx).concat([dep])
          return
        }
        if (!color[dep]) {
          dfs(dep)
          if (cycle) return
        }
      }
    }

    color[node] = 2
    stack.pop()
  }

  for (var code in parsed) {
    if (color[code] !== 2 && !cycle) dfs(code)
  }
  // depMap uzerinden baslatilmayan (ornek: source olmayan) target'lari da tara
  for (var targetCode in reverseDeps) {
    if (color[targetCode] !== 2 && !cycle) dfs(targetCode)
  }

  // 3) Topological sort (Kahn)
  var topoOrder = []
  if (!cycle) {
    // In-degree = how many source a target depends on
    var inDeg = {}
    Object.keys(widgetByCode).forEach(function (c) { inDeg[c] = 0 })
    Object.keys(reverseDeps).forEach(function (t) {
      inDeg[t] = reverseDeps[t].size
    })
    var queue = []
    Object.keys(inDeg).forEach(function (c) {
      if (inDeg[c] === 0) queue.push(c)
    })
    while (queue.length > 0) {
      var node = queue.shift()
      topoOrder.push(node)
      var dependents = depMap[node]
      if (dependents) {
        dependents.forEach(function (dep) {
          inDeg[dep]--
          if (inDeg[dep] === 0) queue.push(dep)
        })
      }
    }
  }

  return {
    widgets: widgets,
    widgetByCode: widgetByCode,
    parsed: parsed,
    depMap: depMap,
    reverseDeps: reverseDeps,
    topoOrder: topoOrder,
    cycle: cycle,
    parseErrors: parseErrors,
    fatalErrors: fatalErrors,
    hasRules: hasRules,
  }
}

// ════════════════════════════════════════════════════════════
// Evaluation yardimcisi (tek widget, try/catch)
// ════════════════════════════════════════════════════════════

function evalSafe(expr, scope) {
  try {
    return { ok: true, value: expr.evaluate(scope) }
  } catch (e) {
    return { ok: false, error: e.message || String(e) }
  }
}

// ════════════════════════════════════════════════════════════
// recomputeAll — initial pass (ve her fullsync ihtiyacinda)
// ════════════════════════════════════════════════════════════

/**
 * Tum formula/visibility/disabled'lari tek topolojik pass ile hesaplar.
 * Mount sonrasi ve reload sonrasi bir kere cagrilir.
 * Returns: { values, visibility, disabled, errors }
 */
export function recomputeAll(graph, inputValues) {
  if (graph.cycle) {
    return {
      values: Object.assign({}, inputValues),
      visibility: {},
      disabled: {},
      errors: { __cycle: 'Sonsuz dongu: ' + graph.cycle.join(' → ') },
    }
  }

  var values = Object.assign({}, inputValues)
  var visibility = {}
  var disabled = {}
  var errors = {}

  var iterations = 0
  for (var i = 0; i < graph.topoOrder.length; i++) {
    var code = graph.topoOrder[i]
    iterations++
    if (iterations > MAX_ITERATIONS) {
      errors.__abort = 'Kural motoru iterasyon siniri asildi (ilk pass).'
      break
    }
    var p = graph.parsed[code]
    if (!p) continue

    var scope = buildScope(graph.widgets, values)
    var w = graph.widgetByCode[code]

    if (p._fexpr) {
      var fr = evalSafe(p._fexpr, scope)
      if (fr.ok) {
        values[code] = coerceResult(fr.value, w.dataType)
      } else {
        errors[code] = 'Formul: ' + fr.error
      }
    }
    if (p._vexpr) {
      var vr = evalSafe(p._vexpr, scope)
      visibility[code] = vr.ok ? !!vr.value : true
      if (!vr.ok) errors[code] = (errors[code] || '') + ' visibleIf: ' + vr.error
    }
    if (p._dexpr) {
      var dr = evalSafe(p._dexpr, scope)
      disabled[code] = dr.ok ? !!dr.value : false
      if (!dr.ok) errors[code] = (errors[code] || '') + ' disabledIf: ' + dr.error
    }
  }

  return { values: values, visibility: visibility, disabled: disabled, errors: errors }
}

// ════════════════════════════════════════════════════════════
// propagateChange — kullanici edit'i sonrasi incremental recompute
// ════════════════════════════════════════════════════════════

/**
 * Bir widget'in degeri degisti. Etkilenen kapanimi BFS ile bul, topo sirali
 * yeniden hesapla. Patch objesi (values/visibility/disabled/errors) doner.
 * Parent'in state setter'lari bu patch'i tek batch olarak uygular.
 */
export function propagateChange(changedCode, newValue, graph, currentState) {
  if (graph.cycle) {
    // Cycle varsa form zaten render edilmiyor; defansif fallback
    var v = Object.assign({}, currentState.values)
    v[changedCode] = newValue
    return {
      values: v,
      visibility: currentState.visibility || {},
      disabled: currentState.disabled || {},
      errors: currentState.errors || {},
    }
  }

  // 1) Baslangic: yeni degeri scope'a yaz
  var values = Object.assign({}, currentState.values, { [changedCode]: newValue })
  var visibility = Object.assign({}, currentState.visibility || {})
  var disabled = Object.assign({}, currentState.disabled || {})
  var errors = Object.assign({}, currentState.errors || {})
  // Onceki bu widget'a ait hatalari temizle — yeni edit yeni sans
  delete errors[changedCode]

  // 2) changedCode'dan baslayarak etkilenen kapanimi BFS ile topla
  var affected = new Set()
  var queue = [changedCode]
  while (queue.length > 0) {
    var node = queue.shift()
    var dependents = graph.depMap[node]
    if (!dependents) continue
    dependents.forEach(function (t) {
      if (!affected.has(t)) {
        affected.add(t)
        queue.push(t)
      }
    })
  }

  if (affected.size === 0) {
    return { values: values, visibility: visibility, disabled: disabled, errors: errors }
  }

  // 3) Topo siraya gore sadece etkilenen widget'lari recompute et
  var iterations = 0
  for (var i = 0; i < graph.topoOrder.length; i++) {
    var code = graph.topoOrder[i]
    if (!affected.has(code)) continue
    iterations++
    if (iterations > MAX_ITERATIONS) {
      console.error('[ruleEngine] propagateChange: MAX_ITERATIONS asildi, durduruluyor')
      errors.__abort = 'Iterasyon siniri asildi'
      break
    }

    var p = graph.parsed[code]
    if (!p) continue
    var w = graph.widgetByCode[code]
    var scope = buildScope(graph.widgets, values)

    // Hata tekrari onlenmesi — her recompute once bu widget'in hatalarini sil
    delete errors[code]

    if (p._fexpr) {
      var fr = evalSafe(p._fexpr, scope)
      if (fr.ok) {
        values[code] = coerceResult(fr.value, w.dataType)
      } else {
        errors[code] = 'Formul: ' + fr.error
      }
    }
    if (p._vexpr) {
      var vr = evalSafe(p._vexpr, scope)
      visibility[code] = vr.ok ? !!vr.value : true
      if (!vr.ok) errors[code] = (errors[code] || '') + ' visibleIf: ' + vr.error
    }
    if (p._dexpr) {
      var dr = evalSafe(p._dexpr, scope)
      disabled[code] = dr.ok ? !!dr.value : false
      if (!dr.ok) errors[code] = (errors[code] || '') + ' disabledIf: ' + dr.error
    }
  }

  return { values: values, visibility: visibility, disabled: disabled, errors: errors }
}
