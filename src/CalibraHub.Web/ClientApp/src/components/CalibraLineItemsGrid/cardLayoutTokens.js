/**
 * cardLayoutTokens — kalem KARTI duzeni icin paylasilan sabitler.
 *
 * 2026-08-19 SADELESTIRME: gercek kart artik CSS-grid/48-birim izgara DEGIL,
 * flex-wrap kompakt bir SERIT (bkz. CalibraLineItemsGrid.jsx renderFieldsList).
 * `span`, hucrenin GORELI GENISLIK AGIRLIGI (flex-grow) olarak yorumlanir; serbest
 * satir/sutun konumlandirma (row/col) runtime'da KULLANILMAZ. Duzen editoru buna
 * gore sadelestirildi (LineCardLayoutEditor.jsx): Sira + Gorunurluk + Genislik
 * (Dar/Normal/Genis) — ham 1-48 sayi/piksel-surukleme YOK.
 *
 * DIKKAT: `CARD_GRID_UNITS` server sozlesmesiyle baglidir
 * (LineCardLayoutController.GridUnits = 48, LayoutItemDto Span 1-48 whitelist'i).
 * resolvePlacements CalibraLineItemsGrid.jsx tarafindan hala import edilir
 * (eski v1/v2 kayitlarin row/col alanlarini sessizce yok saymak icin) — bu
 * dosyaya DOKUNMADAN once orayi kontrol et.
 */
import {
  Hash, FileText, Ruler, Sigma, DollarSign, Percent, Calculator, StickyNote,
  CircleDot, Tag, Barcode, Warehouse,
} from 'lucide-react'

/* C#'taki icon string'ini React bilesenine cevirir. */
export var ICON_MAP = {
  Hash: Hash,
  FileText: FileText,
  Ruler: Ruler,
  Sigma: Sigma,
  DollarSign: DollarSign,
  Percent: Percent,
  Calculator: Calculator,
  StickyNote: StickyNote,
  Tag: Tag,
  Barcode: Barcode,
  Warehouse: Warehouse,
}
/**
 * Ikon adi → bilesen. Bos/null ad = "bu alanda ikon ISTENMIYOR" → null doner
 * (Iskonto/KDV basliklarinda zaten "%" var, bir de yuzde ikonu tekrar oluyordu).
 * Cagiranlar null'a karsi guard etmelidir.
 */
export function resolveIcon(name) {
  if (name == null || name === '') return null
  return ICON_MAP[name] || CircleDot
}

/* Kart etiketi renk token'i → Tailwind sinifi (light+dark cifti tek sinifta).
   HEX saklanmaz/yazilmaz — tema uyumu semantik token uzerinden saglanir
   (LineCardLayout.LabelColor). */
export var CARD_LABEL_COLOR_CLS = {
  slate:   'text-slate-500 dark:text-white/45',
  indigo:  'text-indigo-600 dark:text-indigo-300',
  emerald: 'text-emerald-600 dark:text-emerald-300',
  amber:   'text-amber-600 dark:text-amber-300',
  rose:    'text-rose-600 dark:text-rose-300',
  blue:    'text-blue-600 dark:text-blue-300',
  violet:  'text-violet-600 dark:text-violet-300',
}
export var LABEL_COLOR_TOKENS = ['slate', 'indigo', 'emerald', 'amber', 'rose', 'blue', 'violet']
/* Renk secici yuvarlaklarinin dolgusu — semantik token'in gorsel karsiligi. */
export var LABEL_COLOR_SWATCH_CLS = {
  slate: 'bg-slate-400', indigo: 'bg-indigo-500', emerald: 'bg-emerald-500',
  amber: 'bg-amber-500', rose: 'bg-rose-500', blue: 'bg-blue-500', violet: 'bg-violet-500',
}
/* Renk secicide ekran okuyucu/tooltip metni — renk tek ayirt edici olmasin. */
export var LABEL_COLOR_NAMES = {
  slate: 'Gri', indigo: 'İndigo', emerald: 'Yeşil', amber: 'Amber',
  rose: 'Kırmızı', blue: 'Mavi', violet: 'Mor',
}

/* Izgara cozunurlugu — server GridUnits ile ayni (48; v1'de 24'tu, eski
   kayitlari server okuma yolunda x2 olcekleyip normalize eder). Artik
   pozisyon degil, `span`in ust siniri (goreli genislik agirligi) olarak
   kullanilir. */
