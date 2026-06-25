/**
 * SmartBoardConfigPanel
 *
 * Kullanici widget ozellestirme paneli (Katman 2 — Gorsel Esneklik).
 *
 * Calisma mantigi:
 *   1. Admin tarafindan SmartBoard'a gonderilen `masterWidgets` (JSON props) alinir.
 *      Bu Admin'in izin verdigi "master liste"dir. Pasif widget'lar zaten burada yoktur.
 *   2. Kullanicinin localStorage'daki tercihleri (visibleIds, order) bu master
 *      listenin uzerine uygulanir.
 *   3. Kullanici drag ile siralar, X ile gizler, + ile geri ekler. Kaydet'e
 *      basinca localStorage'a yazilir, panel kapanir, SmartBoard yeniden cizilir.
 *   4. Master listede OLMAYAN bir widget id'si (ornek: admin sonradan pasif etti)
 *      localStorage'da bulunsa bile otomatik goz ardi edilir — master kazanir.
 */
import { useState, useEffect, useMemo, useCallback } from 'react'
import { motion, AnimatePresence } from 'framer-motion'
import {
  DndContext, closestCenter, PointerSensor, TouchSensor, useSensor, useSensors,
} from '@dnd-kit/core'
import {
  arrayMove, SortableContext, verticalListSortingStrategy, useSortable,
} from '@dnd-kit/sortable'
import { CSS } from '@dnd-kit/utilities'
import { X, GripVertical, Plus, Check, RotateCcw, Settings2, Search } from 'lucide-react'
import { resolveIcon, resolveColor } from './DynamicWidgetFactory'
import { loadWidgetConfig, saveWidgetConfig, resetWidgetConfig } from '../../services/widgetConfigService'

/* ── Aktif widget satiri (drag ile sirala) ────── */
function ActiveRow(props) {
  var widget = props.widget
  var onRemove = props.onRemove

  var Icon = resolveIcon(widget.icon, null, widget.dataType)
  var palette = resolveColor(widget.color, widget.dataType)

  var sortable = useSortable({ id: widget.id })
  var style = {
    transform: CSS.Transform.toString(sortable.transform),
    transition: sortable.transition,
    zIndex: sortable.isDragging ? 40 : 1,
  }

  return (
    <motion.div
      ref={sortable.setNodeRef}
      style={style}
      {...sortable.attributes}
      layout
      className={'flex items-center gap-2 px-3 py-2.5 rounded-xl transition-colors ' +
        (sortable.isDragging
          ? 'bg-slate-200 dark:bg-white/10 shadow-lg'
          : 'bg-slate-100 dark:bg-white/[0.03] hover:bg-slate-200/60 dark:hover:bg-white/[0.06]')
      }
    >
      <button
        {...sortable.listeners}
        className="cursor-grab active:cursor-grabbing p-0.5 text-slate-400 dark:text-white/40 hover:text-slate-600 dark:hover:text-white/40 transition-colors flex-shrink-0"
        title="Surukle"
      >
        <GripVertical size={14} />
      </button>

      <div className="flex items-center gap-2 flex-1 min-w-0">
        <div
          className="w-6 h-6 rounded-lg flex items-center justify-center flex-shrink-0"
          style={{ background: palette.bg, border: '1px solid ' + palette.border }}
        >
          <Icon size={12} style={{ color: palette.icon }} strokeWidth={1.8} />
        </div>
        <div className="flex flex-col min-w-0 flex-1">
          <span className="text-xs text-slate-700 dark:text-white/70 font-medium truncate">{widget.label}</span>
          {widget.dataType && (
            <span className="text-[9px] text-slate-400 dark:text-white/45 uppercase tracking-wider">
              {widget.dataType}
            </span>
          )}
        </div>
      </div>

      <button
        onClick={function() { onRemove(widget.id) }}
        className="p-1 rounded-lg text-slate-400 dark:text-white/40 hover:text-red-600 dark:hover:text-red-400 hover:bg-red-100 dark:hover:bg-red-400/10 transition-colors flex-shrink-0"
        title="Gizle"
      >
        <X size={14} />
      </button>
    </motion.div>
  )
}

