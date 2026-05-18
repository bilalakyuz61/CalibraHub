/**
 * fieldTokens.js — Rehber kisitlarinda kullanilan {#token} sistemi icin tek standart.
 *
 * Token formatlari (genisleyebilir, hepsi `{#...}` ile baslar):
 *   {#fieldId}            — Sayfa formundaki bir DOM input/select/textarea (id ile)
 *   {#head.fieldId}       — Yukaridaki ile ayni (alias) — semantik netlik icin
 *   {#row.fieldKey}       — Kalem satir kolonu (LineGridCell baglaminda mevcut row)
 *   {#row.combo.attrCode} — Kalem satirinin secili kombinasyonundaki attribute degeri
 *   {#row.extras.code}    — Kalem satirina eklenen ek saha (PR-future)
 *   {#head.extras.code}   — Belge ust bilgilerine eklenen ek saha (PR-future)
 *
 * Iki ana operasyon:
 *
 *   1) listFieldOptions(extraOptions)
 *      → Modal acildiginda @ dropdown'unda gosterilecek alan listesini doner.
 *      → DOM'dan `id` tasiyan inputlari otomatik kesfeder (Form Bilgileri grubu)
 *      → Ek source'lardan (extraOptions) gelen alanlari grupla birlikte ekler.
 *
 *   2) resolveTokens(text, context)
 *      → Kayitli FilterJson icindeki tum token'lari runtime'da gercek degerlerle
 *        replace eder. context: { row, combo, headExtras, rowExtras }.
 *      → DOM lookup'i (`{#xxx}`) varsayilan; nokta-prefiksli token'lar context'ten okunur.
 *
 * Bu sayede yeni bir saha turu eklemek icin yapilacaklar tek noktada:
 *   - `listFieldOptions` icin: caller `extraOptions` array'ini genisletir
 *   - `resolveTokens` icin: yeni bir context kanali eklenir
 *
 * Backward-compat: Eski `{#fieldId}` kayitlari (nokta yok) eskisi gibi DOM'dan okunur.
 */

