/**
 * LineCardLayoutEditor — belge kalem KARTI duzen editoru.
 * Yeniden tasarim: 2026-08-06 (cok-ajanli tasarim paneli → "Kademeli Derinlik").
 *
 * TASARIM ILKESI — tuvalde kalici editor kromu YOK:
 *   Onceki surumde her alan kutusunun ustunde tutamak + "10/48" rozeti + goz
 *   ikonu, her satirin ustunde ilerleme rayi vardi. Alan sayisi kadar tekrar eden
 *   bu gostergeler hem ekrani yoruyor hem de WYSIWYG'i bozuyordu (gercek kartta
 *   bunlarin hicbiri yok). Simdi: tuval = gercek kartin aynasi; tutamak/isaret
 *   yalnizca hover/secim/surukleme aninda ve hucrenin GEOMETRISINE DOKUNMAYAN
 *   negatif inset'li overlay katmaninda belirir. Tum ayarlar sagdaki 296px sabit
 *   denetim rayinda (LineCardInspector) — ray hep mount oldugu icin alan secmek
 *   sifir layout shift uretir.
 *
 * Form bazli ORTAK duzen (admin tasarlar, herkes ayni karti gorur):
 *   - Surukle-birak ile alan SIRALAMA (HTML5 drag — ek bagimlilik yok)
 *   - Sag kenardan cekerek GENISLIK (48 birimlik izgara span'i)
 *   - Rayda switch ile GORUNURLUK (zorunlu alanlar kilitli — gizlenemez;
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
  LayoutGrid, GripVertical, X as XIcon, RotateCcw, AlertTriangle, Maximize2,
  MoreHorizontal, Layers, Settings, Trash2,
} from 'lucide-react'
import LineCardInspector from './LineCardInspector'
import {
  resolveIcon, CARD_LABEL_COLOR_CLS, CARD_GRID_UNITS, MIN_SPAN,
  CARD_COLUMN_GAP, CARD_ROW_GAP, CARD_FIELD_HEIGHT, WIDTH_PRESETS, spanLabel,
  clampSpan, snapSpan, packRows, unitStep,
} from './cardLayoutTokens'
// NOT: Bu modal BILEREK top govdesine (getTopBody) portallanmaz. Top'a
// portallanirsa `fixed inset-0` perdesi ust menu seridini de kaplar ve modal
// acikken baska sayfaya gecilemez (2026-08-06 kullanici bildirimi). Kurallar/
// Formuller modali (RuleBuilderModal) gibi iframe'in kendi body'sine portallanir:
// perde yalnizca calisma alanini kaplar, ust serit tiklanabilir kalir.

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
      span: (typeof it.span === 'number' && it.span >= 1 && it.span <= CARD_GRID_UNITS) ? it.span : 12,
      visible: it.visible !== false,
      locked: it.locked === true,
      isWidget: it.isWidget === true,
      // Baslik override'lari — bos/null = varsayilan
      labelText: typeof it.labelText === 'string' ? it.labelText : '',
      labelSize: it.labelSize || null,
      labelWeight: it.labelWeight || null,
      labelColor: CARD_LABEL_COLOR_CLS[it.labelColor] ? it.labelColor : null,
      // Alan Yonetimi sozlugu: null=Standart, 'modern'=Modern, 'inline'=Sade
      labelStyle: (it.labelStyle === 'modern' || it.labelStyle === 'inline') ? it.labelStyle : null,
    }
  })
}

/* Kaydet payload'i — dirty karsilastirmasi da bunun uzerinden yapilir ki
   "degisti mi" sorusu SUNUCUYA GIDECEK veriyle ayni tanima sahip olsun. */
function buildPayloadItems(items) {
  return items.map(function (it, i) {
    return {
      key: it.key, span: it.span, order: i, visible: it.visible,
      label: (it.labelText && it.labelText.trim()) ? it.labelText.trim() : null,
      labelSize: it.labelSize || null,
      labelWeight: it.labelWeight || null,
      labelColor: it.labelColor || null,
      labelStyle: it.labelStyle || null,
    }
  })
}

/* ── Merkez onay modali — proje standardi (CLAUDE.md "Silme onay standardi"):
   native confirm() YASAK, ekran ortasinda custom modal. Iki kullanim (sifirlama
   + kaydedilmemis cikis) ayni bilesenden beslenir (DRY). */
