/**
 * SmartColumnSettings
 *
 * SmartBoard TABLO modu (viewMode:'table') icin gelismis "Sutun Ayarlari" paneli.
 * SmartBoardConfigPanel.jsx'in iskelet/etkilesim desenini (dnd-kit surukle, arama
 * kutusu, Sifirla, Kaydet/Iptal, master-kazanir reconcile) baz alir; buna ek olarak
 * FulfillmentCenter.cshtml `_renderColPanel` derinligindeki per-sutun eksenlerini
 * (hizala, genislik, sabitle/pin, font boyutu+agirligi, yeniden adlandirma) React'e
 * tasir.
 *
 * KAPSAM: sadece TABLO modu board'lari kullanir (bugun tek board: Malzeme Kartlari).
 * Kart modundaki SmartBoardConfigPanel bu dosyaya HIC dokunmaz — ayri, paralel bir
 * bilesendir (regresyonsuzluk, bkz. CLAUDE.md "CalibraSmartBoard (C-Grid)").
 *
 * Calisma mantigi:
 *   1. Panel acildiginda columnConfigService.loadBoardColumnConfig(boardKey) ile
 *      (backend → localStorage fallback) kayitli config cekilir; SmartBoard'un
 *      gonderdigi `masterWidgets` (admin master listesi) ile reconcile edilir
 *      (master'da olmayan id'ler atilir, master'da yeni olanlar sona eklenir).
 *   2. Kullanici drag ile siralar, goz ile gizler/gosterir, pin ile sabitler,
 *      detay panelinden hizalama/genislik/font/baslik ayarlar. Sabitlenmis (pin)
 *      sutunlar surukleme/gizleme kilitlidir (once sabitleme kaldirilmali) ve
 *      listenin basinda gosterilir — SmartTable'daki sticky-left render'i ile
 *      birebir tutarli olmasi icin.
 *   3. Kaydet → columnConfigService.saveBoardColumnConfig(boardKey, {visibleIds,
 *      order, columns}) — localStorage'a hemen, backend'e best-effort.
 */
import { useState, useEffect, useMemo } from 'react'
import { motion, AnimatePresence } from 'framer-motion'
import {
  DndContext, closestCenter, pointerWithin, PointerSensor, TouchSensor, useSensor, useSensors,
} from '@dnd-kit/core'
import {
  arrayMove, SortableContext, verticalListSortingStrategy, useSortable,
} from '@dnd-kit/sortable'
import { CSS } from '@dnd-kit/utilities'
import {
  X, GripVertical, Plus, Check, RotateCcw, Columns3, Search, ChevronDown,
  Pin, PinOff, AlignLeft, AlignCenter, AlignRight, Minus, Loader2,
} from 'lucide-react'
import { resolveIcon, resolveColor, resolveChipWidth } from './DynamicWidgetFactory'
import { loadBoardColumnConfig, saveBoardColumnConfig } from '../../services/columnConfigService'

var ALIGN_OPTIONS = [
  { value: 'left', label: 'Sola Hizala', icon: AlignLeft },
  { value: 'center', label: 'Ortala', icon: AlignCenter },
  { value: 'right', label: 'Sağa Hizala', icon: AlignRight },
]
var FONT_SIZE_OPTIONS = [
  { value: 0, label: 'Otomatik' },
  { value: 11, label: 'Küçük' },
  { value: 13, label: 'Normal' },
  { value: 15, label: 'Büyük' },
]
var FONT_WEIGHT_OPTIONS = [
  { value: 0, label: 'Otomatik' },
  { value: 400, label: 'İnce' },
  { value: 500, label: 'Orta' },
  { value: 600, label: 'Yarı Kalın' },
  { value: 700, label: 'Kalın' },
]
var WIDTH_STEP = 20
var WIDTH_MIN = 90
var WIDTH_MAX = 480

