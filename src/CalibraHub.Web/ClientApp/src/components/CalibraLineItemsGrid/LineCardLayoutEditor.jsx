/**
 * LineCardLayoutEditor — belge kalem KARTI duzen editoru (2026-08-05).
 *
 * Form bazli ORTAK duzen (admin tasarlar, herkes ayni karti gorur):
 *   - Surukle-birak ile alan SIRALAMA (HTML5 drag — ek bagimlilik yok)
 *   - Sag kenardan cekerek GENISLIK (24 kolonlu izgara span'i, WidgetMas.ColSpan
 *     standardiyla ayni olcek)
 *   - Goz toggle ile GORUNURLUK (zorunlu/miktar alanlari kilitli — gizlenemez;
 *     CLAUDE.md sessiz-kirik kurali 3: veri girisi sessizce kaybolmasin)
 *
 * Kalicilik: POST /api/line-card-layout/save (admin-only, CSRF). Sifirla →
 * POST /api/line-card-layout/reset — grid varsayilan auto-fill izgarasina doner.
 * Kaydedilen duzen additive-safe'tir: duzende olmayan yeni kolonlar runtime'da
 * varsayilan genislikle sona eklenir (CalibraLineItemsGrid.cardItems).
 */
import { useState, useRef, useEffect } from 'react'
import { createPortal } from 'react-dom'
import {
  LayoutGrid, GripVertical, Eye, EyeOff, X as XIcon, RotateCcw, AlertTriangle,
  Hash, FileText, Ruler, Sigma, DollarSign, Percent, Calculator, StickyNote,
  CircleDot, Tag, Barcode, Warehouse,
} from 'lucide-react'
import { getTopBody } from '../../utils/topPortal'

// CalibraLineItemsGrid.ICON_MAP ile ayni eslesme — onizleme kart etiketiyle
// birebir ayni ikonu gosterir ("kalemde nasil gorunuyorsa oyle" ilkesi).
var ICON_MAP = {
  Hash: Hash, FileText: FileText, Ruler: Ruler, Sigma: Sigma,
  DollarSign: DollarSign, Percent: Percent, Calculator: Calculator,
  StickyNote: StickyNote, Tag: Tag, Barcode: Barcode, Warehouse: Warehouse,
}
function resolveIcon(name) { return ICON_MAP[name] || CircleDot }

/* Kart etiketi renk token'i → Tailwind sinifi — CalibraLineItemsGrid.
   CARD_LABEL_COLOR_CLS ile birebir ayni (onizleme = gercek kart). */
var LABEL_COLOR_CLS = {
  slate:   'text-slate-500 dark:text-white/45',
  indigo:  'text-indigo-600 dark:text-indigo-300',
  emerald: 'text-emerald-600 dark:text-emerald-300',
  amber:   'text-amber-600 dark:text-amber-300',
  rose:    'text-rose-600 dark:text-rose-300',
  blue:    'text-blue-600 dark:text-blue-300',
  violet:  'text-violet-600 dark:text-violet-300',
}
var LABEL_COLOR_TOKENS = ['slate', 'indigo', 'emerald', 'amber', 'rose', 'blue', 'violet']
/* Renk secici yuvarlaklarinin dolgusu — semantik token'in gorsel karsiligi. */
var LABEL_COLOR_SWATCH_CLS = {
  slate: 'bg-slate-400', indigo: 'bg-indigo-500', emerald: 'bg-emerald-500',
  amber: 'bg-amber-500', rose: 'bg-rose-500', blue: 'bg-blue-500', violet: 'bg-violet-500',
}

/* Izgara cozunurlugu — CalibraLineItemsGrid.CARD_GRID_UNITS + server GridUnits ile
   ayni (48; v1'de 24'tu, eski kayitlari server okuma yolunda olcekler). */
var GRID_UNITS = 48
var MIN_SPAN = 3

/* ── Sutun genisligi altyapisi (2026-08-06) ────────────────────────────────
   Ham 48'lik birim kullaniciya "18/48" olarak degil KESIR olarak gosterilir
   (1/4, 1/3, 1/2 …) — premium form tasarimcilarinin dili. Birim hassasiyeti
   korunur: preset disi degerler "n/48" olarak gosterilir ve +/- ile ayarlanir. */