const TOKEN_RE = /\{#([A-Za-z_][A-Za-z0-9_.\-]*)\}/g

/**
 * Bir DOM eleman icin Turkce label bul: <label for="id">, onceki kardes label,
 * veya parent'in ilk label cocugu. Bulunamazsa null.
 */
function findInputLabel(el) {
  if (!el) return null
  if (el.id) {
    try {
      const lbl = document.querySelector(`label[for="${CSS.escape(el.id)}"]`)
      if (lbl && lbl.textContent.trim()) return lbl.textContent.trim()
    } catch (_) {}
  }
  let prev = el.previousElementSibling
  let hops = 0
  while (prev && hops < 5) {
    if (prev.tagName === 'LABEL' && prev.textContent.trim()) return prev.textContent.trim()
    prev = prev.previousElementSibling
    hops++
  }
  const parent = el.parentElement
  if (parent) {
    const firstLabel = parent.querySelector('label')
    if (firstLabel && firstLabel.textContent.trim()) return firstLabel.textContent.trim()
  }
  return null
}

/**
 * DOM'dan modal disindaki id'li input/select/textarea'lari tara.
 * Modal portallarini disla (z-index 10000/9999 elementler).
 */
// HTML input type → kullanıcıya gösterilen Türkçe veri tipi etiketi.
// "+ Form Alanı Ekle" dropdown'unda her satırın yanında küçük chip olarak gösterilir.
function inferDataType(el) {
  if (!el) return ''
  const tag = (el.tagName || '').toLowerCase()
  if (tag === 'textarea') return 'Metin'
  if (tag === 'select')   return 'Seçim'
  const t = (el.type || '').toLowerCase()
  if (t === 'number')             return 'Sayısal'
  if (t === 'date')               return 'Tarih'
  if (t === 'datetime-local')     return 'Tarih/Saat'
  if (t === 'time')               return 'Saat'
  if (t === 'checkbox')           return 'Bool'
  if (t === 'radio')              return 'Seçim'
  if (t === 'email')              return 'E-posta'
  if (t === 'tel')                return 'Telefon'
  if (t === 'url')                return 'URL'
  if (t === 'password')           return 'Şifre'
  if (t === 'hidden')             return 'Hidden'
  return 'Metin'
}

function scanFormFields() {
  if (typeof document === 'undefined') return []
  const found = []
  const seen = new Set()
  const els = document.querySelectorAll('input[id], select[id], textarea[id]')
  els.forEach(el => {
    if (!el.id || seen.has(el.id)) return
    let p = el.parentElement
    let inModal = false
    while (p) {
      if (p && p.style && (p.style.zIndex === '10000' || p.style.zIndex === '9999')) { inModal = true; break }
      p = p.parentElement
    }
    if (inModal) return
    if (el.id.length < 2) return
    const tr = findInputLabel(el)
    found.push({
      token:    '#' + el.id,
      label:    tr || el.id,
      secondary: el.id,
      dataType: inferDataType(el),
      group:    'Form Bilgileri',
    })
    seen.add(el.id)
  })
  return found.sort((a, b) => a.label.localeCompare(b.label, 'tr'))
}

/**
 * Global widget registry'sinden snapshot oku (Tip 1 sabit alan baglaminda
 * widget alanlarina erisim icin). Sayfa basina tek registry — DWR mount
 * oldugunda doldurur, unmount oldugunda temizler.
 *
 * Sema: window.__CALIBRA_WIDGETS__ = { schema: [{code, label}], values: {code: value}, formCode }
 */
function readGlobalWidgetSchema() {
  if (typeof window === 'undefined') return []
  const reg = window.__CALIBRA_WIDGETS__
  return (reg && Array.isArray(reg.schema)) ? reg.schema : []
}
function readGlobalWidgetValues() {
  if (typeof window === 'undefined') return {}
  const reg = window.__CALIBRA_WIDGETS__
  return (reg && reg.values) ? reg.values : {}
}

/**
 * @ dropdown icin alan listesini olustur.
 * - DOM'dan otomatik form alanlari (her zaman dahil)
 * - Sayfada DWR mount ediliyse: window.__CALIBRA_WIDGETS__'tan widget alanlari
 *   ("Widget Alanları" grubu — Tip 1 sabit alan rehberlerinde de gorunur)
 * - Caller'in saglagi `extraOptions` (line cols, combo, extras vb.)
 *
 * extraOptions: [{ token, label, secondary, group }]
 *   - token:     `row.fieldKey` veya `row.combo.attr` gibi nokta-prefiksli identifier
 *                (not: `#` PREFIX'I OLMADAN, listFieldOptions ekliyor)
 *   - label:     dropdown'da birinci satir (Turkce)
 *   - secondary: dropdown'da ikinci satir monospace (token gosterimi icin opsiyonel)
 *   - group:     "Satır Bilgileri", "Kombinasyon", "Ek Sahalar" gibi
 */
export function listFieldOptions(extraOptions) {
  const dom = scanFormFields()
  // Global DWR widget registry — sayfada widget panel mount ediliyse otomatik gelir.
  // Tip 1 (sabit alan) rehberinde de "Widget Alanları" grubu altinda secilebilir.
  const widgetSchema = readGlobalWidgetSchema().map(w => ({
    token:     '#widget.' + (w.code || w.widgetCode),
    label:     w.label || w.fieldLabel || w.code || w.widgetCode,
    secondary: 'widget.' + (w.code || w.widgetCode),
    // Admin tanımlı widget'larda dataType var (Metin, Sayısal, Tarih vb.) — direkt aktarılır.
    dataType:  w.dataType || w.DataType || '',
    group:     'Widget Alanları',
  })).filter(o => o.token !== '#widget.' && o.token !== '#widget.undefined')
  const extra = Array.isArray(extraOptions)
    ? extraOptions.map(o => ({
        token:     o.token && o.token.startsWith('#') ? o.token : ('#' + (o.token || '')),
        label:     o.label || o.token,
        secondary: o.secondary || o.token,
        dataType:  o.dataType || '',
        group:     o.group || 'Diger',
      }))
    : []
  // Caller'in extraOptions'unda ayni widget code zaten varsa global'i ekleme (Tip 2'de duplicate)
  const seenTokens = new Set([...dom, ...extra].map(o => o.token))
  const widgetUnique = widgetSchema.filter(w => !seenTokens.has(w.token))
  return [...dom, ...widgetUnique, ...extra]
}

/**
 * Token'lari runtime degerlerle replace et.
 *
 * Kurallar:
 *   - Body nokta icermez (`{#xxx}`)        → DOM lookup (document.getElementById(xxx))
 *   - `{#head.xxx}`                        → DOM lookup `xxx` ile (alias)
 *   - `{#row.xxx}`                         → context.row?.[xxx]
 *   - `{#row.combo.xxx}`                   → context.combo?.[xxx]
 *   - `{#row.extras.xxx}`                  → context.rowExtras?.[xxx]
 *   - `{#head.extras.xxx}`                 → context.headExtras?.[xxx]
 *
 * Donus: SQL/JSON-string-safe escape edilmis deger. Eslesme yoksa "" doner.
 */
export function resolveTokens(text, context) {
  if (text == null) return text
  if (typeof text !== 'string') return text
  if (text.indexOf('{#') === -1) return text
  const ctx = context || {}
  return text.replace(TOKEN_RE, (raw, body) => {
    const v = lookupToken(body, ctx)
    return escapeForJsonAndSql(v == null ? '' : String(v))
  })
}

function lookupToken(body, ctx) {
  // Hizli yol: DOM id (nokta yok)
  if (body.indexOf('.') === -1) {
    return readDom(body)
  }
  const parts = body.split('.')
  // {#row.*}
  if (parts[0] === 'row') {
    if (parts.length === 2) {
      return readObject(ctx.row, parts[1])
    }
    if (parts[1] === 'combo' && parts.length >= 3) {
      return readObject(ctx.combo, parts.slice(2).join('.'))
    }
    if (parts[1] === 'extras' && parts.length >= 3) {
      return readObject(ctx.rowExtras, parts.slice(2).join('.'))
    }
    // Bilinmeyen `row.*` → row state'inde bos kalsin (eski format icin sessiz)
    return readObject(ctx.row, parts.slice(1).join('.'))
  }
  // {#head.*}
  if (parts[0] === 'head') {
    if (parts[1] === 'extras' && parts.length >= 3) {
      return readObject(ctx.headExtras, parts.slice(2).join('.'))
    }
    // {#head.fieldId} → DOM lookup (alias for plain {#fieldId})
    return readDom(parts.slice(1).join('.'))
  }
  // {#combo.*} kisa yolu (row varsayilan): {#row.combo.x} ile esdeger
  if (parts[0] === 'combo') {
    return readObject(ctx.combo, parts.slice(1).join('.'))
  }
  // {#widget.*} — Tip 2 (widget tanim formu) icin kardes widget degerleri.
  // Tip 1 (sabit alan) baglaminda ctx.widgets bos olur → window.__CALIBRA_WIDGETS__ fallback.
  if (parts[0] === 'widget') {
    const wKey = parts.slice(1).join('.')
    const fromCtx = readObject(ctx.widgets, wKey)
    if (fromCtx !== '' && fromCtx != null) return fromCtx
    return readObject(readGlobalWidgetValues(), wKey)
  }
  // Fallback: DOM (eski davranis)
  return readDom(body)
}

/**
 * Token gov'unu (`{#xxx}` icindeki body) DOM'da okur. Sirayla:
 *   1) document.getElementById(id) — exact match (eski davranis)
 *   2) `[data-field-key="id"]` — Razor view'larinda input.id farklidir ama
 *      bu attribute ile DB-kolon-adi token'i ('code', 'name') eslesir.
 *      Match case-insensitive.
 *   3) Case-insensitive id taramasi — input/select/textarea
 * Donus: input.value (veya data-value attribute fallback) string'i; bulunamazsa ''.
 */
function readDom(id) {
  if (typeof document === 'undefined' || !id) return ''
  try {
    // 1) Exact id match
    const direct = document.getElementById(id)
    if (direct && direct.value != null) return String(direct.value)

    const lower = String(id).toLowerCase()

    // 2) data-field-key attribute (case-insensitive)
    const dfk = document.querySelectorAll('[data-field-key]')
    for (let i = 0; i < dfk.length; i++) {
      const k = dfk[i].getAttribute('data-field-key') || ''
      if (k.toLowerCase() === lower) {
        const v = dfk[i].value != null ? dfk[i].value : (dfk[i].getAttribute('data-value') || '')
        return String(v || '')
      }
    }

    // 3) Case-insensitive id taramasi
    const els = document.querySelectorAll('input[id], select[id], textarea[id]')
    for (let j = 0; j < els.length; j++) {
      if (String(els[j].id).toLowerCase() === lower) {
        return els[j].value != null ? String(els[j].value) : ''
      }
    }
    return ''
  } catch (_) {
    return ''
  }
}

function readObject(obj, key) {
  if (obj == null || key == null) return ''
  // case-insensitive okuma — backend bazen camelCase, bazen PascalCase
  if (Object.prototype.hasOwnProperty.call(obj, key)) return obj[key]
  const first = key.charAt(0)
  const lo = first.toLowerCase() + key.slice(1)
  if (Object.prototype.hasOwnProperty.call(obj, lo)) return obj[lo]
  const up = first.toUpperCase() + key.slice(1)
  if (Object.prototype.hasOwnProperty.call(obj, up)) return obj[up]
  const needle = key.toLowerCase()
  for (const k of Object.keys(obj)) {
    if (k.toLowerCase() === needle) return obj[k]
  }
  return ''
}

/**
 * Replace icin tek-yonlu escape: backslash, double-quote ve single-quote.
 * FilterJson hem JSON hem SQL parametrize edilmeden once parse edilebilir;
 * bu escape iki katmani da emniyete alir.
 */
function escapeForJsonAndSql(s) {
  return s
    .replace(/\\/g, '\\\\')
    .replace(/"/g, '\\"')
    .replace(/'/g, "''")
}

/**
 * Widget tanim formundaki kardes alanlardan @ dropdown extra options uret.
 * Tip 2 (widget rehberi) baglaminda kullanilir — widget'in eklendigi formdaki
 * diger widget'lar `{#widget.WCODE}` ile kisitlanabilir.
 *
 * @param existingFields: [{ widgetCode, label, fieldLabel }] — kardes alan listesi
 * @param currentEditingFieldId: opsiyonel — kendi kendine referansi disla
 * @returns array of { token, label, secondary, group }
 */
export function buildWidgetExtraOptions(existingFields, currentEditingFieldId) {
  if (!Array.isArray(existingFields)) return []
  const out = []
  existingFields.forEach(f => {
    if (!f) return
    if (currentEditingFieldId != null && f.id === currentEditingFieldId) return
    const code = f.widgetCode || f.code
    if (!code) return
    const label = f.label || f.fieldLabel || code
    out.push({
      token:     'widget.' + code,
      label:     label,
      secondary: 'widget.' + code,
      group:     'Kardeş Widget',
    })
  })
  return out
}

/**
 * Kalem grid kolon listesinden @ dropdown icin extra options uret.
 * Kombinasyon takibi olan satirlarda combinationDetails'tan attribute'lari ekler.
 *
 * @param siblingColumns: [{ key, label }] — gridin tum kalem kolonlari (current dahil)
 * @param row:            mevcut satir (combinationDetails okumak icin)
 * @returns array of { token, label, secondary, group }
 */
export function buildLineExtraOptions(siblingColumns, row) {
  const out = []
  if (Array.isArray(siblingColumns)) {
    siblingColumns.forEach(col => {
      if (!col || !col.key) return
      out.push({
        token:     'row.' + col.key,
        label:     col.label || col.key,
        secondary: 'row.' + col.key,
        group:     'Kalem Bilgileri',
      })
    })
  }
  // Kombinasyon: row.combinationDetails varsa attribute key'lerini ek olarak goster
  if (row && Array.isArray(row.combinationDetails)) {
    row.combinationDetails.forEach(d => {
      if (!d) return
      const attrCode = d.attributeCode || d.AttributeCode || d.code || d.Code
      const attrName = d.attributeName || d.AttributeName || d.name || d.Name || attrCode
      if (!attrCode) return
      out.push({
        token:     'row.combo.' + attrCode,
        label:     attrName,
        secondary: 'row.combo.' + attrCode,
        group:     'Kombinasyon',
      })
    })
  }
  return out
}
