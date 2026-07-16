/**
 * DynamicWidgetFactory
 *
 * Generic widget factory — CalibraSmartBoard icin her tur entity'yi (malzeme,
 * cari hesap, satis teklifi, is emri vb.) destekler. Kart icindeki widget'lar
 * tamamen JSON props'lardan beslenir. Kart icerisine hicbir hardcoded is
 * mantigi girmez.
 *
 * Widget tipleri:
 *   - 'data' : Sadece okuma amacli deger gosterir (stok, bakiye, tarih)
 *   - 'link' : Tiklanabilir kisayol (Ekstre Al, Siparise Cevir)
 *
 * Widget dataType'lari (otomatik icon + format):
 *   - 'text'     : Duz metin (FileText icon)
 *   - 'numeric'  : Sayi, tr-TR bin ayiricisi ile (Hash icon)
 *   - 'currency' : Para, "₺ X,XX" (DollarSign icon)
 *   - 'percent'  : Yuzde, "% X" (Percent icon)
 *   - 'date'     : Tarih "dd.MM.yyyy" (Calendar icon)
 *   - 'datetime' : Tarih+saat "dd.MM.yyyy HH:mm" (Clock icon)
 *   - 'boolean'  : Evet/Hayir (CheckCircle / XCircle icon, renk otomatik)
 *   - 'status'   : Etiket gibi duz gosterim (Activity icon)
 *
 * Widget JSON sekli:
 *   {
 *     id:           'unique_key',
 *     type:         'data' | 'link',
 *     dataType:     'date' | 'numeric' | ...,  // icin icon + format otomatik
 *     icon:         'IconHint',                // manuel override (ops)
 *     label:        'Son Kullanma',
 *     value:        '2026-04-30',              // raw deger — dataType'a gore format
 *     detail:       'Stok takip eden tarih',
 *     color:        'amber',                   // manuel override (ops)
 *     permissionKey:'VIEW_EXPIRY',             // C# tarafinda filtrelenir, React ignore
 *     url:          '/path?id={id}',           // link tipinde
 *   }
 *
 * Factory'nin gorevi:
 *   1. Icon string'ini (veya dataType'i) Lucide component'ine donusturur
 *   2. Color string'ini (veya dataType'i) renk paletine esler
 *   3. Raw value'yu dataType'a gore formatlar
 *   4. URL template placeholder'larini entity verisiyle doldurur
 */
import {
  // Data ikonlari
  Package, DollarSign, Warehouse, Tag, Scale, Ruler, Shuffle,
  Calendar, Clock, TrendingUp, Layers, FileText, Hash, ShieldCheck,
  User, Users, Building2, Phone, Mail, MapPin, Percent,
  AlertTriangle, CheckCircle, CheckCircle2, XCircle, CircleDot, Activity,
  AlignLeft, Paperclip,
  // Link ikonlari
  ArrowUpRight, ExternalLink, ArrowRight, Link as LinkIcon,
  TreePine, Receipt, Send, FileCheck, History, BarChart3,
  Printer, Copy, Edit, Edit2, Eye, Share2, Download, List,
  Trash, Trash2, ClipboardList, Cog,
  // Media / Toggle ikonlari
  Play, Pause, PlayCircle, PauseCircle, Power,
  ToggleLeft, ToggleRight, RefreshCw, RotateCcw,
  // Lookup / Search ikonlari
  Search,
  // Grid / Master-Detail ikonlari
  Table,
  // Genel
  Plus, Minus,
  // AR-GE
  FlaskConical,
} from 'lucide-react'

/* ── Icon registry ─────────────────────────────── */
var iconMap = {
  Package: Package, DollarSign: DollarSign, Warehouse: Warehouse,
  Tag: Tag, Scale: Scale, Ruler: Ruler, Shuffle: Shuffle,
  Calendar: Calendar, Clock: Clock, TrendingUp: TrendingUp,
  Layers: Layers, FileText: FileText, Hash: Hash, AlignLeft: AlignLeft, Paperclip: Paperclip,
  ShieldCheck: ShieldCheck, User: User, Users: Users,
  Building2: Building2, Phone: Phone, Mail: Mail, MapPin: MapPin,
  Percent: Percent, AlertTriangle: AlertTriangle,
  CheckCircle: CheckCircle, CheckCircle2: CheckCircle2, XCircle: XCircle,
  CircleDot: CircleDot, Activity: Activity,
  // Link ikonlari
  ArrowUpRight: ArrowUpRight, ExternalLink: ExternalLink,
  ArrowRight: ArrowRight, Link: LinkIcon, TreePine: TreePine,
  Receipt: Receipt, Send: Send, FileCheck: FileCheck,
  History: History, BarChart3: BarChart3, Printer: Printer,
  Copy: Copy, Edit: Edit, Edit2: Edit2, Eye: Eye, Share2: Share2, Download: Download, List: List,
  Trash: Trash, Trash2: Trash2, ClipboardList: ClipboardList, Cog: Cog,
  // Media / Toggle
  Play: Play, Pause: Pause, PlayCircle: PlayCircle, PauseCircle: PauseCircle, Power: Power,
  ToggleLeft: ToggleLeft, ToggleRight: ToggleRight, RefreshCw: RefreshCw, RotateCcw: RotateCcw,
  // Lookup
  Search: Search,
  // Grid / Master-Detail
  Table: Table,
  // Genel
  Plus: Plus, Minus: Minus,
  FlaskConical: FlaskConical,
}

