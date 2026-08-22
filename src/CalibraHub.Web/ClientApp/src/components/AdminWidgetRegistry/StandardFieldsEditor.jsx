/**
 * StandardFieldsEditor — Form Davranış Katmanı editörü (2026-08-05, Bölüm ayarı 2026-08-19).
 *
 * Üst bilgi (header) STANDART alanlarının davranışını yönetir: Görünür / Zorunlu /
 * Varsayılan Değer / Başlık Metni + Stili / koşullu kurallar (visibleIf, requiredIf)
 * + sol sekmelerin görünürlük / sıra / ad yönetimi + alanın kart üzerindeki BÖLÜMÜ
 * (Kimlik / Şerit 1 / Şerit 2 / …).
 *
 * Bölüm ayarı: belge üst bilgi (Genel Bilgiler) ve kalem kartı ekranları "kimlik +
 * şerit" düzenine göre çizilir; hangi alanın kimlikte, hangisinin kaçıncı şeritte
 * duracağı TEK bu ekrandan ayarlanır (ayrı "Kart Düzeni" editörü kaldırıldı).
 * `cardSection`: 0 = Kimlik, 1..N = Şerit N, null = ayarlanmamış (Varsayılan —
 * belge ekranı kendi varsayılan dağılımını uygular). Backend alanı henüz
 * dönmüyorsa (paralel geliştirme) `cardSection` undefined gelir → burada "Varsayılan"
 * (null) sayılır, hata verilmez.
 *
 * Bölüm İÇİ sıralama (2026-08-20): her alan `cardOrder` (int|null) taşır — aynı
 * `cardSection` içindeki sıra (küçük önce). Sürükle-bırak YOK (kullanıcı tercihi);
 * her satırda yukarı/aşağı ok düğmesi var. Bir grup içinde sıra her değiştiğinde
 * (taşıma veya bölüm değişimi) o grubun TÜM alanları 0,1,2… olacak şekilde
 * yeniden numaralandırılır (kararlı, boşluksuz). Bölüm değişince alan hedef
 * grubun SONUNA eklenir, hem eski hem yeni grup yeniden numaralandırılır.
 * `cardOrder` backend'den henüz dönmüyorsa `undefined` → `null` (varsayılan sıra,
 * katalog sırası korunur).
 *
 * Hücre genişliği (2026-08-20): şeritler 12 sütunluk ORTAK ızgara olarak çizilir
 * (tüm şeritlerdeki hücreler alt alta hizalanır). Her alan `cardWidth` (1..12|null)
 * taşır — `null` = form varsayılanını kullan. Form varsayılanı `defaultCardWidth`
 * (kök seviyede, GET yanıtının/POST gövdesinin dışında değil KÖKÜNDE) — `null`
 * gelirse 3 kabul edilir. İkisi de backend henüz dönmüyorsa (paralel geliştirme)
 * sessizce 3'e düşer, hata vermez.
 *
 * Ekran markup'ı SABİTTİR — burada yalnızca davranış metadata'sı düzenlenir
 * (GET/POST /api/form-behavior). Fail-open: hiçbir davranış tanımlanmamışsa ekran
 * bugünkü haliyle çalışır. Kilitli alanlar (belge no, tarih, cari, para birimi)
 * gizlenemez. Kural ifadeleri widget kural motoru sözdizimiyle aynıdır; scope'ta
 * bu formun alan key'leri bulunur (örn. currency == 'USD', vatIncluded == true).
 */
import { useState, useEffect, useRef, Fragment } from 'react'
import { createPortal } from 'react-dom'
import FieldBehaviorModal from './FieldBehaviorModal'
import {
  DndContext, DragOverlay, PointerSensor, useSensor, useSensors, useDroppable,
  pointerWithin, MeasuringStrategy,
} from '@dnd-kit/core'
import {
  SortableContext, useSortable, verticalListSortingStrategy,
} from '@dnd-kit/sortable'
import { CSS } from '@dnd-kit/utilities'
import {
  SlidersHorizontal, X as XIcon, Eye, EyeOff, Lock,
  AlertTriangle, Plus, Minus, LayoutGrid, Settings2, Trash2,
  GripVertical, ChevronDown, ChevronRight, Search, RotateCcw, Link2,
} from 'lucide-react'
// Top govdesine portallanmaz — bkz. LineCardLayoutEditor'daki ayni not: tam ekran
// perde ust menu seridini kilitliyordu. iframe'in kendi body'sine portallanir.

// Bölüm şeritlerinin üst sınırı — pratikte 1-4 şerit yeterli, makul bir tavan
// (kimlik + bu kadar şerit) UI'ın taşmasını engeller.
var MAX_STRIPS = 6

function readCsrfToken() {
  try {
    var input = document.querySelector('input[name="__RequestVerificationToken"]')
    if (input && input.value) return input.value
    var shellCfg = window.__CALIBRA_SHELL_CONFIG__
    if (shellCfg && shellCfg.antiforgeryToken) return shellCfg.antiforgeryToken
    return ''
  } catch (e) { return '' }
}

// API henüz string/integer her ikisini de dönebilir (paralel backend geliştirmesi
// tamamlanmadan) — güvenle integer'a normalize et, tanınmayan değer "Varsayılan" (null).
function normalizeCardSection(v) {
  if (typeof v === 'number' && Number.isFinite(v) && v >= 0) return Math.floor(v)
  if (typeof v === 'string' && v.trim() !== '' && !Number.isNaN(Number(v))) {
    var n = Math.floor(Number(v))
    if (n >= 0) return n
  }
  return null
}

// cardOrder da aynı gerekçeyle normalize edilir (paralel backend string/integer
// her ikisini de dönebilir) — tanınmayan/eksik değer null (sıra ayarlanmamış).
function normalizeCardOrder(v) {
  if (typeof v === 'number' && Number.isFinite(v)) return Math.floor(v)
  if (typeof v === 'string' && v.trim() !== '' && !Number.isNaN(Number(v))) return Math.floor(Number(v))
  return null
}

// cardWidth (alan başına hücre genişliği, 1..12) — aynı normalize deseni; aralık
// dışı/tanınmayan değer null (form varsayılanını kullan). 12 sütunluk ortak ızgara
// sözleşmesi backend ile SABİT (bkz. CLAUDE.md).
var CARD_WIDTH_MIN = 1
var CARD_WIDTH_MAX = 12
function normalizeCardWidth(v) {
  var n = null
  if (typeof v === 'number' && Number.isFinite(v)) n = Math.floor(v)
  else if (typeof v === 'string' && v.trim() !== '' && !Number.isNaN(Number(v))) n = Math.floor(Number(v))
  if (n === null) return null
  if (n < CARD_WIDTH_MIN) return CARD_WIDTH_MIN
  if (n > CARD_WIDTH_MAX) return CARD_WIDTH_MAX
  return n
}

// Bölüm içi sıralama karşılaştırıcısı: cardOrder küçük önce, null olanlar en
// sona. Eşit (veya iki null) durumda dizideki mevcut konum korunur (Array.sort
// stabil) — bu da katalog sırasına denk gelir çünkü `fields` state'i kendi
// dizi konumunu asla değiştirmez (yalnız cardOrder/cardSection değerleri mutasyona uğrar).
function compareByCardOrder(a, b) {
  var ao = (typeof a.cardOrder === 'number') ? a.cardOrder : null
  var bo = (typeof b.cardOrder === 'number') ? b.cardOrder : null
  if (ao === null && bo === null) return 0
  if (ao === null) return 1
  if (bo === null) return -1
  return ao - bo
}

function Switch(props) {
  var on = props.on
  var disabled = props.disabled
  return (
    <button
      type="button"
      disabled={disabled}
      onClick={props.onToggle}
      title={props.title || ''}
      className={'relative w-9 h-5 rounded-full transition-colors flex-shrink-0 ' + (
        disabled
          ? 'bg-slate-200 dark:bg-white/10 cursor-not-allowed opacity-60'
          : (on ? (props.color || 'bg-indigo-500/70') : 'bg-slate-300 dark:bg-white/10')
      )}
    >
      <span
        className="absolute top-0.5 w-4 h-4 rounded-full bg-white shadow-sm transition-all"
        style={{ left: on ? 18 : 2 }}
      />
    </button>
  )
}

/**
 * Hücre genişliği kontrolü (2026-08-20) — 12 sütunluk ortak ızgarada 1..12 arası
 * adım. Native `<select>` DEĞİL (kullanıcı isteği): "－ / N/12 / ＋" küçük stepper.
 * `allowClear` true ise (alan bazlı kullanım) değer null'a dönebilir → "Varsayılan
 * (N)" olarak gösterilir; genel ayar (form varsayılanı) için allowClear kapalı,
 * her zaman somut bir sayı taşır.
 */
/* ── Önizleme modalı (2026-08-21) ────────────────────────────────────────────
   Onceki surumde onizleme alan listesinin USTUNDE hep acikti ve editorun yarisini
   kapliyordu; kullanici istegi uzerine MODALA tasindi. Serit burada gercek
   olculeriyle cizilir; hucre kenarindan surukleyerek genislik, alt kenardan serit
   yuksekligi ayarlanir.

   TASLAK MODEL: modal kendi kopyasi uzerinde calisir. "Uygula" denene kadar
   editordeki `fields` / `stripHeights` state'ine DOKUNMAZ — vazgecen kullanici
   surukleyerek bozdugu olculeri geri almak zorunda kalmaz. "Uygula" degerleri
   alan duzeni ekranina aktarir; KALICI yazma yine acik "Kaydet" ile olur.

   CLAUDE.md "Modal boyut sabitligi": sabit genislik + yukseklik, govde kendi
   icinde kaydirilir — sekme degistikce modal buyuyup kuculmez. */
