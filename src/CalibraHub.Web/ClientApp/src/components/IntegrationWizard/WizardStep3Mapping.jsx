/**
 * Step 3 — Alan eşleme (Mapping editor) — yatay "üst + kalem" görünümü.
 *
 * Mevcut belge formlarındaki "üst (FatUst) + kalem (Kalems[])" deneyimini
 * taklit eder. Her root key bir bölüm (kart) olur:
 *   • Object grup → tek satırlık kart (tek kayıt için fixed alanlar)
 *   • Array grup  → kalem tablosu (her execute'da tekrar eden satır şablonu)
 *
 * Path'in ilk segmenti grup adı:
 *   "FatUst.CariKod"     → grup="FatUst" (object)
 *   "Kalems[].StokKodu"  → grup="Kalems" (array — sonu [] ise veya bodySchema'da array)
 *   "Seri"               → grup="(genel)" (no nesting)
 *
 * Her gruptaki alanlar yatay grid olarak gösterilir; tıklanınca alttan inline
 * editor açılır (mevcut iw-mapping-edit yapısı korundu).
 */
import React, { useState, useEffect, useCallback, useMemo, useRef } from 'react'
import { createPortal } from 'react-dom'
import {
  Plus, Trash2, ChevronDown, ChevronRight, Sparkles, Loader2,
  Box, ListTree, Layers, AlertCircle, Filter, X, ChevronDown as CaretDown,
  EyeOff, Eye,
} from 'lucide-react'

