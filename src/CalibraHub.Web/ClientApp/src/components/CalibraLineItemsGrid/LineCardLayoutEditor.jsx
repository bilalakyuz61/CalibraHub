/**
 * LineCardLayoutEditor — belge kalem KARTI duzen editoru.
 *
 * 2026-08-19 SADELESTIRME (referans tasarim goc): gercek kart artik CSS-grid
 * (48-birim izgara + serbest satir/sutun konumlandirma) DEGIL, flex-wrap
 * KOMPAKT BIR SERIT olarak render ediliyor (bkz. CalibraLineItemsGrid.jsx
 * renderFieldsList + .clc-strip/.clc-field-cell CSS, index.css). Onceki surumun
 * 48-birimlik surukle-birak/boyutlandirma tuvali artik GERCEKLE ORTUSMUYORDU
 * (yaniltici onizleme) — tamamen kaldirildi.
 *
 * Editor artik yalniz GERCEKTEN calisan uc ayari sunar:
 *   - Sira    — surukle-birak VEYA yukari/asagi dugmeleri (FieldRow, LineCardInspector.jsx)
 *   - Gorunurluk — switch (CLAUDE.md: checkbox degil switch)
 *   - Genislik   — Dar/Normal/Genis segment (arka planda span'e — flex-grow
 *                  agirligina — eslenir; ham 1-48 sayi/piksel-surukleme YOK)
 * + (korunan) Baslik override — metin/stil/boyut/kalinlik/renk (yalniz serit
 *   alanlarinda anlamli; kimlik alanlarinda gercek kart bu override'lari hic
 *   OKUMAZ — bkz. asagidaki "Kimlik" notu).
 *
 * KIMLIK ALANLARI (malzeme kodu/adi, satir toplami) kartin UST kimlik satirinda
 * SABITTIR — surukle-birak/genislik/baslik override'i onlar icin ANLAMSIZ
 * (gercek kart bunlari `materialCodeCol`/`materialNameCol`/`lineTotalCol`
 * uzerinden DOGRUDAN, cardItems sirasindan BAGIMSIZ cizer). Editorde ayri,
 * salt "Gorunurluk" anahtarli bir bolumde listelenir (malzeme kodu kilitli —
 * veri girisinin kapisi, asla gizlenemez).
 *
 * Onizleme artik GERCEK kart CSS siniflarini (.clc-strip, .clc-field-cell,
 * .clc-ident-*, .clc-cell-underline — index.css) YENIDEN KULLANIR (CardPreview,
 * bu dosyada) — WYSIWYG artik gercekten dogru.
 *
 * Kalicilik: POST /api/line-card-layout/save (admin-only, CSRF). Sifirla →
 * POST /api/line-card-layout/reset. Kaydedilen duzen additive-safe'tir: duzende
 * olmayan yeni kolonlar runtime'da varsayilan genislikle sona eklenir
 * (CalibraLineItemsGrid.jsx cardItems). Eski kayitlardaki row/col (v1/v2/v3
 * serbest yerlesim) alanlari YUKLEME sirasinda sessizce OKUNMAZ/yok sayilir;
 * yeni editor bu alanlari hic YAZMAZ (backend DTO'sunda hala opsiyonel/nullable
 * — kayit/yukleme HATA VERMEZ).
 */