function LayoutPreviewModal(props) {
  var tabTree = props.tabTree || []
  var onApply = props.onApply
  var onClose = props.onClose
  var drag = useRef(null)

  var firstWithFields = tabTree.find(function (t) { return t.fieldCount > 0 }) || tabTree[0]
  var [activeKey, setActiveKey] = useState(firstWithFields ? firstWithFields.key : null)

  // Taslak olculer — modal acilirken mevcut degerlerden kopyalanir.
  var [widths, setWidths] = useState(function () {
    var m = {}
    tabTree.forEach(function (t) {
      t.sections.forEach(function (s) {
        s.fields.forEach(function (f) {
          m[f.key] = (typeof f.cellWidthPx === 'number') ? f.cellWidthPx : null
        })
      })
    })
    return m
  })
  var [heights, setHeights] = useState(function () { return Object.assign({}, props.stripHeights || {}) })

  useEffect(function () {
    function move(e) {
      var d = drag.current
      if (!d) return
      e.preventDefault()
      if (d.kind === 'w') {
        var w = Math.round(Math.min(600, Math.max(60, d.start + (e.clientX - d.p0))))
        setWidths(function (prev) { var n = Object.assign({}, prev); n[d.key] = w; return n })
      } else {
        var h = Math.round(Math.min(96, Math.max(28, d.start + (e.clientY - d.p0))))
        setHeights(function (prev) { var n = Object.assign({}, prev); n[d.section] = h; return n })
      }
    }
    function up() {
      if (drag.current) { drag.current = null; document.body.style.cursor = '' }
    }
    function onKey(e) { if (e.key === 'Escape') onClose() }
    document.addEventListener('mousemove', move)
    document.addEventListener('mouseup', up)
    document.addEventListener('keydown', onKey)
    return function () {
      document.removeEventListener('mousemove', move)
      document.removeEventListener('mouseup', up)
      document.removeEventListener('keydown', onKey)
      document.body.style.cursor = ''
    }
  }, [onClose])

  function startW(e, key, cur) {
    e.preventDefault(); e.stopPropagation()
    drag.current = { kind: 'w', key: key, start: cur, p0: e.clientX }
    document.body.style.cursor = 'col-resize'
  }
  function startH(e, section, cur) {
    e.preventDefault(); e.stopPropagation()
    drag.current = { kind: 'h', section: section, start: cur, p0: e.clientY }
    document.body.style.cursor = 'row-resize'
  }
  function widthOf(key) {
    var v = widths[key]
    return (typeof v === 'number') ? v : freeDefaultWidth(key)
  }
  function apply() {
    onApply({ widths: widths, heights: heights })
    onClose()
  }

  var activeTab = tabTree.find(function (t) { return t.key === activeKey }) || firstWithFields
  var visible = activeTab ? activeTab.sections.filter(function (g) { return g.fields.length > 0 }) : []

  return createPortal(
    <div
      onClick={function (e) { if (e.target === e.currentTarget) onClose() }}
      className="fixed inset-0 z-[120] flex items-center justify-center p-4"
      style={{ background: 'rgba(15,23,42,0.55)', backdropFilter: 'blur(4px)', WebkitBackdropFilter: 'blur(4px)' }}
    >
      {/* bg-[#fff] (bg-white DEGIL): Bootstrap .bg-white{...!important} dark: varyantini yener. */}
      <div
        onClick={function (e) { e.stopPropagation() }}
        className="flex flex-col rounded-2xl border border-slate-200 bg-[#fff] shadow-2xl dark:border-white/10 dark:bg-[#171c2a] overflow-hidden"
        style={{ width: '100%', maxWidth: 'min(940px, calc(100vw - 48px))', height: 'min(620px, calc(100vh - 64px))' }}
      >
        <div className="flex items-start gap-3 px-5 py-4 border-b border-slate-200 dark:border-white/10 flex-shrink-0">
          <div className="min-w-0 flex-1">
            <div className="text-[14px] font-bold text-slate-800 dark:text-white truncate">Düzen Önizleme</div>
            <div className="text-[11.5px] text-slate-500 dark:text-white/45 mt-0.5">
              Hücre kenarından sürükleyerek genişlik, şeridin alt kenarından yükseklik ayarlanır.
              Uygula ölçüleri alan düzeni ekranına aktarır; kalıcı olması için Kaydet gerekir.
            </div>
          </div>
          <button type="button" onClick={onClose} title="Kapat"
                  className="flex-shrink-0 p-1 rounded-lg text-slate-400 hover:bg-slate-100 dark:text-white/40 dark:hover:bg-white/10">
            <XIcon size={16} strokeWidth={2} />
          </button>
        </div>

        {/* Sekme secici — alanlar sekmelere dagildigi icin hepsini tek seritte
            cizmek yaniltici olurdu; her sekme kendi duzeniyle onizlenir. */}
        {tabTree.length > 1 && (
          <div className="flex items-center gap-1 flex-wrap px-5 py-2 border-b border-slate-200 dark:border-white/10 flex-shrink-0">
            {tabTree.map(function (t) {
              var on = activeTab && t.key === activeTab.key
              return (
                <button key={t.key} type="button" onClick={function () { setActiveKey(t.key) }}
                  className={'px-2.5 py-1 rounded-md text-[11px] font-semibold border transition-colors ' + (
                    on ? 'bg-indigo-50 text-indigo-600 border-indigo-200 dark:bg-indigo-500/15 dark:text-indigo-300 dark:border-indigo-400/30'
                       : 'text-slate-500 border-slate-200 hover:bg-slate-50 dark:text-white/55 dark:border-white/10 dark:hover:bg-white/[0.08]'
                  )}>
                  {t.label}
                  <span className="ml-1 opacity-60">({t.fieldCount})</span>
                </button>
              )
            })}
          </div>
        )}

        <div className="flex-1 min-h-0 overflow-auto px-5 py-4">
          {visible.length === 0 ? (
            <div className="text-[11.5px] text-slate-400 dark:text-white/35 py-8 text-center">
              Bu sekmede alan yok.
            </div>
          ) : (
            <div className="rounded-lg border border-slate-200 bg-[#fff] dark:border-white/10 dark:bg-white/[0.03] overflow-hidden">
              {visible.map(function (g) {
                var h = (typeof heights[g.section] === 'number') ? heights[g.section] : 36
                return (
                  <div key={g.key} className="relative border-b border-slate-100 last:border-b-0 dark:border-white/[0.06]">
                    <div className="px-3 pt-1.5 text-[9px] font-bold tracking-wide text-slate-400 dark:text-white/30">{g.label}</div>
                    <div className="flex flex-wrap items-start gap-y-2 px-3 pb-3">
                      {g.fields.map(function (f) {
                        var w = widthOf(f.key)
                        return (
                          <div key={f.key} style={{ width: w, paddingRight: 10 }} className="relative flex-shrink-0">
                            <div className="text-[9.5px] text-slate-400 dark:text-white/35 truncate mb-0.5">{f.labelText || f.label}</div>
                            <div style={{ height: Math.max(16, h - 18) }}
                                 className="rounded-sm border-b border-slate-300 bg-slate-50/70 dark:border-white/20 dark:bg-white/[0.04]" />
                            <div
                              onMouseDown={function (e) { startW(e, f.key, w) }}
                              title={'Genişlik: ' + w + 'px — sürükleyin'}
                              style={{ position: 'absolute', top: 14, right: 2, width: 5, height: Math.max(14, h - 20), cursor: 'col-resize' }}
                              className="rounded-sm bg-slate-300 hover:bg-indigo-500 dark:bg-white/20 dark:hover:bg-indigo-400"
                            />
                          </div>
                        )
                      })}
                    </div>
                    {g.kind === 'strip' && (
                      <div
                        onMouseDown={function (e) { startH(e, g.section, h) }}
                        title={'Şerit yüksekliği: ' + h + 'px — sürükleyin'}
                        style={{ height: 5, cursor: 'row-resize' }}
                        className="bg-slate-100 hover:bg-indigo-400 dark:bg-white/[0.06] dark:hover:bg-indigo-500"
                      />
                    )}
                  </div>
                )
              })}
            </div>
          )}
        </div>

        <div className="flex items-center justify-end gap-2 px-5 py-3 border-t border-slate-200 dark:border-white/10 flex-shrink-0">
          <button type="button" onClick={onClose}
                  className="px-3 py-1.5 rounded-lg text-[11.5px] font-semibold text-slate-600 border border-slate-200 hover:bg-slate-50 dark:text-white/70 dark:border-white/10 dark:hover:bg-white/[0.08]">
            Vazgeç
          </button>
          <button type="button" onClick={apply}
                  className="px-3.5 py-1.5 rounded-lg text-[11.5px] font-bold text-white bg-indigo-600 hover:bg-indigo-700">
            Uygula
          </button>
        </div>
      </div>
    </div>,
    document.body
  )
}

/* Serbest duzende tip bazli varsayilan genislikler — DocumentEdit.cshtml'deki
   FREE_DEFAULT_W ile AYNI kume olmali (ayrisirsa editorde gosterilen "varsayilan"
   ile ekranda cizilen genislik tutmaz). */
var FREE_DEFAULT_W = {
  quoteDate: 150, validUntil: 220, rateDate: 150,
  currency: 90, exchangeRate: 120, vatIncluded: 90,
  salesRep: 240, warehouse: 200, paymentPlan: 200,
}
function freeDefaultWidth(key) { return FREE_DEFAULT_W[key] || 200 }

/* Piksel genislik adimlayicisi (60-600, 10px adim). Olculer SABIT — deger
   "Varsayilan (150)" ile "220px" arasinda gidip gelirken satirdaki switch'ler
   kaymasin (ayni tuzak bu oturumda bir kez yasandi). */
function PxWidthStepper(props) {
  var value = (typeof props.value === 'number') ? props.value : null
  var fallback = props.fallback
  var onChange = props.onChange
  var display = (value !== null) ? value : fallback
  function step(d) { onChange(Math.max(60, Math.min(600, display + d))) }
  return (
    <div className="flex items-center gap-1 flex-shrink-0">
      <div className="flex items-center rounded-md border border-slate-200 bg-[#fff] dark:border-white/10 dark:bg-white/[0.04] overflow-hidden">
        <button type="button" onClick={function () { step(-10) }} disabled={display <= 60} title="Daralt"
                className="w-5 h-5 flex items-center justify-center text-slate-500 hover:text-indigo-600 dark:text-white/55 dark:hover:text-indigo-300 disabled:opacity-30">
          <Minus size={11} strokeWidth={2.4} />
        </button>
        <span style={{ minWidth: 86 }}
              title={value !== null ? (value + ' piksel') : ('Ayarlanmamış — varsayılan: ' + fallback + 'px')}
              className={'px-1.5 text-[10.5px] font-bold tabular-nums text-center whitespace-nowrap ' + (
                value !== null ? 'text-slate-700 dark:text-white/85' : 'text-slate-400 italic dark:text-white/40')}>
          {value !== null ? (value + 'px') : ('Varsayılan (' + fallback + ')')}
        </span>
        <button type="button" onClick={function () { step(10) }} disabled={display >= 600} title="Genişlet"
                className="w-5 h-5 flex items-center justify-center text-slate-500 hover:text-indigo-600 dark:text-white/55 dark:hover:text-indigo-300 disabled:opacity-30">
          <Plus size={11} strokeWidth={2.4} />
        </button>
      </div>
      <button type="button" onClick={function () { if (value !== null) onChange(null) }}
              disabled={value === null} aria-hidden={value === null} tabIndex={value === null ? -1 : 0}
              title="Varsayılana dön"
              style={{ width: 13, visibility: value === null ? 'hidden' : 'visible' }}
              className="flex-shrink-0 text-slate-400 hover:text-indigo-600 dark:text-white/35 dark:hover:text-indigo-300">
        <RotateCcw size={11} strokeWidth={2.2} />
      </button>
    </div>
  )
}

/* bg-[#fff]: Bootstrap'in .bg-white{...!important} utility'si Tailwind dark:
   varyantini eziyordu (karanlik temada beyaz bloklar) — ayni gorunum, cakismayan ad. */
var inputCls = 'w-full px-2 py-1 rounded-md text-[11.5px] border border-slate-200 bg-[#fff] text-slate-700 ' +
  'placeholder:text-slate-300 focus:outline-none focus:ring-1 focus:ring-indigo-400 ' +
  'dark:border-white/[0.14] dark:bg-slate-900/60 dark:text-white/85 dark:placeholder:text-white/45'

/**
 * "Ayarla" butonunun etiketi — hangi davranislarin tanimli oldugunu tek bakista
 * gosterir. Hicbiri yoksa notr "Ayarla" yazar (kullanici neyin bos oldugunu bilir).
 */
function describeBehavior(f) {
  var parts = []
  if (f.defaultValue) parts.push('varsayılan: ' + f.defaultValue)
  if (f.visibleIf) parts.push('koşullu görünür')
  if (f.requiredIf) parts.push('koşullu zorunlu')
  return parts.length ? parts.join(' · ') : 'Ayarla'
}

// Sekme key'ine göre bağlam rozeti (yalnız üst bilgi formunda; kalem formunda
// tek sekme olduğundan gösterilmez).
var tabLabels = { general: 'Genel Bilgiler', lines: 'Kalem Bilgileri', conditions: 'Koşullar', notes: 'Notlar' }

