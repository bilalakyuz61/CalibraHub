/**
 * WidgetBuilderForm — Sol kolon (yeni / edit widget formu)
 *
 * Kompakt yatay layout: label solda (sabit genislik), input sagda.
 * Yer tasarrufu icin etiket ve deger yan yana.
 *
 * Duplicate check: fieldLabel veya fieldKey ayni olan baska bir widget
 * varsa kaydetmeye izin verilmez (case-insensitive).
 */
import { useState, useEffect, useRef } from 'react'
import { motion } from 'framer-motion'
import { Plus, Check, Sparkles, Pencil, List, Wrench, CheckCircle2, CircleDashed, Settings } from 'lucide-react'
import DataTypeDropdown from './DataTypeDropdown'
import GroupSelector from './GroupSelector'
import OptionsModal from './OptionsModal'
import RuleBuilderModal from './RuleBuilderModal'
import GuideSettingsModal from './GuideSettingsModal'
import { buildWidgetExtraOptions } from '../../utils/fieldTokens'

/**
 * Row — label + input satiri.
 * ONEMLI: Row component'ini WidgetBuilderForm render fonksiyonunun
 * DISINDA tanimliyoruz. Iceride tanimlarsak her render'da yeni component
 * referansi olusur, React eski input'u unmount edip yenisini mount
 * eder ve type sirasinda focus kaybolur.
 */
/**
 * humanizeFieldKey — FldSet'te eslesmemis bir kolon icin kullanici-dostu
 * Turkce/Insan-okunabilir baslik uretir. CamelCase ve snake_case'i once
 * kelimelere boler, sonra mini sozlukten Turkce karsiligi varsa kullanir;
 * yoksa baslik harf bicimini uygular ('Stock Code', 'Tax Rate' gibi).
 *
 * Tek yonlu kayipsiz donusum degil — admin Sabit Alan Eslestirme sayfasindan
 * tam Turkce label kaydedince labelMap'ten gelir; humanize yalniz fallback.
 */
var TR_DICT = {
  // Ortak kavramlar
  id: 'ID', code: 'Kod', name: 'Ad', label: 'Etiket', description: 'Açıklama',
  type: 'Tip', kind: 'Tür', status: 'Durum', state: 'Durum',
  date: 'Tarih', time: 'Saat',
  rate: 'Oran', tax: 'Vergi', price: 'Fiyat', amount: 'Tutar', cost: 'Maliyet',
  unit: 'Birim', currency: 'Para Birimi', qty: 'Miktar', quantity: 'Miktar',
  // Mas tablolar
  customer: 'Müşteri', supplier: 'Tedarikçi', contact: 'Cari Hesap',
  account: 'Hesap', user: 'Kullanıcı', company: 'Şirket',
  item: 'Malzeme', material: 'Malzeme', stock: 'Stok', product: 'Ürün',
  group: 'Grup', category: 'Kategori', location: 'Lokasyon',
  // Gorev/akis
  order: 'Sipariş', invoice: 'Fatura', quote: 'Teklif', dispatch: 'İrsaliye',
  document: 'Belge', work: 'İş', operation: 'Operasyon', routing: 'Rota',
  machine: 'Makine', personnel: 'Personel', combination: 'Kombinasyon',
  // Bayraklar / sistem
  active: 'Aktif', deleted: 'Silindi',
  create: 'Oluşturma', modify: 'Değişiklik', updated: 'Güncelleme',
  // Pozisyon/iliski
  preferred: 'Tercih Edilen', default: 'Varsayılan',
  parent: 'Üst', child: 'Alt', source: 'Kaynak', target: 'Hedef',
}

function humanizeFieldKey(raw) {
  if (!raw) return ''
  var s = String(raw)
  // snake_case → bosluk
  s = s.replace(/_+/g, ' ')
  // camelCase → bosluk: aB → a B (abbr koruma: TaxRate → Tax Rate, IDNumber → ID Number)
  s = s.replace(/([a-z0-9])([A-Z])/g, '$1 $2')
  s = s.replace(/([A-Z]+)([A-Z][a-z])/g, '$1 $2')
  // Tekil bosluklar
  s = s.replace(/\s+/g, ' ').trim()
  if (!s) return raw
  // 'Id' suffix'i (... Id) Turkce'de gerekirse atilir veya 'ID' yapilir.
  // Mini sozlukten kelime bazli ceviri; kelime sozlukte yoksa baslik harf.
  var words = s.split(' ')
  var translated = words.map(function(w) {
    var key = w.toLowerCase()
    if (TR_DICT[key]) return TR_DICT[key]
    if (key === 'id') return 'ID'
    // baslik bicimi: ilk harf buyuk
    return w.charAt(0).toUpperCase() + w.slice(1).toLowerCase()
  })
  // 'X ID' kuyruklarini tek 'X' olarak birak (CompanyId → Şirket ID → Şirket)
  // Sadece son kelime ID ise ve bir onceki kelimeyle birlestiginde 1+ kelime kaliyorsa
  // ID'yi at — 'Customer ID' → 'Müşteri'.
  if (translated.length > 1 && translated[translated.length - 1] === 'ID') {
    translated.pop()
  }
  return translated.join(' ')
}

function Row(props) {
  return (
    <div className="flex items-center gap-2.5 min-h-[36px]">
      <label className="w-[140px] flex-shrink-0 text-[10px] font-semibold uppercase tracking-wider text-slate-500 dark:text-white/40 leading-tight">
        {props.label}
        {props.hint && (
          <span className="block text-slate-400 dark:text-white/45 font-normal normal-case text-[9px] mt-0.5">
            {props.hint}
          </span>
        )}
      </label>
      <div className="flex-1 min-w-0">{props.children}</div>
    </div>
  )
}

// Sayisal bicim presetleri — tek dropdown secimi. Ayraclar (ondalik/binlik)
// tarayicinin bolge/dil ayarindan Intl.NumberFormat ile turetilir, preset
// sadece stil (decimal/percent/currency) + ondalik hane sayisi belirler.
var NUMBER_FORMAT_PRESETS = [
  { code: 'int',  style: 'decimal',  places: 0, sample: 1000        },
  { code: 'dec1', style: 'decimal',  places: 1, sample: 1000.1      },
  { code: 'dec2', style: 'decimal',  places: 2, sample: 1000.12     },
  { code: 'dec3', style: 'decimal',  places: 3, sample: 1000.123    },
  { code: 'dec4', style: 'decimal',  places: 4, sample: 1000.1234   },
  { code: 'pct0', style: 'percent',  places: 0, sample: 0.12        },
  { code: 'pct1', style: 'percent',  places: 1, sample: 0.123       },
  { code: 'pct2', style: 'percent',  places: 2, sample: 0.1234      },
  { code: 'cur',  style: 'currency', places: 2, sample: 1000, currency: 'TRY' },
]

function formatNumberSample(preset) {
  try {
    var opts = {
      style: preset.style,
      minimumFractionDigits: preset.places,
      maximumFractionDigits: preset.places,
    }
    if (preset.currency) opts.currency = preset.currency
    var loc = (typeof navigator !== 'undefined' && navigator.language) || 'tr-TR'
    return new Intl.NumberFormat(loc, opts).format(preset.sample)
  } catch (e) {
    return String(preset.sample)
  }
}

// Saklanan metadata (eski veya yeni) → preset code eslestir.
function metaToPresetCode(meta) {
  if (!meta) return 'int'
  var style = String(meta.numericFormat || 'number')
  var places = parseInt(meta.decimalPlaces, 10)
  if (isNaN(places)) places = 0
  // Legacy: decimal2 / decimal4
  if (style === 'decimal2') { style = 'decimal'; places = 2 }
  else if (style === 'decimal4') { style = 'decimal'; places = 4 }
  if (style === 'currency') return 'cur'
  if (style === 'percent') {
    if (places <= 0) return 'pct0'
    if (places === 1) return 'pct1'
    return 'pct2'
  }
  // number / decimal
  if (places <= 0) return 'int'
  if (places === 1) return 'dec1'
  if (places === 2) return 'dec2'
  if (places === 3) return 'dec3'
  return 'dec4'
}

// Veri tipine + ek faktorlere gore tavsiye edilen 24-col genislik.
// Faktorler:
//   1) dataType   — bazi tipler kendiliginden dar/genis (date 6, numeric 6,
//                   text/lookup 12, multi-select 24, grid/group 24)
//   2) label      — uzun baslik input'u sikistirir, daha genis cell gerekir
//                   (ozellikle Standart modda label inline-solda max-content alir)
//   3) labelStyle — 'modern' floating (yatayda yer kaplamaz) / 'standard'
//                   inline (max-content) / 'inline' (Sade, 160px sabit;
//                   cell en az 12 col olmali ki input'a yer kalsin)
//   4) hasGuide   — rehberli alanlar display name (ornek "MANOLYA OLIVE OIL")
//                   gosterir, dar kalirsa truncate olur — 12 col taban gerekir
//
// Yeni widget olustururken useEffect ile otomatik uygulanir; slider altindaki
// hint admin'e gercek-zamanli onerilen degeri gosterir, "Uygula" linki tek
// tikla setColSpan(rec) yapar.
function recommendedColSpanFor(ctx) {
  var dt        = String(ctx.dataType || '').toLowerCase()
  var labelLen  = (ctx.label || '').trim().length
  var ls        = String(ctx.labelStyle || 'standard').toLowerCase()
  var hasGuide  = !!ctx.hasGuide

  // Her zaman tam satir gereken tipler — diger faktorler etkilemez
  if (dt === 'grid' || dt === 'group')   return 24
  if (dt === 'multi-select')             return 24

  // 1) Veri tipi tabani
  var base
  switch (dt) {
    case 'date':
    case 'datetime':
    case 'datetime-local':
    case 'time':
    case 'numeric':
    case 'boolean':
      base = 6   // 1/4 — kompakt input
      break
    case 'dropdown':
      base = 8   // 1/3 — secenek metni icin biraz nefes
      break
    case 'text':
    case 'link':
    case 'lookup':
    default:
      base = 12  // 1/2 — orta uzunluk
  }

  // 2) Rehber: display name (kod yerine isim) gosterilir → daha fazla yer
  if (hasGuide) base = Math.max(base, 12)

  // 3) Baslik stili x baslik uzunlugu — sikistirma riskini telafi
  if (ls === 'standard') {
    // Label inline-solda max-content; uzun label input'u dogrudan sikistirir
    if      (labelLen >= 25) base = Math.max(base, 24)
    else if (labelLen >= 16) base = Math.max(base, 18)
    else if (labelLen >= 10) base = Math.max(base, 12)
  } else if (ls === 'inline') {
    // Sade: 160px sabit label — cell en az 12 col olmali (yoksa input cok dar)
    base = Math.max(base, 12)
    if      (labelLen >= 22) base = Math.max(base, 18)
  } else if (ls === 'modern') {
    // Modern: floating label yatayda yer kaplamaz; sadece cok uzun label icin bump
    if      (labelLen >= 30) base = Math.max(base, 18)
    else if (labelLen >= 20) base = Math.max(base, 12)
  }

  return Math.min(24, Math.max(1, base))
}