var WIDTH_PRESETS = [
  { span: 8,  label: '1/6' },
  { span: 12, label: '1/4' },
  { span: 16, label: '1/3' },
  { span: 24, label: '1/2' },
  { span: 32, label: '2/3' },
  { span: 36, label: '3/4' },
  { span: 48, label: 'Tam' },
]
function spanLabel(span) {
  for (var i = 0; i < WIDTH_PRESETS.length; i++) {
    if (WIDTH_PRESETS[i].span === span) return WIDTH_PRESETS[i].label
  }
  return span + '/' + GRID_UNITS
}
function clampSpan(span) {
  return Math.min(GRID_UNITS, Math.max(MIN_SPAN, span))
}
/* Surukleme sirasinda yaygin kesirlere yapisma (1 birim tolerans) — serbest
   suruklemede 23/48 gibi "neredeyse yarim" degerler olusmasin. */
function snapSpan(span) {
  for (var i = 0; i < WIDTH_PRESETS.length; i++) {
    if (Math.abs(WIDTH_PRESETS[i].span - span) <= 1) return WIDTH_PRESETS[i].span
  }
  return span
}
/* CSS grid'in satir sarma davranisinin aynisi: alan mevcut satira sigmiyorsa
   yeni satira akar (kalan bosluk bos kalir). Editordeki satir raylari ve
   "satiri doldur / esit dagit" araclari bu paketlemeden beslenir. */
