/**
 * cardLayoutTokens — kalem KARTI duzeni icin paylasilan sabitler (2026-08-06).
 *
 * Bu bloklar daha once hem CalibraLineItemsGrid.jsx (gercek kart render'i) hem
 * LineCardLayoutEditor.jsx (duzen editoru) icinde KOPYA duruyordu. Editorun
 * onizlemesi gercek kartla birebir ayni gorunmek zorunda oldugu icin iki kopyanin
 * ayrisması dogrudan WYSIWYG bozulmasi demek — DRY kurali geregi tek kaynak.
 *
 * DIKKAT: Buradaki degerler server sozlesmesiyle baglidir
 * (LineCardLayoutController.GridUnits = 48, LayoutItemDto whitelist'leri).
 * Degistirmeden once server tarafina bak.
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
export function resolveIcon(name) {
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
   kayitlari server okuma yolunda x2 olcekleyip normalize eder). */
export var CARD_GRID_UNITS = 48
export var MIN_SPAN = 3
/* Gercek kartin alan izgarasi bosluklari (CalibraLineItemsGrid kart render'i) —
   editor onizlemesi ayni degerleri kullanmak zorunda, yoksa oranlar kayar. */
export var CARD_COLUMN_GAP = 12
export var CARD_ROW_GAP = 10
/* Gercek karttaki giris alaninin yuksekligi — LineGridCell input'u
   (`px-2.5 py-2 text-[13px]`) canli olcumde 29-30px veriyor. Editor onizlemesi
   bu degeri kullanmak ZORUNDA: 34px kullanildiginda satir basina ~4px fazla
   yukseklik olusuyordu ve "ayar ekranindaki satirlar gercekten daha yuksek"
   sikayeti dogdu (2026-08-06 kullanici bildirimi). */
export var CARD_FIELD_HEIGHT = 30

/* ── Sutun genisligi altyapisi ─────────────────────────────────────────────
   Ham 48'lik birim kullaniciya "18/48" olarak degil KESIR olarak gosterilir
   (1/4, 1/3, 1/2 …) — premium form tasarimcilarinin dili. Birim hassasiyeti
   korunur: preset disi degerler "n/48" olarak gosterilir. */
export var WIDTH_PRESETS = [
  { span: 8,  label: '1/6' },
  { span: 12, label: '1/4' },
  { span: 16, label: '1/3' },
  { span: 24, label: '1/2' },
  { span: 32, label: '2/3' },
  { span: 36, label: '3/4' },
  { span: 48, label: 'Tam' },
]
export function spanLabel(span) {
  for (var i = 0; i < WIDTH_PRESETS.length; i++) {
    if (WIDTH_PRESETS[i].span === span) return WIDTH_PRESETS[i].label
  }
  return span + '/' + CARD_GRID_UNITS
}
export function clampSpan(span) {
  return Math.min(CARD_GRID_UNITS, Math.max(MIN_SPAN, span))
}
/* Surukleme sirasinda yaygin kesirlere yapisma (1 birim tolerans) — serbest
   suruklemede 23/48 gibi "neredeyse yarim" degerler olusmasin. */
export function snapSpan(span) {
  for (var i = 0; i < WIDTH_PRESETS.length; i++) {
    if (Math.abs(WIDTH_PRESETS[i].span - span) <= 1) return WIDTH_PRESETS[i].span
  }
  return span
}

/* CSS grid'in satir sarma davranisinin aynisi: alan mevcut satira sigmiyorsa
   yeni satira akar (kalan bosluk bos kalir). Editordeki satir bilgisi ve
   "satiri doldur / esit dagit" araclari bu paketlemeden beslenir. */
export function packRows(entries) {
  var rows = []
  var cur = []
  var used = 0
  for (var i = 0; i < entries.length; i++) {
    var s = clampSpan(entries[i].it.span)
    if (cur.length && used + s > CARD_GRID_UNITS) {
      rows.push({ entries: cur, used: used })
      cur = []
      used = 0
    }
    cur.push(entries[i])
    used += s
  }
  if (cur.length) rows.push({ entries: cur, used: used })
  return rows
}

/* Serbest yerlesimde bir satirin taban yuksekligi (etiket 15 + mb 2 + alan 30).
   `gridAutoRows` bu degeri kullanir — aksi halde BOS birakilan satirlar 0px olur
   ve "ustte bosluk birakma" ozelligi calismaz. */
export var CARD_ROW_HEIGHT = 47

/* ── Serbest yerlesim (v3, 2026-08-06) ─────────────────────────────────────
   Alanlar artik yalniz siraya gore sola/uste yigilmaz; her ogenin row/col'u
   olabilir. Asagidaki yardimcilar cakismalari cozer ve koordinati olmayan
   (v1/v2) ogeleri ESKI AKIS semantigiyle yerlestirir — boylece mevcut
   duzenlerin gorunumu birebir korunur. */
function occupy(map, row, col, span) {
  for (var c = col; c < col + span; c++) map[row + ':' + c] = 1
}
function slotFree(map, row, col, span) {
  if (col < 1 || col + span - 1 > CARD_GRID_UNITS) return false
  for (var c = col; c < col + span; c++) { if (map[row + ':' + c]) return false }
  return true
}
/* Verilen konumdan baslayarak ilk uygun yuvayi bulur: once ayni satirda saga,
   sigmazsa bir alt satirin basindan devam. */
export function findFreeSlot(map, startRow, startCol, span) {
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
 * Item listesine kesin (row, col) atar.
 *   1) Koordinati olanlar once yerlesir (cakisirsa en yakin bos yuvaya kayar).
 *   2) Koordinatsizlar (v1/v2 kayitlari, yeni eklenen alanlar) siradan akar.
 * Girdi mutasyona ugramaz; { key, span, row, col } listesi doner.
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
/* Yerlesim listesinden isgal haritasi — bir ogeyi disarida birakabilir
   (surukledigimiz ogeyi kendi eski yerine carptirmamak icin). */
export function buildOccupancy(placements, exceptKey) {
  var map = {}
  ;(placements || []).forEach(function (p) {
    if (exceptKey && p.key === exceptKey) return
    occupy(map, p.row, p.col, p.span)
  })
  return map
}

/**
 * Bir izgara biriminin piksel karsiligi — SURUKLEYEREK GENISLETMENIN olcegi.
 *
 * Naif `width / 48` YANLIS: 48 kolonun arasinda 47 adet CARD_COLUMN_GAP vardir
 * ve bosluklar toplam genisligin buyuk kismini tutar (48 birimlik izgarada ~%60).
 * Dogru turetme: W = 48c + 47g  →  c + g = (W + g) / 48. Sürükleme mesafesi
 * "birim + bosluk" adimlariyla ilerledigi icin bolen bu degerdir.
 */
export function unitStep(gridWidth) {
  return (gridWidth + CARD_COLUMN_GAP) / CARD_GRID_UNITS
}