/* ── Color palette (DARK tema — orijinal, light pastel text) ────
   `text` degerleri dark tema icin acik pastel — light temada okunmaz.
   Light tema icin asagida `colorPaletteLight` var. */
export var colorPalette = {
  emerald: { bg: 'rgba(16,185,129,0.08)',  border: 'rgba(16,185,129,0.18)',  text: '#6ee7b7', icon: '#34d399' },
  amber:   { bg: 'rgba(245,158,11,0.08)',  border: 'rgba(245,158,11,0.18)',  text: '#fcd34d', icon: '#fbbf24' },
  blue:    { bg: 'rgba(59,130,246,0.08)',  border: 'rgba(59,130,246,0.18)',  text: '#93c5fd', icon: '#60a5fa' },
  violet:  { bg: 'rgba(139,92,246,0.08)',  border: 'rgba(139,92,246,0.18)',  text: '#c4b5fd', icon: '#a78bfa' },
  cyan:    { bg: 'rgba(6,182,212,0.08)',   border: 'rgba(6,182,212,0.18)',   text: '#67e8f9', icon: '#22d3ee' },
  rose:    { bg: 'rgba(244,63,94,0.08)',   border: 'rgba(244,63,94,0.18)',   text: '#fda4af', icon: '#fb7185' },
  slate:   { bg: 'rgba(100,116,139,0.08)', border: 'rgba(100,116,139,0.18)', text: '#cbd5e1', icon: '#94a3b8' },
  indigo:  { bg: 'rgba(99,102,241,0.08)',  border: 'rgba(99,102,241,0.18)',  text: '#a5b4fc', icon: '#818cf8' },
  teal:    { bg: 'rgba(20,184,166,0.08)',  border: 'rgba(20,184,166,0.18)',  text: '#5eead4', icon: '#2dd4bf' },
  orange:  { bg: 'rgba(249,115,22,0.08)',  border: 'rgba(249,115,22,0.18)',  text: '#fdba74', icon: '#fb923c' },
  red:     { bg: 'rgba(239,68,68,0.08)',   border: 'rgba(239,68,68,0.18)',   text: '#fca5a5', icon: '#f87171' },
}

/* ── Color palette (LIGHT tema — koyu text, beyaz kartta okunabilir) ──
   bg/border daha belirgin (0.14/0.35), text Tailwind *-700 tonlari. */
export var colorPaletteLight = {
  emerald: { bg: 'rgba(16,185,129,0.14)',  border: 'rgba(16,185,129,0.35)',  text: '#047857', icon: '#059669' },
  amber:   { bg: 'rgba(245,158,11,0.14)',  border: 'rgba(245,158,11,0.35)',  text: '#b45309', icon: '#d97706' },
  blue:    { bg: 'rgba(59,130,246,0.14)',  border: 'rgba(59,130,246,0.35)',  text: '#1d4ed8', icon: '#2563eb' },
  violet:  { bg: 'rgba(139,92,246,0.14)',  border: 'rgba(139,92,246,0.35)',  text: '#6d28d9', icon: '#7c3aed' },
  cyan:    { bg: 'rgba(6,182,212,0.14)',   border: 'rgba(6,182,212,0.35)',   text: '#0e7490', icon: '#0891b2' },
  rose:    { bg: 'rgba(244,63,94,0.14)',   border: 'rgba(244,63,94,0.35)',   text: '#be123c', icon: '#e11d48' },
  slate:   { bg: 'rgba(100,116,139,0.14)', border: 'rgba(100,116,139,0.35)', text: '#334155', icon: '#475569' },
  indigo:  { bg: 'rgba(99,102,241,0.14)',  border: 'rgba(99,102,241,0.35)',  text: '#4338ca', icon: '#4f46e5' },
  teal:    { bg: 'rgba(20,184,166,0.14)',  border: 'rgba(20,184,166,0.35)',  text: '#0f766e', icon: '#0d9488' },
  orange:  { bg: 'rgba(249,115,22,0.14)',  border: 'rgba(249,115,22,0.35)',  text: '#c2410c', icon: '#ea580c' },
  red:     { bg: 'rgba(239,68,68,0.14)',   border: 'rgba(239,68,68,0.35)',   text: '#b91c1c', icon: '#dc2626' },
}

