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
import { useState, useEffect } from 'react'
import { createPortal } from 'react-dom'
import FieldBehaviorModal from './FieldBehaviorModal'
import {
  DndContext, closestCenter, PointerSensor, useSensor, useSensors, useDroppable,
} from '@dnd-kit/core'
import {
  SortableContext, useSortable, verticalListSortingStrategy,
} from '@dnd-kit/sortable'
import { CSS } from '@dnd-kit/utilities'
import {
  SlidersHorizontal, X as XIcon, Eye, EyeOff, Lock, ArrowUp, ArrowDown,
  AlertTriangle, Plus, Minus, LayoutGrid, Settings2, Trash2,
  GripVertical, ChevronDown, ChevronRight, Search, RotateCcw,
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
function WidthStepper(props) {
  var value = (typeof props.value === 'number') ? props.value : null
  var fallback = props.fallback
  var allowClear = props.allowClear === true
  var onChange = props.onChange
  var display = (value !== null) ? value : fallback
  function step(delta) {
    var next = Math.max(CARD_WIDTH_MIN, Math.min(CARD_WIDTH_MAX, display + delta))
    onChange(next)
  }
  return (
    /* 2026-08-20 (kullanici istegi): SABIT olculer. Etiket "Varsayilan (3)" (uzun)
       ile "3/12" (kisa) arasinda gidip geldigi ve temizle butonu gelip gittigi icin
       adimlayicinin genisligi degisiyor, bu da satirdaki Gorunur/Zorunlu
       switch'lerini kaydiriyordu. Hem etikete min-width hem temizle yuvasina sabit
       yer verildi (buton yokken yuva GORUNMEZ ama yer kaplar). */
    <div className="flex items-center gap-1 flex-shrink-0">
      <div className="flex items-center rounded-md border border-slate-200 bg-[#fff] dark:border-white/10 dark:bg-white/[0.04] overflow-hidden">
        <button
          type="button"
          onClick={function () { step(-1) }}
          disabled={display <= CARD_WIDTH_MIN}
          title="Bir sütun daralt"
          className="w-5 h-5 flex items-center justify-center text-slate-500 hover:text-indigo-600 hover:bg-slate-50 dark:text-white/55 dark:hover:text-indigo-300 dark:hover:bg-white/[0.08] disabled:opacity-30 disabled:cursor-not-allowed"
        >
          <Minus size={11} strokeWidth={2.4} />
        </button>
        <span
          style={{ minWidth: 78 }}
          className={'px-1.5 text-[10.5px] font-bold tabular-nums text-center whitespace-nowrap ' + (
            value !== null ? 'text-slate-700 dark:text-white/85' : 'text-slate-400 italic dark:text-white/40'
          )}
          title={value !== null ? (value + ' / 12 sütun') : ('Ayarlanmamış — form varsayılanı: ' + fallback + ' / 12')}
        >
          {value !== null ? (value + '/12') : ('Varsayılan (' + fallback + ')')}
        </span>
        <button
          type="button"
          onClick={function () { step(1) }}
          disabled={display >= CARD_WIDTH_MAX}
          title="Bir sütun genişlet"
          className="w-5 h-5 flex items-center justify-center text-slate-500 hover:text-indigo-600 hover:bg-slate-50 dark:text-white/55 dark:hover:text-indigo-300 dark:hover:bg-white/[0.08] disabled:opacity-30 disabled:cursor-not-allowed"
        >
          <Plus size={11} strokeWidth={2.4} />
        </button>
      </div>
      {allowClear && (
        <button
          type="button"
          onClick={function () { if (value !== null) onChange(null) }}
          disabled={value === null}
          aria-hidden={value === null}
          tabIndex={value === null ? -1 : 0}
          title="Varsayılana dön"
          style={{ width: 13, visibility: value === null ? 'hidden' : 'visible' }}
          className="flex-shrink-0 text-slate-400 hover:text-indigo-600 dark:text-white/35 dark:hover:text-indigo-300"
        >
          <RotateCcw size={11} strokeWidth={2.2} />
        </button>
      )}
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
  var over = useDroppable({ id: 'sec:' + (props.section === null ? 'null' : props.section) })
  return (
    <div
      ref={over.setNodeRef}
      className={'rounded-lg transition-colors ' + (over.isOver
        ? 'bg-indigo-50/70 outline outline-1 outline-dashed outline-indigo-300 dark:bg-indigo-500/10 dark:outline-indigo-400/40'
        : '')}
    >
      {props.children}
    </div>
  )
}

/** Surukleneble alan satiri — tutamak yalnizca GripVertical, govde tiklanabilir kalir. */
function SortableRow(props) {
  var sortable = useSortable({ id: props.id })
  var style = {
    transform: CSS.Transform.toString(sortable.transform),
    transition: sortable.transition,
    opacity: sortable.isDragging ? 0.45 : 1,
  }
  return (
    <div ref={sortable.setNodeRef} style={style}
         className="rounded-lg border border-slate-200 bg-slate-50/60 dark:border-white/10 dark:bg-white/[0.03] px-2.5 py-2 flex items-start gap-2">
      <button
        type="button"
        {...sortable.attributes}
        {...sortable.listeners}
        title="Sürükleyerek sırala veya başka bölüme taşı"
        className="flex-shrink-0 mt-0.5 p-0.5 rounded text-slate-400 hover:text-indigo-600 dark:text-white/35 dark:hover:text-indigo-300 cursor-grab active:cursor-grabbing"
      >
        <GripVertical size={13} strokeWidth={2} />
      </button>
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
  // Sifirla — yuklenen (kaydedilmis) hale donus icin anlik goruntu
  var [initialFields, setInitialFields] = useState([])
  // Arama kutusu (Sutun Ayarlari paneliyle ayni etkilesim dili)
  var [search, setSearch] = useState('')
  // Katlanir satirlar: acik olanlarin key seti. Varsayilan KAPALI — liste kisa
  // kalsin, detay istenince acilsin.
  var [expanded, setExpanded] = useState({})

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

  function patchField(key, patch) {
    setFields(function (prev) {
      return prev.map(function (f) { return f.key === key ? Object.assign({}, f, patch) : f })
    })
  }

  function moveTab(idx, dir) {
    setTabs(function (prev) {
      var next = prev.slice()
      var to = idx + dir
      if (to < 0 || to >= next.length) return prev
      var tmp = next[idx]; next[idx] = next[to]; next[to] = tmp
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

  /** Droppable id <-> cardSection donusumu ("sec:0" | "sec:3" | "sec:null") */
  function sectionFromDroppableId(id) {
    var m = /^sec:(.+)$/.exec(String(id))
    if (!m) return undefined
    return m[1] === 'null' ? null : parseInt(m[1], 10)
  }

  /**
   * Alani hedef bolume (ve o bolumde hedef indekse) tasir. Hem kaynak hem hedef
   * grup 0..n-1 olacak sekilde yeniden numaralandirilir — bosluksuz, kararli
   * (bosluksuz, kararli yeniden numaralandirma).
   */
  function moveFieldTo(key, targetSection, targetIndex) {
    setFields(function (prev) {
      var moving = prev.find(function (f) { return f.key === key })
      if (!moving) return prev
      var sourceSection = moving.cardSection
      // Hedef gruptaki mevcut sira (tasinan haric)
      var target = prev.filter(function (f) {
        return f.cardSection === targetSection && f.key !== key
      }).slice().sort(compareByCardOrder)
      var idx = (targetIndex == null || targetIndex < 0 || targetIndex > target.length)
        ? target.length : targetIndex
      target.splice(idx, 0, moving)
      var orderInTarget = {}
      target.forEach(function (f, i) { orderInTarget[f.key] = i })
      // Kaynak grup (bolum degistiyse) yeniden numaralandirilir
      var orderInSource = {}
      if (sourceSection !== targetSection) {
        prev.filter(function (f) { return f.cardSection === sourceSection && f.key !== key })
            .slice().sort(compareByCardOrder)
            .forEach(function (f, i) { orderInSource[f.key] = i })
      }
      return prev.map(function (f) {
        if (f.key === key) {
          return Object.assign({}, f, { cardSection: targetSection, cardOrder: orderInTarget[f.key] })
        }
        if (orderInTarget[f.key] != null) return Object.assign({}, f, { cardOrder: orderInTarget[f.key] })
        if (orderInSource[f.key] != null) return Object.assign({}, f, { cardOrder: orderInSource[f.key] })
        return f
      })
    })
  }

  function handleDragEnd(event) {
    var active = event.active, over = event.over
    if (!active || !over || active.id === over.id) return
    var key = String(active.id)
    var overId = String(over.id)

    // Bos bir bolume birakildi (droppable kabin kendisi)
    var secFromZone = sectionFromDroppableId(overId)
    if (secFromZone !== undefined) { moveFieldTo(key, secFromZone, null); return }

    // Baska bir alanin uzerine birakildi → o alanin bolumu + sirasi
    var overField = fields.find(function (f) { return f.key === overId })
    if (!overField) return
    var groupFields = fields.filter(function (f) {
      return f.cardSection === overField.cardSection && f.key !== key
    }).slice().sort(compareByCardOrder)
    var at = groupFields.findIndex(function (f) { return f.key === overId })
    moveFieldTo(key, overField.cardSection, at < 0 ? null : at)
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
          }
        }),
        tabs: tabs.map(function (t, i) {
          return { key: t.key, isVisible: t.isVisible, sortOrder: i, labelText: t.labelText.trim() || null }
        }),
        // Serit satir yukseklikleri — yalniz DEGER VERILMIS seritler gonderilir;
        // gonderilmeyen serit sunucuda satir acmaz (varsayilan, fail-open).
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

  var scopeKeys = fields.map(function (f) { return f.key }).join(', ')
  var showTabBadge = tabs.length > 0
  var sectionGroups = buildSectionGroups(fields, maxStrip)
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
              Görünürlük, zorunluluk, varsayılan değer, başlık, koşullu kurallar ve kart Bölümü bu formun tüm kullanıcıları için geçerlidir.
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
              {/* ── Sekmeler (yalnız üst bilgi formlarında — kalem formunda boş gelir) ── */}
              {tabs.length > 0 && (
              <div className="mb-4">
                <div className="text-[11.5px] font-bold text-slate-700 dark:text-white/80 mb-1.5">Sekmeler</div>
                <div className="flex flex-col gap-1">
                  {tabs.map(function (t, idx) {
                    return (
                      <div key={t.key} className="flex items-center gap-2 rounded-lg border border-slate-200 bg-slate-50/60 dark:border-white/10 dark:bg-white/[0.03] px-2.5 py-1.5">
                        <div className="flex flex-col gap-0.5">
                          <button type="button" onClick={function () { moveTab(idx, -1) }} disabled={idx === 0}
                            className="text-slate-500 hover:text-indigo-600 dark:text-white/55 dark:hover:text-indigo-300 disabled:opacity-25">
                            <ArrowUp size={11} strokeWidth={2.2} />
                          </button>
                          <button type="button" onClick={function () { moveTab(idx, 1) }} disabled={idx === tabs.length - 1}
                            className="text-slate-500 hover:text-indigo-600 dark:text-white/55 dark:hover:text-indigo-300 disabled:opacity-25">
                            <ArrowDown size={11} strokeWidth={2.2} />
                          </button>
                        </div>
                        <span className="text-[11.5px] font-semibold text-slate-600 dark:text-white/70 min-w-[110px]">{t.label}</span>
                        <input
                          type="text"
                          value={t.labelText}
                          maxLength={40}
                          placeholder={t.label + ' (varsayılan ad)'}
                          onChange={function (e) {
                            var v = e.target.value
                            setTabs(function (prev) { return prev.map(function (x) { return x.key === t.key ? Object.assign({}, x, { labelText: v }) : x }) })
                          }}
                          className={inputCls + ' max-w-[220px]'}
                        />
                        <div className="flex-1" />
                        {t.locked ? (
                          <span title="Bu sekme gizlenemez"><Lock size={12} className="text-slate-400 dark:text-white/45" /></span>
                        ) : (
                          <button
                            type="button"
                            onClick={function () {
                              setTabs(function (prev) { return prev.map(function (x) { return x.key === t.key ? Object.assign({}, x, { isVisible: !x.isVisible }) : x }) })
                            }}
                            title={t.isVisible ? 'Sekmeyi gizle' : 'Sekmeyi göster'}
                            className="text-slate-500 hover:text-indigo-600 dark:text-white/55 dark:hover:text-indigo-300"
                          >
                            {t.isVisible ? <Eye size={13} strokeWidth={2} /> : <EyeOff size={13} strokeWidth={2} className="text-rose-500 dark:text-rose-300" />}
                          </button>
                        )}
                      </div>
                    )
                  })}
                </div>
              </div>
              )}

              {/* ── Varsayılan Hücre Genişliği (2026-08-20) — form genelinde 12 sütunluk
                  ortak ızgarada, genişliği ayarlanmamış alanların kullanacağı değer. ── */}
              <div className="mb-4 rounded-lg border border-slate-200 bg-slate-50/60 dark:border-white/10 dark:bg-white/[0.03] px-3 py-2.5">
                <div className="flex items-center gap-2 flex-wrap">
                  <LayoutGrid size={13} className="text-slate-500 dark:text-white/55 flex-shrink-0" />
                  <span className="text-[11.5px] font-bold text-slate-700 dark:text-white/80">Varsayılan Hücre Genişliği</span>
                  <div className="flex-1" />
                  <WidthStepper value={defaultCardWidth} fallback={3} allowClear={false}
                    onChange={function (v) { setDefaultCardWidth(v) }} />
                </div>
                <div className="text-[10px] text-slate-500 dark:text-white/50 mt-1">
                  Genişliği ayarlanmamış alanlar bu değeri kullanır (12 sütunluk şerit ızgarası).
                </div>
              </div>

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
              <DndContext sensors={dndSensors} collisionDetection={closestCenter} onDragEnd={handleDragEnd}>
              {sectionGroups.map(function (group) {
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
                    <SectionDropZone section={group.section}>
                    <SortableContext items={visibleFields.map(function (f) { return f.key })}
                                     strategy={verticalListSortingStrategy}>
                    <div className="flex flex-col gap-1.5 min-h-[34px]">
                      {visibleFields.length === 0 && (
                        <div className="text-[10.5px] text-slate-400 dark:text-white/30 px-2.5 py-2 border border-dashed border-slate-200 dark:border-white/10 rounded-lg">
                          Boş — alan sürükleyip bırakabilirsiniz
                        </div>
                      )}
                      {visibleFields.map(function (f) {
                        var isOpen = expanded[f.key] === true
                        return (
                          <SortableRow key={f.key} id={f.key}>
                            {/* Satır 1: alan adı + bağlam rozeti + Görünür/Zorunlu + Bölüm */}
                            <div className="flex items-center gap-2 flex-wrap">
                              <div className="flex items-center gap-1.5 min-w-0">
                                {f.locked && <span title="Çekirdek alan — gizlenemez"><Lock size={11} className="text-slate-400 dark:text-white/45 flex-shrink-0" /></span>}
                                <span className="truncate text-[11.5px] font-semibold text-slate-600 dark:text-white/70" title={f.key}>{f.label}</span>
                                {showTabBadge && tabLabels[f.tab] && (
                                  <span className="px-1.5 py-0.5 rounded text-[9px] font-semibold bg-slate-100 text-slate-500 border border-slate-200 dark:bg-white/[0.05] dark:text-white/50 dark:border-white/10 flex-shrink-0">
                                    {tabLabels[f.tab]}
                                  </span>
                                )}
                              </div>
                              <div className="flex-1" />
                              <div className="flex items-center gap-1.5">
                                <span className="text-[9.5px] font-semibold text-slate-500 dark:text-white/50">Görünür</span>
                                <Switch
                                  on={f.isVisible}
                                  disabled={f.locked}
                                  color="bg-emerald-500/70"
                                  title={f.locked ? 'Çekirdek alan — gizlenemez' : 'Görünür'}
                                  onToggle={function () { patchField(f.key, { isVisible: !f.isVisible }) }}
                                />
                              </div>
                              <div className="flex items-center gap-1.5">
                                <span className="text-[9.5px] font-semibold text-slate-500 dark:text-white/50">Zorunlu</span>
                                <Switch
                                  on={f.isRequired}
                                  color="bg-red-500/70"
                                  title="Zorunlu — boş bırakılırsa kayıt engellenir"
                                  onToggle={function () { patchField(f.key, { isRequired: !f.isRequired }) }}
                                />
                              </div>
                              <div className="flex items-center gap-1.5">
                                <span className="text-[9.5px] font-semibold text-slate-500 dark:text-white/50">Genişlik</span>
                                <WidthStepper
                                  value={f.cardWidth}
                                  fallback={defaultCardWidth}
                                  allowClear={true}
                                  onChange={function (v) { patchField(f.key, { cardWidth: v }) }}
                                />
                              </div>
                              {/* Bolum artik SURUKLEYEREK degistirilir — buton grubu kalkti.
                                  Yerine detaylari acan katlama dugmesi. */}
                              <button
                                type="button"
                                onClick={function () {
                                  setExpanded(function (prev) {
                                    var next = Object.assign({}, prev)
                                    if (next[f.key]) delete next[f.key]; else next[f.key] = true
                                    return next
                                  })
                                }}
                                title={isOpen ? 'Detayları gizle' : 'Başlık, stil ve davranış ayarları'}
                                className="flex items-center gap-1 px-1.5 py-0.5 rounded text-[9.5px] font-semibold text-slate-500 hover:text-indigo-600 dark:text-white/50 dark:hover:text-indigo-300"
                              >
                                {isOpen ? <ChevronDown size={12} strokeWidth={2.2} /> : <ChevronRight size={12} strokeWidth={2.2} />}
                                Detay
                              </button>
                            </div>

                            {/* Satır 2: detaylar — yalnizca satir ACIKKEN. Kapaliyken
                                liste kisa kalir (Sutun Ayarlari panelindeki desen). */}
                            {isOpen && (
                            <div className="grid grid-cols-1 lg:grid-cols-[1fr_100px_1.2fr] gap-2 items-center">
                              <input
                                type="text"
                                value={f.labelText}
                                maxLength={60}
                                placeholder={f.label}
                                onChange={function (e) { patchField(f.key, { labelText: e.target.value }) }}
                                className={inputCls}
                              />
                              <select
                                value={f.labelStyle}
                                onChange={function (e) { patchField(f.key, { labelStyle: e.target.value }) }}
                                className={inputCls}
                              >
                                <option value="">Standart</option>
                                <option value="modern">Modern</option>
                                <option value="inline">Sade</option>
                              </select>
                              {/* 2026-08-20 (kullanici istegi): Varsayilan Deger / Gorunurluk
                                  Kosulu / Zorunluluk Kosulu artik satir icinde serbest metin
                                  DEGIL — tek butonla acilan modalden tanimlanir. Kosullar
                                  NCalc ifadesi oldugu icin kurucu + gelismis (ham) mod sunar. */}
                              <button
                                type="button"
                                onClick={function () { setBehaviorKey(f.key) }}
                                title="Varsayılan değer, görünürlük ve zorunluluk koşulunu tanımla"
                                className={'flex items-center gap-1.5 px-2.5 py-1 rounded-md text-[11px] font-semibold border transition-colors ' + (
                                  (f.defaultValue || f.visibleIf || f.requiredIf)
                                    ? 'text-indigo-600 border-indigo-200 bg-indigo-50 hover:bg-indigo-100 dark:text-indigo-300 dark:border-indigo-400/30 dark:bg-indigo-500/10 dark:hover:bg-indigo-500/20'
                                    : 'text-slate-500 border-slate-200 bg-[#fff] hover:bg-slate-50 dark:text-white/55 dark:border-white/10 dark:bg-white/[0.04] dark:hover:bg-white/[0.08]'
                                )}
                              >
                                <Settings2 size={12} strokeWidth={2.2} className="flex-shrink-0" />
                                <span className="truncate">{describeBehavior(f)}</span>
                              </button>
                            </div>
                            )}
                          </SortableRow>
                        )
                      })}
                    </div>
                    </SortableContext>
                    </SectionDropZone>
                  </div>
                )
              })}
              </DndContext>

              <div className="text-[10.5px] text-slate-500 dark:text-white/55">
                Koşul ifadelerinde kullanılabilir alanlar: <span className="font-mono">{scopeKeys}</span>.
                Örnekler: <span className="font-mono">currency != 'TRY'</span> · <span className="font-mono">vatIncluded == true</span> · <span className="font-mono">discountRate &gt; 0</span>
              </div>
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