function packRows(entries) {
  var rows = []
  var cur = []
  var used = 0
  for (var i = 0; i < entries.length; i++) {
    var s = clampSpan(entries[i].it.span)
    if (cur.length && used + s > GRID_UNITS) {
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

function readCsrfToken() {
  try {
    var input = document.querySelector('input[name="__RequestVerificationToken"]')
    if (input && input.value) return input.value
    var shellCfg = window.__CALIBRA_SHELL_CONFIG__
    if (shellCfg && shellCfg.antiforgeryToken) return shellCfg.antiforgeryToken
    return ''
  } catch (e) {
    return ''
  }
}

function normalizeEditorItems(arr) {
  return (arr || []).map(function (it) {
    return {
      key: it.key,
      label: it.label || it.key,
      icon: it.icon || null,
      span: (typeof it.span === 'number' && it.span >= 1 && it.span <= GRID_UNITS) ? it.span : 12,
      visible: it.visible !== false,
      locked: it.locked === true,
      isWidget: it.isWidget === true,
      // Baslik override'lari — bos/null = varsayilan
      labelText: typeof it.labelText === 'string' ? it.labelText : '',
      labelSize: it.labelSize || null,
      labelWeight: it.labelWeight || null,
      labelColor: LABEL_COLOR_CLS[it.labelColor] ? it.labelColor : null,
      // Alan Yonetimi sozlugu: null=Standart, 'modern'=Modern, 'inline'=Sade
      labelStyle: (it.labelStyle === 'modern' || it.labelStyle === 'inline') ? it.labelStyle : null,
    }
  })
}

export default function LineCardLayoutEditor(props) {
  var formCode = props.formCode
  var onClose = props.onClose
  var onSaved = props.onSaved
  var onReset = props.onReset
  // autoLoad: items prop'suz kullanim (Alan Yönetimi) — katalog + mevcut duzen
  // /api/line-card-layout/{formCode}/fields endpoint'inden cekilir.
  var autoLoad = props.autoLoad === true

  // Calisma kopyasi — Kaydet'e basilana kadar grid'e dokunulmaz.
  var [items, setItems] = useState(function () {
    return autoLoad ? [] : normalizeEditorItems(props.items)
  })
  var [hasCustomLayout, setHasCustomLayout] = useState(props.hasCustomLayout === true)
  var [loading, setLoading] = useState(autoLoad)
  var [saving, setSaving] = useState(false)
  var [error, setError] = useState(null)
  var [confirmReset, setConfirmReset] = useState(false)

  useEffect(function () {
    if (!autoLoad) return undefined
    var alive = true
    fetch('/api/line-card-layout/' + encodeURIComponent(formCode) + '/fields', { credentials: 'same-origin' })
      .then(function (r) { return r.ok ? r.json() : null })
      .then(function (data) {
        if (!alive) return
        if (!data || data.ok !== true) {
          setError((data && data.error) || 'Alan kataloğu yüklenemedi.')
          return
        }
        setItems(normalizeEditorItems(data.items))
        setHasCustomLayout(data.hasCustomLayout === true)
      })
      .catch(function (e) { if (alive) setError('Hata: ' + (e && e.message ? e.message : String(e))) })
      .then(function () { if (alive) setLoading(false) })
    return function () { alive = false }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [autoLoad, formCode])
  // Secili alan (baslik metni/stili paneli icin) — key ile izlenir ki
  // surukle-birak sonrasi secim kaybolmasin.
  var [selectedKey, setSelectedKey] = useState(null)

  function patchItem(key, patch) {
    setItems(function (prev) {
      return prev.map(function (it) {
        return it.key === key ? Object.assign({}, it, patch) : it
      })
    })
  }

  // ── Sutun genisligi islemleri ──
  function setSpan(key, span) {
    patchItem(key, { span: clampSpan(span) })
  }
  /* Satirdaki alanlari 48 birime esit boler; artan birim (48 % n) soldan
     dagitilir. Alan sayisi cok fazlaysa (MIN_SPAN'in altina duserdi) islem
     yapilmaz — sessizce bozuk duzen uretmektense hicbir sey yapmak dogru. */
  function distributeRow(rowEntries) {
    var n = rowEntries.length
    if (!n) return
    var base = Math.floor(GRID_UNITS / n)
    if (base < MIN_SPAN) return
    var extra = GRID_UNITS - base * n
    var byKey = {}
    rowEntries.forEach(function (en, i) { byKey[en.it.key] = base + (i < extra ? 1 : 0) })
    setItems(function (prev) {
      return prev.map(function (it) {
        return byKey[it.key] ? Object.assign({}, it, { span: byKey[it.key] }) : it
      })
    })
  }
  /* Satirdaki bos birimleri SON alana ekler — "satir sonu boslugu" kapatmanin
     en sik ihtiyaci. Zaten doluysa no-op. */
  function fillRow(rowEntries) {
    if (!rowEntries.length) return
    var used = rowEntries.reduce(function (a, en) { return a + clampSpan(en.it.span) }, 0)
    var free = GRID_UNITS - used
    if (free <= 0) return
    var last = rowEntries[rowEntries.length - 1].it
    setSpan(last.key, clampSpan(last.span + free))
  }
  function moveItem(idx, dir) {
    setItems(function (prev) {
      var to = idx + dir
      if (to < 0 || to >= prev.length) return prev
      var next = prev.slice()
      var moved = next.splice(idx, 1)[0]
      next.splice(to, 0, moved)
      return next
    })
  }

  // ── Surukle-birak siralama ──
  var dragIndexRef = useRef(null)
  var [dragOverIndex, setDragOverIndex] = useState(null)

  function handleDragStart(e, idx) {
    dragIndexRef.current = idx
    try { e.dataTransfer.effectAllowed = 'move'; e.dataTransfer.setData('text/plain', String(idx)) } catch (_) {}
  }
  function handleDragOver(e, idx) {
    e.preventDefault()
    var from = dragIndexRef.current
    if (from == null || from === idx) return
    setDragOverIndex(idx)
    setItems(function (prev) {
      var next = prev.slice()
      var moved = next.splice(from, 1)[0]
      next.splice(idx, 0, moved)
      return next
    })
    dragIndexRef.current = idx
  }
  function handleDragEnd() {
    dragIndexRef.current = null
    setDragOverIndex(null)
  }

  // ── Sag kenardan genislik (span) cekme ──
  //   Pointer Events: mouse + touch + pen tek API (WidgetBuilderForm colSpan
  //   slider'i ile ayni yaklasim). Izgara hucre genisligi konteyner/24'ten olculur.
  var gridRef = useRef(null)
  var resizeRef = useRef(null) // { key, startX, startSpan, cellWidth }
  // Suren islem: izgara kilavuz cizgileri + canli genislik rozeti icin.
  var [resizingKey, setResizingKey] = useState(null)

  function handleResizeStart(e, idx) {
    if (!gridRef.current) return
    var rect = gridRef.current.getBoundingClientRect()
    resizeRef.current = {
      key: items[idx].key,
      startX: e.clientX,
      startSpan: items[idx].span,
      // Olcek satir izgarasindan degil KART genisliginden alinir: her satir
      // ayri grid oldugu icin (satir raylari) hucre genisligi ortak olmali.
      cellWidth: rect.width / GRID_UNITS,
    }
    setResizingKey(items[idx].key)
    try { e.currentTarget.setPointerCapture(e.pointerId) } catch (_) {}
    e.preventDefault()
  }
  function handleResizeMove(e) {
    var st = resizeRef.current
    if (!st || !e.currentTarget.hasPointerCapture || !e.currentTarget.hasPointerCapture(e.pointerId)) return
    e.preventDefault()
    var deltaSpan = Math.round((e.clientX - st.startX) / Math.max(st.cellWidth, 4))
    // Shift = serbest (yapismasiz) hassas surukleme.
    var raw = clampSpan(st.startSpan + deltaSpan)
    var nextSpan = e.shiftKey ? raw : clampSpan(snapSpan(raw))
    setItems(function (prev) {
      var i = prev.findIndex(function (x) { return x.key === st.key })
      if (i < 0 || prev[i].span === nextSpan) return prev
      var next = prev.slice()
      next[i] = Object.assign({}, next[i], { span: nextSpan })
      return next
    })
  }
  function handleResizeEnd(e) {
    try {
      if (e.currentTarget.hasPointerCapture(e.pointerId)) e.currentTarget.releasePointerCapture(e.pointerId)
    } catch (_) {}
    resizeRef.current = null
    setResizingKey(null)
  }

  /* Klavye ile hassas ayar (secili alan): Alt+←/→ bir birim, Alt+Shift+←/→
     onceki/sonraki hazir genislik, Ctrl+←/→ sira degistir. Fare hassasiyeti
     yetmedigi durumlarda profesyonel tasarimcilarin bekledigi kontrol. */
  function handleModalKeyDown(e) {
    if (e.key === 'Escape') { if (!saving) onClose(); return }
    if (!selectedKey) return
    var idx = items.findIndex(function (x) { return x.key === selectedKey })
    if (idx < 0) return
    var it = items[idx]
    var isLeft = e.key === 'ArrowLeft'
    var isRight = e.key === 'ArrowRight'
    if (!isLeft && !isRight) return
    if (e.altKey) {
      e.preventDefault()
      if (e.shiftKey) {
        // Hazir genislikler arasinda atla
        var spans = WIDTH_PRESETS.map(function (p) { return p.span })
        var target = null
        if (isRight) {
          for (var i = 0; i < spans.length; i++) { if (spans[i] > it.span) { target = spans[i]; break } }
        } else {
          for (var j = spans.length - 1; j >= 0; j--) { if (spans[j] < it.span) { target = spans[j]; break } }
        }
        if (target != null) setSpan(it.key, target)
      } else {
        setSpan(it.key, it.span + (isRight ? 1 : -1))
      }
    } else if (e.ctrlKey || e.metaKey) {
      e.preventDefault()
      moveItem(idx, isRight ? 1 : -1)
    }
  }

  function toggleVisible(idx) {
    setItems(function (prev) {
      var next = prev.slice()
      if (next[idx].locked) return prev
      next[idx] = Object.assign({}, next[idx], { visible: !next[idx].visible })
      return next
    })
  }

  async function handleSave() {
    if (saving) return
    setSaving(true)
    setError(null)
    try {
      var payload = {
        formCode: formCode,
        items: items.map(function (it, i) {
          return {
            key: it.key, span: it.span, order: i, visible: it.visible,
            label: (it.labelText && it.labelText.trim()) ? it.labelText.trim() : null,
            labelSize: it.labelSize || null,
            labelWeight: it.labelWeight || null,
            labelColor: it.labelColor || null,
            labelStyle: it.labelStyle || null,
          }
        }),
      }
      var resp = await fetch('/api/line-card-layout/save', {
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
      if (typeof onSaved === 'function') onSaved(data.items || payload.items)
    } catch (e) {
      setError('Hata: ' + (e && e.message ? e.message : String(e)))
    } finally {
      setSaving(false)
    }
  }

  async function handleReset() {
    if (saving) return
    setSaving(true)
    setError(null)
    try {
      var resp = await fetch('/api/line-card-layout/reset', {
        method: 'POST',
        credentials: 'same-origin',
        headers: {
          'Content-Type': 'application/json',
          'Accept': 'application/json',
          'RequestVerificationToken': readCsrfToken(),
        },
        body: JSON.stringify({ formCode: formCode }),
      })
      var data = null
      try { data = await resp.json() } catch (_) {}
      if (!resp.ok || !data || data.ok !== true) {
        setError((data && data.error) || ('Sıfırlanamadı (HTTP ' + resp.status + ')'))
        return
      }
      if (typeof onReset === 'function') onReset()
    } catch (e) {
      setError('Hata: ' + (e && e.message ? e.message : String(e)))
    } finally {
      setSaving(false)
      setConfirmReset(false)
    }
  }

  return createPortal(
    <div
      onClick={function (e) { if (e.target === e.currentTarget && !saving) onClose() }}
      onKeyDown={function (e) { if (e.key === 'Escape' && !saving) onClose() }}
      className="fixed inset-0 z-[60] flex items-center justify-center p-4"
      style={{ background: 'rgba(15,23,42,0.45)', backdropFilter: 'blur(5px)', WebkitBackdropFilter: 'blur(5px)' }}
    >
      <div
        className="w-full max-w-[860px] max-h-[88vh] flex flex-col overflow-hidden rounded-2xl border border-slate-200 bg-[#fff] shadow-2xl dark:border-white/10 dark:bg-slate-900"
        role="dialog"
        aria-label="Kart Düzeni"
      >
        {/* Header */}
        <div className="flex items-center gap-3 px-5 py-4 border-b border-slate-200 dark:border-white/[0.08] flex-shrink-0">
          <div className="w-9 h-9 rounded-xl flex items-center justify-center bg-indigo-50 border border-indigo-200 text-indigo-600 dark:bg-indigo-500/15 dark:border-indigo-400/30 dark:text-indigo-300">
            <LayoutGrid size={17} strokeWidth={1.9} />
          </div>
          <div className="flex-1 min-w-0">
            <div className="text-[14px] font-bold text-slate-800 dark:text-white/90">Kart Düzeni</div>
            <div className="text-[11px] text-slate-500 dark:text-white/45">
              Alanları sürükleyerek sıralayın, sağ kenardan çekerek genişletin. Düzen bu belge türünün tüm kullanıcıları için geçerlidir.
            </div>
          </div>
          <button
            type="button"
            onClick={function () { if (!saving) onClose() }}
            className="w-8 h-8 rounded-lg flex items-center justify-center text-slate-400 hover:text-rose-600 hover:bg-rose-50 dark:text-white/40 dark:hover:text-rose-300 dark:hover:bg-rose-500/10 transition-colors"
            title="Kapat (Esc)"
          >
            <XIcon size={15} strokeWidth={2} />
          </button>
        </div>

        {/* Body — 24 kolonlu onizleme izgarasi. Dis kabuk gercek kalem karti
            gorunumundedir (ayni border/arka plan) — WYSIWYG onizleme. */}
        <div className="flex-1 min-h-0 overflow-y-auto px-5 py-4">
          {loading && (
            <div className="py-10 text-center text-[12px] text-slate-400 dark:text-white/35">
              Alanlar yükleniyor…
            </div>
          )}
          {!loading && (function () {
            // Tuval yalniz KARTTA GORUNEN alanlari cizer (gizliler asagidaki
            // tepsiye duser) — boylece satir paketleme ve doluluk olculeri
            // gercek kartla birebir ayni olur.
            var entries = items.map(function (it, i) { return { it: it, idx: i } })
            var visibleEntries = entries.filter(function (en) { return en.it.visible })
            var hiddenEntries = entries.filter(function (en) { return !en.it.visible })
            var rows = packRows(visibleEntries)
            var guidesOn = resizingKey != null || dragOverIndex != null
            // Kilavuz cizgileri: 1/12'lik adimlar — yalniz surukleme/boyutlandirma
            // sirasinda gorunur, dinlenme halinde onizlemeyi kirletmez.
            var guideStyle = guidesOn ? {
              backgroundImage: 'repeating-linear-gradient(to right, rgba(99,102,241,0.16) 0 1px, transparent 1px calc(100% / 12))',
            } : null

            function renderItem(en) {
              var it = en.it
              var idx = en.idx
              var hiddenCls = !it.visible ? ' opacity-40' : ''
              var dragCls = dragOverIndex === idx ? ' ring-2 ring-indigo-400/70' : ''
              var selectedCls = selectedKey === it.key ? ' border-indigo-400/80 bg-indigo-50/50 dark:bg-indigo-500/[0.08]' : ' border-transparent'
              var Icon = resolveIcon(it.icon)
              // Onizleme etiketinde override'lar CANLI uygulanir — kartla birebir.
              var pvText = (it.labelText && it.labelText.trim()) ? it.labelText.trim() : it.label
              var pvColorCls = it.labelColor ? LABEL_COLOR_CLS[it.labelColor] : 'text-slate-500 dark:text-white/45'
              var pvMode = (it.labelStyle === 'modern' || it.labelStyle === 'inline') ? it.labelStyle : 'standard'
              var pvStyle = {}
              if (it.labelSize) pvStyle.fontSize = it.labelSize
              if (it.labelWeight) pvStyle.fontWeight = it.labelWeight
              var pvLabelInner = (
                <>
                  <Icon size={10} strokeWidth={1.8} className="text-slate-400 dark:text-white/35 flex-shrink-0" />
                  <span className="truncate">{pvText}</span>
                  {it.locked && <span className="text-rose-500 dark:text-rose-400">*</span>}
                </>
              )
              return (
                <div
                  key={it.key}
                  draggable
                  onDragStart={function (e) { handleDragStart(e, idx) }}
                  onDragOver={function (e) { handleDragOver(e, idx) }}
                  onDragEnd={handleDragEnd}
                  onClick={function () { setSelectedKey(selectedKey === it.key ? null : it.key) }}
                  tabIndex={0}
                  style={{ gridColumn: 'span ' + it.span, position: 'relative' }}
                  className={'group select-none rounded-lg px-1.5 pt-1 pb-1.5 cursor-grab active:cursor-grabbing border outline-none hover:border-indigo-200/70 dark:hover:border-indigo-400/25' + hiddenCls + dragCls + selectedCls}
                >
                  {/* Kontrol satiri — tut/genislik/goz. Kart gorunumunu bozmasin
                      diye kucuk; blok hover'inda belirginlesir. */}
                  <div className="flex items-center gap-1 mb-0.5 opacity-50 group-hover:opacity-100 transition-opacity">
                    <GripVertical size={11} className="text-slate-400 dark:text-white/35 flex-shrink-0" />
                    {it.isWidget && (
                      <span className="text-[8.5px] font-bold px-1 rounded bg-sky-100 text-sky-600 dark:bg-sky-500/15 dark:text-sky-300 flex-shrink-0" title="Özel alan (Alan Yönetimi)">EK</span>
                    )}
                    {/* Genislik rozeti — kesir dili (1/2, 1/3…); ham birim tooltip'te.
                        Boyutlandirma surerken belirginlesir (canli geri bildirim). */}
                    <span
                      title={'Genişlik: ' + it.span + '/' + GRID_UNITS + ' birim'}
                      className={'ml-auto text-[9px] font-mono tabular-nums flex-shrink-0 px-1 rounded transition-colors ' + (
                        resizingKey === it.key
                          ? 'bg-indigo-500 text-white font-bold'
                          : 'text-slate-400 dark:text-white/35'
                      )}
                    >
                      {spanLabel(it.span)}
                    </span>
                    <button
                      type="button"
                      onClick={function (e) { e.stopPropagation(); toggleVisible(idx) }}
                      disabled={it.locked}
                      title={it.locked ? 'Zorunlu alan — gizlenemez' : (it.visible ? 'Kartta gizle' : 'Kartta göster')}
                      className={'w-5 h-5 rounded flex items-center justify-center flex-shrink-0 transition-colors ' + (
                        it.locked
                          ? 'text-slate-300 dark:text-white/20 cursor-not-allowed'
                          : 'text-slate-400 hover:text-indigo-600 hover:bg-indigo-50 dark:text-white/40 dark:hover:text-indigo-300 dark:hover:bg-indigo-500/10'
                      )}
                    >
                      {it.visible ? <Eye size={11} strokeWidth={2} /> : <EyeOff size={11} strokeWidth={2} />}
                    </button>
                  </div>
                  {/* Kartla birebir ayni etiket + hucre kutusu (canli onizleme) —
                      stil moduna gore: ustte / yuzer (modern) / solda (Sade). */}
                  {pvMode === 'standard' && (
                    <>
                      <div className={'calibra-line-card-label flex items-center gap-1 text-[10px] font-bold tracking-wide mb-0.5 ' + pvColorCls} style={pvStyle}>
                        {pvLabelInner}
                      </div>
                      {/* Underline standardi (gercek karttaki gorunumun aynisi): kutu degil alt cizgi */}
                      <div className="h-[34px] border-b border-slate-200 dark:border-white/[0.12]" />
                    </>
                  )}
                  {pvMode === 'modern' && (
                    <div style={{ position: 'relative' }} className="mt-1.5">
                      <div
                        className={'calibra-line-card-label absolute flex items-center gap-1 text-[9.5px] font-bold tracking-wide ' + pvColorCls}
                        style={Object.assign({ top: -7, left: 8, zIndex: 2, lineHeight: '12px' }, pvStyle)}
                      >
                        {pvLabelInner}
                      </div>
                      <div className="h-[34px] border-b border-slate-200 dark:border-white/[0.12]" />
                    </div>
                  )}
                  {pvMode === 'inline' && (
                    <div className="flex items-center gap-2">
                      <div className={'calibra-line-card-label flex items-center gap-1 text-[10px] font-bold tracking-wide flex-shrink-0 max-w-[45%] ' + pvColorCls} style={pvStyle}>
                        {pvLabelInner}
                      </div>
                      <div className="flex-1 min-w-0 h-[34px] border-b border-slate-200 dark:border-white/[0.12]" />
                    </div>
                  )}
                  {/* Sag kenar resize tutamaci */}
                  <div
                    onPointerDown={function (e) { handleResizeStart(e, idx) }}
                    onPointerMove={handleResizeMove}
                    onPointerUp={handleResizeEnd}
                    onPointerCancel={handleResizeEnd}
                    title="Genişliği sürükleyerek ayarla"
                    style={{ touchAction: 'none' }}
                    className="absolute right-0 top-0 bottom-0 w-2 cursor-ew-resize rounded-r-lg opacity-0 group-hover:opacity-100 bg-indigo-400/40 dark:bg-indigo-400/30 transition-opacity"
                  />
                </div>
              )
            })}
          </div>
          </div>
          )}

          {/* Secili Alan paneli — baslik metni + stil override'lari (2026-08-05) */}
          {(function () {
            var sel = items.find(function (x) { return x.key === selectedKey })
            if (!sel) return null
            return (
              <div className="mt-3 rounded-xl border border-indigo-200/70 bg-indigo-50/40 dark:border-indigo-400/25 dark:bg-indigo-500/[0.06] px-4 py-3">
                <div className="flex items-center gap-2 mb-2.5">
                  <span className="text-[11.5px] font-bold text-slate-700 dark:text-white/80">Seçili Alan:</span>
                  <span className="text-[11.5px] font-semibold text-indigo-600 dark:text-indigo-300">{sel.label}</span>
                  <button
                    type="button"
                    onClick={function () { setSelectedKey(null) }}
                    className="ml-auto text-[10.5px] text-slate-400 hover:text-slate-600 dark:text-white/35 dark:hover:text-white/60"
                  >Kapat</button>
                </div>
                <div className="grid grid-cols-1 sm:grid-cols-[1fr_auto_auto_auto_auto] gap-3 items-end">
                  <div>
                    <div className="text-[10px] font-semibold text-slate-500 dark:text-white/45 mb-1">Başlık Metni</div>
                    <input
                      type="text"
                      value={sel.labelText}
                      maxLength={60}
                      placeholder={sel.label + ' (varsayılan)'}
                      onChange={function (e) { patchItem(sel.key, { labelText: e.target.value }) }}
                      className="w-full px-2.5 py-1.5 rounded-lg text-[12px] border border-slate-200 bg-[#fff] text-slate-700 placeholder:text-slate-300 focus:outline-none focus:ring-1 focus:ring-indigo-400 dark:border-white/10 dark:bg-white/[0.04] dark:text-white/85 dark:placeholder:text-white/25"
                    />
                  </div>
                  <div>
                    <div className="text-[10px] font-semibold text-slate-500 dark:text-white/45 mb-1">Başlık Stili</div>
                    <select
                      value={sel.labelStyle || ''}
                      onChange={function (e) { patchItem(sel.key, { labelStyle: e.target.value || null }) }}
                      title="Alan Yönetimi'ndeki etiket stilleriyle aynı: Standart (başlık üstte), Modern (kutunun üstünde yüzer), Sade (başlık solda)"
                      className="px-2 py-1.5 rounded-lg text-[12px] border border-slate-200 bg-[#fff] text-slate-700 focus:outline-none dark:border-white/10 dark:bg-white/[0.04] dark:text-white/85"
                    >
                      <option value="">Standart</option>
                      <option value="modern">Modern</option>
                      <option value="inline">Sade</option>
                    </select>
                  </div>
                  <div>
                    <div className="text-[10px] font-semibold text-slate-500 dark:text-white/45 mb-1">Boyut</div>
                    <select
                      value={sel.labelSize || ''}
                      onChange={function (e) { patchItem(sel.key, { labelSize: e.target.value ? parseInt(e.target.value, 10) : null }) }}
                      className="px-2 py-1.5 rounded-lg text-[12px] border border-slate-200 bg-[#fff] text-slate-700 focus:outline-none dark:border-white/10 dark:bg-white/[0.04] dark:text-white/85"
                    >
                      <option value="">Varsayılan</option>
                      {[9, 10, 11, 12, 13, 14].map(function (s) {
                        return <option key={s} value={s}>{s} px</option>
                      })}
                    </select>
                  </div>
                  <div>
                    <div className="text-[10px] font-semibold text-slate-500 dark:text-white/45 mb-1">Kalınlık</div>
                    <select
                      value={sel.labelWeight || ''}
                      onChange={function (e) { patchItem(sel.key, { labelWeight: e.target.value ? parseInt(e.target.value, 10) : null }) }}
                      className="px-2 py-1.5 rounded-lg text-[12px] border border-slate-200 bg-[#fff] text-slate-700 focus:outline-none dark:border-white/10 dark:bg-white/[0.04] dark:text-white/85"
                    >
                      <option value="">Varsayılan</option>
                      <option value="400">Normal</option>
                      <option value="500">Orta</option>
                      <option value="600">Yarı Kalın</option>
                      <option value="700">Kalın</option>
                    </select>
                  </div>
                  <div>
                    <div className="text-[10px] font-semibold text-slate-500 dark:text-white/45 mb-1">Renk</div>
                    <div className="flex items-center gap-1.5">
                      <button
                        type="button"
                        onClick={function () { patchItem(sel.key, { labelColor: null }) }}
                        title="Varsayılan renk"
                        className={'w-6 h-6 rounded-full border-2 flex items-center justify-center text-[9px] font-bold text-slate-400 dark:text-white/40 ' + (
                          !sel.labelColor ? 'border-indigo-500' : 'border-slate-200 dark:border-white/15'
                        )}
                      >—</button>
                      {LABEL_COLOR_TOKENS.map(function (tok) {
                        return (
                          <button
                            key={tok}
                            type="button"
                            onClick={function () { patchItem(sel.key, { labelColor: tok }) }}
                            title={tok}
                            className={'w-6 h-6 rounded-full border-2 ' + (
                              sel.labelColor === tok ? 'border-indigo-500' : 'border-transparent'
                            )}
                          >
                            <span className={'block w-full h-full rounded-full ' + LABEL_COLOR_SWATCH_CLS[tok]} />
                          </button>
                        )
                      })}
                    </div>
                  </div>
                </div>
              </div>
            )
          })()}

          <div className="mt-3 text-[10.5px] text-slate-400 dark:text-white/35">
            Toplam genişlik 48 birimdir; bir satıra sığmayan alanlar otomatik alt satıra akar.
            Bir alana tıklayarak başlık metnini ve stilini düzenleyebilirsiniz.
            Dar ekranlarda düzen otomatik olarak varsayılan ızgaraya döner.
          </div>

          {error && (
            <div className="mt-3 flex items-center gap-2 text-[11.5px] text-rose-600 dark:text-rose-300">
              <AlertTriangle size={13} /> {error}
            </div>
          )}
        </div>

        {/* Footer */}
        <div className="flex items-center gap-2 px-5 py-3.5 border-t border-slate-200 dark:border-white/[0.08] bg-slate-50/60 dark:bg-white/[0.02] flex-shrink-0">
          {hasCustomLayout && !confirmReset && (
            <button
              type="button"
              onClick={function () { setConfirmReset(true) }}
              disabled={saving}
              className="flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-[11.5px] font-semibold border transition-colors bg-[#fff] text-slate-500 border-slate-200 hover:text-rose-600 hover:border-rose-200 hover:bg-rose-50 dark:bg-white/[0.04] dark:text-white/50 dark:border-white/10 dark:hover:text-rose-300 dark:hover:border-rose-400/30 dark:hover:bg-rose-500/10"
            >
              <RotateCcw size={12} strokeWidth={2} />
              <span>Varsayılana Dön</span>
            </button>
          )}
          {confirmReset && (
            <div className="flex items-center gap-2">
              <span className="text-[11.5px] text-rose-600 dark:text-rose-300 font-semibold">
                Kayıtlı düzen silinsin mi?
              </span>
              <button
                type="button"
                onClick={function () { setConfirmReset(false) }}
                disabled={saving}
                className="px-2.5 py-1 rounded-md text-[11px] font-semibold border bg-[#fff] text-slate-500 border-slate-200 hover:bg-slate-100 dark:bg-white/[0.04] dark:text-white/60 dark:border-white/10 dark:hover:bg-white/[0.08]"
              >
                Vazgeç
              </button>
              <button
                type="button"
                onClick={handleReset}
                disabled={saving}
                className="px-2.5 py-1 rounded-md text-[11px] font-bold border bg-rose-600 text-white border-rose-600 hover:bg-rose-700 dark:bg-rose-500 dark:border-rose-500 dark:hover:bg-rose-600"
              >
                Sil
              </button>
            </div>
          )}
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
            disabled={saving}
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
    </div>,
    getTopBody()
  )
}
