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