/* ── Aktif sutun satiri — kompakt baslik + acilir detay paneli ────────── */
function ColumnRow(props) {
  var column = props.column
  var format = props.format || {}
  var pinned = !!format.pin
  var expanded = props.expanded
  var naturalWidth = props.naturalWidth

  var Icon = resolveIcon(column.icon, null, column.dataType)
  var palette = resolveColor(column.color, column.dataType)
  var displayLabel = (format.label && format.label.trim()) ? format.label : column.label

  var sortable = useSortable({ id: column.id, disabled: pinned })
  var style = {
    transform: sortable.transform
      ? CSS.Transform.toString({ ...sortable.transform, x: 0 })
      : undefined,
    transition: sortable.isDragging ? 'none' : 'transform 180ms cubic-bezier(0.2, 0, 0, 1)',
    zIndex: sortable.isDragging ? 40 : undefined,
    willChange: sortable.isDragging ? 'transform' : undefined,
    boxShadow: sortable.isDragging ? '0 8px 24px rgba(0,0,0,0.35)' : undefined,
  }

  var widthOverridden = typeof format.width === 'number' && format.width > 0
  var currentWidth = widthOverridden ? format.width : naturalWidth

  return (
    <div
      ref={sortable.setNodeRef}
      style={style}
      {...sortable.attributes}
      className={'rounded-xl border transition-all duration-[140ms] overflow-hidden ' +
        (sortable.isDragging
          ? 'border-indigo-400/50 bg-[#16223c] dark:bg-[#16223c]'
          : pinned
            ? 'border-indigo-300/50 dark:border-indigo-400/25 bg-indigo-50/70 dark:bg-indigo-500/[0.07]'
            : 'border-transparent bg-slate-100 dark:bg-white/[0.03] hover:bg-slate-200/60 dark:hover:bg-white/[0.06] hover:border-slate-200 dark:hover:border-white/[0.06]')
      }
    >
      <div className="flex items-center gap-2 px-3 py-2.5">
        {pinned ? (
          <span className="p-0.5 text-indigo-500 dark:text-indigo-400 flex-shrink-0" title="Sabitlenmiş sütun — sürüklenemez">
            <Pin size={13} />
          </span>
        ) : (
          <button
            {...sortable.listeners}
            className="cursor-grab active:cursor-grabbing p-0.5 text-slate-400 dark:text-white/40 hover:text-slate-600 dark:hover:text-white/40 transition-colors flex-shrink-0"
            title="Sürükle"
          >
            <GripVertical size={14} />
          </button>
        )}

        <div
          className="w-6 h-6 rounded-lg flex items-center justify-center flex-shrink-0"
          style={{ background: palette.bg, border: '1px solid ' + palette.border }}
        >
          <Icon size={12} style={{ color: palette.icon }} strokeWidth={1.8} />
        </div>

        <button
          type="button"
          onClick={props.onToggleExpand}
          className="flex items-center gap-1.5 flex-1 min-w-0 text-left"
          title="Detay ayarları"
        >
          <span className="flex flex-col min-w-0 flex-1">
            <span className="text-xs text-slate-700 dark:text-white/70 font-medium truncate">{displayLabel}</span>
            {column.dataType && (
              <span className="text-[9px] text-slate-400 dark:text-white/45 uppercase tracking-wider">
                {column.dataType}
              </span>
            )}
          </span>
        </button>

        <button
          type="button"
          onClick={props.onTogglePin}
          className={'p-1 rounded-lg transition-colors flex-shrink-0 ' +
            (pinned
              ? 'text-indigo-600 dark:text-indigo-400 bg-indigo-100 dark:bg-indigo-500/15'
              : 'text-slate-400 dark:text-white/40 hover:text-indigo-600 dark:hover:text-indigo-400 hover:bg-indigo-50 dark:hover:bg-indigo-500/10')
          }
          title={pinned ? 'Sabitlemeyi kaldır' : 'Sütunu sabitle (sola yaslanır, kaydırmada sabit kalır)'}
        >
          {pinned ? <Pin size={13} /> : <PinOff size={13} />}
        </button>

        <button
          type="button"
          onClick={pinned ? undefined : props.onRemove}
          disabled={pinned}
          className={'p-1 rounded-lg transition-colors flex-shrink-0 ' +
            (pinned
              ? 'text-slate-300 dark:text-white/15 cursor-not-allowed'
              : 'text-slate-400 dark:text-white/40 hover:text-red-600 dark:hover:text-red-400 hover:bg-red-100 dark:hover:bg-red-400/10')
          }
          title={pinned ? 'Önce sabitlemeyi kaldırın' : 'Gizle'}
        >
          <X size={14} />
        </button>

        <button
          type="button"
          onClick={props.onToggleExpand}
          className="p-1 rounded-lg text-slate-400 dark:text-white/40 hover:text-slate-600 dark:hover:text-white/60 transition-colors flex-shrink-0"
          title="Detay ayarları"
        >
          <ChevronDown size={14} style={{ transform: expanded ? 'rotate(180deg)' : 'none', transition: 'transform 160ms ease' }} />
        </button>
      </div>

      {expanded && (
        <div className="px-3 pb-3 pt-1 space-y-2.5 border-t border-slate-200/60 dark:border-white/[0.06]">
          {/* Başlık (yeniden adlandırma) */}
          <div className="flex items-center gap-2">
            <span className="w-16 text-[10px] font-semibold text-slate-400 dark:text-white/35 flex-shrink-0">Başlık</span>
            <input
              type="text"
              value={format.label || ''}
              placeholder={column.label}
              onChange={function (e) { props.onSetLabel(e.target.value) }}
              className="flex-1 min-w-0 px-2 py-1 rounded-lg bg-white dark:bg-white/[0.04] border border-slate-200 dark:border-white/[0.08] text-xs text-slate-700 dark:text-white/70 placeholder-slate-400 dark:placeholder-white/25 focus:outline-none focus:border-indigo-400/50"
            />
          </div>

          {/* Hizalama */}
          <div className="flex items-center gap-2">
            <span className="w-16 text-[10px] font-semibold text-slate-400 dark:text-white/35 flex-shrink-0">Hizalama</span>
            <div className="flex items-center gap-1">
              {ALIGN_OPTIONS.map(function (opt) {
                var active = (format.align || 'left') === opt.value
                var OptIcon = opt.icon
                return (
                  <button
                    key={opt.value}
                    type="button"
                    onClick={function () { props.onSetAlign(opt.value) }}
                    className={'p-1.5 rounded-lg border transition-colors ' +
                      (active
                        ? 'bg-indigo-500/20 border-indigo-400/40 text-indigo-600 dark:text-indigo-300'
                        : 'bg-white dark:bg-white/[0.03] border-slate-200 dark:border-white/[0.06] text-slate-400 dark:text-white/40 hover:text-slate-600 dark:hover:text-white/60')
                    }
                    title={opt.label}
                  >
                    <OptIcon size={12} />
                  </button>
                )
              })}
            </div>
          </div>

          {/* Genişlik */}
          <div className="flex items-center gap-2">
            <span className="w-16 text-[10px] font-semibold text-slate-400 dark:text-white/35 flex-shrink-0">Genişlik</span>
            <div className="flex items-center gap-1.5">
              <button
                type="button"
                onClick={function () { props.onWidthStep(-1) }}
                className="w-6 h-6 rounded-lg flex items-center justify-center bg-white dark:bg-white/[0.03] border border-slate-200 dark:border-white/[0.06] text-slate-500 dark:text-white/50 hover:text-indigo-600 dark:hover:text-indigo-300 transition-colors flex-shrink-0"
                title="Daralt"
              >
                <Minus size={11} />
              </button>
              <span
                className="text-[11px] text-slate-500 dark:text-white/45 min-w-[58px] text-center flex-shrink-0"
                style={{ fontFamily: 'ui-monospace, Menlo, Consolas, monospace' }}
              >
                {widthOverridden ? (currentWidth + 'px') : 'Otomatik'}
              </span>
              <button
                type="button"
                onClick={function () { props.onWidthStep(1) }}
                className="w-6 h-6 rounded-lg flex items-center justify-center bg-white dark:bg-white/[0.03] border border-slate-200 dark:border-white/[0.06] text-slate-500 dark:text-white/50 hover:text-indigo-600 dark:hover:text-indigo-300 transition-colors flex-shrink-0"
                title="Genişlet"
              >
                <Plus size={11} />
              </button>
              {widthOverridden && (
                <button
                  type="button"
                  onClick={props.onWidthReset}
                  className="text-[10px] text-slate-400 dark:text-white/35 hover:text-indigo-500 dark:hover:text-indigo-300 underline underline-offset-2 ml-1 flex-shrink-0"
                >
                  Otomatik&apos;a dön
                </button>
              )}
            </div>
          </div>

          {/* Boyut */}
          <div className="flex items-center gap-2">
            <span className="w-16 text-[10px] font-semibold text-slate-400 dark:text-white/35 flex-shrink-0">Boyut</span>
            <div className="flex items-center gap-1 flex-wrap">
              {FONT_SIZE_OPTIONS.map(function (opt) {
                var active = (format.fontSize || 0) === opt.value
                return (
                  <button
                    key={opt.value}
                    type="button"
                    onClick={function () { props.onSetFontSize(opt.value) }}
                    className={'px-2 py-1 rounded-lg border text-[10.5px] font-medium transition-colors ' +
                      (active
                        ? 'bg-indigo-500/20 border-indigo-400/40 text-indigo-600 dark:text-indigo-300'
                        : 'bg-white dark:bg-white/[0.03] border-slate-200 dark:border-white/[0.06] text-slate-400 dark:text-white/40 hover:text-slate-600 dark:hover:text-white/60')
                    }
                  >
                    {opt.label}
                  </button>
                )
              })}
            </div>
          </div>

          {/* Kalınlık */}
          <div className="flex items-center gap-2">
            <span className="w-16 text-[10px] font-semibold text-slate-400 dark:text-white/35 flex-shrink-0">Kalınlık</span>
            <div className="flex items-center gap-1 flex-wrap">
              {FONT_WEIGHT_OPTIONS.map(function (opt) {
                var active = (format.fontWeight || 0) === opt.value
                return (
                  <button
                    key={opt.value}
                    type="button"
                    onClick={function () { props.onSetFontWeight(opt.value) }}
                    className={'px-2 py-1 rounded-lg border text-[10.5px] transition-colors ' +
                      (active
                        ? 'bg-indigo-500/20 border-indigo-400/40 text-indigo-600 dark:text-indigo-300'
                        : 'bg-white dark:bg-white/[0.03] border-slate-200 dark:border-white/[0.06] text-slate-400 dark:text-white/40 hover:text-slate-600 dark:hover:text-white/60')
                    }
                    style={{ fontWeight: opt.value || 400 }}
                  >
                    {opt.label}
                  </button>
                )
              })}
            </div>
          </div>
        </div>
      )}
    </div>
  )
}

