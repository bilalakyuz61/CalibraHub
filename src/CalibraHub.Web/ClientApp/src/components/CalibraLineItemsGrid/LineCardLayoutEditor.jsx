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
  LayoutGrid, GripVertical, X as XIcon, RotateCcw, AlertTriangle,
  MoreHorizontal, Layers, Settings, Trash2,
} from 'lucide-react'
import LineCardInspector from './LineCardInspector'
import {
  resolveIcon, CARD_LABEL_COLOR_CLS, CARD_GRID_UNITS, MIN_SPAN,
  CARD_COLUMN_GAP, CARD_ROW_GAP, CARD_FIELD_HEIGHT, CARD_ROW_HEIGHT,
  WIDTH_PRESETS, spanLabel, clampSpan, snapSpan, unitStep,
  resolvePlacements, findFreeSlot, buildOccupancy, maxSpanAt,
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
      // Serbest yerlesim (v3): null → akistan turetilir (v1/v2 uyumu)
      row: (typeof it.row === 'number' && it.row >= 1) ? it.row : null,
      col: (typeof it.col === 'number' && it.col >= 1) ? it.col : null,
    }
  })
}

/**
 * Yuklemede TUM gorunur alanlarin (row, col) koordinatini sabitler.
 *
 * Neden zorunlu (2026-08-06 kullanici bildirimi): koordinati olmayan ogeler her
 * render'da AKISTAN turetilir. Kullanici tek bir alani asagi tasidiginda o alan
 * koordinat kazaniyor, digerleri hala akista oldugu icin bosalan yeri dolduruyor
 * ve "dikeyde otomatik yerlesme" olusuyordu. Koordinatlar bastan sabitlenince
 * bir alani tasimak diger alanlari ASLA oynatmaz.
 */