// 24-col span degerini insan-okur fraksiyon etiketine cevir.
function colSpanFractionLabel(span) {
  if (span === 24) return 'Tam satır'
  if (span === 18) return '3/4'
  if (span === 16) return '2/3'
  if (span === 12) return '1/2'
  if (span ===  8) return '1/3'
  if (span ===  6) return '1/4'
  if (span ===  4) return '1/6'
  if (span ===  3) return '1/8'
  if (span ===  2) return '1/12'
  if (span ===  1) return '1/24'
  return span + '/24'
}

// Preset code → backend payload icin [numericFormat, decimalSep, thousandSep, decimalPlaces].
// Ayraclar tarayici locale'inden turetilir (render tarafi da locale'den okuyacak).
function presetToPayload(code) {
  var p = NUMBER_FORMAT_PRESETS.find(function(x) { return x.code === code }) || NUMBER_FORMAT_PRESETS[0]
  var style = p.style === 'currency' ? 'currency'
            : p.style === 'percent'  ? 'percent'
            : p.places > 0 ? 'decimal' : 'number'
  // Locale'den ayraclari cikar (backend arka uyumluluk icin her iki ayraci da bekliyor).
  var decSep = ','
  var thouSep = '.'
  try {
    var loc = (typeof navigator !== 'undefined' && navigator.language) || 'tr-TR'
    var parts = new Intl.NumberFormat(loc, { minimumFractionDigits: 1 }).formatToParts(1234.5)
    var dp = parts.find(function(x) { return x.type === 'decimal' })
    var gp = parts.find(function(x) { return x.type === 'group' })
    if (dp) decSep = dp.value
    if (gp) thouSep = gp.value
  } catch (e) {}
  return [style, decSep, thouSep, String(p.places)]
}