/* ── Havuzdaki (gizli) sutun satiri ────────────────────────────────────── */
function PoolRow(props) {
  var column = props.column
  var onAdd = props.onAdd

  var Icon = resolveIcon(column.icon, null, column.dataType)
  var palette = resolveColor(column.color, column.dataType)

  return (
    <div className="w-full flex items-center gap-2 px-3 py-2 rounded-xl bg-slate-50 dark:bg-white/[0.02] hover:bg-slate-100 dark:hover:bg-white/[0.05] border border-transparent hover:border-slate-200 dark:hover:border-white/[0.06] transition-all group">
      <button
        type="button"
        onClick={function () { onAdd(column.id) }}
        className="flex items-center gap-3 flex-1 min-w-0 text-left"
      >
        <div
          className="w-7 h-7 rounded-lg flex items-center justify-center flex-shrink-0 opacity-70"
          style={{ background: palette.bg, border: '1px solid ' + palette.border }}
        >
          <Icon size={14} style={{ color: palette.icon, opacity: 0.8 }} strokeWidth={1.8} />
        </div>
        <div className="flex flex-col flex-1 text-left min-w-0">
          <span className="text-sm text-slate-500 dark:text-white/40 group-hover:text-slate-700 dark:group-hover:text-white/60 font-medium transition-colors truncate">
            {column.label}
          </span>
          {column.dataType && (
            <span className="text-[10px] text-slate-400 dark:text-white/45 uppercase tracking-wider">
              {column.dataType}
            </span>
          )}
        </div>
        <Plus size={14} className="text-slate-400 dark:text-white/40 group-hover:text-emerald-600 dark:group-hover:text-emerald-400/60 transition-colors flex-shrink-0" />
      </button>
    </div>
  )
}