function freezePlacements(list) {
  var visible = list.filter(function (it) { return it.visible })
  var pl = resolvePlacements(visible.map(function (it) {
    return { key: it.key, span: it.span, row: it.row, col: it.col }
  }))
  var byKey = {}
  pl.forEach(function (p) { byKey[p.key] = p })
  return list.map(function (it) {
    var p = byKey[it.key]
    return p ? Object.assign({}, it, { row: p.row, col: p.col }) : it
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
      row: it.row || null,
      col: it.col || null,
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
    return autoLoad ? [] : freezePlacements(normalizeEditorItems(props.items))
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
      baselineRef.current = JSON.stringify(buildPayloadItems(freezePlacements(normalizeEditorItems(props.items))))
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
        // Koordinatlari HEMEN sabitle: aksi halde akistaki (row/col'suz) alanlar
        // baska bir alan tasinip yer acildiginda oraya kayiyordu.
        var next = freezePlacements(normalizeEditorItems(data.items))
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
  /* Genisletme komsuya kadar SINIRLIDIR — serbest yerlesimde bir alanin
     buyumesi baska bir alani itmemeli (2026-08-06 kullanici bildirimi). */
  function spanCeiling(key) {
    var p = placeByKey[key]
    if (!p) return CARD_GRID_UNITS
    return maxSpanAt(buildOccupancy(placements, key), p.row, p.col)
  }
  function setSpan(key, span) {
    patchItem(key, { span: Math.min(clampSpan(span), spanCeiling(key)) })
  }
  /* Satirdaki alanlari 48 birime esit boler; artan birim (48 % n) soldan
     dagitilir. Alan sayisi cok fazlaysa (MIN_SPAN'in altina duserdi) buton
     RAYDA zaten pasiftir ve gerekcesi yazilidir — sessiz no-op YOK. */
  function distributeRow(rowPls) {
    var n = (rowPls || []).length
    if (!n) return
    var base = Math.floor(CARD_GRID_UNITS / n)
    if (base < MIN_SPAN) return
    var extra = CARD_GRID_UNITS - base * n
    // Serbest yerlesimde "esit dagit" hem genisligi hem SUTUNU yeniden kurar:
    // satir soldan itibaren bosluksuz doldurulur.
    var patch = {}
    var cursor = 1
    rowPls.forEach(function (p, i) {
      var s = base + (i < extra ? 1 : 0)
      patch[p.key] = { span: s, col: cursor, row: p.row }
      cursor += s
    })
    setItems(function (prev) {
      return prev.map(function (it) {
        return patch[it.key] ? Object.assign({}, it, patch[it.key]) : it
      })
    })
  }
  /* Satirin SAGINDAKI bos birimleri en sagdaki alana ekler. */
  function fillRow(rowPls) {
    if (!rowPls || !rowPls.length) return
    var last = rowPls[rowPls.length - 1]
    var free = CARD_GRID_UNITS - (last.col - 1 + last.span)
    if (free <= 0) return
    patchItem(last.key, { span: clampSpan(last.span + free) })
  }
  /* Serbest yerlesimde "sira degistirme" yerine KONUM kaydirma: alani bir birim
     saga/sola veya bir satir yukari/asagi tasir. Hedef doluysa en yakin bos
     yuvaya oturur (ust uste binme yok). */
  function nudge(key, dRow, dCol) {
    var p = placeByKey[key]
    var it = items.find(function (x) { return x.key === key })
    if (!p || !it) return
    var span = clampSpan(it.span)
    var row = Math.max(1, p.row + dRow)
    var col = Math.min(Math.max(p.col + dCol, 1), CARD_GRID_UNITS - span + 1)
    var slot = findFreeSlot(buildOccupancy(placements, key), row, col, span)
    patchItem(key, { row: slot.row, col: slot.col })
  }
  function toggleVisible(key) {
    setItems(function (prev) {
      var i = prev.findIndex(function (x) { return x.key === key })
      if (i < 0 || prev[i].locked) return prev
      var next = prev.slice()
      next[i] = Object.assign({}, next[i], { visible: !next[i].visible })
      return next
    })
    // Secim KORUNUR: gizlenen alanin switch'i kirmizi olarak gorunur kalsin ki
    // yanlislikla gizlendiginde tek tikla geri acilabilsin (2026-08-06).
  }
  function showHidden(key) {
    setItems(function (prev) {
      return prev.map(function (it) { return it.key === key ? Object.assign({}, it, { visible: true }) : it })
    })
  }

  /* ── Surukle-birak: SERBEST yerlesim (2026-08-06 kullanici istegi) ─────
     Alanlar artik sola/uste yigilmak zorunda degil; birakildigi IZGARA
     KONUMUNA (satir + sutun) oturur, aradaki bosluk korunur. Konum yalniz
     birakma aninda yazilir; surukleme boyunca tuval sabit kalir ve hedef yuva
     kesikli bir hayaletle gosterilir (eski canli-siralama titremesi bu yuzden
     geri gelmez). Dolu bir yuvaya birakilirsa en yakin bos yuvaya kayar. */
  var dragKeyRef = useRef(null)
  var [draggingKey, setDraggingKey] = useState(null)
  var [dropSlot, setDropSlot] = useState(null)   // { row, col, span }

  /* Fare noktasindan izgara yuvasi. Satir yuksekligi sabit varsayilir
     (CARD_ROW_HEIGHT + CARD_ROW_GAP) — tuvalde tum satirlar `gridAutoRows` ile
     bu tabana oturdugu icin hesap tutarlidir. */
  function slotFromPoint(clientX, clientY, span, offCols) {
    if (!gridRef.current) return null
    var rect = gridRef.current.getBoundingClientRect()
    if (rect.width <= 0) return null
    var step = unitStep(rect.width)
    // offCols: alani ORTASINDAN tuttuysan hedef, tuttugun noktadan degil alanin
    // SOL kenarindan hesaplanmali — aksi halde hayalet saga kayik durur.
    var col = Math.floor((clientX - rect.left) / step) + 1 - (offCols || 0)
    var rowH = CARD_ROW_HEIGHT + CARD_ROW_GAP
    var row = Math.floor((clientY - rect.top) / rowH) + 1
    col = Math.min(Math.max(col, 1), CARD_GRID_UNITS - span + 1)
    row = Math.min(Math.max(row, 1), 500)
    return { row: row, col: col, span: span }
  }

  function handleDragStart(e, key, span, cellEl) {
    var offCols = 0
    try {
      var r = cellEl.getBoundingClientRect()
      if (gridRef.current) {
        var step = unitStep(gridRef.current.getBoundingClientRect().width)
        offCols = Math.max(0, Math.min(span - 1, Math.floor((e.clientX - r.left) / step)))
      }
      // Tarayicinin varsayilan surukleme kopyasi hucreye TAM hizalanir; boylece
      // "ucuncu hayalet" izlenimi ve kayik gorunum kalkar.
      e.dataTransfer.setDragImage(cellEl, e.clientX - r.left, e.clientY - r.top)
    } catch (_) {}
    dragKeyRef.current = { key: key, span: span, offCols: offCols }
    setDraggingKey(key)
    try { e.dataTransfer.effectAllowed = 'move'; e.dataTransfer.setData('text/plain', key) } catch (_) {}
  }
  function handleCanvasDragOver(e) {
    var st = dragKeyRef.current
    if (!st) return
    e.preventDefault()
    try { e.dataTransfer.dropEffect = 'move' } catch (_) {}
    var slot = slotFromPoint(e.clientX, e.clientY, st.span, st.offCols)
    if (!slot) return
    if (!dropSlot || dropSlot.row !== slot.row || dropSlot.col !== slot.col) setDropSlot(slot)
  }
  function handleCanvasDrop(e) {
    var st = dragKeyRef.current
    if (!st) return
    e.preventDefault()
    var slot = slotFromPoint(e.clientX, e.clientY, st.span, st.offCols)
    if (slot) {
      // Hedef doluysa en yakin bos yuvaya kaydir (ust uste binme YOK).
      var occ = buildOccupancy(placements, st.key)
      var free = findFreeSlot(occ, slot.row, slot.col, st.span)
      patchItem(st.key, { row: free.row, col: free.col })
    }
    handleDragEnd()
  }
  function handleDragEnd() {
    dragKeyRef.current = null
    setDraggingKey(null)
    setDropSlot(null)
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
      // Komsuya kadar olan tavan surukleme BASINDA olculur — her karede yeniden
      // hesaplansa kendi yeni genisligi tavani da buyutup kayma uretirdi.
      ceiling: spanCeiling(key),
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
    if (st.ceiling) nextSpan = Math.min(nextSpan, st.ceiling)
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
      // Ek alanlar burada gizlenemez — gorunurlukleri Alan Yonetimi'ndeki
      // "Kartta Göster" ile yonetilir (rayda da switch gosterilmez).
      if (it.locked || it.isWidget) return
      e.preventDefault()
      toggleVisible(it.key)
      return
    }
    var isLeft = e.key === 'ArrowLeft'
    var isRight = e.key === 'ArrowRight'
    var isUp = e.key === 'ArrowUp'
    var isDown = e.key === 'ArrowDown'
    // Ctrl/Cmd + ok: KONUM (serbest yerlesim) — satir/sutun kaydirma
    if (mod && (isLeft || isRight || isUp || isDown)) {
      e.preventDefault()
      nudge(it.key, isDown ? 1 : (isUp ? -1 : 0), isRight ? 1 : (isLeft ? -1 : 0))
      return
    }
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

  /* Kesin yerlesim: koordinati olanlar oldugu yerde, olmayanlar (v1/v2 kayitlari
     ve yeni eklenen alanlar) akistan turetilir. Cakisma varsa en yakin bos
     yuvaya kaydirilir — iki alan asla ust uste binmez. */
  var placements = resolvePlacements(visibleEntries.map(function (en) {
    return { key: en.it.key, span: en.it.span, row: en.it.row, col: en.it.col }
  }))
  var placeByKey = {}
  placements.forEach(function (p) { placeByKey[p.key] = p })

  var selected = selectedKey ? items.find(function (x) { return x.key === selectedKey }) : null
  var selPlace = selected ? placeByKey[selected.key] : null
  var rowPlacements = selPlace
    ? placements.filter(function (p) { return p.row === selPlace.row }).sort(function (a, b) { return a.col - b.col })
    : []
  var rowUsed = rowPlacements.reduce(function (a, p) { return a + p.span }, 0)
  var rowInfo = selPlace ? {
    index: selPlace.row - 1,
    used: rowUsed,
    free: CARD_GRID_UNITS - rowUsed,
    count: rowPlacements.length,
    canDistribute: rowPlacements.length > 0 && Math.floor(CARD_GRID_UNITS / rowPlacements.length) >= MIN_SPAN,
  } : null

  // ── Tuval hucresi ────────────────────────────────────────────────────────
  function renderCell(en, place) {
    var it = en.it
    var Icon = resolveIcon(it.icon)
    var isSelected = selectedKey === it.key
    var isResizing = resizingKey === it.key
    var isBeingDragged = draggingKey === it.key
    var labelText = (it.labelText && it.labelText.trim()) ? it.labelText.trim() : it.label
    /* Onizlemede etiket HER ZAMAN standart modda cizilir (2026-08-06 kullanici
       istegi): Modern (yuzer) ve Sade (yan yana) stilleri hucre yuksekligini/
       hizasini degistirip duzen ayarlarken tuvali karistiriyordu. Secilen stil
       kaydedilir ve GERCEK kartta uygulanir; editorde yalniz rayda ozet olarak
       gorunur. */
    var mode = 'standard'
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
        data-key={it.key}
        data-row={place.row}
        data-col={place.col}
        tabIndex={0}
        role="button"
        aria-pressed={isSelected}
        aria-label={labelText + ' alanı, satır ' + place.row + ', sütun ' + place.col + ', genişlik ' + it.span + '/' + CARD_GRID_UNITS + (it.locked ? ', zorunlu' : '')}
        draggable={canEdit && !saving}
        onDragStart={function (e) { handleDragStart(e, it.key, clampSpan(it.span), e.currentTarget) }}
        onDragEnd={handleDragEnd}
        onClick={function () { setSelectedKey(isSelected ? null : it.key) }}
        onKeyDown={function (e) {
          if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); setSelectedKey(isSelected ? null : it.key) }
        }}
        className={'group relative outline-none ' + (canEdit ? 'cursor-grab active:cursor-grabbing ' : '') + (mode === 'inline' ? 'flex items-center gap-2 ' : '')}
        style={{
          // Serbest yerlesim: hucre TAM konumuna oturur (aradaki bosluk korunur).
          gridColumn: place.col + ' / span ' + clampSpan(it.span),
          gridRow: String(place.row),
          alignSelf: 'end',
          opacity: isBeingDragged ? 0.4 : 1,
          transition: 'opacity .12s ease',
        }}
      >
        {/* Alan kutucugu + secim/hover katmani. Kesik cizgili cerceve alanin
            sinirlarini gorsel olarak belli eder (2026-08-06 kullanici istegi);
            `absolute` + negatif inset oldugu icin hucre GENISLIGINE DOKUNMAZ —
            border/ring hucreye verilseydi izgara oranlari kayardi. */}
        <div
          aria-hidden="true"
          /* border-[1px]: Bootstrap'in `.border{border:1px solid ...!important}`
             utility'si Tailwind'in `border` sinifini eziyor (kesikli cerceve
             solid'e donuyordu) — cakismayan arbitrary deger kullanilir. */
          className={'absolute rounded-lg transition-colors border-[1px] border-dashed ' + (
            isSelected
              ? 'border-indigo-400/80 bg-indigo-100/60 dark:border-indigo-400/60 dark:bg-indigo-500/[0.12]'
              : 'border-slate-300/60 dark:border-white/[0.12] group-hover:border-indigo-300 group-hover:bg-indigo-50/40 dark:group-hover:border-indigo-400/40 dark:group-hover:bg-indigo-500/[0.08]'
          )}
          style={Object.assign(
            { top: -4, bottom: -4, left: -2, right: -2, zIndex: 1, pointerEvents: 'none' },
            isSelected ? { outline: '1px solid rgba(99,102,241,.55)' } : null,
            null
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

  /* Satir sonu boslugu icin tuvalde GOSTERGE YOK (2026-08-06 kullanici
     bildirimi): kesikli cerceveli "hayalet blok" denendi, ama kullanicilar onu
     Miktar/Parti No gibi alanlarin yanindaki fazladan bir KUTU sandi. Bosluk
     bilgisi ve "Boşluğu Doldur" eylemi yalniz sag rayin "Satır" bolumunde. */
  var canvasChildren = visibleEntries.map(function (en) {
    var p = placeByKey[en.it.key]
    return p ? renderCell(en, p) : null
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
                    onDragOver={handleCanvasDragOver}
                    onDrop={handleCanvasDrop}
                    style={{
                      gridArea: 'fields',
                      display: 'grid',
                      gridTemplateColumns: 'repeat(' + CARD_GRID_UNITS + ', minmax(0, 1fr))',
                      // Serbest yerlesim: bos birakilan satirlar da yukseklik alir,
                      // yoksa "ustte bosluk birakma" gorsel olarak coker.
                      gridAutoRows: 'minmax(' + CARD_ROW_HEIGHT + 'px, auto)',
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

                    {/* Birakma hedefi hayaleti — alanin hangi satir/sutuna
                        oturacagini surukleme sirasinda gosterir. */}
                    {dropSlot && draggingKey && (
                      <div
                        aria-hidden="true"
                        style={{
                          gridColumn: dropSlot.col + ' / span ' + dropSlot.span,
                          gridRow: String(dropSlot.row),
                          alignSelf: 'end',
                          height: CARD_FIELD_HEIGHT,
                          border: '2px dashed #6366f1',
                          borderRadius: 8,
                          background: 'rgba(99,102,241,0.12)',
                          pointerEvents: 'none',
                          zIndex: 4,
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
              place={selPlace}
              rowInfo={rowInfo}
              hiddenItems={hiddenItems}
              canEdit={canEdit && !saving}
              coarse={coarse}
              onPatch={patchItem}
              onSetSpan={setSpan}
              onDistributeRow={function () { distributeRow(rowPlacements) }}
              onFillRow={function () { fillRow(rowPlacements) }}
              onToggleVisible={toggleVisible}
              onShowHidden={showHidden}
              onNudge={function (dRow, dCol) { if (selectedKey) nudge(selectedKey, dRow, dCol) }}
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