/* ── dataType → Icon eslemesi ─────────────────── */
var dataTypeIconMap = {
  text:     FileText,
  numeric:  Hash,
  currency: DollarSign,
  percent:  Percent,
  date:     Calendar,
  datetime: Clock,
  boolean:  CheckCircle,
  status:   Activity,
  phone:    Phone,
}

/* ── dataType/type → sabit chip genisligi (px) ──────────────────
   SmartCard'daki widget chip'leri (SmartWidget) icerige gore degisken
   genislikte degil, tipe gore SABIT genislikte render edilir. Bir
   board'daki tum kartlar ayni widget setini/sirasini tasidigi icin
   (bkz. SmartBoard masterWidgets), her pozisyondaki chip her kartta
   ayni genislikte olursa kumulatif x-offset de her kartta ayni olur →
   dikey "sutun" hizalamasi elde edilir (gercek tablo gibi), icerik
   uzunlugundan bagimsiz. Uzun icerik (uzun stok adi vb.) bu sabit
   genislik icinde ellipsis ile kirpilir; tam metin WidgetTooltip
   hover'inda gorunur — bkz. SmartWidget.jsx.
   3 kademe (tek genislik yerine): kisa/sabit formatli tipler dar,
   serbest-metin tipler genis — boylece "Durum: Aktif" gibi kisa
   chip'ler gereksiz yere sismez, "Stok Adi" gibi metin alanlari da
   asiri kirpilmez. */
export var CHIP_WIDTH_COMPACT = 132  // boolean, numeric, percent, date, status
export var CHIP_WIDTH_MEDIUM  = 158  // currency, datetime, dropdown/options, attachment
export var CHIP_WIDTH_WIDE    = 192  // text, textarea, lookup, guide-list, link, multi-select, default

// SmartTable'in en bastaki TEK sabit sutunu (px): basliksiz "Islemler" (kebab).
// Sil artik bu menunun ICINE tasindigi icin ayri bir Sil sutunu yok (2026-07-16
// revizyon 3). Burada (SmartTable.jsx yerine) tanimlanmasinin sebebi: hem
// SmartTable.jsx hem SmartTableRow.jsx bu degeri kullanir (kimlik hucresinin
// sticky-left offset'i icin) — iki dosyanin birbirini import etmesi (dongusel
// import) yerine ortak, bagimliligi olmayan bu dosyadan paylasilir.
// Sira: Islemler (0'dan) → Kod/Ad (Islemler'den sonra) → veri sutunlari
// (pin'liler once, bkz. SmartTable.computeColumns).
export var TABLE_MENU_COL_WIDTH = 44

// Kompozit "Kod/Ad" kimlik sutunu kaldirildiginda (2026-07-16 revizyon 4)
// yerini alan iki normal widget — LogisticsController.BuildMaterialCardsBoardConfigAsync
// icinde MakeWidget("w_kod","Stok Kodu",...) / MakeWidget("w_ad","Stok Adi",...)
// ile üretilir (id ile baglanir, isimle DEGIL — admin bu plain-field'lari
// custom widget'a maplerse bu id'ler masterWidgets'ta hic bulunmayabilir,
// tum kullanim yerleri bunu tolere eder). Kullanim: (1) SmartTable.computeColumns
// — config yokken varsayilan ilk iki sira + kullanici tum sutunlari
// kaldirdiginda "asla tamamen bos tablo olmasin" fallback'i, (2) SmartColumnSettings
// — ilk acilis ve Sifirla'da ayni varsayilan sirayi yansitmak icin. Ikisi de
// (varsa) DIGER sutunlarla AYNI ayarlara tabi — burada sadece "varsayilan
// sirada once gelsinler" bilgisini tasir, baska hicbir ayricalik yok.
export var TABLE_LEAD_WIDGET_IDS = ['w_kod', 'w_ad']

