/**
 * guideLookupAdapters — Iki tip rehber konfigurasyonunu GuideLookupModal'in
 * birlesik `columns` propuna ceviren saf fonksiyonlar.
 *
 * Tip 1 (Sabit alan rehberi):  formatJson { visibleColumns, columnLabels, distinctColumns? }
 * Tip 2 (Widget rehberi):      guideConfig { columns: [{ name, label, visible, distinct }], constraint }
 *
 * Cikti birlestirilmis sekil:
 *   columns: Array<{ name, label, visible, distinct }>
 */

/** formatJson hem JSON string hem object kabul eder. Bos / parse hatasi → bos sablon. */
function parseFormatJson(raw) {
  if (!raw) return null
  if (typeof raw === 'object') return raw
  try {
    var p = JSON.parse(raw)
    return (p && typeof p === 'object') ? p : null
  } catch (e) { return null }
}

/** guideConfig hem JSON string hem object kabul eder. */
function parseGuideConfig(raw) {
  if (!raw) return null
  if (typeof raw === 'object') return raw
  try {
    var p = JSON.parse(raw)
    return (p && typeof p === 'object') ? p : null
  } catch (e) { return null }
}

/**
 * formatJson veya guideConfig icindeki valueColumn / displayColumn override'larini
 * doner. Override yoksa null doner — caller row.value/row.display fallback'ini kullanir.
 *
 * Bu helper pickRow handler'larin row.cells'ten dogru kolonu cekmesi icin —
 * rehber tanim'inin valueColumn'unu (GuideMas) override eder.
 */
export function extractValueDisplay(raw) {
  if (!raw) return { valueColumn: null, displayColumn: null }
  var p = raw
  if (typeof raw === 'string') {
    try { p = JSON.parse(raw) } catch (e) { return { valueColumn: null, displayColumn: null } }
  }
  if (!p || typeof p !== 'object') return { valueColumn: null, displayColumn: null }
  return {
    valueColumn:   p.valueColumn   || null,
    displayColumn: p.displayColumn || null,
  }
}

/**
 * Tip 1 — Sabit alan rehberi: formatJson + schema'dan kolonlari uretir.
 *   - schemaCols: schema.columns (string[])
 *   - distinct her zaman true (kullanici karari: tum rehber kolonlarinda filtre aktif)
 */
export function adaptFormatJson(formatJsonRaw, schemaCols) {
  if (!Array.isArray(schemaCols) || schemaCols.length === 0) return []
  var cfg = parseFormatJson(formatJsonRaw) || {}
  var visibleArr = Array.isArray(cfg.visibleColumns) ? cfg.visibleColumns : null
  var labelMap = (cfg.columnLabels && typeof cfg.columnLabels === 'object') ? cfg.columnLabels : {}
  var columnOrder = Array.isArray(cfg.columnOrder) ? cfg.columnOrder : null

  // Sutun sirasi: kullanicinin kaydettigi columnOrder once, schema'da olmayanlar atilir;
  // schema'da olup columnOrder'da olmayan yeni kolonlar (view'a sonradan eklenmis)
  // listenin sonuna eklenir → eski config kirilmaz.
  var orderedNames = []
  var seen = {}
  if (columnOrder) {
    columnOrder.forEach(function (name) {
      if (!seen[name] && schemaCols.indexOf(name) !== -1) {
        orderedNames.push(name)
        seen[name] = true
      }
    })
  }
  schemaCols.forEach(function (name) {
    if (!seen[name]) orderedNames.push(name)
  })

  return orderedNames.map(function (name) {
    return {
      name: name,
      label: labelMap[name] || name,
      visible: visibleArr ? (visibleArr.indexOf(name) !== -1) : true,
      distinct: true,
    }
  })
}

/**
 * Tip 2 — Widget rehberi: guideConfig.columns'u schema ile uzlastirir.
 *   - guideConfig'te listelenen kolon ayarlari korunur (label, visible)
 *   - schema'da olmayan kolonlar atilir
 *   - guideConfig'te listelenmeyen schema kolonlari default ile eklenir (visible=true)
 *   - distinct: TUM kolonlarda zorla `true` — kullanici karari, rehber davranisi
 *     tip-tip degil tek standart. Admin'in eski `distinct` toggle'i runtime'da
 *     etkisizdir (geri-uyumluluk icin atlanir).
 */
export function adaptGuideConfig(guideConfigRaw, schemaCols) {
  if (!Array.isArray(schemaCols) || schemaCols.length === 0) return []
  var cfg = parseGuideConfig(guideConfigRaw) || {}
  var configured = Array.isArray(cfg.columns) ? cfg.columns : []
  var byName = {}
  configured.forEach(function (c) {
    if (c && c.name) byName[c.name] = c
  })

  return schemaCols.map(function (name) {
    var c = byName[name]
    if (c) {
      return {
        name: name,
        label: (c.label && c.label !== name) ? c.label : name,
        visible: c.visible !== false,
        distinct: true,
      }
    }
    return { name: name, label: name, visible: true, distinct: true }
  })
}

/**
 * Constraint birlestirme. Modalda kullanilmak uzere tek bir JSON string uretir.
 *
 *   staticConstraint    — Tip 1'de filterJson; Tip 2'de guideConfig.constraint
 *                         (string ya da array kabul edilir)
 *   runtimeConstraint   — Tip 2'de DynamicWidgetRenderer'in {w_xxx} token replace
 *                         sonrasi olusan resolved string (Tip 1'de null)
 *   distinctSelections  — { col: string[] } — kolon distinct popover secimleri
 *
 * Cikti: JSON string (constraint dizisi) veya null (hicbir kisit yoksa).
 */
export function mergeConstraints(staticConstraint, runtimeConstraint, distinctSelections) {
  var arr = []

  function append(src) {
    if (!src) return
    var parsed = src
    if (typeof src === 'string') {
      var trimmed = src.trim()
      if (!trimmed) return
      try { parsed = JSON.parse(trimmed) } catch (e) { return }
    }
    if (Array.isArray(parsed)) {
      parsed.forEach(function (item) { if (item) arr.push(item) })
    } else if (parsed && typeof parsed === 'object') {
      arr.push(parsed)
    }
  }

  append(staticConstraint)
  append(runtimeConstraint)

  if (distinctSelections && typeof distinctSelections === 'object') {
    Object.keys(distinctSelections).forEach(function (col) {
      var vals = distinctSelections[col]
      if (Array.isArray(vals) && vals.length > 0) {
        arr.push({ field: col, operator: 'in', value: vals.join(','), logic: 'and' })
      }
    })
  }

  return arr.length > 0 ? JSON.stringify(arr) : null
}