// ────────────────────────────────────────────────────────────────────────────
// SearchableCombo — Tasarım Kuralı (DocLayoutRule) ekranındaki cari arama
// dropdown'unu taklit eden reusable combobox.
//
// Props:
//   value      : string — secili deger (input'ta gosterilen)
//   onChange   : (value: string) => void
//   options    : Array<{ value:string, label?:string, hint?:string }>
//                — label yoksa value gosterilir; hint sag tarafta kucuk gri chip.
//   placeholder, monospace, allowFreeText (default true), disabled
//
// Davranis:
//   - Tıklayinca acilir, yazınca filtrelenir
//   - ArrowUp/Down + Enter ile klavye navigasyonu
//   - Escape ile kapanir
//   - Bos input + focus = hepsi listelenir (ilk 100)
//   - allowFreeText=false ise sadece listeden secime izin verir
// ────────────────────────────────────────────────────────────────────────────
function SearchableCombo({
  value, onChange, options,
  placeholder, monospace = false, allowFreeText = true, disabled = false,
  size = 'md',
}) {
  const [open, setOpen]         = useState(false)
  const [query, setQuery]       = useState(value || '')
  const [focusIdx, setFocusIdx] = useState(0)
  const [menuRect, setMenuRect] = useState(null)   // {left, top, width} — body-portal positioning
  const inputRef = useRef(null)
  const wrapRef  = useRef(null)

  // Parent value degisirse query'i sync et
  useEffect(() => { setQuery(value || '') }, [value])

  const trimmed = (query || '').trim().toLowerCase()
  const filtered = useMemo(() => {
    const all = options || []
    if (trimmed.length === 0) return all.slice(0, 100)
    return all.filter(o => {
      const lbl  = (o.label || o.value || '').toLowerCase()
      const val  = (o.value || '').toLowerCase()
      const hint = (o.hint || '').toLowerCase()
      return lbl.includes(trimmed) || val.includes(trimmed) || hint.includes(trimmed)
    }).slice(0, 100)
  }, [options, trimmed])

  // Acik iken pozisyonu input'un viewport koordinatina kilitle
  // (Tablo/grup overflow: hidden parent'larini bypass etmek icin position: fixed)
  const recomputeRect = useCallback(() => {
    if (!wrapRef.current) return
    const r = wrapRef.current.getBoundingClientRect()
    // Asagi tasacaksa yukari ac
    const spaceBelow = window.innerHeight - r.bottom
    const openUp = spaceBelow < 220 && r.top > 220
    setMenuRect({
      left:  r.left,
      top:   openUp ? r.top - 4 : r.bottom + 2,
      width: r.width,
      openUp,
    })
  }, [])

  useEffect(() => {
    if (!open) { setMenuRect(null); return }
    recomputeRect()
    const onScroll = () => recomputeRect()
    const onResize = () => recomputeRect()
    // Capture: ic scroll container'larda da tetiklensin
    window.addEventListener('scroll', onScroll, true)
    window.addEventListener('resize', onResize)
    return () => {
      window.removeEventListener('scroll', onScroll, true)
      window.removeEventListener('resize', onResize)
    }
  }, [open, recomputeRect])

  // Disaridaki tiklama → kapat (input + portal'i da kapsa)
  useEffect(() => {
    if (!open) return
    const handler = (e) => {
      const inWrap   = wrapRef.current && wrapRef.current.contains(e.target)
      const inPortal = e.target.closest && e.target.closest('[data-sc-menu]')
      if (!inWrap && !inPortal) setOpen(false)
    }
    document.addEventListener('mousedown', handler)
    return () => document.removeEventListener('mousedown', handler)
  }, [open])

  const pick = (opt) => {
    onChange(opt.value)
    setQuery(opt.value)
    setOpen(false)
  }

  // Stiller — tablo hücresinde kompakt ama nefes alan görünüm icin "sm" mod
  // (cellInput ile aynı dikey ritim — Alan Eşleme tablosundaki tüm kontroller hizalı kalır)
  const padY = size === 'sm' ? 5 : 7
  const padX = size === 'sm' ? 6 : 10
  const fontSz = size === 'sm' ? 11 : 12.5

  // Portal menu — fixed position, body'nin sonuna render.
  // Cam (frosted-glass) efekti: yari-saydam arka plan + backdrop blur.
  // Tema bazli arka plan rengi CSS class ile (sc-menu) — light/dark farkli RGBA.
  const menu = (open && !disabled && menuRect) ? (
    <div data-sc-menu="1" className="sc-menu" style={{
      position: 'fixed', left: menuRect.left, width: menuRect.width,
      ...(menuRect.openUp
        ? { bottom: window.innerHeight - menuRect.top }
        : { top: menuRect.top }),
      maxHeight: 240, overflowY: 'auto', zIndex: 9999,
      borderRadius: 8,
      border: '1px solid var(--iw-border)',
      boxShadow: '0 14px 40px rgba(0,0,0,0.55)',
    }}>
      {filtered.length === 0 ? (
        <div style={{ padding: '10px 12px', fontSize: 11, color: 'var(--iw-muted)', textAlign: 'center' }}>
          Eşleşen kayıt yok
        </div>
      ) : filtered.map((opt, i) => (
        <div key={`${opt.value}-${i}`}
             onMouseDown={e => { e.preventDefault(); pick(opt) }}
             onMouseEnter={() => setFocusIdx(i)}
             style={{
               padding: '7px 10px', fontSize: fontSz, cursor: 'pointer',
               borderBottom: i < filtered.length - 1 ? '1px solid var(--iw-border)' : 'none',
               background: i === focusIdx ? 'var(--iw-indigo-bg)' : 'transparent',
               color: 'var(--iw-text)',
               display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 8,
             }}>
          <span style={{ fontFamily: monospace ? 'ui-monospace, Menlo, Consolas, monospace' : 'inherit',
                         overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', flex: 1 }}>
            {opt.label || opt.value}
          </span>
          {opt.hint && (
            <span style={{
              fontSize: 10, color: 'var(--iw-muted)', fontFamily: 'ui-monospace, Menlo, Consolas, monospace',
              background: 'var(--iw-bg)', padding: '1px 5px', borderRadius: 3, flexShrink: 0,
            }}>{opt.hint}</span>
          )}
        </div>
      ))}
    </div>
  ) : null

  return (
    <div ref={wrapRef} style={{ position: 'relative', width: '100%' }}>
      <div style={{ position: 'relative' }}>
        <input ref={inputRef}
               type="text" value={query} disabled={disabled} autoComplete="off"
               placeholder={placeholder || ''}
               onChange={e => {
                 setQuery(e.target.value); setOpen(true); setFocusIdx(0)
                 if (allowFreeText) onChange(e.target.value)
               }}
               onFocus={() => setOpen(true)}
               onKeyDown={e => {
                 if (e.key === 'ArrowDown') {
                   e.preventDefault(); setOpen(true)
                   setFocusIdx(i => Math.min(filtered.length - 1, i + 1))
                 } else if (e.key === 'ArrowUp') {
                   e.preventDefault()
                   setFocusIdx(i => Math.max(0, i - 1))
                 } else if (e.key === 'Enter') {
                   if (open && filtered[focusIdx]) {
                     e.preventDefault(); pick(filtered[focusIdx])
                   }
                 } else if (e.key === 'Escape') {
                   setOpen(false)
                 }
               }}
               style={{
                 width: '100%', padding: `${padY}px ${padX + 18}px ${padY}px ${padX}px`,
                 fontSize: fontSz, border: '1px solid var(--iw-border)', borderRadius: 5,
                 background: 'var(--iw-bg)', color: 'var(--iw-text)', outline: 'none',
                 fontFamily: monospace ? 'ui-monospace, Menlo, Consolas, monospace' : 'inherit',
                 opacity: disabled ? 0.5 : 1, boxSizing: 'border-box',
               }} />
        <CaretDown size={12}
                   onClick={() => { if (!disabled) { setOpen(o => !o); inputRef.current?.focus() } }}
                   style={{
                     position: 'absolute', right: 4, top: '50%', transform: 'translateY(-50%)',
                     color: 'var(--iw-muted)', cursor: disabled ? 'default' : 'pointer',
                     pointerEvents: disabled ? 'none' : 'auto',
                   }} />
      </div>
      {menu && createPortal(menu, document.body)}
    </div>
  )
}

// Lookup constraint operator listesi
const LOOKUP_OPERATORS = [
  { value: 'eq',  label: '=' },
  { value: 'neq', label: '≠' },
  { value: 'gt',  label: '>' },
  { value: 'gte', label: '≥' },
  { value: 'lt',  label: '<' },
  { value: 'lte', label: '≤' },
  { value: 'like', label: 'içerir' },
  { value: 'in',  label: 'IN (virgüllü)' },
]

/** Lookup filter JSON parse — bozuksa boş array doner. */
function parseLookupFilters(json) {
  if (!json) return []
  try {
    const arr = JSON.parse(json)
    return Array.isArray(arr) ? arr : []
  } catch { return [] }
}

function stringifyLookupFilters(arr) {
  const clean = (arr || [])
    .map(x => {
      const out = { ...x }
      // __SELECT__ frontend placeholder — kullanici alan secmeden modu degistirdi, temizle
      if (out.sourceField === '__SELECT__') out.sourceField = ''
      return out
    })
    .filter(x => x && (x.field || x.rawSql))
  return clean.length === 0 ? null : JSON.stringify(clean)
}

// SourceType enum'a karsi gelen sayilar (backend ile eslesir)
const SOURCE_TYPES = [
  { value: 0, label: 'Form Alanı',   key: 'formfield', desc: 'Kaynak formdan bir alan' },
  { value: 1, label: 'Sabit Değer',  key: 'constant',  desc: 'Statik literal değer' },
  { value: 2, label: 'Formül',       key: 'formula',   desc: 'NCalc expression (Adet * BirimFiyat)' },
  { value: 3, label: 'Rehber',       key: 'lookup',    desc: 'Standart rehber lookup (cbv_Guide_*)' },
  { value: 4, label: 'Fonksiyon',    key: 'function',  desc: 'Sabit fonksiyon: Stok/Cari/Depo gibi pre-defined entity\'lerden anahtar→deger cek' },
]
const DATA_TYPES = ['string', 'numeric', 'decimal', 'int', 'date', 'datetime', 'bool']
const ROOT_GROUP = '(genel)'

/**
 * Format alani placeholder — hedef tipe gore baglama uygun ornek goster.
 * Engine: MappingEngine.ApplyFormat (date/decimal/string brancindaki pattern'ler).
 */
function formatPlaceholder(dataType) {
  switch ((dataType || '').toLowerCase()) {
    case 'date':
    case 'datetime': return 'yyyy-MM-dd'
    case 'decimal':
    case 'numeric':
    case 'money':    return 'F2 (örn. 1234,56)'
    case 'int':
    case 'integer':  return '(format yok)'
    case 'bool':
    case 'boolean':  return '(format yok)'
    case 'string':
    case 'text':
    default:         return 'upper / lower / trim'
  }
}

/** Format alani tooltip — daha detayli aciklama. */
function formatTooltip(dataType) {
  switch ((dataType || '').toLowerCase()) {
    case 'date':
    case 'datetime':
      return 'Tarih format pattern. Örnek: "yyyy-MM-dd" → 2026-05-14, "dd.MM.yyyy" → 14.05.2026, "yyyy-MM-ddTHH:mm:ss" → ISO 8601. Boş = ISO default.'
    case 'decimal':
    case 'numeric':
    case 'money':
      return 'Sayı format pattern. Örnek: "F2" → 2 ondalık, "N0" → binlik ayraçlı tam sayı, "0.0000" → 4 ondalık sabit. Boş = 2 ondalık.'
    case 'int':
    case 'integer':
    case 'bool':
    case 'boolean':
      return 'Bu tip için format pattern yok — değer doğrudan gönderilir.'
    case 'string':
    case 'text':
    default:
      return 'Metin dönüşümü: "upper" → BÜYÜK, "lower" → küçük, "trim" → baştaki/sondaki boşlukları sil. Boş = dokunma.'
  }
}

/**
 * Endpoint body schema'sını parse eder ve root-level her key'in array mi
 * object mi olduğunu döner. Otomatik grup tipi tespiti için.
 *   { FatUst: 'object', Kalems: 'array', Seri: 'leaf', KDV_DAHILMI: 'leaf' }
 */
function parseSchemaTopology(schemaText) {
  if (!schemaText) return {}
  try {
    const obj = JSON.parse(schemaText)
    if (!obj || typeof obj !== 'object') return {}
    const topo = {}
    Object.keys(obj).forEach(k => {
      const v = obj[k]
      if (Array.isArray(v))                   topo[k] = 'array'
      else if (v && typeof v === 'object')    topo[k] = 'object'
      else                                    topo[k] = 'leaf'
    })
    return topo
  } catch { return {} }
}

/**
 * Body schema'yı tam dolaşır ve her grup için leaf path listesi cıkarir.
 * Step 3'te Hedef Path inputuna datalist autocomplete olarak verilir.
 *
 * Cikis ornegi:
 *   {
 *     "FatUst":  ["FatUst.FATIRS_NO", "FatUst.Tarih", "FatUst.CariKod"],
 *     "Kalems":  ["Kalems[].StokKodu", "Kalems[].STra_GCMIK"],
 *     "(genel)": ["Seri", "KDV_DAHILMI"]
 *   }
 *
 * - Object grup: "Group.Field"  (ic ice walk edilir, "Group.Sub.Field" da olur)
 * - Array  grup: "Group[].Field" (array elemanin ilk objesini sablon kabul eder)
 * - Root leaf:   "Field"          → ROOT_GROUP altina dusulur
 */
function parseSchemaLeaves(schemaText) {
  const groups = {}
  if (!schemaText) return groups
  let root
  try { root = JSON.parse(schemaText) } catch { return groups }
  if (!root || typeof root !== 'object') return groups

  const ROOT = ROOT_GROUP

  const pushLeaf = (groupName, path) => {
    if (!groups[groupName]) groups[groupName] = []
    if (!groups[groupName].includes(path)) groups[groupName].push(path)
  }

  // Bir alt agaci recursive walk — prefix mevcut path
  const walk = (node, prefix, groupName) => {
    if (Array.isArray(node)) {
      // Array elementinin ilk objesini sablon kabul et
      if (node.length > 0 && node[0] && typeof node[0] === 'object' && !Array.isArray(node[0])) {
        walk(node[0], prefix + '[]', groupName)
      } else {
        // Array of primitive — leaf'i prefix[]'e ekle (ornek: "Tags[]")
        pushLeaf(groupName, prefix + '[]')
      }
      return
    }
    if (node && typeof node === 'object') {
      Object.keys(node).forEach(k => {
        const child = node[k]
        const path  = prefix ? `${prefix}.${k}` : k
        if (Array.isArray(child) || (child && typeof child === 'object')) {
          walk(child, path, groupName)
        } else {
          pushLeaf(groupName, path)
        }
      })
      return
    }
    // primitive (root-level leaf zaten dis dongude handle ediliyor)
  }

  Object.keys(root).forEach(k => {
    const v = root[k]
    if (Array.isArray(v) || (v && typeof v === 'object')) {
      // Grup adi root key — ic ic walk
      walk(v, k, k)
    } else {
      // Root primitive → ROOT grup
      pushLeaf(ROOT, k)
    }
  })

  // Her grupta path'leri sirala
  Object.keys(groups).forEach(g => groups[g].sort((a, b) => a.localeCompare(b)))
  return groups
}

/**
 * Bir mapping satırının grup adını ve grup tipini cikarir.
 * targetPath'e gore:
 *   "FatUst.CariKod"     -> { name: "FatUst", kind: "object" }
 *   "Kalems[].StokKodu"  -> { name: "Kalems", kind: "array" }
 *   "Seri"               -> { name: "(genel)", kind: "object" }
 * Endpoint schema topolojisi varsa o öncelikli.
 */
function detectGroup(targetPath, topo) {
  if (!targetPath) return { name: ROOT_GROUP, kind: 'object' }
  const norm = String(targetPath).trim()
  if (!norm.includes('.')) return { name: ROOT_GROUP, kind: 'object' }
  let first = norm.split('.')[0]
  let kind = 'object'
  if (first.endsWith('[]')) {
    first = first.slice(0, -2)
    kind = 'array'
  } else if (topo[first] === 'array') {
    kind = 'array'
  }
  return { name: first, kind }
}

/** Bir mapping path'inden yaprak (leaf) adini cıkar — chip etiketi için. */
function leafName(path) {
  if (!path) return '(boş)'
  const parts = String(path).split('.')
  const last = parts[parts.length - 1] || path
  return last.replace('[]', '')
}

// ── Tablo cell stilleri (Alan Eşleme tablosu) ──
const cellHeader = {
  padding: '8px 8px',
  textAlign: 'left',
  borderBottom: '1px solid var(--iw-border)',
}
// İkincil sütun başlığı (Varsayılan / Format / Zor.) — az kullanılan kontroller;
// ana odak Hedef Alan/Kaynak/Değer/Bağımlılık'ta kalsın diye görsel olarak geri çekilir.
const cellHeaderAdv = { ...cellHeader, fontWeight: 500, opacity: 0.72 }
const cellBody = {
  padding: '6px 7px',
  borderBottom: '1px solid var(--iw-border)',
  verticalAlign: 'top',
}
const cellInput = {
  width: '100%',
  padding: '5px 7px',
  fontSize: 11,
  border: '1px solid var(--iw-border)',
  borderRadius: 4,
  background: 'var(--iw-bg)',
  color: 'var(--iw-text)',
  outline: 'none',
}
// Ana alanlarla ikincil (az kullanılan) alanlar arasındaki görsel sınır — sadece
// grubun ilk sütununda (Varsayılan) kullanılır, tüm satırlar boyunca ince bir
// ayırıcı çizgi oluşturur.
const advDivider = { borderLeft: '1px dashed var(--iw-border)' }

export default function WizardStep3Mapping({ apiBase, state, update }) {
  const [fields, setFields]           = useState([])
  const [endpoint, setEndpoint]       = useState(null)
  const [expanded, setExpanded]       = useState(null)   // index of expanded mapping
  const [autoMapping, setAutoMapping] = useState(false)
  const [collapsedGroups, setCollGr]  = useState(new Set())
  const [openLookups, setOpenLookups] = useState(new Set()) // expanded lookup-advanced rows
  const [actionSlot, setActionSlot]   = useState(null)
  useEffect(() => {
    setActionSlot(document.getElementById('iw-step-actions-portal'))
  }, [])
  const [guideViews, setGuideViews]   = useState([])      // [{ viewName, schemaName, columns:[] }]
  const [guideColumns, setGuideColumns] = useState({})    // viewName → columns[] (lazy-loaded)
  const [guideSchemas, setGuideSchemas] = useState({})    // viewName → { valueColumn, displayColumn }
  // Fonksiyon (sourceType=4) icin pre-defined registry — UI dropdown'unu doldurur
  // [{id, label, description, returnColumns:[{column, label}]}]
  const [lookupFunctions, setLookupFunctions] = useState([])

  // 2026-05-22 Cascade: "Bağımlılık" dropdown verisi — aktif + AllowAsCascadeTarget=true
  // tüm integration'lar. Kendi kendine cascade etmeyi engellemek için excludeId ile
  // mevcut wizard integration'ı (state.id) filtrelenir. Yüklemeyi bir kez yapıyoruz —
  // wizard yaşam süresi boyunca cascade target listesi nadiren değişir.
  const [cascadeTargets, setCascadeTargets] = useState([])
  useEffect(() => {
    const url = state.id && state.id > 0
      ? `${apiBase}/cascade-targets?excludeId=${state.id}`
      : `${apiBase}/cascade-targets`
    fetch(url, { credentials: 'same-origin' })
      .then(r => r.ok ? r.json() : null)
      .then(d => {
        if (d && d.success && Array.isArray(d.targets)) setCascadeTargets(d.targets)
      })
      .catch(() => {/* network — dropdown boş kalır, sorun değil */})
  }, [apiBase, state.id])

  // Sample record (Step 4'te de kullanilan) — Ornek kolonu icin lazy fetch
  const [sampleRecord, setSampleRecord] = useState(null)   // { fieldValues: { code: value } }
  const [sampleLines, setSampleLines]   = useState([])     // [{ field: value }, ...]
  useEffect(() => {
    if (!state.sourceFormCode) return
    fetch(`${apiBase}/sample?formCode=${encodeURIComponent(state.sourceFormCode)}`,
          { credentials: 'same-origin' })
      .then(r => r.ok ? r.json() : null)
      .then(d => {
        if (d && d.success && d.sample) {
          setSampleRecord(d.sample)
          // V1: lines sample yok (server tarafindan ayri endpoint olabilir) — bos gec
          setSampleLines([])
        }
      })
      .catch(() => {/* sample yoksa onizleme bos */})
  }, [apiBase, state.sourceFormCode])

  // Field docs — Step 1'de body schema'da ⓘ tooltip icin kullanilan ayni katalog.
  // Step 2'de "Olasi Degerler" kolonu icin hedef path'e gore allowed values goster.
  // { "FatUst.Tip": { description, allowedValues:[{value,label}], example, notes } }
  const [fieldDocs, setFieldDocs] = useState({})
  useEffect(() => {
    if (!state.targetEndpointId) { setFieldDocs({}); return }
    fetch(`${apiBase}/field-docs?endpointId=${state.targetEndpointId}`,
          { credentials: 'same-origin' })
      .then(r => r.json())
      .then(d => {
        const docs = d?.success ? (d.docs || {}) : {}
        console.log('[WizardStep3] field-docs loaded:', Object.keys(docs).length, 'keys', Object.keys(docs).slice(0, 5))
        setFieldDocs(docs)
      })
      .catch(err => { console.warn('[WizardStep3] field-docs fetch failed:', err); setFieldDocs({}) })
  }, [apiBase, state.targetEndpointId])

  // Grup gizleme — kullanici "EIrsEkBilgi'ye ihtiyacim yok" deyince butun grubu sakla.
  // localStorage'da endpoint bazli saklanir; "Gizli gruplari goster" toggle ile geri acilir.
  const [hiddenGroups, setHiddenGroups] = useState(() => new Set())
  const [showHiddenGroups, setShowHiddenGroups] = useState(false)
  const groupHideKey = state.targetEndpointId ? `iw:hiddenGroups:${state.targetEndpointId}` : null
  useEffect(() => {
    if (!groupHideKey) { setHiddenGroups(new Set()); return }
    try {
      const raw = localStorage.getItem(groupHideKey)
      setHiddenGroups(raw ? new Set(JSON.parse(raw)) : new Set())
    } catch { setHiddenGroups(new Set()) }
  }, [groupHideKey])
  const toggleHideGroup = useCallback((name) => {
    setHiddenGroups(prev => {
      const next = new Set(prev)
      if (next.has(name)) next.delete(name); else next.add(name)
      if (groupHideKey) {
        try { localStorage.setItem(groupHideKey, JSON.stringify([...next])) } catch {/* */}
      }
      return next
    })
  }, [groupHideKey])
  const clearHiddenGroups = useCallback(() => {
    setHiddenGroups(new Set())
    if (groupHideKey) { try { localStorage.removeItem(groupHideKey) } catch {/* */} }
  }, [groupHideKey])
  useEffect(() => {
    // Wizard "Fonksiyon" source dropdown'i artik direkt DB'deki SQL fonksiyonlari listeler
    // (admin "Lookup Fonksiyonu" tablosu by-pass). Kullanici DB'de 3-paramli function
    // tanimlar, burada secip @P2 (form alani) + @P3 (manuel param) ekler.
    fetch('/Integrations/api/db-functions', { credentials: 'same-origin' })
      .then(r => r.ok ? r.json() : null)
      .then(d => {
        if (!(d && d.success && Array.isArray(d.functions))) return
        // DB function listesi → wizard shape'ine map (kind='sqlfn' = 3. kolon manuel param)
        const mapped = d.functions.map(fn => ({
          id: fn.fullName,
          label: fn.fullName,
          description: (fn.type === 'FN' ? 'scalar function' : (fn.type === 'IF' ? 'inline TVF' : 'TVF')) +
                       ' · ' + fn.parameterCount + ' param' +
                       (fn.parameterCount !== 3 ? ' ⚠' : ''),
          kind: 'sqlfn',
          returnColumns: [],
          paramCount: fn.parameterCount,
        }))
        // 3-paramli olanlar onerilen — basa
        mapped.sort((a, b) => {
          if ((a.paramCount === 3) !== (b.paramCount === 3)) return a.paramCount === 3 ? -1 : 1
          return a.id.localeCompare(b.id)
        })
        setLookupFunctions(mapped)
      })
      .catch(() => {})
  }, [])

  // Form alanlarini cek
  useEffect(() => {
    if (!state.sourceFormCode) return
    fetch(`${apiBase}/forms/${encodeURIComponent(state.sourceFormCode)}/fields`,
          { credentials: 'same-origin' })
      .then(r => r.json())
      .then(d => { if (d.success) setFields(d.fields || []) })
  }, [apiBase, state.sourceFormCode])

  // Rehber view listesini bir kere cek — Lookup sourceValue dropdown'u icin
  useEffect(() => {
    fetch(`/api/guides/views`, { credentials: 'same-origin' })
      .then(r => r.ok ? r.json() : [])
      .then(d => { if (Array.isArray(d)) setGuideViews(d) })
      .catch(() => {})
  }, [])

  // Edit modunda yuklenen Lookup mapping'leri icin sourceValue (view adi) varsa
  // o view'in kolonlarini + schema'sini (ValueColumn / DisplayColumn) bir kere
  // cek — return/key column dropdown'larini ve "eslesir" chip'ini beslemek icin.
  useEffect(() => {
    const lookupViews = state.mappings
      .filter(m => m.sourceType === 3 && m.sourceValue)
      .map(m => m.sourceValue)
    const unique = Array.from(new Set(lookupViews))
    unique.forEach(viewName => {
      if (!guideColumns[viewName]) {
        fetch(`/api/guides/views/${encodeURIComponent(viewName)}/columns`,
          { credentials: 'same-origin' })
          .then(r => r.ok ? r.json() : [])
          .then(cols => {
            if (Array.isArray(cols))
              setGuideColumns(prev => prev[viewName] ? prev : { ...prev, [viewName]: cols })
          })
          .catch(() => {})
      }
      if (!guideSchemas[viewName]) {
        fetch(`/api/guides/${encodeURIComponent(viewName)}/schema`,
          { credentials: 'same-origin' })
          .then(r => r.ok ? r.json() : null)
          .then(s => {
            if (s && s.valueColumn) {
              setGuideSchemas(prev => prev[viewName] ? prev : {
                ...prev,
                [viewName]: { valueColumn: s.valueColumn, displayColumn: s.displayColumn }
              })
            }
          })
          .catch(() => {})
      }
    })
  }, [state.mappings, guideColumns, guideSchemas])

  // Endpoint'i cek (body schema topolojisi icin)
  useEffect(() => {
    if (!state.targetEndpointId) { setEndpoint(null); return }
    fetch(`${apiBase}/endpoints/${state.targetEndpointId}`, { credentials: 'same-origin' })
      .then(r => r.json())
      .then(d => { if (d.success) setEndpoint(d.endpoint) })
      .catch(() => setEndpoint(null))
  }, [apiBase, state.targetEndpointId])

  const topology = useMemo(() => parseSchemaTopology(endpoint?.bodySchema), [endpoint])
  const schemaLeaves = useMemo(() => parseSchemaLeaves(endpoint?.bodySchema), [endpoint])

  // Mapping'leri gruplara ayir — schema topolojisi de kullanılır
  const groups = useMemo(() => {
    const map = new Map()

    // 1) Once mevcut mapping satırlarını grupla
    state.mappings.forEach((m, idx) => {
      const g = detectGroup(m.targetPath, topology)
      if (!map.has(g.name)) map.set(g.name, { name: g.name, kind: g.kind, rows: [] })
      // Bir grup hem object hem array olarak detect edildiyse array kalsin (daha bilgilendirici)
      if (g.kind === 'array') map.get(g.name).kind = 'array'
      map.get(g.name).rows.push({ ...m, _idx: idx })
    })

    // 2) Schema'daki ama henuz mapping'i olmayan grupları "boş grup" olarak ekle
    let hasRootLeaf = false
    Object.keys(topology).forEach(k => {
      if (topology[k] === 'leaf') { hasRootLeaf = true; return }  // root-level leaf'ler (genel)'e gider
      if (!map.has(k)) map.set(k, { name: k, kind: topology[k], rows: [] })
    })
    // Root-level primitive alanlar varsa "(genel)" grubunu da bos olarak ekle —
    // boylece kullanici Use64BitService, TransactSupport gibi alanlar icin
    // mapping satiri ekleyebilir.
    if (hasRootLeaf && !map.has(ROOT_GROUP)) {
      map.set(ROOT_GROUP, { name: ROOT_GROUP, kind: 'object', rows: [] })
    }

    // 3) Sirala — root (genel) en altta; arraylar sonda; alfabetik
    return Array.from(map.values()).sort((a, b) => {
      if (a.name === ROOT_GROUP && b.name !== ROOT_GROUP) return  1
      if (b.name === ROOT_GROUP && a.name !== ROOT_GROUP) return -1
      if (a.kind === 'array' && b.kind !== 'array')       return  1
      if (b.kind === 'array' && a.kind !== 'array')       return -1
      return a.name.localeCompare(b.name, 'tr')
    })
  }, [state.mappings, topology])

  const updateRow = useCallback((idx, patch) => {
    const next = state.mappings.map((m, i) => i === idx ? { ...m, ...patch } : m)
    update({ mappings: next })
  }, [state.mappings, update])

  const removeRow = useCallback((idx) => {
    const next = state.mappings.filter((_, i) => i !== idx)
    next.forEach((m, i) => { m.sortOrder = i + 1 })
    update({ mappings: next })
    setExpanded(null)
  }, [state.mappings, update])

  /** Bir gruba yeni mapping satırı ekle — path'in prefix'i + sourceSection otomatik gelir. */
  const addRowToGroup = useCallback((groupName, kind) => {
    const prefix =
      groupName === ROOT_GROUP ? '' :
      kind === 'array' ? `${groupName}[].` : `${groupName}.`
    // Master-Detail: array hedefler Lines, object/root hedefler Header alır
    const defaultSection = kind === 'array' ? 'Lines' : 'Header'
    const newRow = {
      targetPath: prefix,
      targetDataType: 'string',
      sourceType: 0,
      sourceValue: '',
      lookupSourceField: null,
      lookupReturnColumn: null,
      lookupParam: null,                    // SqlFn modu icin manuel @P3
      defaultValue: null,
      formatPattern: null,
      isRequired: false,
      sortOrder: state.mappings.length + 1,
      groupKey: groupName === ROOT_GROUP ? null : groupName,
      sourceSection: defaultSection,
      cascadeToIntegrationId: null,         // 2026-05-22 Cascade: FK alanı için hedef integration
    }
    const newIdx = state.mappings.length
    update({ mappings: [...state.mappings, newRow] })
    setExpanded(newIdx)
  }, [state.mappings, update])

  /** Otomatik Eşle — ayni isimde form alani olan target path'leri FormField olarak ekler */
  const autoMap = useCallback(async () => {
    if (!state.targetEndpointId || fields.length === 0) return
    setAutoMapping(true)
    try {
      const r = await fetch(`${apiBase}/endpoints/${state.targetEndpointId}`,
                            { credentials: 'same-origin' })
      const d = await r.json()
      if (!d.success || !d.endpoint?.bodySchema) return
      let schema = null
      try { schema = JSON.parse(d.endpoint.bodySchema) } catch { return }
      if (!schema || typeof schema !== 'object') return

      // Tum target path'leri flatten et — array elementlerinde [] notasyonu kullan
      const targetPaths = []
      const walk = (obj, prefix) => {
        if (Array.isArray(obj)) {
          // Array sample — ilk eleman uzerinden walk
          if (obj.length > 0 && typeof obj[0] === 'object') {
            walk(obj[0], prefix + '[]')
          }
          return
        }
        Object.keys(obj).forEach(k => {
          const val = obj[k]
          const path = prefix ? `${prefix}.${k}` : k
          if (Array.isArray(val))                 walk(val, path)
          else if (val && typeof val === 'object') walk(val, path)
          else                                     targetPaths.push(path)
        })
      }
      walk(schema, '')

      const fieldByLower = {}
      fields.forEach(f => { fieldByLower[f.code.toLowerCase()] = f })
      const existingPaths = new Set(state.mappings.map(m => m.targetPath))
      const additions = []

      targetPaths.forEach(tp => {
        if (existingPaths.has(tp)) return
        const leaf = tp.split('.').pop().replace('[]', '').toLowerCase()
        const m = fieldByLower[leaf]
        if (m) {
          additions.push({
            targetPath: tp,
            targetDataType: m.dataType || 'string',
            sourceType: 0,
            sourceValue: m.code,
            lookupSourceField: null,
            defaultValue: null,
            formatPattern: null,
            isRequired: m.isRequired,
            sortOrder: state.mappings.length + additions.length + 1,
            groupKey: tp.includes('.') ? tp.split('.')[0].replace('[]', '') : null,
          })
        }
      })

      if (additions.length === 0) {
        if (window.CalibraHub?.toast) window.CalibraHub.toast('Eşleştirme yapacak alan bulunamadı.', 'warn')
      } else {
        update({ mappings: [...state.mappings, ...additions] })
        if (window.CalibraHub?.toast) window.CalibraHub.toast(`${additions.length} alan otomatik eşleştirildi.`, 'ok')
      }
    } finally {
      setAutoMapping(false)
    }
  }, [apiBase, state.targetEndpointId, state.mappings, fields, update])

  const toggleGroup = (name) => {
    setCollGr(s => {
      const next = new Set(s)
      if (next.has(name)) next.delete(name); else next.add(name)
      return next
    })
  }

  // Faz O — Sadece Prosedür modu: explicit flag (state.procedureOnlyMode).
  const isProcedureOnly = state.procedureOnlyMode === true
  if (isProcedureOnly) {
    return (
      <>
        <h2 className="iw-step-title">Alan Eşleme — Atlandı</h2>
        <div style={{
          maxWidth: 1200, marginTop: 18, padding: 24,
          border: '1px solid var(--iw-emerald-color)', borderRadius: 10,
          background: 'var(--iw-emerald-bg)', color: 'var(--iw-text)',
          textAlign: 'center', fontSize: 14, lineHeight: 1.7,
        }}>
          ⚙ <strong>Sadece Prosedür Modu</strong> aktif — alan eşleme bu adımda yapılmaz.<br />
          Step 5'te <strong>Öncesi/Sonrası Prosedür</strong> kartlarından prosedürünüzü tanımlayın.
          Prosedür parametrelerine form alanı, sabit veya RunMeta verisi geçebilirsiniz.
        </div>
      </>
    )
  }

  return (
    <>
      {actionSlot && createPortal(
        <>
          <button className="iw-btn-secondary" onClick={autoMap}
                  disabled={!state.targetEndpointId || fields.length === 0 || autoMapping}>
            {autoMapping ? <Loader2 className="iw-spin" size={13} /> : <Sparkles size={13} />}
            Otomatik Eşle
          </button>
          {hiddenGroups.size > 0 && (
            <button className="iw-btn-secondary"
                    onClick={() => setShowHiddenGroups(s => !s)}
                    title={showHiddenGroups ? 'Gizlileri tekrar gizle' : 'Gizlenen grupları göster'}>
              {showHiddenGroups ? <Eye size={12} /> : <EyeOff size={12} />}
              {showHiddenGroups ? `${hiddenGroups.size} gizli görünür` : `${hiddenGroups.size} grup gizli`}
            </button>
          )}
          {hiddenGroups.size > 0 && (
            <button className="iw-btn-secondary" onClick={clearHiddenGroups}
                    title="Tüm grup gizlemelerini sıfırla">
              Sıfırla
            </button>
          )}
        </>,
        actionSlot
      )}


      {/* Grup yok — boş durum */}
      {groups.length === 0 && (
        <div className="iw-mapping-pane" style={{ maxWidth: 1700, textAlign: 'center', padding: 32, color: 'var(--iw-muted)' }}>
          <AlertCircle size={32} style={{ opacity: 0.3, marginBottom: 8 }} />
          <div style={{ fontSize: 13, marginBottom: 4 }}>Henüz mapping yok.</div>
          <div style={{ fontSize: 11 }}>"Otomatik Eşle" deneyin veya aşağıdaki bir gruba alan ekleyin.</div>
          {Object.keys(topology).length === 0 && (
            <div style={{ fontSize: 11, marginTop: 6 }}>
              <em>Endpoint Body Schema'sı boşsa Step 2'ye dönüp "Otomatik Çek" veya "Şablon Galerisi" deneyin.</em>
            </div>
          )}
        </div>
      )}

      {/* Gruplar */}
      <div style={{ display: 'flex', flexDirection: 'column', gap: 18, maxWidth: 1700 }}>
        {groups.filter(g => showHiddenGroups || !hiddenGroups.has(g.name)).map(group => {
          const isCollapsed = collapsedGroups.has(group.name)
          const isArray = group.kind === 'array'
          const isRoot  = group.name === ROOT_GROUP
          const isHidden = hiddenGroups.has(group.name)

          return (
            <div key={group.name} style={{
              border: '1px solid ' + (isArray ? 'var(--iw-amber-color)' : 'var(--iw-indigo-bdr)'),
              borderRadius: 12, background: 'var(--iw-surface)', overflow: 'visible',
              opacity: isHidden ? 0.5 : 1,
            }}>
              {/* Grup header — sticky: scroll oldukca grup biter, "Alan Ekle" / "Gizle" butonu
                  surekli erisilebilir kalir. Solid background + isolation: isolate ile alt katman
                  geciskaligini onler. */}
              <div style={{
                display: 'flex', alignItems: 'center', gap: 10,
                padding: '12px 16px',
                background: isArray ? 'var(--iw-amber-bg)' : isRoot ? 'var(--iw-bg)' : 'var(--iw-indigo-bg)',
                borderBottom: isCollapsed ? 'none' : '1px solid var(--iw-border)',
                cursor: 'pointer',
                borderTopLeftRadius: 12, borderTopRightRadius: 12,
                position: 'sticky', top: 0, zIndex: 10,
                boxShadow: '0 1px 3px rgba(0,0,0,0.10)',
              }} onClick={() => toggleGroup(group.name)}>
                {isCollapsed ? <ChevronRight size={14} /> : <ChevronDown size={14} />}
                <span style={{
                  width: 25, height: 25, borderRadius: 7,
                  display: 'flex', alignItems: 'center', justifyContent: 'center',
                  background: isArray ? 'var(--iw-amber-color)' : isRoot ? 'var(--iw-muted)' : 'var(--iw-indigo-color)',
                  color: '#fff', flexShrink: 0,
                }}>
                  {isArray ? <ListTree size={13} /> : isRoot ? <Layers size={13} /> : <Box size={13} />}
                </span>
                <div style={{ flex: 1 }}>
                  <div style={{ fontSize: 13, fontWeight: 600, color: 'var(--iw-text)' }}>
                    {isRoot ? 'Genel Alanlar' : group.name}
                    <span style={{
                      marginLeft: 8, fontSize: 10, fontWeight: 600, padding: '1px 6px', borderRadius: 4,
                      background: isArray ? 'var(--iw-amber-color)' : 'var(--iw-indigo-color)', color: '#fff',
                    }}>
                      {isArray ? 'Kalem (Her Satır İçin Tekrar)' : isRoot ? 'Root' : 'Üst'}
                    </span>
                  </div>
                  <div style={{ fontSize: 11, color: 'var(--iw-muted)', marginTop: 1 }}>
                    {group.rows.length} alan
                    {!isRoot && (
                      <> · <code style={{ fontSize: 10, fontFamily: 'ui-monospace, Menlo, Consolas, monospace' }}>
                        {isArray ? `${group.name}[].fieldName` : `${group.name}.fieldName`}
                      </code></>
                    )}
                  </div>
                </div>
                <button className="iw-btn-secondary"
                        onClick={e => { e.stopPropagation(); addRowToGroup(group.name, group.kind) }}
                        style={{ padding: '4px 10px', fontSize: 11 }}>
                  <Plus size={11} /> Alan Ekle
                </button>
                <button onClick={e => { e.stopPropagation(); toggleHideGroup(group.name) }}
                        title={isHidden
                          ? 'Bu grubu tekrar göster'
                          : 'Bu grubu gizle (bu entegrasyonda ihtiyaç yok — yarın gerekirse "Gizli grupları göster" ile geri açabilirsin)'}
                        style={{
                          padding: 6, marginLeft: 2, borderRadius: 6,
                          background: 'transparent',
                          color: isHidden ? 'var(--iw-amber-color)' : 'var(--iw-muted)',
                          border: '1px solid ' + (isHidden ? 'var(--iw-amber-color)' : 'var(--iw-border)'),
                          cursor: 'pointer', display: 'inline-flex', alignItems: 'center',
                        }}>
                  {isHidden ? <Eye size={12} /> : <EyeOff size={12} />}
                </button>
              </div>

              {/* Tablo layout — her mapping satırı yatay düzende, hücreler direkt editable */}
              {!isCollapsed && (
                <div style={{ padding: '8px 0' }}>
                  {group.rows.length === 0 && (
                    <div style={{
                      margin: '8px 14px', padding: 16, textAlign: 'center',
                      fontSize: 11, color: 'var(--iw-muted)',
                      border: '1px dashed var(--iw-line)', borderRadius: 7,
                    }}>
                      Bu grupta henüz mapping yok. "Alan Ekle" tıklayın.
                    </div>
                  )}

                  {group.rows.length > 0 && (
                    <div style={{ overflow: 'visible' }}>
                      <table style={{
                        width: '100%', borderCollapse: 'separate', borderSpacing: 0,
                        fontSize: 11, tableLayout: 'fixed',
                      }}>
                        <colgroup>
                          {/* Hedef Alan */}      <col style={{ width: '17%' }} />
                          {/* Tip */}              <col style={{ width: '110px' }} />
                          {/* Kaynak Tipi */}     <col style={{ width: '130px' }} />
                          {/* Section */}         {group.kind === 'array' && <col style={{ width: '90px' }} />}
                          {/* Source Field */}    <col style={{ width: 'auto' }} />
                          {/* Bağımlılık */}      <col style={{ width: '150px' }} />
                          {/* Varsayılan */}      <col style={{ width: '90px' }} />
                          {/* Format */}          <col style={{ width: '86px' }} />
                          {/* * */}               <col style={{ width: '46px' }} />
                          {/* 🗑 */}              <col style={{ width: '40px' }} />
                        </colgroup>
                        <thead>
                          {/* Title Case başlıklar — CLAUDE.md kuralı: text-transform:uppercase yok.
                              Ana odak (Hedef Alan / Kaynak / Alan-Değer / Bağımlılık) tam ağırlıkta;
                              az kullanılan Varsayılan/Format/Zor. cellHeaderAdv ile görsel olarak geri
                              çekilir (daha soluk + ince ayırıcı çizgi). */}
                          <tr style={{
                            fontSize: 10.5, fontWeight: 600,
                            letterSpacing: 0.15, color: 'var(--iw-muted)',
                            background: 'var(--iw-bg)',
                          }}>
                            <th style={cellHeader}>Hedef Alan *</th>
                            <th style={cellHeader}>Tip</th>
                            <th style={cellHeader}>Kaynak</th>
                            {group.kind === 'array' && <th style={cellHeader}>Section</th>}
                            <th style={cellHeader}>Alan / Değer</th>
                            <th style={cellHeader} title="FK alanları için cascade hedef entegrasyon (ERP'de yoksa önce bu çağrılır)">Bağımlılık</th>
                            <th style={{ ...cellHeaderAdv, ...advDivider }} title="Kaynak boş dönerse kullanılacak fallback değer">Varsayılan</th>
                            <th style={cellHeaderAdv} title="Tipe göre format dönüşümü (date pattern, sayı format, string upper/lower)">Format</th>
                            <th style={{ ...cellHeaderAdv, textAlign: 'center' }} title="Zorunlu alan">Zor.</th>
                            <th style={{ ...cellHeader, textAlign: 'center' }}>Sil</th>
                          </tr>
                          {/* Rehber sub-header — grup içinde en az bir Rehber (sourceType=3) satır varsa göster */}
                          {group.rows.some(r => state.mappings[r._idx]?.sourceType === 3) && (() => {
                            const lbl = { fontSize: 10, color: 'var(--iw-muted)', userSelect: 'none', paddingLeft: 2 }
                            return (
                              <tr style={{ background: 'var(--iw-bg)' }}>
                                <td colSpan={group.kind === 'array' ? 4 : 3} />
                                <td style={{ padding: '0 8px 4px' }}>
                                  <div style={{ display: 'grid', gridTemplateColumns: '2fr 1.1fr 10px 1fr 10px 1.4fr', gap: 3 }}>
                                    <span style={lbl}>Rehber</span>
                                    <span style={lbl}>Form Değeri</span>
                                    <span />
                                    <span style={lbl}>Eşleşme Kolonu</span>
                                    <span />
                                    <span style={lbl}>Dönecek Kolon</span>
                                  </div>
                                </td>
                                <td colSpan={5} />
                              </tr>
                            )
                          })()}
                        </thead>
                        <tbody>
                          {group.rows.map(r => {
                            const idx = r._idx
                            const m = state.mappings[idx]
                            if (!m) return null
                            const allowedSections = group.kind === 'array' ? ['Lines', 'Combination'] : ['Header']
                            // Bu gruba ait leaf path onerileri (zaten kullanilanlari hariç tut)
                            const allLeaves = schemaLeaves[group.name] || []
                            const usedPaths = new Set(group.rows.filter(rr => rr._idx !== idx).map(rr => state.mappings[rr._idx]?.targetPath))
                            const suggestedLeaves = allLeaves.filter(p => !usedPaths.has(p))
                            const datalistId = `dl-${group.name.replace(/[^a-zA-Z0-9_]/g, '_')}-${idx}`
                            const currentSection = m.sourceSection || (group.kind === 'array' ? 'Lines' : 'Header')
                            const sectionFields = fields.filter(f => (f.section || 'Header') === currentSection)
                            const secCfg = ({
                              Header:      { tag: 'H', color: 'indigo'  },
                              Lines:       { tag: 'L', color: 'amber'   },
                              Combination: { tag: 'C', color: 'emerald' },
                            }[currentSection] || { tag: 'H', color: 'indigo' })

                            const doc = fieldDocs[m.targetPath]
                            const hasDoc = !!(doc && (doc.description || doc.example || doc.notes || (doc.allowedValues && doc.allowedValues.length > 0)))
                            return (
                              <tr key={idx} className="iw-map-row">
                                {/* Hedef Path — searchable combo (schema leaf'larindan).
                                    fieldDoc varsa basinda 'i' info badge: hover'da aciklama + olasi degerler. */}
                                <td style={cellBody}>
                                  <div style={{ display: 'flex', alignItems: 'center', gap: 4 }}>
                                    {hasDoc && (
                                      <AllowedValuesBadge doc={doc} />
                                    )}
                                    <div style={{ flex: 1, minWidth: 0 }}>
                                      <SearchableCombo
                                        value={m.targetPath || ''}
                                        onChange={v => updateRow(idx, { targetPath: v })}
                                        options={suggestedLeaves.map(leaf => ({ value: leaf }))}
                                        placeholder={group.name === ROOT_GROUP ? 'FieldName' :
                                                     group.kind === 'array'    ? `${group.name}[].FieldName` :
                                                                                 `${group.name}.FieldName`}
                                        monospace size="sm" />
                                    </div>
                                  </div>
                                </td>
                                {/* Tip */}
                                <td style={cellBody}>
                                  <select value={m.targetDataType || 'string'}
                                          onChange={e => updateRow(idx, { targetDataType: e.target.value })}
                                          className="iw-cell-input" style={cellInput}>
                                    {DATA_TYPES.map(t => <option key={t} value={t}>{t}</option>)}
                                  </select>
                                </td>
                                {/* Kaynak Tipi */}
                                <td style={cellBody}>
                                  <select value={m.sourceType}
                                          onChange={e => updateRow(idx, { sourceType: parseInt(e.target.value) })}
                                          className="iw-cell-input" style={cellInput}>
                                    {SOURCE_TYPES.map(s => <option key={s.value} value={s.value}>{s.label}</option>)}
                                  </select>
                                </td>
                                {/* Section (sadece array group) */}
                                {group.kind === 'array' && (
                                  <td style={cellBody}>
                                    <select value={currentSection}
                                            onChange={e => updateRow(idx, { sourceSection: e.target.value, sourceValue: '' })}
                                            className="iw-cell-input"
                                            style={{ ...cellInput,
                                              background: `var(--iw-${secCfg.color}-bg)`,
                                              color:      `var(--iw-${secCfg.color}-color)`,
                                              fontWeight: 600,
                                            }}>
                                      {allowedSections.map(s => (
                                        <option key={s} value={s}>{s === 'Lines' ? 'Kalem' : s === 'Combination' ? 'Kombinasyon' : s}</option>
                                      ))}
                                    </select>
                                  </td>
                                )}
                                {/* Source Field / Value */}
                                <td style={cellBody}>
                                  {m.sourceType === 0 ? (
                                    <select value={m.sourceValue || ''}
                                            onChange={e => updateRow(idx, { sourceValue: e.target.value })}
                                            className="iw-cell-input" style={cellInput}>
                                      <option value="">— Alan seç —</option>
                                      {sectionFields.map(f => (
                                        <option key={`${f.section || 'Header'}.${f.code}`} value={f.code}>
                                          {f.label && f.label !== f.code ? `${f.label} (${f.code})` : f.code}
                                        </option>
                                      ))}
                                      {sectionFields.length === 0 && (
                                        <option disabled value="">
                                          {currentSection === 'Lines' ? 'Kalem form tanımlı değil' :
                                           currentSection === 'Combination' ? 'Kalem yok → kombinasyon yok' :
                                           'Form alanı yok'}
                                        </option>
                                      )}
                                    </select>
                                  ) : m.sourceType === 3 ? (() => {
                                    // Lookup → 4 alan tek satır kompakt:
                                    //   [Rehber] [Form Alanı] = [Kısıt Alanı] ↩ [Dönecek]
                                    const filters = parseLookupFilters(m.lookupFiltersJson)
                                    const cols = guideColumns[m.sourceValue] || []
                                    const valueCol = guideSchemas[m.sourceValue]?.valueColumn || 'Code'
                                    const displayCol = guideSchemas[m.sourceValue]?.displayColumn || 'Name'

                                    // PRIMARY WHERE — filters[0] (tek-anahtar). Geriye uyum: legacy lookupSourceField.
                                    const primary = filters[0] || {
                                      field: valueCol,
                                      operator: 'eq',
                                      sourceField: m.lookupSourceField || '',
                                      logic: 'and',
                                    }
                                    // Eski multi-WHERE varsa data kaybetme — sakla, UI'da gosterme
                                    const preservedExtras = filters.length > 1 ? filters.slice(1) : []
                                    const updatePrimary = (patch) => {
                                      const newFirst = { ...primary, ...patch }
                                      const next = [newFirst, ...preservedExtras]
                                      updateRow(idx, {
                                        lookupFiltersJson: stringifyLookupFilters(next),
                                        lookupSourceField: newFirst.sourceField || null,
                                      })
                                    }

                                    const fetchGuideMeta = (v) => {
                                      if (v && !guideColumns[v]) {
                                        fetch(`/api/guides/views/${encodeURIComponent(v)}/columns`,
                                          { credentials: 'same-origin' })
                                          .then(r => r.ok ? r.json() : [])
                                          .then(cs => { if (Array.isArray(cs)) setGuideColumns(p => ({ ...p, [v]: cs })) })
                                          .catch(() => {})
                                      }
                                      if (v && !guideSchemas[v]) {
                                        fetch(`/api/guides/${encodeURIComponent(v)}/schema`,
                                          { credentials: 'same-origin' })
                                          .then(r => r.ok ? r.json() : null)
                                          .then(s => {
                                            if (s && s.valueColumn)
                                              setGuideSchemas(p => ({ ...p, [v]: {
                                                valueColumn: s.valueColumn, displayColumn: s.displayColumn
                                              }}))
                                          })
                                          .catch(() => {})
                                      }
                                    }

                                    return (
                                      <div style={{
                                        display: 'grid',
                                        gridTemplateColumns: '2fr 1.1fr 10px 1fr 10px 1.4fr',
                                        gap: 3, alignItems: 'center',
                                      }}>
                                        {/* 1) Rehber view */}
                                        <SearchableCombo
                                          value={m.sourceValue || ''}
                                          onChange={v => { updateRow(idx, { sourceValue: v }); fetchGuideMeta(v) }}
                                          options={guideViews.map(g => ({
                                            value: g.viewName, label: g.viewName, hint: g.schemaName,
                                          }))}
                                          placeholder="cbv_Guide_..."
                                          monospace size="sm" />
                                        {/* 2) Form değeri — formdan gelen anahtar */}
                                        <select value={primary.sourceField || ''}
                                                onChange={e => updatePrimary({ sourceField: e.target.value })}
                                                className="iw-cell-input" style={{ ...cellInput, fontSize: 10 }}
                                                title="Formdan alınacak değer — rehberin eşleşme kolonuna karşılaştırılır">
                                          <option value="">— Form alanı —</option>
                                          {sectionFields.map(f => (
                                            <option key={`lk.${f.section || 'Header'}.${f.code}`} value={f.code}>
                                              {f.label}
                                            </option>
                                          ))}
                                        </select>
                                        <span style={{ textAlign: 'center', fontSize: 11, color: 'var(--iw-muted)', fontWeight: 700 }}>=</span>
                                        {/* 3) Eşleşme kolonu — rehberde WHERE filtre kolonu */}
                                        <SearchableCombo
                                          value={primary.field || ''}
                                          onChange={v => updatePrimary({ field: v })}
                                          options={cols.map(c => ({ value: c }))}
                                          placeholder={!m.sourceValue ? '↑ rehber seç' : `ör. ${valueCol}`}
                                          monospace size="sm" />
                                        <span style={{ textAlign: 'center', fontSize: 11, color: 'var(--iw-amber-color)', fontWeight: 700 }} title="Dönecek kolon">↩</span>
                                        {/* 4) Dönecek kolon — rehberden alınacak değer */}
                                        <SearchableCombo
                                          value={m.lookupReturnColumn || ''}
                                          onChange={v => updateRow(idx, { lookupReturnColumn: v || null })}
                                          options={cols.map(c => ({ value: c }))}
                                          placeholder={
                                            !m.sourceValue ? '↑ rehber seç' :
                                            cols.length === 0 ? 'Yükleniyor…' :
                                            `ör. ${displayCol}`
                                          }
                                          monospace size="sm" />
                                      </div>
                                    )
                                  })() : m.sourceType === 4 ? (() => {
                                    // Fonksiyon → 3 alan tek satır kompakt:
                                    //   [Fonksiyon] [Form Alanı = anahtar] ↩ [Dönecek Kolon]
                                    // Stok/Cari/Depo gibi pre-defined registry'den seçim, sabit
                                    // donus kolonu — Rehber'den daha sade UX. Backend registry'si
                                    // hangi view'in hangi kolondan eslesecegini bilir; kullanici
                                    // sadece "Stok" sec → "Code" don gibi sec.
                                    const fnSpec = lookupFunctions.find(f => f.id === m.sourceValue)
                                    // Yeni model: tum fonksiyonlar DB scalar function (3-paramli).
                                    // 3. kolon her zaman manuel param input (@P3).
                                    const isSqlFn = true
                                    return (
                                      <div style={{
                                        display: 'grid',
                                        gridTemplateColumns: '1.4fr 1.2fr 10px 1.4fr',
                                        gap: 3, alignItems: 'center',
                                      }}>
                                        {/* 1) Fonksiyon — registry listesinden seç */}
                                        <select value={m.sourceValue || ''}
                                                onChange={e => updateRow(idx, {
                                                  sourceValue: e.target.value,
                                                  // Function degisince donulecek kolon + manuel param resetlenir
                                                  lookupReturnColumn: null,
                                                  lookupParam: null,
                                                })}
                                                className="iw-cell-input" style={{ ...cellInput, fontSize: 10 }}
                                                title="Fonksiyon — hangi entity'den çekilecek">
                                          <option value="">— Fonksiyon —</option>
                                          {/* DB function listesi (sema.fn) — 3-paramli olanlar onerilen */}
                                          {lookupFunctions.map(fn => (
                                            <option key={fn.id} value={fn.id} title={fn.description}>
                                              {fn.paramCount === 3 ? '✓ ' : '⚠ '}{fn.label}
                                              {fn.paramCount !== 3 ? ` (${fn.paramCount} param!)` : ''}
                                            </option>
                                          ))}
                                          {/* Legacy SourceValue (mevcut kayit listede yoksa kaybolmasin) */}
                                          {m.sourceValue && !lookupFunctions.some(f => f.id === m.sourceValue) && (
                                            <option value={m.sourceValue}>
                                              ⚠ {m.sourceValue} (legacy — DB'de yok)
                                            </option>
                                          )}
                                        </select>
                                        {/* 2) Anahtar olarak gönderilecek form alanı (@P2) */}
                                        <select value={m.lookupSourceField || ''}
                                                onChange={e => updateRow(idx, { lookupSourceField: e.target.value })}
                                                className="iw-cell-input" style={{ ...cellInput, fontSize: 10 }}
                                                title={isSqlFn ? 'Form alanı → SQL function @P2 parametresi' : "Form'daki anahtar alanı (örn. ItemId)"}>
                                          <option value="">— Anahtar —</option>
                                          {sectionFields.map(f => (
                                            <option key={`${f.section || 'Header'}.${f.code}`} value={f.code}>
                                              {f.label && f.label !== f.code ? `${f.label} (${f.code})` : f.code}
                                            </option>
                                          ))}
                                        </select>
                                        <span style={{ textAlign: 'center', fontSize: 11, color: 'var(--iw-muted)' }}
                                              title={isSqlFn ? '@P3 — Manuel parametre' : 'Dönüş'}>
                                          {isSqlFn ? '+' : '↩'}
                                        </span>
                                        {/* 3) @P3 — manuel parametre input (DB function 3. parametresi) */}
                                        <input value={m.lookupParam || ''}
                                               onChange={e => updateRow(idx, { lookupParam: e.target.value || null })}
                                               placeholder="Manuel param (örn. USD)"
                                               className="iw-cell-input" style={{ ...cellInput, fontSize: 10 }}
                                               title="SQL function 3. parametresi (@P3). Mapping satırı için sabit, kullanıcı serbest yazar." />
                                      </div>
                                    )
                                  })() : (
                                    <input value={m.sourceValue || ''}
                                           placeholder={
                                             m.sourceType === 1 ? 'Sabit değer' :
                                             m.sourceType === 2 ? 'NCalc: Adet * BF' :
                                             ''
                                           }
                                           onChange={e => updateRow(idx, { sourceValue: e.target.value })}
                                           className="iw-cell-input" style={cellInput} />
                                  )}
                                </td>
                                {/* 2026-05-22 Cascade — bu mapping bir FK alanı ise hedef cascade integration'ı.
                                    Aktif olan sourceType'lar:
                                      FormField (0) → FK = sourceValue (örn. ContactId)
                                      Lookup    (3) → FK = lookupSourceField (örn. ContactId)
                                      Function  (4) → FK = lookupSourceField (örn. ItemId)
                                    Diğer (Constant, Formula) için cascade anlamsız → disabled. */}
                                <td style={cellBody}>
                                  {(m.sourceType === 0 || m.sourceType === 3 || m.sourceType === 4) ? (
                                    <div style={{ display: 'flex', flexDirection: 'column', gap: 3 }}>
                                      <select
                                        value={m.cascadeToIntegrationId ?? ''}
                                        onChange={e => updateRow(idx, {
                                          cascadeToIntegrationId: e.target.value ? parseInt(e.target.value, 10) : null,
                                          cascadeByValue: e.target.value ? m.cascadeByValue : false,
                                        })}
                                        className="iw-cell-input"
                                        style={{
                                          ...cellInput,
                                          background: m.cascadeToIntegrationId ? 'var(--iw-emerald-bg)' : 'var(--iw-bg)',
                                          color: m.cascadeToIntegrationId ? 'var(--iw-emerald-color)' : 'var(--iw-text)',
                                          fontWeight: m.cascadeToIntegrationId ? 600 : 400,
                                        }}
                                        title="ERP'de bu FK'nin işaret ettiği kayıt yoksa önce burada seçilen entegrasyon tetiklenir.">
                                        <option value="">— Cascade yok —</option>
                                        {cascadeTargets.length === 0 && (
                                          <option value="" disabled>(önce başka integration tanımlayın)</option>
                                        )}
                                        {cascadeTargets.map(t => (
                                          <option key={t.id} value={t.id}>{t.name}</option>
                                        ))}
                                      </select>
                                      {/* Kod bazlı toggle — sadece FormField (sourceType=0) ve cascade seçiliyse */}
                                      {m.sourceType === 0 && m.cascadeToIntegrationId && (
                                        <label style={{
                                          display: 'flex', alignItems: 'center', gap: 4,
                                          fontSize: 10, color: m.cascadeByValue ? 'var(--iw-emerald-color)' : 'var(--iw-muted)',
                                          cursor: 'pointer', userSelect: 'none', padding: '1px 2px',
                                        }}
                                        title="Alan değeri (kod) doğrudan cascade key olarak kullanılır. Hedef integrasyon 'Kod Kolonu' ayarıyla entity'yi bulur.">
                                          <input type="checkbox"
                                                 checked={!!m.cascadeByValue}
                                                 onChange={e => updateRow(idx, { cascadeByValue: e.target.checked })}
                                                 style={{ margin: 0 }} />
                                          Değer bazlı
                                        </label>
                                      )}
                                    </div>
                                  ) : (
                                    <span style={{
                                      fontSize: 10, color: 'var(--iw-muted)', fontStyle: 'italic',
                                      padding: '4px 6px',
                                    }}>(FK alanı değil)</span>
                                  )}
                                </td>
                                {/* Varsayılan — kaynak null/bos donerse kullanilacak fallback deger.
                                    Az kullanılan ikincil alan: kutu kroması sakin durumda geri
                                    çekilir (iw-cell-input--adv), değer her zaman tam kontrastta okunur. */}
                                <td style={{ ...cellBody, ...advDivider }}>
                                  <input value={m.defaultValue || ''}
                                         placeholder="(boş = boş gönder)"
                                         onChange={e => updateRow(idx, { defaultValue: e.target.value || null })}
                                         className="iw-cell-input iw-cell-input--adv"
                                         title="Kaynak alan boş/null dönerse bu değer gönderilir. Boş bırakılırsa hedef'e null/boş gider." />
                                </td>
                                {/* Format — tipe gore dinamik placeholder + tooltip (ikincil alan) */}
                                <td style={cellBody}>
                                  <input value={m.formatPattern || ''}
                                         placeholder={formatPlaceholder(m.targetDataType)}
                                         onChange={e => updateRow(idx, { formatPattern: e.target.value || null })}
                                         className="iw-cell-input iw-cell-input--adv"
                                         title={formatTooltip(m.targetDataType)} />
                                </td>
                                {/* Zorunlu — ikincil (küçük) switch */}
                                <td style={{ ...cellBody, textAlign: 'center' }}>
                                  <button className={'iw-switch iw-switch--sm' + (m.isRequired ? ' is-on' : '')}
                                          onClick={() => updateRow(idx, { isRequired: !m.isRequired })}
                                          title="Zorunlu alan — null/boş ise validation hatası">
                                    <span className="iw-switch__thumb" />
                                  </button>
                                </td>
                                {/* Sil — daha buyuk hit area */}
                                <td style={{ ...cellBody, textAlign: 'center' }}>
                                  <button onClick={() => removeRow(idx)}
                                          title="Mapping satırını sil"
                                          style={{
                                            background: 'transparent', border: '1px solid transparent',
                                            cursor: 'pointer', padding: '4px 6px', borderRadius: 5,
                                            color: 'var(--iw-muted)',
                                            display: 'inline-flex', alignItems: 'center', justifyContent: 'center',
                                          }}
                                          onMouseEnter={e => {
                                            e.currentTarget.style.color = 'var(--iw-rose-color)'
                                            e.currentTarget.style.background = 'var(--iw-rose-bg)'
                                            e.currentTarget.style.borderColor = 'var(--iw-rose-color)'
                                          }}
                                          onMouseLeave={e => {
                                            e.currentTarget.style.color = 'var(--iw-muted)'
                                            e.currentTarget.style.background = 'transparent'
                                            e.currentTarget.style.borderColor = 'transparent'
                                          }}>
                                    <Trash2 size={14} />
                                  </button>
                                </td>
                              </tr>
                            )
                          })}
                        </tbody>
                      </table>
                    </div>
                  )}
                </div>
              )}
            </div>
          )
        })}
      </div>
    </>
  )
}

// ────────────────────────────────────────────────────────────────────────────
// AllowedValuesBadge — Hedef Path cell'inin basina kucuk 'i' badge.
// Sadece allowedValues olan hedef alanlar icin (enum tipi). Hover'da tum
// degerlerin Turkce karsiligini liste halinde gosterir.
// ────────────────────────────────────────────────────────────────────────────
function AllowedValuesBadge({ doc }) {
  const [open, setOpen] = useState(false)
  const [pinned, setPinned] = useState(false)
  const wrapRef = useRef(null)
  useEffect(() => {
    if (!pinned) return
    function onDoc(e) {
      if (wrapRef.current && !wrapRef.current.contains(e.target)) {
        setPinned(false); setOpen(false)
      }
    }
    document.addEventListener('mousedown', onDoc)
    return () => document.removeEventListener('mousedown', onDoc)
  }, [pinned])
  const show = open || pinned
  const hasAllowed = Array.isArray(doc.allowedValues) && doc.allowedValues.length > 0
  const titleText = hasAllowed
    ? `${doc.allowedValues.length} olası değer — tıkla / üzerine gel`
    : 'Alan açıklaması'
  return (
    <span ref={wrapRef}
          style={{ position: 'relative', display: 'inline-flex', alignItems: 'center' }}
          onMouseEnter={() => setOpen(true)}
          onMouseLeave={() => !pinned && setOpen(false)}>
      <span onClick={(e) => { e.stopPropagation(); setPinned(p => !p); setOpen(true) }}
            style={{
              display: 'inline-flex', alignItems: 'center', justifyContent: 'center',
              width: 18, height: 18, borderRadius: '50%',
              background: 'var(--iw-indigo-color)', color: '#fff',
              fontSize: 12, fontWeight: 800, cursor: 'pointer',
              fontFamily: 'Georgia, "Times New Roman", serif', flexShrink: 0,
              border: '1.5px solid #fff',
              boxShadow: pinned
                ? '0 0 0 2px rgba(99,102,241,0.45), 0 2px 6px rgba(0,0,0,0.3)'
                : '0 1px 3px rgba(0,0,0,0.3)',
              lineHeight: 1,
            }}
            title={titleText}>
        i
      </span>
      {show && (
        <div style={{
          position: 'absolute', top: '100%', left: 0, marginTop: 4, zIndex: 50,
          minWidth: 220, maxWidth: 360, padding: '10px 12px', borderRadius: 8,
          background: 'var(--iw-surface)',
          border: '1px solid var(--iw-indigo-color)',
          boxShadow: '0 8px 24px rgba(0,0,0,0.35)',
          fontFamily: 'system-ui, -apple-system, sans-serif',
          fontSize: 12, color: 'var(--iw-text)', lineHeight: 1.5,
          whiteSpace: 'normal',
        }}>
          {doc.description && (
            <div style={{ marginBottom: 6 }}>{doc.description}</div>
          )}
          {hasAllowed && (
            <div style={{ marginTop: 8, borderTop: '1px dashed var(--iw-border)', paddingTop: 6 }}>
              <div style={{ color: 'var(--iw-muted)', fontSize: 11, marginBottom: 4 }}>
                İzin verilen değerler ({doc.allowedValues.length}):
              </div>
              <table style={{ borderCollapse: 'collapse', width: '100%', fontSize: 11 }}>
                <tbody>
                  {doc.allowedValues.map(av => (
                    <tr key={av.value}>
                      <td style={{
                        padding: '2px 8px 2px 0', color: 'var(--iw-indigo-color)',
                        fontFamily: 'ui-monospace, Menlo, Consolas, monospace',
                        whiteSpace: 'nowrap', verticalAlign: 'top',
                      }}>{av.value}</td>
                      <td style={{ padding: '2px 0', color: 'var(--iw-text)' }}>{av.label}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
          {doc.example && (
            <div style={{ marginTop: 8, borderTop: '1px dashed var(--iw-border)', paddingTop: 6, fontSize: 11 }}>
              <span style={{ color: 'var(--iw-muted)' }}>Örnek: </span>
              <code style={{ fontFamily: 'ui-monospace, Menlo, Consolas, monospace', color: 'var(--iw-emerald-color)' }}>{doc.example}</code>
            </div>
          )}
          {doc.notes && (
            <div style={{ marginTop: 6, fontSize: 11, color: 'var(--iw-muted)', fontStyle: 'italic' }}>
              {doc.notes}
            </div>
          )}
        </div>
      )}
    </span>
  )
}

// ────────────────────────────────────────────────────────────────────────────
// SamplePreviewCell — "Olasi Degerler" kolonu. Oncelik sirasi:
//   1. fieldDoc.allowedValues varsa → "0=Verilen, 1=Alinan, ..." kompakt liste
//      (hover'da tam tablo tooltip)
//   2. fieldDoc.example varsa → ornek deger
//   3. Aksi halde mapping'in mevcut sample/sabit degerine fallback
// ────────────────────────────────────────────────────────────────────────────
function SamplePreviewCell({ mapping, sample, linesSample, groupKind, fieldDoc }) {
  if (!mapping) return null

  // ── 1) Allowed values (en degerli bilgi — kullaniciya "ne secebilirim" gosterir)
  if (fieldDoc?.allowedValues && fieldDoc.allowedValues.length > 0) {
    const summary = fieldDoc.allowedValues.slice(0, 3)
      .map(av => av.value).join(', ')
    const more = fieldDoc.allowedValues.length > 3 ? ` +${fieldDoc.allowedValues.length - 3}` : ''
    const tooltip = fieldDoc.allowedValues
      .map(av => `${av.value} = ${av.label}`)
      .join('\n')
    return (
      <span title={tooltip} style={{
        display: 'inline-flex', alignItems: 'center', gap: 4,
        padding: '2px 8px', borderRadius: 999,
        background: 'var(--iw-indigo-bg)', color: 'var(--iw-indigo-color)',
        fontSize: 10, fontWeight: 600, cursor: 'help',
        fontFamily: 'ui-monospace, Menlo, Consolas, monospace',
        maxWidth: '100%', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap',
      }}>
        {fieldDoc.allowedValues.length} seçenek · {summary}{more}
      </span>
    )
  }

  // ── 2) Ornek deger (catalog'tan)
  if (fieldDoc?.example) {
    return (
      <span title={fieldDoc.notes || 'Önerilen örnek değer'} style={{
        padding: '2px 6px', borderRadius: 4,
        background: 'var(--iw-bg)', color: 'var(--iw-muted)',
        fontSize: 10, fontFamily: 'ui-monospace, Menlo, Consolas, monospace', cursor: 'help',
        maxWidth: '100%', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap',
        display: 'inline-block',
      }}>
        örn. {String(fieldDoc.example).replace(/^["']|["']$/g, '')}
      </span>
    )
  }

  // ── 3) Fallback: mapping'in mevcut kayittaki degeri
  let value = null
  let mode  = 'empty'

  if (mapping.sourceType === 0 && mapping.sourceValue) {
    const code = mapping.sourceValue
    if ((mapping.sourceSection === 'Lines' || groupKind === 'array') && Array.isArray(linesSample) && linesSample.length > 0) {
      value = linesSample[0]?.[code]
      mode = value != null ? 'live' : 'empty'
    } else if (sample?.fieldValues) {
      value = sample.fieldValues[code]
      mode = value != null ? 'live' : 'empty'
    }
  } else if (mapping.sourceType === 1) {
    value = mapping.sourceValue
    mode  = (value != null && value !== '') ? 'static' : 'empty'
  } else if ([2, 3, 4].includes(mapping.sourceType)) {
    mode = 'runtime'
  }

  if (mode === 'empty') return <span style={{ color: 'var(--iw-muted)', fontSize: 10, fontStyle: 'italic' }}>—</span>
  if (mode === 'runtime') return <span style={{
    color: 'var(--iw-muted)', fontSize: 10, fontStyle: 'italic',
    fontFamily: 'ui-monospace, Menlo, Consolas, monospace',
  }}>(runtime)</span>

  let displayValue = typeof value === 'object'
    ? (() => { try { return JSON.stringify(value) } catch { return String(value) } })()
    : String(value ?? '')
  if (displayValue.length > 40) displayValue = displayValue.slice(0, 37) + '…'

  return (
    <span title={String(value ?? '')} style={{
      display: 'inline-block', maxWidth: '100%', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap',
      padding: '2px 6px', borderRadius: 4,
      background: mode === 'live' ? 'var(--iw-emerald-bg)' : 'var(--iw-bg)',
      color: mode === 'live' ? 'var(--iw-emerald-color)' : 'var(--iw-text)',
      fontSize: 10, fontFamily: 'ui-monospace, Menlo, Consolas, monospace',
    }}>
      {displayValue || '∅'}
    </span>
  )
}