/**
 * Alanları cardSection'a göre grupla: Kimlik(0) → Şerit 1..N → Varsayılan(null) en
 * sonda. Her grup içi ayrıca cardOrder'a göre sıralanır (bkz. compareByCardOrder).
 */
/** Alanin EFEKTIF sekmesi: kullanici tasidiysa o, yoksa katalogdaki. */
function effTab(f) { return f.targetTab || f.tab || 'general' }

/**
 * Sekme → bolum agaci (2026-08-21 Faz 4). Once sekmeye, sonra bolume gruplar.
 * Yalnizca ALAN barindirabilen sekmeler dallanir; bilesen sekmeleri (kalem gridi,
 * ekli dosyalar…) alan listesine girmez — icerikleri tasinabilir ama parcalanamaz.
 */
function buildTabTree(fields, tabs, maxStrip) {
  var fieldTabKeys = {}
  fields.forEach(function (f) { fieldTabKeys[effTab(f)] = true })
  // Alan barindiran sekmeler + kullanici tanimli sekmeler (henuz bos olabilir)
  var keys = tabs
    .filter(function (t) { return fieldTabKeys[t.key] || t.isCustom })
    .map(function (t) { return t.key })
  Object.keys(fieldTabKeys).forEach(function (k) {
    if (keys.indexOf(k) === -1) keys.push(k)
  })
  return keys.map(function (key) {
    var t = tabs.find(function (x) { return x.key === key })
    var mine = fields.filter(function (f) { return effTab(f) === key })
    return {
      key: key,
      label: (t && (t.labelText || t.label)) || key,
      rawLabel: (t && t.label) || key,     // katalogdaki ozgun ad (placeholder)
      labelText: (t && t.labelText) || '', // kullanicinin verdigi ad (input degeri)
      isCustom: !!(t && t.isCustom),
      locked: !!(t && t.locked),
      isVisible: !t || t.isVisible !== false,
      targetTabKey: (t && t.targetTabKey) || '',
      fieldCount: mine.length,
      sections: buildSectionGroups(mine, maxStrip),
    }
  })
}

function buildSectionGroups(fields, maxStrip) {
  function bySection(sec) {
    return fields.filter(function (f) { return f.cardSection === sec }).slice().sort(compareByCardOrder)
  }
  var groups = []
  groups.push({ key: 'section-0', section: 0, label: 'Kimlik', hint: 'Kart başlığı', kind: 'identity', fields: bySection(0) })
  for (var i = 1; i <= maxStrip; i++) {
    (function (n) {
      groups.push({ key: 'section-' + n, section: n, label: 'Şerit ' + n, hint: null, kind: 'strip', fields: bySection(n) })
    })(i)
  }
  groups.push({ key: 'section-null', section: null, label: 'Varsayılan (Ayarlanmamış)', hint: 'Belge ekranı kendi varsayılan dağılımını uygular',
    kind: 'default', fields: bySection(null) })
  return groups
}

/** Bolum kabi — bos olsa bile birakma hedefi olur (alan surukleyip birakilabilsin). */
function SectionDropZone(props) {
  // 2026-08-21 Faz 4: birakma hedefi artik SEKME + BOLUM. Tek anahtarda tasinir:
  // "tab:<key>|sec:<n>" (bkz. dropTargetFromId).
  var zoneId = 'tab:' + (props.tab || 'general') + '|sec:' + (props.section === null ? 'null' : props.section)
  var over = useDroppable({ id: zoneId })
  return (
    <div
      ref={over.setNodeRef}
      data-drop-zone={zoneId}
      className={'rounded-lg transition-colors ' + (over.isOver
        ? 'bg-indigo-50/70 outline outline-1 outline-dashed outline-indigo-300 dark:bg-indigo-500/10 dark:outline-indigo-400/40'
        : '')}
    >
      {props.children}
    </div>
  )
}

/** Surukleneble alan satiri — tutamak yalnizca GripVertical, govde tiklanabilir kalir. */
/* Grup uyeleri: Para Birimi anchor, digerleri onunla birlikte hareket eder
   (bkz. DocumentEdit __HGROUP_ANCHOR). Bunlarin tek basina surukklenmesi
   ETKISIZ oldugu icin tutamak kapatilir — kullanici bosuna denemesin. */
var HGROUP_FOLLOWER = { exchangeRate: 1, rateDate: 1 }

/* Belge ekraninda TOPLAMLAR blogunun icine orulu ust-bilgi alanlari
   (DocumentEdit `.sqe-total-*` markup'i). Kalem Bilgileri sekmesinde
   listeleniyorlar cunku fiziksel yerleri kalem panesinin alti — ama kalem
   verisi degil, belge geneline ait. Kart icinde ayri baslik altinda toplanirlar.
   Yeni bir toplam alani ayarlanabilir hale gelirse anahtarini buraya ekle. */
var TOTALS_BLOCK_KEYS = { discountRate: 1 }

/* 2026-08-22 (kullanici bildirimi: "seritler arasi alan tasirken tasinan alan
   kayboluyor"): sebep DragOverlay YOKLUGU idi. dnd-kit'te aktif oge DOM'da kendi
   kabinda kalir; imlec baska bir kaba gecince o kabin sirasi artik ogeye transform
   uretmez ve oge kaynak yerinde soluk halde ASILI kalir — kullanici "kayboldu"
   olarak gorur (birakma yine de dogru calisir; sikayetin "tasima sorunsuz" kismi bu).
   Cozum: aktif ogenin kopyasi DragOverlay ile imlecin altinda TASINIR; kaynak satir
   ise yerinde kesik-cizgili BOSLUK olarak kalir (nereden alindigi gorunur). */
/* ── Sekme KARTI (2026-08-22, kullanici istegi) ─────────────────────────────
   Onceden sekmeler iki yerde yonetiliyordu: ustte ayri bir "Sekmeler" paneli
   (sira oklari + ad + gorunurluk + hedef sekme) ve asagida alan listesinin
   basligi. Artik TEK yer: her sekme kendi karti. Kart basligindan surukleyerek
   sira degistirilir, chevron ile icerigi katlanir.
   Kendi DndContext'i var — alan surukleme baglamiyla karismaz; iki baglamin
   sensorleri yalnizca kendi tutamaklarina bagli. */
function isTabDrag(id) { return String(id || '').indexOf('tabcard:') === 0 }

function TabCard(props) {
  var sortable = useSortable({ id: 'tabcard:' + props.tabKey })
  var style = {
    transform: CSS.Transform.toString(sortable.transform),
    transition: sortable.transition,
    opacity: sortable.isDragging ? 0.5 : 1,
  }
  return (
    <div ref={sortable.setNodeRef} style={style}
         className={'mb-3 rounded-xl border overflow-hidden ' + (props.dim
           ? 'border-slate-200 bg-slate-50/40 dark:border-white/10 dark:bg-white/[0.015]'
           : 'border-slate-200 bg-[#fff] dark:border-white/10 dark:bg-white/[0.025]')}>
      <div className="flex items-center gap-2 px-2.5 py-2 border-b border-slate-100 dark:border-white/[0.06] bg-slate-50/70 dark:bg-white/[0.04]">
        <button
          type="button"
          {...sortable.attributes}
          {...sortable.listeners}
          title="Sürükleyerek sekme sırasını değiştir"
          className="flex-shrink-0 p-0.5 rounded text-slate-400 hover:text-indigo-600 dark:text-white/35 dark:hover:text-indigo-300 cursor-grab active:cursor-grabbing"
        >
          <GripVertical size={13} strokeWidth={2} />
        </button>
        <button
          type="button"
          onClick={props.onToggle}
          title={props.collapsed ? 'İçeriği göster' : 'İçeriği gizle'}
          className="flex-shrink-0 p-0.5 rounded text-slate-500 hover:text-indigo-600 dark:text-white/55 dark:hover:text-indigo-300"
        >
          {props.collapsed ? <ChevronRight size={14} strokeWidth={2.2} /> : <ChevronDown size={14} strokeWidth={2.2} />}
        </button>
        {props.header}
      </div>
      {!props.collapsed && <div className="px-2.5 pt-2.5">{props.children}</div>}
    </div>
  )
}

/** Surukleme sirasinda hedef seritte acilan bosluk — alan buraya dusecek. */
function DropPlaceholder() {
  return (
    <div className="rounded-lg border border-dashed border-indigo-400 bg-indigo-50/60 dark:border-indigo-400/60 dark:bg-indigo-500/10"
         style={{ height: 34 }} aria-hidden="true" />
  )
}

function SortableRow(props) {
  var locked = props.locked === true
  var sortable = useSortable({ id: props.id, disabled: locked })
  var dragging = sortable.isDragging
  var style = {
    transform: CSS.Transform.toString(sortable.transform),
    transition: sortable.transition,
  }
  return (
    <div ref={sortable.setNodeRef} style={style}
         data-field-row={props.id}
         className={'rounded-lg px-2.5 py-2 flex items-start gap-2 ' + (dragging
           ? 'border border-dashed border-indigo-300 bg-indigo-50/40 opacity-55 dark:border-indigo-400/40 dark:bg-indigo-500/[0.07]'
           : 'border border-slate-200 bg-slate-50/60 dark:border-white/10 dark:bg-white/[0.03]')}>
      {locked ? (
        <span title={props.lockedTitle || 'Bu alan taşınamaz'}
              className="flex-shrink-0 mt-0.5 p-0.5 text-slate-300 dark:text-white/20 cursor-not-allowed">
          <Link2 size={13} strokeWidth={2} />
        </span>
      ) : (
        <button
          type="button"
          {...sortable.attributes}
          {...sortable.listeners}
          title="Sürükleyerek sırala, başka bölüme veya sekmeye taşı"
          className="flex-shrink-0 mt-0.5 p-0.5 rounded text-slate-400 hover:text-indigo-600 dark:text-white/35 dark:hover:text-indigo-300 cursor-grab active:cursor-grabbing"
        >
          <GripVertical size={13} strokeWidth={2} />
        </button>
      )}
      <div className="flex-1 min-w-0 flex flex-col gap-1.5">{props.children}</div>
    </div>
  )
}