/* ── Havuzdaki widget satiri ──────────────────── */
function PoolRow(props) {
  var widget = props.widget
  var onAdd = props.onAdd

  var Icon = resolveIcon(widget.icon, null, widget.dataType)
  var palette = resolveColor(widget.color, widget.dataType)

  return (
    <div className="w-full flex items-center gap-2 px-3 py-2 rounded-xl bg-slate-50 dark:bg-white/[0.02] hover:bg-slate-100 dark:hover:bg-white/[0.05] border border-transparent hover:border-slate-200 dark:hover:border-white/[0.06] transition-all group">
      <button
        type="button"
        onClick={function() { onAdd(widget.id) }}
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
            {widget.label}
          </span>
          {widget.dataType && (
            <span className="text-[10px] text-slate-400 dark:text-white/45 uppercase tracking-wider">
              {widget.dataType}
            </span>
          )}
        </div>
        <Plus size={14} className="text-slate-400 dark:text-white/40 group-hover:text-emerald-600 dark:group-hover:text-emerald-400/60 transition-colors flex-shrink-0" />
      </button>
    </div>
  )
}

/* ── Ana Panel ────────────────────────────────── */
export default function SmartBoardConfigPanel(props) {
  var isOpen = props.isOpen
  var onClose = props.onClose
  var boardKey = props.boardKey
  var masterWidgets = Array.isArray(props.masterWidgets) ? props.masterWidgets : []
  var onSaved = props.onSaved

  var [visibleIds, setVisibleIds] = useState([])
  var [order, setOrder] = useState([])
  var [saving, setSaving] = useState(false)
  var [searchQuery, setSearchQuery] = useState('')
  var [listableError, setListableError] = useState(null)

  // masterWidgets'i id bazli map'le — isPlainField sync icin
  var [localMasterWidgets, setLocalMasterWidgets] = useState(masterWidgets)

  useEffect(function() {
    setLocalMasterWidgets(masterWidgets)
  }, [masterWidgets])

  // Panel her acildiginda config'i yeniden yukle
  useEffect(function() {
    if (!isOpen) return
    setSearchQuery('')
    setListableError(null)
    var saved = loadWidgetConfig(boardKey)
    if (saved && Array.isArray(saved.visibleIds)) {
      // Master listede olmayan id'leri at (admin pasif etmisse)
      var allIds = masterWidgets.map(function(w) { return w.id })
      var cleanVisible = saved.visibleIds.filter(function(id) { return allIds.indexOf(id) !== -1 })
      var cleanOrder = (saved.order || []).filter(function(id) { return allIds.indexOf(id) !== -1 })
      // Master listedeki yeni widget'lari order sonuna ekle
      allIds.forEach(function(id) {
        if (cleanOrder.indexOf(id) === -1) cleanOrder.push(id)
      })
      setVisibleIds(cleanVisible)
      setOrder(cleanOrder)
    } else {
      // Ilk acilisda: tum master widget'lar gorunur
      var allIds2 = masterWidgets.map(function(w) { return w.id })
      setVisibleIds(allIds2.slice())
      setOrder(allIds2.slice())
    }
  }, [isOpen, boardKey, masterWidgets])

  var sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 3 } }),
    useSensor(TouchSensor, { activationConstraint: { delay: 100, tolerance: 5 } })
  )

  // Aktif widget'lar — master listeden visibleIds + order uygulanmis
  var activeWidgets = useMemo(function() {
    var map = {}
    localMasterWidgets.forEach(function(w) { map[w.id] = w })
    return order
      .filter(function(id) { return visibleIds.indexOf(id) !== -1 })
      .map(function(id) { return map[id] })
      .filter(function(w) { return w != null })
  }, [order, visibleIds, localMasterWidgets])

  // Havuz — master listede olan ama gorunur olmayanlar
  var poolWidgets = useMemo(function() {
    return localMasterWidgets.filter(function(w) {
      return visibleIds.indexOf(w.id) === -1
    })
  }, [visibleIds, localMasterWidgets])

  // Arama filtresi
  var q = searchQuery.trim().toLowerCase()
  var filteredActive = useMemo(function() {
    if (!q) return activeWidgets
    return activeWidgets.filter(function(w) {
      return (w.label || '').toLowerCase().indexOf(q) !== -1
          || (w.id   || '').toLowerCase().indexOf(q) !== -1
    })
  }, [activeWidgets, q])
  var filteredPool = useMemo(function() {
    if (!q) return poolWidgets
    return poolWidgets.filter(function(w) {
      return (w.label || '').toLowerCase().indexOf(q) !== -1
          || (w.id   || '').toLowerCase().indexOf(q) !== -1
    })
  }, [poolWidgets, q])

  function handleDragEnd(event) {
    var active = event.active
    var over = event.over
    if (!over || active.id === over.id) return
    var oldIdx = order.indexOf(active.id)
    var newIdx = order.indexOf(over.id)
    if (oldIdx === -1 || newIdx === -1) return
    setOrder(arrayMove(order, oldIdx, newIdx))
  }

  function handleAdd(widgetId) {
    if (visibleIds.indexOf(widgetId) !== -1) return
    var newVisible = visibleIds.concat([widgetId])
    setVisibleIds(newVisible)
    // Order'a da ekle (sona)
    if (order.indexOf(widgetId) === -1) {
      setOrder(order.concat([widgetId]))
    }
  }

  function handleRemove(widgetId) {
    setVisibleIds(visibleIds.filter(function(id) { return id !== widgetId }))
  }

  async function handleSave() {
    setSaving(true)
    try {
      await saveWidgetConfig(boardKey, {
        visibleIds: visibleIds,
        order: order,
        colors: {},
      })
      if (onSaved) onSaved({ visibleIds: visibleIds, order: order })
      if (onClose) onClose()
    } catch (e) {
      // Rapor §6.6 — toast fallback
      var em = 'Kaydedilirken hata: ' + e.message
      if (window.CalibraHub && window.CalibraHub.toast) window.CalibraHub.toast(em, 'err')
      else alert(em)
    } finally {
      setSaving(false)
    }
  }

  async function handleReset() {
    // Rapor §6.6 — CalibraAlert.confirm fallback
    var ok = window.CalibraAlert && window.CalibraAlert.confirm
      ? await window.CalibraAlert.confirm('Tum widget ayarlari sifirlanacak (tum widget\'lar gorunur + varsayilan sira). Devam edilsin mi?',
          { title: 'Widget Ayarlarini Sifirla', okText: 'Evet, Sifirla', cancelText: 'Vazgec', danger: true })
      : window.confirm('Tum widget ayarlari sifirlanacak (tum widget\'lar gorunur + varsayilan sira). Devam edilsin mi?')
    if (!ok) return
    try {
      await resetWidgetConfig(boardKey)
      var allIds = localMasterWidgets.map(function(w) { return w.id })
      setVisibleIds(allIds.slice())
      setOrder(allIds.slice())
      if (onSaved) onSaved({ visibleIds: allIds.slice(), order: allIds.slice() })
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
              className="h-full flex flex-col border-l border-slate-200 dark:border-white/10 shadow-[-8px_0_40px_rgba(15,23,42,0.15)] dark:shadow-[-8px_0_40px_rgba(0,0,0,0.35)]"
              style={{
                background: 'rgba(8, 11, 20, 0.92)',
                backdropFilter: 'blur(32px)',
                WebkitBackdropFilter: 'blur(32px)',
              }}
            >
              {/* Header */}
              <div className="flex items-center justify-between px-5 py-4 border-b border-white/[0.06] flex-shrink-0">
                <div className="flex items-center gap-2.5">
                  <div className="w-8 h-8 rounded-xl bg-indigo-500/20 border border-indigo-400/20 flex items-center justify-center">
                    <Settings2 size={15} className="text-indigo-400" />
                  </div>
                  <div>
                    <h3 className="text-base font-bold text-white/90">Widget Ayarlari</h3>
                    <p className="text-[11px] text-white/30 mt-0.5">Kolon ve kart gorunum ayarlari</p>
                  </div>
                </div>
                <button onClick={onClose} className="p-2 rounded-xl hover:bg-white/5 transition-colors">
                  <X size={18} className="text-white/40" />
                </button>
              </div>

              {/* Arama */}
              <div className="px-4 pt-3 pb-2 flex-shrink-0">
                <div className="relative">
                  <Search size={13} className="absolute left-3 top-1/2 -translate-y-1/2 text-white/25 pointer-events-none" />
                  <input
                    type="search"
                    value={searchQuery}
                    onChange={function(e) { setSearchQuery(e.target.value) }}
                    placeholder="Widget ara…"
                    className="w-full pl-8 pr-3 py-2 rounded-xl bg-white/[0.04] border border-white/[0.07] text-white/70 placeholder-white/20 text-xs focus:outline-none focus:border-indigo-400/40 focus:bg-white/[0.06] transition-all"
                  />
                  {searchQuery && (
                    <button
                      type="button"
                      onClick={function() { setSearchQuery('') }}
                      className="absolute right-2 top-1/2 -translate-y-1/2 text-white/30 hover:text-white/60 transition-colors text-sm leading-none"
                    >
                      ×
                    </button>
                  )}
                </div>
              </div>

              {/* Content */}
              <div className="flex-1 overflow-y-auto min-h-0">

                {/* Active widgets */}
                <div className="px-5 pt-3 pb-2">
                  <div className="flex items-center gap-2 mb-2">
                    <div className="w-1.5 h-1.5 rounded-full bg-emerald-400" />
                    <span className="text-[11px] font-bold text-white/40 uppercase tracking-wider">
                      Aktif Widget&apos;lar ({activeWidgets.length}{q ? ' / ' + filteredActive.length + ' eslesme' : ''})
                    </span>
                  </div>

                  {filteredActive.length === 0 ? (
                    <div className="text-center py-6 text-white/20 text-sm">
                      {activeWidgets.length === 0
                        ? 'Hicbir widget gorunur degil. Havuzdan ekleyin.'
                        : 'Arama ile esleşen aktif widget yok.'}
                    </div>
                  ) : (
                    <DndContext sensors={sensors} collisionDetection={closestCenter} onDragEnd={handleDragEnd}>
                      <SortableContext
                        items={activeWidgets.map(function(w) { return w.id })}
                        strategy={verticalListSortingStrategy}
                      >
                        <div className="space-y-1.5">
                          {filteredActive.map(function(w) {
                            return (
                              <ActiveRow
                                key={w.id}
                                widget={w}
                                onRemove={handleRemove}
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

                {/* Pool */}
                <div className="px-5 pb-4">
                  <div className="flex items-center gap-2 mb-2">
                    <div className="w-1.5 h-1.5 rounded-full bg-blue-400" />
                    <span className="text-[11px] font-bold text-white/40 uppercase tracking-wider">
                      Widget Havuzu ({poolWidgets.length}{q ? ' / ' + filteredPool.length + ' eslesme' : ''})
                    </span>
                  </div>

                  {filteredPool.length === 0 ? (
                    <div className="text-center py-5 text-white/20 text-xs">
                      {poolWidgets.length === 0
                        ? 'Tum widget\'lar zaten aktif'
                        : 'Arama ile esleşen widget yok.'}
                    </div>
                  ) : (
                    <div className="space-y-1">
                      {filteredPool.map(function(w) {
                        return <PoolRow key={w.id} widget={w} onAdd={handleAdd} />
                      })}
                    </div>
                  )}
                </div>
              </div>

              {/* Footer */}
              <div className="px-5 py-4 border-t border-white/[0.06] flex items-center gap-2 flex-shrink-0">
                <button
                  onClick={handleReset}
                  className="px-3 py-2.5 rounded-xl bg-white/[0.02] hover:bg-white/[0.06] border border-white/[0.06] text-xs font-medium text-white/40 hover:text-white/60 transition-all flex items-center gap-1.5"
                  title="Varsayilana sifirla"
                >
                  <RotateCcw size={13} />
                  Sifirla
                </button>
                <div className="flex-1" />
                <button
                  onClick={onClose}
                  className="px-4 py-2.5 rounded-xl bg-white/[0.04] hover:bg-white/[0.08] border border-white/[0.06] text-sm font-medium text-white/50 hover:text-white/70 transition-all"
                >
                  Iptal
                </button>
                <button
                  onClick={handleSave}
                  disabled={saving}
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