var dataTypeWidthMap = {
  boolean:        CHIP_WIDTH_COMPACT,
  numeric:        CHIP_WIDTH_COMPACT,
  percent:        CHIP_WIDTH_COMPACT,
  date:           CHIP_WIDTH_COMPACT,
  status:         CHIP_WIDTH_COMPACT,
  currency:       CHIP_WIDTH_MEDIUM,
  datetime:       CHIP_WIDTH_MEDIUM,
  dropdown:       CHIP_WIDTH_MEDIUM,
  options:        CHIP_WIDTH_MEDIUM,
  attachment:     CHIP_WIDTH_MEDIUM,
  phone:          CHIP_WIDTH_MEDIUM,
  text:           CHIP_WIDTH_WIDE,
  textarea:       CHIP_WIDTH_WIDE,
  lookup:         CHIP_WIDTH_WIDE,
  'multi-select': CHIP_WIDTH_WIDE,
  multiselect:    CHIP_WIDTH_WIDE,
  multi_select:   CHIP_WIDTH_WIDE,
}

/**
 * Widget chip'i icin sabit genislik (px) doner — icerige (value/label
 * uzunluguna) DEGIL sadece dataType/type'a bakar; ayni widget her
 * kartta ayni genislikte cizilir (hizalama sarti bunu gerektirir).
 * type='link' ve dataType='guide-list' her zaman WIDE (ok ikonu +
 * serbest etiket tasirlar). Taninmayan/tanimsiz dataType → WIDE
 * (guvenli taraf: dar kirpmaktansa biraz bosluk).
 */
export function resolveChipWidth(dataType, type) {
  if (type === 'link') return CHIP_WIDTH_WIDE
  var dt = String(dataType || '').toLowerCase()
  if (dt === 'guide-list') return CHIP_WIDTH_WIDE
  if (Object.prototype.hasOwnProperty.call(dataTypeWidthMap, dt)) return dataTypeWidthMap[dt]
  return CHIP_WIDTH_WIDE
}

/* ── dataType → varsayilan renk ───────────────── */
var dataTypeColorMap = {
  text:     'slate',
  numeric:  'blue',
  currency: 'amber',
  percent:  'violet',
  date:     'cyan',
  datetime: 'cyan',
  boolean:  'emerald',
  status:   'indigo',
  phone:    'blue',
}

/**
 * Icon string'ini veya dataType'i Lucide component'ine cevirir.
 * Oncelik: iconHint (manuel) > dataType (otomatik) > fallback.
 */
export function resolveIcon(iconHint, fallback, dataType) {
  // 1. Manuel icon hint
  if (iconHint) {
    if (typeof iconHint === 'function') return iconHint
    if (iconMap[iconHint]) return iconMap[iconHint]
  }
  // 2. dataType'tan cozumle
  if (dataType && dataTypeIconMap[dataType]) {
    return dataTypeIconMap[dataType]
  }
  return fallback || CircleDot
}

/**
 * Renk adini palette'den cozer. Verilmezse dataType'tan otomatik.
 */
export function resolveColor(colorName, dataType) {
  if (colorName && colorPalette[colorName]) return colorPalette[colorName]
  if (dataType && dataTypeColorMap[dataType]) {
    return colorPalette[dataTypeColorMap[dataType]]
  }
  return colorPalette.slate
}

/**
 * Theme-aware color resolver. isDark true → dark palette (acik pastel text),
 * false → light palette (koyu text). Ikisinin de ayni shape'i var.
 */
export function resolveColorForTheme(colorName, dataType, isDark) {
  var pal = isDark ? colorPalette : colorPaletteLight
  if (colorName && pal[colorName]) return pal[colorName]
  if (dataType && dataTypeColorMap[dataType]) {
    return pal[dataTypeColorMap[dataType]]
  }
  return pal.slate
}

/**
 * 'phone' dataType icin mini TR format fonksiyonu — bundle bagimsiz calisir.
 * window.CalibraPhone (wwwroot/js/calibra-phone.js, global <script> enhancer)
 * yuklenmisse formatValue onu kullanir (tek kaynak, ayni algoritma); bu SmartBoard
 * React bundle'i o script'ten once veya ondan bagimsiz da calisabildigi icin
 * ayni 10-hane algoritmasi burada lokal olarak tekrar edilir (2026-07-16).
 */
function formatPhoneMini(raw) {
  if (raw == null) return ''
  var s = String(raw)
  var d = s.replace(/\D+/g, '')
  if (!d) return s
  var work = d
  if (work.length >= 12 && work.indexOf('90') === 0) work = work.substring(2)
  while (work.length > 1 && work.charAt(0) === '0') work = work.substring(1)
  if (work === '0') return '0'
  if (work.length > 10) return s
  if (work.length === 0) return ''
  var out = '0 (' + work.substring(0, Math.min(3, work.length))
  if (work.length <= 3) return out
  out += ') ' + work.substring(3, Math.min(6, work.length))
  if (work.length <= 6) return out
  out += ' ' + work.substring(6, Math.min(8, work.length))
  if (work.length <= 8) return out
  out += ' ' + work.substring(8, 10)
  return out
}