function ConfirmDialog(props) {
  var Icon = props.icon || AlertTriangle
  return (
    <div
      className="fixed inset-0 z-[70] flex items-center justify-center p-4"
      style={{ background: 'rgba(15,23,42,0.45)' }}
      onClick={function (e) { if (e.target === e.currentTarget) props.onCancel() }}
      onKeyDown={function (e) {
        if (e.key === 'Escape') { e.stopPropagation(); props.onCancel() }
        else if (e.key === 'Enter') { e.stopPropagation(); props.onConfirm() }
      }}
    >
      <div
        role="alertdialog"
        aria-modal="true"
        aria-label={props.title}
        className="w-full max-w-[380px] rounded-2xl border p-5 text-center shadow-2xl border-slate-200 bg-[#fff] dark:border-white/10 dark:bg-slate-900"
      >
        <div className="w-10 h-10 rounded-full mx-auto flex items-center justify-center bg-rose-50 text-rose-600 dark:bg-rose-500/15 dark:text-rose-300">
          <Icon size={18} strokeWidth={2} />
        </div>
        <div className="mt-3 text-[14px] font-bold text-slate-800 dark:text-white/90">{props.title}</div>
        <div className="mt-1.5 text-[12px] text-slate-500 dark:text-white/50">{props.message}</div>
        <div className="mt-4 flex items-center gap-2">
          <button
            type="button"
            onClick={props.onCancel}
            className="flex-1 h-9 rounded-lg text-[12px] font-semibold border border-slate-200 text-slate-600 hover:bg-slate-100 dark:border-white/10 dark:text-white/70 dark:hover:bg-white/[0.07]"
          >Vazgeç</button>
          <button
            type="button"
            autoFocus
            onClick={props.onConfirm}
            className="flex-1 h-9 rounded-lg text-[12px] font-bold text-[#fff] bg-rose-600 hover:bg-rose-500"
          >{props.dangerLabel}</button>
        </div>
      </div>
    </div>
  )
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
  var [canEdit, setCanEdit] = useState(true)
  var [unsupported, setUnsupported] = useState(false)
  // confirm: null | 'reset' | 'close'
  var [confirm, setConfirm] = useState(null)
  // Secili alan — key ile izlenir ki surukle-birak sonrasi secim kaybolmasin.
  var [selectedKey, setSelectedKey] = useState(null)

  var modalRef = useRef(null)
  var baselineRef = useRef(null)

  /* Dokunmatik / dar ekran — hover'a bagli her sey icin fallback sinyali. */
  var [coarse] = useState(function () {
    try { return window.matchMedia('(pointer: coarse)').matches } catch (e) { return false }
  })

  useEffect(function () {
    if (!autoLoad) {
      baselineRef.current = JSON.stringify(buildPayloadItems(normalizeEditorItems(props.items)))
      return undefined
    }
    var alive = true
    fetch('/api/line-card-layout/' + encodeURIComponent(formCode) + '/fields', { credentials: 'same-origin' })
      .then(function (r) { return r.ok ? r.json() : null })
      .then(function (data) {
        if (!alive) return
        if (!data || data.ok !== true) {
          setError((data && data.error) || 'Alan kataloğu yüklenemedi.')
          setUnsupported(true)
          return
        }
        var next = normalizeEditorItems(data.items)
        setItems(next)
        setHasCustomLayout(data.hasCustomLayout === true)
        if (data.canEdit === false) setCanEdit(false)
        baselineRef.current = JSON.stringify(buildPayloadItems(next))
      })
      .catch(function (e) { if (alive) setError('Hata: ' + (e && e.message ? e.message : String(e))) })
      .then(function () { if (alive) setLoading(false) })
    return function () { alive = false }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [autoLoad, formCode])

  var dirty = baselineRef.current != null && JSON.stringify(buildPayloadItems(items)) !== baselineRef.current

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
     dagitilir. Alan sayisi cok fazlaysa (MIN_SPAN'in altina duserdi) buton
     RAYDA zaten pasiftir ve gerekcesi yazilidir — sessiz no-op YOK. */
  function distributeRow(rowEntries) {
    var n = rowEntries.length
    if (!n) return
    var base = Math.floor(CARD_GRID_UNITS / n)
    if (base < MIN_SPAN) return
    var extra = CARD_GRID_UNITS - base * n
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
    var free = CARD_GRID_UNITS - used
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
  function toggleVisible(key) {
    setItems(function (prev) {
      var i = prev.findIndex(function (x) { return x.key === key })
      if (i < 0 || prev[i].locked) return prev
      var next = prev.slice()
      next[i] = Object.assign({}, next[i], { visible: !next[i].visible })
      return next
    })
    if (selectedKey === key) setSelectedKey(null)
  }
  function showHidden(key) {
    setItems(function (prev) {
      return prev.map(function (it) { return it.key === key ? Object.assign({}, it, { visible: true }) : it })
    })
  }

  // ── Surukle-birak siralama (HTML5 DnD — mevcut mekanizma korunur) ──
  var dragIndexRef = useRef(null)
  var [dragging, setDragging] = useState(false)
  var [dragOverKey, setDragOverKey] = useState(null)

  function handleDragStart(e, idx) {
    dragIndexRef.current = idx
    setDragging(true)
    try { e.dataTransfer.effectAllowed = 'move'; e.dataTransfer.setData('text/plain', String(idx)) } catch (_) {}
  }
  function handleDragOver(e, idx, key) {
    e.preventDefault()
    var from = dragIndexRef.current
    if (from == null || from === idx) return
    setDragOverKey(key)
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
    setDragging(false)
    setDragOverKey(null)
  }

  // ── Sag kenardan genislik (span) cekme ──
  //   Pointer Events: mouse + touch + pen tek API. Olcek `unitStep` ile alinir —
  //   ham `width / 48` bosluklari saymadigi icin sistematik kayma uretiyordu.
  var gridRef = useRef(null)
  var resizeRef = useRef(null) // { key, startX, startSpan, cellStep }
  var [resizingKey, setResizingKey] = useState(null)

  function handleResizeStart(e, key) {
    if (!gridRef.current) return
    var it = items.find(function (x) { return x.key === key })
    if (!it) return
    var rect = gridRef.current.getBoundingClientRect()
    resizeRef.current = {
      key: key,
      startX: e.clientX,
      startSpan: it.span,
      cellStep: unitStep(rect.width),
    }
    setResizingKey(key)
    try { e.currentTarget.setPointerCapture(e.pointerId) } catch (_) {}
    e.preventDefault()
    e.stopPropagation()
  }
  function handleResizeMove(e) {
    var st = resizeRef.current
    if (!st || !e.currentTarget.hasPointerCapture || !e.currentTarget.hasPointerCapture(e.pointerId)) return
    e.preventDefault()
    var deltaSpan = Math.round((e.clientX - st.startX) / Math.max(st.cellStep, 4))
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

  function requestClose() {
    if (saving) return
    if (dirty) { setConfirm('close'); return }
    onClose()
  }

  /* Klavye haritasi — Esc kademeli (once secim, sonra modal), Alt/Ctrl ok
     tuslariyla hassas ayar, Ctrl+S kaydet, Delete ile gizle. */
  function handleModalKeyDown(e) {
    if (e.key === 'Escape') {
      if (confirm) return           // onay modali kendi Esc'ini isler
      if (selectedKey) { setSelectedKey(null); return }
      requestClose()
      return
    }
    var mod = e.ctrlKey || e.metaKey
    if (mod && (e.key === 's' || e.key === 'S')) {
      e.preventDefault()
      if (canEdit && !saving && !loading) handleSave()
      return
    }
    if (!selectedKey || !canEdit) return
    var idx = items.findIndex(function (x) { return x.key === selectedKey })
    if (idx < 0) return
    var it = items[idx]
    var isInput = e.target && (e.target.tagName === 'INPUT' || e.target.tagName === 'TEXTAREA')

    if ((e.key === 'Delete' || e.key === 'Backspace') && !isInput) {
      if (it.locked) return
      e.preventDefault()
      toggleVisible(it.key)
      return
    }
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
    } else if (mod) {
      e.preventDefault()
      moveItem(idx, isRight ? 1 : -1)
    }
  }

  async function handleSave() {
    if (saving) return
    setSaving(true)
    setError(null)
    try {
      var payload = { formCode: formCode, items: buildPayloadItems(items) }
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
      baselineRef.current = JSON.stringify(payload.items)
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
      setConfirm(null)
    }
  }

  // ── Turetilmis veri ──
  var visibleEntries = []
  items.forEach(function (it, idx) { if (it.visible) visibleEntries.push({ it: it, idx: idx }) })
  var hiddenItems = items.filter(function (it) { return !it.visible })
  var rows = packRows(visibleEntries)

  var selected = selectedKey ? items.find(function (x) { return x.key === selectedKey }) : null
  // Secili alanin hangi satirda oldugunu bul (rayin "Satir" bolumu icin)
  var selRow = null
  var selRowIndex = -1
  if (selected) {
    for (var r = 0; r < rows.length; r++) {
      if (rows[r].entries.some(function (en) { return en.it.key === selected.key })) { selRow = rows[r]; selRowIndex = r; break }
    }
  }
  var rowInfo = selRow ? {
    index: selRowIndex,
    used: selRow.used,
    free: CARD_GRID_UNITS - selRow.used,
    count: selRow.entries.length,
    canDistribute: Math.floor(CARD_GRID_UNITS / selRow.entries.length) >= MIN_SPAN,
  } : null

  // ── Tuval hucresi ────────────────────────────────────────────────────────
  function renderCell(en) {
    var it = en.it
    var idx = en.idx
    var Icon = resolveIcon(it.icon)
    var isSelected = selectedKey === it.key
    var isResizing = resizingKey === it.key
    var isDragTarget = dragOverKey === it.key
    var labelText = (it.labelText && it.labelText.trim()) ? it.labelText.trim() : it.label
    var mode = it.labelStyle === 'modern' ? 'modern' : (it.labelStyle === 'inline' ? 'inline' : 'standard')
    // Ek alan (widget) sinyali: eskiden her hucrede tekrar eden "EK" rozetiydi —
    // kalabalik yapiyordu. Simdi ozel renk secilmemisse etiket METNI + ikonu sky
    // tonunda; acik "Ek Alan" rozeti yalniz alan seciliyken sag rayda gorunur.
    var colorCls = it.labelColor
      ? CARD_LABEL_COLOR_CLS[it.labelColor]
      : (it.isWidget ? 'text-sky-600 dark:text-sky-300' : 'text-slate-500 dark:text-white/45')
    var labelStyleOv = {}
    if (it.labelSize) labelStyleOv.fontSize = it.labelSize
    if (it.labelWeight) labelStyleOv.fontWeight = it.labelWeight

    // Alt cizgi — dinlenmede yari opak (gercek kartta cizgi hover/odakta belirir),
    // hover/secimde gercek kartin hover degerine cikar.
    // Yukseklik gercek karttaki giris alaniyla ayni (CARD_FIELD_HEIGHT) — sabit
    // sinif yerine token, cunku 34px kullanildiginda satirlar gercekten yuksek cikiyordu.
    var underlineCls = 'border-b transition-colors ' + (
      (isSelected || isResizing)
        ? 'border-slate-200 dark:border-white/[0.12]'
        : 'border-slate-200/50 dark:border-white/[0.06] group-hover:border-slate-200 dark:group-hover:border-white/[0.12]'
    )

    var labelInner = (
      <>
        <Icon
          size={10}
          strokeWidth={1.8}
          className={(it.isWidget ? 'text-sky-500 dark:text-sky-300' : 'text-slate-400 dark:text-white/35') + ' flex-shrink-0'}
        />
        <span className="truncate">{labelText}</span>
        {it.locked && <span className="text-rose-500 dark:text-rose-400">*</span>}
      </>
    )

    return (
      <div
        key={it.key}
        data-idx={idx}
        data-key={it.key}
        tabIndex={0}
        role="button"
        aria-pressed={isSelected}
        aria-label={labelText + ' alanı, genişlik ' + it.span + '/' + CARD_GRID_UNITS + (it.locked ? ', zorunlu' : '')}
        draggable={canEdit && !saving}
        onDragStart={function (e) { handleDragStart(e, idx) }}
        onDragOver={function (e) { handleDragOver(e, idx, it.key) }}
        onDragEnd={handleDragEnd}
        onClick={function () { setSelectedKey(isSelected ? null : it.key) }}
        onKeyDown={function (e) {
          if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); setSelectedKey(isSelected ? null : it.key) }
        }}
        className={'group relative outline-none ' + (canEdit ? 'cursor-grab active:cursor-grabbing ' : '') + (mode === 'inline' ? 'flex items-center gap-2 ' : '')}
        style={{ gridColumn: 'span ' + clampSpan(it.span), opacity: dragging && dragIndexRef.current === idx ? 0.45 : 1 }}
      >
        {/* Overlay — hucre geometrisine DOKUNMAZ (negatif inset + pointer-events yok) */}
        <div
          aria-hidden="true"
          className={'absolute rounded-lg transition-colors ' + (
            isSelected
              ? 'bg-indigo-100/60 dark:bg-indigo-500/[0.12]'
              : 'group-hover:bg-indigo-50/40 dark:group-hover:bg-indigo-500/[0.08]'
          )}
          style={Object.assign(
            { top: -4, bottom: -4, left: -2, right: -2, zIndex: 1, pointerEvents: 'none' },
            isSelected ? { outline: '1px solid rgba(99,102,241,.55)' } : null,
            isDragTarget ? { borderLeft: '2px solid #6366f1' } : null
          )}
        />
        {/* Tutamak — yalniz hover/secim */}
        {canEdit && (
          <GripVertical
            size={11}
            strokeWidth={2}
            aria-hidden="true"
            className={'absolute text-slate-400 dark:text-white/35 transition-opacity pointer-events-none ' + (
              isSelected ? 'opacity-100' : 'opacity-0 group-hover:opacity-100'
            )}
            style={{ top: -3, left: -3, zIndex: 2 }}
          />
        )}

        {/* Etiket — gercek kartin uc modu (standard / modern / inline) */}
        {mode === 'standard' && (
          <div
            className={'calibra-line-card-label flex items-center gap-1 text-[10px] font-bold tracking-wide mb-0.5 relative z-[2] ' + colorCls}
            style={labelStyleOv}
          >{labelInner}</div>
        )}
        {mode === 'inline' && (
          <div
            className={'calibra-line-card-label flex items-center gap-1 text-[10px] font-bold tracking-wide flex-shrink-0 max-w-[45%] relative z-[2] ' + colorCls}
            style={labelStyleOv}
          >{labelInner}</div>
        )}
        {mode === 'modern' ? (
          <div className="relative mt-1.5 w-full">
            <div
              className={'calibra-line-card-label absolute flex items-center gap-1 text-[9.5px] font-bold tracking-wide ' + colorCls}
              style={Object.assign({ top: -1, left: 10, zIndex: 2, lineHeight: '12px' }, labelStyleOv)}
            >{labelInner}</div>
            <div className={underlineCls} style={{ height: CARD_FIELD_HEIGHT }} />
          </div>
        ) : (
          <div
            className={underlineCls + (mode === 'inline' ? ' flex-1 min-w-0' : '')}
            style={{ height: CARD_FIELD_HEIGHT }}
          />
        )}

        {/* Canli genislik rozeti — tuvaldeki TEK sayi, yalniz boyutlandirirken */}
        {isResizing && (
          <div
            className="absolute px-1.5 py-0.5 rounded text-[11px] font-mono tabular-nums font-bold bg-indigo-500 text-[#fff]"
            style={{ top: -18, right: 0, zIndex: 3 }}
          >{spanLabel(it.span) + ' · ' + it.span + '/' + CARD_GRID_UNITS}</div>
        )}

        {/* Resize kolu — hover/secim/dokunmatik */}
        {canEdit && (
          <div
            onPointerDown={function (e) { handleResizeStart(e, it.key) }}
            onPointerMove={handleResizeMove}
            onPointerUp={handleResizeEnd}
            onPointerCancel={handleResizeEnd}
            onClick={function (e) { e.stopPropagation() }}
            title="Genişliği ayarlamak için çekin (Shift: serbest)"
            className={'absolute flex items-center justify-center transition-opacity ' + (
              (isSelected || isResizing || coarse) ? 'opacity-100' : 'opacity-0 group-hover:opacity-100'
            )}
            style={{ right: -3, top: 0, bottom: 0, width: coarse ? 20 : 10, cursor: 'ew-resize', touchAction: 'none', zIndex: 3 }}
          >
            <span className="w-[2px] h-4 rounded-full bg-indigo-400/70 dark:bg-indigo-400/50" />
          </div>
        )}
      </div>
    )
  }

  /* Satir sonu boslugu — sayi/ray yerine BOSLUGUN KENDISI gosterir. */
  function renderGap(row, rIdx) {
    var free = CARD_GRID_UNITS - row.used
    if (free < MIN_SPAN || dragging || resizingKey) return null
    return (
      <div
        key={'gap-' + rIdx}
        role="button"
        tabIndex={0}
        aria-label={'Satır ' + (rIdx + 1) + ' boşluğunu doldur (' + free + ' birim)'}
        onClick={function () { if (canEdit) fillRow(row.entries) }}
        onKeyDown={function (e) {
          if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); if (canEdit) fillRow(row.entries) }
        }}
        /* Dinlenmede neredeyse gorunmez — bos alan bir "alanmis" gibi
           algilanmasin (kullanici geri bildirimi); hover'da belirginlesir. */
        className="group/gap rounded-md border border-dashed flex items-center justify-center transition-colors border-slate-200/40 dark:border-white/[0.05] hover:border-indigo-300/70 dark:hover:border-indigo-400/40 cursor-pointer outline-none"
        style={{ gridColumn: 'span ' + free, height: CARD_FIELD_HEIGHT, alignSelf: 'end' }}
      >
        <span className="opacity-0 group-hover/gap:opacity-100 transition-opacity text-slate-400 dark:text-white/40">
          {free >= 8
            ? <span className="text-[11px] font-semibold">Boşluğu Doldur</span>
            : <Maximize2 size={13} strokeWidth={2} />}
        </span>
      </div>
    )
  }

  // Tuvale cizilecek duz liste: her satirin hucreleri + (varsa) hayalet bosluk
  var canvasChildren = []
  rows.forEach(function (row, rIdx) {
    row.entries.forEach(function (en) { canvasChildren.push(renderCell(en)) })
    var gap = renderGap(row, rIdx)
    if (gap) canvasChildren.push(gap)
  })

  return createPortal(
    <div
      onClick={function (e) { if (e.target === e.currentTarget) requestClose() }}
      onKeyDown={handleModalKeyDown}
      className="fixed inset-0 z-[60] flex items-center justify-center p-4"
      /* Blur YOK (2026-08-06 kullanici istegi): admin hangi ekran icin duzen
         yaptigini arka planda gorebilmeli. Perde yalnizca odagi modala cekecek
         kadar koyu. */
      style={{ background: 'rgba(15,23,42,0.28)' }}
    >
      <div
        ref={modalRef}
        className="w-full flex flex-col overflow-hidden rounded-2xl border shadow-2xl border-slate-200 bg-[#fff] dark:border-white/10 dark:bg-slate-900 [color-scheme:light] dark:[color-scheme:dark]"
        /* SABIT olcu (2026-08-06 kullanici istegi): genislik VE yukseklik icerige
           gore degismez — alan secildikce sag rayin icerigi degisiyor ve modal
           buyuyup kuculuyordu. Tuval genisligi de bilerek genis: gercek kart
           ~1000px, ray 296px → tuval ~1050px ile onizleme gercek kartla ayni
           OLCEKTE gorunur (WYSIWYG; dar tuvalde alanlar orantisiz genis duruyordu). */
        style={{
          maxWidth: 'min(1400px, calc(100vw - 96px))',
          height: 'min(760px, calc(100vh - 64px))',
        }}
        role="dialog"
        aria-modal="true"
        aria-label="Kart Düzeni"
      >
        {/* ── Header ── */}
        <div className="flex items-center gap-3 px-5 py-4 border-b border-slate-200 dark:border-white/[0.08] flex-shrink-0">
          <div className="w-9 h-9 rounded-xl flex items-center justify-center bg-indigo-50 border border-indigo-200 text-indigo-600 dark:bg-indigo-500/15 dark:border-indigo-400/30 dark:text-indigo-300 flex-shrink-0">
            <LayoutGrid size={17} strokeWidth={1.9} />
          </div>
          <div className="flex-1 min-w-0">
            <div className="text-[14px] font-bold text-slate-800 dark:text-white/90 flex items-center gap-2">
              <span>Kart Düzeni</span>
              {/* Hangi ekranin duzeni duzenleniyor — admin belge turleri
                  arasinda gezerken karisiklik olmasin. */}
              <span className="px-1.5 py-0.5 rounded text-[11px] font-semibold bg-indigo-50 text-indigo-600 border border-indigo-200 dark:bg-indigo-500/15 dark:text-indigo-300 dark:border-indigo-400/30 truncate">
                {props.formLabel || formCode}
              </span>
            </div>
            <div className="text-[11px] text-slate-500 dark:text-white/45 mt-0.5">
              Bu düzen belge türünün tüm kullanıcıları için geçerlidir.
            </div>
          </div>
          {dirty && (
            <div className="flex items-center gap-1.5 flex-shrink-0">
              <span className="w-1.5 h-1.5 rounded-full bg-amber-500" />
              <span className="text-[11px] text-slate-400 dark:text-white/40">Kaydedilmedi</span>
            </div>
          )}
          <button
            type="button"
            onClick={requestClose}
            aria-label="Kapat"
            className="w-8 h-8 rounded-lg flex items-center justify-center text-slate-400 hover:text-rose-500 hover:bg-rose-50 dark:text-white/35 dark:hover:text-rose-300 dark:hover:bg-rose-500/10 flex-shrink-0"
          >
            <XIcon size={15} strokeWidth={2} />
          </button>
        </div>

        {/* ── Govde: tuval + denetim rayi ── */}
        <div className="flex-1 min-h-0 grid grid-cols-1 lg:grid-cols-[1fr_296px]">
          {/* Tuval */}
          <div className="overflow-y-auto px-5 py-4">
            {unsupported ? (
              <div className="py-10 text-center text-[12px] text-slate-500 dark:text-white/45">
                {error || 'Bu form için kart düzeni desteklenmiyor.'}
              </div>
            ) : (
              <div className="rounded-xl border overflow-hidden border-slate-200 bg-[#fff] dark:border-white/10 dark:bg-white/[0.025]">
                <div
                  className="p-3"
                  style={{
                    display: 'grid',
                    gridTemplateColumns: 'auto 1fr',
                    gridTemplateAreas: '"actions fields"',
                    columnGap: CARD_COLUMN_GAP,
                    rowGap: CARD_ROW_GAP,
                    alignItems: 'start',
                  }}
                >
                  {/* Gercek kartta aksiyonlar SOL kenarda dikey serittir ve alan
                      izgarasi o kadar daralir — onizleme ayni payi birakmazsa
                      genislikler gercekle ortusmez. */}
                  <div
                    aria-hidden="true"
                    title="Kart aksiyon şeridi — sabittir, düzenlenemez"
                    className="flex flex-col items-center gap-1 flex-shrink-0 justify-self-start pointer-events-none select-none"
                    style={{ gridArea: 'actions', alignSelf: 'start', opacity: 0.55 }}
                  >
                    {[MoreHorizontal, Layers, Settings, Trash2].map(function (I, i) {
                      return (
                        <span key={i} className="w-7 h-7 rounded-lg flex items-center justify-center bg-slate-100 text-slate-300 dark:bg-white/[0.06] dark:text-white/20">
                          <I size={13} strokeWidth={2} />
                        </span>
                      )
                    })}
                  </div>

                  {/* Alan izgarasi — TEK 48 kolonluk grid; sarmayi tarayici yapar */}
                  <div
                    ref={gridRef}
                    onClick={function (e) { if (e.target === e.currentTarget) setSelectedKey(null) }}
                    style={{
                      gridArea: 'fields',
                      display: 'grid',
                      gridTemplateColumns: 'repeat(' + CARD_GRID_UNITS + ', minmax(0, 1fr))',
                      columnGap: CARD_COLUMN_GAP,
                      rowGap: CARD_ROW_GAP,
                      alignItems: 'end',
                      position: 'relative',
                    }}
                  >
                    {/* Kilavuz — yalniz boyutlandirma sirasinda, 1/4 adim */}
                    {resizingKey && (
                      <div
                        aria-hidden="true"
                        className="absolute inset-0 pointer-events-none"
                        style={{
                          zIndex: 0,
                          backgroundImage: 'repeating-linear-gradient(to right, rgba(99,102,241,.14) 0 1px, transparent 1px 25%)',
                        }}
                      />
                    )}

                    {loading
                      ? [0, 1, 2].map(function (i) {
                          return (
                            <div key={'sk-' + i} style={{ gridColumn: 'span 16' }}>
                              <div className="rounded-md bg-slate-100 dark:bg-white/[0.05] animate-pulse" style={{ height: CARD_FIELD_HEIGHT }} />
                            </div>
                          )
                        })
                      : canvasChildren}
                  </div>
                </div>

                {!loading && !visibleEntries.length && (
                  <div className="py-8 text-center text-[11.5px] text-slate-400 dark:text-white/35">
                    Kartta gösterilecek alan kalmadı — sağdaki Alan Havuzu'ndan geri ekleyin.
                  </div>
                )}
              </div>
            )}
          </div>

          {/* Denetim rayi — 296px sabit, HER ZAMAN mount (sifir layout shift) */}
          <div
            className="overflow-y-auto p-4 border-t lg:border-t-0 lg:border-l border-slate-200 dark:border-white/[0.08]"
            style={{ scrollbarGutter: 'stable' }}
          >
            <LineCardInspector
              item={selected}
              rowInfo={rowInfo}
              hiddenItems={hiddenItems}
              canEdit={canEdit && !saving}
              coarse={coarse}
              onPatch={patchItem}
              onSetSpan={setSpan}
              onDistributeRow={function () { if (selRow) distributeRow(selRow.entries) }}
              onFillRow={function () { if (selRow) fillRow(selRow.entries) }}
              onToggleVisible={toggleVisible}
              onShowHidden={showHidden}
              onMove={function (dir) {
                var i = items.findIndex(function (x) { return x.key === selectedKey })
                if (i >= 0) moveItem(i, dir)
              }}
              onClearSelection={function () { setSelectedKey(null) }}
            />
          </div>
        </div>

        {/* ── Hata seridi ── */}
        {error && !unsupported && (
          <div role="alert" className="px-5 py-2 flex items-center gap-2 border-t bg-rose-50 border-rose-200/60 dark:bg-rose-500/10 dark:border-rose-400/20 flex-shrink-0">
            <AlertTriangle size={13} strokeWidth={2} className="text-rose-600 dark:text-rose-300 flex-shrink-0" />
            <span className="text-[12px] text-rose-600 dark:text-rose-300 flex-1">{error}</span>
            <button
              type="button"
              onClick={function () { setError(null) }}
              aria-label="Hatayı kapat"
              className="w-5 h-5 rounded flex items-center justify-center text-rose-500 hover:bg-rose-100 dark:hover:bg-rose-500/20 flex-shrink-0"
            ><XIcon size={12} strokeWidth={2} /></button>
          </div>
        )}

        {/* ── Footer ── */}
        <div className="flex items-center gap-2 px-5 py-3 border-t border-slate-200 dark:border-white/[0.08] bg-slate-50/60 dark:bg-white/[0.02] flex-shrink-0">
          {canEdit && hasCustomLayout && (
            <button
              type="button"
              onClick={function () { setConfirm('reset') }}
              disabled={saving}
              className="flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-[11.5px] font-semibold border transition-colors bg-[#fff] text-slate-500 border-slate-200 hover:text-rose-600 hover:border-rose-200 hover:bg-rose-50 dark:bg-white/[0.04] dark:text-white/50 dark:border-white/10 dark:hover:text-rose-300 dark:hover:border-rose-400/30 dark:hover:bg-rose-500/10"
            >
              <RotateCcw size={12} strokeWidth={2} />
              <span>Varsayılana Dön</span>
            </button>
          )}
          <div className="flex-1" />
          {canEdit ? (
            <>
              <button
                type="button"
                onClick={requestClose}
                disabled={saving}
                className="px-3.5 py-1.5 rounded-lg text-[12px] font-semibold border transition-colors bg-[#fff] text-slate-600 border-slate-200 hover:bg-slate-100 dark:bg-white/[0.04] dark:text-white/70 dark:border-white/10 dark:hover:bg-white/[0.08]"
              >Vazgeç</button>
              <button
                type="button"
                onClick={handleSave}
                disabled={saving || loading}
                title="Ctrl+S"
                className={'px-4 py-1.5 rounded-lg text-[12px] font-bold border transition-colors text-[#fff] ' + (
                  saving ? 'bg-indigo-300 border-indigo-300 cursor-wait' : 'bg-indigo-600 border-indigo-600 hover:bg-indigo-700'
                )}
              >{saving ? 'Kaydediliyor…' : 'Kaydet'}</button>
            </>
          ) : (
            <button
              type="button"
              onClick={onClose}
              className="px-3.5 py-1.5 rounded-lg text-[12px] font-semibold border transition-colors bg-[#fff] text-slate-600 border-slate-200 hover:bg-slate-100 dark:bg-white/[0.04] dark:text-white/70 dark:border-white/10 dark:hover:bg-white/[0.08]"
            >Kapat</button>
          )}
        </div>
      </div>

      {confirm === 'reset' && (
        <ConfirmDialog
          icon={RotateCcw}
          title="Varsayılan Düzene Dön"
          message="Bu belge türü için kayıtlı kart düzeni silinecek ve kart varsayılan ızgaraya dönecek. Bu işlem geri alınamaz."
          dangerLabel="Sıfırla"
          onCancel={function () { setConfirm(null) }}
          onConfirm={handleReset}
        />
      )}
      {confirm === 'close' && (
        <ConfirmDialog
          icon={AlertTriangle}
          title="Kaydedilmemiş Değişiklikler"
          message="Yaptığınız düzen değişiklikleri kaydedilmedi. Çıkarsanız kaybolacak."
          dangerLabel="Çık"
          onCancel={function () { setConfirm(null) }}
          onConfirm={function () { setConfirm(null); onClose() }}
        />
      )}
    </div>,
    document.body
  )
}