export default function StandardFieldsEditor(props) {
  var formCode = props.formCode
  var onClose = props.onClose
  var onSaved = props.onSaved

  var [loading, setLoading] = useState(true)
  var [error, setError] = useState(null)
  var [saving, setSaving] = useState(false)
  var [fields, setFields] = useState([])
  var [tabs, setTabs] = useState([])
  var [maxStrip, setMaxStrip] = useState(3)
  // Form varsayılan hücre genişliği (1..12) — genişliği ayarlanmamış (cardWidth=null)
  // alanlar bunu kullanır. Backend henüz dönmüyorsa/null ise sözleşme gereği 3 kabul
  // edilir (bkz. dosya başı not) — state her zaman somut bir sayı taşır.
  var [defaultCardWidth, setDefaultCardWidth] = useState(3)
  var [initialDefaultCardWidth, setInitialDefaultCardWidth] = useState(3)
  // Davranis modali acik olan alanin key'i (null = kapali)
  var [behaviorKey, setBehaviorKey] = useState(null)
  // Serit basina satir yuksekligi (px): { 1: 44, 2: 36, ... }. Bos = varsayilan.
  var [stripHeights, setStripHeights] = useState({})
  // Onizleme modali acik mi (2026-08-21) — canli onizleme editorden cikarildi.
  var [previewOpen, setPreviewOpen] = useState(false)
  // Sifirla — yuklenen (kaydedilmis) hale donus icin anlik goruntu
  var [initialFields, setInitialFields] = useState([])
  // Arama kutusu (Sutun Ayarlari paneliyle ayni etkilesim dili)
  var [search, setSearch] = useState('')

  useEffect(function () {
    var alive = true
    fetch('/api/form-behavior/' + encodeURIComponent(formCode), { credentials: 'same-origin' })
      .then(function (r) { return r.ok ? r.json() : null })
      .then(function (data) {
        if (!alive) return
        if (!data || data.ok !== true) {
          setError((data && data.error) || 'Davranış tanımı yüklenemedi.')
          return
        }
        var loadedFields = (data.fields || []).map(function (f) {
          return {
            key: f.key, label: f.label, tab: f.tab, dataType: f.dataType, locked: f.locked === true,
            isVisible: f.isVisible !== false,
            isRequired: f.isRequired === true,
            defaultValue: f.defaultValue || '',
            labelText: f.labelText || '',
            labelStyle: f.labelStyle || '',
            cellWidthPx: (typeof f.cellWidthPx === 'number' && isFinite(f.cellWidthPx)) ? f.cellWidthPx : null,
            tab: f.tab || 'general',            // katalog sekmesi (degismez)
            targetTab: f.targetTab || null,     // kullanicinin tasidigi sekme (null = katalog)
            movable: f.movable !== false,       // false → baska sekmeye tasinamaz
            visibleIf: f.visibleIf || '',
            requiredIf: f.requiredIf || '',
            cardSection: normalizeCardSection(f.cardSection),
            cardOrder: normalizeCardOrder(f.cardOrder),
            cardWidth: normalizeCardWidth(f.cardWidth),
          }
        })
        setFields(loadedFields)
        // Sifirla icin baslangic anlik goruntusu (kaydedilmis son hal)
        setInitialFields(loadedFields)
        var sh = {}
        ;(data.stripHeights || []).forEach(function (x) {
          if (x && x.section > 0 && typeof x.rowHeight === 'number') sh[x.section] = x.rowHeight
        })
        setStripHeights(sh)
        // Form varsayilan hucre genisligi — kokte gelir, null/eksikse 3 (sozlesme).
        var loadedDefaultCardWidth = normalizeCardWidth(data.defaultCardWidth)
        var resolvedDefaultCardWidth = loadedDefaultCardWidth === null ? 3 : loadedDefaultCardWidth
        setDefaultCardWidth(resolvedDefaultCardWidth)
        setInitialDefaultCardWidth(resolvedDefaultCardWidth)
        setTabs((data.tabs || []).map(function (t) {
          return {
            key: t.key, label: t.label, locked: t.locked === true,
            isVisible: t.isVisible !== false,
            labelText: t.labelText || '',
            targetTabKey: t.targetTabKey || '',
            isCustom: t.isCustom === true,
          }
        }))
        // Mevcut en yüksek şerit numarasına göre başlangıç şerit sayısı (en az 3,
        // en çok MAX_STRIPS) — kullanıcı zaten atanmış şeritleri görsün.
        var maxAssigned = loadedFields.reduce(function (acc, f) {
          return (typeof f.cardSection === 'number' && f.cardSection > acc) ? f.cardSection : acc
        }, 0)
        // Taban 1 (eskiden 3): 3 olsaydi kullanici seridi silip kaydetse bile
        // yeniden acilista bos seritler geri gelirdi — silme kalici olmazdi.
        setMaxStrip(Math.max(1, Math.min(MAX_STRIPS, maxAssigned)))
      })
      .catch(function (e) { if (alive) setError('Hata: ' + (e && e.message ? e.message : String(e))) })
      .then(function () { if (alive) setLoading(false) })
    return function () { alive = false }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [formCode])

  /* Alan satirinin govdesi — hem SIRALANABILIR seritlerde hem de bilesen
     sekmesinin duz listesinde AYNI markup kullanilir (DRY). */
  function renderFieldRow(f) {
    /* ── TEK SATIR, SABIT IZGARA (2026-08-22, kullanici istegi) ──
    Onceden: satir 1'de ad + switch'ler (flex-wrap), satir 2'de
    "Detay" ile acilan baslik metni + Ayarla. Iki sorun vardi:
    (1) katlanir satirda geriye yalnizca iki kontrol kalmisti,
    (2) flex-wrap'te ad uzunlugu degistikce switch'ler saga sola
    kayiyor, satirlar arasi DIKEY hiza tutmuyordu.
    Simdi: SABIT sutunlu grid — her satirda Görünür/Zorunlu/
    Genişlik/Ayarla tam olarak ayni x'te durur. `expanded`
    (katlama) state'i tamamen kalkti. */
    return (
    <div className="w-full grid items-center gap-2"
                         style={{ gridTemplateColumns: 'minmax(0,1.3fr) minmax(0,1fr) 74px 74px 150px 148px' }}>
                      {/* 1) Alan adı + kilit + sekme rozeti */}
                      <div className="flex items-center gap-1.5 min-w-0">
                        {f.locked && <span title="Çekirdek alan — gizlenemez"><Lock size={11} className="text-slate-400 dark:text-white/45 flex-shrink-0" /></span>}
                        <span className="truncate text-[11.5px] font-semibold text-slate-600 dark:text-white/70" title={f.key}>{f.label}</span>
                        {showTabBadge && tabLabels[f.tab] && (
                          <span className="px-1.5 py-0.5 rounded text-[9px] font-semibold bg-slate-100 text-slate-500 border border-slate-200 dark:bg-white/[0.05] dark:text-white/50 dark:border-white/10 flex-shrink-0">
                            {tabLabels[f.tab]}
                          </span>
                        )}
                      </div>

                      {/* 2) Başlık metni — bos birakilirsa katalog adi kullanilir */}
                      <input
                        type="text"
                        value={f.labelText}
                        maxLength={60}
                        placeholder={f.label}
                        title="Ekranda görünecek başlık — boş bırakılırsa alanın kendi adı kullanılır"
                        onChange={function (e) { patchField(f.key, { labelText: e.target.value }) }}
                        className={inputCls}
                      />

                      {/* 3) Görünür */}
                      <div className="flex items-center gap-1.5 justify-self-start">
                        <span className="text-[9.5px] font-semibold text-slate-500 dark:text-white/50">Görünür</span>
                        <Switch
                          on={f.isVisible}
                          disabled={f.locked}
                          color="bg-emerald-500/70"
                          title={f.locked ? 'Çekirdek alan — gizlenemez' : 'Görünür'}
                          onToggle={function () { patchField(f.key, { isVisible: !f.isVisible }) }}
                        />
                      </div>

                      {/* 4) Zorunlu */}
                      <div className="flex items-center gap-1.5 justify-self-start">
                        <span className="text-[9.5px] font-semibold text-slate-500 dark:text-white/50">Zorunlu</span>
                        <Switch
                          on={f.isRequired}
                          color="bg-red-500/70"
                          title="Zorunlu — boş bırakılırsa kayıt engellenir"
                          onToggle={function () { patchField(f.key, { isRequired: !f.isRequired }) }}
                        />
                      </div>

                      {/* 5) Genişlik (piksel) */}
                      <div className="justify-self-start">
                        <PxWidthStepper
                          value={f.cellWidthPx}
                          fallback={freeDefaultWidth(f.key)}
                          onChange={function (v) { patchField(f.key, { cellWidthPx: v }) }}
                        />
                      </div>

                      {/* 6) Varsayilan Deger / Gorunurluk / Zorunluluk kosulu — modal */}
                      <button
                        type="button"
                        onClick={function () { setBehaviorKey(f.key) }}
                        title="Varsayılan değer, görünürlük ve zorunluluk koşulunu tanımla"
                        className={'w-full flex items-center justify-center gap-1.5 px-2 py-1 rounded-md text-[11px] font-semibold border transition-colors ' + (
                          (f.defaultValue || f.visibleIf || f.requiredIf)
                            ? 'text-indigo-600 border-indigo-200 bg-indigo-50 hover:bg-indigo-100 dark:text-indigo-300 dark:border-indigo-400/30 dark:bg-indigo-500/10 dark:hover:bg-indigo-500/20'
                            : 'text-slate-500 border-slate-200 bg-[#fff] hover:bg-slate-50 dark:text-white/55 dark:border-white/10 dark:bg-white/[0.04] dark:hover:bg-white/[0.08]'
                        )}
                      >
                        <Settings2 size={12} strokeWidth={2.2} className="flex-shrink-0" />
                        <span className="truncate">{describeBehavior(f)}</span>
                      </button>
                    </div>
    )
  }
  function patchField(key, patch) {
    setFields(function (prev) {
      return prev.map(function (f) { return f.key === key ? Object.assign({}, f, patch) : f })
    })
  }

  /* Sekme karti surukleyerek siralanir (eski yukari/asagi oklari kalkti).
     `tabs` dizisinin SIRASI kaydetmede sortOrder olur — bu yuzden tabTree'deki
     gorunen sirayi degil `tabs`'i yeniden diziyoruz. */
  function handleTabDragEnd(event) {
    var active = event.active, over = event.over
    if (!active || !over || active.id === over.id) return
    if (!isTabDrag(over.id)) return
    var from = String(active.id).replace('tabcard:', '')
    var to = String(over.id).replace('tabcard:', '')
    setTabs(function (prev) {
      var next = prev.slice()
      var i = next.findIndex(function (t) { return t.key === from })
      var j = next.findIndex(function (t) { return t.key === to })
      if (i < 0 || j < 0) return prev
      var moved = next.splice(i, 1)[0]
      next.splice(j, 0, moved)
      return next
    })
  }

  /* ── Surukle-birak (2026-08-20) ─────────────────────────────────────────────
     Sutun Ayarlari panelindeki etkilesim dili buraya tasindi, ama ANLIK KAYIT
     ALINMADI (kullanici karari): burada yazilan ayar FormFieldBehavior'a gider ve
     formun TUM kullanicilarini baglar — kazara bir surukleme herkesi etkilerdi.
     Bu yuzden surukleme yalnizca yerel state'i degistirir, kalici yazma acik
     "Kaydet" ile olur.

     Sutun panelinden farki: orada tek boyutlu bir liste var, burada IKI boyut —
     alan hangi BOLUMDE (Kimlik/Serit N/Varsayilan) ve o bolum icinde kacinci
     sirada. Bu yuzden cok-kapli (multi-container) surukleme kuruldu; eski
     "Bolum" buton grubu ve yukari/asagi oklari kaldirildi. */
  var dndSensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 4 } }))
  // Suruklenen alanin key'i — DragOverlay icerigi bundan cizilir (null = surukleme yok).
  var [dragKey, setDragKey] = useState(null)
  /* Surukleme sirasinda hedef seritte acilan BOSLUGUN yeri: {tab, section, index}.
     Yalnizca gorsel — `fields` state'ine DOKUNMAZ (bkz. handleDragOver notu). */
  var [dropHint, setDropHint] = useState(null)
  // Katlanmis sekme kartlari (key seti). Varsayilan: hepsi acik.
  var [collapsedTabs, setCollapsedTabs] = useState({})
  // Imlecin anlik konumu: aktivasyon olayi + dnd-kit delta (scroll duzeltmesi dahil).
  var dragPointer = useRef(null)

  /* Hedef serit icinde SATIR HASSASIYETINDE index (2026-08-22).
     Kullanici bildirimi: (1) baska serite tasima kendi seridi kadar hassas degil,
     (2) bir seritte ustten tutulan alan EN ALT satira konulamiyor.
     Ikisinin de koku ayni: index yalnizca `over` bir SATIR oldugunda hesaplaniyor
     ve o satirin ONUNE ekliyordu. Son sira icin `length` gerekir; "onune ekle"
     kurali son siraya ASLA ulasamaz. Ayrica imlec satirlar arasi bosluktaysa
     index null olup sona atiyordu — bu da hassasiyet kaybiydi.
     Cozum: index'i imlecin Y'sinden, hedef kaptaki satirlarin GORSEL orta
     noktalarina gore hesapla — kendi seridi de baska serit de ayni yol. */
  function indexFromPointer(tabKey, section, movingKey) {
    var p = dragPointer.current
    if (!p) return null
    var host = document.querySelector('[data-drop-zone="tab:' + tabKey + '|sec:' + (section === null ? 'null' : section) + '"]')
    if (!host) return null
    var rows = Array.prototype.slice.call(host.querySelectorAll('[data-field-row]'))
      .filter(function (el) { return el.getAttribute('data-field-row') !== movingKey })
    for (var i = 0; i < rows.length; i++) {
      var r = rows[i].getBoundingClientRect()
      if (p.y < r.top + r.height / 2) return i
    }
    return rows.length
  }

  /** Birakma hedefi: hangi sekme + hangi serit + o seritte kacinci sira. */
  function resolveDrop(event) {
    var over = event.over
    if (!over) return null
    var moving = fields.find(function (f) { return f.key === String(event.active.id) })
    if (!moving) return null
    var dst = containerOfOverId(String(over.id))
    if (!dst) return null
    var dstTab = dst.tab || effTab(moving)
    return { tab: dstTab, section: dst.section, index: indexFromPointer(dstTab, dst.section, moving.key) }
  }

  /* Carpisma stratejisi (2026-08-22): `closestCenter` cok-kapli listede kaba
     kaliyordu — imlec bir seridin USTUNDEYKEN bile merkezi daha yakin olan BASKA
     seritteki satir "over" secilebiliyordu. Once `pointerWithin` (imlecin GERCEKTEN
     icinde oldugu hedefler), o bos donerse (imlec bosluktaysa) `rectIntersection`.
     Sonuc: birakma noktasi imlecin altindaki hedefle birebir ortusur. */
  function preciseCollision(args) {
    /* Sekme karti suruklenirken YALNIZ sekme kartlari hedef olabilir ve hedef
       IMLECE gore secilir. `closestCenter` denendi (olculdu): suruklenen KARTIN
       merkezine bakiyor; kart uzun oldugu icin merkezi cok asagida kaliyor ve
       30px'lik bir hareket bile sekmeyi en sona atiyordu. */
    if (isTabDrag(args.active && args.active.id)) {
      var tabsOnly = args.droppableContainers.filter(function (c) { return isTabDrag(c.id) })
      var tabHit = pointerWithin(Object.assign({}, args, { droppableContainers: tabsOnly }))
      return tabHit.length > 0 ? tabHit : nearestByPointer(args, tabsOnly)
    }
    var hits = pointerWithin(args).filter(function (h) { return !isTabDrag(h.id) })
    /* Kap (SectionDropZone) satirlari SARDIGI icin imlec bir satirin uzerindeyken
       hem satir hem kap carpisir. dnd-kit listenin ILKINI secer; siralama merkeze
       uzakliga gore oldugundan buyuk kabin merkezi bazen satirdan yakin kalip
       "sona ekle" davranisi ureterek birakma noktasini kaydiriyordu.
       Kural: SATIR carpismasi varsa DAIMA o kazanir. */
    var rows = hits.filter(function (h) { return !dropTargetFromId(h.id) && !isTabDrag(h.id) })
    if (rows.length > 0) return rows
    if (hits.length > 0) return hits
    /* Imlec hicbir hedefin ICINDE degil — serit basliklari, seritler arasi bosluk,
       kap kenar paylari. Burada eskiden `rectIntersection`'a dusuyorduk; O imlece
       DEGIL suruklenen ogenin dikdortgenine bakar ve uzaktaki bir bolume atliyordu
       (olculdu: imlec Serit 2'nin hemen ustundeyken hedef "Varsayilan" secildi).
       Dogrusu: imlece DIKEYDE en yakin kabi sec — yatay uzaklik zayif agirlikli,
       cunku seritler alt alta dizili. */
    return nearestByPointer(args, args.droppableContainers.filter(function (c) {
      return !isTabDrag(c.id) && !!dropTargetFromId(c.id)
    }))
  }

  /** Imlece DIKEYDE en yakin hedef (yatay uzaklik zayif agirlikli — dikey yigin). */
  function nearestByPointer(args, containers) {
    var p = args.pointerCoordinates
    if (!p) return []
    var best = null
    containers.forEach(function (c) {
      var r = args.droppableRects.get(c.id)
      if (!r) return
      var dy = p.y < r.top ? r.top - p.y : (p.y > r.top + r.height ? p.y - (r.top + r.height) : 0)
      var dx = p.x < r.left ? r.left - p.x : (p.x > r.left + r.width ? p.x - (r.left + r.width) : 0)
      var d = dy + dx * 0.25
      if (!best || d < best.d) best = { d: d, id: c.id }
    })
    return best ? [{ id: best.id }] : []
  }


  /* Droppable id (2026-08-21 Faz 4): "tab:<key>|sec:<n>" — iki seviye tek
     anahtarda. Eski tek seviyeli "sec:<n>" bicimi de okunur (geri uyum). */
  function dropTargetFromId(id) {
    var m = /^tab:([^|]+)\|sec:(.+)$/.exec(String(id))
    if (m) return { tab: m[1], section: m[2] === 'null' ? null : parseInt(m[2], 10) }
    var m2 = /^sec:(.+)$/.exec(String(id))
    if (m2) return { tab: null, section: m2[1] === 'null' ? null : parseInt(m2[1], 10) }
    return null
  }

  /**
   * Alani hedef bolume (ve o bolumde hedef indekse) tasir. Hem kaynak hem hedef
   * grup 0..n-1 olacak sekilde yeniden numaralandirilir — bosluksuz, kararli
   * (bosluksuz, kararli yeniden numaralandirma).
   */
  function moveFieldTo(key, targetSection, targetIndex, targetTab) {
    setFields(function (prev) {
      var moving = prev.find(function (f) { return f.key === key })
      if (!moving) return prev
      // Tasinamaz alan (ör. Genel İskonto %) sekme DEGISTIREMEZ; bolum icinde
      // siralanabilir. Sessizce yok saymak yerine sekme kismini iptal ediyoruz.
      var newTab = (targetTab != null && moving.movable !== false)
        ? (targetTab === (moving.tab || 'general') ? null : targetTab)
        : moving.targetTab
      var sourceSection = moving.cardSection
      // Hedef gruptaki mevcut sira (tasinan haric)
      // Hedef gruptaki mevcut sira — AYNI sekme + AYNI bolum
      var tabOf = function (f) { return f.targetTab || f.tab || 'general' }
      var wantTab = newTab || (moving.tab || 'general')
      var target = prev.filter(function (f) {
        return f.cardSection === targetSection && tabOf(f) === wantTab && f.key !== key
      }).slice().sort(compareByCardOrder)
      var idx = (targetIndex == null || targetIndex < 0 || targetIndex > target.length)
        ? target.length : targetIndex
      target.splice(idx, 0, moving)
      var orderInTarget = {}
      target.forEach(function (f, i) { orderInTarget[f.key] = i })
      // Kaynak grup (bolum degistiyse) yeniden numaralandirilir
      var orderInSource = {}
      if (sourceSection !== targetSection || (moving.targetTab || null) !== (newTab || null)) {
        var srcTab = tabOf(moving)
        prev.filter(function (f) { return f.cardSection === sourceSection && tabOf(f) === srcTab && f.key !== key })
            .slice().sort(compareByCardOrder)
            .forEach(function (f, i) { orderInSource[f.key] = i })
      }
      return prev.map(function (f) {
        if (f.key === key) {
          return Object.assign({}, f, {
            cardSection: targetSection,
            cardOrder: orderInTarget[f.key],
            targetTab: newTab,
          })
        }
        if (orderInTarget[f.key] != null) return Object.assign({}, f, { cardOrder: orderInTarget[f.key] })
        if (orderInSource[f.key] != null) return Object.assign({}, f, { cardOrder: orderInSource[f.key] })
        return f
      })
    })
  }

  /** `over.id` hangi kaba (sekme+bolum) denk geliyor — satir da olabilir kap da. */
  function containerOfOverId(overId) {
    var zone = dropTargetFromId(overId)
    if (zone) return { tab: zone.tab, section: zone.section }
    var f = fields.find(function (x) { return x.key === overId })
    return f ? { tab: effTab(f), section: f.cardSection } : null
  }

  /* ── Serit degisiminde CANLI GERI BILDIRIM (2026-08-22) ────────────────────
     Kullanici bildirimi: "kendi seridi icinde diger alanlari kaydirabiliyor ama
     baska seritte ilk tasimada bunu yapamiyor."
     Sebep: `SortableContext` yalnizca KENDI kabindaki ogeleri kaydirir; alan hala
     kaynak kabin listesinde oldugu icin hedef seridin satirlari yer acmiyordu.

     ILK DENEME (geri alindi): dnd-kit cok-kapli reciplerinin standardi olan
     "imlec baska kaba gecince alani HEMEN oraya tasi" yolu denendi ve olculdu —
     kaydirma calisti ama YENI bir hata dogurdu: alan kaynak seritten cikinca o
     serit kisaliyor, ALTINDAKI her sey yukari kayiyor ve hedef serit imlecin
     altindan kaciyordu (olcum: imlec Serit 2'deki satirda birakildi, alan
     "Varsayilan"a dustu). Serit'ler yatay sutunlar degil DIKEY yigin oldugu icin
     bu kacinilmaz.

     KALICI COZUM: alan tasinmaz — hedef seritte yalnizca BOSLUK acilir. Kaynak
     satir yerinde (soluk) kaldigindan ustteki hicbir sey kaymaz; hedef yalnizca
     ASAGI dogru buyur, yani imlecin altindaki hedef sabit kalir. Gercek tasima
     tek noktada, `handleDragEnd`'de olur → Esc/iptal de bedava dogru calisir. */
  function handleDragOver(event) {
    var active = event.active, over = event.over
    if (!active || !over || isTabDrag(active.id)) { setDropHint(null); return }
    var moving = fields.find(function (f) { return f.key === String(active.id) })
    var r = resolveDrop(event)
    if (!moving || !r) { setDropHint(null); return }
    // Kendi seridi → sortable zaten kaydiriyor, bosluk acma.
    if (r.section === moving.cardSection && r.tab === effTab(moving)) { setDropHint(null); return }
    setDropHint(function (prev) {
      if (prev && prev.tab === r.tab && prev.section === r.section && prev.index === r.index) return prev
      return r
    })
  }


  function handleDragEnd(event) {
    setDragKey(null)
    setDropHint(null)
    var active = event.active, over = event.over
    if (!active || !over) { dragPointer.current = null; return }
    if (isTabDrag(active.id)) { dragPointer.current = null; handleTabDragEnd(event); return }
    var r = resolveDrop(event)
    dragPointer.current = null
    if (!r) return
    moveFieldTo(String(active.id), r.section, r.index, r.tab)
  }


  /** Kaydedilmis son hale don — kaydetmez, yalniz yerel degisiklikleri atar. */
  function resetChanges() {
    setFields(initialFields)
    setDefaultCardWidth(initialDefaultCardWidth)
    var maxAssigned = initialFields.reduce(function (acc, f) {
      return (typeof f.cardSection === 'number' && f.cardSection > acc) ? f.cardSection : acc
    }, 0)
    setMaxStrip(Math.max(1, Math.min(MAX_STRIPS, maxAssigned)))
  }

  function addStrip() {
    setMaxStrip(function (prev) { return Math.min(MAX_STRIPS, prev + 1) })
  }

  /**
   * Bir seridi kaldirir (2026-08-20 kullanici istegi: "fazla seritler silinebilmeli").
   * Serit sayisi kalici bir ayar DEGIL — kayittaki cardSection atamalarindan turetilir.
   * Bu yuzden silmek = o seridin alanlarini "Varsayilan"a dusurmek + ustundeki
   * seritleri bir asagi kaydirmak (numaralandirma bosluksuz kalsin). Aksi halde
   * kaydet/yeniden-ac dongusunde silinen serit geri gelirdi.
   */
  function removeStrip(n) {
    setFields(function (prev) {
      return prev.map(function (f) {
        if (f.cardSection === n) return Object.assign({}, f, { cardSection: null, cardOrder: null })
        if (typeof f.cardSection === 'number' && f.cardSection > n) {
          return Object.assign({}, f, { cardSection: f.cardSection - 1 })
        }
        return f
      })
    })
    setMaxStrip(function (prev) { return Math.max(1, prev - 1) })
  }

  async function handleSave() {
    if (saving) return
    setSaving(true)
    setError(null)
    try {
      var payload = {
        formCode: formCode,
        defaultCardWidth: (typeof defaultCardWidth === 'number') ? defaultCardWidth : null,
        fields: fields.map(function (f) {
          return {
            key: f.key,
            isVisible: f.isVisible,
            isRequired: f.isRequired,
            defaultValue: f.defaultValue.trim() || null,
            labelText: f.labelText.trim() || null,
            labelStyle: f.labelStyle || null,
            visibleIf: f.visibleIf.trim() || null,
            requiredIf: f.requiredIf.trim() || null,
            cardSection: (typeof f.cardSection === 'number') ? f.cardSection : null,
            cardOrder: (typeof f.cardOrder === 'number') ? f.cardOrder : null,
            cardWidth: (typeof f.cardWidth === 'number') ? f.cardWidth : null,
            cellWidthPx: (typeof f.cellWidthPx === 'number') ? f.cellWidthPx : null,
            targetTab: f.targetTab || null,
          }
        }),
        tabs: tabs.map(function (t, i) {
          return {
            key: t.key, isVisible: t.isVisible, sortOrder: i,
            labelText: t.labelText.trim() || null,
            targetTabKey: t.targetTabKey || null,
            isCustom: t.isCustom === true,
          }
        }),
        // Serit satir yukseklikleri — yalniz DEGER VERILMIS seritler gonderilir;
        // gonderilmeyen serit sunucuda satir acmaz (varsayilan, fail-open).
        // 2026-08-22 (kullanici karari): "Izgara (tablo)" secenegi KALDIRILDI —
        // duzen daima serbest. `cardWidth` (1..12 span) verisi DB'de silinmez,
        // yalniz artik duzenlenmez; genislik alan basina piksel (`cellWidthPx`).
        layoutMode: 'free',
        stripHeights: Object.keys(stripHeights).map(function (k) {
          return { section: parseInt(k, 10), rowHeight: stripHeights[k] }
        }).filter(function (x) { return x.section > 0 && typeof x.rowHeight === 'number' }),
      }
      var resp = await fetch('/api/form-behavior/save', {
        method: 'POST',
        credentials: 'same-origin',
        headers: {
          'Content-Type': 'application/json',
          'Accept': 'application/json',
          'RequestVerificationToken': readCsrfToken(),
        },
        body: JSON.stringify(payload),
      })
      var data = null
      try { data = await resp.json() } catch (_) {}
      if (!resp.ok || !data || data.ok !== true) {
        setError((data && data.error) || ('Kaydedilemedi (HTTP ' + resp.status + ')'))
        return
      }
      if (typeof onSaved === 'function') onSaved()
    } catch (e) {
      setError('Hata: ' + (e && e.message ? e.message : String(e)))
    } finally {
      setSaving(false)
    }
  }

  var showTabBadge = tabs.length > 0
  var tabTree = buildTabTree(fields, tabs, maxStrip)
  /* ÜST BILGI formunda 'lines' sekmesi bir BILESEN sekmesidir: icerigi kalem
     gridi'dir, ic duzeni KALEM formunun kendi Alan Duzeni'nden yonetilir. Bu
     yuzden burada serit/bolum makinesi (Kimlik / Serit N / yukseklik / birakma
     hedefi) GOSTERILMEZ — kullanici istegi 2026-08-22.
     Ayrim: kalem formunun KENDI editorunde de sekme anahtari 'lines'tir; oradaki
     'lines' asil icerik oldugu icin serit duzeni GEREKLIDIR. Ust bilgi formunu
     'general' sekmesinin varligindan anliyoruz. */
  var isHeaderForm = tabTree.some(function (t) { return t.key === 'general' })
  function isComponentTab(tabKey) { return isHeaderForm && tabKey === 'lines' }
  var customCount = tabs.filter(function (t) { return t.isCustom }).length

  /** Yeni ozel sekme — bir sonraki bos c<N> anahtarini secer. */
  function addCustomTab() {
    if (customCount >= 6) return
    var used = {}
    tabs.forEach(function (t) { if (t.isCustom) used[t.key] = true })
    var n = 1
    while (used['c' + n] && n < 20) n++
    var key = 'c' + n
    setTabs(function (prev) {
      return prev.concat([{
        key: key, label: 'Sekme ' + n, locked: false, isVisible: true,
        labelText: 'Sekme ' + n, targetTabKey: '', isCustom: true,
      }])
    })
  }

  /** Ozel sekmeyi sil — YALNIZ bos oldugunda (alanlari once tasinmali). */
  function removeCustomTab(key) {
    var used = fields.some(function (f) { return effTab(f) === key })
    if (used) return
    setTabs(function (prev) { return prev.filter(function (t) { return t.key !== key }) })
  }

  function renameTab(key, name) {
    setTabs(function (prev) {
      return prev.map(function (t) { return t.key === key ? Object.assign({}, t, { labelText: name }) : t })
    })
  }
  var behaviorField = behaviorKey ? fields.find(function (f) { return f.key === behaviorKey }) : null

  return createPortal(
    <div
      onClick={function (e) { if (e.target === e.currentTarget && !saving) onClose() }}
      onKeyDown={function (e) { if (e.key === 'Escape' && !saving) onClose() }}
      className="fixed inset-0 z-[60] flex items-center justify-center p-4"
      style={{ background: 'rgba(15,23,42,0.45)', backdropFilter: 'blur(5px)', WebkitBackdropFilter: 'blur(5px)' }}
    >
      <div
        className="w-full max-w-[1060px] flex flex-col overflow-hidden rounded-2xl border border-slate-200 bg-[#fff] shadow-2xl dark:border-white/10 dark:bg-slate-900"
        style={{ height: 'min(880px, calc(100vh - 64px))' }}
        role="dialog"
        aria-label="Alan Düzeni"
      >
        {/* Header */}
        <div className="flex items-center gap-3 px-5 py-4 border-b border-slate-200 dark:border-white/[0.08] flex-shrink-0">
          <div className="w-9 h-9 rounded-xl flex items-center justify-center bg-indigo-50 border border-indigo-200 text-indigo-600 dark:bg-indigo-500/15 dark:border-indigo-400/30 dark:text-indigo-300">
            <SlidersHorizontal size={17} strokeWidth={1.9} />
          </div>
          <div className="flex-1 min-w-0">
            <div className="text-[14px] font-bold text-slate-800 dark:text-white/90">Alan Düzeni</div>
            <div className="text-[11px] text-slate-600 dark:text-white/60">
              Görünürlük, zorunluluk, başlık metni, genişlik ve alanın hangi sekme/şeritte duracağı bu formun tüm kullanıcıları için geçerlidir.
            </div>
          </div>
          <button
            type="button"
            onClick={function () { if (!saving) onClose() }}
            className="w-8 h-8 rounded-lg flex items-center justify-center text-slate-500 hover:text-rose-600 hover:bg-rose-50 dark:text-white/55 dark:hover:text-rose-300 dark:hover:bg-rose-500/10 transition-colors"
            title="Kapat (Esc)"
          >
            <XIcon size={15} strokeWidth={2} />
          </button>
        </div>

        {/* Body */}
        <div className="flex-1 min-h-0 overflow-y-auto px-5 py-4">
          {loading && (
            <div className="py-10 text-center text-[12px] text-slate-500 dark:text-white/55">Yükleniyor…</div>
          )}

          {!loading && !error && (
            <>
              {/* ── Bölüm şeridi araç çubuğu ── */}
              <div className="flex items-center gap-2 mb-2">
                <LayoutGrid size={13} className="text-slate-500 dark:text-white/55 flex-shrink-0" />
                <span className="text-[11.5px] font-bold text-slate-700 dark:text-white/80">Alanlar — Bölüme Göre</span>
                <span className="text-[10.5px] text-slate-500 dark:text-white/50">({maxStrip}/{MAX_STRIPS} şerit)</span>
                <div className="flex-1" />
                <button
                  type="button"
                  onClick={addStrip}
                  disabled={maxStrip >= MAX_STRIPS}
                  title={maxStrip >= MAX_STRIPS ? ('En fazla ' + MAX_STRIPS + ' şerit tanımlanabilir') : 'Yeni şerit ekle'}
                  className={'flex items-center gap-1 px-2.5 py-1 rounded-md text-[10.5px] font-semibold border transition-colors ' + (
                    maxStrip >= MAX_STRIPS
                      ? 'text-slate-300 border-slate-200 cursor-not-allowed dark:text-white/25 dark:border-white/10'
                      : 'text-indigo-600 border-indigo-200 bg-indigo-50 hover:bg-indigo-100 dark:text-indigo-300 dark:border-indigo-400/30 dark:bg-indigo-500/10 dark:hover:bg-indigo-500/20'
                  )}
                >
                  <Plus size={12} strokeWidth={2.4} /> Yeni Şerit Ekle
                </button>
                {/* 2026-08-21 Faz 4: kullanici tanimli sekme. Icine alan surukleyip
                    birakilir; bos kalirsa silinebilir. En fazla 6 (sunucu da sinirlar). */}
                <button
                  type="button"
                  onClick={addCustomTab}
                  disabled={customCount >= 6}
                  title={customCount >= 6 ? 'En fazla 6 özel sekme' : 'Yeni sekme ekle'}
                  className={'flex items-center gap-1 px-2.5 py-1 rounded-md text-[10.5px] font-semibold border transition-colors ' + (
                    customCount >= 6
                      ? 'text-slate-300 border-slate-200 cursor-not-allowed dark:text-white/25 dark:border-white/10'
                      : 'text-indigo-600 border-indigo-200 bg-indigo-50 hover:bg-indigo-100 dark:text-indigo-300 dark:border-indigo-400/30 dark:bg-indigo-500/10 dark:hover:bg-indigo-500/20'
                  )}
                >
                  <Plus size={12} strokeWidth={2.4} /> Yeni Sekme Ekle
                </button>
                {/* Onizleme MODALDA (2026-08-21) — eskiden liste ustunde hep acikti. */}
                <button
                  type="button"
                  onClick={function () { setPreviewOpen(true) }}
                  title="Düzeni önizle ve genişlik ayarla"
                  className="flex items-center gap-1 px-2.5 py-1 rounded-md text-[10.5px] font-semibold border transition-colors text-slate-600 border-slate-200 hover:bg-slate-50 dark:text-white/70 dark:border-white/10 dark:hover:bg-white/[0.08]"
                >
                  <Eye size={12} strokeWidth={2.2} /> Önizleme
                </button>
              </div>

              {/* Arama — alan sayisi arttikca liste uzuyor (Sutun Ayarlari paneliyle ayni desen) */}
              <div className="flex items-center gap-2 mb-2 px-2.5 py-1.5 rounded-lg border border-slate-200 bg-[#fff] dark:border-white/10 dark:bg-white/[0.04]">
                <Search size={13} strokeWidth={2} className="flex-shrink-0 text-slate-400 dark:text-white/35" />
                <input
                  type="text"
                  value={search}
                  placeholder="Alan ara…"
                  onChange={function (e) { setSearch(e.target.value) }}
                  className="flex-1 min-w-0 bg-transparent outline-none text-[11.5px] text-slate-700 dark:text-white/80"
                />
                {search && (
                  <button type="button" onClick={function () { setSearch('') }} title="Aramayı temizle"
                          className="flex-shrink-0 text-slate-400 hover:text-slate-600 dark:text-white/35 dark:hover:text-white/70">
                    <XIcon size={12} strokeWidth={2.2} />
                  </button>
                )}
              </div>

              {/* ── Alanlar: bölüme göre gruplu, bölümler arası SÜRÜKLENEBİLİR ──
                  Bos bolumler de render edilir — birakma hedefi olmalari icin. */}
              {/* measuring Always: serit yukseklikleri surukleme SIRASINDA degisiyor
                  (satir cikinca kap kisaliyor). Varsayilan olcum bir kez alindigi icin
                  hedef dikdortgenleri bayatliyor ve birakma birkac px kayabiliyordu. */}
              <DndContext
                sensors={dndSensors}
                collisionDetection={preciseCollision}
                measuring={{ droppable: { strategy: MeasuringStrategy.Always } }}
                onDragStart={function (e) {
                  var ae = e.activatorEvent
                  dragPointer.current = (ae && typeof ae.clientY === 'number') ? { x: ae.clientX, y: ae.clientY } : null
                  setDragKey(isTabDrag(e.active.id) ? null : String(e.active.id))
                }}
                onDragMove={function (e) {
                  var ae = e.activatorEvent
                  if (ae && typeof ae.clientY === 'number') {
                    dragPointer.current = { x: ae.clientX + e.delta.x, y: ae.clientY + e.delta.y }
                  }
                }}
                onDragOver={handleDragOver}
                onDragCancel={function () { setDragKey(null); setDropHint(null); dragPointer.current = null }}
                onDragEnd={handleDragEnd}>
              {/* Sekme kartlari TEK surukleme baglamini paylasir; ayrim id on eki
                  ile yapilir ('tabcard:<key>'). Ic ice DndContext denendi — sekme
                  surukleme hic tetiklenmedi (olculdu), tek baglam guvenli yol. */}
              <SortableContext items={tabTree.map(function (t) { return 'tabcard:' + t.key })}
                               strategy={verticalListSortingStrategy}>
              {tabTree.map(function (tab) {
              var isCollapsed = collapsedTabs[tab.key] === true
              return (
                <TabCard
                  key={'tab-' + tab.key}
                  tabKey={tab.key}
                  collapsed={isCollapsed}
                  dim={!tab.isVisible}
                  onToggle={function () {
                    setCollapsedTabs(function (prev) {
                      var next = Object.assign({}, prev)
                      if (next[tab.key]) delete next[tab.key]; else next[tab.key] = true
                      return next
                    })
                  }}
                  header={
                    <div className="flex items-center gap-2 flex-1 min-w-0">
                      {/* Sekme adi — bos birakilirsa katalog adi kullanilir */}
                      <input
                        type="text"
                        value={tab.labelText}
                        maxLength={40}
                        placeholder={tab.rawLabel}
                        title="Sekme başlığı — boş bırakılırsa varsayılan ad kullanılır"
                        onChange={function (e) { renameTab(tab.key, e.target.value) }}
                        className={inputCls + ' max-w-[200px] font-semibold'}
                      />
                      <span className="text-[10px] text-slate-400 dark:text-white/35 flex-shrink-0">
                        {tab.fieldCount} alan
                      </span>
                      {!tab.isCustom && tab.fieldCount === 0 && (
                        <span className="text-[10px] text-amber-600 dark:text-amber-300 flex-shrink-0">boş — belgede gizlenir</span>
                      )}
                      <div className="flex-1" />
                      {/* Icerigi baska sekmeye tasi — kaynak bos kalirsa belgede gizlenir */}
                      <select
                        value={tab.targetTabKey}
                        title="İçeriği başka bir sekmeye taşı"
                        onChange={function (e) {
                          var v = e.target.value
                          setTabs(function (prev) { return prev.map(function (x) { return x.key === tab.key ? Object.assign({}, x, { targetTabKey: v }) : x }) })
                        }}
                        className={inputCls + ' max-w-[160px] flex-shrink-0'}
                      >
                        <option value="">Kendi sekmesi</option>
                        {tabTree.filter(function (o) { return o.key !== tab.key }).map(function (o) {
                          return <option key={o.key} value={o.key}>→ {o.label}</option>
                        })}
                      </select>
                      {tab.locked ? (
                        <span title="Bu sekme gizlenemez" className="flex-shrink-0"><Lock size={12} className="text-slate-400 dark:text-white/45" /></span>
                      ) : (
                        <button
                          type="button"
                          onClick={function () {
                            setTabs(function (prev) { return prev.map(function (x) { return x.key === tab.key ? Object.assign({}, x, { isVisible: !x.isVisible }) : x }) })
                          }}
                          title={tab.isVisible ? 'Sekmeyi gizle' : 'Sekmeyi göster'}
                          className="flex-shrink-0 text-slate-500 hover:text-indigo-600 dark:text-white/55 dark:hover:text-indigo-300"
                        >
                          {tab.isVisible ? <Eye size={13} strokeWidth={2} /> : <EyeOff size={13} strokeWidth={2} className="text-rose-500 dark:text-rose-300" />}
                        </button>
                      )}
                      {tab.isCustom && (function () {
                        var canDel = tab.fieldCount === 0
                        return (
                          <button type="button" disabled={!canDel}
                            onClick={function () { if (canDel) removeCustomTab(tab.key) }}
                            title={canDel ? 'Sekmeyi sil' : 'Önce alanları başka sekmeye taşıyın'}
                            className={'flex-shrink-0 flex items-center justify-center w-5 h-5 rounded transition-colors ' + (
                              canDel ? 'text-rose-500 hover:bg-rose-50 dark:text-rose-300 dark:hover:bg-rose-500/15'
                                     : 'text-slate-300 cursor-not-allowed dark:text-white/20')}>
                            <Trash2 size={11} strokeWidth={2.2} />
                          </button>
                        )
                      })()}
                    </div>
                  }
                >
              {isComponentTab(tab.key) ? (
                <div className="pb-2.5">
                  <div className="mb-2 px-2.5 py-1.5 rounded-lg border border-dashed border-slate-200 bg-slate-50/60 text-[10.5px] text-slate-500 dark:border-white/10 dark:bg-white/[0.03] dark:text-white/50">
                    Bu sekmenin içeriği kalem gridi. Kalem kartının şerit/hücre düzeni
                    <span className="font-semibold"> Kalem Bilgileri</span> alan düzeninden yönetilir.
                    Aşağıdakiler belge üst bilgisine ait, kalem alanına yerleşmiş alanlardır.
                  </div>
                  {(function () {
                    var own = []
                    tab.sections.forEach(function (g) {
                      g.fields.forEach(function (f) {
                        if (search) {
                          var q = search.toLocaleLowerCase('tr')
                          if (String(f.label || '').toLocaleLowerCase('tr').indexOf(q) < 0 &&
                              String(f.key || '').toLocaleLowerCase('tr').indexOf(q) < 0) return
                        }
                        own.push(f)
                      })
                    })
                    if (own.length === 0) {
                      return (
                        <div className="text-[10.5px] text-slate-400 dark:text-white/30 px-2.5 py-2">
                          Bu sekmede ayarlanacak üst bilgi alanı yok.
                        </div>
                      )
                    }
                    // Toplamlar bloguna ait olanlar ayri baslik altinda; kalani "Diger".
                    var gruplar = [
                      { key: 'totals', label: 'Toplamlar Bloğu', hint: 'Kalem listesinin altındaki toplamlar satırlarında görünür',
                        fields: own.filter(function (f) { return !!TOTALS_BLOCK_KEYS[f.key] }) },
                      { key: 'other', label: 'Diğer', hint: null,
                        fields: own.filter(function (f) { return !TOTALS_BLOCK_KEYS[f.key] }) },
                    ].filter(function (g) { return g.fields.length > 0 })
                    return (
                      <div className="flex flex-col gap-3">
                        {gruplar.map(function (g) {
                          return (
                            <div key={g.key}>
                              <div className="flex items-center gap-1.5 mb-1.5">
                                <span className="px-1.5 py-0.5 rounded text-[9.5px] font-bold border bg-amber-50 text-amber-700 border-amber-200 dark:bg-amber-500/15 dark:text-amber-300 dark:border-amber-400/30">
                                  {g.label}
                                </span>
                                {g.hint && <span className="text-[10px] text-slate-400 dark:text-white/40">{g.hint}</span>}
                                <span className="text-[10px] text-slate-400 dark:text-white/35">({g.fields.length})</span>
                              </div>
                              <div className="flex flex-col gap-1.5">
                                {g.fields.map(function (f) {
                                  return (
                                    <div key={f.key}
                                         className="rounded-lg border border-slate-200 bg-slate-50/60 dark:border-white/10 dark:bg-white/[0.03] px-2.5 py-2">
                                      {renderFieldRow(f)}
                                    </div>
                                  )
                                })}
                              </div>
                            </div>
                          )
                        })}
                      </div>
                    )
                  })()}
                </div>
              ) : tab.sections.map(function (group) {
                var visibleFields = search
                  ? group.fields.filter(function (f) {
                      var q = search.toLocaleLowerCase('tr')
                      return String(f.label || '').toLocaleLowerCase('tr').indexOf(q) >= 0 ||
                             String(f.key || '').toLocaleLowerCase('tr').indexOf(q) >= 0
                    })
                  : group.fields
                // Arama aktifken bos gruplari gizle; arama yokken bos gruplar
                // birakma hedefi olarak KALIR.
                if (search && visibleFields.length === 0) return null
                return (
                  <div key={group.key} className="mb-4">
                    <div className="flex items-center gap-1.5 mb-1.5">
                      <span className={'px-1.5 py-0.5 rounded text-[9.5px] font-bold border ' + (
                        group.kind === 'identity'
                          ? 'bg-indigo-50 text-indigo-600 border-indigo-200 dark:bg-indigo-500/15 dark:text-indigo-300 dark:border-indigo-400/30'
                          : group.kind === 'strip'
                            ? 'bg-violet-50 text-violet-600 border-violet-200 dark:bg-violet-500/15 dark:text-violet-300 dark:border-violet-400/30'
                            : 'bg-slate-100 text-slate-500 border-slate-200 dark:bg-white/[0.06] dark:text-white/55 dark:border-white/10'
                      )}>
                        {group.label}
                      </span>
                      {group.hint && <span className="text-[10px] text-slate-400 dark:text-white/40">{group.hint}</span>}
                      <span className="text-[10px] text-slate-400 dark:text-white/35">({visibleFields.length})</span>
                      {/* Serit satir yuksekligi (2026-08-20 kullanici istegi: "serit basina
                          ayri"). Bos = varsayilan (kart kendi olcusunu kullanir); deger
                          verilince o seridin TUM hucreleri bu yuksekligi alir. Sifirla
                          okuyla varsayilana donulur. */}
                      {group.kind === 'strip' && (function () {
                        var h = stripHeights[group.section]
                        function setH(v) {
                          setStripHeights(function (prev) {
                            var next = Object.assign({}, prev)
                            if (v == null) delete next[group.section]; else next[group.section] = v
                            return next
                          })
                        }
                        var cur = (typeof h === 'number') ? h : 36
                        return (
                          <span className="flex items-center gap-1 ml-1">
                            <span className="text-[9.5px] font-semibold text-slate-500 dark:text-white/50">Yükseklik</span>
                            <span className="flex items-center rounded-md border border-slate-200 bg-[#fff] dark:border-white/10 dark:bg-white/[0.04] overflow-hidden">
                              <button type="button" onClick={function () { setH(Math.max(28, cur - 4)) }}
                                      disabled={cur <= 28} title="Daralt"
                                      className="w-5 h-5 flex items-center justify-center text-slate-500 hover:text-indigo-600 dark:text-white/55 dark:hover:text-indigo-300 disabled:opacity-30">
                                <Minus size={10} strokeWidth={2.4} />
                              </button>
                              <span style={{ minWidth: 58 }}
                                    className={'px-1 text-[10px] font-bold tabular-nums text-center whitespace-nowrap ' + (
                                      typeof h === 'number' ? 'text-slate-700 dark:text-white/85' : 'text-slate-400 italic dark:text-white/40')}>
                                {typeof h === 'number' ? (h + 'px') : 'Varsayılan'}
                              </span>
                              <button type="button" onClick={function () { setH(Math.min(96, cur + 4)) }}
                                      disabled={cur >= 96} title="Genişlet"
                                      className="w-5 h-5 flex items-center justify-center text-slate-500 hover:text-indigo-600 dark:text-white/55 dark:hover:text-indigo-300 disabled:opacity-30">
                                <Plus size={10} strokeWidth={2.4} />
                              </button>
                            </span>
                            <button type="button" onClick={function () { if (typeof h === 'number') setH(null) }}
                                    disabled={typeof h !== 'number'} title="Varsayılana dön"
                                    style={{ width: 13, visibility: typeof h === 'number' ? 'visible' : 'hidden' }}
                                    className="flex-shrink-0 text-slate-400 hover:text-indigo-600 dark:text-white/35 dark:hover:text-indigo-300">
                              <RotateCcw size={11} strokeWidth={2.2} />
                            </button>
                          </span>
                        )
                      })()}
                      {/* 2026-08-20 (kullanici istegi): silme butonu SERIDIN YANINDA.
                          Yalnizca serit gruplarinda ve yalnizca serit BOSSA aktif —
                          dolu bir seridi silmek alanlari sessizce "Varsayilan"a
                          dusururdu. Silinince ustundeki seritler bir asagi kayar
                          (removeStrip yeniden numaralandirir). */}
                      {group.kind === 'strip' && (function () {
                        var canDelete = maxStrip > 1 && group.fields.length === 0
                        return (
                          <button
                            type="button"
                            onClick={function () { if (canDelete) removeStrip(group.section) }}
                            disabled={!canDelete}
                            title={
                              maxStrip <= 1 ? 'En az bir şerit kalmalı'
                                : (group.fields.length > 0
                                    ? (group.label + ' içinde ' + group.fields.length + ' alan var — önce onları başka bölüme taşıyın')
                                    : (group.label + ' sil'))
                            }
                            className={'flex items-center justify-center w-5 h-5 rounded transition-colors ' + (
                              canDelete
                                ? 'text-rose-500 hover:bg-rose-50 dark:text-rose-300 dark:hover:bg-rose-500/15'
                                : 'text-slate-300 cursor-not-allowed dark:text-white/20'
                            )}
                          >
                            <Trash2 size={11} strokeWidth={2.2} />
                          </button>
                        )
                      })()}
                    </div>
                    <SectionDropZone tab={tab.key} section={group.section}>
                    <SortableContext items={visibleFields.map(function (f) { return f.key })}
                                     strategy={verticalListSortingStrategy}>
                    {(function () {
                      var hintOn = dropHint && dropHint.tab === tab.key && dropHint.section === group.section
                      var hintIdx = hintOn
                        ? (dropHint.index == null ? visibleFields.length : Math.min(dropHint.index, visibleFields.length))
                        : -1
                      return (
                    <div className="flex flex-col gap-1.5 min-h-[34px]">
                      {hintIdx >= visibleFields.length && <DropPlaceholder />}
                      {visibleFields.length === 0 && hintIdx < 0 && (
                        <div className="text-[10.5px] text-slate-400 dark:text-white/30 px-2.5 py-2 border border-dashed border-slate-200 dark:border-white/10 rounded-lg">
                          Boş — alan sürükleyip bırakabilirsiniz
                        </div>
                      )}
                      {visibleFields.map(function (f, fIdx) {
                        return (
                          <Fragment key={f.key}>
                          {fIdx === hintIdx && <DropPlaceholder />}
                          <SortableRow id={f.key}
                            locked={f.movable === false || !!HGROUP_FOLLOWER[f.key]}
                            lockedTitle={HGROUP_FOLLOWER[f.key]
                              ? 'Para Birimi ile birlikte hareket eder'
                              : 'Bu alan bagimsiz bir hucre degil — tasinamaz'}>
                            {renderFieldRow(f)}
                          </SortableRow>
                          </Fragment>
                        )
                      })}
                    </div>
                      )
                    })()}
                    </SortableContext>
                    </SectionDropZone>
                  </div>
                )
              })}
                </TabCard>
              )
              })}
              </SortableContext>

              {/* Imlecin altinda tasinan kopya — surukleme boyunca HER ZAMAN gorunur,
                  kap sinirlarindan ve overflow'dan etkilenmez (portal degil, DndContext
                  icinde konumlanir ama transform ile serbest hareket eder). */}
              <DragOverlay dropAnimation={{ duration: 160, easing: 'cubic-bezier(.2,0,.2,1)' }}>
                {dragKey ? (function () {
                  var df = fields.find(function (x) { return x.key === dragKey })
                  if (!df) return null
                  return (
                    <div className="rounded-lg border border-indigo-300 bg-[#fff] shadow-lg px-2.5 py-2 flex items-center gap-2 dark:border-indigo-400/50 dark:bg-[#1b2233]">
                      <GripVertical size={13} strokeWidth={2} className="flex-shrink-0 text-indigo-500 dark:text-indigo-300" />
                      <span className="text-[11.5px] font-semibold text-slate-700 dark:text-white/85 truncate">
                        {df.labelText || df.label || df.key}
                      </span>
                      {showTabBadge && tabLabels[df.tab] && (
                        <span className="px-1.5 py-0.5 rounded text-[9px] font-semibold bg-slate-100 text-slate-500 border border-slate-200 dark:bg-white/[0.05] dark:text-white/50 dark:border-white/10 flex-shrink-0">
                          {tabLabels[df.tab]}
                        </span>
                      )}
                    </div>
                  )
                })() : null}
              </DragOverlay>
              </DndContext>

            </>
          )}

          {error && (
            <div className="mt-3 flex items-center gap-2 text-[11.5px] text-rose-600 dark:text-rose-300">
              <AlertTriangle size={13} /> {error}
            </div>
          )}
        </div>

        {/* Footer */}
        <div className="flex items-center gap-2 px-5 py-3.5 border-t border-slate-200 dark:border-white/[0.08] bg-slate-50/60 dark:bg-white/[0.02] flex-shrink-0">
          {/* Sifirla — kaydedilmis son hale doner. KAYDETMEZ; kalici yazma hala
              "Kaydet" ile. (Sutun Ayarlari panelindeki Sifirla ile ayni jest, ama
              orasi anlik kaydeder, burasi ETMEZ — form geneli yonetisim.) */}
          <button
            type="button"
            onClick={function () { if (!saving) resetChanges() }}
            disabled={saving || loading}
            title="Kaydedilmiş son hale dön (kaydetmez)"
            className="flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-[12px] font-semibold border transition-colors bg-[#fff] text-slate-600 border-slate-200 hover:bg-slate-100 dark:bg-white/[0.04] dark:text-white/70 dark:border-white/10 dark:hover:bg-white/[0.08] disabled:opacity-40 disabled:cursor-not-allowed"
          >
            <RotateCcw size={12} strokeWidth={2.2} /> Sıfırla
          </button>
          <div className="flex-1" />
          <button
            type="button"
            onClick={function () { if (!saving) onClose() }}
            disabled={saving}
            className="px-3.5 py-1.5 rounded-lg text-[12px] font-semibold border transition-colors bg-[#fff] text-slate-600 border-slate-200 hover:bg-slate-100 dark:bg-white/[0.04] dark:text-white/70 dark:border-white/10 dark:hover:bg-white/[0.08]"
          >
            Vazgeç
          </button>
          <button
            type="button"
            onClick={handleSave}
            disabled={saving || loading || !!(error && fields.length === 0)}
            className={'px-4 py-1.5 rounded-lg text-[12px] font-bold border transition-colors ' + (
              saving
                ? 'bg-indigo-300 text-white border-indigo-300 cursor-wait dark:bg-indigo-500/40 dark:border-indigo-400/30'
                : 'bg-indigo-600 text-white border-indigo-600 hover:bg-indigo-700 dark:bg-indigo-500 dark:border-indigo-500 dark:hover:bg-indigo-600'
            )}
          >
            {saving ? 'Kaydediliyor…' : 'Kaydet'}
          </button>
        </div>
      </div>

      {/* Alan davranisi modali — Varsayilan Deger / Gorunurluk / Zorunluluk.
          Kendi portal'ini acar (z-[120]), bu modalin USTUNDE durur. Uygula
          yalnizca yerel state'i gunceller; kalici yazma yine alttaki "Kaydet". */}
      {/* Onizleme modali (2026-08-21) — taslak olculer "Uygula" ile bu ekrana
          aktarilir; kalici yazma yine "Kaydet" ile. `key` ile her acilista yeniden
          kurulur ki taslak bir onceki oturumun degerlerinden baslamasin. */}
      {previewOpen && (
        <LayoutPreviewModal
          key={'preview-' + fields.length}
          tabTree={tabTree}
          stripHeights={stripHeights}
          onClose={function () { setPreviewOpen(false) }}
          onApply={function (r) {
            setFields(function (prev) {
              return prev.map(function (f) {
                if (!Object.prototype.hasOwnProperty.call(r.widths, f.key)) return f
                var w = r.widths[f.key]
                var next = (typeof w === 'number') ? w : null
                return (next === f.cellWidthPx) ? f : Object.assign({}, f, { cellWidthPx: next })
              })
            })
            setStripHeights(Object.assign({}, r.heights))
          }}
        />
      )}

      <FieldBehaviorModal
        field={behaviorField}
        fields={fields.map(function (f) { return { key: f.key, label: f.labelText || f.label || f.key } })}
        onApply={function (patch) { patchField(behaviorKey, patch) }}
        onClose={function () { setBehaviorKey(null) }}
      />
    </div>,
    document.body
  )
}
