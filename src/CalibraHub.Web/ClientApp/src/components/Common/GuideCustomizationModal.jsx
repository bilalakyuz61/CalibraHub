/**
 * GuideCustomizationModal — Tüm proje genelinde tek rehber özelleştirme modal'ı.
 *
 * Hem widget tanım formundan, hem standart text-field rehber popup'larından
 * çağrılır. UI tek noktada — rehber konfigürasyon değişikliği isteyen herkes
 * bu component'i kullanır.
 *
 * Props:
 *   isOpen        bool
 *   onClose       fn()
 *   onSaved       fn({ viewCode, guideCode, columns: [{name, label, visible, distinct}], constraint })
 *   fieldLabel    string — modal başlığında görünür
 *   initialConfig { viewCode, guideCode, columns, constraint } | null
 *   saving        bool (opt) — caller'dan kaydet butonunu disable etmek için
 *   error         string (opt) — caller hata gösterimi (üstte gösterilir)
 *
 * onSaved sonrası caller kendi save logic'ini çalıştırır:
 *   - Widget tarafı: metadata'ya JSON yazar
 *   - Field tarafı: upsertFieldByFormCode endpoint'ini çağırır
 *
 * Backend kaynağı (PR 2+): /api/guides/views — DB'deki fiziksel cbv_Guide_% view
 * listesi. Her view tek bir entry — duplikat YOK. ViewName artik direkt kullanilir,
 * GuideMas indirection'i kaldirildi (GuideMas PR 3'te tamamen drop edilecek).
 *
 * Dropdown value'su `viewName` (örn. "cbv_Guide_Items"). onSaved çağrısında hem
 * `viewName` hem de geriye uyumluluk için `guideCode = viewName` aliasi yayilir.
 */
import { useState, useEffect, useRef } from 'react'
import { createPortal } from 'react-dom'
import { listFieldOptions } from '../../utils/fieldTokens'

function useThemeIsLight() {
  const [light, setLight] = useState(() => {
    if (typeof document === 'undefined') return false
    return document.body.classList.contains('app-theme-light')
  })
  useEffect(() => {
    const obs = new MutationObserver(() => {
      setLight(document.body.classList.contains('app-theme-light'))
    })
    obs.observe(document.body, { attributes: true, attributeFilter: ['class'] })
    return () => obs.disconnect()
  }, [])
  return light
}

// Standart rehber view'lari — sistem-tanimli (Tip 1). GÖRÜNÜŞ toggle gizlenir,
// sadece DÖNÜŞ (valueColumn) sorulur — Code/Name varsayilanlari yeterli.
// Tip 2 (ozel rehber): DÖNÜŞ + GÖRÜNÜŞ ayri secilir.
// PR 5 ViewMeta.IsStandard ile API uzerinden donecek; simdilik hardcoded.
const STANDARD_VIEWS = new Set([
  // View adlari (eski seed)
  'cbv_Guide_Items',
  'cbv_Guide_Contacts',
  'cbv_Guide_Suppliers',
  'cbv_Guide_Documents',
  'cbv_Guide_SalesQuotes',
  // 2026-06-02: GuideCode'lar (Tip 1 standart rehber, GuideMas'ta seed'li).
  // openGuideLookup'tan gelen `viewCode` aslinda GuideMas.Code ('CUSTOMERS') olur;
  // view adi olmadigindan bu set'e koduyla da bakarak Tip 1 algilanır.
  'CUSTOMERS',
  'SUPPLIERS',
  'MATERIALS',
  'CONTACTS',
  'DOCUMENTS',
  'SALES_QUOTES',
  'SALES_ORDERS',
  'PURCHASE_REQUESTS',
])

// FilterJson icindeki raw SQL fragment'ini cikar — yeni format [{rawSql,logic}] veya
// eski yapili [{field,op,value}] olabilir. Eski formatlari da raw SQL'e cevirir
// (geri-uyumluluk; admin yeniden yazabilir).
function extractRawSqlFromConstraint(constraintRaw) {
  if (!constraintRaw) return ''
  try {
    const p = typeof constraintRaw === 'string' ? JSON.parse(constraintRaw) : constraintRaw
    if (!Array.isArray(p) || p.length === 0) return ''
    // Yeni format: ilk satirda rawSql varsa direkt al
    if (p[0] && p[0].rawSql) return String(p[0].rawSql)
    // Eski yapili format: field/op/value tripletlerini SQL'e cevir
    const sqlParts = p.map((c, i) => {
      const f = c && c.field
      const op = c && c.op
      const v = c && c.value != null ? String(c.value) : ''
      if (!f || !op) return ''
      const opMap = { eq: '=', neq: '<>', gt: '>', lt: '<', like: 'LIKE' }
      const opSql = opMap[op] || '='
      const logic = i === 0 ? '' : ((c.logic === 'or' ? 'OR ' : 'AND '))
      return `${logic}[${f}] ${opSql} '${v.replace(/'/g, "''")}'`
    }).filter(Boolean)
    return sqlParts.join(' ')
  } catch (e) {
    return ''
  }
}

