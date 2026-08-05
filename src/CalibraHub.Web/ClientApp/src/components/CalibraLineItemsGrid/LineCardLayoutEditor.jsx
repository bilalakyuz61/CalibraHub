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
import { useState, useRef } from 'react'
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

export default function LineCardLayoutEditor(props) {
  var formCode = props.formCode
  var onClose = props.onClose
  var onSaved = props.onSaved
  var onReset = props.onReset
  var hasCustomLayout = props.hasCustomLayout === true

  // Calisma kopyasi — Kaydet'e basilana kadar grid'e dokunulmaz.
  var [items, setItems] = useState(function () {
    return (props.items || []).map(function (it) {
      return {
        key: it.key,
        label: it.label || it.key,
        icon: it.icon || null,
        span: (typeof it.span === 'number' && it.span >= 1 && it.span <= 24) ? it.span : 6,
        visible: it.visible !== false,
        locked: it.locked === true,
        isWidget: it.isWidget === true,
      }
    })
  })
  var [saving, setSaving] = useState(false)
  var [error, setError] = useState(null)
  var [confirmReset, setConfirmReset] = useState(false)

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
  var resizeRef = useRef(null) // { idx, startX, startSpan, cellWidth }

  function handleResizeStart(e, idx) {
    if (!gridRef.current) return
    var rect = gridRef.current.getBoundingClientRect()
    resizeRef.current = {
      idx: idx,
      startX: e.clientX,
      startSpan: items[idx].span,
      cellWidth: rect.width / 24,
    }
    try { e.currentTarget.setPointerCapture(e.pointerId) } catch (_) {}
    e.preventDefault()
  }
  function handleResizeMove(e) {
    var st = resizeRef.current
    if (!st || !e.currentTarget.hasPointerCapture || !e.currentTarget.hasPointerCapture(e.pointerId)) return
    e.preventDefault()
    var deltaSpan = Math.round((e.clientX - st.startX) / Math.max(st.cellWidth, 8))
    var nextSpan = Math.min(24, Math.max(2, st.startSpan + deltaSpan))
    setItems(function (prev) {
      if (!prev[st.idx] || prev[st.idx].span === nextSpan) return prev
      var next = prev.slice()
      next[st.idx] = Object.assign({}, next[st.idx], { span: nextSpan })
      return next
    })
  }
  function handleResizeEnd(e) {
    try {
      if (e.currentTarget.hasPointerCapture(e.pointerId)) e.currentTarget.releasePointerCapture(e.pointerId)
    } catch (_) {}
    resizeRef.current = null
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
          return { key: it.key, span: it.span, order: i, visible: it.visible }
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
        className="w-full max-w-[860px] max-h-[88vh] flex flex-col overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-2xl dark:border-white/10 dark:bg-slate-900"
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
          <div className="rounded-xl border border-slate-200 bg-white dark:border-white/10 dark:bg-white/[0.025] p-3">
          <div
            ref={gridRef}
            style={{ display: 'grid', gridTemplateColumns: 'repeat(24, minmax(0, 1fr))', gap: 10 }}
          >
            {items.map(function (it, idx) {
              var hiddenCls = !it.visible ? ' opacity-40' : ''
              var dragCls = dragOverIndex === idx ? ' ring-2 ring-indigo-400/70' : ''
              var Icon = resolveIcon(it.icon)
              return (
                <div
                  key={it.key}
                  draggable
                  onDragStart={function (e) { handleDragStart(e, idx) }}
                  onDragOver={function (e) { handleDragOver(e, idx) }}
                  onDragEnd={handleDragEnd}
                  style={{ gridColumn: 'span ' + it.span, position: 'relative' }}
                  className={'group select-none rounded-lg px-1.5 pt-1 pb-1.5 cursor-grab active:cursor-grabbing border border-transparent hover:border-indigo-200/70 dark:hover:border-indigo-400/25' + hiddenCls + dragCls}
                >
                  {/* Kontrol satiri — tut/genislik/goz. Kart gorunumunu bozmasin
                      diye kucuk; blok hover'inda belirginlesir. */}
                  <div className="flex items-center gap-1 mb-0.5 opacity-50 group-hover:opacity-100 transition-opacity">
                    <GripVertical size={11} className="text-slate-400 dark:text-white/35 flex-shrink-0" />
                    {it.isWidget && (
                      <span className="text-[8.5px] font-bold px-1 rounded bg-sky-100 text-sky-600 dark:bg-sky-500/15 dark:text-sky-300 flex-shrink-0" title="Özel alan (Alan Yönetimi)">EK</span>
                    )}
                    <span className="ml-auto text-[9px] font-mono tabular-nums text-slate-400 dark:text-white/35 flex-shrink-0">
                      {it.span}/24
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
                  {/* Kartla birebir ayni etiket + hucre kutusu (canli onizleme) */}
                  <div className="calibra-line-card-label flex items-center gap-1 text-[10px] font-bold tracking-wide text-slate-500 dark:text-white/45 mb-0.5">
                    <Icon size={10} strokeWidth={1.8} className="text-slate-400 dark:text-white/35 flex-shrink-0" />
                    <span className="truncate">{it.label}</span>
                    {it.locked && <span className="text-rose-500 dark:text-rose-400">*</span>}
                  </div>
                  <div className="h-[34px] rounded-lg border border-slate-200 bg-slate-50/70 dark:border-white/10 dark:bg-white/[0.03]" />
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

          <div className="mt-3 text-[10.5px] text-slate-400 dark:text-white/35">
            Toplam genişlik 24 birimdir; bir satıra sığmayan alanlar otomatik alt satıra akar.
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
              className="flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-[11.5px] font-semibold border transition-colors bg-white text-slate-500 border-slate-200 hover:text-rose-600 hover:border-rose-200 hover:bg-rose-50 dark:bg-white/[0.04] dark:text-white/50 dark:border-white/10 dark:hover:text-rose-300 dark:hover:border-rose-400/30 dark:hover:bg-rose-500/10"
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
                className="px-2.5 py-1 rounded-md text-[11px] font-semibold border bg-white text-slate-500 border-slate-200 hover:bg-slate-100 dark:bg-white/[0.04] dark:text-white/60 dark:border-white/10 dark:hover:bg-white/[0.08]"
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
            className="px-3.5 py-1.5 rounded-lg text-[12px] font-semibold border transition-colors bg-white text-slate-600 border-slate-200 hover:bg-slate-100 dark:bg-white/[0.04] dark:text-white/70 dark:border-white/10 dark:hover:bg-white/[0.08]"
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
