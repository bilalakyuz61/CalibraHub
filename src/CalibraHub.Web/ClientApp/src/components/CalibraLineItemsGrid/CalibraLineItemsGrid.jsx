/**
 * CalibraLineItemsGrid — Dinamik, satir-ici duzenlenebilir kalem grid'i
 *
 * "Aptal Bilesen, Zeki Veri": Kolonlar + satirlar C#'tan gelen JSON
 * (BuildDocumentLineGridConfig) ile dinamik cizilir. React icinde hardcoded
 * alan ismi / siralama YOK.
 *
 * Glassmorphism container + Tailwind + framer-motion satir animasyonlari.
 *
 * Props:
 *   config: { schemaVersion, columns, rows, labels, footer }
 *   onRowsChange: function(rows) — her degisiklikte cagirilir (vanilla JS bridge)
 *
 * Imperative API (window.CalibraHub.salesLineGrid):
 *   setRows(rows) — initial data load icin (AJAX'tan gelen lines)
 *   getRows()     — save flow icin
 */
import { useState, useCallback, useEffect, useRef, useMemo } from 'react'
import { createPortal } from 'react-dom'
import { motion, AnimatePresence } from 'framer-motion'
import {
  Plus, Trash2, Pencil, Hash, FileText, Ruler, Sigma, DollarSign,
  Percent, Calculator, StickyNote, CircleDot, Lock, Pin, PinOff,
  Settings, X as XIcon, GitBranch, History, AlertTriangle,
  MoreHorizontal, ExternalLink, ChevronRight, Tag, Barcode, Warehouse, Layers,
} from 'lucide-react'
import { Parser as ExprParser } from 'expr-eval'
import { navigateInWorkspace } from '../../utils/workspaceNav'
import LineGridCell, { CombinationLookupCell, SerialEntryModal, LotBreakdownModal, SerialBreakdownModal, TraceEntryCell } from './LineGridCell'
import CostViewerModal from './CostViewerModal'
import QuoteCostSummaryModal from './QuoteCostSummaryModal'
import FulfillmentDetailModal from './FulfillmentDetailModal'
import { evaluate } from './formulaEvaluator'
import { getTopBody } from '../../utils/topPortal'
import DynamicWidgetRenderer from '../DynamicWidgetRenderer/DynamicWidgetRenderer'
import { loadDecimalSettings, resolveColumnDecimals, roundTo, onDecimalSettingsChanged } from '../../utils/decimalSettings'