export default function GuideCustomizationModal(props) {
  const isOpen        = !!props.isOpen
  const onClose       = props.onClose || (() => {})
  const onSaved       = props.onSaved || (() => {})
  const fieldLabel    = props.fieldLabel || ''
  const initialConfig = props.initialConfig || null
  const externalSaving = !!props.saving
  const externalError  = props.error || null
  // extraFieldOptions: caller saglar — kalem grid kolonlari, kombinasyon attributes,
  // ek sahalar gibi DOM'dan tarananin disindaki token kaynaklari.
  // Format: [{ token: 'row.fieldKey', label: 'Malzeme Kodu', secondary: 'row.materialCode', group: 'Kalem Bilgileri' }]
  const extraFieldOptions = Array.isArray(props.extraFieldOptions) ? props.extraFieldOptions : []
  // forceShowDisplayColumn: caller tip 1 (standart) rehberde de GÖRÜNÜM toggle'inin
  // gosterilmesini istiyor. Widget tanim formundan (Alan Rehberi sayfasi) acilinca
  // admin override yapabilmeli — true geciliyor. Field rehber popup'tan acilinca
  // varsayilan false → tip 1'de yalniz DÖNÜŞ.
  const forceShowDisplayColumn = props.forceShowDisplayColumn === true
  // hideValueDisplayColumns: caller hem DÖNÜŞ hem GÖRÜNÜM kolonunu gizlemek istiyor.
  // Guide-list (salt okunur akordion liste) widget tipinde caller tek satir
  // secimi yapmaz, dolayisiyla DÖNÜŞ/GÖRÜNÜM seciminin anlami yoktur — toggle'lar
  // pasif, header gizli, valueColumn validate atlanir.
  const hideValueDisplayColumns = props.hideValueDisplayColumns === true

  const isLight = useThemeIsLight()

  // ── State ──
  const [viewCode, setViewCode]           = useState('')
  const [valueColumn, setValueColumn]     = useState('')
  const [displayColumn, setDisplayColumn] = useState('')
  const [guides, setGuides]               = useState([])
  const [loading, setLoading]             = useState(false)
  const [columns, setColumns]             = useState([])  // [{ name, label, visible, distinct }]
  // SQL kisiti — raw SQL fragment (WHERE'a append edilir). Token: {#fieldId} runtime'da resolve edilir.
  const [rawSqlConstraint, setRawSqlConstraint] = useState('')
  // Token alan listesi — DOM scan + extraFieldOptions birlesimi.
  // Format: [{ token: '#sqCustomerId', label, secondary, group }]
  const [formFields, setFormFields]       = useState([])
  const sqlTextareaRef = useRef(null)
  // @ tetikli autocomplete (form alani ekleme): mentionStart >= 0 iken popup acik
  const [mentionStart, setMentionStart]   = useState(-1)
  const [mentionFilter, setMentionFilter] = useState('')
  const [mentionIndex, setMentionIndex]   = useState(0) // klavye navigasyonu icin secili item
  const [mentionPos, setMentionPos]       = useState({ top: 0, left: 0 }) // popup pixel koordinatlari (textarea wrapper'a gore)
  const [error, setError]                 = useState(null)
  // "+ Form Alanı Ekle" custom dropdown'u — native <select> tema'ya uymuyordu.
  const [fieldDropdownOpen, setFieldDropdownOpen] = useState(false)
  const fieldDropdownRef = useRef(null)
  useEffect(() => {
    if (!fieldDropdownOpen) return undefined
    function handleDocClick(e) {
      if (fieldDropdownRef.current && !fieldDropdownRef.current.contains(e.target)) {
        setFieldDropdownOpen(false)
      }
    }
    document.addEventListener('mousedown', handleDocClick)
    return () => document.removeEventListener('mousedown', handleDocClick)
  }, [fieldDropdownOpen])

  // Save formatJson constraint: raw SQL textarea'yi JSON [{rawSql, logic}] formatina sar
  function serializeRawSql() {
    const v = (rawSqlConstraint || '').trim()
    if (!v) return ''
    return JSON.stringify([{ rawSql: v, logic: 'and' }])
  }

  // Modal acildiginda token alan listesini olustur:
  //   - DOM scan (form-level inputs) — fieldTokens.scanFormFields
  //   - extraFieldOptions (caller'in saglagi: kalem grid, kombinasyon, ek sahalar...)
  // Yeni saha turleri eklemek icin caller `extraFieldOptions` prop'u uzerinden gonderir;
  // burada degisiklik gerekmez. Standardi bozmamak icin tum @ secimleri tek noktadan akar.
  useEffect(() => {
    if (!isOpen) return undefined
    setFormFields(listFieldOptions(extraFieldOptions))
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isOpen, extraFieldOptions])

  // Form alani secildiginde: cursor pozisyonunda textarea'ya {#token} insert et.
  // tokenBody '#' prefix'i icermez (orn. 'sqCustomerId' veya 'row.materialCode').
  function insertFormField(tokenBody) {
    if (!tokenBody) return
    const clean = tokenBody.replace(/^#/, '')
    const ta = sqlTextareaRef.current
    const token = `{#${clean}}`
    const v = rawSqlConstraint || ''
    if (ta && typeof ta.selectionStart === 'number') {
      const start = ta.selectionStart
      const end = ta.selectionEnd
      const newVal = v.substring(0, start) + token + v.substring(end)
      setRawSqlConstraint(newVal)
      requestAnimationFrame(() => {
        try {
          ta.focus()
          const pos = start + token.length
          ta.setSelectionRange(pos, pos)
        } catch (_) {}
      })
    } else {
      setRawSqlConstraint(v + token)
    }
  }

  // Textarea icindeki belirli bir karakter pozisyonunun pixel koordinatlarini hesapla
  // (mirror-div teknigi). Sonuc: { top, left } — textarea'nin sol-ust koselinine gore.
  // Caret yukseklik popup yerlestirmesi icin lineHeight kullanilir (cagiran tarafta eklenir).
  function computeCaretCoords(textarea, position) {
    if (!textarea) return { top: 0, left: 0 }
    const style = getComputedStyle(textarea)
    const div = document.createElement('div')
    const props = [
      'boxSizing', 'width', 'height',
      'borderTopWidth', 'borderRightWidth', 'borderBottomWidth', 'borderLeftWidth', 'borderStyle',
      'paddingTop', 'paddingRight', 'paddingBottom', 'paddingLeft',
      'fontStyle', 'fontVariant', 'fontWeight', 'fontStretch', 'fontSize',
      'fontSizeAdjust', 'lineHeight', 'fontFamily',
      'textAlign', 'textTransform', 'textIndent', 'textDecoration',
      'letterSpacing', 'wordSpacing', 'tabSize',
      'whiteSpace', 'wordBreak', 'wordWrap', 'overflowWrap',
    ]
    props.forEach(p => { try { div.style[p] = style[p] } catch(_) {} })
    div.style.position = 'absolute'
    div.style.top = '0'
    div.style.left = '-9999px'
    div.style.visibility = 'hidden'
    div.style.whiteSpace = 'pre-wrap'
    div.style.wordWrap = 'break-word'
    div.style.overflow = 'hidden'
    div.style.width = textarea.clientWidth + 'px'

    document.body.appendChild(div)
    div.textContent = textarea.value.substring(0, position)
    const span = document.createElement('span')
    span.textContent = textarea.value.substring(position) || '.'
    div.appendChild(span)

    const top = span.offsetTop
    const left = span.offsetLeft
    document.body.removeChild(div)
    return { top, left }
  }

  // @ autocomplete: textarea degisince cursor'dan geriye dogru @ ara, popup'i ac/kapat
  function detectMention(text, cursorPos) {
    if (cursorPos == null) cursorPos = text.length
    let i = cursorPos - 1
    while (i >= 0) {
      const ch = text[i]
      if (ch === '@') {
        // Onceki karakter alphanumeric ise mention DEGIL (email gibi)
        const prev = i > 0 ? text[i - 1] : ''
        if (!/[a-zA-Z0-9_]/.test(prev)) {
          setMentionStart(i)
          setMentionFilter(text.substring(i + 1, cursorPos))
          setMentionIndex(0)
          // Popup pozisyonunu @ karakterinin VIEWPORT pixel koordinatina ayarla.
          // Popup body'ye portal edildigi icin fixed positioning, modal overflow'una takilmaz.
          // Auto-flip: asagi sigmiyorsa yukari ac.
          const ta = sqlTextareaRef.current
          if (ta) {
            const coords = computeCaretCoords(ta, i)
            const taRect = ta.getBoundingClientRect()
            const lineH = parseInt(getComputedStyle(ta).lineHeight) || (parseInt(getComputedStyle(ta).fontSize) * 1.4) || 16
            const popupMaxH = 220
            const charScreenY = taRect.top + coords.top - ta.scrollTop
            const topBelow = charScreenY + lineH                              // @ altinda
            const topAbove = charScreenY - popupMaxH                          // @ ustunde
            const useAbove = (topBelow + popupMaxH > window.innerHeight - 8) && topAbove > 8
            setMentionPos({
              top: useAbove ? Math.max(8, topAbove) : topBelow,
              left: Math.min(taRect.left + coords.left, window.innerWidth - 240),  // sag kenara takilmasin
            })
          }
          return
        }
        break
      }
      // Alphanumeric/underscore disindaki karakter: mention biter
      if (!/[a-zA-Z0-9_]/.test(ch)) break
      i--
    }
    if (mentionStart >= 0) {
      setMentionStart(-1)
      setMentionFilter('')
    }
  }

  function handleSqlChange(e) {
    const v = e.target.value
    setRawSqlConstraint(v)
    detectMention(v, e.target.selectionStart)
  }

  function handleSqlKeyDown(e) {
    if (mentionStart < 0) return
    const filtered = filteredMentionFields()
    if (filtered.length === 0) return
    if (e.key === 'ArrowDown') {
      e.preventDefault()
      setMentionIndex(prev => Math.min(prev + 1, filtered.length - 1))
    } else if (e.key === 'ArrowUp') {
      e.preventDefault()
      setMentionIndex(prev => Math.max(prev - 1, 0))
    } else if (e.key === 'Enter' || e.key === 'Tab') {
      e.preventDefault()
      e.stopPropagation()
      selectMention(filtered[mentionIndex] || filtered[0])
    } else if (e.key === 'Escape') {
      e.preventDefault()
      e.stopPropagation()
      setMentionStart(-1)
      setMentionFilter('')
    }
  }

  function filteredMentionFields() {
    const q = (mentionFilter || '').toLowerCase()
    if (!q) return formFields.slice(0, 20)
    // Hem token hem Turkce label'da ara — kullanici "tarih" yazsa Tarih (sqQuoteDate) eslessin
    return formFields.filter(f =>
      (f.label && f.label.toLowerCase().includes(q)) ||
      (f.token && f.token.toLowerCase().includes(q)) ||
      (f.secondary && f.secondary.toLowerCase().includes(q))
    ).slice(0, 20)
  }

  function selectMention(field) {
    if (!field || mentionStart < 0) return
    const ta = sqlTextareaRef.current
    if (!ta) return
    const v = rawSqlConstraint || ''
    const cursorPos = ta.selectionStart
    const before = v.substring(0, mentionStart)         // @ oncesi
    const after = v.substring(cursorPos)                // cursor sonrasi
    // field.token = '#sqCustomerId' veya '#row.materialCode'; '#' prefix dahil
    const cleanBody = (field.token || '').replace(/^#/, '')
    const token = `{#${cleanBody}}`
    const newVal = before + token + after
    setRawSqlConstraint(newVal)
    setMentionStart(-1)
    setMentionFilter('')
    requestAnimationFrame(() => {
      try {
        ta.focus()
        const pos = before.length + token.length
        ta.setSelectionRange(pos, pos)
      } catch (_) {}
    })
  }

  // 1) Fetch /api/guides/views — DB'deki fiziksel cbv_Guide_% view listesi
  //    (her view tek entry; GuideMas-bazli duplikat sorun yok)
  useEffect(() => {
    if (!isOpen) return undefined
    setError(null)
    setLoading(true)
    fetch('/api/guides/views')
      .then(r => (r.ok ? r.json() : []))
      .then(list => {
        const arr = Array.isArray(list) ? list.filter(g => g && g.viewName) : []
        const normalized = arr.map(g => ({
          // PR 2+: ViewName direkt kullanilir; guideCode = viewName aliasi (back-compat).
          guideCode:     g.viewName,
          viewName:      g.viewName,
          columns:       Array.isArray(g.columns) ? g.columns : [],
          label:         g.viewName,
          // value/display column auto-detect: Id varsa Id, yoksa ilk kolon = value;
          // Name/Title/Description benzeri varsa display, yoksa ikinci kolon.
          valueColumn:   pickValueColumn(g.columns),
          displayColumn: pickDisplayColumn(g.columns),
        }))
        setGuides(normalized)
      })
      .catch(() => { setError('Rehber view listesi yüklenemedi.'); setGuides([]) })
      .finally(() => setLoading(false))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isOpen])

  // 2) initialConfig + guides hazır olunca state'i hizala (FieldSettingsForm
  //    canlı binding fetch ettikten sonra initialConfig değişir → bu effect
  //    yeniden tetiklenir ve fresh değerleri uygular)
  useEffect(() => {
    if (!isOpen || guides.length === 0) return
    const ic = initialConfig || {}

    const wanted = ic.guideCode || ic.viewCode || ''
    // 1) guideCode ile birebir match  2) Legacy: viewName eşleşmesi
    let vCode = ''
    if (wanted) {
      let match = guides.find(g => g.guideCode === wanted)
      if (!match) match = guides.find(g => g.viewName && g.viewName === wanted)
      if (match) vCode = match.guideCode
    }
    if (!vCode && guides.length > 0) vCode = guides[0].guideCode
    setViewCode(vCode)
    // Constraint'i parse et: ya raw SQL JSON'i ([{rawSql,..}]) ya da eski yapili JSON
    setRawSqlConstraint(extractRawSqlFromConstraint(ic.constraint))

    const sel = vCode ? guides.find(g => g.guideCode === vCode) : null
    const viewDefaults = buildDefaultColumns(sel)

    // Değer / Gösterim kolonu — initialConfig öncelikli, yoksa rehber default'u
    // Guide-list'te DÖNÜŞ/GÖRÜNÜM secimi anlamsiz — state'te bos kalsin.
    if (hideValueDisplayColumns) {
      setValueColumn('')
      setDisplayColumn('')
    } else {
      setValueColumn(ic.valueColumn || (sel && sel.valueColumn) || (viewDefaults[0] && viewDefaults[0].name) || '')
      setDisplayColumn(ic.displayColumn || (sel && sel.displayColumn) || '')
    }

    // visibleColumns authoritative — null/undefined → hepsi visible; array → liste
    const visibleSet = Array.isArray(ic.visibleColumns) ? new Set(ic.visibleColumns) : null
    const overrideMap = new Map()
    if (Array.isArray(ic.columns)) {
      ic.columns.forEach(c => { if (c && c.name) overrideMap.set(c.name, c) })
    }

    // ColumnOrder varsa kullanicinin sirasini uygula; yoksa viewDefaults sirasi
    // (server INFORMATION_SCHEMA order). View'a sonradan eklenen kolonlar
    // columnOrder'da yoksa sona eklenir → eski config kirilmaz.
    const orderArr = Array.isArray(ic.columnOrder) ? ic.columnOrder : null
    let orderedDefaults = viewDefaults
    if (orderArr) {
      const seen = new Set()
      const reordered = []
      orderArr.forEach(n => {
        const m = viewDefaults.find(d => d.name === n)
        if (m && !seen.has(n)) { reordered.push(m); seen.add(n) }
      })
      viewDefaults.forEach(d => { if (!seen.has(d.name)) reordered.push(d) })
      orderedDefaults = reordered
    }

    let merged = orderedDefaults.map(d => {
      const ov = overrideMap.get(d.name)
      const labelOverride = (ov && typeof ov.label === 'string' && ov.label.length > 0 && ov.label !== d.name) ? ov.label : d.name
      const isVisible = visibleSet ? visibleSet.has(d.name) : (ov ? ov.visible !== false : true)
      return { name: d.name, label: labelOverride, visible: isVisible, distinct: true }
    })

    // View defaults boş ise (view henüz seçilmedi vb.) override'ları olduğu gibi kullan
    if (merged.length === 0 && Array.isArray(ic.columns) && ic.columns.length > 0) {
      merged = ic.columns.map(c => ({
        name: c.name,
        label: c.label || c.name,
        visible: visibleSet ? visibleSet.has(c.name) : (c.visible !== false),
        distinct: true,
      }))
    }
    // Invariant: visible'lar uste, invisible'lar en alta — initial yuklemede de uygula
    const vis = merged.filter(c => c.visible)
    const hid = merged.filter(c => !c.visible)
    setColumns([...vis, ...hid])
  }, [isOpen, guides, initialConfig])

  function changeView(newCode) {
    setViewCode(newCode)
    const sel = guides.find(g => g.guideCode === newCode)
    const defaults = buildDefaultColumns(sel)
    setColumns(defaults)
    if (hideValueDisplayColumns) {
      setValueColumn('')
      setDisplayColumn('')
    } else {
      setValueColumn(sel ? (sel.valueColumn || (defaults[0] && defaults[0].name) || '') : '')
      setDisplayColumn(sel ? (sel.displayColumn || '') : '')
    }
  }

  // Visible kolonlar listenin uzerinde, invisible olanlar daima en altta. Bu invariant
  // her toggle/move sonrasi korunur — moveColumn invisible bolgeye gecis yapamaz.
  function normalizeColumnsOrder(cols) {
    const vis = cols.filter(c => c.visible)
    const hid = cols.filter(c => !c.visible)
    return [...vis, ...hid]
  }
  function toggleColumn(idx) {
    setColumns(prev => {
      const next = prev.map((c, i) => i === idx ? { ...c, visible: !c.visible } : c)
      return normalizeColumnsOrder(next)
    })
  }
  function changeLabel(idx, lbl) {
    setColumns(prev => prev.map((c, i) => i === idx ? { ...c, label: lbl } : c))
  }
  function selectAll() {
    setColumns(prev => prev.map(c => ({ ...c, visible: true })))
  }
  // Sutun sirasi: ↑/↓ butonlari sadece VISIBLE grubunda calisir. Invisible kolonlar
  // hep en altta sabittir, sirayi etkilemez (formatJson.columnOrder'a tam sira yazilir
  // ama visible'lar onde).
  function moveColumn(idx, direction) {
    setColumns(prev => {
      const visibleCount = prev.filter(c => c.visible).length
      // Invisible kolon (idx >= visibleCount) hareket ettirilemez
      if (idx >= visibleCount) return prev
      const newIdx = idx + direction
      if (newIdx < 0 || newIdx >= visibleCount) return prev  // visible siniri asma
      const next = prev.slice()
      const tmp = next[idx]
      next[idx] = next[newIdx]
      next[newIdx] = tmp
      return next
    })
  }

  function handleSave() {
    if (!viewCode) { setError('Lütfen bir rehber view seçin.'); return }
    // Standart rehber (Tip 1): GÖRÜNÜŞ toggle gizli, sadece DÖNÜŞ (valueColumn) sorulur.
    //   displayColumn = '' kalir → fallback ile DÖNÜŞ'e dusurulur.
    // Ozel rehber (Tip 2): DÖNÜŞ + GÖRÜNÜŞ ayri secilir; GÖRÜNÜŞ bos ise DÖNÜŞ'e fallback.
    // Guide-list: ikisi de anlamsiz — validation skip, value/display bos kayit edilir.
    if (!hideValueDisplayColumns && !valueColumn) { setError('Lütfen "Dönüş" sahasını seçin.'); return }
    const effectiveDisplay = hideValueDisplayColumns ? '' : (displayColumn || valueColumn)
    onSaved({
      viewCode:      viewCode,
      viewName:      viewCode,    // PR 2+: primary identifier
      guideCode:     viewCode,    // back-compat alias (PR 3'te kaldirilacak)
      valueColumn:   valueColumn,
      displayColumn: effectiveDisplay,
      columns:       columns,
      constraint:    serializeRawSql(),
    })
  }

  if (!isOpen) return null

  // Tip 1 (standart): sadece DÖNÜŞ. Tip 2 (ozel): DÖNÜŞ + GÖRÜNÜŞ.
  // forceShowDisplayColumn=true ise tip ne olursa olsun GÖRÜNÜŞ gosterilir
  // (widget tanim formu — admin override).
  // hideValueDisplayColumns=true ise hem DÖNÜŞ hem GÖRÜNÜŞ tamamen gizlenir
  // (guide-list — salt okunur liste, satir secimi yok).
  // 2026-06-02: Hardcoded view set yerine prefix-based algilama —
  // CalibraHub konvansiyonu: TUM standart (Tip 1) view'lari `cbv_Guide_*` ile
  // baslar. Tip 2 (admin-defined ozel rehber) bu prefix'i kullanmaz.
  // STANDARD_VIEWS set'i artik backward-compat icin GuideCode kısayollarını da
  // kabul ediyor (örn. openGuideLookup'tan 'CUSTOMERS' geçtiğinde).
  const isStandardView    = (typeof viewCode === 'string' && /^cbv_Guide_/i.test(viewCode))
                            || STANDARD_VIEWS.has(viewCode)
  const hideValueColumn   = hideValueDisplayColumns
  const hideDisplayColumn = hideValueDisplayColumns || (isStandardView && !forceShowDisplayColumn)
  // Sutun sirasi icin ek 44px (↑/↓ butonlari)
  const gridTemplate = hideValueColumn
    ? '44px 34px 1fr 1.2fr'              // sira / visible / alan / etiket  (guide-list)
    : (hideDisplayColumn
        ? '44px 34px 1fr 1.2fr 60px'        // sira / visible / alan / etiket / DÖNÜŞ
        : '44px 34px 1fr 1.2fr 60px 60px')  // sira / visible / alan / etiket / DÖNÜŞ / GÖRÜNÜŞ

  // ── Stiller — tema-aware ──
  const bg     = isLight ? '#ffffff'                         : 'rgba(13,17,27,0.98)'
  const bdr    = isLight ? '1px solid #e2e8f0'              : '1px solid rgba(255,255,255,0.12)'
  const shadow = isLight ? '0 16px 48px rgba(0,0,0,0.15)'   : '0 16px 48px rgba(0,0,0,0.55)'
  const hdrBg  = isLight ? '#f8fafc'                         : 'rgba(255,255,255,0.04)'
  const divBdr = isLight ? '1px solid #e2e8f0'              : '1px solid rgba(255,255,255,0.08)'
  const iBg    = isLight ? '#f8fafc'                         : 'rgba(255,255,255,0.06)'
  const iBdr   = isLight ? '1px solid #cbd5e1'              : '1px solid rgba(255,255,255,0.14)'
  const iClr   = isLight ? '#1e293b'                         : 'rgba(255,255,255,0.85)'
  const lblClr = isLight ? '#64748b'                         : 'rgba(255,255,255,0.45)'
  const txtClr = isLight ? '#334155'                         : 'rgba(255,255,255,0.78)'
  const accent = '#6366f1'
  const errClr = '#ef4444'

  const showError = externalError || error

  return createPortal(
    <div
      onClick={(e) => e.stopPropagation()}
      style={{
        position: 'fixed', inset: 0, background: 'rgba(0,0,0,0.55)',
        display: 'flex', alignItems: 'center', justifyContent: 'center',
        // Tip 1 rehber (GuideLookupModal, z-index: 10100) icinden "Alan Ayarlari"
        // butonu ile acilinca rehberin USTUNDE olmali — bu yuzden 10200.
        // Ic dropdown'lar (Distinct vb. line 796) 11000 ile bu modal'in da uzerinde.
        zIndex: 10200, padding: 16, backdropFilter: 'blur(2px)',
      }}
    >
      <div style={{
        background: bg, border: bdr, boxShadow: shadow, borderRadius: 12,
        width: '100%', maxWidth: 560, maxHeight: '85vh',
        display: 'flex', flexDirection: 'column', overflow: 'hidden',
        backdropFilter: isLight ? undefined : 'blur(20px)',
      }}>
        {/* Başlık */}
        <div style={{
          display: 'flex', alignItems: 'center', padding: '10px 16px',
          borderBottom: divBdr, background: hdrBg, flexShrink: 0, gap: 8,
        }}>
          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke={accent} strokeWidth="2">
            <circle cx="12" cy="12" r="3"/>
            <path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83-2.83l.06-.06A1.65 1.65 0 0 0 4.68 15a1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 2.83-2.83l.06.06A1.65 1.65 0 0 0 9 4.68a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 2.83l-.06.06A1.65 1.65 0 0 0 19.4 9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z"/>
          </svg>
          <span style={{ fontSize: 13, fontWeight: 600, color: txtClr, flex: 1 }}>
            Alan Ayarları — <span style={{ fontFamily: 'monospace', color: accent }}>{fieldLabel || '...'}</span>
          </span>
          <button type="button" onClick={onClose}
            style={{ background: 'none', border: 'none', color: lblClr, cursor: 'pointer', fontSize: 18, lineHeight: 1, padding: '0 4px' }}>×</button>
        </div>

        {/* İçerik */}
        <div style={{ flex: 1, overflowY: 'auto', padding: '14px 16px', display: 'flex', flexDirection: 'column', gap: 14 }}>
          {showError && (
            <div style={{
              padding: '7px 12px', fontSize: 11, color: errClr,
              background: 'rgba(239,68,68,0.08)', borderRadius: 6,
              border: '1px solid rgba(239,68,68,0.2)',
            }}>{showError}</div>
          )}

          {/* Rehber View — label + dropdown ayni satirda */}
          <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
            <label style={{
              flexShrink: 0, fontSize: 10, fontWeight: 700, color: lblClr,
              textTransform: 'uppercase', letterSpacing: '.06em',
            }}>
              Rehber View <span style={{ color: errClr }}>*</span>
            </label>
            {loading ? (
              <div style={{ flex: 1, fontSize: 12, color: lblClr }}>Yükleniyor…</div>
            ) : (
              <select
                value={viewCode}
                onChange={(e) => changeView(e.target.value)}
                style={{
                  flex: 1, padding: '7px 10px', fontSize: 12,
                  background: iBg, border: viewCode ? iBdr : '1px solid rgba(239,68,68,0.5)',
                  borderRadius: 6, color: iClr, outline: 'none', cursor: 'pointer',
                  colorScheme: isLight ? 'light' : 'dark',
                }}
              >
                <option value="" style={{ background: isLight ? '#fff' : '#0d1117', color: iClr }}>— Seçiniz —</option>
                {guides.map(g => (
                  <option key={g.viewName} value={g.viewName} style={{ background: isLight ? '#fff' : '#0d1117', color: iClr }}>
                    {g.viewName}
                  </option>
                ))}
              </select>
            )}
          </div>


          {/* Görünür Kolonlar & Etiketler + Distinct toggle */}
          <div>
            <div style={{ display: 'flex', alignItems: 'center', marginBottom: 5 }}>
              <label style={{
                fontSize: 10, fontWeight: 700, color: lblClr,
                textTransform: 'uppercase', letterSpacing: '.06em', flex: 1,
              }}>Görünür Kolonlar & Etiketler</label>
              {columns.length > 0 && (
                <button type="button" onClick={selectAll}
                  style={{ fontSize: 10, color: accent, background: 'none', border: 'none', cursor: 'pointer', textDecoration: 'underline' }}>
                  Tümünü seç
                </button>
              )}
            </div>

            <div style={{ border: divBdr, borderRadius: 6, overflow: 'hidden' }}>
              <div style={{
                display: 'grid', gridTemplateColumns: gridTemplate,
                background: hdrBg, padding: '5px 10px', borderBottom: divBdr, gap: 8,
              }}>
                <span style={{ fontSize: 9.5, fontWeight: 700, color: lblClr, textTransform: 'uppercase', textAlign: 'center' }} title="Sırayı değiştir">Sıra</span>
                <span style={{ fontSize: 9.5, fontWeight: 700, color: lblClr, textTransform: 'uppercase' }} title="Görünür">Gör.</span>
                <span style={{ fontSize: 9.5, fontWeight: 700, color: lblClr, textTransform: 'uppercase' }}>Alan Adı</span>
                <span style={{ fontSize: 9.5, fontWeight: 700, color: lblClr, textTransform: 'uppercase' }}>Etiket</span>
                {!hideValueColumn && (
                  <span style={{ fontSize: 9.5, fontWeight: 700, color: lblClr, textTransform: 'uppercase', textAlign: 'center' }} title="Seçim sonrası alana yazılacak (dönecek) saha. Standart rehberde Id zaten fillMap ile DB'ye gider.">
                    Dönüş
                  </span>
                )}
                {!hideDisplayColumn && (
                  <span style={{ fontSize: 9.5, fontWeight: 700, color: lblClr, textTransform: 'uppercase', textAlign: 'center' }} title="Kullanıcıya gösterilen overlay/label kolonu (boş ise Dönüş'e fallback)">
                    Görünüş
                  </span>
                )}
              </div>

              {columns.length === 0 && (
                <div style={{ padding: '14px 10px', fontSize: 11, color: lblClr, textAlign: 'center' }}>
                  Önce bir rehber view seçin.
                </div>
              )}

              {columns.map((c, idx) => {
                const isValue   = valueColumn   === c.name
                const isDisplay = displayColumn === c.name
                const valueOn   = '#10b981'  // emerald — "değer"
                const displayOn = '#f59e0b'  // amber  — "gösterim"
                const offBg     = isLight ? '#cbd5e1' : 'rgba(255,255,255,0.15)'
                return (
                  <div key={c.name} style={{
                    display: 'grid', gridTemplateColumns: gridTemplate,
                    alignItems: 'center', gap: 8,
                    padding: '6px 10px',
                    borderTop: idx > 0 ? (isLight ? '1px solid #f1f5f9' : '1px solid rgba(255,255,255,0.04)') : 'none',
                    opacity: c.visible ? 1 : 0.55,
                  }}>
                    {/* ↑ / ↓ — sutun sirasi (sadece VISIBLE kolonlarda aktif) */}
                    {(function () {
                      const visibleCount = columns.filter(x => x.visible).length
                      const isInvisible = !c.visible
                      const isFirstVisible = idx === 0 && c.visible
                      const isLastVisible = c.visible && idx === visibleCount - 1
                      return (
                        <div style={{ display: 'flex', gap: 2, justifyContent: 'center' }}>
                          <button type="button" onClick={() => moveColumn(idx, -1)} disabled={isInvisible || isFirstVisible}
                            title={isInvisible ? 'Görünmeyen kolonlar sıralamaya dahil değil' : 'Yukari tasi'}
                            style={{
                              width: 18, height: 18, padding: 0, border: 'none', borderRadius: 3,
                              background: (isInvisible || isFirstVisible) ? 'transparent' : (isLight ? '#e2e8f0' : 'rgba(255,255,255,0.08)'),
                              color: (isInvisible || isFirstVisible) ? lblClr : iClr,
                              cursor: (isInvisible || isFirstVisible) ? 'default' : 'pointer',
                              fontSize: 11, lineHeight: '18px',
                              opacity: (isInvisible || isFirstVisible) ? 0.25 : 1,
                            }}>▲</button>
                          <button type="button" onClick={() => moveColumn(idx, 1)} disabled={isInvisible || isLastVisible}
                            title={isInvisible ? 'Görünmeyen kolonlar sıralamaya dahil değil' : 'Asagi tasi'}
                            style={{
                              width: 18, height: 18, padding: 0, border: 'none', borderRadius: 3,
                              background: (isInvisible || isLastVisible) ? 'transparent' : (isLight ? '#e2e8f0' : 'rgba(255,255,255,0.08)'),
                              color: (isInvisible || isLastVisible) ? lblClr : iClr,
                              cursor: (isInvisible || isLastVisible) ? 'default' : 'pointer',
                              fontSize: 11, lineHeight: '18px',
                              opacity: (isInvisible || isLastVisible) ? 0.25 : 1,
                            }}>▼</button>
                        </div>
                      )
                    })()}

                    {/* Visible toggle */}
                    <button type="button" onClick={() => toggleColumn(idx)}
                      title={c.visible ? 'Görünür' : 'Gizli'}
                      style={{
                        position: 'relative', width: 28, height: 16, borderRadius: 8,
                        border: 'none', cursor: 'pointer',
                        background: c.visible ? accent : offBg,
                        transition: 'background 0.18s',
                      }}>
                      <span style={{
                        position: 'absolute', top: 2, left: c.visible ? 14 : 2,
                        width: 12, height: 12, borderRadius: '50%', background: '#fff',
                        transition: 'left 0.18s',
                      }} />
                    </button>

                    <span style={{ fontFamily: 'monospace', fontSize: 11, color: txtClr, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                      {c.name}
                    </span>

                    <input
                      type="text"
                      value={c.label}
                      onChange={(e) => changeLabel(idx, e.target.value)}
                      placeholder={c.name}
                      disabled={!c.visible}
                      style={{
                        height: 24, padding: '2px 6px', fontSize: 11,
                        background: iBg, border: iBdr, borderRadius: 4,
                        color: iClr, outline: 'none', width: '100%',
                        opacity: c.visible ? 1 : 0.5,
                      }}
                    />

                    {/* DÖNÜŞ toggle — guide-list disinda her zaman gosterilir (standart + ozel).
                        guide-list (hideValueColumn) icinde tek satir secimi olmadigi icin pasif. */}
                    {!hideValueColumn && (
                      <button type="button" onClick={() => setValueColumn(isValue ? '' : c.name)}
                        title={isValue ? 'Dönüş kolonu (seçim sonrası alana yazılır)' : 'Bu kolonu Dönüş olarak işaretle'}
                        style={{
                          position: 'relative', width: 28, height: 16, borderRadius: 8,
                          border: 'none', cursor: 'pointer',
                          background: isValue ? valueOn : offBg,
                          justifySelf: 'center',
                          transition: 'background 0.18s',
                        }}>
                        <span style={{
                          position: 'absolute', top: 2, left: isValue ? 14 : 2,
                          width: 12, height: 12, borderRadius: '50%', background: '#fff',
                          transition: 'left 0.18s',
                        }} />
                      </button>
                    )}

                    {/* GÖRÜNÜŞ toggle — Tip 2'de her zaman; Tip 1'de yalniz forceShowDisplayColumn=true ise. */}
                    {!hideDisplayColumn && (
                      <button type="button" onClick={() => setDisplayColumn(isDisplay ? '' : c.name)}
                        title={isDisplay ? 'Görünüş kolonu (kullanıcıya overlay/label olarak gösterilir)' : 'Bu kolonu Görünüş olarak işaretle'}
                        style={{
                          position: 'relative', width: 28, height: 16, borderRadius: 8,
                          border: 'none', cursor: 'pointer',
                          background: isDisplay ? displayOn : offBg,
                          justifySelf: 'center',
                          transition: 'background 0.18s',
                        }}>
                        <span style={{
                          position: 'absolute', top: 2, left: isDisplay ? 14 : 2,
                          width: 12, height: 12, borderRadius: '50%', background: '#fff',
                          transition: 'left 0.18s',
                        }} />
                      </button>
                    )}
                  </div>
                )
              })}
            </div>
          </div>

          {/* SQL Kısıtı — serbest textarea + Form Alani Ekle dropdown */}
          <div>
            <div style={{ display: 'flex', alignItems: 'center', marginBottom: 5, gap: 8 }}>
              <label style={{
                fontSize: 10, fontWeight: 700, color: lblClr,
                textTransform: 'uppercase', letterSpacing: '.06em', flex: 1,
              }}>SQL Kısıtı (WHERE Fragment)</label>
              {/* "Standart Yap" özelliği tamamen kaldırıldı —
                  hem Tip 1 hem Tip 2 rehberlerde global filtre yazımı admin akışında
                  yer almaz. Per-form bazında SQL kısıt (FilterJson) kaydetmek yeterli. */}
              {formFields.length > 0 && (
                <div ref={fieldDropdownRef} style={{ position: 'relative' }}>
                  <button
                    type="button"
                    onClick={() => setFieldDropdownOpen(o => !o)}
                    style={{
                      height: 24, padding: '0 8px', fontSize: 10, fontWeight: 600,
                      display: 'inline-flex', alignItems: 'center', gap: 4,
                      background: iBg, border: iBdr, borderRadius: 4,
                      color: accent, outline: 'none', cursor: 'pointer',
                    }}
                  >
                    + Form Alanı Ekle
                    <span style={{
                      display: 'inline-block', transform: fieldDropdownOpen ? 'rotate(180deg)' : 'rotate(0)',
                      transition: 'transform 0.18s', fontSize: 9, opacity: 0.6,
                    }}>▾</span>
                  </button>
                  {fieldDropdownOpen && (() => {
                    // Gruplara gore item'lari grupla — dropdown'da her grup ayri sticky baslik altinda
                    const groups = {}
                    formFields.forEach(f => {
                      const g = f.group || 'Diger'
                      if (!groups[g]) groups[g] = []
                      groups[g].push(f)
                    })
                    const groupKeys = Object.keys(groups)
                    return (
                      <div
                        style={{
                          // YUKARI dogru ac (asagisi modal'in kalan alani sinirli — taşıyor).
                          // bottom: 28 → buton ustune yapisik, asagiya kesilmez.
                          position: 'absolute', bottom: 28, right: 0, zIndex: 50,
                          minWidth: 260, maxHeight: 360, overflowY: 'auto',
                          background: isLight ? '#ffffff' : 'rgba(13,17,27,0.98)',
                          border: isLight ? '1px solid #e2e8f0' : '1px solid rgba(255,255,255,0.14)',
                          borderRadius: 8,
                          boxShadow: isLight ? '0 8px 24px rgba(0,0,0,0.12)' : '0 -12px 36px rgba(0,0,0,0.55)',
                          backdropFilter: 'blur(20px)',
                          WebkitBackdropFilter: 'blur(20px)',
                        }}
                      >
                        {groupKeys.map((g, gi) => (
                          <div key={g}>
                            <div style={{
                              padding: '6px 10px', fontSize: 9, fontWeight: 700,
                              textTransform: 'uppercase', letterSpacing: '.06em',
                              color: lblClr,
                              background: isLight ? '#f8fafc' : 'rgba(255,255,255,0.04)',
                              position: 'sticky', top: 0, zIndex: 1,
                              borderTop: gi > 0 ? divBdr : 'none',
                            }}>{g}</div>
                            {groups[g].map((f, fi) => (
                              <button
                                key={(f.token || '') + '-' + fi}
                                type="button"
                                onClick={() => { insertFormField(f.token); setFieldDropdownOpen(false) }}
                                title={f.secondary || f.token}
                                style={{
                                  display: 'flex', alignItems: 'center', gap: 8,
                                  width: '100%', padding: '5px 10px',
                                  background: 'transparent', border: 'none',
                                  textAlign: 'left', cursor: 'pointer',
                                  transition: 'background 0.12s',
                                }}
                                onMouseEnter={e => { e.currentTarget.style.background = isLight ? '#f1f5f9' : 'rgba(99,102,241,0.10)' }}
                                onMouseLeave={e => { e.currentTarget.style.background = 'transparent' }}
                              >
                                <span style={{ fontSize: 11.5, fontWeight: 600, color: iClr, flex: '1 1 auto', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                                  {f.label}
                                </span>
                              </button>
                            ))}
                          </div>
                        ))}
                      </div>
                    )
                  })()}
                </div>
              )}
            </div>
            <div style={{ position: 'relative' }}>
              <textarea
                ref={sqlTextareaRef}
                value={rawSqlConstraint}
                onChange={handleSqlChange}
                onKeyDown={handleSqlKeyDown}
                onBlur={() => { setTimeout(() => setMentionStart(-1), 150) /* tiklamaya zaman ver */ }}
                placeholder={"Örn: [Type] = 'Stock' AND [PreferredAccountId] = @sqCustomerId\n(@ ile form alani eklenir)"}
                rows={3}
                style={{
                  width: '100%', padding: '7px 10px', fontSize: 11,
                  background: iBg, border: iBdr, borderRadius: 6,
                  color: iClr, outline: 'none', resize: 'vertical',
                  fontFamily: 'monospace', boxSizing: 'border-box',
                }}
              />
              {/* @ autocomplete popup — body'ye portal edilir (modal overflow'una takilmaz),
                  fixed positioning ile @ karakterinin viewport pozisyonunda gorunur.
                  Alanlar `group` field'ina gore segmentli (Form Bilgileri, Kalem Bilgileri,
                  Kombinasyon, Ek Sahalar...) — yeni bir grup eklemek icin caller
                  extraFieldOptions'a o grup adiyla item gondermesi yeterli. */}
              {mentionStart >= 0 && (function() {
                const filtered = filteredMentionFields()
                if (filtered.length === 0) return null
                // Grup bazli render: "Form Bilgileri", "Kalem Bilgileri", ...
                const grouped = []
                const groupMap = new Map()
                filtered.forEach((f, idx) => {
                  const g = f.group || 'Diger'
                  if (!groupMap.has(g)) {
                    groupMap.set(g, [])
                    grouped.push({ name: g, items: groupMap.get(g) })
                  }
                  groupMap.get(g).push({ ...f, _flatIdx: idx })
                })
                return createPortal(
                  <div style={{
                    position: 'fixed', top: mentionPos.top, left: mentionPos.left,
                    background: bg, border: bdr, borderRadius: 6,
                    boxShadow: shadow, maxHeight: 260, overflowY: 'auto',
                    zIndex: 11000, minWidth: 260,  // modal z-index 10000'in uzerinde
                  }}>
                    {grouped.map((grp, gIdx) => (
                      <div key={grp.name}>
                        {/* Grup baslik — birden fazla grup varsa goster */}
                        {grouped.length > 1 && (
                          <div style={{
                            padding: '4px 10px', fontSize: 9.5, fontWeight: 700,
                            textTransform: 'uppercase', letterSpacing: 0.5,
                            color: lblClr, opacity: 0.7,
                            background: isLight ? 'rgba(99,102,241,.06)' : 'rgba(99,102,241,.12)',
                            borderTop: gIdx > 0 ? divBdr : 'none',
                          }}>
                            {grp.name}
                          </div>
                        )}
                        {grp.items.map((f) => {
                          const i = f._flatIdx
                          return (
                            <div key={(f.token || '') + '-' + i}
                              onMouseDown={(e) => { e.preventDefault(); selectMention(f) }}
                              onMouseEnter={() => setMentionIndex(i)}
                              title={f.secondary || f.token}
                              style={{
                                padding: '5px 10px', fontSize: 11, cursor: 'pointer',
                                background: i === mentionIndex
                                  ? (isLight ? 'rgba(99,102,241,.10)' : 'rgba(99,102,241,.20)')
                                  : 'transparent',
                                color: i === mentionIndex ? accent : iClr,
                                display: 'flex', alignItems: 'center', gap: 8,
                              }}>
                              <span style={{ fontWeight: 600, flex: '1 1 auto', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                                {f.label}
                              </span>
                            </div>
                          )
                        })}
                      </div>
                    ))}
                  </div>,
                  document.body
                )
              })()}
            </div>
            <div style={{ fontSize: 10, color: lblClr, marginTop: 4, lineHeight: 1.5 }}>
              Bu fragment view'in WHERE klauzülüne eklenir (mevcut WHERE'a AND ile bağlanır).
              <strong style={{ color: accent }}>@</strong> yazarak alan ekleyebilirsiniz (↑↓ ile gezin, Enter/Tab ile seçin).
              Tokenler: <code style={{ fontFamily: 'monospace' }}>{'{#fieldId}'}</code> form, {' '}
              <code style={{ fontFamily: 'monospace' }}>{'{#row.fieldKey}'}</code> kalem,{' '}
              <code style={{ fontFamily: 'monospace' }}>{'{#row.combo.attr}'}</code> kombinasyon — runtime'da değerleri yerine geçer.
            </div>
          </div>
        </div>

        {/* Footer */}
        <div style={{
          padding: '10px 16px', borderTop: divBdr, background: hdrBg,
          flexShrink: 0, display: 'flex', gap: 8, alignItems: 'center',
        }}>
          <button type="button" onClick={handleSave} disabled={externalSaving || !viewCode}
            style={{
              padding: '7px 18px', fontSize: 12, fontWeight: 600,
              background: (externalSaving || !viewCode) ? (isLight ? '#e2e8f0' : 'rgba(255,255,255,0.1)') : accent,
              color: (externalSaving || !viewCode) ? lblClr : '#fff',
              border: 'none', borderRadius: 6,
              cursor: (externalSaving || !viewCode) ? 'not-allowed' : 'pointer',
            }}>
            {externalSaving ? 'Kaydediliyor…' : 'Kaydet'}
          </button>
          <button type="button" onClick={onClose}
            style={{ padding: '7px 16px', fontSize: 12, background: 'transparent', color: lblClr, border: iBdr, borderRadius: 6, cursor: 'pointer' }}>
            İptal
          </button>
        </div>
      </div>
    </div>,
    document.body
  )
}

function buildDefaultColumns(view) {
  if (!view || !Array.isArray(view.columns)) return []
  return view.columns.map(name => ({ name, label: name, visible: true, distinct: false }))
}

/**
 * Auto-detect: ilk INT-benzeri kolon (Id/ID/__Id) yoksa ilk kolon — value column.
 * Bu basit heuristic; kullanici modal'da elle override edebilir.
 */
function pickValueColumn(cols) {
  if (!Array.isArray(cols) || cols.length === 0) return ''
  const idMatch = cols.find(c => /^id$/i.test(c) || /id$/i.test(c))
  return idMatch || cols[0]
}

/**
 * Auto-detect: Name/Title/Description benzeri kolon yoksa ikinci kolon — display column.
 */
function pickDisplayColumn(cols) {
  if (!Array.isArray(cols) || cols.length === 0) return ''
  const namedMatch = cols.find(c => /(name|title|description|label)$/i.test(c))
  if (namedMatch) return namedMatch
  return cols.length >= 2 ? cols[1] : cols[0]
}