export default function WidgetBuilderForm(props) {
  var editingField = props.editingField
  var onSubmit = props.onSubmit
  var onCancel = props.onCancel
  var saving = props.saving
  var groups = Array.isArray(props.groups) ? props.groups : []
  var existingFields = Array.isArray(props.existingFields) ? props.existingFields : []
  // Form'un sabit alanlari — INFORMATION_SCHEMA tablo kolonlari (stok kodu,
  // birim, fiyat vb.). Tip 2 rehber tanimlarken admin "Form Alani Ekle"
  // listesinde bunlari da gormelidir; cunku rehberin SQL kisitinda form'un
  // ana alanlarini parametre olarak kullanmak isteyebilir.
  var formStaticFields = Array.isArray(props.formStaticFields) ? props.formStaticFields : []
  // Ust form (parent) widget'lari — rule/formul'de secilebilsin diye.
  // Ornek: SALES_QUOTE_LINES tanimi yaparken SALES_QUOTE_EDIT alanlari burada gelir.
  var parentFormWidgets = Array.isArray(props.parentFormWidgets) ? props.parentFormWidgets : []
  var activeLayer = props.activeLayer || null
  var activeLayerLabel = props.activeLayerLabel || null

  // Internal state isimleri UI ile uyumlu kalmaya devam ediyor, ama
  // submit payload'u ve editingField hydration'i artik Faz B API alanlarini
  // kullaniyor (label, widgetCode, parentId, dataType: lowercase, options: string[]).
  var [fieldLabel, setFieldLabel]       = useState('')       // → payload.label
  var [fieldKey, setFieldKey]           = useState('')       // → payload.widgetCode
  var [dataType, setDataType]           = useState('text')   // lowercase Faz A key
  var [maxLength, setMaxLength]         = useState('')       // → payload.maxLength (int|null, sadece text)
  var [minLength, setMinLength]         = useState('')       // → payload.minLength (int|null, sadece text)
  var [expectedLength, setExpectedLength] = useState('')    // → payload.expectedLength (int|null, sadece text)
  var [minValue, setMinValue]           = useState('')       // → payload.minValue (decimal|null, sadece numeric)
  var [maxValue, setMaxValue]           = useState('')       // → payload.maxValue (decimal|null, sadece numeric)
  // Sayisal bicim: tek bir preset secilir. Ayraclar (ondalik/binlik) tarayicinin
  // bolge/dil ayarindan (Intl.NumberFormat) runtime'da turetilir.
  var [numberFormatPreset, setNumberFormatPreset] = useState('int')
  var [textGuideCode, setTextGuideCode] = useState('')       // string tipi icin opsiyonel rehber kodu
  var [textConstraints, setTextConstraints] = useState('')   // rehber kisit JSON dizisi
  var [permissionKey, setPermissionKey] = useState('')       // backend'de saklanmayan UI-only alan
  var [groupId, setGroupId]             = useState(null)     // → payload.parentId (int|null)
  var [sortOrder, setSortOrder]         = useState('')       // → payload.sortOrder (int)
  // Form uzerinde kaplayacagi genislik — 24 kolonlu grid (1-24).
  // Daha hassas ayar: 6=1/4, 8=1/3, 12=1/2 (varsayilan), 16=2/3, 18=3/4, 24=tam.
  var [colSpan, setColSpan]             = useState(12)
  // Drag-resize: container ref + pointer event handler'lari. Pointer Events
  // mouse + touch (tablet) + pen tek API ile yakalar; touchAction:none ile
  // tablet'te dikey scroll iptal edilir.
  var colSpanSliderRef = useRef(null)
  var [colSpanDragging, setColSpanDragging] = useState(false)
  // Drag boyunca slider'in en yakin scroll parent'ini ve <body>'yi kilitle —
  // tablet'te touch sürüklemesinde panel scroll'u veya rubber-band kayma
  // sorununu engeller. Drag bitince eski overflow degerleri geri yazilir.
  useEffect(function() {
    if (!colSpanDragging) return undefined
    var el = colSpanSliderRef.current
    var locked = []
    function lock(node) {
      if (!node) return
      locked.push({ node: node, overflow: node.style.overflow, touchAction: node.style.touchAction })
      node.style.overflow = 'hidden'
      node.style.touchAction = 'none'
    }
    var n = el ? el.parentElement : null
    while (n && n !== document.body) {
      var cs = window.getComputedStyle(n)
      if (cs.overflowY === 'auto' || cs.overflowY === 'scroll' || cs.overflow === 'auto' || cs.overflow === 'scroll') {
        lock(n)
      }
      n = n.parentElement
    }
    lock(document.body)
    return function() {
      locked.forEach(function(item) {
        item.node.style.overflow = item.overflow
        item.node.style.touchAction = item.touchAction
      })
    }
  }, [colSpanDragging])
  function colSpanFromClientX(clientX) {
    var el = colSpanSliderRef.current
    if (!el) return colSpan
    var rect = el.getBoundingClientRect()
    if (rect.width <= 0) return colSpan
    var x = clientX - rect.left
    if (x < 0) x = 0
    if (x > rect.width) x = rect.width
    var v = Math.ceil((x / rect.width) * 24)
    if (v < 1) v = 1
    if (v > 24) v = 24
    return v
  }
  function handleColSpanPointerDown(e) {
    if (e.button != null && e.button !== 0) return
    e.preventDefault()
    try { e.currentTarget.setPointerCapture(e.pointerId) } catch (_) {}
    setColSpanDragging(true)
    setColSpan(colSpanFromClientX(e.clientX))
  }
  function handleColSpanPointerMove(e) {
    if (!e.currentTarget.hasPointerCapture(e.pointerId)) return
    e.preventDefault()
    setColSpan(colSpanFromClientX(e.clientX))
  }
  function handleColSpanPointerEnd(e) {
    try {
      if (e.currentTarget.hasPointerCapture(e.pointerId)) {
        e.currentTarget.releasePointerCapture(e.pointerId)
      }
    } catch (_) {}
    setColSpanDragging(false)
  }
  // Etiket gorunum stili: 'standard' (label ustte) / 'modern' (floating) / 'inline'
  // (160px sol etiket + sag input — eski isPlainField davranisinin yerine).
  // Yeni widget varsayilani 'modern' (modern UI standardi).
  var [labelStyle, setLabelStyle]       = useState('modern')
  // Guide-list display scope — 'form' (sadece edit ekrani, akordion ACIK gelir),
  // 'card' (sadece liste sayfasi kart popup'i), 'both' (her ikisi). Default 'both'.
  // Backend metadata.displayScope olarak saklanir; runtime tarafi (DWR + SmartCard)
  // bu degeri okuyup widget'in ilgili context'te render edilip edilmeyecegini belirler.
  var [displayScope, setDisplayScope]   = useState('both')
  // Guide-list opsiyonel arama input'u — true ise tablo ustunde free-text search
  // input'u render olur, guideSearch'e ?search= olarak gider. Backend zaten
  // GuidesController.Search bu parametreyi destekler. Default kapali.
  var [searchEnabled, setSearchEnabled] = useState(false)
  var [isActive, setIsActive]           = useState(true)
  var [isRequired, setIsRequired]       = useState(false)
  var [errors, setErrors]               = useState({})
  // Shake animation — zorunlu alan bos biraktirildiginda Widget Ekle
  // tiklaninca kirmizi cerceveli titresim (metin yerine sadece gorsel).
  var [shakeTick, setShakeTick]         = useState(0)
  var formRef = useRef(null)
  // Options = string[] (sadece label — Faz A spec)
  // OptionsModal icinde duzenleniyor, bu state modal'dan donen veriyi tutar.
  var [options, setOptions]             = useState([])
  // Faz G — Rule Engine kural alanlari (opsiyonel string ifade)
  var [ruleVisibleIf, setRuleVisibleIf]   = useState('')
  var [ruleDisabledIf, setRuleDisabledIf] = useState('')
  var [ruleRequiredIf, setRuleRequiredIf] = useState('')
  var [ruleFormula, setRuleFormula]       = useState('')
  // Varsayilan deger — yeni kayit olusurken atanir (readonly degil)
  var [defaultValue,     setDefaultValue]     = useState('')
  var [defaultValueKind, setDefaultValueKind] = useState('static')  // 'static' | 'formula'
  // Semantik Renk Mimarisi: 0=Statik token, 1=Dinamik SQL (baska widget'in degeri)
  var [colorType, setColorType]   = useState(0)
  var [colorValue, setColorValue] = useState('')

  // Modal state'leri
  var [optionsModalOpen, setOptionsModalOpen] = useState(false)
  var [ruleModalOpen, setRuleModalOpen]       = useState(false)
  var [guideModalOpen, setGuideModalOpen]     = useState(false)
  // Rehber kullanim toggle (sadece text + lookup tipi icin anlamli) ve config.
  // config = { viewCode, columns: [{name,label,visible}], constraint }
  var [guideEnabled, setGuideEnabled] = useState(false)
  var [guideConfig,  setGuideConfig]  = useState(null)
  // PR 3: guideCatalog state kaldirildi — /api/guides katalogu artik kullanilmiyor.
  // Lookup tipinde view secimi GuideCustomizationModal icinde /api/guides/views uzerinden yapilir.

  useEffect(function() {
    if (editingField) {
      // Faz B API alanlari: label, widgetCode, dataType (lowercase), parentId,
      // options (string[]). Legacy alanlara fallback ile geriye donuk uyum.
      setFieldLabel(editingField.label || editingField.fieldLabel || '')
      setFieldKey(editingField.widgetCode || editingField.fieldKey || '')
      setDataType(editingField.dataType || 'text')
      setMaxLength(editingField.maxLength != null ? String(editingField.maxLength) : '')
      setMinLength(editingField.minLength != null ? String(editingField.minLength) : '')
      setExpectedLength(editingField.expectedLength != null ? String(editingField.expectedLength) : '')
      setMinValue(editingField.minValue != null ? String(editingField.minValue) : '')
      setMaxValue(editingField.maxValue != null ? String(editingField.maxValue) : '')
      setNumberFormatPreset(metaToPresetCode(editingField.metadata))
      // Text tipi rehber eslestirme
      setTextGuideCode((editingField.metadata && editingField.metadata.guideCode) || '')
      var cJson = (editingField.metadata && editingField.metadata.constraints)
      setTextConstraints(cJson ? (typeof cJson === 'string' ? cJson : JSON.stringify(cJson, null, 2)) : '')
      // Rehber kullanim toggle + config hydration. metadata.guideConfig veya
      // metadata.guideCode varsa enabled=true. Config JSON dizisinde columns ve
      // constraint yer alir.
      var hasGuide = !!(editingField.metadata && (editingField.metadata.guideCode || editingField.metadata.guideConfig))
      setGuideEnabled(hasGuide)
      if (hasGuide) {
        var gc = null
        try {
          var raw = editingField.metadata.guideConfig
          gc = raw ? (typeof raw === 'string' ? JSON.parse(raw) : raw) : null
        } catch (e) { gc = null }
        if (!gc && editingField.metadata.guideCode) {
          gc = { viewCode: editingField.metadata.guideCode, columns: [], constraint: '' }
        }
        setGuideConfig(gc)
      } else {
        setGuideConfig(null)
      }
      setPermissionKey(editingField.permissionKey || '')
      setGroupId(
        editingField.parentId !== undefined
          ? editingField.parentId
          : (editingField.groupId || null)
      )
      setSortOrder(editingField.sortOrder != null ? String(editingField.sortOrder) : '')
      // colSpan — editingField veya metadata'dan oku (1-24 arasi); yoksa 12 (1/2).
      var cs = editingField.colSpan
      if (cs == null && editingField.metadata) cs = editingField.metadata.colSpan
      var csNum = parseInt(cs, 10)
      setColSpan(!isNaN(csNum) && csNum >= 1 && csNum <= 24 ? csNum : 12)
      // labelStyle — 3 deger: 'standard' / 'modern' / 'inline'. Eski isPlainField=true
      // satirlari 'inline' olarak hydrate edilir (DB migrasyonu da yapar; bu sadece
      // henuz migrate olmamis ya da eski client'tan gelen DTO icin guvence).
      var rawLs = String(editingField.labelStyle || '').toLowerCase()
      var resolvedLs
      if (rawLs === 'modern') resolvedLs = 'modern'
      else if (rawLs === 'inline') resolvedLs = 'inline'
      else if (editingField.isPlainField === true) resolvedLs = 'inline'
      else resolvedLs = 'standard'
      setLabelStyle(resolvedLs)
      setIsActive(editingField.isActive !== false)
      setIsRequired(editingField.isRequired === true)
      // Lookup: guideCode metadata'da saklanir (options degil)
      // Grid: childFormCode metadata'da saklanir
      var existingOpts = []
      var dt = (editingField.dataType || '').toLowerCase()
      if ((dt === 'lookup' || dt === 'guide-list') && editingField.metadata && editingField.metadata.guideCode) {
        existingOpts = [editingField.metadata.guideCode]
      } else if (dt === 'grid' && editingField.metadata && editingField.metadata.childFormCode) {
        existingOpts = [editingField.metadata.childFormCode]
      } else if (Array.isArray(editingField.options)) {
        existingOpts = editingField.options.map(function(o) {
          return typeof o === 'string' ? o : (o.optionLabel || o.label || '')
        }).filter(function(s) { return s && s.length > 0 })
      }
      setOptions(existingOpts)
      // Faz G — Rules hydration
      var r = editingField.rules || {}
      setRuleVisibleIf(r.visibleIf || '')
      setRuleDisabledIf(r.disabledIf || '')
      setRuleRequiredIf(r.requiredIf || '')
      setRuleFormula(r.formula || '')
      // Display scope (guide-list) — metadata.displayScope'tan oku
      var dsRaw = (editingField.metadata && editingField.metadata.displayScope) || ''
      setDisplayScope((dsRaw === 'form' || dsRaw === 'card' || dsRaw === 'both') ? dsRaw : 'both')
      // Search enabled — guideConfig icinde saklanir (JSON object)
      var gcSearchRaw = null
      try {
        var gcRaw = editingField.metadata && editingField.metadata.guideConfig
        if (gcRaw) {
          var gcParsed = (typeof gcRaw === 'string') ? JSON.parse(gcRaw) : gcRaw
          gcSearchRaw = gcParsed && gcParsed.searchEnabled
        }
      } catch (_) {}
      setSearchEnabled(gcSearchRaw === true)
      // Varsayilan deger — top-level veya rules icinden oku (geriye uyum)
      var dvRaw = editingField.defaultValue != null
        ? editingField.defaultValue
        : (r.defaultValue != null ? r.defaultValue : '')
      var dvKind = editingField.defaultValueKind
        || r.defaultValueKind
        || 'static'
      setDefaultValue(String(dvRaw))
      setDefaultValueKind(dvKind)
      // Semantik Renk hydration
      setColorType(editingField.colorType != null ? editingField.colorType : 0)
      setColorValue(editingField.colorValue || '')
    } else {
      setFieldLabel('')
      setFieldKey('')
      setDataType('text')
      setMaxLength('')
      setMinLength('')
      setExpectedLength('')
      setMinValue('')
      setMaxValue('')
      setNumberFormatPreset('int')
      setPermissionKey('')
      setGroupId(null)
      setSortOrder('')
      setColSpan(12)
      setLabelStyle('modern')  // yeni widget varsayilani modern (floating label)
      setGuideEnabled(false)
      setGuideConfig(null)
      setIsActive(true)
      setIsRequired(false)
      setOptions([])
      setRuleVisibleIf('')
      setRuleDisabledIf('')
      setRuleFormula('')
      setDefaultValue('')
      setDefaultValueKind('static')
      setColorType(0)
      setColorValue('')
    }
    setErrors({})
  }, [editingField])

  // Dropdown/multi-select/link/lookup/grid disinda bir tipe donulurse options listesini temizle
  // Tip degisince kisitlama alanlarini da sifirla
  useEffect(function() {
    if (dataType !== 'dropdown' && dataType !== 'multi-select' && dataType !== 'link'
        && dataType !== 'lookup' && dataType !== 'grid') {
      setOptions([])
    }
    if (dataType !== 'text') {
      setMinLength('')
      setExpectedLength('')
      setTextGuideCode('')
      if (!editingField || (editingField && editingField.dataType !== dataType)) {
        setMaxLength('')
      }
    }
    // textConstraints HEM text+rehber HEM de lookup'da kullanilir.
    // Sadece bu iki tip disindaysa temizle.
    if (dataType !== 'text' && dataType !== 'lookup' && dataType !== 'guide-list') {
      setTextConstraints('')
    }
    // Rehber toggle/config text, lookup ve guide-list tiplerinde anlamli.
    if (dataType !== 'text' && dataType !== 'lookup' && dataType !== 'guide-list') {
      setGuideEnabled(false)
      setGuideConfig(null)
    }
    // guide-list saf rehber widget'i — rehber zorunlu, toggle otomatik acilir
    // ve kullanici tarafindan kapatilamaz (render'da disabled). Bu, "Ayarlar"
    // butonunun direkt gorunmesini saglar.
    if (dataType === 'guide-list') {
      setGuideEnabled(true)
    }
    if (dataType !== 'numeric') {
      setMinValue('')
      setMaxValue('')
      setNumberFormatPreset('int')
    }
  }, [dataType])

  // ── ColSpan auto-suggest ────────────────────────────────────────
  // Veri tipi VEYA baslik stili degistiginde, admin ColSpan'a manuel
  // dokunmadiysa yeni onerilen genisligi otomatik uygula. "Manuel dokundu"
  // tespiti: mevcut colSpan onceki onerinin tam ayni degeri ise admin
  // dokunmamis demektir.
  //
  // Auto-apply SADECE [dataType, labelStyle] degisikliklerinde tetiklenir;
  // baslik metni (fieldLabel) ve rehber toggle her keystroke'ta degistigi
  // icin auto-apply listesinde DEGIL — onlar yalnizca slider altindaki
  // hint'te yansir, admin "Uygula"ya tikladiginda uygulanir.
  //
  // Edit modunda (editingField var) prevAutoRec sadece sync edilir, asla
  // setColSpan cagrilmaz — admin'in onceki secimi korunsun.
  var prevAutoRecRef = useRef(null)
  useEffect(function() {
    // editingField transition: tracking sifirlanir ki yeni baglamla baslayalim
    prevAutoRecRef.current = null
  }, [editingField])
  useEffect(function() {
    var hasGuideNow = (
      dataType === 'lookup' ||
      dataType === 'guide-list' ||
      (dataType === 'text' && (guideEnabled || !!textGuideCode))
    )
    var newRec = recommendedColSpanFor({
      dataType:   dataType,
      label:      fieldLabel,
      labelStyle: labelStyle,
      hasGuide:   hasGuideNow,
    })

    if (editingField) {
      prevAutoRecRef.current = newRec
      return
    }
    // Ilk efektif calisma — kaydet ve cik
    if (prevAutoRecRef.current === null) {
      prevAutoRecRef.current = newRec
      return
    }
    // Oneri degistiyse VE admin manuel dokunmadiysa uygula
    if (newRec !== prevAutoRecRef.current && colSpan === prevAutoRecRef.current) {
      setColSpan(newRec)
    }
    prevAutoRecRef.current = newRec
  }, [dataType, labelStyle])

  // Beklenen uzunluk girildiginde min/maks uzunluk sifirlanir ve disable edilir.
  // Beklenen uzunluk tam eslesmeyi sart kostugundan min/maks sinirlari anlamsiz olur.
  useEffect(function() {
    if (expectedLength !== '' && !isNaN(parseInt(expectedLength, 10))) {
      setMinLength('')
      setMaxLength('')
    }
  }, [expectedLength])

  // Yeni widget olusturulurken fieldKey'i label'dan otomatik turet.
  // Edit modunda fieldKey degistirilmez (veri butunlugu).
  useEffect(function() {
    if (editingField) return  // edit modunda dokunma
    var key = fieldLabel
      .toLowerCase()
      .replace(/ı/g, 'i').replace(/ğ/g, 'g').replace(/ş/g, 's')
      .replace(/ö/g, 'o').replace(/ü/g, 'u').replace(/ç/g, 'c')
      .replace(/[^a-z0-9\s]/g, '')
      .trim()
      .replace(/\s+/g, '_')
    if (key && !/^[a-z]/.test(key)) key = 'f_' + key
    setFieldKey(key || '')
  }, [fieldLabel, editingField])

  // Shake tetikleyici — shakeTick her hata submit'inde artar, DOM'daki
  // data-shake-key atributu hata anahtariyla eslesen elementlere
  // .cbh-shake class'ini yeniden uygulayip CSS animasyonu restart eder.
  useEffect(function() {
    if (shakeTick === 0) return
    var root = formRef.current
    if (!root) return
    Object.keys(errors).forEach(function(key) {
      var el = root.querySelector('[data-shake-key="' + key + '"]')
      if (!el) return
      el.classList.remove('cbh-shake')
      // Reflow ile animasyonu zorla restart et
      /* eslint-disable-next-line no-unused-expressions */
      el.offsetWidth
      el.classList.add('cbh-shake')
    })
  }, [shakeTick])

  // PR 3: Lookup guide katalogu fetch'i kaldirildi — kullanilmiyordu (vestigial dead code).
  // Aktif rehber listesi GuideCustomizationModal icinde /api/guides/views ile fetch ediliyor.

  function validate() {
    var e = {}
    var lbl = fieldLabel.trim()
    var key = fieldKey.trim()

    if (!lbl) {
      e.fieldLabel = 'Başlık zorunlu'
    }
    if (!dataType) {
      e.dataType = 'Veri tipi seçin'
    }

    // Duplicate check — editingField'in kendisi haric
    // existingFields Faz B API'den gelir: her eleman { id, label, widgetCode, ... }
    var editingId = editingField ? editingField.id : null
    if (!e.fieldLabel && lbl) {
      var dupLabel = existingFields.find(function(f) {
        if (editingId && f.id === editingId) return false
        var existingLabel = f.label || f.fieldLabel || ''
        return existingLabel.trim().toLowerCase() === lbl.toLowerCase()
      })
      if (dupLabel) {
        e.fieldLabel = 'Bu başlık zaten kullanılıyor'
      }
    }

    // dropdown / multi-select options validation — Faz A string[] formati
    if (dataType === 'dropdown' || dataType === 'multi-select') {
      if (!options || options.length === 0) {
        e.options = 'En az bir seçenek ekleyin'
      }
    }

    // link validation — URL sablonu zorunlu + {value} yer tutucusu
    if (dataType === 'link') {
      var tpl = (options && options[0]) || ''
      if (!tpl) {
        e.options = 'Hedef URL sablonu zorunlu'
      } else if (String(tpl).indexOf('{value}') === -1) {
        e.options = "URL icinde '{value}' yer tutucusu bulunmali"
      }
    }

    // Rehber yapilandirma kontrolu.
    // Lookup / Guide-List: switch zorunlu ON; ayrica config tamamlanmali.
    // Text: switch opsiyonel; ama acildiysa config zorunlu.
    if ((dataType === 'lookup' || dataType === 'guide-list') && (!guideEnabled || !guideConfig || !guideConfig.viewCode)) {
      e.guideSettings = 'Rehber yapılandırın (Ayarlar)'
    } else if (dataType === 'text' && guideEnabled && (!guideConfig || !guideConfig.viewCode)) {
      e.guideSettings = 'Rehber yapılandırmasını tamamlayın'
    }

    // grid validation — childFormCode zorunlu
    if (dataType === 'grid') {
      var cfc = (options && options[0]) || ''
      if (!String(cfc).trim()) {
        e.options = 'Alt form secin'
      }
    }

    // text kisitlama tutarlilik kontrolu
    if (dataType === 'text') {
      var ml  = maxLength      !== '' ? parseInt(maxLength, 10)      : null
      var mnl = minLength      !== '' ? parseInt(minLength, 10)      : null
      var el2 = expectedLength !== '' ? parseInt(expectedLength, 10) : null
      if (mnl != null && ml != null && mnl > ml)
        e.minLength = 'Min. uzunluk, maks. uzunluktan büyük olamaz'
      if (el2 != null && ml != null && el2 > ml)
        e.expectedLength = 'Beklenen uzunluk, maks. uzunluktan büyük olamaz'
    }

    // numeric kisitlama tutarlilik kontrolu
    if (dataType === 'numeric') {
      var mnv = minValue !== '' ? parseFloat(minValue) : null
      var mxv = maxValue !== '' ? parseFloat(maxValue) : null
      if (mnv != null && mxv != null && mnv > mxv)
        e.minValue = 'Min. değer, maks. değerden büyük olamaz'
    }

    return e
  }

  // NOTE: addOption/updateOption/removeOption/slugifyLabel helperlar
  // OptionsModal'a tasindi. Bu form sadece modal'dan donen `options` state'ini
  // tutar ve submit payload'ina ekler.

  function handleSubmit(event) {
    event.preventDefault()
    var e = validate()
    if (Object.keys(e).length > 0) {
      setErrors(e)
      // Her basarisiz submit'te shakeTick artar → useEffect animasyonu tetikler.
      setShakeTick(function(t) { return t + 1 })
      return
    }
    if (onSubmit) {
      // Faz B payload — UpsertWidgetRequest sekli:
      //   { id?, formId, parentId?, widgetCode, label, dataType, maxLength?,
      //     sortOrder, options?, isActive }
      // formId + sortOrder parent (AdminWidgetRegistryPanel) tarafinda eklenir.
      var payloadOptions
      if (dataType === 'dropdown' || dataType === 'multi-select') {
        payloadOptions = options.map(function(o) {
          return typeof o === 'string' ? o : (o.label || '')
        }).filter(function(s) { return s && s.length > 0 })
      } else if (dataType === 'link') {
        // Link: tek elemanli dizi — URL sablonu
        var tpl = (options && options[0]) || ''
        payloadOptions = tpl ? [String(tpl)] : []
      } else if (dataType === 'lookup' || dataType === 'guide-list') {
        // Lookup / Guide-List: [viewCode, configJson?, displayScope?].
        // Toggle ON ve config varsa configJson ile birlikte; degilse tek elemanli viewCode.
        // Guide-list icin Options[2] = displayScope ('form'|'card'|'both').
        // Backend metadata olarak parse eder; runtime tarafi (DWR + SmartCard) okur.
        if (guideEnabled && guideConfig && guideConfig.viewCode) {
          // guide-list icin guideConfig'e searchEnabled flag'ini enrich et
          var enrichedConfig = (dataType === 'guide-list')
            ? Object.assign({}, guideConfig, { searchEnabled: !!searchEnabled })
            : guideConfig
          var cfgJson = JSON.stringify(enrichedConfig)
          if (dataType === 'guide-list') {
            payloadOptions = [String(guideConfig.viewCode), cfgJson, displayScope || 'both']
          } else {
            payloadOptions = [String(guideConfig.viewCode), cfgJson]
          }
        } else {
          payloadOptions = []
        }
      } else if (dataType === 'grid') {
        // Grid: tek elemanli dizi — childFormCode (backend object JSON'a cevirir)
        var cfc = (options && options[0]) || ''
        payloadOptions = cfc ? [String(cfc)] : []
      } else if (dataType === 'numeric') {
        // Preset → [numericFormat, decimalSep, thousandSep, decimalPlaces].
        // Ayraclar tarayici locale'inden turetilir; render tarafi da runtime'da
        // locale'den okuyacaktir (bolge dil ayari).
        payloadOptions = presetToPayload(numberFormatPreset)
      } else if (dataType === 'text' && guideEnabled && guideConfig && guideConfig.viewCode) {
        // Text + rehber: yeni guideConfig (kolonlar/etiketler/kisit) JSON olarak gonderilir.
        payloadOptions = [String(guideConfig.viewCode), JSON.stringify(guideConfig)]
      } else if (dataType === 'text' && textGuideCode.trim()) {
        // Text + rehber (legacy): guideCode + constraints — geriye uyum.
        payloadOptions = [textGuideCode.trim(), textConstraints.trim() || '']
      } else {
        payloadOptions = null
      }

      // Faz G — Rules payload. Hicbir slot dolu degilse null gonder (backend sakin).
      var viTrim = ruleVisibleIf.trim()
      var diTrim = ruleDisabledIf.trim()
      var riTrim = ruleRequiredIf.trim()
      var fmTrim = ruleFormula.trim()
      var dvTrim = String(defaultValue || '').trim()
      var rulesPayload = (viTrim || diTrim || riTrim || fmTrim || dvTrim)
        ? {
            visibleIf:  viTrim || null,
            disabledIf: diTrim || null,
            requiredIf: riTrim || null,
            formula:    fmTrim || null,
            // Varsayilan deger — backend'in rules JSON'una eklenir. Yeni kayitta
            // runtime tarafinda uygulanmasi beklenir (kullanici uzerine yazabilir).
            defaultValue:     dvTrim || null,
            defaultValueKind: dvTrim ? defaultValueKind : null,
          }
        : null

      onSubmit({
        id: editingField ? editingField.id : null,
        parentId: groupId,                     // null ise "Grupsuz"
        widgetCode: fieldKey.trim(),
        label: fieldLabel.trim(),
        dataType: dataType,                    // lowercase: text, numeric, ...
        maxLength: (dataType === 'text' && maxLength !== '' && !isNaN(parseInt(maxLength, 10)))
          ? Math.max(1, Math.min(8000, parseInt(maxLength, 10)))
          : null,
        minLength: (dataType === 'text' && minLength !== '' && !isNaN(parseInt(minLength, 10)))
          ? Math.max(1, Math.min(8000, parseInt(minLength, 10)))
          : null,
        expectedLength: (dataType === 'text' && expectedLength !== '' && !isNaN(parseInt(expectedLength, 10)))
          ? Math.max(1, Math.min(8000, parseInt(expectedLength, 10)))
          : null,
        minValue: (dataType === 'numeric' && minValue !== '' && !isNaN(parseFloat(minValue)))
          ? parseFloat(minValue)
          : null,
        maxValue: (dataType === 'numeric' && maxValue !== '' && !isNaN(parseFloat(maxValue)))
          ? parseFloat(maxValue)
          : null,
        options: payloadOptions,
        sortOrder: sortOrder !== '' && !isNaN(parseInt(sortOrder, 10)) ? parseInt(sortOrder, 10) : null,
        // Form uzerinde kaplayacagi genislik (24-col grid span) — runtime renderer
        // bu degeri CSS grid-column span'ine donusturur. Default 12 (1/2 satir).
        colSpan: (colSpan >= 1 && colSpan <= 24) ? colSpan : 12,
        // Etiket gorunum stili — 'standard' / 'modern' / 'inline'. Backend valide eder.
        labelStyle: (labelStyle === 'modern' || labelStyle === 'inline') ? labelStyle : 'standard',
        isActive: isActive,
        // isPlainField artik UI'da yok — labelStyle='inline' bayragi yerini aldi.
        // Backend yine de bu alani 'inline' iken true olarak yazip eski DB
        // okuyuculari ile senkron kalir; UI gondermez.
        isRequired: isRequired,
        rules: rulesPayload,
        colorType: colorType,
        colorValue: colorValue.trim() || null,
        // Varsayilan deger — ust seviye alan olarak da gonderilir (backend tercihen
        // buradan okuyabilir; rules icinde de yedegi var).
        defaultValue:     dvTrim || null,
        defaultValueKind: dvTrim ? defaultValueKind : null,
        // UI-only alanlar (backend ignore eder ama parent state'i icin iletiliyor)
        permissionKey: permissionKey.trim() || null,
      })
    }
  }

  var isEdit = editingField != null

  // Ortak CSS — input field (sabit yukseklik 36px tum alanlar icin).
  // type="number" inputlari icin webkit/firefox spinner butonlari gizlenir.
  var inputBase = 'w-full h-9 px-3 rounded-lg text-xs transition-all ' +
    'bg-white/60 dark:bg-white/[0.04] ' +
    'text-slate-800 dark:text-white/85 ' +
    'placeholder:text-slate-400 dark:placeholder:text-white/25 ' +
    'focus:outline-none ' +
    '[&>option]:bg-white [&>option]:text-slate-800 dark:[&>option]:bg-[#1e293b] dark:[&>option]:text-white/85 ' +
    '[appearance:textfield] ' +
    '[&::-webkit-outer-spin-button]:appearance-none ' +
    '[&::-webkit-inner-spin-button]:appearance-none [&::-webkit-inner-spin-button]:m-0 '
  var inputOk = 'border border-slate-200 dark:border-white/[0.08] focus:border-indigo-400/60 dark:focus:border-white/20 focus:shadow-[0_0_0_3px_rgba(99,102,241,0.12)]'
  var inputErr = 'border-2 border-red-500/80 focus:border-red-500 focus:shadow-[0_0_0_3px_rgba(239,68,68,0.22)]'

  return (
    <motion.form
      ref={formRef}
      layout
      onSubmit={handleSubmit}
      className="glass rounded-2xl p-4 flex flex-col gap-3 flex-shrink-0 h-fit shadow-[0_4px_24px_rgba(0,0,0,0.12)]"
    >
      {/* Header */}
      <div className="flex items-center gap-2.5 pb-2 border-b border-slate-200/40 dark:border-white/[0.06]">
        <div
          className="w-7 h-7 rounded-lg flex items-center justify-center"
          style={{
            background: isEdit ? 'rgba(245, 158, 11, 0.12)' : 'rgba(99, 102, 241, 0.12)',
            border: '1px solid ' + (isEdit ? 'rgba(245, 158, 11, 0.25)' : 'rgba(99, 102, 241, 0.25)'),
          }}
        >
          <Sparkles size={12} style={{ color: isEdit ? '#fbbf24' : '#818cf8' }} strokeWidth={2} />
        </div>
        <div className="flex-1 min-w-0">
          <h3 className="text-xs font-bold text-slate-800 dark:text-white/90 leading-tight">
            {isEdit ? 'Widget Düzenle' : 'Yeni Widget Tanımla'}
          </h3>
          <p className="text-[10px] text-slate-500 dark:text-white/30 truncate">
            {isEdit
              ? (editingField.widgetCode || editingField.fieldKey || '')
              : (activeLayerLabel
                  ? 'Katman: ' + activeLayerLabel
                  : 'Form alanına eklenecek alan')}
          </p>
        </div>
      </div>

      {/* Başlık */}
      <Row label="Başlık">
        <input
          type="text"
          value={fieldLabel}
          onChange={function(e) { setFieldLabel(e.target.value) }}
          placeholder="Son Kullanma Tarihi"
          data-shake-key="fieldLabel"
          className={inputBase + (errors.fieldLabel ? inputErr : inputOk)}
        />
      </Row>

      {/* Başlık Stili — 3 segmentli secici: Standart / Modern / Sade.
          Her butonun ustunde kucuk bir mini-mockup gosteriyor — admin hangi
          stilin label'i nereye koyacagini gorsel olarak ayirt edebilsin. Mini
          stage'ler 36x18px solid yuzey uzerinde rendere edilir (modern label
          "cut" efekti icin solid bg gerekli).
          Sade ('inline') eski 'Sade alan' (isPlainField) toggle'inin yerine geldi.
          Guide-list akordion bir kart oldugu icin label-stili anlamsiz — gizli. */}
      {dataType !== 'group' && dataType !== 'guide-list' && (
        <Row label="Başlık Stili" hint="görünüm">
          <div className="flex items-center gap-1 p-[2px] rounded-lg bg-slate-100 dark:bg-white/[0.04] border border-slate-200 dark:border-white/[0.06]">
            {[
              { v: 'standard', label: 'Standart', hint: 'Etiket yandan, input bitisik (kompakt)' },
              { v: 'modern',   label: 'Modern',   hint: 'Etiket cizgi uzerinde (floating outlined)' },
              { v: 'inline',   label: 'Sade',     hint: '160px sabit etiket + esnek input' },
            ].map(function(opt) {
              var act = labelStyle === opt.v

              // Stil basina kucuk on-izleme — buton metninin ustunde gosterilir.
              // 36x18px solid yuzey (modern label "cut" efekti icin gerekli).
              // Etiket "Aa" ile temsil edilir, input ince yatay cubukla.
              var stageClass = 'rounded bg-slate-50 dark:bg-slate-800 border border-slate-200 dark:border-slate-700/60'
              var stageStyle = { width: 36, height: 18, padding: 3, boxSizing: 'border-box' }
              var inputBoxClass = 'rounded-sm border border-slate-400 dark:border-slate-500'

              var preview = null
              if (opt.v === 'standard') {
                // Etiket inline solda, input bitisik (max-content + 1fr)
                preview = (
                  <div className={stageClass + ' flex items-center gap-1'} style={stageStyle}>
                    <span className="text-[7px] font-bold leading-none flex-shrink-0">Aa</span>
                    <span className={inputBoxClass} style={{ height: 5, flex: 1 }} />
                  </div>
                )
              } else if (opt.v === 'modern') {
                // Etiket cizgi uzerinde — cercevenin top-line'ini "kesiyor"
                preview = (
                  <div className={stageClass + ' relative'} style={stageStyle}>
                    <span
                      className={inputBoxClass + ' absolute'}
                      style={{ top: 6, left: 3, right: 3, bottom: 3 }}
                    />
                    <span
                      className="absolute font-bold leading-none bg-slate-50 dark:bg-slate-800"
                      style={{ top: 2, left: 5, fontSize: 6, lineHeight: 1, padding: '0 1px' }}
                    >
                      Aa
                    </span>
                  </div>
                )
              } else {
                // inline (Sade) — etiket sabit solda, input saga itilmis
                preview = (
                  <div
                    className={stageClass + ' flex items-center'}
                    style={Object.assign({}, stageStyle, { justifyContent: 'space-between' })}
                  >
                    <span className="text-[7px] font-bold leading-none flex-shrink-0">Aa</span>
                    <span className={inputBoxClass} style={{ width: 14, height: 5, flexShrink: 0 }} />
                  </div>
                )
              }

              return (
                <button
                  key={opt.v}
                  type="button"
                  onClick={function() { setLabelStyle(opt.v) }}
                  title={opt.hint}
                  className={
                    'flex-1 flex flex-col items-center gap-1 py-1.5 px-2 rounded-md text-[10px] font-semibold transition-all ' +
                    (act
                      ? 'bg-white dark:bg-white/[0.08] shadow-sm text-indigo-600 dark:text-indigo-300'
                      : 'text-slate-500 dark:text-white/45 hover:text-slate-700 dark:hover:text-white/70')
                  }
                >
                  {preview}
                  <span>{opt.label}</span>
                </button>
              )
            })}
          </div>
        </Row>
      )}

      {/* Veri Tipi — Edit modunda DAIMA kilitli.
          Sebep: değerler NVARCHAR olarak saklanır, tip değişince parse
          semantiği bozulur (örn. "abc" → Integer parse hatası). */}
      <Row label="Veri Tipi">
        <div data-shake-key="dataType" className="rounded-lg">
          <DataTypeDropdown value={dataType} onChange={setDataType} disabled={isEdit} />
        </div>
      </Row>

      {/* Maks. Uzunluk — sadece text tipi. Beklenen uzunluk doluysa disabled.
          Uzunluk negatif olamaz: '-' karakteri onChange'de atilir. */}
      {dataType === 'text' && (
        <Row label="Maks. Uzunluk" hint="karakter (opsiyonel)">
          <input
            type="number"
            min="1"
            max="8000"
            value={maxLength}
            disabled={expectedLength !== ''}
            onChange={function(e) { setMaxLength(e.target.value.replace(/-/g, '')) }}
            onKeyDown={function(e) { if (e.key === '-' || e.key === 'e' || e.key === 'E') e.preventDefault() }}
            placeholder="örn. 255"
            className={inputBase + inputOk + (expectedLength !== '' ? ' opacity-50 cursor-not-allowed' : '')}
          />
        </Row>
      )}

      {/* Min. Uzunluk — sadece text tipi. Beklenen uzunluk doluysa disabled.
          Uzunluk negatif olamaz. */}
      {dataType === 'text' && (
        <Row label="Min. Uzunluk" hint="karakter (opsiyonel)">
          <input
            type="number"
            min="1"
            max="8000"
            value={minLength}
            disabled={expectedLength !== ''}
            onChange={function(e) { setMinLength(e.target.value.replace(/-/g, '')) }}
            onKeyDown={function(e) { if (e.key === '-' || e.key === 'e' || e.key === 'E') e.preventDefault() }}
            placeholder="örn. 5"
            data-shake-key="minLength"
            className={inputBase + (errors.minLength ? inputErr : inputOk) + (expectedLength !== '' ? ' opacity-50 cursor-not-allowed' : '')}
          />
        </Row>
      )}

      {/* Beklenen Uzunluk — sadece text tipi. Uzunluk negatif olamaz. */}
      {dataType === 'text' && (
        <Row label="Beklenen Uzunluk" hint="tam eşleşme">
          <input
            type="number"
            min="1"
            max="8000"
            value={expectedLength}
            onChange={function(e) { setExpectedLength(e.target.value.replace(/-/g, '')) }}
            onKeyDown={function(e) { if (e.key === '-' || e.key === 'e' || e.key === 'E') e.preventDefault() }}
            placeholder="örn. 10 (VKN)"
            data-shake-key="expectedLength"
            className={inputBase + (errors.expectedLength ? inputErr : inputOk)}
          />
        </Row>
      )}

      {/* Eski text+Rehber inline UI kaldirildi — yeni switch+ayarlar
          modali (yukarida) hem text hem lookup icin kullaniliyor. */}

      {/* Min. Değer — sadece numeric tipi */}
      {dataType === 'numeric' && (
        <Row label="Min. Değer" hint="opsiyonel">
          <input
            type="number"
            step="any"
            value={minValue}
            onChange={function(e) { setMinValue(e.target.value) }}
            placeholder="örn. 0"
            data-shake-key="minValue"
            className={inputBase + (errors.minValue ? inputErr : inputOk)}
          />
        </Row>
      )}

      {/* Max. Değer — sadece numeric tipi */}
      {dataType === 'numeric' && (
        <Row label="Max. Değer" hint="opsiyonel">
          <input
            type="number"
            step="any"
            value={maxValue}
            onChange={function(e) { setMaxValue(e.target.value) }}
            placeholder="örn. 100"
            className={inputBase + inputOk}
          />
        </Row>
      )}

      {/* Sayisal bicim — tek dropdown. Ayraclar tarayici locale'inden turetilir. */}
      {dataType === 'numeric' && (
        <Row label="Biçim">
          <select
            value={numberFormatPreset}
            onChange={function(e) { setNumberFormatPreset(e.target.value) }}
            className={inputBase + inputOk}
          >
            {NUMBER_FORMAT_PRESETS.map(function(p) {
              return <option key={p.code} value={p.code}>{formatNumberSample(p)}</option>
            })}
          </select>
        </Row>
      )}

      {/* ── REHBER (LOOKUP & TEXT) — Switch + Ayarlar butonu ───────
          Switch sol, Ayarlar butonu SAG'A yasli (justify-between).
          Switch ON + yapilandirildi → buton YESIL (configured).
          Switch ON + yapilandirilmadi → buton KIRMIZI; Widget Ekle/Guncelle
          tiklaninca shake.  */}
      {(dataType === 'lookup' || dataType === 'guide-list' || dataType === 'text') && (function() {
        var isConfigured = !!(guideConfig && guideConfig.viewCode)
        var needsConfig  = guideEnabled && !isConfigured
        // guide-list: rehber kapatilamaz (toggle disabled). Sadece "Ayarlar"
        // butonu zorunlu olarak gorunur. Lookup/Text icinse toggle aktif.
        var toggleLocked = dataType === 'guide-list'
        return (
          <Row label="Rehber" hint={(dataType === 'lookup' || dataType === 'guide-list') ? 'zorunlu' : 'opsiyonel'}>
            <div className="flex items-center justify-between gap-2">
              <button
                type="button"
                disabled={toggleLocked}
                onClick={function() {
                  if (toggleLocked) return
                  var nx = !guideEnabled
                  setGuideEnabled(nx)
                  if (!nx) {
                    setGuideConfig(null)
                    if (dataType === 'lookup' || dataType === 'guide-list') setOptions([])
                    if (dataType === 'text') setTextGuideCode('')
                  }
                }}
                title={toggleLocked ? 'Rehber Listesi tipinde rehber zorunlu — kapatılamaz' : (guideEnabled ? 'Rehber kullanilir' : 'Rehber kapali')}
                className={
                  'relative w-10 h-5 rounded-full transition-colors flex-shrink-0 ' +
                  (toggleLocked ? 'opacity-70 cursor-not-allowed ' : '') +
                  (guideEnabled ? 'bg-emerald-500' : 'bg-slate-300 dark:bg-white/10')
                }
              >
                <span
                  className="absolute top-0.5 w-4 h-4 rounded-full bg-white shadow-sm"
                  style={{ left: guideEnabled ? '22px' : '2px', transition: 'left 0.18s' }}
                />
              </button>
              {guideEnabled && (
                <button
                  type="button"
                  data-shake-key="guideSettings"
                  onClick={function() { setGuideModalOpen(true) }}
                  className={
                    'h-9 px-3 flex items-center gap-1.5 rounded-lg text-[11px] font-semibold transition-all flex-shrink-0 ' +
                    (isConfigured
                      ? 'bg-emerald-500/15 hover:bg-emerald-500/25 border border-emerald-400/45 text-emerald-600 dark:text-emerald-300 cursor-pointer'
                      : 'bg-red-500/15 hover:bg-red-500/25 border border-red-400/55 text-red-600 dark:text-red-300 cursor-pointer')
                  }
                  title={isConfigured ? 'Rehber yapılandırması (yeniden düzenle)' : 'Yapılandırma eksik — tıklayıp tamamlayın'}
                >
                  <Settings size={11} strokeWidth={2.4} />
                  Ayarlar
                </button>
              )}
            </div>
          </Row>
        )
      })()}

      {/* Secenekler / Hedef URL / Alt Tablo — dropdown, multi-select,
          link, grid tipleri icin (lookup hariç). Inline panel yerine "Duzenle"
          butonu OptionsModal'i acar; her tip kendi moduna gecer. */}
      {(dataType === 'dropdown' || dataType === 'multi-select' || dataType === 'link'
        || dataType === 'grid') && (
        <Row label={
          dataType === 'link'   ? 'Hedef URL' :
          dataType === 'grid'   ? 'Alt Form' :
          'Seçenekler'
        }>
          <div className="flex items-center gap-2" data-shake-key="options">
            <div
              className={
                'flex-1 h-9 px-3 rounded-lg text-xs flex items-center truncate ' +
                'bg-white/60 dark:bg-white/[0.04] ' +
                (errors.options
                  ? 'border-2 border-red-500/80 text-slate-600 dark:text-white/60'
                  : 'border border-slate-200 dark:border-white/[0.08] text-slate-600 dark:text-white/60')
              }
            >
              {dataType === 'link'
                ? (options && options[0] ? options[0] : 'URL sablonu tanımlanmamış')
                : dataType === 'grid'
                  ? (options && options[0] ? 'Alt form: ' + options[0] : 'Alt form seçilmemiş')
                  : (options.length === 0
                      ? 'Seçenek tanımlanmamış'
                      : options.length + ' seçenek tanımlı')}
            </div>
            <button
              type="button"
              onClick={function() { setOptionsModalOpen(true) }}
              className="h-9 px-3 flex items-center gap-1.5 rounded-lg bg-indigo-500/10 hover:bg-indigo-500/20 dark:bg-indigo-500/15 dark:hover:bg-indigo-500/25 border border-indigo-400/30 dark:border-indigo-400/35 text-[11px] font-semibold text-indigo-600 dark:text-indigo-300 transition-all flex-shrink-0"
              title={
                dataType === 'link' ? 'Hedef URL sablonunu duzenle' :
                dataType === 'grid' ? 'Alt form sec' :
                'Seçenekleri düzenle'
              }
            >
              <Pencil size={11} strokeWidth={2.4} />
              Düzenle
            </button>
          </div>
        </Row>
      )}

      {/* Grup Adı */}
      <Row label="Grup Adı">
        <GroupSelector
          groups={groups}
          value={groupId}
          onChange={setGroupId}
          disabled={saving}
        />
      </Row>

      {/* Arama (sadece guide-list) — opsiyonel free-text search input'u tablo ustunde.
          guideConfig.searchEnabled olarak saklanir; runtime'da GuideListField okur. */}
      {dataType === 'guide-list' && (
        <Row label="Arama" hint="opsiyonel">
          <button
            type="button"
            onClick={function() { setSearchEnabled(function(v) { return !v }) }}
            title={searchEnabled ? 'Arama input\'u tablonun üstünde görünür' : 'Arama özelliği kapalı'}
            className={
              'relative w-10 h-5 rounded-full transition-colors flex-shrink-0 ' +
              (searchEnabled ? 'bg-emerald-500' : 'bg-slate-300 dark:bg-white/10')
            }
          >
            <span
              className="absolute top-0.5 w-4 h-4 rounded-full bg-white shadow-sm"
              style={{ left: searchEnabled ? '22px' : '2px', transition: 'left 0.18s' }}
            />
          </button>
        </Row>
      )}

      {/* Görünüm Yeri (sadece guide-list) — Form / Kart / Her İkisi.
          Backend metadata.displayScope olarak saklanir; DWR ve SmartCard'da
          render kararini verir. Default 'both'. */}
      {dataType === 'guide-list' && (
        <Row label="Görünüm" hint="nerede yer alsın">
          <div className="flex items-center gap-1.5">
            {[
              { v: 'form', label: 'Form', hint: 'Sadece edit ekranı (akordion açık gelir)' },
              { v: 'card', label: 'Kart', hint: 'Sadece liste sayfasındaki kart' },
              { v: 'both', label: 'Her İkisi', hint: 'Form + kart' },
            ].map(function(opt) {
              var act = displayScope === opt.v
              return (
                <button
                  key={opt.v}
                  type="button"
                  onClick={function() { setDisplayScope(opt.v) }}
                  title={opt.hint}
                  className={
                    'flex-1 h-8 px-3 rounded-lg text-[11px] font-semibold transition-all ' +
                    (act
                      ? 'bg-emerald-500/20 border border-emerald-400/55 text-emerald-700 dark:text-emerald-300'
                      : 'bg-white/60 dark:bg-white/[0.04] border border-slate-200 dark:border-white/[0.08] text-slate-600 dark:text-white/55 hover:border-emerald-400/40')
                  }
                >
                  {opt.label}
                </button>
              )
            })}
          </div>
        </Row>
      )}

      {/* Genişlik — 24-kolonlu interaktif slider (daha hassas ayar).
          Cell'lere tikla veya klavye ok'lari ile genislik ayarla.
          Guide-list full-width oldugundan gizli. */}
      {dataType !== 'group' && dataType !== 'guide-list' && (
        <Row label="Genişlik" hint="form satırında">
          <div className="flex flex-col gap-1.5">
            <div
              ref={colSpanSliderRef}
              role="slider"
              tabIndex={0}
              aria-valuemin={1}
              aria-valuemax={24}
              aria-valuenow={colSpan}
              aria-label="Widget genişliği 1-24"
              onKeyDown={function(e) {
                if (e.key === 'ArrowRight' || e.key === 'ArrowUp') {
                  e.preventDefault()
                  setColSpan(function(v) { return Math.min(24, v + 1) })
                } else if (e.key === 'ArrowLeft' || e.key === 'ArrowDown') {
                  e.preventDefault()
                  setColSpan(function(v) { return Math.max(1, v - 1) })
                } else if (e.key === 'Home') {
                  e.preventDefault(); setColSpan(1)
                } else if (e.key === 'End') {
                  e.preventDefault(); setColSpan(24)
                }
              }}
              onPointerDown={handleColSpanPointerDown}
              onPointerMove={handleColSpanPointerMove}
              onPointerUp={handleColSpanPointerEnd}
              onPointerCancel={handleColSpanPointerEnd}
              style={{ touchAction: 'none' }}
              className="flex gap-[1px] h-6 rounded-md overflow-hidden cursor-ew-resize select-none focus:outline-none focus:ring-2 focus:ring-emerald-400/40 touch-none"
            >
              {Array.from({ length: 24 }).map(function(_, i) {
                var val = i + 1
                var filled = val <= colSpan
                var isLast = val === colSpan
                return (
                  <div
                    key={i}
                    title={val + ' / 24'}
                    className={
                      'flex-1 transition-colors flex items-center justify-center text-[8px] font-bold pointer-events-none ' +
                      (filled
                        ? 'bg-emerald-500 text-white'
                        : 'bg-slate-200 dark:bg-white/[0.06] text-slate-400 dark:text-white/30')
                    }
                  >
                    {isLast ? val : ''}
                  </div>
                )
              })}
            </div>
            {/* Altta kucuk fraction etiketi (24-col referans). */}
            <div className="flex items-center justify-between text-[10px] text-slate-500 dark:text-white/45">
              <span className="font-mono">{colSpan}/24</span>
              <span className="font-semibold text-emerald-600 dark:text-emerald-400">
                {colSpanFractionLabel(colSpan)}
              </span>
            </div>
            {/* Onerilen genislik — 4 faktore gore hesaplanir:
                  1) dataType (date/sayi dar, lookup geni, multi-select tam...)
                  2) fieldLabel uzunlugu (uzun baslik input'u sikistirir)
                  3) labelStyle (modern floating yatayda yer kaplamaz, sade
                     160px sabit, standart inline max-content)
                  4) rehber kullanimi (display name yer ister)
                Yeni widget olustururken useEffect dataType/labelStyle
                degisikliginde otomatik uygular; baslik veya rehber kucuk
                degisikliklerinde sadece hint guncellenir, "Uygula" linki
                tek tikla setColSpan(rec) yapar. */}
            {dataType !== 'group' && dataType !== 'grid' && dataType !== 'guide-list' && (function() {
              var hasGuideNow = (
                dataType === 'lookup' ||
                (dataType === 'text' && (guideEnabled || !!textGuideCode))
              )
              var rec = recommendedColSpanFor({
                dataType:   dataType,
                label:      fieldLabel,
                labelStyle: labelStyle,
                hasGuide:   hasGuideNow,
              })
              var matches = colSpan === rec

              // Hangi faktorlerin oneriyi etkiledigini kisa not olarak goster
              // — admin neden 18 onerildigini anlasin (uzun baslik / rehber vs.).
              var reasons = []
              if ((fieldLabel || '').trim().length >= 16) reasons.push('uzun başlık')
              if (hasGuideNow) reasons.push('rehber')
              if (labelStyle === 'inline') reasons.push('sabit etiket')
              var reasonText = reasons.length > 0 ? ' · ' + reasons.join(', ') : ''

              return (
                <div className="flex items-center gap-1.5 text-[10px] text-slate-400 dark:text-white/35 mt-1.5">
                  <span aria-hidden="true">💡</span>
                  <span className="min-w-0 truncate">
                    Önerilen: <span className="font-mono">{rec}/24</span>
                    <span className="opacity-70"> ({colSpanFractionLabel(rec)}){reasonText}</span>
                  </span>
                  {!matches && (
                    <button
                      type="button"
                      onClick={function() { setColSpan(rec) }}
                      className="ml-auto flex-shrink-0 text-indigo-600 dark:text-indigo-400 font-semibold hover:underline"
                    >
                      Uygula
                    </button>
                  )}
                  {matches && (
                    <span className="ml-auto flex-shrink-0 text-emerald-600 dark:text-emerald-400 font-semibold">✓ uygun</span>
                  )}
                </div>
              )
            })()}
            {(dataType === 'grid' || dataType === 'group') && (
              <div className="flex items-center gap-1.5 text-[10px] text-slate-400 dark:text-white/35 mt-1.5">
                <span aria-hidden="true">💡</span>
                <span>Bu tip her zaman tam satır kullanır (renderer otomatik).</span>
              </div>
            )}
          </div>
        </Row>
      )}

      {/* Faz G — Kural/Formül & Renk Modu.
          Durum (tanimli/tanimsiz) butonun icinde sag kenarda ikon olarak gosterilir.
          Guide-list value yazmaz (salt okunur), formula/visibility/required-if/default
          kurallari anlamsiz — buton gizli. */}
      {dataType !== 'guide-list' && (
        <div className="pt-2 border-t border-slate-200/40 dark:border-white/[0.06]">
          <button
            type="button"
            onClick={function() { setRuleModalOpen(true) }}
            className="w-full flex items-center gap-2 px-3 py-2.5 rounded-lg text-left transition-all"
            style={{
              background: 'rgba(245,158,11,0.07)',
              border: '1px solid rgba(245,158,11,0.2)',
            }}
            title={(ruleVisibleIf || ruleDisabledIf || ruleRequiredIf || ruleFormula || colorValue || defaultValue) ? 'Tanımlı' : 'Tanımsız'}
          >
            <Wrench size={13} style={{ color: '#fbbf24', flexShrink: 0 }} strokeWidth={2} />
            <span className="text-[11px] font-semibold flex-1" style={{ color: '#fbbf24' }}>
              Kuralları &amp; Formülleri Düzenle
            </span>
            {(ruleVisibleIf || ruleDisabledIf || ruleRequiredIf || ruleFormula || colorValue || defaultValue) ? (
              <CheckCircle2 size={14} style={{ color: '#10b981', flexShrink: 0 }} strokeWidth={2.4} />
            ) : (
              <CircleDashed size={14} style={{ color: 'rgba(148,163,184,0.6)', flexShrink: 0 }} strokeWidth={2} />
            )}
          </button>
        </div>
      )}

      {/* Zorunlu alan toggle — sadece field tipleri icin.
          NOT: 'Sade alan' (isPlainField) toggle'i Başlık Stili 3-segmentine
          tasindi (Sade = labelStyle='inline').
          requiredIf kurali tanimliysa toggle disable + turuncu — kural
          override eder, statik IsRequired anlamsiz.
          guide-list: salt okunur, deger yazmaz → required gizli. */}
      {dataType !== 'group' && dataType !== 'guide-list' && (function() {
        var hasRequiredRule = !!(ruleRequiredIf && ruleRequiredIf.trim())
        return (
          <div className="flex items-center justify-between py-2 px-1">
            <span className="text-[10px] font-semibold uppercase tracking-wider text-slate-500 dark:text-white/40">
              Zorunlu Alan
              {hasRequiredRule && (
                <span className="ml-2 normal-case font-normal" style={{ color: '#fbbf24', fontSize: '9px' }}>
                  · kural devrede
                </span>
              )}
            </span>
            <button
              type="button"
              disabled={hasRequiredRule}
              onClick={function() { if (!hasRequiredRule) setIsRequired(function(v) { return !v }) }}
              title={hasRequiredRule ? '"Kurallar & Formüller" → Zorunluluk koşulu tanımlı; statik toggle devre dışı.' : ''}
              className={
                'relative w-10 h-5 rounded-full transition-colors ' +
                (hasRequiredRule
                  ? 'bg-amber-400/70 cursor-not-allowed opacity-90'
                  : (isRequired ? 'bg-red-500/70' : 'bg-slate-300 dark:bg-white/10'))
              }
            >
              <motion.div
                className="absolute top-0.5 w-4 h-4 rounded-full bg-white shadow-sm"
                animate={{ left: hasRequiredRule ? 22 : (isRequired ? 22 : 2) }}
                transition={{ type: 'spring', stiffness: 500, damping: 30 }}
              />
            </button>
          </div>
        )
      })()}

      {/* Aktif toggle */}
      <div className="flex items-center justify-between py-2 px-1 border-t border-slate-200/40 dark:border-white/[0.06]">
        <span className="text-[10px] font-semibold uppercase tracking-wider text-slate-500 dark:text-white/40">
          Aktif
        </span>
        <button
          type="button"
          onClick={function() { setIsActive(function(v) { return !v }) }}
          className={
            'relative w-10 h-5 rounded-full transition-colors ' +
            (isActive ? 'bg-emerald-500/70' : 'bg-slate-300 dark:bg-white/10')
          }
        >
          <motion.div
            className="absolute top-0.5 w-4 h-4 rounded-full bg-white shadow-sm"
            animate={{ left: isActive ? 22 : 2 }}
            transition={{ type: 'spring', stiffness: 500, damping: 30 }}
          />
        </button>
      </div>

      {/* Actions */}
      <div className="flex items-center gap-2 pt-1">
        {isEdit && (
          <button
            type="button"
            onClick={onCancel}
            disabled={saving}
            className="px-3 py-2 rounded-lg bg-white/[0.04] hover:bg-white/[0.08] border border-white/[0.06] text-xs font-medium text-white/50 hover:text-white/70 transition-all disabled:opacity-50"
          >
            İptal
          </button>
        )}
        <button
          type="submit"
          disabled={saving}
          className="flex-1 flex items-center justify-center gap-1.5 px-3 py-2 rounded-lg bg-indigo-500 hover:bg-indigo-600 dark:bg-indigo-500/25 dark:hover:bg-indigo-500/35 border border-indigo-500 dark:border-indigo-400/25 text-xs font-semibold text-white dark:text-indigo-200 transition-all disabled:opacity-50 shadow-sm"
        >
          {isEdit ? <Check size={13} /> : <Plus size={13} />}
          {saving ? 'Kaydediliyor...' : (isEdit ? 'Güncelle' : 'Widget Ekle')}
        </button>
      </div>

      <OptionsModal
        isOpen={optionsModalOpen}
        onClose={function() { setOptionsModalOpen(false) }}
        mode={
          dataType === 'link'   ? 'link' :
          dataType === 'lookup' ? 'lookup' :
          dataType === 'grid'   ? 'grid' :
          'options'
        }
        initialOptions={options}
        onSaved={function(newOpts) {
          setOptions(newOpts)
          // errors.options / optionRows temizle
          if (errors.options || errors.optionRows) {
            setErrors(function(prev) {
              var copy = Object.assign({}, prev)
              delete copy.options
              delete copy.optionRows
              return copy
            })
          }
          setOptionsModalOpen(false)
        }}
        isEdit={isEdit}
      />

      <GuideSettingsModal
        isOpen={guideModalOpen}
        onClose={function() { setGuideModalOpen(false) }}
        fieldLabel={fieldLabel || ''}
        initialConfig={guideConfig}
        hideValueDisplayColumns={dataType === 'guide-list'}
        extraFieldOptions={(function() {
          // Kardes widget'lar + form sabit alanlari (DB tablo kolonlari) birlestirilir.
          // Sabit alanlar 'Form Alanları' grubu altinda gorunur; admin rehber SQL
          // kisitinda `{#item_code}` gibi kullanir, runtime DOM lookup ile resolve eder.
          var widgetOpts = buildWidgetExtraOptions(existingFields, editingField && editingField.id)
          var staticOpts = formStaticFields.map(function(col) {
            // Backward compat: eski sema string array idi; yeni sema { fieldKey, label }
            // object array. Iki sekli de destekle ki gecisken davranis bozulmasin.
            var fieldKey = (typeof col === 'string') ? col : String(col.fieldKey || col.token || '')
            var rawLabel = (typeof col === 'string') ? '' : String(col.label || col.fieldLabel || '')
            // FldSet'te eslesme yoksa label === fieldKey doner — bunu ham
            // gormek yerine humanize edip kullanici-dostu bir gorunum saglayalim.
            // (`MaterialCode` → "Malzeme Kodu", `unit_id` → "Birim", `tax_rate` → "Vergi Oranı")
            var label = (rawLabel && rawLabel !== fieldKey) ? rawLabel : humanizeFieldKey(fieldKey)
            return {
              token:     fieldKey,
              label:     label,
              secondary: fieldKey,    // dropdown'da 2. satir monospace teknik kod
              group:     'Form Alanları',
            }
          })
          return widgetOpts.concat(staticOpts)
        })()}
        onSaved={function(cfg) {
          setGuideConfig(cfg)
          // Ayrica eski legacy alanlari da senkronize et (text+rehber icin).
          if (dataType === 'text') setTextGuideCode(cfg.viewCode || '')
          if (dataType === 'lookup' || dataType === 'guide-list') setOptions(cfg.viewCode ? [cfg.viewCode] : [])
          if (errors.options) setErrors(function(p) {
            var c = Object.assign({}, p); delete c.options; return c
          })
          setGuideModalOpen(false)
        }}
      />

      <RuleBuilderModal
        isOpen={ruleModalOpen}
        onClose={function() { setRuleModalOpen(false) }}
        onSave={function(vals) {
          setRuleVisibleIf(vals.visibleIf || '')
          setRuleDisabledIf(vals.disabledIf || '')
          setRuleRequiredIf(vals.requiredIf || '')
          setRuleFormula(vals.formula || '')
          setColorType(vals.colorType != null ? vals.colorType : 0)
          setColorValue(vals.colorValue || '')
          setDefaultValue(vals.defaultValue || '')
          setDefaultValueKind(vals.defaultValueKind || 'static')
          setRuleModalOpen(false)
        }}
        dataType={dataType}
        initialValues={{
          visibleIf:        ruleVisibleIf,
          disabledIf:       ruleDisabledIf,
          requiredIf:       ruleRequiredIf,
          formula:          ruleFormula,
          colorType:        colorType,
          colorValue:       colorValue,
          defaultValue:     defaultValue,
          defaultValueKind: defaultValueKind,
        }}
        availableWidgets={(function() {
          // Onceki: sadece mevcut form'un alanlari. Simdi: kendi alanlari +
          // ust form (parent) alanlari. Her widget _sourceFormCode/Label
          // tasir; FieldDropdown etikete kucuk rozet olarak basar.
          var ownList = existingFields.map(function(f) {
            return {
              widgetCode:       f.widgetCode || f.fieldKey || '',
              label:            f.label || f.fieldLabel || '',
              dataType:         f.dataType || 'text',
              // dropdown / multi-select icin: rule builder value alaninda secim
              // listesi olarak kullanilir.
              options:          Array.isArray(f.options) ? f.options : null,
              _sourceFormCode:  null,
              _sourceFormLabel: null,
            }
          })
          var parentList = parentFormWidgets.map(function(w) {
            return {
              widgetCode:       w.widgetCode || '',
              label:            w.label || '',
              dataType:         w.dataType || 'text',
              options:          Array.isArray(w.options) ? w.options : null,
              _sourceFormCode:  w._sourceFormCode || null,
              _sourceFormLabel: w._sourceFormLabel || null,
            }
          })
          return ownList.concat(parentList)
            .filter(function(w) { return w.widgetCode && w.widgetCode !== fieldKey })
        })()}
      />
    </motion.form>
  )
}