/* Lucide icon haritasi — C#'taki icon string'ini React bilesenine cevirir */
var ICON_MAP = {
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
function resolveIcon(name) {
  return ICON_MAP[name] || CircleDot
}

/* Kart etiketi renk token'i → Tailwind sinifi (light+dark). HEX saklanmaz/yazilmaz —
   tema uyumu semantik token uzerinden saglanir (LineCardLayout.LabelColor). */
/* Kart duzeni izgara cozunurlugu — server GridUnits ile ayni (48; v1'de 24'tu,
   eski kayitlari server okuma yolunda x2 olcekleyip normalize eder). */
var CARD_GRID_UNITS = 48

/* ── Form Davranış Katmanı — satır-scope kural değerlendirme (2026-08-05) ──
   Kural ifadeleri admin tarafından tanımlanır ve server'da RuleExpr süzgecinden
   geçmiştir. Fail-open: parse/eval hatası null döner (görünür + zorunlu değil).
   Davranış katmanı gizleyemeyeceği çekirdek kolonlar: */
var BEHAVIOR_LOCKED_KEYS = { materialCode: 1, quantity: 1 }

function behaviorRowScope(row) {
  function num(v) { var n = typeof v === 'number' ? v : parseFloat(String(v == null ? '' : v).replace(',', '.')); return isNaN(n) ? 0 : n }
  return {
    quantity: num(row.quantity),
    unitPrice: num(row.unitPrice),
    discountRate: num(row.discountRate),
    taxRate: num(row.taxRate),
    lineTotal: num(row.lineTotal),
    materialCode: String(row.materialCode || ''),
    unitId: String(row.unitId || ''),
    notes: String(row.notes || ''),
  }
}

function evalRowRule(expr, row) {
  if (!expr) return null
  try { return ExprParser.parse(expr).evaluate(behaviorRowScope(row)) === true }
  catch (e) { return null }
}

var CARD_LABEL_COLOR_CLS = {
  slate:   'text-slate-500 dark:text-white/45',
  indigo:  'text-indigo-600 dark:text-indigo-300',
  emerald: 'text-emerald-600 dark:text-emerald-300',
  amber:   'text-amber-600 dark:text-amber-300',
  rose:    'text-rose-600 dark:text-rose-300',
  blue:    'text-blue-600 dark:text-blue-300',
  violet:  'text-violet-600 dark:text-violet-300',
}

/* Satir icin benzersiz _uid uret (React key ve yerel takip icin) */
var uidCounter = 0
function makeUid() {
  uidCounter += 1
  return 'row-' + Date.now() + '-' + uidCounter
}

/* Her satir icin computed hucreleri hesaplayip satira gomer.
   Satir save'de ayni sekilde gonderilecektir — server yine kendi hesaplayacak.
   Kolonun precision'i (ondalik ayarindan override edilmis olabilir) hesap
   SONUCUNA uygulanir — gosterim degil, saklanan deger yuvarlanir. */
function applyComputed(row, columns) {
  var result = Object.assign({}, row)
  columns.forEach(function(col) {
    if (col.computed && col.formula) {
      var v = evaluate(col.formula, result)
      result[col.key] = (col.precision != null) ? roundTo(v, col.precision) : v
    }
  })
  return result
}

function TR_FMT(n, precision) {
  if (n == null || isNaN(n)) return '0,00'
  return Number(n).toLocaleString('tr-TR', {
    minimumFractionDigits: precision != null ? precision : 2,
    maximumFractionDigits: precision != null ? precision : 2,
  })
}

export default function CalibraLineItemsGrid(props) {
  var config = props.config || { columns: [], rows: [], labels: {}, footer: {} }
  // 2026-06-01: documentTypeCode = "alis_talebi" (İhtiyaç Kaydı) ise satir context
  // menusunden Fiyat Geçmişi + Maliyet Gör + Revize Et gizlenir — talep ic hareket;
  // fiyatlandirma teklif/siparis asamasinda olusur, revize akisi gerekmez.
  var __docTypeCode = String(config.documentTypeCode || '').toLowerCase()
  var __isPurchaseRequest = __docTypeCode === 'alis_talebi'
  // Sayım (envanter sayımı): Fiyat Geçmişi / Maliyet Gör / Revize Et satır menüsünden gizlenir —
  // sayımda yalnız miktar sayılır, fiyatlandırma/revize akışı yoktur. decimalFormCode = INVENTORY_COUNT
  // ile ayırt edilir (sayım grid config'i documentTypeCode/lineFormCode taşımaz).
  var __isInventoryCount = String(config.decimalFormCode || '').toUpperCase() === 'INVENTORY_COUNT'
    || __docTypeCode === 'sayim'
  var __hidePricingFeatures = __isPurchaseRequest || __isInventoryCount
  // 2026-06-02: Satir ek alanlari icin form code'u config'ten al — daha once
  // hardcoded 'SALES_QUOTE_LINES' idi. Ihtiyac Kaydi (alis_talebi) icin dogru
  // kod 'PURCHASE_REQUEST_LINES' — hardcoded olunca modal YANLIS form'un
  // widget'larini gosteriyor + Kaydet YANLIS form tablosuna yaziyordu
  // (gear kirmizi kaliyordu cunku backend dogru formu kontrol edip eksik
  // goruyordu). Config'ten gelmezse legacy 'SALES_QUOTE_LINES' fallback.
  var __lineFormCode = String(config.lineFormCode || 'SALES_QUOTE_LINES')
  // Kart duzeni (LineCardLayout) icin form kodu — fallback KULLANILMAZ: config
  // lineFormCode tasimayan gridlerde (orn. is emri sarf) baska formun duzeninin
  // yanlislikla uygulanmasini engeller.
  var __layoutFormCode = config.lineFormCode ? String(config.lineFormCode) : null

  // ── "Kartta Goster" widget'lari + kart duzeni (2026-08-05) ─────────────────
  //   cardWidgets  : WidgetMas.ShowOnCard=1 + inline-uyumlu tipteki kalem widget'lari.
  //                  Kart alan izgarasinda dogrudan duzenlenir; degerler row.__extras'a
  //                  yazilir (⚙ Ek Alanlar modaliyla ayni buffer → senkron kalirlar).
  //   cardLayout   : /api/line-card-layout/{formCode} → [{key, span, order, visible}].
  //                  null = varsayilan duzen (mevcut auto-fill izgara aynen korunur).
  //   canEditLayout: admin (DepartmentManager/SystemAdmin) — footer'da duzen butonu.
  var [cardWidgets, setCardWidgets] = useState([])
  var [cardLayout, setCardLayout] = useState(null)
  // Form Davranış Katmanı — kalem kolonu davranışları (key → {isVisible, isRequired,
  // defaultValue, visibleIf, requiredIf}). Kayıt yoksa null = bugünkü davranış.
  var [lineBehaviors, setLineBehaviors] = useState(null)
  // NOT: Kart Düzeni editörü grid'den kaldırıldı (2026-08-05) — düzen yönetimi
  // yalnızca Alan Yönetimi → "Kart Düzeni" üzerinden; grid düzeni yalnız UYGULAR.
  // Dar konteynerde (tablet dikey / bolunmus ekran) 24-kolon span'lar okunmaz
  // kucuklukte alan uretir — genislik esiginin altinda varsayilan auto-fill'e don.
  var [gridNarrow, setGridNarrow] = useState(false)

  // Widget → LineGridCell kolon adaptasyonu. Sadece inline-uyumlu tipler:
  // text/numeric/date/dropdown. Karmasik tipler (textarea, lookup, dosya, grid...)
  // kartta gosterilmez — ⚙ Ek Alanlar modalinde kalir.
  var widgetCardColumns = useMemo(function () {
    return cardWidgets.map(function (w) {
      var dt = String(w.dataType || '').toLowerCase()
      var col = {
        key: 'w_' + w.code,
        __isWidget: true,
        __widgetCode: w.code,
        __widgetType: dt,
        label: w.label,
        icon: 'Tag',
        required: w.isRequired === true,
      }
      if (dt === 'numeric') { col.type = 'number'; col.precision = 2 }
      else if (dt === 'dropdown') {
        col.type = 'select'
        col.options = (w.options || []).map(function (s) { return { code: s, name: s } })
      }
      else if (dt === 'date') { col.type = 'date' }
      else { col.type = 'text' }
      return col
    })
  }, [cardWidgets])

  // ── Ondalık ayarları (form bazında) — kolon precision'larını override eder ──
  // Ayar formu: config.decimalFormCode (açık bildirim) → lineFormCode fallback.
  // Yüklenene kadar C# config'indeki precision'lar geçerli kalır (görsel fark
  // en fazla ilk render'da olur; ayar gelince kolonlar + hesaplar güncellenir).
  var [decimalCfg, setDecimalCfg] = useState(null)
  useEffect(function () {
    var fc = config.decimalFormCode || config.lineFormCode || 'SALES_QUOTE_LINES'
    var alive = true
    function loadIt() {
      loadDecimalSettings(fc).then(function (dec) { if (alive) setDecimalCfg(dec) })
    }
    loadIt()
    // Ondalık Ayarları ekranında kayıt → broadcast → açık grid canlı tazelenir
    var off = onDecimalSettingsChanged(loadIt)
    return function () { alive = false; off() }
  }, [config.decimalFormCode, config.lineFormCode])

  var allColumns = useMemo(function () {
    var src = Array.isArray(config.columns) ? config.columns : []
    return src.map(function (c) {
      var out = Object.assign({}, c)
      if (decimalCfg) {
        var p = resolveColumnDecimals(c, decimalCfg)
        if (p != null) out.precision = p
      }
      // Zorunlu-pozitif dogrulama (ör. Miktar) — kolon config'i ACIKCA
      // requirePositive:true/false belirtmediyse, sayisal tip + key==='quantity'
      // kolonunda varsayilan ACIK. 'quantity' zaten bu grid'in kendi ic
      // mantiginda (handleCellChange, footer hasEmptyRow vb.) sabit/kanonik
      // anahtar olarak kullaniliyor — korukoru bir tahmin degil. decimalCfg'den
      // BAGIMSIZ cozulur: miktar dogrulamasi network round-trip'e bagli
      // olmamali, ilk render'dan itibaren aktif olmali.
      // Ileride baska bir sayisal kolonu da ayni kurala tabi tutmak icin
      // backend requirePositive:true set edebilir; kapatmak icin de
      // requirePositive:false yeterli.
      if (out.requirePositive !== true && out.requirePositive !== false) {
        var isNumericType = out.type === 'number' || out.type === 'currency' || out.type === 'percent'
        out.requirePositive = isNumericType && out.key === 'quantity'
      }
      return out
    })
  }, [config.columns, decimalCfg])
  // Kolonlari yerlesime gore ayir:
  //   - row-below  : satirin altinda (ornek: Not)
  //   - inline     : satir icinde cell olarak
  //   - action     : Islem kolonuna buton olarak (combination-lookup burada)
  var columns = allColumns.filter(function(c) {
    return c.placement !== 'row-below' && c.type !== 'combination-lookup' && c.type !== 'trace-entry'
  })
  var belowColumns = allColumns.filter(function(c) { return c.placement === 'row-below' })
  var actionLookupColumns = allColumns.filter(function(c) { return c.type === 'combination-lookup' })
  // İzlenebilirlik (Lot/Seri) — İŞLEM alanında kompakt buton; modal grid seviyesinde açılır.
  var traceColumns = allColumns.filter(function(c) { return c.type === 'trace-entry' })
  var labels = config.labels || {}
  var footer = config.footer || {}
  var onRowsChange = props.onRowsChange

  // ── Otomatik fiyat cozumu (config.pricing) ─────────────────────────────────
  //   Urun/kombinasyon secilince carinin fiyat listesine (yoksa Genel Liste)
  //   gore birim fiyat doldurulur. Cozum tamamen sunucuda (ResolveLinePrices);
  //   istemci yalnizca contactId/currencyId/tarih gonderir ("aptal bilesen").
  //   __priceAuto: satir otomatik dolduruldu → cari/doviz degisince yeniden cozulur.
  //   Elle unitPrice girilen satir priceManualRef ile dondurulur (uzerine yazilmaz).
  var pricing = config.pricing || { enabled: false }
  var rowsRef = useRef(rows)
  useEffect(function () { rowsRef.current = rows }, [rows])
  var priceManualRef = useRef({})

  // ── Silme: ekran-ortasi onay modali (PageComment Seq 1082) ──
  // Onceki surumde satir-ici geri sayim (Gmail "Undo") kullaniliyordu; kullanici
  // suresiz bekleme yerine standart silme onay modali istedi (CLAUDE.md "Silme
  // onay standardi"). deleteConfirmUid dolu ise ilgili satir icin modal acik —
  // "Sil" ile ANINDA silinir (geri sayim yok), "Vazgec"te modal kapanir.
  var [deleteConfirmUid, setDeleteConfirmUid] = useState(null)
  var deleteConfirmBtnRef = useRef(null)
  // ── Duzeltme modu per satir (kilit/unlock mantigi icin altyapi) ──
  var [editingRowUid, setEditingRowUid] = useState(null)
  // ── "Not ekle" ile acilan satirlar (row-below kolonlarini gostermek icin) ──
  var [openNoteRows, setOpenNoteRows] = useState(function() { return {} })
  // ── Satir-basi "Ek Alanlar" modali icin hedef satir ──
  //   row.id > 0 olan (kayitli) satirlar icin SALES_QUOTE_LINES formundaki
  //   dinamik alanlari DynamicWidgetRenderer ile gosterir.
  var [extrasModalRow, setExtrasModalRow] = useState(null)
  // ── İzlenebilirlik (Lot/Seri) modalı — grid seviyesinde (miktar girişinden sonra otomatik açılır) ──
  //   { row, column } — açık satır + trace kolonu. onApply satırın serials/lotBreakdown'ını günceller.
  var [traceModalRow, setTraceModalRow] = useState(null)
  // ── Zorunlu widget eksik olan satir ID'leri — ⚙ butonu rengini belirler
  //   (kirmizi = eksik, yesil = saved & OK, sky = unsaved).
  var [invalidLineIds, setInvalidLineIds] = useState(function() { return [] })
  var [shakeTick, setShakeTick] = useState(0)
  var [extrasSaving, setExtrasSaving] = useState(false)
  var [extrasToast, setExtrasToast] = useState(null) // { type: 'ok'|'err', text }
  var extrasRendererRef = useRef(null)
  // ── Revize modal — satir bazli revizyon surec destegi ──
  //   Kullanici satir aksiyon seridindeki Revize butonuna bastiginda acilir.
  //   2 sekme: "Revize Et" (yeni revize olustur) + "Gecmis Revizeler" (zincir).
  //   Yeni revize "Revize Olustur" ile eklenir; orjinal satir degismez, yeni
  //   satir revised_from_id = secili satirin id'si ile eklenir.
  var [reviseModal, setReviseModal] = useState(null) // { row, tab: 'revise'|'history', draft:{...} }

  // ── Belge para birimi (#sqCurrency'den okunur, change'de senkron) ──
  // Toplam alaninin sag tarafinda ve para birimi gosterilen yerlerde
  // kullanilir. Default TRY; programatik set sonrasi 'sq:currency' window
  // event'i ile de senkronlanir (DocumentEdit yukleyici tarafinda dispatch).
  // Seq 1077: #sqCurrency <select> value'su currency ID'dir (kod DEĞİL); ISO kodu
  // option'ın data-code attribute'unda. Eskiden el.value (ID) okunuyordu → currencySymbol
  // map'te bulunamayıp ID rakamını ("3" gibi) sembol sanıp footer'da "TOPLAM 25,00 3" gösteriyordu.
  function readCurrencyCode(el) {
    if (!el) return 'TRY'
    var opt = el.options && el.options[el.selectedIndex]
    var code = opt && opt.getAttribute && opt.getAttribute('data-code')
    if (code) return code
    var v = el.value
    if (v && isNaN(Number(v))) return v   // value zaten kod (numeric değil) ise onu kullan
    return 'TRY'
  }
  var [docCurrency, setDocCurrency] = useState(function () {
    if (typeof document === 'undefined') return 'TRY'
    return readCurrencyCode(document.getElementById('sqCurrency'))
  })
  useEffect(function () {
    var el = (typeof document !== 'undefined') ? document.getElementById('sqCurrency') : null
    function syncFromEl() {
      if (el) setDocCurrency(readCurrencyCode(el))
    }
    function onCustom(e) {
      var code = (e && e.detail && e.detail.code) || (el && el.value) || 'TRY'
      setDocCurrency(code)
    }
    if (el) el.addEventListener('change', syncFromEl)
    window.addEventListener('sq:currency', onCustom)
    // Mount'tan sonra select'in degeri (sqLoadQuote ile) sonradan setlenebilir —
    // kucuk bir polling ile ilk degeri yakala (bir kerelik).
    var attempts = 0
    var poll = setInterval(function () {
      if (attempts++ > 20) { clearInterval(poll); return }
      if (!el || !el.value) return
      var code = readCurrencyCode(el)
      if (code !== docCurrency) { setDocCurrency(code); clearInterval(poll) }
    }, 150)
    return function () {
      if (el) el.removeEventListener('change', syncFromEl)
      window.removeEventListener('sq:currency', onCustom)
      clearInterval(poll)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])
  var currencySymbol = ({ TRY: '₺', USD: '$', EUR: '€', GBP: '£' })[docCurrency] || docCurrency

  // ── Belge kuru (#sqExchangeRate) — TL karsiligi kolonlari icin kopru (Seq 1077b) ──
  // Belge dovizi TRY disiyken header'da "1 birim doviz = kac TL" girilir. Computed
  // formula'lar SATIR-LOKAL (formulaEvaluator header'a erisemez) — bu yuzden TL
  // kolonlari formula DEGIL, render-time'da bu state ile elle carpilarak hesaplanir
  // (bkz. tlCellValue). #sqExchangeRate hem kullanici elle girisiyle (oninput) hem de
  // programatik atamayla (dogrudan .value=, event FIRLATMADAN) degisebilir — TCMB
  // otomatik kur cekimi (sqFetchTcmbRate) ve mevcut teklif yuklemesi (sqLoadQuote) ikisi
  // de dogrudan .value atiyor. Bu yuzden tek-seferlik polling yetmez; input/change
  // listener + 'sq:currency' + hafif surekli poll (400ms) birlikte kullanilir.
  function readExchangeRate(el) {
    if (!el) return 1
    var v = parseFloat(el.value)
    return (isFinite(v) && v > 0) ? v : 1
  }
  var [exchangeRate, setExchangeRate] = useState(function () {
    if (typeof document === 'undefined') return 1
    return readExchangeRate(document.getElementById('sqExchangeRate'))
  })
  useEffect(function () {
    var el = (typeof document !== 'undefined') ? document.getElementById('sqExchangeRate') : null
    function sync() {
      var v = readExchangeRate(document.getElementById('sqExchangeRate'))
      setExchangeRate(function (prev) { return prev !== v ? v : prev })
    }
    if (el) { el.addEventListener('input', sync); el.addEventListener('change', sync) }
    window.addEventListener('sq:currency', sync)
    var poll = setInterval(sync, 400)
    return function () {
      if (el) { el.removeEventListener('input', sync); el.removeEventListener('change', sync) }
      window.removeEventListener('sq:currency', sync)
      clearInterval(poll)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])
  // Kur alanının okunabilir gösterimi — TR yerel ayarı, 2-6 ondalık (32 → "32,00",
  // 32,4567 → "32,4567"). Kullanıcı odaktan çıkana (blur) veya Enter'a kadar
  // exchangeRateInput serbestçe düzenlenir; commit sonrası bu format ile eşitlenir.
  function formatExchangeRateDisplay(n) {
    var x = Number(n)
    if (!isFinite(x) || x <= 0) x = 1
    return x.toLocaleString('tr-TR', { minimumFractionDigits: 2, maximumFractionDigits: 6 })
  }
  // ── Kur alanı (grid footer, PageComment Seq 1083) — #sqExchangeRate ile İKİ YÖNLÜ köprü ──
  //   Kullanıcı grid'deki "Kur" input'unu değiştirince #sqExchangeRate.value yazılır +
  //   input/change event'i FIRLATILIR (DocumentEdit'in kendi sqRecalc/TCMB dinleyicileri
  //   tetiklensin diye). #sqExchangeRate DIŞARIDAN (TCMB fetch, sqLoadQuote) değişirse
  //   yukarıdaki sync() efekti exchangeRate state'ini zaten günceller — bu useEffect de
  //   input'un gösterdiği metni o an kullanıcı yazmıyorsa yeniden formatlar.
  var [exchangeRateInput, setExchangeRateInput] = useState(function () { return formatExchangeRateDisplay(exchangeRate) })
  var exchangeRateEditingRef = useRef(false)
  useEffect(function () {
    if (exchangeRateEditingRef.current) return
    setExchangeRateInput(formatExchangeRateDisplay(exchangeRate))
  }, [exchangeRate])
  function commitExchangeRate(raw) {
    exchangeRateEditingRef.current = false
    var n = parseFloat(String(raw == null ? '' : raw).replace(',', '.'))
    if (!isFinite(n) || n <= 0) {
      setExchangeRateInput(formatExchangeRateDisplay(exchangeRate))
      return
    }
    var el = (typeof document !== 'undefined') ? document.getElementById('sqExchangeRate') : null
    if (el) {
      el.value = String(n)
      try { el.dispatchEvent(new Event('input', { bubbles: true })) } catch (_) {}
      try { el.dispatchEvent(new Event('change', { bubbles: true })) } catch (_) {}
    }
    setExchangeRate(n)
    setExchangeRateInput(formatExchangeRateDisplay(n))
  }
  // TL kolonlari (unitPriceTL/lineTotalTL, config'te tlMirror:true) yalniz belge dovizi
  // TRY disiyken gorunur — TRY belgede TL karsiligi gereksiz kolon olur.
  var showTlColumns = !!docCurrency && docCurrency !== 'TRY'
  function tlCellValue(col, row) {
    if (!col.tlMirror) return row[col.key]
    var raw = row[col.sourceKey]
    var n = typeof raw === 'number' ? raw : parseFloat(String(raw == null ? '' : raw).replace(',', '.'))
    if (isNaN(n)) return null
    return n * (exchangeRate || 1)
  }
  // TRY belgede tlMirror kolonlari header/body render'indan dusur (gecici olarak
  // ekleyip kaldirmak yerine, "columns" var zaten reassignable — bkz. yukarida tanimi).
  columns = columns.filter(function (c) { return !c.tlMirror || showTlColumns })

  // ── Form Davranış Katmanı overlay (2026-08-05) ─────────────────────────────
  //   Statik gizleme (çekirdek kolonlar hariç) + zorunluluk işareti + satır-scope
  //   kurallar (__behavior — cardItems render'ında değerlendirilir). Davranış
  //   kaydı yoksa (lineBehaviors=null) hiçbir şey değişmez.
  if (lineBehaviors) {
    columns = columns
      .filter(function (c) {
        var b = lineBehaviors[c.key]
        if (!b || BEHAVIOR_LOCKED_KEYS[c.key] || c.tlMirror) return true
        return b.isVisible !== false
      })
      .map(function (c) {
        var b = lineBehaviors[c.key]
        if (!b || c.tlMirror) return c
        var out = Object.assign({}, c)
        if (b.isRequired) out.required = true
        out.__behavior = b
        return out
      })
  }

  // ── Kart duzeni (PageComment Seq 1079, TL yerlesimi Seq 1083'te guncellendi) ──
  //   kolonlari kart bolgelerine ayir. tlMirror kolonlari (unitPriceTL/lineTotalTL)
  //   ayri alan DEGIL; kaynak alanin (unitPrice/lineTotal) HEMEN YANINDA (yatay,
  //   ayni satirda) kucuk bir TL rozeti olarak gosterilir (bkz. tlMirrorBySource +
  //   kart alan grid'i asagida — showMirror ile gridColumn:'span 2').
  //   materialCode/materialName kartin ust kimlik bolgesine, geri kalanlar
  //   (miktar/birim/fiyat/iskonto/kdv/toplam/seri vb.) alan izgarasina gider.
  //   LineGridCell'in kendisi DEGISMEDI — sadece dis yerlesim (tablo -> kart).
  var tlMirrorBySource = {}
  columns.forEach(function (c) { if (c.tlMirror && c.sourceKey) tlMirrorBySource[c.sourceKey] = c })
  var mainFieldColumns = columns.filter(function (c) { return !c.tlMirror })
  var materialCodeCol = mainFieldColumns.find(function (c) { return c.key === 'materialCode' }) || null
  var materialNameCol = mainFieldColumns.find(function (c) { return c.key === 'materialName' }) || null
  var cardBodyColumns = mainFieldColumns.filter(function (c) {
    return c !== materialCodeCol && c !== materialNameCol
  })

  // ── Kart duzeni uygulamasi (2026-08-05) ───────────────────────────────────
  //   Duzenlenebilir ogeler = kimlik kolonlari (materialCode/materialName) +
  //   sistem kolonlari (cardBodyColumns) + kartta gosterilen widget'lar.
  //   Kayitli duzen varsa: layout sirasi + span (24-kolon) + gorunurluk uygulanir
  //   ve kimlik kolonlari da ALAN IZGARASININ icinde layout'a gore cizilir
  //   (sabit kimlik bolgesi kalkar — 2026-08-05 kullanici istegi: "malzeme kodu
  //   alanini dahil yonetebilmeliyim"). Duzen yokken eski gorunum aynen korunur.
  //   Bilinmeyen key yok sayilir; duzende olmayan yeni kolon varsayilan span ile
  //   SONA eklenir (additive-safe) — kimlik kolonlari ise BASA eklenir (en kritik
  //   alan listenin sonuna gomulmesin). Zorunlu/miktar kolonlari duzenle
  //   GIZLENEMEZ (veri girisi sessizce kaybolmasin — sessiz-kirik kurali 3).
  var identityColumns = []
  if (materialCodeCol) identityColumns.push(materialCodeCol)
  if (materialNameCol) identityColumns.push(materialNameCol)
  var allCardFieldColumns = identityColumns.concat(cardBodyColumns, widgetCardColumns)
  var hasCustomLayout = !!(cardLayout && cardLayout.length > 0)
  var useCustomLayout = hasCustomLayout && !gridNarrow
  var cardItems = (function () {
    if (!hasCustomLayout) {
      // Varsayilan gorunum: kimlik kolonlari sabit bolgede cizilir, alan
      // izgarasina girmez (mevcut davranis birebir korunur).
      return cardBodyColumns.concat(widgetCardColumns).map(function (c) {
        return { col: c, span: 12, visible: true }
      })
    }
    var ordered = []
    var seen = {}
    cardLayout.forEach(function (it) {
      if (!it || !it.key || seen[it.key]) return
      var col = allCardFieldColumns.find(function (c) { return c.key === it.key })
      if (!col) return // bilinmeyen key (kolon kaldirilmis / baska belge tipi) → yok say
      seen[it.key] = true
      var vis = it.visible !== false
      // materialCode config'te required tasimasa bile veri girisinin kapisidir
      // (lookup + hasEmptyRow + guided chain) — duzenle ASLA gizlenemez.
      if (col.required || col.requirePositive || col === materialCodeCol) vis = true
      ordered.push({
        col: col,
        span: (typeof it.span === 'number' && it.span >= 1 && it.span <= CARD_GRID_UNITS) ? it.span : 12,
        visible: vis,
        // Baslik override'lari (server whitelist'ten gecmis halleri)
        label: (typeof it.label === 'string' && it.label.trim()) ? it.label.trim() : null,
        labelSize: (typeof it.labelSize === 'number' && it.labelSize >= 9 && it.labelSize <= 15) ? it.labelSize : null,
        labelWeight: (it.labelWeight === 400 || it.labelWeight === 500 || it.labelWeight === 600 || it.labelWeight === 700) ? it.labelWeight : null,
        labelColor: CARD_LABEL_COLOR_CLS[it.labelColor] ? it.labelColor : null,
        labelStyle: (it.labelStyle === 'modern' || it.labelStyle === 'inline') ? it.labelStyle : null,
      })
    })
    // Duzende olmayan kimlik kolonlari basa (materialCode once) —
    // ters iterasyon + unshift sirayi korur.
    for (var ii = identityColumns.length - 1; ii >= 0; ii--) {
      var idCol = identityColumns[ii]
      if (!seen[idCol.key]) {
        seen[idCol.key] = true
        ordered.unshift({ col: idCol, span: 16, visible: true })
      }
    }
    allCardFieldColumns.forEach(function (c) {
      if (!seen[c.key]) ordered.push({ col: c, span: 12, visible: true })
    })
    return ordered
  })()

  // ── Satir kisayol menusu (•••) ───────────────────────────
  //   Aksiyon seridinin basindaki MoreHorizontal butonuna basilinca acilan liste.
  //   Suan tek item: "Stok Kartina Git". Ileride ek ozellikler (kart bilgisi,
  //   fiyat gecmisi, barkod bas, vb.) bu listeye eklenir. Portal ile butonun
  //   altinda konumlanir, dis click veya Esc ile kapanir.
  //   State: null veya { row, pos:{top,left,width} }
  var [shortcutsMenu, setShortcutsMenu] = useState(null)
  // Maliyet Goruntuleme — kisayol menusunden acilan standart modal.
  // null veya { materialCode, configCode, quantity, materialName }
  var [costViewer, setCostViewer] = useState(null)
  // Karsilama Detayi (PageComment Seq 18) — Ihtiyac Kaydi (alis_talebi) kalem kisayol
  // menusunden acilan modal; secili satirin karsilama defteri (DocumentLineFulfillment)
  // kayitlarini gosterir. null veya { lineId, materialCode, materialName }
  var [fulfillmentDetail, setFulfillmentDetail] = useState(null)
  // Split-pane: modal body icinde solda grup listesi, sagda secili grubun alanlari.
  // DynamicWidgetRenderer her grup icin [data-dyn-group-id] karti render eder;
  // MutationObserver ile bu kartlari yakalayip grup listesini olusturuyoruz.
  // Not: Onceki tab-layout icin kullanilan extrasGroups / extrasActiveGroup
  // state'leri kaldirildi. Artik butun gruplar dikey alt alta stacked olarak
  // gorunuyor (sqe-widget-wrap CSS'i). Invalid alana tiklamada scroll-into-view
  // kullaniyoruz, grup secimi yapmiyoruz.
  var extrasBodyRef = useRef(null)

  function closeExtrasModal() {
    setExtrasModalRow(null)
    setExtrasSaving(false)
    setExtrasToast(null)
  }

  // Zorunlu ama bos alan tespit edildiginde kisa bir shake animasyonu oynatilir;
  // renderer save() sonucunda .is-invalid class'i zaten input'a ekleniyor — biz
  // ustune .cb-invalid-shake sinifini reflow ile yeniden uygulayip titreşimi
  // tetikleriz (yeniden save'de tekrar tetiklenmesi icin her seferinde kaldir-ekle).
  function shakeInvalidInputs() {
    var host = extrasBodyRef.current
    if (!host) return
    var nodes = host.querySelectorAll('.is-invalid')
    if (!nodes || nodes.length === 0) return
    nodes.forEach(function(el) {
      el.classList.remove('cb-invalid-shake')
      // reflow — animasyonu yeniden baslat
      void el.offsetWidth
      el.classList.add('cb-invalid-shake')
    })
    setTimeout(function() {
      nodes.forEach(function(el) { el.classList.remove('cb-invalid-shake') })
    }, 500)
  }

  async function handleExtrasSave() {
    if (!extrasModalRow || !extrasRendererRef.current) return
    setExtrasSaving(true)
    setExtrasToast(null)
    try {
      var savedLineId = extrasModalRow.id != null && Number(extrasModalRow.id) > 0 ? Number(extrasModalRow.id) : null
      // 2026-06-01 diagnostic: hangi yola gidildigini + valuesRef icerigini yaz
      try {
        var __dbgValues = extrasRendererRef.current.getValues ? extrasRendererRef.current.getValues() : '(no getValues)'
        console.log('[CL-EXTRAS] handleExtrasSave', {
          savedLineId: savedLineId,
          path: savedLineId == null ? 'local-pending' : 'backend',
          rowUid: extrasModalRow._uid,
          rowMaterialCode: extrasModalRow.materialCode,
          getValuesSnapshot: __dbgValues,
        })
      } catch (_) {}
      // Kaydedilmemis satirda backend'e gitmiyoruz — validate edip degerleri
      // row.__extras'a local olarak yaziyoruz. Ana sqSave satirlari kaydedip
      // id aldiktan sonra widget API'siyle senkron eder.
      if (savedLineId == null) {
        var v = extrasRendererRef.current.validate()
        if (!v.valid) {
          var firstLabel = (v.errors && v.errors[0]) || 'Zorunlu alan bos'
          setExtrasToast({ type: 'err', text: 'Zorunlu alanlar bos: ' + (v.errors || []).join(', ') })
          // Renderer.validate() saveAttemptErrors state'ini set etmiyor — save() ediyor.
          // Gorsel shake icin save'i cagirip hata donmesini bekleyelim.
          var forcedResult = await extrasRendererRef.current.save({ recordId: '__pending__' })
          // recordId olsa da bizim local yol oldugu icin sonucun success'ini umursamiyoruz;
          // save() en azindan is-invalid class'ini widget input'larina ekliyor.
          void forcedResult
          setTimeout(shakeInvalidInputs, 30)
          // Alt alta stacked layout'ta grup tab'i yok — direkt hatali alana kaydir.
          setTimeout(function() {
            var host = extrasBodyRef.current
            if (!host) return
            var firstInvalid = host.querySelector('.is-invalid')
            if (!firstInvalid) return
            try { firstInvalid.scrollIntoView({ behavior: 'smooth', block: 'center' }) }
            catch (_) { firstInvalid.scrollIntoView() }
          }, 40)
          return
        }
        // Gecerli — degerleri row.__extras'a yaz, modali kapat.
        var localValues = extrasRendererRef.current.getValues() || {}
        console.log('[CL-EXTRAS] local path → row.__extras yaziliyor', {
          uid: extrasModalRow._uid,
          values: localValues,
          keyCount: Object.keys(localValues).length,
        })
        if (Object.keys(localValues).length === 0) {
          console.warn('[CL-EXTRAS] UYARI: getValues bos dondu — varsayilan degerler valuesRef\'e gecmemis olabilir')
        }
        setRows(function(prev) {
          return prev.map(function(r) {
            if (r._uid !== extrasModalRow._uid) return r
            // MERGE — modal artik kartta gosterilen alanlari icermedigi icin
            // localValues'i oldugu gibi yazmak karttaki inline edit'leri silerdi.
            return Object.assign({}, r, { __extras: Object.assign({}, r.__extras || {}, localValues) })
          })
        })
        setExtrasToast({ type: 'ok', text: 'Ek alanlar hazir — satiri Kaydet ile kesinlestirin' })
        setTimeout(function() { closeExtrasModal() }, 650)
        return
      }

      // Kaydedilmis satir — mevcut backend save akisi.
      console.log('[CL-EXTRAS] backend path — save() cagrisi', { savedLineId: savedLineId })
      var result = await extrasRendererRef.current.save({ recordId: String(savedLineId) })
      console.log('[CL-EXTRAS] backend path — save() result', result)
      if (result && result.success === false) {
        console.warn('[CL-EXTRAS] backend save FAIL — gear KIRMIZI kalacak', {
          savedLineId: savedLineId,
          message: result.message,
          requiredErrors: result.requiredErrors,
        })
        setExtrasToast({ type: 'err', text: result.message || 'Kayit basarisiz.' })
        // Eksik zorunlu alan varsa kirmizi shake — .is-invalid DOM'a islenene kadar
        // minik bir gecikme; React render sonrasi class'lar yerinde olur.
        if (result.requiredErrors && result.requiredErrors.length > 0) {
          setTimeout(shakeInvalidInputs, 30)
          // Alt alta stacked layout'ta grup tab'i yok — direkt hatali alana kaydir.
          setTimeout(function() {
            var host = extrasBodyRef.current
            if (!host) return
            var firstInvalid = host.querySelector('.is-invalid')
            if (!firstInvalid) return
            try { firstInvalid.scrollIntoView({ behavior: 'smooth', block: 'center' }) }
            catch (_) { firstInvalid.scrollIntoView() }
          }, 40)
        }
      } else {
        console.log('[CL-EXTRAS] backend save OK — invalidLineIds\'den ' + savedLineId + ' cikarilıyor')
        setExtrasToast({ type: 'ok', text: 'Kaydedildi' })
        // Bu satirin widget'lari dolmus olabilir — invalid listesinden cikar (yesile dons).
        setInvalidLineIds(function(prev) {
          var next = prev.filter(function(x) { return x !== savedLineId })
          console.log('[CL-EXTRAS] invalidLineIds: ' + JSON.stringify(prev) + ' → ' + JSON.stringify(next))
          return next
        })
        // __extras varsa temizle — artik backend source of truth
        setRows(function(prev) {
          return prev.map(function(r) {
            if (r._uid !== extrasModalRow._uid || !r.__extras) return r
            var copy = Object.assign({}, r)
            delete copy.__extras
            return copy
          })
        })
        setTimeout(function() { closeExtrasModal() }, 650)
      }
    } catch (e) {
      setExtrasToast({ type: 'err', text: 'Hata: ' + (e && e.message ? e.message : String(e)) })
    } finally {
      setExtrasSaving(false)
    }
  }

  // ── State: satirlar ──
  var [rows, setRows] = useState(function() {
    return (config.rows || []).map(function(r) {
      return applyComputed(Object.assign({ _uid: makeUid() }, r), allColumns)
    })
  })

  // Dis tarafa her degisiklikte notify (bridge)
  useEffect(function() {
    if (typeof onRowsChange === 'function') {
      // _uid bridge'in disina sizmasin
      var clean = rows.map(function(r) {
        var copy = Object.assign({}, r)
        delete copy._uid
        return copy
      })
      onRowsChange(clean)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [rows])

  // Extras modal acikken: DynamicWidgetRenderer'in render ettigi grup kartlarini
  // (data-dyn-group-id) izle ve sol panele tab listesi olarak yansit. Kartlar
  // shakeTick degistiginde invalidLineIds'deki satirlarin ⚙ butonlarina
  // 'cb-invalid-shake' class'i ekle — 600ms sonra kaldir.
  useEffect(function() {
    if (shakeTick === 0) return
    var selectors = invalidLineIds.map(function(id) { return '[data-extras-line-id="' + id + '"]' })
    if (selectors.length === 0) return
    var els = document.querySelectorAll(selectors.join(','))
    els.forEach(function(el) {
      el.classList.remove('cb-invalid-shake')
      // reflow
      void el.offsetWidth
      el.classList.add('cb-invalid-shake')
    })
    var timer = setTimeout(function() {
      els.forEach(function(el) { el.classList.remove('cb-invalid-shake') })
    }, 650)
    return function() { clearTimeout(timer) }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [shakeTick])

  /* ── Imperative API (vanilla JS bridge) ──
     window.CalibraHub.salesLineGrid.{setRows,getRows}
     setRows ile yuklenen satirlar _locked:true isaretlenir — kullanici
     Duzelt butonuna basmadan hucreler ve Sil butonu pasif kalir. */
  useEffect(function() {
    var api = {
      setRows: function(newRows) {
        // _uid korumasi: ayni satiri yeniden eslestirerek AnimatePresence'in exit+enter
        // animasyonuyla gorunum karisikligina yol acmasini engelle. Siralama:
        //   1) line.Id eslesmesi (UPDATE edilmis var olan satirlar)
        //   2) Pozisyon (index) eslesmesi (sadece fresh INSERT sonrasi id gelsin diye)
        //   Her iki yontem uymazsa yeni _uid uretilir (gercekten yeni satir).
        setRows(function(prevRows) {
          var prevById = {}
          prevRows.forEach(function(pr) {
            if (pr.id != null && pr.id !== '' && Number(pr.id) > 0) {
              prevById[String(pr.id)] = pr
            }
          })
          var usedUids = Object.create(null)
          var nextArr = (newRows || []).map(function(r, idx) {
            var idKey = r.id != null && r.id !== '' && Number(r.id) > 0 ? String(r.id) : null
            var existing = idKey ? prevById[idKey] : null
            // ID match yoksa pozisyona gore prev'i al (ayni index)
            if (!existing && idx < prevRows.length) {
              var posMatch = prevRows[idx]
              if (posMatch && !usedUids[posMatch._uid]) existing = posMatch
            }
            var uid = existing ? existing._uid : makeUid()
            usedUids[uid] = true
            return applyComputed(Object.assign({ _uid: uid, _locked: true }, r), allColumns)
          })
          return nextArr
        })
      },
      getRows: function() {
        return rows.map(function(r) {
          var copy = Object.assign({}, r)
          delete copy._uid
          delete copy._locked
          return copy
        })
      },
      // Kayit yanitindaki satirlari (LineNo sirasinda) grid'in dolu satirlarina
      // pozisyonel eslestirip YALNIZCA id alanini yazar. setRows'tan farki:
      // kullanicinin devam eden duzenlemelerini ezmez, _locked durumunu degistirmez.
      // Id'siz kalan satir bir sonraki kayitta DELETE+INSERT edilir ve satir bazli
      // widget (WidgetTra) kayitlari orphan kalir — bu merge o acigi kapatir.
      // Dolu satir predicate'i sqSave'in rowsFilled filtresiyle ayni olmali
      // (materialCode || materialName) — payload'a giden siralama korunur.
      mergeSavedLineIds: function(savedLines) {
        var arr = Array.isArray(savedLines) ? savedLines : []
        if (arr.length === 0) return
        setRows(function(prev) {
          var cursor = 0
          return prev.map(function(r) {
            if (!r || !(r.materialCode || r.materialName)) return r
            if (cursor >= arr.length) return r
            var sl = arr[cursor]
            cursor++
            var hasId = r.id != null && r.id !== '' && Number(r.id) > 0
            if (hasId || !sl || !(Number(sl.id) > 0)) return r
            // Guvenlik: ucus sirasinda satir degistiyse yanlis Id yazmamak icin
            // itemId tutarliligi aranir — uymazsa satir Id'siz birakilir.
            var slItem  = sl.itemId != null ? Number(sl.itemId) : null
            var rowItem = r.stockCardId != null && r.stockCardId !== '' ? Number(r.stockCardId)
                        : (r.itemId != null && r.itemId !== '' ? Number(r.itemId) : null)
            if (slItem == null || rowItem == null || slItem !== rowItem) return r
            return Object.assign({}, r, { id: Number(sl.id) })
          })
        })
      },
      // Satirlari KILITLEMEDEN in-place guncelle (header -> satir varsayilan lokasyon gibi).
      // setRows'tan farki: _locked:true BASMAZ → kullanicinin aktif duzenlemesini bozmaz.
      // mapFn(row) => yeni obje (degisiklik) VEYA null (o satira dokunma).
      patchRows: function(mapFn) {
        if (typeof mapFn !== 'function') return
        setRows(function(prev) {
          return prev.map(function(r) { var p = mapFn(r); return p ? applyComputed(p, allColumns) : r })
        })
      },
      // Satirlardaki eksik zorunlu widget state'i — ⚙ rengini kirmizi yapar.
      setInvalidLines: function(ids) {
        var arr = Array.isArray(ids) ? ids.map(function(n) { return Number(n) }).filter(function(n) { return n > 0 }) : []
        setInvalidLineIds(arr)
      },
      // Listeyi set et + kirmizilari titrett.
      flashInvalidLines: function(ids) {
        var arr = Array.isArray(ids) ? ids.map(function(n) { return Number(n) }).filter(function(n) { return n > 0 }) : []
        setInvalidLineIds(arr)
        setShakeTick(function(t) { return t + 1 })
      },
    }
    window.CalibraHub = window.CalibraHub || {}
    window.CalibraHub.salesLineGrid = api
    window.CalibraHub.whLineGrid = api   // Ambar/Sayım ekranları bu adı kullanır (alias — aynı API)
    return function() {
      if (window.CalibraHub && window.CalibraHub.salesLineGrid === api) window.CalibraHub.salesLineGrid = null
      if (window.CalibraHub && window.CalibraHub.whLineGrid === api) window.CalibraHub.whLineGrid = null
    }
  }, [rows, columns])

  // Belge context'ini DOM'dan oku (contactId / currencyId / tarih). currencyId
  // ID (int) — save ile ayni kaynak (#sqCurrency.value = currencies.id). docCurrency
  // state KOD gosterir; burada kullanilmaz.
  function readPricingContext() {
    var ctx = pricing.context || {}
    function val(elId) {
      var el = elId && typeof document !== 'undefined' ? document.getElementById(elId) : null
      return el ? el.value : ''
    }
    var contactRaw = val(ctx.contactElId)
    return {
      contactId:  contactRaw ? (parseInt(contactRaw, 10) || null) : null,
      currencyId: parseInt(val(ctx.currencyElId), 10) || 0,
      validOn:    val(ctx.dateElId) || '',
    }
  }

  // Batch fiyat cozumu → { "itemId:configId" : price } map (fiyat bulunmayan key yok).
  function fetchResolvePrices(keys, ctx) {
    var body = {
      contactId:  ctx.contactId,
      currencyId: ctx.currencyId,
      validOn:    ctx.validOn,
      direction:  pricing.direction || 's',
      keys:       keys.map(function (k) { return { itemId: k.itemId, configId: k.configId != null ? k.configId : null } }),
    }
    var token = (typeof document !== 'undefined'
      ? (document.querySelector('input[name="__RequestVerificationToken"]') || {}).value : '') || ''
    var headers = { 'Content-Type': 'application/json', 'Accept': 'application/json' }
    if (token) headers['RequestVerificationToken'] = token
    return fetch(pricing.resolveUrl, {
      method: 'POST', credentials: 'same-origin', headers: headers, body: JSON.stringify(body),
    })
      .then(function (r) { return r.ok ? r.json() : [] })
      .then(function (list) {
        var map = {}
        ;(list || []).forEach(function (row) {
          if (row == null || row.price == null) return
          var cfg = row.configId != null ? Number(row.configId) : ''
          map[Number(row.itemId) + ':' + cfg] = Number(row.price)
        })
        return map
      })
      .catch(function () { return {} })
  }

  // Onceki satir + yeni patch → guncel satir (itemId/configId cikarimi icin).
  function mergedRow(rowUid, columnKey, newValue, fillPatch) {
    var base = (rowsRef.current || []).find(function (r) { return r._uid === rowUid }) || {}
    var m = Object.assign({}, base)
    m[columnKey] = newValue
    if (fillPatch) Object.keys(fillPatch).forEach(function (k) { m[k] = fillPatch[k] })
    return m
  }

  // Tek satirin fiyatini coz + yaz. forceBaseConfig=true → yeni urun secildi,
  // kombinasyon henuz yok → base (configId=null) fiyat.
  var resolveAndApplyPrice = useCallback(function (rowUid, row, forceBaseConfig) {
    if (!pricing.enabled || priceManualRef.current[rowUid]) return
    var itemId = Number(row[pricing.itemKey] || row.stockCardId || row.itemId || 0)
    if (!(itemId > 0)) return
    var configId = forceBaseConfig ? null : (row[pricing.configKey] ? Number(row[pricing.configKey]) : null)
    var ctx = readPricingContext()
    if (!(ctx.currencyId > 0)) return
    fetchResolvePrices([{ itemId: itemId, configId: configId }], ctx).then(function (map) {
      var hit = map[itemId + ':' + (configId != null ? configId : '')]
      if (hit == null) return
      setRows(function (prev) {
        return prev.map(function (r) {
          if (r._uid !== rowUid || priceManualRef.current[rowUid]) return r
          var nr = Object.assign({}, r); nr[pricing.targetKey] = hit; nr.__priceAuto = true
          return applyComputed(nr, allColumns)
        })
      })
    })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [allColumns])

  // Tum otomatik-doldurulmus (elle degistirilmemis) satirlari yeniden coz —
  // cari/doviz/tarih degisince. Yuklenen mevcut belge satirlari (__priceAuto yok)
  // ve elle girilenler dokunulmaz → tarihi fiyat korunur.
  var resolveAllAutoRows = useCallback(function () {
    if (!pricing.enabled) return
    var ctx = readPricingContext()
    if (!(ctx.currencyId > 0)) return
    var targets = (rowsRef.current || []).filter(function (r) {
      return r.__priceAuto === true && !priceManualRef.current[r._uid] &&
             Number(r[pricing.itemKey] || r.stockCardId || 0) > 0
    })
    if (targets.length === 0) return
    var keys = targets.map(function (r) {
      return {
        itemId:   Number(r[pricing.itemKey] || r.stockCardId),
        configId: r[pricing.configKey] ? Number(r[pricing.configKey]) : null,
      }
    })
    fetchResolvePrices(keys, ctx).then(function (map) {
      setRows(function (prev) {
        return prev.map(function (r) {
          if (r.__priceAuto !== true || priceManualRef.current[r._uid]) return r
          var itemId = Number(r[pricing.itemKey] || r.stockCardId || 0)
          if (!(itemId > 0)) return r
          var configId = r[pricing.configKey] ? Number(r[pricing.configKey]) : null
          var hit = map[itemId + ':' + (configId != null ? configId : '')]
          if (hit == null) return r
          var nr = Object.assign({}, r); nr[pricing.targetKey] = hit
          return applyComputed(nr, allColumns)
        })
      })
    })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [allColumns])

  // Cari / doviz / belge tarihi degisince otomatik satirlari yeniden coz.
  //   - Cari guide secimi (#sqCustomerId) fillTargets ile 'change' yayar → yakalanir.
  //   - Cari kod-lookup + doviz set 'sq:contact'/'sq:currency' window event'i yayar.
  //   - Native 'change' (doviz select, tarih input) de dinlenir.
  useEffect(function () {
    if (!pricing.enabled) return undefined
    var ctx = pricing.context || {}
    function el(id) { return id && typeof document !== 'undefined' ? document.getElementById(id) : null }
    var dateEl = el(ctx.dateElId), currencyEl = el(ctx.currencyElId), contactEl = el(ctx.contactElId)
    function onChange() { resolveAllAutoRows() }
    window.addEventListener('sq:currency', onChange)
    window.addEventListener('sq:contact', onChange)
    if (dateEl)     dateEl.addEventListener('change', onChange)
    if (currencyEl) currencyEl.addEventListener('change', onChange)
    if (contactEl)  contactEl.addEventListener('change', onChange)
    return function () {
      window.removeEventListener('sq:currency', onChange)
      window.removeEventListener('sq:contact', onChange)
      if (dateEl)     dateEl.removeEventListener('change', onChange)
      if (currencyEl) currencyEl.removeEventListener('change', onChange)
      if (contactEl)  contactEl.removeEventListener('change', onChange)
    }
  }, [pricing.enabled, resolveAllAutoRows])

  // ── Hucre degisikligi ──
  var handleCellChange = useCallback(function(rowUid, columnKey, newValue, fillPatch) {
    var autoOpenRow = null
    var lockWarn = null
    function _num(x) { if (x == null || x === '') return null; var n = parseFloat(String(x).replace(',', '.')); return isNaN(n) ? null : n }
    setRows(function(prev) {
      return prev.map(function(r) {
        if (r._uid !== rowUid) return r
        var next = Object.assign({}, r)
        next[columnKey] = newValue
        if (fillPatch) {
          Object.keys(fillPatch).forEach(function(k) { next[k] = fillPatch[k] })
        }
        // Bağlantı tabanı: karşılanmış / türetilmiş (bağlantılı) satırın miktarı, taahhüt
        // edilen tabanın (__minQty) altına düşürülemez. Blur commit'inde tabana sabitlenir
        // + uyarı gösterilir. SaveQuoteAsync aynı tabanı sunucuda da zorlar.
        if (columnKey === 'quantity') {
          var minQ = _num(next.__minQty)
          var newQ = _num(next.quantity)
          if (minQ != null && minQ > 0 && newQ != null && newQ < minQ) {
            next.quantity = minQ
            lockWarn = { name: next.materialName || next.materialCode || 'Kalem', min: minQ }
          }
        }
        // İzlenebilirlik: malzeme seçilince (trackSerial/trackLot geldi) miktar varsayılan 1 +
        // seri/lot ekranı açılsın; miktar girişinden (blur) sonra da açılsın (düzeltmede tekrar).
        if (traceColumns.length > 0) {
          var justPickedTrace = fillPatch && (fillPatch.trackSerial === true || fillPatch.trackLot === true)
          if (justPickedTrace) {
            var q0 = _num(next.quantity)
            if (q0 == null || q0 === 0) next.quantity = 1
          }
          var computed0 = applyComputed(next, allColumns)
          var traceable = computed0.trackSerial === true || computed0.trackLot === true
          var q = _num(computed0.quantity)
          if (traceable && q != null && q > 0 && (justPickedTrace || columnKey === 'quantity')) {
            autoOpenRow = computed0
          }
          return computed0
        }
        return applyComputed(next, allColumns)
      })
    })
    if (lockWarn) {
      var lm = "'" + lockWarn.name + "' bağlantılı olduğu için miktarı " + TR_FMT(lockWarn.min, null) + " altına düşürülemez."
      if (window.CalibraHub && window.CalibraHub.toast) window.CalibraHub.toast(lm, 'warn')
      else if (window.CalibraAlert && window.CalibraAlert.warn) window.CalibraAlert.warn(lm)
    }
    if (autoOpenRow && traceColumns.length > 0) setTraceModalRow({ row: autoOpenRow, column: traceColumns[0] })

    // Otomatik fiyat: urun secilince base fiyat, kombinasyon secilince varyant fiyat;
    // elle unitPrice girilince o satiri dondur (bir daha otomatik yazma).
    if (pricing.enabled) {
      if (columnKey === pricing.targetKey) {
        priceManualRef.current[rowUid] = true
      } else if (columnKey === 'materialCode') {
        priceManualRef.current[rowUid] = false
        resolveAndApplyPrice(rowUid, mergedRow(rowUid, columnKey, newValue, fillPatch), true)
      } else if (columnKey === 'combinationCode' || columnKey === pricing.configKey) {
        resolveAndApplyPrice(rowUid, mergedRow(rowUid, columnKey, newValue, fillPatch), false)
      }
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [allColumns, traceColumns, resolveAndApplyPrice])

  // ── Yeni satir ekle ──
  // Guided workflow: satir eklendikten sonra stok rehberi otomatik acilir.
  // Stok secilince, o malzeme kombinasyon takipli ise kombinasyon modal'i
  // acilir; kombinasyon secilince (ya da malzeme combo izlemeyen ise)
  // ek alanlar (SALES_QUOTE_LINES widget'lari) modal'i acilir. Chain global
  // event "lineGrid:autoOpenStage" uzerinden yurur — dispatcherler:
  // handleAddRow (material) → handlePick (combo/extras) → CombinationLookupCell
  // onApply (extras) → grid useEffect (extras modal open).
  function handleAddRow() {
    var newUid = makeUid()
    setRows(function(prev) {
      var blank = { _uid: newUid }
      allColumns.forEach(function(c) {
        if (c.type === 'number' || c.type === 'currency' || c.type === 'percent') {
          blank[c.key] = 0
        } else {
          blank[c.key] = ''
        }
      })
      // Form Davranış Katmanı — kolon varsayılan değerleri (yeni satırda).
      if (lineBehaviors) {
        Object.keys(lineBehaviors).forEach(function (k) {
          var b = lineBehaviors[k]
          if (!b.defaultValue) return
          var col = allColumns.find(function (c) { return c.key === k })
          if (!col) return
          var isNum = col.type === 'number' || col.type === 'currency' || col.type === 'percent'
          blank[k] = isNum
            ? (parseFloat(String(b.defaultValue).replace(',', '.')) || 0)
            : b.defaultValue
        })
      }
      return prev.concat([applyComputed(blank, allColumns)])
    })
    // React state commit + cell mount sonrasinda listener'lar hazir olsun diye kisa gecikme.
    // requestAnimationFrame 1 frame bekler; bu surede useEffect(mount) tetiklenmis olur.
    requestAnimationFrame(function () {
      requestAnimationFrame(function () {
        try {
          window.dispatchEvent(new CustomEvent('lineGrid:autoOpenStage', {
            detail: { rowUid: newUid, stage: 'material' }
          }))
        } catch (_) { /* older browsers: no-op */ }
      })
    })
  }

  // ── Satir sil — ekran-ortasi onay modali (PageComment Seq 1082) ──
  // Sil butonu once onay modalini acar (performDeleteRow HENUZ cagrilmaz).
  function handleDeleteRow(rowUid) {
    setDeleteConfirmUid(rowUid)
  }
  // Gercek silme islemi — modalda "Sil" onaylandiginda ANINDA calisir.
  // ONEMLI: Silinen satir aktif (revisedFromId=null) satirsa, ona isaret eden
  // eski revizyon satirlari da zincir boyunca silinmeli. Aksi halde kayit
  // silinince eski surum gorunur hale gelir. Save sirasinda da getRows()
  // listesinden cikan tum id'ler backend tarafindan DELETE edilir.
  function performDeleteRow(rowUid) {
    setRows(function (prev) {
      var target = prev.find(function (r) { return r._uid === rowUid })
      if (!target) return prev
      // Zinciri BFS ile topla: hedef + hedefin id'sine isaret eden eski surumleri bul
      var removeUids = {}
      removeUids[target._uid] = true
      var queue = [target]
      var guard = 0
      while (queue.length > 0 && guard < 50) {
        var cur = queue.shift()
        guard++
        if (!cur.id || Number(cur.id) <= 0) continue
        var curId = Number(cur.id)
        prev.forEach(function (r) {
          if (r.revisedFromId != null && Number(r.revisedFromId) === curId && !removeUids[r._uid]) {
            removeUids[r._uid] = true
            queue.push(r)
          }
        })
      }
      return prev.filter(function (r) { return !removeUids[r._uid] })
    })
  }
  // Silme onay modali acikken Esc = vazgec, Enter = sil (odak modal disinda
  // olsa bile calisir — shortcutsMenu Esc deseniyle tutarli).
  useEffect(function () {
    if (!deleteConfirmUid) return undefined
    function onKey(e) {
      if (e.key === 'Escape') { e.preventDefault(); setDeleteConfirmUid(null) }
      else if (e.key === 'Enter') { e.preventDefault(); performDeleteRow(deleteConfirmUid); setDeleteConfirmUid(null) }
    }
    document.addEventListener('keydown', onKey)
    return function () { document.removeEventListener('keydown', onKey) }
  }, [deleteConfirmUid])
  // Modal acilinca "Sil" butonuna odak (silme onay standardi: Ok butonu varsayilan focus'ta)
  useEffect(function () {
    if (deleteConfirmUid && deleteConfirmBtnRef.current) deleteConfirmBtnRef.current.focus()
  }, [deleteConfirmUid])

  // Shortcuts menu — Esc kapatir, scroll pozisyondan ayrilma sorunu yasatmamak
  // icin scroll'da da kapatilir (yeniden konumlamaktansa kapatmak daha tutarli).
  useEffect(function () {
    if (!shortcutsMenu) return undefined
    function onKey(e) { if (e.key === 'Escape') setShortcutsMenu(null) }
    function onScroll() { setShortcutsMenu(null) }
    document.addEventListener('keydown', onKey)
    window.addEventListener('scroll', onScroll, true)
    return function () {
      document.removeEventListener('keydown', onKey)
      window.removeEventListener('scroll', onScroll, true)
    }
  }, [shortcutsMenu])

  // ── Auto-chain: guided new-line workflow ──
  // handleAddRow -> 'material' lookup -> (trackCombinations ise) 'combo' ->
  // 'extras' (⚙ ek alanlar modali). Material/combo asamalarini ilgili cell
  // kendisi dinleyip acar; 'extras' asamasi GRID seviyesinde handle edilir
  // cunku extrasModalRow state'i burada. Rows degistikce ref'i guncel tut.
  var rowsRef = useRef(rows)
  useEffect(function () { rowsRef.current = rows }, [rows])

  // SALES_QUOTE_LINES formundaki zorunlu widget durumu — chain sonunda extras
  // modal'i SADECE zorunlu alan varsa otomatik acilir. Yoksa kullanici akistan
  // takilmadan satira devam eder; ihtiyaci olursa ⚙ butonuyla manuel acabilir.
  // Schema bir kez yuklenir ve cache'lenir (ref ile listener'a senkron tutulur).
  var hasRequiredLineWidgetsRef = useRef(false)
  useEffect(function () {
    var alive = true
    function loadSchema() {
      fetch('/api/widgets/forms/' + encodeURIComponent(__lineFormCode) + '/schema', { credentials: 'same-origin' })
        .then(function (r) { return r.ok ? r.json() : null })
        .then(function (schema) {
          if (!alive || !schema || !Array.isArray(schema.widgets)) return
          // ── "Kartta Goster" widget'lari (2026-08-05) ──
          // ShowOnCard=1 + aktif + inline-uyumlu tip → kart alan izgarasina girer.
          var INLINE_TYPES = { text: 1, numeric: 1, date: 1, dropdown: 1 }
          var cw = schema.widgets
            .filter(function (w) {
              if (!w) return false
              var show = w.showOnCard === true || w.ShowOnCard === true
              var active = w.isActive !== false && w.IsActive !== false
              var dt = String(w.dataType || w.DataType || '').toLowerCase()
              return show && active && INLINE_TYPES[dt] === 1
            })
            .map(function (w) {
              return {
                code: w.widgetCode || w.WidgetCode,
                label: w.label || w.Label || w.widgetCode,
                dataType: String(w.dataType || w.DataType || 'text').toLowerCase(),
                options: Array.isArray(w.options) ? w.options : (Array.isArray(w.Options) ? w.Options : []),
                isRequired: w.isRequired === true || w.IsRequired === true,
                sortOrder: w.sortOrder != null ? w.sortOrder : (w.SortOrder != null ? w.SortOrder : 0),
              }
            })
            .sort(function (a, b) { return (a.sortOrder || 0) - (b.sortOrder || 0) })
          setCardWidgets(cw)
          // Guided-chain otomatik ⚙ acilisi: yalnizca MODALDA kalan zorunlu widget
          // varsa gerekir — kartta inline gosterilen zorunlu alan karttan doldurulur.
          // (ASP.NET Core JSON camelCase'e cevirir; Pascal fallback'i de dusuruyoruz.)
          var cwSet = {}
          cw.forEach(function (c) { cwSet[String(c.code).toLowerCase()] = 1 })
          hasRequiredLineWidgetsRef.current = schema.widgets.some(function (w) {
            if (!w || !(w.isRequired === true || w.IsRequired === true)) return false
            var code = String(w.widgetCode || w.WidgetCode || '').toLowerCase()
            return !cwSet[code]
          })
        })
        .catch(function () { /* sessiz — schema yoksa otomatik acma yapma */ })
    }
    loadSchema()
    // Alan Yonetimi'nde tanim degisince canli tazele (ayni sekmede CustomEvent,
    // diger sekmede storage event'i).
    function onSchemaChanged() { loadSchema() }
    function onStorage(e) { if (e && e.key === 'calibra:widget-schema-changed') loadSchema() }
    window.addEventListener('calibra:widget-schema-changed', onSchemaChanged)
    window.addEventListener('storage', onStorage)
    return function () {
      alive = false
      window.removeEventListener('calibra:widget-schema-changed', onSchemaChanged)
      window.removeEventListener('storage', onStorage)
    }
  }, [])

  // ── Kart duzeni yukle (form bazli, herkese ortak) ──
  useEffect(function () {
    if (!__layoutFormCode) return undefined
    var alive = true
    fetch('/api/line-card-layout/' + encodeURIComponent(__layoutFormCode), { credentials: 'same-origin' })
      .then(function (r) { return r.ok ? r.json() : null })
      .then(function (data) {
        if (!alive || !data || data.ok !== true) return
        setCardLayout(Array.isArray(data.items) && data.items.length > 0 ? data.items : null)
      })
      .catch(function () { /* sessiz — duzen yoksa varsayilan izgara */ })
    return function () { alive = false }
  }, [__layoutFormCode])

  // ── Form Davranış Katmanı — kalem kolonu davranışları (2026-08-05) ──
  //   Varsayılandan farklı davranışı olan kolonlar döner; fail-open: istek
  //   düşerse / kayıt yoksa davranış katmanı hiç devreye girmez.
  useEffect(function () {
    if (!__layoutFormCode) return undefined
    var alive = true
    fetch('/api/form-behavior/' + encodeURIComponent(__layoutFormCode), { credentials: 'same-origin' })
      .then(function (r) { return r.ok ? r.json() : null })
      .then(function (data) {
        if (!alive || !data || data.ok !== true || !Array.isArray(data.fields)) return
        var map = {}
        var any = false
        data.fields.forEach(function (f) {
          var hasBehavior = f.isVisible === false || f.isRequired === true
            || f.defaultValue || f.visibleIf || f.requiredIf
          if (!hasBehavior) return
          any = true
          map[f.key] = {
            isVisible: f.isVisible !== false,
            isRequired: f.isRequired === true,
            defaultValue: f.defaultValue || null,
            visibleIf: f.visibleIf || null,
            requiredIf: f.requiredIf || null,
          }
        })
        setLineBehaviors(any ? map : null)
      })
      .catch(function () { /* sessiz — fail-open */ })
    return function () { alive = false }
  }, [__layoutFormCode])

  // ── Dar konteyner tespiti — 640px altinda custom span'lar devre disi ──
  useEffect(function () {
    var el = gridRootRef.current
    if (!el || typeof ResizeObserver === 'undefined') return undefined
    var ro = new ResizeObserver(function (entries) {
      var w = entries && entries[0] && entries[0].contentRect ? entries[0].contentRect.width : 0
      setGridNarrow(w > 0 && w < 640)
    })
    ro.observe(el)
    return function () { ro.disconnect() }
  }, [])

  // ── Kayitli satirlarin widget degerlerini TEK istekle yukle ──
  //   __widgetValues: server'daki mevcut degerler (source of truth gosterim icin;
  //   __extras varsa o kazanir — henuz kaydedilmemis kullanici girisi).
  //   Yukleme isareti satirin KENDISINDE (__widgetValues != null) tutulur; boylece
  //   dis setRows ile satirlar topluca degistirildiginde (save sonrasi refresh)
  //   deger yuklemesi otomatik tekrarlanir. Istek atilan id'ye deger donmese bile
  //   bos obje yazilir — sonsuz refetch dongusu olusmaz.
  var widgetValuesFetchingRef = useRef(false)
  useEffect(function () {
    if (cardWidgets.length === 0 || widgetValuesFetchingRef.current) return
    var missingIds = []
    rows.forEach(function (r) {
      if (r.id != null && Number(r.id) > 0 && r.__widgetValues == null) missingIds.push(String(r.id))
    })
    if (missingIds.length === 0) return
    widgetValuesFetchingRef.current = true
    fetch('/api/widgets/forms/' + encodeURIComponent(__lineFormCode) + '/records/batch-values', {
      method: 'POST',
      credentials: 'same-origin',
      headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
      body: JSON.stringify({ recordIds: missingIds }),
    })
      .then(function (r) { return r.ok ? r.json() : null })
      .then(function (data) {
        var byId = (data && data.ok === true && data.values) ? data.values : {}
        setRows(function (prev) {
          return prev.map(function (r) {
            if (r.id == null || Number(r.id) <= 0 || r.__widgetValues != null) return r
            if (missingIds.indexOf(String(r.id)) < 0) return r
            return Object.assign({}, r, { __widgetValues: byId[String(r.id)] || {} })
          })
        })
      })
      .catch(function () {
        // Yukleme hatasi — bos degerle isaretle ki dongusel refetch olmasin;
        // kullanici karti yine duzenleyebilir (__extras uzerinden).
        setRows(function (prev) {
          return prev.map(function (r) {
            if (r.id == null || Number(r.id) <= 0 || r.__widgetValues != null) return r
            if (missingIds.indexOf(String(r.id)) < 0) return r
            return Object.assign({}, r, { __widgetValues: {} })
          })
        })
      })
      .then(function () { widgetValuesFetchingRef.current = false })
  }, [rows, cardWidgets])

  // ── Kart ustu inline widget degisikligi ──
  //   Deger row.__extras'a MERGE edilerek yazilir (mevcut server degerleri +
  //   onceki edit'ler korunur) — ana Kaydet akisi ve ⚙ modal ayni buffer'i
  //   gordugu icin uc taraf senkron kalir.
  function handleWidgetValueChange(rowUid, widgetCode, val) {
    setRows(function (prev) {
      return prev.map(function (r) {
        if (r._uid !== rowUid) return r
        var merged = Object.assign({}, r.__widgetValues || {}, r.__extras || {})
        merged[widgetCode] = val
        return Object.assign({}, r, { __extras: merged })
      })
    })
  }

  // ── Kayitli satirda inline widget edit'ini otomatik kalicilastir ──
  //   ⚙ modal kayitli satirda aninda backend'e yazar; inline edit'in de ayni
  //   garantiyi vermesi icin 1.2sn debounce ile widget API'sine flush edilir.
  //   (Kaydedilmemis satirlar ana belge Kaydet akisiyla senkronlanir.)
  //   Basarisiz flush'ta __extras SILINMEZ (veri kaybi olmaz) ve satir invalid
  //   isaretlenir — gear kirmizi olur, kullanici modaldan tamamlar.
  var widgetFlushTimerRef = useRef(null)
  useEffect(function () {
    if (cardWidgets.length === 0) return undefined
    var pending = rows.filter(function (r) {
      return r.id != null && Number(r.id) > 0 && r.__extras && Object.keys(r.__extras).length > 0
    })
    if (pending.length === 0) return undefined
    if (widgetFlushTimerRef.current) clearTimeout(widgetFlushTimerRef.current)
    widgetFlushTimerRef.current = setTimeout(function () {
      pending.forEach(function (r) {
        var snapshot = JSON.stringify(r.__extras)
        fetch('/api/widgets/forms/' + encodeURIComponent(__lineFormCode) + '/records/' + encodeURIComponent(String(r.id)), {
          method: 'POST',
          credentials: 'same-origin',
          headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
          body: JSON.stringify({ values: r.__extras, grids: null }),
        })
          .then(function (resp) { return resp.ok ? resp.json() : { success: false } })
          .then(function (result) {
            if (result && result.success !== false) {
              // Basari: __extras → __widgetValues'a tasi (o esnada yeni edit
              // gelmediyse). Yeni edit geldiyse buffer durur, sonraki flush alir.
              setRows(function (prev) {
                return prev.map(function (rr) {
                  if (rr._uid !== r._uid || !rr.__extras) return rr
                  if (JSON.stringify(rr.__extras) !== snapshot) return rr
                  var copy = Object.assign({}, rr, { __widgetValues: Object.assign({}, rr.__extras) })
                  delete copy.__extras
                  return copy
                })
              })
              setInvalidLineIds(function (prev) {
                return prev.filter(function (x) { return x !== Number(r.id) })
              })
            } else {
              // Zorunlu alan eksik vb. — deger lokalde durur, gear kirmizi olur.
              setInvalidLineIds(function (prev) {
                var idNum = Number(r.id)
                return prev.indexOf(idNum) >= 0 ? prev : prev.concat([idNum])
              })
            }
          })
          .catch(function () { /* ag hatasi — buffer korunur, sonraki flush/kaydet dener */ })
      })
    }, 1200)
    return function () {
      if (widgetFlushTimerRef.current) clearTimeout(widgetFlushTimerRef.current)
    }
  }, [rows, cardWidgets])

  useEffect(function () {
    function onAutoOpen(e) {
      var d = e.detail || {}
      if (d.stage !== 'extras') return
      // Zorunlu widget yoksa otomatik acma — guided chain burada sessizce biter.
      if (!hasRequiredLineWidgetsRef.current) return
      var target = (rowsRef.current || []).find(function (r) { return r._uid === d.rowUid })
      if (target) setExtrasModalRow(target)
    }
    window.addEventListener('lineGrid:autoOpenStage', onAutoOpen)
    return function () { window.removeEventListener('lineGrid:autoOpenStage', onAutoOpen) }
  }, [])


  // ── Satir duzelt (kilit/unlock sistemi) ──
  // setRows ile yuklenen satirlar _locked:true olarak gelir. Kullanici Duzelt
  // butonuna basmadan hucreler + Sil + Kombinasyon + Not butonlari pasif.
  // Buton click: _locked toggle; acildiginda ilk editable input'a focus.
  function handleEditRow(rowUid) {
    setRows(function (prev) {
      return prev.map(function (r) {
        if (r._uid !== rowUid) return r
        return Object.assign({}, r, { _locked: !r._locked })
      })
    })
    setEditingRowUid(function (prev) { return prev === rowUid ? null : rowUid })
    // Unlock sonrasi ilk editable hucreye focus
    requestAnimationFrame(function() {
      var rowEl = document.querySelector('[data-row-uid="' + rowUid + '"]')
      if (!rowEl) return
      var firstInput = rowEl.querySelector('input:not([disabled]), select:not([disabled]), textarea:not([disabled])')
      if (firstInput && typeof firstInput.focus === 'function') firstInput.focus()
    })
  }

  // Row-level flag helpers
  // canEdit: Duzelt butonu her zaman aktif (kilit toggle icin)
  // canDelete / canModify: _locked false olmali (ve server-side __canDelete engellemedigi surece)
  //
  // Kit tam-set teslimat satirlari (Faz 4a) SALT-OKUNUR: irsaliyede kit BASLIK
  // (stok-etkisiz, fiyatli) + patlatilmis BILESEN (gercek stok) satirlarinin
  // duzenlenmesi/silinmesi teslimati bozar (fiyat baslikta, stok bilesende; bilesen
  // silinirse orphan/desenkron). Bilesen: row.kitParentLineId dolu. Baslik: baska bir
  // satir bu satirin id'sine kitParentLineId ile isaret ediyor. NORMAL teklif/siparis
  // kit satiri (tek satir, kitParentLineId=null, kimse isaret etmez) ETKILENMEZ — orada
  // kit hala duzenlenebilir; kilit yalniz patlatilmis teslimat satirlarinda devrededir.
  function isKitLockedRow(row) {
    if (!row) return false
    if (row.kitParentLineId != null && Number(row.kitParentLineId) > 0) return true   // bilesen
    if (row.id == null || Number(row.id) <= 0) return false
    var hid = Number(row.id)
    return (rows || []).some(function (r) { return r && r.kitParentLineId != null && Number(r.kitParentLineId) === hid })  // baslik
  }
  function canEdit(row) { return row.__canEdit !== false && !isKitLockedRow(row) }
  // Kilitleme gecici olarak devre disi — YALNIZ kit tam-set teslimat satirlari salt-okunur.
  function isRowLocked(row) { return isKitLockedRow(row) }
  function canDelete(row) { return !isRowLocked(row) && row.__canDelete !== false }
  function canModify(row) { return !isRowLocked(row) }

  // ── Zorunlu-pozitif (ör. Miktar) dogrulama — satir seviyesinde ──
  // Yalnizca satirda zaten icerik (materialCode) VARSA degerlendirir — henuz
  // stok kodu secilmemis tamamen bos/yeni satir bu isaretle kirmizi boyanmaz
  // (kullanici daha yeni ekledi). Icerik varsa VE herhangi bir requirePositive
  // kolonu bos/<=0 ise true doner. NumericCell'deki hucre-ici kirmizi kenarlik
  // ile ayni "satir icerigi var mi" kuralini paylasir — bkz. LineGridCell.jsx.
  function rowHasInvalidRequiredQty(row) {
    var hasContent = !!(row && row.materialCode != null && String(row.materialCode).trim() !== '')
    if (!hasContent) return false
    for (var i = 0; i < columns.length; i++) {
      var c = columns[i]
      if (c.requirePositive !== true) continue
      var v = row[c.key]
      var n = typeof v === 'number' ? v : (v == null || v === '' ? null : parseFloat(String(v).replace(',', '.')))
      if (v == null || v === '' || n == null || isNaN(n) || n <= 0) return true
    }
    return false
  }

  // ── Not paneli toggle ──
  // Panel acik: manuel acildi (openNoteRows[uid]) VEYA satir pinli (row.notesPinned)
  // ONEMLI: Yalniz dolu olmak panele otomatik acilma saglamaz — kullanici not simgesiyle acar.
  function hasAnyBelowValue(row) {
    for (var i = 0; i < belowColumns.length; i++) {
      var v = row[belowColumns[i].key]
      if (v != null && String(v).trim() !== '') return true
    }
    return false
  }
  function isNoteOpen(row) {
    return openNoteRows[row._uid] === true || row.notesPinned === true
  }
  function toggleNote(rowUid) {
    setOpenNoteRows(function(prev) {
      var next = Object.assign({}, prev)
      if (next[rowUid]) delete next[rowUid]
      else next[rowUid] = true
      return next
    })
    // Acildiginda below cell'in ilk input'una fokus
    requestAnimationFrame(function() {
      var rowEl = document.querySelector('[data-row-uid="' + rowUid + '"]')
      if (!rowEl) return
      var input = rowEl.querySelector('[data-below-cell] input, [data-below-cell] textarea')
      if (input && typeof input.focus === 'function') input.focus()
    })
  }
  // Satir notu icin pin toggle — true ise belge acilislarinda otomatik acik gelir
  function toggleNotePin(rowUid) {
    setRows(function(prev) {
      return prev.map(function(r) {
        if (r._uid !== rowUid) return r
        return Object.assign({}, r, { notesPinned: !r.notesPinned })
      })
    })
  }

  // ── Footer subtotal hesapla ──
  // ÖNEMLI: Revize edilmis (superseded) parent satirlar UI'da gizli — bunlari
  // topluya KATMA. Aksi halde revize sonrasi toplam ciftleyip kullaniciya
  // "neden eski kalemi de sayiyor?" hissi verir.
  var subtotals = useMemo(function() {
    var out = {}
    if (footer.showSubtotal && Array.isArray(footer.subtotalColumns)) {
      // Aktif satirlar: revisedFromId bos — eski (superseded) satirlar kendi revisedFromId'sini tasir
      var liveRows = rows.filter(function (r) {
        return r.revisedFromId == null || Number(r.revisedFromId) <= 0
      })
      footer.subtotalColumns.forEach(function(colKey) {
        var sum = 0
        liveRows.forEach(function(r) {
          var v = r[colKey]
          if (typeof v === 'number') sum += v
          else if (v != null && v !== '') {
            var n = parseFloat(String(v).replace(',', '.'))
            if (!isNaN(n)) sum += n
          }
        })
        out[colKey] = sum
      })
    }
    return out
  }, [rows, footer])

  var totalSum = Object.values(subtotals).reduce(function(a, b) { return a + b }, 0)

  // Not: eski tablo duzeninin kolon-genisligi hesaplayan widthCss() yardimcisi
  // kart duzenine gecince (PageComment Seq 1079) kaldirildi — alan izgarasi artik
  // CSS grid auto-fill ile genisliyor, kolon bazli px genislik gerekmiyor.

  // Keyboard navigasyonu:
  //   Tab         → yatayda (browser default — mudahale yok)
  //   Enter       → yatayda (Tab gibi) — bir sonraki odaklanabilir elemana gecer
  //   Ctrl+Enter  → dikeyde — alt satirin ayni kolonuna git. Son satirda ise
  //                 window.sqSave() tetikle (validation DocumentEdit tarafinda).
  var gridRootRef = useRef(null)

  function handleGridKeyDown(e) {
    if (e.key !== 'Enter') return
    var t = e.target
    if (!t || t.tagName !== 'INPUT') return
    // IME compose sirasinda Enter'i isleme
    if (e.isComposing || e.keyCode === 229) return
    if (t.type === 'checkbox' || t.type === 'radio') return

    var isVerticalNav = e.ctrlKey || e.metaKey  // Ctrl (win/linux) veya Cmd (mac)

    var cell = t.closest('[data-cell-key]')
    var rowEl = t.closest('[data-row-uid]')
    if (!cell || !rowEl) return

    e.preventDefault()
    t.blur()

    if (isVerticalNav) {
      // Ctrl+Enter: alt satirin ayni kolonuna git (veya son satirda save)
      var colKey = cell.getAttribute('data-cell-key')
      var currentUid = rowEl.getAttribute('data-row-uid')
      var rowIdx = rows.findIndex(function (r) { return r._uid === currentUid })
      if (rowIdx < 0) return

      if (rowIdx < rows.length - 1) {
        var nextUid = rows[rowIdx + 1]._uid
        var root = gridRootRef.current || document
        var nextInput = root.querySelector(
          '[data-row-uid="' + nextUid + '"] [data-cell-key="' + colKey + '"] input, ' +
          '[data-row-uid="' + nextUid + '"] [data-cell-key="' + colKey + '"] select, ' +
          '[data-row-uid="' + nextUid + '"] [data-cell-key="' + colKey + '"] textarea'
        )
        if (nextInput) {
          setTimeout(function () {
            nextInput.focus()
            if (typeof nextInput.select === 'function') nextInput.select()
          }, 0)
        }
      } else {
        if (typeof window.sqSave === 'function') {
          setTimeout(function () { window.sqSave() }, 0)
        }
      }
    } else {
      // Plain Enter: Tab gibi — DOM sirasinda bir sonraki odaklanabilir elemana git
      var root2 = gridRootRef.current || document
      var focusables = Array.prototype.slice.call(root2.querySelectorAll(
        'input:not([disabled]):not([type="hidden"]), select:not([disabled]), textarea:not([disabled]), button:not([disabled])'
      )).filter(function (el) {
        // tabIndex -1 olanlari ve gozukmeyenleri atla
        if (el.tabIndex < 0) return false
        var rect = el.getBoundingClientRect()
        return rect.width > 0 && rect.height > 0
      })
      var idx = focusables.indexOf(t)
      if (idx >= 0 && idx < focusables.length - 1) {
        var next2 = focusables[idx + 1]
        setTimeout(function () {
          next2.focus()
          if (typeof next2.select === 'function' && (next2.tagName === 'INPUT' || next2.tagName === 'TEXTAREA')) next2.select()
        }, 0)
      }
    }
  }

  return (
    <div
      ref={gridRootRef}
      onKeyDown={handleGridKeyDown}
      className="calibra-line-grid calibra-line-grid--cards rounded-2xl border border-slate-200 bg-white/70 dark:bg-white/[0.04] dark:border-white/10 backdrop-blur-xl shadow-sm">
      {/* Kart listesi (PageComment Seq 1079) — her aktif kalem tek bir karttir.
          Onceki tablo basligi (kolon adlari) kaldirildi; her alanin etiketi artik
          kartin icinde, o alanin hemen ustunde gosterilir (bkz. asagidaki
          cardBodyColumns.map). */}
      <div className="p-2.5 sm:p-3 flex flex-col gap-2.5">
        {/* ── Revize zinciri: superseded satirlari GIZLE ─────────────────
            Bir satir X'i revize ederek yeni satir Y eklenince, X "kapanmis"
            sayilir (Y.revisedFromId = X.id). Grid yalnizca zincirin en
            sonundaki (henuz revize edilmemis) satirlari gosterir. Eski revizeler
            DB'de silinmez — save akisinda hepsi korunur, sadece UI'da gizlenir.
            Revize modal'i icinde "Gecmis Revizeler" sekmesinde tamami gorulur. */}
        {rows.length === 0 ? (
          <div className="px-6 py-10 text-center text-[12px] text-slate-400 dark:text-white/30">
            {labels.emptyText || 'Henuz kalem eklenmemis'}
          </div>
        ) : (
          <AnimatePresence initial={false}>
            {(function () {
              // Aktif satirlar: revisedFromId bos — eski (superseded) satirlarda revisedFromId dolu
              var visibleRows = rows.filter(function (r) {
                return r.revisedFromId == null || Number(r.revisedFromId) <= 0
              })
              if (visibleRows.length === 0) {
                // Tum satirlar revize edilmis (edge case) — bos mesaji goster
                return (
                  <div className="px-6 py-6 text-center text-[12px] text-slate-400 dark:text-white/30">
                    Gorunur satir yok (tumu revize edilmis)
                  </div>
                )
              }
              // Kit tam-set kismi teslimat (Faz 4a — SADECE GORSEL grupla):
              // Kit BASLIK satirinin id'sini isaret eden bilesen satirlari
              // (row.kitParentLineId) varsa, o id'yi "kitHeaderIds" isaretleyip
              // basligi rozetle + bilesenleri girintili/silik goster. Backend bu
              // alani yalniz kit tam-set teslimatinda doldurur (bkz. DocumentLineDto.
              // KitParentLineId) — normal teklif/siparis/irsaliye satirlarinda hep
              // null gelir, dolayisiyla bu blok o ekranlarda tamamen no-op'tur.
              var kitHeaderIds = {}
              visibleRows.forEach(function (r) {
                if (r.kitParentLineId != null && Number(r.kitParentLineId) > 0) {
                  kitHeaderIds[Number(r.kitParentLineId)] = true
                }
              })
              return visibleRows.map(function(row) {
              var isKitHeader = row.id != null && Number(row.id) > 0 && kitHeaderIds[Number(row.id)] === true
              var isKitComponent = row.kitParentLineId != null && Number(row.kitParentLineId) > 0
              return (
                <motion.div
                  key={row._uid}
                  data-row-uid={row._uid}
                  initial={{ opacity: 0, y: -4 }}
                  animate={{ opacity: 1, y: 0 }}
                  exit={{ opacity: 0, y: -4, height: 0 }}
                  transition={{ duration: 0.18 }}
                  className={'calibra-line-card relative rounded-xl border overflow-hidden transition-colors ' +
                    'border-slate-200 bg-white hover:border-indigo-200/70 dark:border-white/10 dark:bg-white/[0.025] dark:hover:border-indigo-400/25' +
                    (isKitComponent ? ' bg-indigo-50/40 dark:bg-indigo-500/[0.04]' : '')}
                  style={{ position: 'relative' }}
                >
                  {isKitComponent && (
                    <div
                      title="Kit bileseni — kit baslik satirinin teslim edilen bir parcasi"
                      style={{
                        position: 'absolute', left: 0, top: 0, bottom: 0,
                        width: 3, zIndex: 3, pointerEvents: 'none',
                        background: 'linear-gradient(180deg, rgba(99,102,241,.55), rgba(99,102,241,.15))',
                      }}
                    />
                  )}
                  {/* Zorunlu-pozitif (Miktar) uyari seridi — kartin SOL kenarinda ince
                      kirmizi cizgi. Sadece satirda icerik (materialCode) varken ve bir
                      requirePositive kolonu bos/<=0 iken gorunur (bkz. rowHasInvalidRequiredQty).
                      Uzun listede sorunlu karti hucrelere tek tek bakmadan taramaya yarar. */}
                  {rowHasInvalidRequiredQty(row) && (
                    <div
                      style={{
                        position: 'absolute', left: 0, top: 0, bottom: 0,
                        width: 3, zIndex: 4, pointerEvents: 'none',
                        background: '#ef4444',
                        boxShadow: '0 0 6px rgba(239,68,68,.6)',
                      }}
                    />
                  )}

                  <div
                    className="p-2.5 sm:p-3"
                    style={{
                      display: 'grid',
                      gridTemplateColumns: 'auto 1fr',
                      // Aksiyon seridi kartin SOLUNDA dikey sutun (2026-08-05 kullanici
                      // istegi). Ozel duzen aktifken kimlik kolonlari alan izgarasinin
                      // ICINDE layout'a gore cizilir — sabit kimlik bolgesi kalkar.
                      gridTemplateAreas: useCustomLayout
                        ? '"actions fields"'
                        : '"actions identity" "actions fields"',
                      columnGap: 12, rowGap: 10, alignItems: 'start',
                    }}
                  >
                    {/* DOM sirasi (identity → fields → actions) klavye Enter-nav ile
                        eslesir (bkz. handleGridKeyDown plain-Enter focusables listesi):
                        malzeme → alan izgarasi → aksiyon butonlari → sonraki kart.
                        Gorsel yerlesim ise CSS grid-area ile bagimsiz kontrol edilir —
                        aksiyonlar DOM'da sonda olsa da saga-usta gorunur, boylece
                        Enter tuşuyla ilerlerken Sil butonuna erken takilmaz. */}
                    {/* ── Kart kimlik bolgesi: malzeme kodu + adi. Ozel duzen aktifken
                        render EDILMEZ — kimlik kolonlari alan izgarasinda layout'a
                        gore cizilir (KIT rozetleri de oraya tasinir). ── */}
                    {!useCustomLayout && (
                    <div style={{ gridArea: 'identity' }} className="min-w-[180px]">
                        <div className="flex items-center gap-1.5 flex-wrap">
                          {isKitComponent && (
                            <span
                              className="text-[12px] leading-none text-indigo-400 dark:text-indigo-300/70 select-none flex-shrink-0"
                              title="Kit bileseni"
                            >↳</span>
                          )}
                          {materialCodeCol && (
                            <div
                              data-cell-key={materialCodeCol.key}
                              className="w-full max-w-[260px]"
                              style={isRowLocked(row) ? { opacity: 0.75, pointerEvents: 'none' } : {}}
                            >
                              <div className="calibra-line-card-label flex items-center gap-1 text-[10px] font-bold tracking-wide text-slate-500 dark:text-white/45 mb-0.5">
                                <span className="truncate">{materialCodeCol.label}</span>
                                {(materialCodeCol.required || materialCodeCol.requirePositive) && <span className="text-rose-500 dark:text-rose-400">*</span>}
                              </div>
                              {/* Alt-cizgi (underline) standardi — ust bilgi alanlariyla ayni gorunum
                                  (site.css .ux-edit-pane): kutu yerine yalniz border-bottom, odakta indigo. */}
                              <div className="border-b border-slate-200 focus-within:border-indigo-500 focus-within:shadow-[0_1px_0_0_#6366f1] dark:border-white/[0.12] dark:focus-within:border-indigo-400 dark:focus-within:shadow-[0_1px_0_0_#818cf8] transition-[border-color,box-shadow]">
                                <LineGridCell
                                  column={materialCodeCol}
                                  row={row}
                                  value={tlCellValue(materialCodeCol, row)}
                                  onChange={function(k, v, fill) { handleCellChange(row._uid, k, v, fill) }}
                                  siblingColumns={allColumns}
                                />
                              </div>
                            </div>
                          )}
                          {isKitHeader && (
                            <span
                              className="inline-flex items-center rounded px-1.5 py-[2px] text-[9px] font-bold tracking-wide bg-indigo-100 text-indigo-700 dark:bg-indigo-500/20 dark:text-indigo-300 select-none flex-shrink-0"
                              title="Kit — bilesenleri asagida listelenir"
                            >KİT</span>
                          )}
                        </div>
                        {materialNameCol && (
                          <div data-cell-key={materialNameCol.key} className="min-w-0 mt-0.5 border-b border-slate-200 focus-within:border-indigo-500 focus-within:shadow-[0_1px_0_0_#6366f1] dark:border-white/[0.12] dark:focus-within:border-indigo-400 dark:focus-within:shadow-[0_1px_0_0_#818cf8] transition-[border-color,box-shadow]">
                            <LineGridCell
                              column={materialNameCol}
                              row={row}
                              value={tlCellValue(materialNameCol, row)}
                              onChange={function(k, v, fill) { handleCellChange(row._uid, k, v, fill) }}
                              siblingColumns={allColumns}
                            />
                          </div>
                        )}
                      </div>
                    )}

                    {/* ── Kart alan izgarasi: kalan kolonlar (miktar/birim/fiyat/iskonto/
                        kdv/toplam/seri vb.), her biri kendi etiketiyle. Sabit min-genislik
                        + esit sutunlar (auto-fill/minmax) tum alanlarin ayni hiza/genislikte
                        durmasini saglar; her hucrenin etiketi tek satirda kirpilir (truncate)
                        boylece giris kutulari hep ayni dikey hizada baslar (PageComment
                        Seq 1083 "simetrik duzen"). TL karsiligi (tlMirror) olan alanlarda
                        (birim fiyat/satir toplami) doviz kutusunun HEMEN YANINDA kucuk bir
                        TL rozeti gosterilir — artik alt satir DEGIL (Seq 1083 "TL yaninda");
                        bu alanlar 2 sutun kaplar (daha fazla yer gerektigi icin). DOM'da
                        kimlik alanindan (materialCode/materialName) hemen sonra gelir —
                        Enter-nav bu alanlari aksiyon butonlarindan ONCE gezer (bkz. asagidaki not). */}
                    <div
                      style={useCustomLayout
                        ? {
                            // alignItems 'end': farkli baslik stillerinde (standart ustte /
                            // modern yuzer / sade solda) hucre yukseklikleri degisir —
                            // taban hizasi giris kutularini ayni satirda ayni cizgiye oturtur.
                            gridArea: 'fields', display: 'grid',
                            gridTemplateColumns: 'repeat(' + CARD_GRID_UNITS + ', minmax(0, 1fr))',
                            columnGap: 12, rowGap: 10, alignItems: 'end',
                          }
                        : {
                            gridArea: 'fields', display: 'grid',
                            gridTemplateColumns: 'repeat(auto-fill, minmax(126px, 1fr))',
                            columnGap: 12, rowGap: 10, alignItems: 'end',
                          }}
                      className={isKitComponent ? 'opacity-90' : ''}
                    >
                      {cardItems.map(function(item) {
                        var col = item.col
                        if (!item.visible) return null
                        // ── Form Davranış Katmanı: satır-scope kurallar ──
                        //   visibleIf false → hücre bu SATIRDA gizli; requiredIf true →
                        //   dinamik zorunlu (yıldız + boşsa kırmızı çerçeve). Eval hatası
                        //   fail-open (görünür / zorunlu değil).
                        var beh = col.__behavior || null
                        if (beh && beh.visibleIf && evalRowRule(beh.visibleIf, row) === false) return null
                        var behReqNow = !!(beh && ((beh.isRequired) || (beh.requiredIf && evalRowRule(beh.requiredIf, row) === true)))
                        var behEmpty = false
                        if (behReqNow) {
                          var __bRaw = row[col.key]
                          var __bIsNum = col.type === 'number' || col.type === 'currency' || col.type === 'percent'
                          if (__bIsNum) {
                            var __bN = typeof __bRaw === 'number' ? __bRaw : parseFloat(String(__bRaw == null ? '' : __bRaw).replace(',', '.'))
                            behEmpty = isNaN(__bN) || __bN <= 0
                          } else {
                            behEmpty = __bRaw == null || String(__bRaw).trim() === ''
                          }
                        }
                        var __rowHasContent = !!(row.materialCode && String(row.materialCode).trim() !== '')
                        var behInvalid = behReqNow && behEmpty && __rowHasContent
                        // Kilitli satirda tum hucrelere pointer-events: none — sadece gorsel, tiklanmaz
                        var lockedStyle = isRowLocked(row) ? { opacity: 0.75, pointerEvents: 'none' } : {}
                        var Icon = resolveIcon(col.icon)
                        var mirror = col.__isWidget ? null : tlMirrorBySource[col.key]
                        var showMirror = mirror && showTlColumns
                        var cellStyle = Object.assign({}, lockedStyle)
                        if (useCustomLayout) {
                          cellStyle.gridColumn = 'span ' + Math.min(Math.max(item.span || 12, 1), CARD_GRID_UNITS)
                        } else if (showMirror) {
                          cellStyle.gridColumn = 'span 2'
                        } else if (col.type === 'percent') {
                          // Seq 1085: İskonto/KDV % alanları en fazla 3 karakter (0-100) — ayrılan
                          // alan gereksiz genişti; dar tutulur (custom layout'ta admin span'i geçerli).
                          cellStyle.maxWidth = 104
                        }
                        // Widget hucre degeri: __extras (bekleyen edit) > __widgetValues (server)
                        var widgetValue = null
                        if (col.__isWidget) {
                          var wc = col.__widgetCode
                          widgetValue = (row.__extras && (wc in row.__extras))
                            ? row.__extras[wc]
                            : ((row.__widgetValues || {})[wc])
                        }
                        var isMaterialCodeCell = col === materialCodeCol
                        // Baslik override'lari (duzen editorunden): metin + boyut +
                        // kalinlik (inline) + renk (semantik token → Tailwind sinifi)
                        // + stil (Alan Yonetimi sozlugu: standard/modern/inline).
                        var labelText = item.label || col.label
                        var labelMode = (item.labelStyle === 'modern' || item.labelStyle === 'inline') ? item.labelStyle : 'standard'
                        var labelColorCls = item.labelColor
                          ? CARD_LABEL_COLOR_CLS[item.labelColor]
                          : 'text-slate-500 dark:text-white/45'
                        var labelStyleOv = {}
                        if (item.labelSize) labelStyleOv.fontSize = item.labelSize
                        if (item.labelWeight) labelStyleOv.fontWeight = item.labelWeight
                        // Modern (yuzer) etiket absolute konumlanir — hucre anchor olmali.
                        if (labelMode === 'modern') cellStyle = Object.assign({}, cellStyle, { position: 'relative' })
                        // Etiket icerigi — uc stil modunda da ayni (kit susleri dahil).
                        var labelInner = (
                          <>
                            {/* Kit süsleri — kimlik bolgesi ozel duzende kalktigi icin
                                ↳ oku ve KIT rozeti malzeme kodu hucresinin etiketine tasinir. */}
                            {isMaterialCodeCell && isKitComponent && (
                              <span className="text-[12px] leading-none text-indigo-400 dark:text-indigo-300/70 select-none flex-shrink-0" title="Kit bileseni">↳</span>
                            )}
                            <Icon size={10} strokeWidth={1.8} className="text-slate-400 dark:text-white/35 flex-shrink-0" />
                            <span className="truncate">{labelText}</span>
                            {(col.required || col.requirePositive || behReqNow) && <span className="text-rose-500 dark:text-rose-400">*</span>}
                            {isMaterialCodeCell && isKitHeader && (
                              <span
                                className="inline-flex items-center rounded px-1.5 py-[2px] text-[9px] font-bold tracking-wide bg-indigo-100 text-indigo-700 dark:bg-indigo-500/20 dark:text-indigo-300 select-none flex-shrink-0"
                                title="Kit — bilesenleri asagida listelenir"
                              >KİT</span>
                            )}
                          </>
                        )
                        return (
                          <div key={col.key} data-cell-key={col.key} style={cellStyle} className={labelMode === 'inline' ? 'flex items-center gap-2' : undefined}>
                            {/* standard: etiket ustte · inline (Sade): etiket solda ·
                                modern: etiket kutunun ust kenarinda yuzer (asagida). */}
                            {labelMode === 'standard' && (
                              <div
                                className={'calibra-line-card-label flex items-center gap-1 text-[10px] font-bold tracking-wide mb-0.5 ' + labelColorCls}
                                style={labelStyleOv}
                              >
                                {labelInner}
                              </div>
                            )}
                            {labelMode === 'inline' && (
                              <div
                                className={'calibra-line-card-label flex items-center gap-1 text-[10px] font-bold tracking-wide flex-shrink-0 max-w-[45%] ' + labelColorCls}
                                style={labelStyleOv}
                              >
                                {labelInner}
                              </div>
                            )}
                            {labelMode === 'modern' && (
                              <div
                                className={/* Underline gorunumde maskelenecek ust kenar yok — yuzer etiket zeminsiz */
                                  'calibra-line-card-label absolute flex items-center gap-1 text-[9.5px] font-bold tracking-wide ' + labelColorCls}
                                style={Object.assign({ top: -1, left: 10, zIndex: 2, lineHeight: '12px' }, labelStyleOv)}
                              >
                                {labelInner}
                              </div>
                            )}
                            <div className={'flex items-stretch gap-1.5' + (labelMode === 'inline' ? ' flex-1 min-w-0' : '') + (labelMode === 'modern' ? ' mt-1.5' : '')}>
                              <div
                                className="flex-1 min-w-0 border-b border-slate-200 focus-within:border-indigo-500 focus-within:shadow-[0_1px_0_0_#6366f1] dark:border-white/[0.12] dark:focus-within:border-indigo-400 dark:focus-within:shadow-[0_1px_0_0_#818cf8] transition-[border-color,box-shadow]"
                                style={behInvalid ? { borderBottomColor: '#ef4444', boxShadow: '0 1px 0 0 #ef4444', backgroundColor: 'rgba(239,68,68,0.05)' } : undefined}
                                title={behInvalid ? 'Bu alan zorunlu' : undefined}
                              >
                                {col.__isWidget ? (
                                  col.__widgetType === 'date' ? (
                                    <input
                                      type="date"
                                      data-native-date
                                      value={widgetValue == null ? '' : String(widgetValue)}
                                      onChange={function(e) { handleWidgetValueChange(row._uid, col.__widgetCode, e.target.value) }}
                                      className="w-full h-full bg-transparent border-0 outline-none px-2.5 py-2 text-[13px] text-slate-800 dark:text-white/85 transition-colors"
                                    />
                                  ) : (
                                    <LineGridCell
                                      column={col}
                                      row={row}
                                      value={widgetValue}
                                      onChange={function(k, v) { handleWidgetValueChange(row._uid, col.__widgetCode, v) }}
                                    />
                                  )
                                ) : (
                                  <LineGridCell
                                    column={col}
                                    row={row}
                                    value={tlCellValue(col, row)}
                                    onChange={function(k, v, fill) { handleCellChange(row._uid, k, v, fill) }}
                                    siblingColumns={allColumns}
                                  />
                                )}
                              </div>
                              {showMirror && (
                                <div
                                  className="flex-shrink-0 flex items-center gap-1 px-1.5 text-[11px] font-mono tabular-nums text-slate-500 dark:text-white/45"
                                  title="Belge kuruyla TL karşılığı"
                                >
                                  <span className="opacity-70">₺</span>
                                  <span>{TR_FMT(tlCellValue(mirror, row), mirror.precision)}</span>
                                </div>
                              )}
                            </div>
                          </div>
                        )
                      })}
                    </div>

                    {/* Aksiyon kumesi (sadelesti): ••• kisayol + Kombinasyon + Seri + Ek Alanlar (⚙) + Sil.
                        Not ve Revize butonlari ••• icine tasindi — tek dropdown'dan erisilir.
                        DOM sirasi bilerek alan izgarasindan SONRA konuldu: klavye Enter-nav
                        (handleGridKeyDown) DOM sirasini takip eder — boylece kullanici
                        miktar/fiyat/iskonto gibi veri alanlarini gezerken araya giren bir
                        butona (ozellikle Sil'e) yanlislikla odaklanip Enter'a basmaz. Gorsel
                        yerlesim grid-area:"actions" ile kimlik satirinin SOLUNA sabitlenir
                        (Seq 1081 kullanici istegi) — DOM sirasindan bagimsizdir. */}
                    <div style={{ gridArea: 'actions', alignSelf: 'start' }} className="flex flex-col items-center gap-1 flex-shrink-0 justify-self-start">
                      {/* Satir kisayol menusu — MoreHorizontal ikonu, tiklayinca liste acilir */}
                      <button
                        type="button"
                        onClick={function (e) {
                          // Butonun ekrandaki pozisyonunu al, menuyu onun altina konumla.
                          var rect = e.currentTarget.getBoundingClientRect()
                          setShortcutsMenu({
                            row: row,
                            pos: { top: rect.bottom + 4, left: rect.left, width: 200 },
                          })
                        }}
                        className="w-7 h-7 rounded-lg flex items-center justify-center transition-colors text-slate-400 hover:text-indigo-500 hover:bg-indigo-50 dark:text-white/30 dark:hover:text-indigo-300 dark:hover:bg-indigo-500/10"
                        title="Kisayollar / satir islemleri"
                        aria-label="Kisayol menusu"
                        aria-haspopup="menu"
                        aria-expanded={!!(shortcutsMenu && shortcutsMenu.row && shortcutsMenu.row._uid === row._uid)}
                      >
                        <MoreHorizontal size={14} strokeWidth={2} />
                      </button>
                      {actionLookupColumns.map(function(col) {
                        // Kilitli satirda Kombinasyon butonu da pasif — CombinationLookupCell'in
                        // kendi iki hali var (secili/eksik), burada locked ozel durumu DOM'da
                        // pointer-events: none ile disari kapatiyoruz.
                        return (
                          <div key={col.key} style={isRowLocked(row) ? { opacity: 0.45, pointerEvents: 'none' } : {}}>
                            <CombinationLookupCell
                              compact={true}
                              column={col}
                              row={row}
                              value={row[col.key]}
                              onChange={function(k, v, fill) { handleCellChange(row._uid, k, v, fill) }}
                            />
                          </div>
                        )
                      })}
                      {/* İzlenebilirlik (Lot/Seri) — kompakt buton, modal grid seviyesinde açılır */}
                      {traceColumns.map(function(col) {
                        return (
                          <div key={col.key} style={isRowLocked(row) ? { opacity: 0.45, pointerEvents: 'none' } : {}}>
                            <TraceEntryCell column={col} row={row} onOpen={function(r) { setTraceModalRow({ row: r, column: col }) }} />
                          </div>
                        )
                      })}
                      {/* Not butonu aksiyon kumesinden cikartildi — artik ••• kisayol
                          menusunun icinde "Not Ekle / Goster / Gizle" olarak yer aliyor. */}
                      {/* Ek Alanlar (SALES_QUOTE_LINES widget'lari) — sadece kayitli satirlarda.
                          Renk kuralı:
                            - Satir kaydedilmemis → sky (notr)
                            - Kaydedilmis + invalidLineIds'de → kirmizi (eksik zorunlu widget)
                            - Kaydedilmis + invalidLineIds'de degil → yesil (OK) */}
                      {(function() {
                        var savedLineId = row.id != null && row.id !== '' && Number(row.id) > 0 ? Number(row.id) : null
                        // Kayitli olmasa da ⚙ butonu aktif — kullanici ek alanlari
                        // girip "Kaydet" deyince degerler row.__extras icinde local
                        // tutulur; ana Kaydet'te satir DB'ye islendikten sonra
                        // extras widget API'siyle satir id'sine senkron edilir.
                        // Sadece kilitli satirda pasif (canModify=false).
                        var disabled = !canModify(row)
                        var hasPending = row.__extras && Object.keys(row.__extras).length > 0
                        var isInvalid = savedLineId != null && invalidLineIds.indexOf(savedLineId) !== -1
                        var colorClass
                        if (disabled) {
                          colorClass = 'text-slate-300 dark:text-white/15 cursor-not-allowed'
                        } else if (isInvalid) {
                          // Boyut sabit kalsin diye ring yok — sadece zemin + metin rengi.
                          colorClass = 'text-white bg-rose-600 hover:bg-rose-500 dark:bg-rose-500/80 dark:hover:bg-rose-500'
                        } else if (savedLineId == null && !hasPending) {
                          // Kaydedilmemis + ek alan doldurulmamis → notr (sky)
                          colorClass = 'text-sky-600 bg-sky-50 hover:bg-sky-100 dark:text-sky-300 dark:bg-sky-500/15 dark:hover:bg-sky-500/25'
                        } else {
                          // Kaydedilmis + gecerli, veya kaydedilmemis ama local __extras dolu → yesil
                          colorClass = 'text-emerald-600 bg-emerald-50 hover:bg-emerald-100 dark:text-emerald-300 dark:bg-emerald-500/15 dark:hover:bg-emerald-500/25'
                        }
                        return (
                          <button
                            type="button"
                            data-extras-line-id={savedLineId || ''}
                            onClick={function() {
                              if (disabled) return
                              setExtrasModalRow(row)
                            }}
                            disabled={disabled}
                            className={'w-7 h-7 rounded-lg flex items-center justify-center transition-colors ' + colorClass}
                            title={disabled
                              ? 'Once kilidi acin'
                              : (isInvalid
                                  ? 'Zorunlu ek alanlar eksik — doldurun'
                                  : (savedLineId == null
                                      ? (hasPending ? 'Ek alan girildi — satiri Kaydet ile kesinlestirin' : 'Bu satir icin ek alan gir (Kaydet ile kesinlesir)')
                                      : 'Satira ait ek alanlari duzenle'))}
                          >
                            <Settings size={13} strokeWidth={1.8} />
                          </button>
                        )
                      })()}
                      {/* Revize butonu aksiyon kumesinden cikartildi — artik ••• kisayol
                          menusunde "Revize Et" olarak yer aliyor (hala revised zincir
                          rozetini kart uzerinde gostermiyoruz; modal icinde zincir var). */}
                      <button
                        type="button"
                        onClick={function() {
                          if (canDelete(row)) handleDeleteRow(row._uid)
                        }}
                        disabled={!canDelete(row)}
                        className={'w-7 h-7 rounded-lg flex items-center justify-center transition-colors ' + (
                          !canDelete(row)
                            ? 'text-slate-300 dark:text-white/15 cursor-not-allowed'
                            : 'text-rose-500 hover:text-white hover:bg-rose-500 dark:text-rose-400 dark:hover:text-white dark:hover:bg-rose-500'
                        )}
                        title={(isKitHeader || isKitComponent) ? 'Kit teslimat satiri — kit bilesenleriyle birlikte yonetilir, tek tek duzenlenemez/silinemez'
                               : (isRowLocked(row) ? 'Once kilidi acin' : (row.__canDelete === false ? (row.__deleteLockReason || 'Bu satir silinemez') : 'Sil'))}
                      >
                        {canDelete(row) ? <Trash2 size={13} strokeWidth={2} /> : <Lock size={12} strokeWidth={1.8} />}
                      </button>
                    </div>
                  </div>

                  {/* Satir alti kolonlar (placement: row-below) — ornegin "Not".
                      Panel sadece kullanici "Not ekle" butonuna basinca VEYA not doluysa gorunur. */}
                  {belowColumns.length > 0 && isNoteOpen(row) && (
                    <div className="flex flex-col gap-1 pl-3 pr-3 pb-2.5 pt-0 border-t border-slate-100 dark:border-white/[0.06]">
                      {belowColumns.map(function(col) {
                        var Icon = resolveIcon(col.icon)
                        return (
                          <div
                            key={col.key}
                            data-below-cell
                            className="flex items-center gap-2 rounded-md border border-slate-100 bg-slate-50/60 dark:border-white/[0.06] dark:bg-white/[0.02] mt-2"
                          >
                            <button
                              type="button"
                              onClick={function() { if (canModify(row)) toggleNotePin(row._uid) }}
                              disabled={!canModify(row)}
                              className={'ml-1.5 w-6 h-6 rounded-md flex items-center justify-center transition-colors flex-shrink-0 ' + (
                                !canModify(row)
                                  ? 'text-slate-300 dark:text-white/15 cursor-not-allowed'
                                  : (row.notesPinned
                                      ? 'text-indigo-600 bg-indigo-50 dark:text-indigo-300 dark:bg-indigo-500/15'
                                      : 'text-slate-400 hover:text-indigo-500 hover:bg-indigo-50 dark:text-white/40 dark:hover:text-indigo-300 dark:hover:bg-indigo-500/10')
                              )}
                              title={!canModify(row) ? 'Once kilidi acin' : (row.notesPinned ? 'Pini cikar — belge acilisinda not gizli gelir' : 'Pinle — belge acilisinda not otomatik acilir')}
                            >
                              {row.notesPinned
                                ? <Pin size={12} strokeWidth={2} />
                                : <PinOff size={12} strokeWidth={1.8} />}
                            </button>
                            <div className="flex items-center gap-1.5 py-1.5 text-[10px] font-semibold uppercase tracking-wider text-slate-500 dark:text-white/50 flex-shrink-0">
                              <Icon size={11} strokeWidth={1.8} className="text-slate-400 dark:text-white/40 flex-shrink-0" />
                              <span>{col.label}</span>
                            </div>
                            <div className="flex-1 min-w-0">
                              <LineGridCell
                                column={col}
                                row={row}
                                value={row[col.key]}
                                onChange={function(k, v, fill) { handleCellChange(row._uid, k, v, fill) }}
                              />
                            </div>
                          </div>
                        )
                      })}
                    </div>
                  )}
                </motion.div>
              )
              })
            })()}
          </AnimatePresence>
        )}
      </div>

      {/* Footer: Yeni kalem + toplam */}
      <div className="flex items-center justify-between px-3 py-2.5 border-t border-slate-200 bg-slate-50/60 dark:bg-white/[0.02] dark:border-white/[0.08]">
        {(function() {
          // Stok kodu bos olan satir var ise Yeni Kalem pasif — once mevcut bos satiri doldur.
          var hasEmptyRow = rows.some(function(r) {
            return !r.materialCode || String(r.materialCode).trim() === ''
          })
          return (
            <motion.button
              type="button"
              whileTap={hasEmptyRow ? undefined : { scale: 0.97 }}
              onClick={hasEmptyRow ? undefined : handleAddRow}
              disabled={hasEmptyRow}
              title={hasEmptyRow ? 'Once mevcut bos satira stok kodu girin' : ''}
              className={'flex items-center gap-2 px-3 py-1.5 rounded-lg text-[12px] font-semibold border transition-colors ' + (
                hasEmptyRow
                  ? 'bg-slate-100 text-slate-400 border-slate-200 cursor-not-allowed dark:bg-white/[0.04] dark:text-white/30 dark:border-white/[0.08]'
                  : 'bg-indigo-50 text-indigo-600 border-indigo-200 hover:bg-indigo-100 dark:bg-indigo-500/15 dark:text-indigo-300 dark:border-indigo-400/30 dark:hover:bg-indigo-500/25'
              )}
            >
              <Plus size={13} strokeWidth={2.2} />
              <span>{labels.addRow || 'Yeni Kalem'}</span>
            </motion.button>
          )
        })()}

        {/* Kart Düzeni butonu buradan KALDIRILDI (2026-08-05 kullanici istegi) —
            duzen yonetimi yalnizca Alan Yönetimi → "Kart Düzeni" uzerinden yapilir. */}

        {/* Kur girisi buradan KALDIRILDI (2026-08-05 kullanici istegi) — kur,
            ust bilgide Para Birimi'nin yanindaki #sqExchangeRate ile yonetilir;
            grid'in exchangeRate state'i o inputla senkron kalmaya devam eder
            (TL karsiligi kolon/toplamlari icin). */}

        {footer.showSubtotal && rows.length > 0 && (
          <div className="flex items-center gap-3 text-[12px]">
            <span className="text-slate-500 dark:text-white/40 uppercase tracking-wider font-semibold text-[10px]">
              {labels.totalLabel || 'Toplam'}
            </span>
            <span className="font-mono tabular-nums text-amber-600 dark:text-amber-300 text-[15px] font-bold">
              {TR_FMT(totalSum, decimalCfg ? decimalCfg.amount : 2)} {currencySymbol}
            </span>
          </div>
        )}

        {/* Belge dovizi TRY disiyken toplamin TL karsiligi (Seq 1077b) — lineTotalTL kolonuyla
            tutarli sekilde ayni exchangeRate ile hesaplanir, ayrica store edilmez. */}
        {footer.showSubtotal && rows.length > 0 && showTlColumns && (
          <div className="flex items-center gap-3 text-[12px]">
            <span className="text-slate-500 dark:text-white/40 uppercase tracking-wider font-semibold text-[10px]">
              {(labels.totalLabel || 'Toplam') + ' (TL)'}
            </span>
            <span className="font-mono tabular-nums text-amber-600 dark:text-amber-300 text-[15px] font-bold">
              {TR_FMT(totalSum * (exchangeRate || 1), decimalCfg ? decimalCfg.amount : 2)} ₺
            </span>
          </div>
        )}
      </div>

      {/* Silme onay modali (PageComment Seq 1082) — CLAUDE.md "Silme onay standardi":
          tam ekran backdrop + ortalanmis card + danger ikon + Vazgec/Sil. Native
          confirm() KULLANILMAZ. Esc/backdrop = vazgec, Enter = onay (bkz. yukaridaki
          deleteConfirmUid useEffect'i); Sil butonu acilista odakta (autoFocus + ref). */}
      {deleteConfirmUid && (function () {
        var drow = rows.find(function (r) { return r._uid === deleteConfirmUid }) || null
        if (!drow) return null
        function doCancel() { setDeleteConfirmUid(null) }
        function doConfirm() { performDeleteRow(deleteConfirmUid); setDeleteConfirmUid(null) }
        return createPortal(
          <div
            onClick={function (e) { if (e.target === e.currentTarget) doCancel() }}
            className="fixed inset-0 z-[70] flex items-center justify-center p-4"
            style={{ background: 'rgba(15,23,42,0.45)', backdropFilter: 'blur(4px)', WebkitBackdropFilter: 'blur(4px)' }}
          >
            <div
              className="w-full max-w-[380px] rounded-2xl border border-slate-200 bg-white shadow-2xl dark:border-white/10 dark:bg-[#171c2a] overflow-hidden"
              onClick={function (e) { e.stopPropagation() }}
            >
              <div className="p-5 flex flex-col items-center text-center gap-3">
                <div className="w-11 h-11 rounded-full flex items-center justify-center bg-rose-50 text-rose-500 dark:bg-rose-500/15 dark:text-rose-300">
                  <Trash2 size={20} strokeWidth={2} />
                </div>
                <div>
                  <div className="text-[14px] font-bold text-slate-800 dark:text-white">Kalemi Sil</div>
                  <div className="text-[12.5px] text-slate-500 dark:text-white/50 mt-1 leading-snug">
                    {'"' + (drow.materialName || drow.materialCode || 'Bu kalem') + '" satırı silinecek. Bu işlem geri alınamaz.'}
                  </div>
                </div>
              </div>
              <div className="flex items-center gap-2 px-5 pb-5">
                <button
                  type="button"
                  onClick={doCancel}
                  className="flex-1 px-3 py-2 rounded-lg text-[12.5px] font-semibold border border-slate-200 text-slate-600 hover:bg-slate-50 dark:border-white/10 dark:text-white/70 dark:hover:bg-white/5"
                >
                  Vazgeç
                </button>
                <button
                  type="button"
                  ref={deleteConfirmBtnRef}
                  onClick={doConfirm}
                  className="flex-1 px-3 py-2 rounded-lg text-[12.5px] font-bold text-white bg-rose-600 hover:bg-rose-500 dark:bg-rose-500 dark:hover:bg-rose-400"
                >
                  Sil
                </button>
              </div>
            </div>
          </div>,
          (document.querySelector('.sqe-body') || document.body)
        )
      })()}

      {/* Satir-basi Ek Alanlar modali — SALES_QUOTE_LINES formunun widget'lari.
          Sadece kayitli satirlarda (row.id > 0) acilir; recordId = line.id.
          Portal: .sqe-tab-content icine absolute konumlanir — app shell (ust bar,
          sol menu, alt panel) ve SQE sol tab navi gizlenmez, sadece icerik alani
          ortulur. */}
      {/* İzlenebilirlik (Lot/Seri) modalı — grid seviyesinde; miktar girişinden sonra otomatik açılır.
          onApply yalnızca serials/lotBreakdown'ı yazar (miktar sürücü; save adet=miktar zorunlu kılar). */}
      {traceModalRow && (function () {
        var __tl = !(typeof document !== 'undefined' && document.body.classList.contains('app-theme-dark'))
        var trow = traceModalRow.row
        var tcol = traceModalRow.column
        function _n(x) { if (x == null || x === '') return null; var n = parseFloat(String(x).replace(',', '.')); return isNaN(n) ? null : n }
        if (trow.trackSerial === true) {
          // Sayım — zengin seri kırılımı tablosu (Seri No · SKT · Açıklama · Miktar; seri=parti)
          return (
            <SerialBreakdownModal isLight={__tl} row={trow} column={tcol} qtyTarget={trow.quantity}
              value={Array.isArray(trow.serialBreakdown) ? trow.serialBreakdown : []}
              onApply={function (list) { handleCellChange(trow._uid, 'serialBreakdown', list); setTraceModalRow(null) }}
              onClose={function () { setTraceModalRow(null) }} />
          )
        }
        if (trow.trackLot === true) {
          return (
            <LotBreakdownModal isLight={__tl} row={trow} column={tcol} qtyTarget={trow.quantity}
              value={Array.isArray(trow.lotBreakdown) ? trow.lotBreakdown : []}
              onApply={function (list) { handleCellChange(trow._uid, 'lotBreakdown', list); setTraceModalRow(null) }}
              onClose={function () { setTraceModalRow(null) }} />
          )
        }
        return null
      })()}

      {extrasModalRow && (function () {
        // Tema detection — kisayol menusuyle ayni chain (iframe parent fallback, default light).
        var __isLight = (function () {
          if (typeof document === 'undefined') return true
          try {
            if (document.body.classList.contains('app-theme-light')) return true
            if (document.body.classList.contains('app-theme-dark'))  return false
            if (window.parent && window.parent !== window && window.parent.document && window.parent.document.body) {
              if (window.parent.document.body.classList.contains('app-theme-light')) return true
              if (window.parent.document.body.classList.contains('app-theme-dark'))  return false
            }
          } catch (_) {}
          return true
        })()
        var __overlayBg  = __isLight
          ? 'radial-gradient(at 20% 10%, rgba(99,102,241,0.06) 0%, transparent 45%), radial-gradient(at 85% 85%, rgba(168,85,247,0.05) 0%, transparent 45%), rgba(15,23,42,0.35)'
          : 'radial-gradient(at 20% 10%, rgba(99,102,241,0.12) 0%, transparent 45%), radial-gradient(at 85% 85%, rgba(168,85,247,0.10) 0%, transparent 45%), rgba(3,6,15,0.72)'
        var __panelBg     = __isLight
          ? 'linear-gradient(180deg, #ffffff 0%, #f8fafc 100%)'
          : 'linear-gradient(180deg, rgba(23,28,42,0.98) 0%, rgba(15,19,30,0.98) 100%)'
        var __panelBorder = __isLight ? '1px solid #e2e8f0'                 : '1px solid rgba(255,255,255,0.10)'
        var __panelShadow = __isLight
          ? '0 16px 48px rgba(15,23,42,0.18), 0 0 0 1px rgba(99,102,241,0.08)'
          : '0 32px 96px rgba(0,0,0,0.65), 0 0 0 1px rgba(99,102,241,0.08)'
        var __textColor   = __isLight ? '#0f172a' : 'rgba(255,255,255,0.92)'
        var __mutedText   = __isLight ? '#64748b' : 'rgba(255,255,255,0.5)'
        var __subtleText  = __isLight ? '#94a3b8' : 'rgba(255,255,255,0.35)'
        var __sepColor    = __isLight ? '#e2e8f0' : 'rgba(255,255,255,0.06)'
        var __headTitle   = __isLight ? '#0f172a' : '#fff'
        var __chipBg      = __isLight ? 'rgba(99,102,241,0.08)'  : 'rgba(99,102,241,0.12)'
        var __chipText    = __isLight ? '#4338ca'                : '#a5b4fc'
        var __chipBorder  = __isLight ? 'rgba(99,102,241,0.20)'  : 'rgba(99,102,241,0.22)'
        var __closeBtnBg  = __isLight ? 'rgba(15,23,42,0.04)'    : 'rgba(255,255,255,0.04)'
        var __closeBtnBdr = __isLight ? 'rgba(15,23,42,0.08)'    : 'rgba(255,255,255,0.08)'
        var __closeBtnClr = __isLight ? '#475569'                : 'rgba(255,255,255,0.7)'
        var __footerBg    = __isLight ? 'rgba(15,23,42,0.02)'    : 'rgba(0,0,0,0.18)'
        var __cancelBtnBg = __isLight ? '#fff'                    : 'rgba(255,255,255,0.04)'
        var __cancelBdr   = __isLight ? '#e2e8f0'                : 'rgba(255,255,255,0.10)'
        var __cancelClr   = __isLight ? '#475569'                : 'rgba(255,255,255,0.8)'
        var __cancelBgHov = __isLight ? '#f1f5f9'                : 'rgba(255,255,255,0.08)'
        var __bodyTint    = __isLight
          ? 'linear-gradient(180deg, rgba(99,102,241,0.02) 0%, transparent 40%)'
          : 'linear-gradient(180deg, rgba(255,255,255,0.008) 0%, transparent 40%)'

        return createPortal(
        <div
          onClick={function(e) { if (e.target === e.currentTarget && !extrasSaving) closeExtrasModal() }}
          style={{
            position: 'absolute', inset: 0,
            background: __overlayBg,
            backdropFilter: 'blur(6px)', WebkitBackdropFilter: 'blur(6px)',
            display: 'flex', alignItems: 'center', justifyContent: 'center',
            zIndex: 50, padding: 16,
            animation: 'sqExtrasFade 160ms ease-out',
          }}
        >
          <style>{
            '@keyframes sqExtrasFade{from{opacity:0}to{opacity:1}}' +
            '@keyframes sqExtrasPop{from{opacity:0;transform:translateY(8px) scale(.985)}to{opacity:1;transform:translateY(0) scale(1)}}'
          }</style>
          <div style={{
            width: '92%', maxWidth: 820, maxHeight: '88vh',
            display: 'flex', flexDirection: 'column', overflow: 'hidden',
            borderRadius: 18,
            background: __panelBg,
            border: __panelBorder,
            boxShadow: __panelShadow,
            color: __textColor,
            animation: 'sqExtrasPop 220ms cubic-bezier(.2,.8,.3,1)',
          }}>
            {/* Ust gradient serit */}
            <div style={{
              height: 3,
              background: 'linear-gradient(90deg, #6366f1 0%, #a855f7 50%, #6366f1 100%)',
              backgroundSize: '200% 100%',
              animation: 'sqExtrasShimmer 3s linear infinite',
            }} />
            <style>{'@keyframes sqExtrasShimmer{0%{background-position:0% 0%}100%{background-position:200% 0%}}'}</style>

            {/* Header */}
            <div style={{
              display: 'flex', alignItems: 'center', gap: 14,
              padding: '16px 22px',
              borderBottom: '1px solid ' + __sepColor,
              flexShrink: 0,
            }}>
              <div style={{
                width: 40, height: 40, borderRadius: 12,
                display: 'flex', alignItems: 'center', justifyContent: 'center',
                background: 'linear-gradient(135deg, rgba(99,102,241,0.25) 0%, rgba(168,85,247,0.20) 100%)',
                border: '1px solid rgba(99,102,241,0.35)',
                boxShadow: '0 4px 16px rgba(99,102,241,0.18)',
                flexShrink: 0,
              }}>
                <Settings size={18} strokeWidth={1.8} style={{ color: __isLight ? '#4f46e5' : '#a5b4fc' }} />
              </div>
              <div style={{ flex: 1, minWidth: 0 }}>
                <div style={{ fontSize: 15, fontWeight: 700, letterSpacing: '-0.012em', color: __headTitle }}>
                  Kalem Ek Alanları
                </div>
                <div style={{ fontSize: 11.5, color: __mutedText, marginTop: 2, display: 'flex', alignItems: 'center', gap: 8 }}>
                  <span style={{
                    fontFamily: "'JetBrains Mono','Consolas',monospace",
                    fontSize: 10.5, fontWeight: 700, letterSpacing: '.04em',
                    padding: '2px 8px', borderRadius: 6,
                    background: __chipBg, color: __chipText,
                    border: '1px solid ' + __chipBorder,
                  }}>
                    {extrasModalRow.materialCode || '—'}
                  </span>
                  <span style={{ opacity: 0.55 }}>·</span>
                  <span>Satır #{extrasModalRow.id || '—'}</span>
                </div>
              </div>
              <button
                type="button"
                onClick={function() { if (!extrasSaving) closeExtrasModal() }}
                disabled={extrasSaving}
                style={{
                  background: __closeBtnBg, border: '1px solid ' + __closeBtnBdr,
                  color: __closeBtnClr, cursor: extrasSaving ? 'not-allowed' : 'pointer',
                  width: 32, height: 32, borderRadius: 10,
                  display: 'flex', alignItems: 'center', justifyContent: 'center',
                  transition: 'all .12s',
                  opacity: extrasSaving ? 0.4 : 1,
                }}
                onMouseEnter={function(e) { if (!extrasSaving) { e.currentTarget.style.background='rgba(239,68,68,0.12)'; e.currentTarget.style.color = (__isLight ? '#b91c1c' : '#fca5a5'); e.currentTarget.style.borderColor='rgba(239,68,68,0.25)' } }}
                onMouseLeave={function(e) { e.currentTarget.style.background = __closeBtnBg; e.currentTarget.style.color = __closeBtnClr; e.currentTarget.style.borderColor = __closeBtnBdr }}
                title="Kapat (Esc)"
              >
                <XIcon size={15} strokeWidth={2} />
              </button>
            </div>

            {/* Body — ust bilgiler ek alanlari paneli gibi: sol tab yok,
                butun gruplar dikey alt alta stacked, her grubun kendi
                section basligi gorunur. sqe-widget-wrap class'i ile hedeflenen
                CSS (DocumentEdit.cshtml) bu duzeni saglar. */}
            <div
              ref={extrasBodyRef}
              className="sqe-widget-wrap"
              style={{
                flex: 1, minHeight: 0, overflowY: 'auto', padding: '18px 24px',
                background: __bodyTint,
              }}
            >
              <DynamicWidgetRenderer
                ref={extrasRendererRef}
                formCode={__lineFormCode}
                /* Kaydedilmemis satirda recordId bos; renderer schema'yi yukler ama
                   server'dan value getirmez. initialValues ile daha once bu satira
                   girilmis local degerler pre-fill edilir (row.__extras). */
                recordId={extrasModalRow.id != null && Number(extrasModalRow.id) > 0 ? String(extrasModalRow.id) : ''}
                initialValues={extrasModalRow.__extras || null}
                /* Kartta inline gosterilen alanlar modalda TEKRARLANMAZ; kart
                   degerleri save payload'ina merge edilir (zorunlu-alan denetimi
                   posted dict'e bakar — kart degeri olmadan 400 doner). */
                excludeWidgetCodes={cardWidgets.map(function (w) { return w.code })}
                mergeValuesOnSave={(function () {
                  if (cardWidgets.length === 0) return null
                  var src = Object.assign({}, extrasModalRow.__widgetValues || {}, extrasModalRow.__extras || {})
                  var out = {}
                  cardWidgets.forEach(function (w) { if (w.code in src) out[w.code] = src[w.code] })
                  return out
                })()}
                /* 2026-08-05: gruplar sol sekme olarak (sekme adi = grup adi) —
                   header Ek Alanlar paneliyle ayni sidetabs standardi. */
                layout="sidetabs"
                /* Satirin standart degerleri (quantity/unitPrice/...) kural
                   scope'una girer — kalem widget kurali satir alanina referans
                   verebilir (orn. quantity > 100). Modal acilis ani snapshot'i. */
                externalScope={behaviorRowScope(extrasModalRow)}
                classPrefix="sqe"
              />
            </div>

            {/* Footer */}
            <div style={{
              display: 'flex', alignItems: 'center', justifyContent: 'space-between',
              gap: 12, padding: '14px 22px',
              borderTop: '1px solid ' + __sepColor,
              background: __footerBg,
              flexShrink: 0,
            }}>
              <div style={{ fontSize: 11.5, minHeight: 18 }}>
                {extrasToast && extrasToast.type === 'ok' && (
                  <span style={{ color: __isLight ? '#047857' : '#86efac', display: 'flex', alignItems: 'center', gap: 6 }}>
                    <span style={{ width: 6, height: 6, borderRadius: '50%', background: '#22c55e', boxShadow: '0 0 8px #22c55e' }} />
                    {extrasToast.text}
                  </span>
                )}
                {extrasToast && extrasToast.type === 'err' && (
                  <span style={{ color: __isLight ? '#b91c1c' : '#fca5a5' }}>{extrasToast.text}</span>
                )}
                {!extrasToast && (
                  <span style={{ color: __subtleText }}>
                    Değişiklikler kaydedildiğinde satıra işlenir
                  </span>
                )}
              </div>
              <div style={{ display: 'flex', gap: 8 }}>
                <button
                  type="button"
                  onClick={function() { if (!extrasSaving) closeExtrasModal() }}
                  disabled={extrasSaving}
                  style={{
                    padding: '8px 16px', borderRadius: 10,
                    background: __cancelBtnBg,
                    border: '1px solid ' + __cancelBdr,
                    color: __cancelClr,
                    fontSize: 12.5, fontWeight: 600,
                    cursor: extrasSaving ? 'not-allowed' : 'pointer',
                    opacity: extrasSaving ? 0.5 : 1,
                    transition: 'all .12s',
                  }}
                  onMouseEnter={function(e) { if (!extrasSaving) e.currentTarget.style.background = __cancelBgHov }}
                  onMouseLeave={function(e) { e.currentTarget.style.background = __cancelBtnBg }}
                >
                  İptal
                </button>
                <button
                  type="button"
                  onClick={handleExtrasSave}
                  disabled={extrasSaving}
                  style={{
                    display: 'inline-flex', alignItems: 'center', gap: 7,
                    padding: '8px 18px', borderRadius: 10,
                    background: extrasSaving
                      ? 'rgba(99,102,241,0.4)'
                      : 'linear-gradient(135deg, #6366f1 0%, #4f46e5 100%)',
                    border: '1px solid rgba(99,102,241,0.55)',
                    color: '#fff', fontSize: 12.5, fontWeight: 700,
                    cursor: extrasSaving ? 'wait' : 'pointer',
                    boxShadow: '0 4px 16px rgba(99,102,241,0.35)',
                    transition: 'all .15s',
                  }}
                  onMouseEnter={function(e) { if (!extrasSaving) e.currentTarget.style.transform='translateY(-1px)' }}
                  onMouseLeave={function(e) { e.currentTarget.style.transform='translateY(0)' }}
                >
                  {extrasSaving ? (
                    <>
                      <span style={{
                        width: 12, height: 12, border: '2px solid rgba(255,255,255,0.35)',
                        borderTopColor: '#fff', borderRadius: '50%',
                        animation: 'sqExtrasSpin 0.7s linear infinite',
                      }} />
                      Kaydediliyor…
                    </>
                  ) : 'Kaydet'}
                </button>
                <style>{'@keyframes sqExtrasSpin{to{transform:rotate(360deg)}}'}</style>
              </div>
            </div>
          </div>
        </div>,
        // Portal: satis teklif formunun body'sine (.sqe-body) absolute konumlanir.
        // .sqe-body zaten position:relative oldugu icin modal tam ortaya oturur.
        // Boylece app shell (ust bar/sol menu/alt panel) ve SQE action bar
        // modal tarafindan ortulmez, sadece sol tab navi + sag icerik ortulur.
        (document.querySelector('.sqe-body') || document.body)
      )
      })()}

      {/* ── Kisayol menusu (••• butonu dropdown'i) ────────────────────
          Butonun altinda absolute konumlanmis kucuk liste. Dis click veya
          Esc kapatir. Her item kendi tiklamasinda menuyu kapatir (navigasyon
          sonrasi state hizli temizlensin). Portal ile .sqe-body'ye cizilir
          (action bar container'inin overflow'una takilmamasi icin). */}
      {/* Portal hep mount; AnimatePresence dropdown'in giris/cikis animasyonunu handle eder */}
      {createPortal(
        <AnimatePresence>{shortcutsMenu && (function () {
          var srow = shortcutsMenu.row
          var pos = shortcutsMenu.pos || { top: 0, left: 0, width: 200 }
          var itemId = srow && (srow.stockCardId || srow.itemId)

          function close() { setShortcutsMenu(null) }
          // Shell API'sine guvenli erisim — iframe icindeyken window.top.CalibraHub
          // varsa onu, yoksa fallback olarak yerel navigation'i kullan.
          function openInWorkspaceTab(url, title, matchPath) {
            try {
              var topWin = window.top || window
              if (topWin && topWin.CalibraHub && typeof topWin.CalibraHub.openWorkspaceTab === 'function') {
                topWin.CalibraHub.openWorkspaceTab({ url: url, title: title, matchPath: matchPath })
                return
              }
            } catch (_) { /* cross-origin */ }
            navigateInWorkspace(url)
          }
          function goToStockCard() {
            close()
            if (!itemId) {
              // Rapor §6.6 — toast fallback
              var m = 'Bu satirda malzeme secilmedi — stok kartina gidilemedi.'
              if (window.CalibraHub && window.CalibraHub.toast) window.CalibraHub.toast(m, 'warn')
              else alert(m)
              return
            }
            openInWorkspaceTab(
              '/Logistics/MaterialCardEdit?id=' + itemId,
              'Malzeme Kartlari',
              '/Logistics/MaterialCard',
            )
          }
          function goToPriceList() {
            close()
            // Fiyat listesi — materialCode hint olarak gecirilir, PriceList sayfasi
            // ileride bu paramayi kullanip ilgili satirin fiyat gecmisini auto-expand
            // edebilir. Suan icin fiyat girisi/listeleme sayfasi yeni tab'ta acilir.
            var matCode = srow && srow.materialCode ? String(srow.materialCode) : ''
            var url = '/PriceList/PriceList' + (matCode ? '?stockCode=' + encodeURIComponent(matCode) : '')
            openInWorkspaceTab(url, 'Fiyat Listesi', '/PriceList/PriceList')
          }
          function toggleNoteFromMenu() {
            close()
            if (!srow || !canModify(srow)) return
            toggleNote(srow._uid)
          }
          function openReviseFromMenu() {
            close()
            if (!srow) return
            setReviseModal({
              row: srow,
              tab: 'revise',
              // Sadelestirilmis revize akisi: kullanici sadece ACIKLAMA girer
              // (ESKI satira not olarak eklenir). Yeni satir aynen kopyalanarak
              // alta eklenir; degisiklikler gridden yapilir.
              draft: { notes: '' },
            })
          }
          function openCostViewerFromMenu() {
            close()
            if (!srow || !srow.materialCode) return
            setCostViewer({
              materialCode: srow.materialCode,
              configCode:   srow.combinationCode || null,
              quantity:     Number(srow.quantity) || 1,
              materialName: srow.materialName || '',
            })
          }
          // PageComment Seq 18: Ihtiyac Kaydi kalem satirinin karsilama defteri kayitlarini
          // gosteren modal. Kaydedilmemis (id'siz) satirin henuz defter kaydi olamaz — buton
          // items dizisinde disabled olarak isaretlenir, buraya tiklanamaz normal akiste.
          function openFulfillmentDetailFromMenu() {
            close()
            if (!srow || srow.id == null || Number(srow.id) <= 0) return
            setFulfillmentDetail({
              lineId:       Number(srow.id),
              materialCode: srow.materialCode || '',
              materialName: srow.materialName || '',
            })
          }

          // Not durumuna gore label + ikon degisir.
          var noteDisabled = !srow || !canModify(srow) || (belowColumns.length === 0)
          var noteHasContent = srow ? hasAnyBelowValue(srow) : false
          var noteOpen = srow ? isNoteOpen(srow) : false
          var noteLabel = noteOpen
            ? 'Notu Gizle'
            : (noteHasContent ? 'Notu Goster' : 'Not Ekle')
          var noteDisabledTitle = (!srow || !canModify(srow))
            ? 'Once kilidi acin'
            : (belowColumns.length === 0 ? 'Bu grid icin not alani tanimli degil' : '')

          // Revize: satir kilidinden bagimsiz calisir. Kayitsiz (id yok) satir icin de
          // menu acilir ama createRevision icinde id yoksa uyari veriliyor — UX tutarli.
          var reviseHasParent = srow && srow.id != null && Number(srow.id) > 0 &&
            rows.some(function (r) { return r.revisedFromId != null && Number(r.revisedFromId) === Number(srow.id) })
          var reviseLabel = reviseHasParent ? 'Revize Et / Gecmisi Goster' : 'Revize Et'

          // Item tanimi — ileride yeni kisayollar buraya eklenir (barkod bas,
          // stok hareketleri vb.). Her item bir aksent renge sahip (icon pill
          // ve hover vurgusu icin), ve grup ayraci `groupBefore` ile baslayan
          // ilk item'larda belirir. Aksiyonlar 2 grupta: Navigasyon (kart/fiyat)
          // ve Satir Islemi (not/revize).
          var items = [
            {
              key: 'stock-card',
              label: 'Stok Kartina Git',
              hint: 'Yeni sekme',
              icon: ExternalLink,
              accent: 'indigo',
              hasArrow: true,
              onClick: goToStockCard,
              disabled: !itemId,
              disabledTitle: 'Once malzeme seciniz',
            },
            {
              key: 'price-list',
              label: 'Fiyat Gecmisi',
              hint: 'Fiyat Listesi',
              icon: History,
              accent: 'emerald',
              hasArrow: true,
              onClick: goToPriceList,
              disabled: false,
            },
            {
              key: 'cost-view',
              label: 'Maliyet Gör',
              hint: 'Reçete fiyat',
              icon: Calculator,
              accent: 'amber',
              groupBefore: true,
              onClick: openCostViewerFromMenu,
              disabled: !srow || !srow.materialCode,
              disabledTitle: 'Önce malzeme seçin',
            },
            {
              key: 'note',
              label: noteLabel,
              icon: StickyNote,
              accent: 'amber',
              onClick: toggleNoteFromMenu,
              disabled: noteDisabled,
              disabledTitle: noteDisabledTitle,
            },
            {
              key: 'revise',
              label: reviseLabel,
              icon: GitBranch,
              accent: 'violet',
              onClick: openReviseFromMenu,
              disabled: false,
            },
          ]
          // İhtiyaç Kaydi (alis_talebi) + Sayım (INVENTORY_COUNT): sadece Stok Kartina Git + Not Ekle.
          // Fiyat Geçmişi / Maliyet Gör / Revize Et bu bağlamlarda anlamsız (fiyatlandırma teklif/
          // sipariş aşamasında oluşur; sayımda yalnız miktar sayılır).
          if (__hidePricingFeatures) {
            items = items.filter(function (it) {
              return it.key === 'stock-card' || it.key === 'note'
            })
          }
          // PageComment Seq 18 (2026-07-21): "Ihtiyac karsilama kalem bilgilerindeki kalem
          // bazinda islemler butonuna karsilama detayi menusu ekleyebilir misin." Yalnizca
          // Ihtiyac Kaydi'nda (alis_talebi) gorunur — Sayim'da (__isInventoryCount) karsilama
          // kavrami yok, bu yuzden __hidePricingFeatures degil __isPurchaseRequest ile kosullanir.
          if (__isPurchaseRequest) {
            items.push({
              key: 'fulfillment-detail',
              label: 'Karşılama Detayı',
              hint: 'Karşılama Defteri',
              icon: Layers,
              accent: 'sky',
              groupBefore: true,
              onClick: openFulfillmentDetailFromMenu,
              disabled: !srow || srow.id == null || Number(srow.id) <= 0,
              disabledTitle: 'Önce satırı kaydedin',
            })
          }
          // Aksent renk haritasi — icon pill bg / text + hover bg.
          // Light/dark farkli paletler. Tema body class'i ile alginirir; iframe icinde
          // mount edilmis ise parent dokumanin body class'ina da bakilir (workspace
          // iframe'leri tema sinifini bazen biraz sonra alir — fallback chain). Default light.
          var __isLight = (function () {
            if (typeof document === 'undefined') return true
            try {
              if (document.body.classList.contains('app-theme-light')) return true
              if (document.body.classList.contains('app-theme-dark'))  return false
              if (window.parent && window.parent !== window && window.parent.document && window.parent.document.body) {
                if (window.parent.document.body.classList.contains('app-theme-light')) return true
                if (window.parent.document.body.classList.contains('app-theme-dark'))  return false
              }
            } catch (_) {}
            return true   // hicbir class yoksa light varsayilan
          })()
          var accentMap = __isLight ? {
            indigo:  { bg: 'rgba(99,102,241,0.10)',  text: '#4f46e5', hoverBg: 'rgba(99,102,241,0.10)',  hoverShadow: 'rgba(99,102,241,0.22)' },
            emerald: { bg: 'rgba(16,185,129,0.10)',  text: '#047857', hoverBg: 'rgba(16,185,129,0.10)',  hoverShadow: 'rgba(16,185,129,0.22)' },
            amber:   { bg: 'rgba(245,158,11,0.12)',  text: '#b45309', hoverBg: 'rgba(245,158,11,0.10)',  hoverShadow: 'rgba(245,158,11,0.25)' },
            violet:  { bg: 'rgba(139,92,246,0.10)',  text: '#6d28d9', hoverBg: 'rgba(139,92,246,0.10)',  hoverShadow: 'rgba(139,92,246,0.22)' },
            sky:     { bg: 'rgba(14,165,233,0.10)',  text: '#0369a1', hoverBg: 'rgba(14,165,233,0.10)',  hoverShadow: 'rgba(14,165,233,0.22)' },
            slate:   { bg: 'rgba(148,163,184,0.14)', text: '#475569', hoverBg: 'rgba(148,163,184,0.10)', hoverShadow: 'rgba(148,163,184,0.22)' },
          } : {
            indigo:  { bg: 'rgba(99,102,241,0.18)',  text: '#a5b4fc', hoverBg: 'rgba(99,102,241,0.12)',  hoverShadow: 'rgba(99,102,241,0.20)' },
            emerald: { bg: 'rgba(16,185,129,0.18)',  text: '#6ee7b7', hoverBg: 'rgba(16,185,129,0.10)',  hoverShadow: 'rgba(16,185,129,0.18)' },
            amber:   { bg: 'rgba(245,158,11,0.18)',  text: '#fcd34d', hoverBg: 'rgba(245,158,11,0.10)',  hoverShadow: 'rgba(245,158,11,0.18)' },
            violet:  { bg: 'rgba(139,92,246,0.20)',  text: '#c4b5fd', hoverBg: 'rgba(139,92,246,0.10)',  hoverShadow: 'rgba(139,92,246,0.20)' },
            sky:     { bg: 'rgba(14,165,233,0.18)',  text: '#7dd3fc', hoverBg: 'rgba(14,165,233,0.10)',  hoverShadow: 'rgba(14,165,233,0.18)' },
            slate:   { bg: 'rgba(148,163,184,0.18)', text: '#cbd5e1', hoverBg: 'rgba(148,163,184,0.10)', hoverShadow: 'rgba(148,163,184,0.18)' },
          }
          // Tema-bagli surface/text degerleri — paneli ve item rengini saran ortak palet
          var __menuBg     = __isLight ? 'linear-gradient(180deg, #ffffff 0%, #f8fafc 100%)' : 'linear-gradient(180deg, rgba(28,32,48,0.97) 0%, rgba(20,24,38,0.97) 100%)'
          var __menuBorder = __isLight ? '1px solid rgba(99,102,241,0.18)' : '1px solid rgba(255,255,255,0.08)'
          var __menuShadow = __isLight
            ? '0 12px 36px rgba(15,23,42,0.14), 0 4px 14px rgba(99,102,241,0.10), inset 0 1px 0 rgba(255,255,255,0.6)'
            : '0 20px 60px rgba(0,0,0,0.55), 0 6px 20px rgba(99,102,241,0.18), inset 0 1px 0 rgba(255,255,255,0.06)'
          var __sepColor   = __isLight ? 'rgba(15,23,42,0.08)' : 'rgba(255,255,255,0.10)'
          var __textColor      = __isLight ? '#0f172a'             : 'rgba(255,255,255,0.92)'
          var __textColorDis   = __isLight ? '#94a3b8'             : 'rgba(255,255,255,0.35)'
          var __hintColor      = __isLight ? '#64748b'             : 'rgba(255,255,255,0.42)'
          var __hintColorDis   = __isLight ? '#cbd5e1'             : 'rgba(255,255,255,0.25)'
          var __chevronColor   = __isLight ? '#94a3b8'             : 'rgba(255,255,255,0.30)'

          return (
            <>
              {/* Gorunmez overlay — dis click ile kapat */}
              <motion.div
                key="sm-shortcuts-overlay"
                onClick={close}
                initial={{ opacity: 0 }}
                animate={{ opacity: 1 }}
                exit={{ opacity: 0 }}
                transition={{ duration: 0.12 }}
                style={{
                  position: 'fixed', inset: 0, zIndex: 9998,
                  background: 'transparent',
                }}
              />
              {/* Menu — butonun altina konumla, spring entrance + child stagger */}
              <motion.div
                key="sm-shortcuts-menu"
                role="menu"
                aria-label="Satir kisayol menusu"
                onKeyDown={function (e) { if (e.key === 'Escape') close() }}
                initial={{ opacity: 0, y: -10, scale: 0.94 }}
                animate={{ opacity: 1, y: 0, scale: 1 }}
                exit={{ opacity: 0, y: -8, scale: 0.96 }}
                transition={{ type: 'spring', stiffness: 420, damping: 28, mass: 0.6 }}
                style={{
                  position: 'fixed',
                  top: pos.top, left: pos.left,
                  minWidth: Math.max(pos.width, 240),
                  zIndex: 9999,
                  borderRadius: 14,
                  padding: 6,
                  // Glassmorphism — tema-bagli zemin + ic isik + cam efekti
                  background: __menuBg,
                  border: __menuBorder,
                  boxShadow: __menuShadow,
                  backdropFilter: 'blur(18px) saturate(140%)',
                  WebkitBackdropFilter: 'blur(18px) saturate(140%)',
                  display: 'flex', flexDirection: 'column',
                  transformOrigin: 'top left',
                }}
              >
                {/* Renkli ust aksent cizgisi — cam yansimasi gibi durur. Light temada
                    daha hafif tonda, dark temada daha belirgin. */}
                <div aria-hidden="true" style={{
                  position: 'absolute', top: 0, left: 14, right: 14, height: 1,
                  background: __isLight
                    ? 'linear-gradient(90deg, transparent 0%, rgba(99,102,241,0.40) 30%, rgba(168,85,247,0.40) 70%, transparent 100%)'
                    : 'linear-gradient(90deg, transparent 0%, rgba(99,102,241,0.55) 30%, rgba(168,85,247,0.55) 70%, transparent 100%)',
                  pointerEvents: 'none',
                }} />
                {items.map(function (it, idx) {
                  var Icon = it.icon
                  var pal = accentMap[it.accent] || accentMap.slate
                  // Custom render with stagger via per-item motion
                  return (
                    <span key={it.key} style={{ display: 'contents' }}>
                      {it.groupBefore && (
                        <motion.div
                          aria-hidden="true"
                          initial={{ opacity: 0, scaleX: 0.6 }}
                          animate={{ opacity: 1, scaleX: 1 }}
                          transition={{ delay: 0.04 * idx + 0.05, duration: 0.18, ease: [0.23, 1, 0.32, 1] }}
                          style={{
                            height: 1, margin: '4px 8px',
                            background: 'linear-gradient(90deg, transparent, ' + __sepColor + ', transparent)',
                            transformOrigin: 'left center',
                          }}
                        />
                      )}
                      <motion.button
                        type="button"
                        role="menuitem"
                        disabled={!!it.disabled}
                        onClick={it.onClick}
                        title={it.disabled ? (it.disabledTitle || '') : (it.title || '')}
                        initial={{ opacity: 0, x: -6 }}
                        animate={{ opacity: 1, x: 0 }}
                        transition={{ delay: 0.04 * idx + 0.06, duration: 0.22, ease: [0.23, 1, 0.32, 1] }}
                        whileHover={!it.disabled ? { x: 2 } : {}}
                        whileTap={!it.disabled ? { scale: 0.985 } : {}}
                        style={{
                          display: 'flex', alignItems: 'center', gap: 11,
                          padding: '8px 10px 8px 8px',
                          fontSize: 12.75, fontWeight: 600, letterSpacing: '-0.005em',
                          color: it.disabled ? __textColorDis : __textColor,
                          background: 'transparent', border: 'none',
                          borderRadius: 9,
                          cursor: it.disabled ? 'not-allowed' : 'pointer',
                          textAlign: 'left',
                          transition: 'background .14s ease, box-shadow .14s ease, color .14s ease',
                          position: 'relative',
                        }}
                        onMouseEnter={function (e) {
                          if (it.disabled) return
                          e.currentTarget.style.background = pal.hoverBg
                          e.currentTarget.style.boxShadow = '0 0 0 1px ' + pal.hoverShadow + ' inset'
                          e.currentTarget.style.color = pal.text
                        }}
                        onMouseLeave={function (e) {
                          e.currentTarget.style.background = 'transparent'
                          e.currentTarget.style.boxShadow = 'none'
                          e.currentTarget.style.color = it.disabled ? __textColorDis : __textColor
                        }}
                      >
                        {/* Icon pill — accent renkli kucuk yuvarlak kare */}
                        <span style={{
                          flexShrink: 0,
                          width: 26, height: 26,
                          borderRadius: 8,
                          display: 'inline-flex', alignItems: 'center', justifyContent: 'center',
                          background: it.disabled ? (__isLight ? 'rgba(148,163,184,0.18)' : 'rgba(148,163,184,0.10)') : pal.bg,
                          color: it.disabled ? __textColorDis : pal.text,
                          border: '1px solid ' + (it.disabled ? (__isLight ? 'rgba(148,163,184,0.20)' : 'rgba(148,163,184,0.10)') : 'transparent'),
                          transition: 'transform .14s ease',
                        }}>
                          <Icon size={14} strokeWidth={1.9} />
                        </span>
                        {/* Label + opsiyonel ipucu metni */}
                        <span style={{ flex: 1, display: 'flex', flexDirection: 'column', gap: 1, minWidth: 0 }}>
                          <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                            {it.label}
                          </span>
                          {it.hint && (
                            <span style={{
                              fontSize: 10, fontWeight: 500, letterSpacing: '.02em',
                              color: it.disabled ? __hintColorDis : __hintColor,
                              overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap',
                            }}>
                              {it.hint}
                            </span>
                          )}
                        </span>
                        {/* Sag chevron — navigasyon item'larini isaret eder */}
                        {it.hasArrow && !it.disabled && (
                          <ChevronRight size={13} strokeWidth={2} style={{
                            flexShrink: 0,
                            color: __chevronColor,
                            transition: 'transform .14s ease, color .14s ease',
                          }} />
                        )}
                      </motion.button>
                    </span>
                  )
                })}
              </motion.div>
            </>
          )
        })()}</AnimatePresence>,
        (document.querySelector('.sqe-body') || document.body)
      )}

      {/* ── Revize modal'i ─────────────────────────────────────────────
          Satir aksiyon seridindeki Revize butonuna basildiginda acilir.
          Iki sekme:
            - Revize Et: miktar/birim fiyat/iskonto/not degisiklikleri icin form
            - Gecmis Revizeler: revised_from_id zinciri geriye takip edilerek listelenir
          Revize Olustur tiklaninca yeni bir satir eklenir — orijinal satir
          degismez; yeni satirin revisedFromId alani secili satirin id'sine
          set edilir (satir kayitsizsa uyari: once ana belgeyi kaydet). */}
      {reviseModal && createPortal(
        (function () {
          var row = reviseModal.row
          var activeTab = reviseModal.tab || 'revise'
          var draft = reviseModal.draft || {}

          // Revize zinciri — kok (orijinal) -> son revizelere dogru
          // Yeni yon: eski satirlarda revisedFromId = daha yeni satirin id'si
          // row (aktif, revisedFromId=null) baslangic; gerideki surumleri bul
          var chain = []
          var seen = {}
          var cur = row
          if (cur) {
            seen[cur._uid] = true
            if (cur.id) seen['id:' + cur.id] = true
            chain.push(cur)
          }
          var guard = 0
          while (cur && cur.id && Number(cur.id) > 0 && guard < 50) {
            guard++
            var curId = Number(cur.id)
            var predecessor = rows.find(function (r) {
              return r.revisedFromId != null && Number(r.revisedFromId) === curId && !seen[r._uid]
            })
            if (!predecessor) break
            seen[predecessor._uid] = true
            if (predecessor.id) seen['id:' + predecessor.id] = true
            chain.push(predecessor)
            cur = predecessor
          }
          chain.reverse() // orijinal en basta, bu satir en altta
          var chainIndex = chain.length - 1 // bu satirin pozisyonu (0 = orijinal)
          var hasRevisionParent = chain.length > 1

          // Tarih/para formatlamak icin kisa yardimci — satir icinde inline
          var fmtNum = function (n) {
            if (n == null || n === '') return '-'
            var x = Number(n)
            if (!isFinite(x)) return String(n)
            return x.toLocaleString('tr-TR', { maximumFractionDigits: 4 })
          }

          function close() { setReviseModal(null) }
          function setDraft(key, val) {
            setReviseModal(function (m) {
              if (!m) return m
              var nd = Object.assign({}, m.draft); nd[key] = val
              return Object.assign({}, m, { draft: nd })
            })
          }
          // Cok alanli guncellemeler icin — CombinationLookupCell onChange
          // (key, value, fill) imzasinda fill ile ek alanlar dolduruyor;
          // setDraft ile ayri ayri yaparsak React batching sorunu olusabilir.
          function mergeDraft(patch) {
            setReviseModal(function (m) {
              if (!m) return m
              var nd = Object.assign({}, m.draft, patch || {})
              return Object.assign({}, m, { draft: nd })
            })
          }
          function setTab(tab) {
            setReviseModal(function (m) { return m ? Object.assign({}, m, { tab: tab }) : m })
          }
          function createRevision() {
            var parentId = Number(row.id)
            if (!parentId || parentId <= 0) {
              // Rapor §6.6 — toast fallback
              var m0 = 'Once ana belgeyi kaydedin — kayitli olmayan satir revize edilemez.'
              if (window.CalibraAlert && window.CalibraAlert.warn) window.CalibraAlert.warn(m0)
              else if (window.CalibraHub && window.CalibraHub.toast) window.CalibraHub.toast(m0, 'warn')
              else alert(m0)
              return
            }
            // Server-side atomik revize — /Sales/ReviseLine tek transaction:
            //   1) Eski satirin notes'unu @Description ile guncelle
            //   2) Yeni satiri INSERT (eski'nin birebir kopyasi + revised_from_id)
            //   3) Kombinasyon detaylari + widget/alan degerleri de kopyalanir
            // Basari halinde grid sunucudan taze verilerle yeniden yuklenir.
            var reviseNote = (draft.notes != null ? String(draft.notes) : '').trim()
            setReviseModal(function (m) { return m ? Object.assign({}, m, { saving: true }) : m })

            var token = (document.querySelector('input[name="__RequestVerificationToken"]') || {}).value || ''
            var headers = { 'Content-Type': 'application/json' }
            if (token) headers['RequestVerificationToken'] = token

            fetch('/Sales/ReviseLine', {
              method: 'POST',
              credentials: 'same-origin',
              headers: headers,
              body: JSON.stringify({ parentLineId: parentId, description: reviseNote })
            })
              .then(function (resp) { return resp.json() })
              .then(function (data) {
                if (!data || data.success !== true) {
                  // Rapor §6.6 — toast fallback
                  var m1 = 'Revize basarisiz: ' + (data && data.message ? data.message : 'bilinmeyen hata')
                  if (window.CalibraAlert && window.CalibraAlert.error) window.CalibraAlert.error(m1)
                  else if (window.CalibraHub && window.CalibraHub.toast) window.CalibraHub.toast(m1, 'err')
                  else alert(m1)
                  setReviseModal(function (m) { return m ? Object.assign({}, m, { saving: false }) : m })
                  return
                }
                close()
                // Grid'i sunucudan yeniden yukle — sayfa helper'i varsa oradan, yoksa elle.
                if (typeof window.sqReloadLinesFromServer === 'function') {
                  window.sqReloadLinesFromServer()
                  return
                }
                var docId = (rows.find(function (r) { return Number(r.documentId) > 0 }) || {}).documentId || null
                if (!docId) return
                fetch('/Sales/GetQuote?id=' + docId, { credentials: 'same-origin' })
                  .then(function (r2) { return r2.json() })
                  .then(function (q) {
                    if (!q || !Array.isArray(q.lines)) return
                    var mats = (typeof window !== 'undefined' && Array.isArray(window.__SQ_MATERIALS__)) ? window.__SQ_MATERIALS__ : []
                    var synced = q.lines.map(function (ln) {
                      var m = mats.find(function (x) { return x.id === ln.itemId })
                      return Object.assign({}, ln, {
                        stockCardId:       ln.itemId,
                        trackCombinations: m ? m.trackCombinations === true : false,
                        taxRate:           ln.taxRate != null ? ln.taxRate : (m && m.taxRate != null ? m.taxRate : 20),
                      })
                    })
                    setRows(synced)
                  })
                  .catch(function () { /* swallow */ })
              })
              .catch(function (err) {
                var m2 = 'Revize hatasi: ' + (err && err.message ? err.message : String(err))
                if (window.CalibraAlert && window.CalibraAlert.error) window.CalibraAlert.error(m2)
                else if (window.CalibraHub && window.CalibraHub.toast) window.CalibraHub.toast(m2, 'err')
                else alert(m2)
                setReviseModal(function (m) { return m ? Object.assign({}, m, { saving: false }) : m })
              })
          }

          // Tema tespiti — extras modal (Kalem Ek Alanlari, ~satir 2358) ile ayni zincir.
          var __isLight = (function () {
            if (typeof document === 'undefined') return true
            try {
              if (document.body.classList.contains('app-theme-light')) return true
              if (document.body.classList.contains('app-theme-dark'))  return false
              if (window.parent && window.parent !== window && window.parent.document && window.parent.document.body) {
                if (window.parent.document.body.classList.contains('app-theme-light')) return true
                if (window.parent.document.body.classList.contains('app-theme-dark'))  return false
              }
            } catch (_) {}
            return true
          })()
          var __overlayBg  = __isLight
            ? 'radial-gradient(at 20% 10%, rgba(139,92,246,0.06) 0%, transparent 45%), radial-gradient(at 85% 85%, rgba(99,102,241,0.05) 0%, transparent 45%), rgba(15,23,42,0.35)'
            : 'radial-gradient(at 20% 10%, rgba(139,92,246,0.12) 0%, transparent 45%), radial-gradient(at 85% 85%, rgba(99,102,241,0.10) 0%, transparent 45%), rgba(3,6,15,0.72)'
          var __panelBg     = __isLight
            ? 'linear-gradient(180deg, #ffffff 0%, #f8fafc 100%)'
            : 'linear-gradient(180deg, rgba(23,28,42,0.98) 0%, rgba(15,19,30,0.98) 100%)'
          var __panelBorder = __isLight ? '1px solid #e2e8f0' : '1px solid rgba(255,255,255,0.10)'
          var __panelShadow = __isLight
            ? '0 16px 48px rgba(15,23,42,0.18), 0 0 0 1px rgba(139,92,246,0.08)'
            : '0 32px 96px rgba(0,0,0,0.65), 0 0 0 1px rgba(139,92,246,0.10)'
          var __textColor   = __isLight ? '#0f172a' : 'rgba(255,255,255,0.92)'
          var __mutedText   = __isLight ? '#64748b' : 'rgba(255,255,255,0.5)'
          var __subtleText  = __isLight ? '#94a3b8' : 'rgba(255,255,255,0.35)'
          var __sepColor    = __isLight ? '#e2e8f0' : 'rgba(255,255,255,0.06)'
          var __headTitle   = __isLight ? '#0f172a' : '#fff'
          var __accentText  = __isLight ? '#7c3aed' : '#c4b5fd'
          var __chipBg      = __isLight ? 'rgba(139,92,246,0.08)' : 'rgba(139,92,246,0.14)'
          var __chipBorder  = __isLight ? 'rgba(139,92,246,0.22)' : 'rgba(139,92,246,0.25)'
          var __revizeBadgeBg     = __isLight ? 'rgba(99,102,241,0.10)' : 'rgba(99,102,241,0.15)'
          var __revizeBadgeText   = __isLight ? '#4338ca' : '#a5b4fc'
          var __revizeBadgeBorder = __isLight ? 'rgba(99,102,241,0.24)' : 'rgba(99,102,241,0.28)'
          var __closeBtnBg    = __isLight ? 'rgba(15,23,42,0.04)' : 'rgba(255,255,255,0.04)'
          var __closeBtnBdr   = __isLight ? 'rgba(15,23,42,0.08)' : 'rgba(255,255,255,0.08)'
          var __closeBtnClr   = __isLight ? '#475569' : 'rgba(255,255,255,0.7)'
          var __closeHoverClr = __isLight ? '#b91c1c' : '#fca5a5'
          var __tabBadgeActiveBg    = __isLight ? 'rgba(139,92,246,0.16)' : 'rgba(139,92,246,0.25)'
          var __tabBadgeActiveClr   = __isLight ? '#6d28d9' : '#ddd6fe'
          var __tabBadgeInactiveBg  = __isLight ? 'rgba(15,23,42,0.06)' : 'rgba(255,255,255,0.08)'
          var __tabBadgeInactiveClr = __isLight ? '#64748b' : 'rgba(255,255,255,0.65)'
          var __inputBorder = __isLight ? '#e2e8f0' : 'rgba(255,255,255,0.14)'
          var __inputBg     = __isLight ? '#f8fafc' : 'rgba(10,14,24,0.55)'
          var __inputText   = __isLight ? '#0f172a' : 'rgba(255,255,255,0.95)'
          var __focusBorder = __isLight ? 'rgba(124,58,237,0.55)' : 'rgba(139,92,246,0.65)'
          var __focusShadow = __isLight ? '0 0 0 3px rgba(124,58,237,0.14)' : '0 0 0 3px rgba(139,92,246,0.18)'
          var __noteBg     = __isLight ? 'rgba(15,23,42,0.03)' : 'rgba(255,255,255,0.025)'
          var __noteBorder = __isLight ? 'rgba(15,23,42,0.14)' : 'rgba(255,255,255,0.10)'
          var __chainCurrentBg = __isLight
            ? 'linear-gradient(135deg, rgba(139,92,246,0.10), rgba(99,102,241,0.07))'
            : 'linear-gradient(135deg, rgba(139,92,246,0.14), rgba(99,102,241,0.10))'
          var __chainDefaultBg     = __isLight ? 'rgba(15,23,42,0.02)' : 'rgba(255,255,255,0.025)'
          var __chainCurrentBorder = __isLight ? 'rgba(139,92,246,0.30)' : 'rgba(139,92,246,0.35)'
          var __chainDefaultBorder = __isLight ? 'rgba(15,23,42,0.08)' : 'rgba(255,255,255,0.06)'
          var __origBadgeText   = __isLight ? '#047857' : '#86efac'
          var __origBadgeBorder = __isLight ? 'rgba(34,197,94,0.30)' : 'rgba(34,197,94,0.28)'
          var __violetBadgeBorder = __isLight ? 'rgba(139,92,246,0.30)' : 'rgba(139,92,246,0.28)'
          var __currentPillBg  = __isLight ? 'rgba(139,92,246,0.16)' : 'rgba(139,92,246,0.28)'
          var __currentPillClr = __isLight ? '#6d28d9' : '#ddd6fe'
          var __detailText = __isLight ? '#334155' : 'rgba(255,255,255,0.75)'
          var __cancelBtnBg = __isLight ? '#fff' : 'rgba(255,255,255,0.04)'
          var __cancelBdr   = __isLight ? '#e2e8f0' : 'rgba(255,255,255,0.10)'
          var __cancelClr   = __isLight ? '#475569' : 'rgba(255,255,255,0.82)'
          var __cancelBgHov = __isLight ? '#f1f5f9' : 'rgba(255,255,255,0.09)'

          return (
            <div
              onClick={function (e) { if (e.target === e.currentTarget) close() }}
              style={{
                position: 'absolute', inset: 0,
                background: __overlayBg,
                backdropFilter: 'blur(6px)', WebkitBackdropFilter: 'blur(6px)',
                display: 'flex', alignItems: 'center', justifyContent: 'center',
                zIndex: 55, padding: 16,
                animation: 'sqExtrasFade 160ms ease-out',
              }}
            >
              <div style={{
                width: '92%', maxWidth: 720, maxHeight: '88vh',
                display: 'flex', flexDirection: 'column', overflow: 'hidden',
                borderRadius: 18,
                background: __panelBg,
                border: __panelBorder,
                boxShadow: __panelShadow,
                color: __textColor,
                animation: 'sqExtrasPop 220ms cubic-bezier(.2,.8,.3,1)',
              }}>
                {/* Ust gradient serit — mor/indigo tonlari */}
                <div style={{
                  height: 3,
                  background: 'linear-gradient(90deg, #8b5cf6 0%, #6366f1 50%, #8b5cf6 100%)',
                  backgroundSize: '200% 100%',
                  animation: 'sqExtrasShimmer 3s linear infinite',
                }} />

                {/* Header */}
                <div style={{
                  display: 'flex', alignItems: 'center', gap: 14,
                  padding: '16px 22px',
                  borderBottom: '1px solid ' + __sepColor,
                  flexShrink: 0,
                }}>
                  <div style={{
                    width: 40, height: 40, borderRadius: 12,
                    display: 'flex', alignItems: 'center', justifyContent: 'center',
                    background: 'linear-gradient(135deg, rgba(139,92,246,0.25) 0%, rgba(99,102,241,0.20) 100%)',
                    border: '1px solid rgba(139,92,246,0.35)',
                    boxShadow: '0 4px 16px rgba(139,92,246,0.18)',
                    flexShrink: 0,
                  }}>
                    <GitBranch size={18} strokeWidth={1.9} style={{ color: __accentText }} />
                  </div>
                  <div style={{ flex: 1, minWidth: 0 }}>
                    <div style={{ fontSize: 15, fontWeight: 700, letterSpacing: '-0.012em', color: __headTitle }}>
                      Satir Revizyonu
                    </div>
                    <div style={{ fontSize: 11.5, color: __mutedText, marginTop: 2, display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
                      <span style={{
                        fontFamily: "'JetBrains Mono','Consolas',monospace",
                        fontSize: 10.5, fontWeight: 700, letterSpacing: '.04em',
                        padding: '2px 8px', borderRadius: 6,
                        background: __chipBg, color: __accentText,
                        border: '1px solid ' + __chipBorder,
                      }}>
                        {row.materialCode || '—'}
                      </span>
                      <span style={{ opacity: 0.6 }}>·</span>
                      <span>{row.materialName || '—'}</span>
                      {hasRevisionParent && (
                        <>
                          <span style={{ opacity: 0.5 }}>·</span>
                          <span style={{
                            fontSize: 10.5, fontWeight: 700, letterSpacing: '.04em',
                            padding: '2px 7px', borderRadius: 6,
                            background: __revizeBadgeBg, color: __revizeBadgeText,
                            border: '1px solid ' + __revizeBadgeBorder,
                          }}>
                            {chain.length - 1 > 0 ? (chain.length - 1) + '. Revize' : 'Orijinal'}
                          </span>
                        </>
                      )}
                    </div>
                  </div>
                  <button
                    type="button"
                    onClick={close}
                    style={{
                      background: __closeBtnBg, border: '1px solid ' + __closeBtnBdr,
                      color: __closeBtnClr, cursor: 'pointer',
                      width: 32, height: 32, borderRadius: 10,
                      display: 'flex', alignItems: 'center', justifyContent: 'center',
                      transition: 'all .12s',
                    }}
                    onMouseEnter={function (e) { e.currentTarget.style.background='rgba(239,68,68,0.12)'; e.currentTarget.style.color=__closeHoverClr; e.currentTarget.style.borderColor='rgba(239,68,68,0.25)' }}
                    onMouseLeave={function (e) { e.currentTarget.style.background=__closeBtnBg; e.currentTarget.style.color=__closeBtnClr; e.currentTarget.style.borderColor=__closeBtnBdr }}
                    title="Kapat (Esc)"
                  >
                    <XIcon size={15} strokeWidth={2} />
                  </button>
                </div>

                {/* Tab bar */}
                <div style={{
                  display: 'flex', gap: 6,
                  padding: '10px 22px 0',
                  borderBottom: '1px solid ' + __sepColor,
                  flexShrink: 0,
                }}>
                  {[
                    { k: 'revise', label: 'Revize Et', icon: GitBranch },
                    { k: 'history', label: 'Gecmis Revizeler', icon: History, badge: chain.length > 1 ? chain.length : null },
                  ].map(function (t) {
                    var T = t.icon
                    var active = activeTab === t.k
                    return (
                      <button
                        key={t.k}
                        type="button"
                        onClick={function () { setTab(t.k) }}
                        style={{
                          display: 'inline-flex', alignItems: 'center', gap: 7,
                          padding: '9px 14px',
                          border: 'none',
                          borderBottom: active ? '2px solid ' + __accentText : '2px solid transparent',
                          background: 'transparent',
                          color: active ? __headTitle : __mutedText,
                          fontSize: 12.5, fontWeight: active ? 700 : 600,
                          letterSpacing: '-0.005em',
                          cursor: 'pointer',
                          transition: 'color .15s, border-color .15s',
                          marginBottom: -1,
                        }}
                      >
                        <T size={13} strokeWidth={2} />
                        {t.label}
                        {t.badge && (
                          <span style={{
                            fontSize: 10, fontWeight: 700,
                            padding: '1px 6px', borderRadius: 8,
                            background: active ? __tabBadgeActiveBg : __tabBadgeInactiveBg,
                            color: active ? __tabBadgeActiveClr : __tabBadgeInactiveClr,
                          }}>{t.badge}</span>
                        )}
                      </button>
                    )
                  })}
                </div>

                {/* Body */}
                <div style={{ flex: 1, overflowY: 'auto', padding: '18px 22px' }}>
                  {activeTab === 'revise' && (
                    <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
                      <div style={{ fontSize: 12, color: __mutedText, lineHeight: 1.55 }}>
                        Yazacaginiz aciklama <strong style={{ color: __accentText }}>bu (eski) satira</strong>
                        not olarak eklenir — eski halinin niye revize edildigini anlatir.
                        <strong style={{ color: __accentText }}> Revize Et</strong> dediginizde mevcut kalem
                        aynen kopyalanarak alta yeni bir satir olarak eklenir; miktar, fiyat, iskonto ve
                        kombinasyon degisikliklerini <strong style={{ color: __accentText }}>yeni satir uzerinde</strong>
                        gridden yapabilirsiniz. Eski revize bilgileri "Gecmis Revizeler" sekmesinden
                        goruntulenebilir.
                      </div>
                      <label style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
                        <span style={{ fontSize: 10.5, fontWeight: 700, letterSpacing: '.04em', textTransform: 'uppercase', color: __mutedText }}>
                          Aciklama (eski kaleme ait)
                        </span>
                        <textarea
                          rows={5}
                          autoFocus
                          value={draft.notes != null ? draft.notes : ''}
                          onChange={function (e) { setDraft('notes', e.target.value) }}
                          style={{
                            font: 'inherit', fontSize: 13,
                            padding: '10px 12px',
                            borderRadius: 10, resize: 'vertical', minHeight: 120,
                            border: '1px solid ' + __inputBorder,
                            background: __inputBg,
                            color: __inputText,
                            outline: 'none', lineHeight: 1.55,
                          }}
                          onFocus={function (e) { e.currentTarget.style.borderColor = __focusBorder; e.currentTarget.style.boxShadow = __focusShadow }}
                          onBlur={function (e) { e.currentTarget.style.borderColor = __inputBorder; e.currentTarget.style.boxShadow = 'none' }}
                          placeholder="Ornek: Musteri ilk basta 10 adet istemisti, sonradan artirdi…"
                        />
                      </label>
                      {/* Bu satirin ONCEKI notu varsa (ornegin daha onceki revizyondan kalan) bilgi olarak goster */}
                      {row.notes && (
                        <div style={{
                          padding: '8px 12px', borderRadius: 9,
                          background: __noteBg,
                          border: '1px dashed ' + __noteBorder,
                          fontSize: 11.5, color: __mutedText, lineHeight: 1.5,
                        }}>
                          <span style={{ fontWeight: 700, opacity: 0.75 }}>Mevcut not:</span>
                          <span style={{ fontStyle: 'italic', marginLeft: 6 }}>"{row.notes}"</span>
                          <span style={{ display: 'block', marginTop: 3, opacity: 0.55, fontSize: 10.5 }}>
                            Revize et dediginizde bu metin yazdiklariniz ile degistirilecek.
                          </span>
                        </div>
                      )}
                    </div>
                  )}

                  {activeTab === 'history' && (
                    <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
                      {chain.length === 0 && (
                        <div style={{ padding: 24, textAlign: 'center', color: __subtleText, fontSize: 12.5 }}>
                          Bu satirin revizyon zinciri bulunamadi.
                        </div>
                      )}
                      {chain.length > 0 && (
                        <div style={{ fontSize: 12, color: __mutedText, marginBottom: 6 }}>
                          Zincir uzunlugu: <strong style={{ color: __accentText }}>{chain.length}</strong>
                          {chain.length > 1 ? ' kayit (orijinal + ' + (chain.length - 1) + ' revize)' : ' kayit (orijinal)'}
                        </div>
                      )}
                      {chain.map(function (item, idx) {
                        var isCurrent = item._uid === row._uid
                        var isOriginal = idx === 0
                        var label = isOriginal ? 'Orijinal' : (idx + '. Revize')
                        return (
                          <div
                            key={item._uid || ('chain-' + idx)}
                            style={{
                              display: 'flex', alignItems: 'stretch', gap: 12,
                              padding: '12px 14px',
                              borderRadius: 12,
                              background: isCurrent ? __chainCurrentBg : __chainDefaultBg,
                              border: '1px solid ' + (isCurrent ? __chainCurrentBorder : __chainDefaultBorder),
                            }}
                          >
                            <div style={{
                              width: 44, flexShrink: 0,
                              display: 'flex', alignItems: 'center', justifyContent: 'center',
                              fontSize: 11, fontWeight: 700, letterSpacing: '.03em',
                              padding: '4px 6px', borderRadius: 8,
                              background: isOriginal ? 'rgba(34,197,94,0.14)' : 'rgba(139,92,246,0.14)',
                              color: isOriginal ? __origBadgeText : __accentText,
                              border: '1px solid ' + (isOriginal ? __origBadgeBorder : __violetBadgeBorder),
                              textAlign: 'center',
                            }}>
                              {isOriginal ? 'ORJ' : '#' + idx}
                            </div>
                            <div style={{ flex: 1, minWidth: 0 }}>
                              <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap', marginBottom: 4 }}>
                                <span style={{ fontSize: 12.5, fontWeight: 700, color: __headTitle }}>{label}</span>
                                {isCurrent && (
                                  <span style={{
                                    fontSize: 10, fontWeight: 700,
                                    padding: '1px 7px', borderRadius: 999,
                                    background: __currentPillBg, color: __currentPillClr,
                                  }}>bu satir</span>
                                )}
                                <span style={{ fontSize: 10.5, color: __subtleText, fontFamily: "'JetBrains Mono','Consolas',monospace" }}>
                                  {item.id ? '#' + item.id : '(kayit bekliyor)'}
                                </span>
                              </div>
                              <div style={{ display: 'flex', gap: 16, fontSize: 12, color: __detailText, flexWrap: 'wrap' }}>
                                <span><span style={{ opacity: 0.55 }}>Miktar:</span> <strong>{fmtNum(item.quantity)}</strong></span>
                                <span><span style={{ opacity: 0.55 }}>B.Fiyat:</span> <strong>{fmtNum(item.unitPrice)}</strong></span>
                                <span><span style={{ opacity: 0.55 }}>Isk%:</span> <strong>{fmtNum(item.discountRate)}</strong></span>
                                {/* Kombinasyon — bir onceki revizyonla farkliysa "Degisti" rozeti ile vurgula.
                                    Kullanici zincirde hangi adimda kombinasyonun degistigini tek bakista gorur. */}
                                {item.combinationCode && (function () {
                                  var prev = idx > 0 ? chain[idx - 1] : null
                                  var changed = prev && prev.combinationCode && prev.combinationCode !== item.combinationCode
                                  return (
                                    <span style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
                                      <span style={{ opacity: 0.55 }}>Kombinasyon:</span>
                                      <strong style={{ fontFamily: "'JetBrains Mono','Consolas',monospace", fontSize: 11.5 }}>
                                        {item.combinationCode}
                                      </strong>
                                      {changed && (
                                        <span style={{
                                          fontSize: 9.5, fontWeight: 700,
                                          padding: '1px 6px', borderRadius: 8,
                                          background: 'rgba(234,179,8,0.20)', color: '#fde68a',
                                          border: '1px solid rgba(234,179,8,0.40)',
                                          letterSpacing: '.04em', textTransform: 'uppercase',
                                        }} title={'Onceki: ' + prev.combinationCode}>
                                          Degisti
                                        </span>
                                      )}
                                    </span>
                                  )
                                })()}
                                {item.notes && (
                                  <span style={{ flexBasis: '100%', color: __mutedText, fontStyle: 'italic', marginTop: 2 }}>
                                    "{item.notes}"
                                  </span>
                                )}
                              </div>
                            </div>
                          </div>
                        )
                      })}
                    </div>
                  )}
                </div>

                {/* Footer */}
                <div style={{
                  display: 'flex', justifyContent: 'flex-end', gap: 10,
                  padding: '14px 22px',
                  borderTop: '1px solid ' + __sepColor,
                  flexShrink: 0,
                }}>
                  <button
                    type="button"
                    onClick={close}
                    style={{
                      padding: '9px 18px',
                      borderRadius: 9,
                      fontSize: 12.5, fontWeight: 700,
                      color: __cancelClr,
                      background: __cancelBtnBg,
                      border: '1px solid ' + __cancelBdr,
                      cursor: 'pointer',
                      transition: 'all .12s',
                    }}
                    onMouseEnter={function (e) { e.currentTarget.style.background = __cancelBgHov; e.currentTarget.style.color = __isLight ? __cancelClr : '#fff' }}
                    onMouseLeave={function (e) { e.currentTarget.style.background = __cancelBtnBg; e.currentTarget.style.color = __cancelClr }}
                  >
                    Iptal
                  </button>
                  {activeTab === 'revise' && (
                    <button
                      type="button"
                      onClick={createRevision}
                      disabled={!!reviseModal.saving}
                      style={{
                        padding: '9px 20px',
                        borderRadius: 9,
                        fontSize: 12.5, fontWeight: 700,
                        color: '#fff',
                        background: 'linear-gradient(135deg, #8b5cf6, #6366f1)',
                        border: 'none',
                        cursor: reviseModal.saving ? 'not-allowed' : 'pointer',
                        opacity: reviseModal.saving ? 0.7 : 1,
                        boxShadow: '0 4px 14px rgba(139,92,246,0.32)',
                        display: 'inline-flex', alignItems: 'center', gap: 7,
                        transition: 'transform .1s, filter .12s, opacity .12s',
                      }}
                      onMouseEnter={function (e) { if (!reviseModal.saving) { e.currentTarget.style.filter = 'brightness(1.08)'; e.currentTarget.style.transform = 'translateY(-1px)' } }}
                      onMouseLeave={function (e) { e.currentTarget.style.filter = 'none'; e.currentTarget.style.transform = 'none' }}
                    >
                      <GitBranch size={13} strokeWidth={2.2} />
                      {reviseModal.saving ? 'Kaydediliyor…' : 'Revize Et'}
                    </button>
                  )}
                </div>
              </div>
            </div>
          )
        })(),
        (document.querySelector('.sqe-body') || document.body)
      )}

      {/* ── Belge Toplu Maliyet Modal'i ────────────────────────────────────
          Window CustomEvent `quote:open-cost-summary` ile dis dunyadan acilir
          (cshtml'deki Islemler dropdown'undaki "Tüm Ürünlerin Maliyeti"). Her
          kalem icin paralel /Logistics/GetMaterialCost cagriyla toplam hesaplanir. */}
      <QuoteCostSummaryModal />

      {/* ── Maliyet Goruntuleme modal ──────────────────────────────────────
          Standart yeniden-kullanilabilir modal: kalem grid'inden satir kisayol
          menusu ile cagrilir; ileride Tip 1 (sabit alan) veya Tip 2 (widget)
          icindeki "Maliyetini Gor" aksiyonlari da ayni component'i kullanir. */}
      <CostViewerModal
        isOpen={!!costViewer}
        onClose={function () { setCostViewer(null) }}
        materialCode={costViewer ? costViewer.materialCode : ''}
        configCode={costViewer ? costViewer.configCode : null}
        quantity={costViewer ? costViewer.quantity : 1}
        title={costViewer
          ? ('Maliyet Görüntüleme — ' + costViewer.materialCode
              + (costViewer.materialName ? ' (' + costViewer.materialName + ')' : '')
              + (costViewer.configCode ? ' / ' + costViewer.configCode : ''))
          : ''}
      />

      {/* ── Karşılama Detayı modal (PageComment Seq 18) ─────────────────────
          İhtiyaç Kaydı kalem kısayol menüsünden çağrılır; seçili satırın
          karşılama defteri (DocumentLineFulfillment) kayıtlarını listeler. */}
      <FulfillmentDetailModal
        isOpen={!!fulfillmentDetail}
        onClose={function () { setFulfillmentDetail(null) }}
        lineId={fulfillmentDetail ? fulfillmentDetail.lineId : null}
        materialCode={fulfillmentDetail ? fulfillmentDetail.materialCode : ''}
        materialName={fulfillmentDetail ? fulfillmentDetail.materialName : ''}
      />
    </div>
  )
}