/* ── Ana Panel ──────────────────────────────────────────────────────────── */
export default function SmartColumnSettings(props) {
  var isOpen = props.isOpen
  var onClose = props.onClose
  var boardKey = props.boardKey
  var masterWidgets = Array.isArray(props.masterWidgets) ? props.masterWidgets : []
  var onSaved = props.onSaved

  var [visibleIds, setVisibleIds] = useState([])
  var [order, setOrder] = useState([])
  var [columns, setColumns] = useState({})
  var [loadingCfg, setLoadingCfg] = useState(false)
  var [saving, setSaving] = useState(false)
  var [searchQuery, setSearchQuery] = useState('')
  var [expandedIds, setExpandedIds] = useState(function () { return new Set() })

  var [localMasterWidgets, setLocalMasterWidgets] = useState(masterWidgets)
  useEffect(function () { setLocalMasterWidgets(masterWidgets) }, [masterWidgets])

  // Panel her acildiginda config'i backend'den (once) / localStorage'dan (fallback) yeniden yukle.
  useEffect(function () {
    if (!isOpen) return
    setSearchQuery('')
    setExpandedIds(new Set())
    setLoadingCfg(true)
    var cancelled = false
    loadBoardColumnConfig(boardKey).then(function (saved) {
      if (cancelled) return
      var allIds = masterWidgets.map(function (w) { return w.id })
      if (saved && Array.isArray(saved.visibleIds) && saved.visibleIds.length + saved.order.length > 0) {
        var cleanVisible = saved.visibleIds.filter(function (id) { return allIds.indexOf(id) !== -1 })
        var cleanOrder = (saved.order || []).filter(function (id) { return allIds.indexOf(id) !== -1 })
        allIds.forEach(function (id) { if (cleanOrder.indexOf(id) === -1) cleanOrder.push(id) })
        var cleanColumns = {}
        var savedColumns = saved.columns || {}
        Object.keys(savedColumns).forEach(function (id) {
          if (allIds.indexOf(id) !== -1) cleanColumns[id] = savedColumns[id]
        })
        setVisibleIds(cleanVisible)
        setOrder(cleanOrder)
        setColumns(cleanColumns)
      } else {
        // Ilk acilis / hic kayit yok — tum master sutunlar gorunur, dogal sira.
        setVisibleIds(allIds.slice())
        setOrder(allIds.slice())
        setColumns({})
      }
    }).finally(function () {
      if (!cancelled) setLoadingCfg(false)
    })
    return function () { cancelled = true }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isOpen, boardKey, masterWidgets])

  var sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 5 } }),
    useSensor(TouchSensor, { activationConstraint: { delay: 120, tolerance: 6 } })
  )

  var activeColumns = useMemo(function () {
    var map = {}
    localMasterWidgets.forEach(function (w) { map[w.id] = w })
    return order
      .filter(function (id) { return visibleIds.indexOf(id) !== -1 })
      .map(function (id) { return map[id] })
      .filter(function (w) { return w != null })
  }, [order, visibleIds, localMasterWidgets])

  var poolColumns = useMemo(function () {
    return localMasterWidgets.filter(function (w) { return visibleIds.indexOf(w.id) === -1 })
  }, [visibleIds, localMasterWidgets])

  // Pin partition — sabitlenmis sutunlar SmartTable'daki sticky-left render'iyle
  // tutarli olmasi icin listenin basina alinir (bkz. SmartTable.computeColumns).
  var pinnedActive = useMemo(function () {
    return activeColumns.filter(function (w) { return columns[w.id] && columns[w.id].pin })
  }, [activeColumns, columns])
  var unpinnedActive = useMemo(function () {
    return activeColumns.filter(function (w) { return !(columns[w.id] && columns[w.id].pin) })
  }, [activeColumns, columns])
  var orderedActive = useMemo(function () { return pinnedActive.concat(unpinnedActive) }, [pinnedActive, unpinnedActive])

  var q = searchQuery.trim().toLowerCase()
  function matchesQuery(w) {
    return (w.label || '').toLowerCase().indexOf(q) !== -1 || (w.id || '').toLowerCase().indexOf(q) !== -1
  }
  var filteredActive = useMemo(function () {
    if (!q) return orderedActive
    return orderedActive.filter(matchesQuery)
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [orderedActive, q])
  var filteredPool = useMemo(function () {
    if (!q) return poolColumns
    return poolColumns.filter(matchesQuery)
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [poolColumns, q])

  function handleDragEnd(event) {
    var active = event.active
    var over = event.over
    if (!over || active.id === over.id) return
    if (columns[active.id] && columns[active.id].pin) return
    if (columns[over.id] && columns[over.id].pin) return
    var oldIdx = order.indexOf(active.id)
    var newIdx = order.indexOf(over.id)
    if (oldIdx === -1 || newIdx === -1) return
    setOrder(arrayMove(order, oldIdx, newIdx))
  }

  function handleAdd(id) {
    if (visibleIds.indexOf(id) !== -1) return
    setVisibleIds(visibleIds.concat([id]))
    if (order.indexOf(id) === -1) setOrder(order.concat([id]))
  }

  function handleRemove(id) {
    if (columns[id] && columns[id].pin) return
    setVisibleIds(visibleIds.filter(function (x) { return x !== id }))
  }

  // Tek noktadan patch — falsy/varsayilan degerler ("left" hizalama, 0 boyut/agirlik/
  // genislik, false pin, bos etiket) key'i siler; config sadece OVERRIDE'lari tasir.
  function patchColumn(id, patch) {
    setColumns(function (prev) {
      var next = Object.assign({}, prev)
      var cur = Object.assign({}, next[id] || {})
      Object.keys(patch).forEach(function (k) {
        var v = patch[k]
        if (v === undefined || v === null || v === '' || v === 0 || v === false) delete cur[k]
        else cur[k] = v
      })
      if (Object.keys(cur).length === 0) delete next[id]
      else next[id] = cur
      return next
    })
  }

  function handleTogglePin(id) {
    var isPinned = !!(columns[id] && columns[id].pin)
    patchColumn(id, { pin: isPinned ? undefined : true })
  }
  function handleSetAlign(id, align) {
    patchColumn(id, { align: align === 'left' ? undefined : align })
  }
  function handleSetLabel(id, label) {
    patchColumn(id, { label: label })
  }
  function handleSetFontSize(id, size) {
    patchColumn(id, { fontSize: size || undefined })
  }
  function handleSetFontWeight(id, weight) {
    patchColumn(id, { fontWeight: weight || undefined })
  }
  function handleWidthStep(id, widget, dir) {
    var natural = resolveChipWidth(widget.dataType, widget.type)
    var cur = (columns[id] && columns[id].width) || natural
    var next = Math.max(WIDTH_MIN, Math.min(WIDTH_MAX, cur + dir * WIDTH_STEP))
    patchColumn(id, { width: next === natural ? undefined : next })
  }
  function handleWidthReset(id) {
    patchColumn(id, { width: undefined })
  }
  function toggleExpand(id) {
    setExpandedIds(function (prev) {
      var next = new Set(prev)
      if (next.has(id)) next.delete(id); else next.add(id)
      return next
    })
  }

  function handleSave() {
    setSaving(true)
    try {
      var payload = { visibleIds: visibleIds, order: order, columns: columns }
      var normalized = saveBoardColumnConfig(boardKey, payload)
      if (onSaved) onSaved(normalized)
      if (onClose) onClose()
    } catch (e) {
      var em = 'Kaydedilirken hata: ' + e.message
      if (window.CalibraHub && window.CalibraHub.toast) window.CalibraHub.toast(em, 'err')
      else alert(em)
    } finally {
      setSaving(false)
    }
  }

  async function handleReset() {
    var ok = window.CalibraAlert && window.CalibraAlert.confirm
      ? await window.CalibraAlert.confirm('Tum sutun ayarlari sifirlanacak (tum sutunlar gorunur + varsayilan sira/bicim). Devam edilsin mi?',
          { title: 'Sutun Ayarlarini Sifirla', okText: 'Evet, Sifirla', cancelText: 'Vazgec', danger: true })
      : window.confirm('Tum sutun ayarlari sifirlanacak (tum sutunlar gorunur + varsayilan sira/bicim). Devam edilsin mi?')
    if (!ok) return
    var allIds = localMasterWidgets.map(function (w) { return w.id })
    var defaultConfig = { visibleIds: allIds.slice(), order: allIds.slice(), columns: {} }
    try {
      var normalized = saveBoardColumnConfig(boardKey, defaultConfig)
      setVisibleIds(normalized.visibleIds)
      setOrder(normalized.order)
      setColumns(normalized.columns)
      if (onSaved) onSaved(normalized)
    } catch (e) {
      var em2 = 'Sifirlanamadi: ' + e.message
      if (window.CalibraHub && window.CalibraHub.toast) window.CalibraHub.toast(em2, 'err')
      else alert(em2)
    }
  }

  return (
    <AnimatePresence>
      {isOpen && (
        <>
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            className="fixed inset-0 z-[9998] bg-black/60 backdrop-blur-sm"
            onClick={onClose}
          />

          <motion.div
            initial={{ opacity: 0, x: 80 }}
            animate={{ opacity: 1, x: 0 }}
            exit={{ opacity: 0, x: 60 }}
            transition={{ type: 'spring', stiffness: 320, damping: 30 }}
            className="fixed right-0 top-0 bottom-0 z-[9999] w-full max-w-md"
            data-nodirty
          >
            <div
              className="scs-panel h-full flex flex-col border-l border-slate-200 dark:border-white/10 shadow-[-8px_0_40px_rgba(15,23,42,0.15)] dark:shadow-[-8px_0_40px_rgba(0,0,0,0.35)] bg-white/95 dark:bg-[rgba(8,11,20,0.92)] backdrop-blur-[32px]"
            >
              {/* Header */}
              <div className="flex items-center justify-between px-5 py-4 border-b border-slate-200 dark:border-white/[0.06] flex-shrink-0">
                <div className="flex items-center gap-2.5">
                  <div className="w-8 h-8 rounded-xl bg-indigo-500/20 border border-indigo-400/20 flex items-center justify-center">
                    <Columns3 size={15} className="text-indigo-400" />
                  </div>
                  <div>
                    <h3 className="text-base font-bold text-slate-800 dark:text-white/90">Sütun Ayarları</h3>
                    <p className="text-[11px] text-slate-500 dark:text-white/30 mt-0.5">Görünürlük, sıralama ve biçim</p>
                  </div>
                </div>
                <button onClick={onClose} className="p-2 rounded-xl hover:bg-slate-100 dark:hover:bg-white/5 transition-colors">
                  <X size={18} className="text-slate-400 dark:text-white/40" />
                </button>
              </div>

              {/* Arama */}
              <div className="px-4 pt-3 pb-2 flex-shrink-0">
                <div className="relative">
                  <Search size={13} className="absolute left-3 top-1/2 -translate-y-1/2 text-slate-400 dark:text-white/25 pointer-events-none" />
                  <input
                    type="search"
                    value={searchQuery}
                    onChange={function (e) { setSearchQuery(e.target.value) }}
                    placeholder="Sütun ara…"
                    className="w-full pl-8 pr-3 py-2 rounded-xl bg-slate-100 dark:bg-white/[0.04] border border-slate-200 dark:border-white/[0.07] text-slate-700 dark:text-white/70 placeholder-slate-400 dark:placeholder-white/20 text-xs focus:outline-none focus:border-indigo-400/50 dark:focus:border-indigo-400/40 focus:bg-white dark:focus:bg-white/[0.06] transition-all"
                  />
                  {searchQuery && (
                    <button
                      type="button"
                      onClick={function () { setSearchQuery('') }}
                      className="absolute right-2 top-1/2 -translate-y-1/2 text-slate-400 dark:text-white/30 hover:text-slate-600 dark:hover:text-white/60 transition-colors text-sm leading-none"
                    >
                      ×
                    </button>
                  )}
                </div>
              </div>

              {/* Content */}
              <div className="flex-1 overflow-y-auto min-h-0">
                {loadingCfg ? (
                  <div className="flex items-center justify-center gap-2 py-16 text-slate-400 dark:text-white/30 text-xs">
                    <Loader2 size={16} className="animate-spin" />
                    Yükleniyor…
                  </div>
                ) : (
                  <>
                    {/* Aktif sutunlar */}
                    <div className="px-5 pt-3 pb-2">
                      <div className="flex items-center gap-2 mb-2">
                        <div className="w-1.5 h-1.5 rounded-full bg-emerald-400" />
                        <span className="text-[11px] font-bold text-slate-500 dark:text-white/40 uppercase tracking-wider">
                          Aktif Sütunlar ({activeColumns.length}{q ? ' / ' + filteredActive.length + ' eşleşme' : ''})
                        </span>
                      </div>

                      {filteredActive.length === 0 ? (
                        <div className="text-center py-6 text-slate-400 dark:text-white/20 text-sm">
                          {activeColumns.length === 0
                            ? 'Hiçbir sütun görünür değil. Havuzdan ekleyin.'
                            : 'Arama ile eşleşen aktif sütun yok.'}
                        </div>
                      ) : (
                        <DndContext
                          sensors={sensors}
                          collisionDetection={function (args) {
                            var within = pointerWithin(args)
                            return within.length ? within : closestCenter(args)
                          }}
                          onDragEnd={handleDragEnd}
                        >
                          <SortableContext
                            items={orderedActive.map(function (w) { return w.id })}
                            strategy={verticalListSortingStrategy}
                          >
                            <div className="space-y-1.5">
                              {filteredActive.map(function (w) {
                                var format = columns[w.id] || {}
                                return (
                                  <ColumnRow
                                    key={w.id}
                                    column={w}
                                    format={format}
                                    expanded={expandedIds.has(w.id)}
                                    naturalWidth={resolveChipWidth(w.dataType, w.type)}
                                    onToggleExpand={function () { toggleExpand(w.id) }}
                                    onTogglePin={function () { handleTogglePin(w.id) }}
                                    onRemove={function () { handleRemove(w.id) }}
                                    onSetAlign={function (a) { handleSetAlign(w.id, a) }}
                                    onSetLabel={function (l) { handleSetLabel(w.id, l) }}
                                    onSetFontSize={function (s) { handleSetFontSize(w.id, s) }}
                                    onSetFontWeight={function (fw) { handleSetFontWeight(w.id, fw) }}
                                    onWidthStep={function (dir) { handleWidthStep(w.id, w, dir) }}
                                    onWidthReset={function () { handleWidthReset(w.id) }}
                                  />
                                )
                              })}
                            </div>
                          </SortableContext>
                        </DndContext>
                      )}
                    </div>

                    {/* Divider */}
                    <div className="mx-5 my-3 h-px bg-white/[0.06]" />

                    {/* Havuz */}
                    <div className="px-5 pb-4">
                      <div className="flex items-center gap-2 mb-2">
                        <div className="w-1.5 h-1.5 rounded-full bg-blue-400" />
                        <span className="text-[11px] font-bold text-white/40 uppercase tracking-wider">
                          Sütun Havuzu ({poolColumns.length}{q ? ' / ' + filteredPool.length + ' eşleşme' : ''})
                        </span>
                      </div>

                      {filteredPool.length === 0 ? (
                        <div className="text-center py-5 text-white/20 text-xs">
                          {poolColumns.length === 0
                            ? 'Tüm sütunlar zaten aktif'
                            : 'Arama ile eşleşen sütun yok.'}
                        </div>
                      ) : (
                        <div className="space-y-1">
                          {filteredPool.map(function (w) {
                            return <PoolRow key={w.id} column={w} onAdd={handleAdd} />
                          })}
                        </div>
                      )}
                    </div>
                  </>
                )}
              </div>

              {/* Footer */}
              <div className="px-5 py-4 border-t border-white/[0.06] flex items-center gap-2 flex-shrink-0">
                <button
                  onClick={handleReset}
                  className="px-3 py-2.5 rounded-xl bg-white/[0.02] hover:bg-white/[0.06] border border-white/[0.06] text-xs font-medium text-white/40 hover:text-white/60 transition-all flex items-center gap-1.5"
                  title="Varsayilana sifirla"
                >
                  <RotateCcw size={13} />
                  Sıfırla
                </button>
                <div className="flex-1" />
                <button
                  onClick={onClose}
                  className="px-4 py-2.5 rounded-xl bg-white/[0.04] hover:bg-white/[0.08] border border-white/[0.06] text-sm font-medium text-white/50 hover:text-white/70 transition-all"
                >
                  İptal
                </button>
                <button
                  onClick={handleSave}
                  disabled={saving || loadingCfg}
                  className="px-4 py-2.5 rounded-xl bg-indigo-500/25 hover:bg-indigo-500/35 border border-indigo-400/25 hover:border-indigo-400/35 text-sm font-semibold text-indigo-200 transition-all flex items-center gap-2 disabled:opacity-50"
                >
                  <Check size={15} />
                  {saving ? 'Kaydediliyor...' : 'Kaydet'}
                </button>
              </div>
            </div>
          </motion.div>
        </>
      )}
    </AnimatePresence>
  )
}