/**
 * Raw value'yu dataType'a gore formatlar.
 * Value zaten formatlanmissa (string ve dataType text/status) oldugu gibi doner.
 */
export function formatValue(value, dataType) {
  if (value == null || value === '') return ''
  try {
    switch (dataType) {
      case 'phone': {
        if (typeof window !== 'undefined' && window.CalibraPhone && typeof window.CalibraPhone.format === 'function') {
          return window.CalibraPhone.format(value)
        }
        return formatPhoneMini(value)
      }
      case 'numeric': {
        var n = typeof value === 'number' ? value : parseFloat(value)
        if (isNaN(n)) return String(value)
        return n.toLocaleString('tr-TR')
      }
      case 'currency': {
        var c = typeof value === 'number' ? value : parseFloat(value)
        if (isNaN(c)) return String(value)
        return '₺' + c.toLocaleString('tr-TR', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
      }
      case 'percent': {
        var p = typeof value === 'number' ? value : parseFloat(value)
        if (isNaN(p)) return String(value)
        return '%' + p.toLocaleString('tr-TR', { maximumFractionDigits: 2 })
      }
      case 'date': {
        var d = value instanceof Date ? value : new Date(value)
        if (isNaN(d.getTime())) return String(value)
        var dd = String(d.getDate()).padStart(2, '0')
        var mm = String(d.getMonth() + 1).padStart(2, '0')
        var yy = d.getFullYear()
        return dd + '.' + mm + '.' + yy
      }
      case 'datetime': {
        var dt = value instanceof Date ? value : new Date(value)
        if (isNaN(dt.getTime())) return String(value)
        var ddt = String(dt.getDate()).padStart(2, '0')
        var mmt = String(dt.getMonth() + 1).padStart(2, '0')
        var yyt = dt.getFullYear()
        var hh = String(dt.getHours()).padStart(2, '0')
        var mn = String(dt.getMinutes()).padStart(2, '0')
        return ddt + '.' + mmt + '.' + yyt + ' ' + hh + ':' + mn
      }
      case 'boolean': {
        if (value === true || value === 'true' || value === 1 || value === '1') return 'Evet'
        if (value === false || value === 'false' || value === 0 || value === '0') return 'Hayir'
        return String(value)
      }
      case 'text':
      case 'status':
      default:
        return String(value)
    }
  } catch (e) {
    return String(value)
  }
}

/**
 * Boolean dataType icin dinamik icon (CheckCircle / XCircle).
 */
export function resolveBooleanIcon(rawValue) {
  var isTrue = (rawValue === true || rawValue === 'true' || rawValue === 1 || rawValue === '1' || rawValue === 'Evet')
  return isTrue ? CheckCircle : XCircle
}

/**
 * URL template'inde {placeholder} yerine entity field'larini ikame eder.
 * Ornek: '/Finance/Account?id={id}' + { id: 42 } → '/Finance/Account?id=42'
 */
export function interpolateUrl(template, entity) {
  if (!template) return null
  return template.replace(/\{(\w+)\}/g, function(_, key) {
    return (entity && entity[key] != null) ? String(entity[key]) : ''
  })
}

/**
 * Bir widget JSON'ini "resolved widget"a cevirir:
 * - icon string → component
 * - url template → interpolated url
 * - type default'u 'data'
 */
export function resolveWidget(widget, entity) {
  if (!widget) return null
  var type = widget.type || 'data'
  var Icon = resolveIcon(widget.icon)
  var palette = resolveColor(widget.color)

  var resolved = {
    id: widget.id,
    type: type,
    icon: Icon,
    label: widget.label,
    value: widget.value,
    detail: widget.detail || '',
    color: widget.color || 'slate',
    palette: palette,
  }

  if (type === 'link') {
    // URL interpolate (hem raw url hem template destegi)
    if (widget.url) {
      resolved.url = interpolateUrl(widget.url, entity)
    }
  }

  return resolved
}

/**
 * Bir entity'nin widget listesini toplu olarak resolve eder.
 */
export function resolveEntityWidgets(widgets, entity) {
  if (!Array.isArray(widgets)) return []
  return widgets
    .map(function(w) { return resolveWidget(w, entity) })
    .filter(function(w) { return w != null })
}