import { useState, useRef, useEffect } from 'react'
import { createPortal } from 'react-dom'
import {
  LayoutGrid, X as XIcon, RotateCcw, AlertTriangle, Lock,
  MoreHorizontal, Layers, Settings, Trash2,
} from 'lucide-react'
import FieldRow, { SwitchToggle } from './LineCardInspector'
import {
  resolveIcon, CARD_LABEL_COLOR_CLS, CARD_GRID_UNITS, clampSpan,
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

/* Kartin UST kimlik satirinda sabit alanlar — gercek kartta cardItems sirasindan
   BAGIMSIZ, dogrudan materialCodeCol/materialNameCol/lineTotalCol uzerinden
   cizilirler (bkz. CalibraLineItemsGrid.jsx). Editorde surukle-birak/genislik/
   baslik-override SUNULMAZ — yaniltici olurdu. */
var IDENTITY_ORDER = { materialCode: 0, materialName: 1, lineTotal: 2 }
/* İskonto %/KDV % gercek kartta HER ZAMAN dar (col.type==='percent' ozel-durumu,
   bkz. CalibraLineItemsGrid.jsx cellStyle). Editorde bu iki alan icin genislik
   kontrolu yaniltici olacagindan gizlenir; "Her zaman dar" notu gosterilir. */
var PERCENT_LOCKED_KEYS = ['discountRate', 'taxRate']

function normalizeEditorItems(arr) {
  return (arr || []).map(function (it) {
    return {
      key: it.key,
      label: it.label || it.key,
      icon: it.icon || null,
      span: (typeof it.span === 'number' && it.span >= 1 && it.span <= CARD_GRID_UNITS) ? it.span : 16,
      visible: it.visible !== false,
      locked: it.locked === true,
      isWidget: it.isWidget === true,
      // Baslik override'lari — bos/null = varsayilan
      labelText: typeof it.labelText === 'string' ? it.labelText : '',
      labelSize: it.labelSize || null,
      labelWeight: it.labelWeight || null,
      labelColor: CARD_LABEL_COLOR_CLS[it.labelColor] ? it.labelColor : null,
      labelStyle: (it.labelStyle === 'modern' || it.labelStyle === 'inline') ? it.labelStyle : null,
      // row/col (v1/v2/v3 serbest yerlesim) BILEREK okunmuyor — bkz. dosya basi notu.
    }
  })
}

/* Katalogu iki gruba ayirir: Kimlik (kart basliginda sabit) + Serit (siralanabilir). */
function splitItems(raw) {
  var norm = normalizeEditorItems(raw)
  var identity = []
  var strip = []
  norm.forEach(function (it) {
    if (Object.prototype.hasOwnProperty.call(IDENTITY_ORDER, it.key)) identity.push(it)
    else strip.push(it)
  })
  identity.sort(function (a, b) { return IDENTITY_ORDER[a.key] - IDENTITY_ORDER[b.key] })
  return { identity: identity, strip: strip }
}

/* Kaydet payload'i — dirty karsilastirmasi da bunun uzerinden yapilir ki
   "degisti mi" sorusu SUNUCUYA GIDECEK veriyle ayni tanima sahip olsun.
   row/col ARTIK GONDERILMEZ (backend DTO'sunda opsiyonel/nullable — eksik
   olmasi hataya yol acmaz, sunucuda null olarak kalir). */
function buildPayloadItems(identityList, stripList) {
  return identityList.concat(stripList).map(function (it, i) {
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

/* ── Onizleme — GERCEK kart CSS siniflarini yeniden kullanir (WYSIWYG) ────── */
var PREVIEW_SAMPLE = {
  materialCode: 'ÖRN-001',
  materialName: 'Örnek Malzeme Adı',
  unitId: 'Adet',
  quantity: '10',
  unitPrice: '125,00',
  discountRate: '10',
  taxRate: '20',
  lineTotal: '1.250,00',
}

function PreviewInput(props) {
  return (
    <input
      type="text"
      readOnly
      tabIndex={-1}
      value={props.value || ''}
      placeholder={props.placeholder || ''}
      className={props.className || 'w-full h-full bg-transparent border-0 outline-none px-2.5 py-2 text-[13px] text-slate-800 dark:text-white/85'}
    />
  )
}

function PreviewFieldCell(props) {
  var it = props.item
  var Icon = resolveIcon(it.icon)
  var widthLocked = PERCENT_LOCKED_KEYS.indexOf(it.key) >= 0
  var cellStyle = widthLocked
    ? { flex: '0 0 88px', minWidth: 72, maxWidth: 104 }
    : { flex: clampSpan(it.span) + ' 1 116px', minWidth: 96 }
  var labelMode = (it.labelStyle === 'modern' || it.labelStyle === 'inline') ? it.labelStyle : 'standard'
  var labelColorCls = it.labelColor ? CARD_LABEL_COLOR_CLS[it.labelColor] : 'text-slate-500 dark:text-white/45'
  var labelStyleOv = {}
  if (it.labelSize) labelStyleOv.fontSize = it.labelSize
  if (it.labelWeight) labelStyleOv.fontWeight = it.labelWeight
  if (labelMode === 'modern') cellStyle = Object.assign({}, cellStyle, { position: 'relative' })
  var labelText = (it.labelText && it.labelText.trim()) ? it.labelText.trim() : it.label
  var labelInner = (
    <>
      {Icon && <Icon size={10} strokeWidth={1.8} className="text-slate-400 dark:text-white/35 flex-shrink-0" />}
      <span className="truncate">{labelText}</span>
      {it.locked && <span className="text-rose-500 dark:text-rose-400">*</span>}
    </>
  )
  return (
    <div className={'clc-field-cell' + (labelMode === 'inline' ? ' flex items-center gap-2' : '')} style={cellStyle}>
      {labelMode === 'standard' && (
        <div className={'calibra-line-card-label flex items-center gap-1 text-[10px] font-bold tracking-wide mb-0.5 ' + labelColorCls} style={labelStyleOv}>{labelInner}</div>
      )}
      {labelMode === 'inline' && (
        <div className={'calibra-line-card-label flex items-center gap-1 text-[10px] font-bold tracking-wide flex-shrink-0 max-w-[45%] ' + labelColorCls} style={labelStyleOv}>{labelInner}</div>
      )}
      {labelMode === 'modern' && (
        <div className={'calibra-line-card-label absolute flex items-center gap-1 text-[9.5px] font-bold tracking-wide ' + labelColorCls} style={Object.assign({ top: -1, left: 10, zIndex: 2, lineHeight: '12px' }, labelStyleOv)}>{labelInner}</div>
      )}
      <div className={'clc-cell-underline' + (labelMode === 'inline' ? ' flex-1 min-w-0' : '') + (labelMode === 'modern' ? ' mt-1.5' : '')}>
        <PreviewInput value={PREVIEW_SAMPLE[it.key]} placeholder={it.isWidget ? 'Örnek' : ''} />
      </div>
    </div>
  )
}

/* Kart iskeleti CalibraLineItemsGrid.jsx'teki markup'un sadelestirilmis, TEK
   ornek satirlik aynasidir — ayni CSS siniflari (.calibra-line-card, .clc-strip,
   .clc-field-cell, .clc-ident-*) kullanildigi icin renk/aralik/tipografi
   otomatik birebir eslenir (iki kopya CSS DEGERI YOK — yalniz markup tekrari,
   DRY siniri CSS tarafinda zaten korunuyor). */
function CardPreview(props) {
  var identityItems = props.identityItems || []
  var stripItems = props.stripItems || []
  var materialCode = identityItems.filter(function (it) { return it.key === 'materialCode' })[0] || null
  var materialName = identityItems.filter(function (it) { return it.key === 'materialName' })[0] || null
  var lineTotal = identityItems.filter(function (it) { return it.key === 'lineTotal' })[0] || null
  var showName = !!materialName && materialName.visible
  var showTotal = !!lineTotal && lineTotal.visible
  var visibleStrip = stripItems.filter(function (it) { return it.visible })

  return (
    <div aria-hidden="true" className="calibra-line-card rounded-xl border border-slate-200 bg-[#fff] dark:border-white/10 dark:bg-white/[0.025] overflow-hidden">
      <div className="p-2.5 sm:p-3 flex flex-col gap-2.5">
        <div className="flex items-start justify-between gap-3">
          <div className="flex items-baseline gap-2 min-w-0 flex-1">
            <span className="clc-ident-num flex-shrink-0">#1</span>
            {materialCode && (
              <div className="clc-ident-code clc-cell-underline flex-shrink-0 max-w-[220px]">
                <PreviewInput value={PREVIEW_SAMPLE.materialCode} className="w-full bg-transparent border-0 outline-none px-1 py-1 text-[13.5px]" />
              </div>
            )}
            {showName && (
              <div className="clc-ident-name clc-cell-underline min-w-0 flex-1">
                <PreviewInput value={PREVIEW_SAMPLE.materialName} className="w-full bg-transparent border-0 outline-none px-1 py-1 text-[13px]" />
              </div>
            )}
          </div>
          {showTotal && (
            <div className="flex-shrink-0 text-right">
              <div className="clc-ident-total-value">{PREVIEW_SAMPLE.lineTotal}</div>
            </div>
          )}
        </div>

        <div className="clc-strip flex items-stretch">
          <div aria-hidden="true" className="clc-strip-actions flex items-center gap-1 px-1.5 py-1.5 flex-shrink-0" style={{ opacity: 0.55 }}>
            {[MoreHorizontal, Layers, Settings, Trash2].map(function (I, i) {
              return (
                <span key={i} className="w-6 h-6 rounded-lg flex items-center justify-center bg-slate-100 text-slate-300 dark:bg-white/[0.06] dark:text-white/20">
                  <I size={12} strokeWidth={2} />
                </span>
              )
            })}
          </div>
          <div className="clc-fields-row" style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'stretch', flex: 1, minWidth: 0 }}>
            {visibleStrip.length
              ? visibleStrip.map(function (it) { return <PreviewFieldCell key={it.key} item={it} /> })
              : <div className="px-2.5 py-3 text-[11px] text-slate-400 dark:text-white/35">Şeritte gösterilecek alan yok.</div>}
          </div>
        </div>
      </div>
    </div>
  )
}

/* Kimlik satirindaki TEK alan — surukle/genislik/baslik YOK, yalniz gorunurluk
   (materialCode kilitli: veri girisinin kapisi, asla gizlenemez). */
function IdentityRow(props) {
  var it = props.item
  var Icon = resolveIcon(it.icon)
  return (
    <div className="flex items-center gap-2 px-2.5 py-2 rounded-lg border border-slate-200 dark:border-white/10 bg-slate-50/60 dark:bg-white/[0.02]">
      {Icon && <Icon size={13} strokeWidth={1.8} className="text-slate-400 dark:text-white/35 flex-shrink-0" />}
      <span className="text-[12.5px] text-slate-700 dark:text-white/80 flex-1 min-w-0 truncate">{it.label}</span>
      {it.locked ? (
        <span className="flex items-center gap-1 text-[10.5px] text-rose-500/80 dark:text-rose-300/70 flex-shrink-0">
          <Lock size={11} strokeWidth={2} /> Zorunlu · her zaman görünür
        </span>
      ) : (
        <SwitchToggle
          checked={it.visible}
          disabled={!props.canEdit}
          ariaLabel={it.label + ' kart başlığında görünsün'}
          onChange={function () { props.onToggle(it.key) }}
          onClass="bg-emerald-500"
          offClass="bg-rose-400 dark:bg-rose-500/60"
        />
      )}
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

  // Calisma kopyasi — Kaydet'e basilana kadar disariya dokunulmaz. Kimlik
  // (kart basligi) ve Serit (siralanabilir) ayri state'ler: gercek kartta
  // ikisinin davranisi tamamen farkli (bkz. dosya basi notu).
  var [identityItems, setIdentityItems] = useState(function () {
    return autoLoad ? [] : splitItems(props.items).identity
  })
  var [stripItems, setStripItems] = useState(function () {
    return autoLoad ? [] : splitItems(props.items).strip
  })
  var [hasCustomLayout, setHasCustomLayout] = useState(props.hasCustomLayout === true)
  var [loading, setLoading] = useState(autoLoad)
  var [saving, setSaving] = useState(false)
  var [error, setError] = useState(null)
  var [canEdit, setCanEdit] = useState(true)
  var [unsupported, setUnsupported] = useState(false)
  // confirm: null | 'reset' | 'close'
  var [confirm, setConfirm] = useState(null)
  // Serit sirasi surukleme — index bazli (drop hedefiyle konum degisimi).
  var dragIndexRef = useRef(null)
  var [draggingKey, setDraggingKey] = useState(null)

  var modalRef = useRef(null)
  var baselineRef = useRef(null)

  useEffect(function () {
    if (!autoLoad) {
      var split0 = splitItems(props.items)
      baselineRef.current = JSON.stringify(buildPayloadItems(split0.identity, split0.strip))
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
        var split = splitItems(data.items)
        setIdentityItems(split.identity)
        setStripItems(split.strip)
        setHasCustomLayout(data.hasCustomLayout === true)
        if (data.canEdit === false) setCanEdit(false)
        baselineRef.current = JSON.stringify(buildPayloadItems(split.identity, split.strip))
      })
      .catch(function (e) { if (alive) setError('Hata: ' + (e && e.message ? e.message : String(e))) })
      .then(function () { if (alive) setLoading(false) })
    return function () { alive = false }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [autoLoad, formCode])

  var dirty = baselineRef.current != null &&
    JSON.stringify(buildPayloadItems(identityItems, stripItems)) !== baselineRef.current

  function patchStripItem(key, patch) {
    setStripItems(function (prev) {
      return prev.map(function (it) { return it.key === key ? Object.assign({}, it, patch) : it })
    })
  }
  function toggleStripVisible(key) {
    setStripItems(function (prev) {
      return prev.map(function (it) {
        if (it.key !== key || it.locked) return it
        return Object.assign({}, it, { visible: !it.visible })
      })
    })
  }
  function toggleIdentityVisible(key) {
    setIdentityItems(function (prev) {
      return prev.map(function (it) {
        if (it.key !== key || it.locked) return it
        return Object.assign({}, it, { visible: !it.visible })
      })
    })
  }
  function setStripSpan(key, span) {
    patchStripItem(key, { span: span })
  }
  function moveStripItem(index, dir) {
    setStripItems(function (prev) {
      var to = index + dir
      if (to < 0 || to >= prev.length) return prev
      var next = prev.slice()
      var tmp = next[index]; next[index] = next[to]; next[to] = tmp
      return next
    })
  }
  function reorderStrip(fromIndex, toIndex) {
    if (fromIndex === toIndex) return
    setStripItems(function (prev) {
      if (fromIndex < 0 || fromIndex >= prev.length) return prev
      var next = prev.slice()
      var moved = next.splice(fromIndex, 1)[0]
      next.splice(toIndex, 0, moved)
      return next
    })
  }

  function handleRowDragStart(e, key, index) {
    dragIndexRef.current = index
    setDraggingKey(key)
    try { e.dataTransfer.effectAllowed = 'move'; e.dataTransfer.setData('text/plain', String(index)) } catch (_) {}
  }
  function handleRowDrop(index) {
    var from = dragIndexRef.current
    dragIndexRef.current = null
    setDraggingKey(null)
    if (from == null) return
    reorderStrip(from, index)
  }
  function handleRowDragEnd() {
    dragIndexRef.current = null
    setDraggingKey(null)
  }

  function requestClose() {
    if (saving) return
    if (dirty) { setConfirm('close'); return }
    onClose()
  }

  /* Klavye haritasi — Esc (once onay modali kendi Esc'ini isler, sonra kapat),
     Ctrl+S kaydet. Hucre-secim/nudge kalkti (canvas kalmadigi icin gerek yok). */
  function handleModalKeyDown(e) {
    if (e.key === 'Escape') {
      if (confirm) return
      requestClose()
      return
    }
    var mod = e.ctrlKey || e.metaKey
    if (mod && (e.key === 's' || e.key === 'S')) {
      e.preventDefault()
      if (canEdit && !saving && !loading) handleSave()
    }
  }

  async function handleSave() {
    if (saving) return
    setSaving(true)
    setError(null)
    try {
      var payload = { formCode: formCode, items: buildPayloadItems(identityItems, stripItems) }
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

  var visibleStripCount = stripItems.filter(function (it) { return it.visible }).length

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
        /* SABIT olcu — genislik VE yukseklik icerige gore degismez (CLAUDE.md
           "Modal boyut sabitliği"). Canvas kalkinca gercek kart genisligiyle
           orantili kompakt bir liste yeterli — eski 1400px tuval genisligine
           gerek kalmadi. */
        style={{
          maxWidth: 'min(760px, calc(100vw - 96px))',
          height: 'min(720px, calc(100vh - 64px))',
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

        {/* ── Govde: tek kolon, kaydirilabilir (modal boyutu sabit kalir) ── */}
        <div className="flex-1 min-h-0 overflow-y-auto px-5 py-4">
          {unsupported ? (
            <div className="py-10 text-center text-[12px] text-slate-500 dark:text-white/45">
              {error || 'Bu form için kart düzeni desteklenmiyor.'}
            </div>
          ) : (
            <div className="flex flex-col gap-5">
              {/* Onizleme */}
              <div>
                <div className="text-[11px] font-semibold text-slate-500 dark:text-white/45 mb-1.5">Önizleme</div>
                <div className="rounded-xl border border-slate-200 dark:border-white/10 bg-slate-50/60 dark:bg-white/[0.015] p-3">
                  {loading ? (
                    <div className="rounded-xl bg-slate-100 dark:bg-white/[0.05] animate-pulse" style={{ height: 96 }} />
                  ) : (
                    <CardPreview identityItems={identityItems} stripItems={stripItems} />
                  )}
                </div>
              </div>

              {/* Kimlik (kart başlığında sabit) */}
              <div>
                <div className="flex items-center gap-2 mb-1.5">
                  <span className="text-[12px] font-semibold text-slate-700 dark:text-white/80">Kimlik (Kart Başlığı)</span>
                </div>
                <div className="text-[11px] text-slate-500 dark:text-white/45 mb-2">
                  Sırası ve konumu sabittir (malzeme kodu solda, adı yanında, satır toplamı sağda) — yalnız görünürlük ayarlanır.
                </div>
                <div className="flex flex-col gap-1.5">
                  {!loading && identityItems.map(function (it) {
                    return <IdentityRow key={it.key} item={it} canEdit={canEdit && !saving} onToggle={toggleIdentityVisible} />
                  })}
                  {!loading && !identityItems.length && (
                    <div className="text-[11px] text-slate-400 dark:text-white/35 px-1">Bu form için kimlik alanı yok.</div>
                  )}
                </div>
              </div>

              {/* Şerit alanları */}
              <div>
                <div className="flex items-center justify-between gap-2 mb-1.5">
                  <span className="text-[12px] font-semibold text-slate-700 dark:text-white/80">Şerit Alanları</span>
                  {!loading && (
                    <span className="text-[10.5px] font-mono tabular-nums text-slate-400 dark:text-white/35 flex-shrink-0">
                      {visibleStripCount}/{stripItems.length} görünür
                    </span>
                  )}
                </div>
                <div className="text-[11px] text-slate-500 dark:text-white/45 mb-2">
                  Sürükleyip bırakın veya ok düğmeleriyle sıralayın; genişlik, görünürlük ve başlık her satırda ayarlanır.
                </div>
                <div className="flex flex-col gap-1.5">
                  {loading
                    ? [0, 1, 2].map(function (i) {
                        return <div key={'sk-' + i} className="rounded-lg bg-slate-100 dark:bg-white/[0.05] animate-pulse" style={{ height: 38 }} />
                      })
                    : stripItems.map(function (it, idx) {
                        return (
                          <FieldRow
                            key={it.key}
                            item={it}
                            isFirst={idx === 0}
                            isLast={idx === stripItems.length - 1}
                            canEdit={canEdit && !saving}
                            isDragging={draggingKey === it.key}
                            dragDisabled={saving}
                            widthLocked={PERCENT_LOCKED_KEYS.indexOf(it.key) >= 0}
                            onDragStart={function (e) { handleRowDragStart(e, it.key, idx) }}
                            onDrop={function () { handleRowDrop(idx) }}
                            onDragEnd={handleRowDragEnd}
                            onMoveUp={function () { moveStripItem(idx, -1) }}
                            onMoveDown={function () { moveStripItem(idx, 1) }}
                            onSetSpan={setStripSpan}
                            onToggleVisible={toggleStripVisible}
                            onPatch={patchStripItem}
                          />
                        )
                      })}
                  {!loading && !stripItems.length && (
                    <div className="text-[11px] text-slate-400 dark:text-white/35 px-1">Bu form için düzenlenebilir alan yok.</div>
                  )}
                </div>
              </div>

              {!canEdit && (
                <div className="text-[11px] text-amber-600 dark:text-amber-300">
                  Bu düzeni değiştirme yetkiniz yok — yalnızca görüntülüyorsunuz.
                </div>
              )}
            </div>
          )}
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
          message="Bu belge türü için kayıtlı kart düzeni silinecek ve kart varsayılan görünüme dönecek. Bu işlem geri alınamaz."
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