export var CARD_GRID_UNITS = 48
export function clampSpan(span) {
  return Math.min(CARD_GRID_UNITS, Math.max(1, span))
}

/* ── Genislik kontrolü (2026-08-19 sadelestirme) ───────────────────────────
   Editorde ham "n/48" sayisi/piksel-surukleme YERINE 3 anlasilir secenek:
   Dar / Normal / Genis. Deger, gercek kartta flex-grow agirligi olarak
   yorumlanan `span`e eslenir. Kayitli span bu ucunden birine tam denk
   gelmeyebilir (eski kayitlar / gelecekte elle JSON) — `widthValueForSpan`
   en yakinini secer, kullanici degistirmedikce span DEGISTIRILMEZ. */
export var WIDTH_OPTIONS = [
  { value: 'narrow', span: 8,  label: 'Dar' },
  { value: 'normal', span: 16, label: 'Normal' },
  { value: 'wide',   span: 28, label: 'Geniş' },
]
export function widthValueForSpan(span) {
  var best = WIDTH_OPTIONS[1]
  var bestDist = Infinity
  WIDTH_OPTIONS.forEach(function (o) {
    var d = Math.abs(o.span - span)
    if (d < bestDist) { bestDist = d; best = o }
  })
  return best.value
}
export function spanForWidthValue(value) {
  for (var i = 0; i < WIDTH_OPTIONS.length; i++) {
    if (WIDTH_OPTIONS[i].value === value) return WIDTH_OPTIONS[i].span
  }
  return 16
}

/* ── Serbest yerlesim uyumluluk katmani (v1/v2/v3 kayitlari) ──────────────
   Gercek kart artik row/col KULLANMIYOR (bkz. CalibraLineItemsGrid.jsx
   yorum satiri "span, goreli genislik agirligi"). Ancak fonksiyon
   CalibraLineItemsGrid.jsx icinde HALA cagriliyor (o dosyaya bu gorevde
   DOKUNULMUYOR) — kaldirilamaz. Eski kayitlardaki row/col alanlari burada
   sessizce tuketilir (okunur ama ciktida artik hicbir yerde render
   pozisyonu olarak kullanilmaz), yeni editor bu alanlari hic YAZMAZ. */
function occupy(map, row, col, span) {
  for (var c = col; c < col + span; c++) map[row + ':' + c] = 1
}
function slotFree(map, row, col, span) {
  if (col < 1 || col + span - 1 > CARD_GRID_UNITS) return false
  for (var c = col; c < col + span; c++) { if (map[row + ':' + c]) return false }
  return true
}
function findFreeSlot(map, startRow, startCol, span) {
  var row = Math.max(1, startRow || 1)
  var col = Math.max(1, startCol || 1)
  for (var guard = 0; guard < 5000; guard++) {
    if (col + span - 1 > CARD_GRID_UNITS) { row++; col = 1; continue }
    if (slotFree(map, row, col, span)) return { row: row, col: col }
    col++
  }
  return { row: row, col: 1 }
}
/**
 * Item listesine kesin (row, col) atar (bkz. CalibraLineItemsGrid.jsx
 * kullanim yeri — sonuc artik render pozisyonu olarak KULLANILMIYOR, yalniz
 * geriye donuk uyumluluk icin hesaplaniyor). Girdi mutasyona ugramaz;
 * { key, span, row, col } listesi doner.
 */
export function resolvePlacements(items) {
  var map = {}
  var out = (items || []).map(function (it) {
    return {
      key: it.key,
      span: clampSpan(it.span),
      row: (typeof it.row === 'number' && it.row >= 1) ? it.row : null,
      col: (typeof it.col === 'number' && it.col >= 1) ? it.col : null,
    }
  })
  out.forEach(function (p) {
    if (p.row == null || p.col == null) return
    var slot = findFreeSlot(map, p.row, p.col, p.span)
    p.row = slot.row; p.col = slot.col
    occupy(map, p.row, p.col, p.span)
  })
  var flowRow = 1, flowCol = 1
  out.forEach(function (p) {
    if (p.row != null && p.col != null) return
    if (flowCol + p.span - 1 > CARD_GRID_UNITS) { flowRow++; flowCol = 1 }
    while (!slotFree(map, flowRow, flowCol, p.span)) {
      flowCol++
      if (flowCol + p.span - 1 > CARD_GRID_UNITS) { flowRow++; flowCol = 1 }
    }
    p.row = flowRow; p.col = flowCol
    occupy(map, p.row, p.col, p.span)
    flowCol += p.span
  })
  return out
}
