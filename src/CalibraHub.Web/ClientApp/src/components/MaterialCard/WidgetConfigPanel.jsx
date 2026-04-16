import { useState, useEffect, useMemo } from 'react'
import { motion, AnimatePresence } from 'framer-motion'
import {
  DndContext, closestCenter, PointerSensor, TouchSensor, useSensor, useSensors,
} from '@dnd-kit/core'
import {
  arrayMove, SortableContext, verticalListSortingStrategy, useSortable,
} from '@dnd-kit/sortable'
import { CSS } from '@dnd-kit/utilities'
import { X, GripVertical, Plus, Check, Search, RotateCcw } from 'lucide-react'
import widgetRegistry, { colorOptions, categories, DEFAULT_CONFIG, getWidgetById } from './widgetRegistry'
import { loadWidgetConfig, saveWidgetConfig, resetWidgetConfig } from '../../services/widgetConfigService'

/* ── Aktif widget satiri (suruklenebilir) ─────────── */
function ActiveWidgetRow(props) {
  var widget = props.widget         // registry objesi
  var color = props.color           // secili renk id'si
  var onColorChange = props.onColorChange
  var onRemove = props.onRemove

  // KRITIK: Icon destructure (kucuk harf JSX hatasini onler)
  var Icon = widget.icon
  var palette = colorOptions.find(function(c) { return c.id === color }) || colorOptions[0]

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
      className={'flex items-center gap-3 px-3 py-2.5 rounded-xl transition-colors ' +
        (sortable.isDragging
          ? 'bg-slate-200 dark:bg-white/10 shadow-lg'
          : 'bg-slate-100 dark:bg-white/[0.03] hover:bg-slate-200/60 dark:hover:bg-white/[0.06]')
      }
    >
      {/* Drag handle */}
      <button
        {...sortable.listeners}
        className="cursor-grab active:cursor-grabbing p-0.5 text-slate-400 dark:text-white/40 hover:text-slate-600 dark:hover:text-white/40 transition-colors"
        title="Suruklemek icin tutun"
      >
        <GripVertical size={14} />
      </button>

      {/* Icon + Label */}
      <div className="flex items-center gap-2.5 flex-1 min-w-0">
        <div
          className="w-7 h-7 rounded-lg flex items-center justify-center flex-shrink-0"
          style={{ background: palette.hex + '20', border: '1px solid ' + palette.hex + '40' }}
        >
          <Icon size={14} style={{ color: palette.hex }} strokeWidth={1.8} />
        </div>
        <span className="text-sm text-slate-700 dark:text-white/70 font-medium truncate">{widget.label}</span>
      </div>

      {/* Color dots */}
      <div className="flex items-center gap-1">
        {colorOptions.slice(0, 5).map(function(c) {
          return (
            <button
              key={c.id}
              onClick={function() { onColorChange(widget.id, c.id) }}
              className={'w-4 h-4 rounded-full border transition-all ' +
                (color === c.id ? 'scale-125 border-slate-400 dark:border-white/40' : 'border-transparent opacity-50 hover:opacity-80')
              }
              style={{ background: c.hex }}
              title={c.label}
            />
          )
        })}
      </div>

      {/* Remove */}
      <button
        onClick={function() { onRemove(widget.id) }}
        className="p-1 rounded-lg text-slate-400 dark:text-white/40 hover:text-red-600 dark:hover:text-red-400 hover:bg-red-100 dark:hover:bg-red-400/10 transition-colors"
        title="Kaldir"
      >
        <X size={14} />
      </button>
    </motion.div>
  )
}

/* ── Havuzdaki widget satiri ──────────────────────── */
function PoolWidgetRow(props) {
  var widget = props.widget         // registry objesi
  var onAdd = props.onAdd

  // KRITIK: Icon destructure
  var Icon = widget.icon
  var palette = colorOptions.find(function(c) { return c.id === widget.defaultColor }) || colorOptions[0]

  return (
    <motion.button
      whileHover={{ scale: 1.01 }}
      whileTap={{ scale: 0.98 }}
      onClick={function() { onAdd(widget.id) }}
      className="w-full flex items-center gap-3 px-3 py-2 rounded-xl bg-slate-50 dark:bg-white/[0.02] hover:bg-slate-100 dark:hover:bg-white/[0.05] border border-transparent hover:border-slate-200 dark:hover:border-white/[0.06] transition-all group"
    >
      <div
        className="w-7 h-7 rounded-lg flex items-center justify-center flex-shrink-0"
        style={{ background: palette.hex + '15', border: '1px solid ' + palette.hex + '30' }}
      >
        <Icon size={14} style={{ color: palette.hex, opacity: 0.7 }} strokeWidth={1.8} />
      </div>
      <span className="text-sm text-slate-500 dark:text-white/40 group-hover:text-slate-700 dark:group-hover:text-white/60 font-medium flex-1 text-left transition-colors">
        {widget.label}
      </span>
      <Plus size={14} className="text-slate-400 dark:text-white/40 group-hover:text-emerald-600 dark:group-hover:text-emerald-400/60 transition-colors" />
    </motion.button>
  )
}

/* ── Ana Panel ────────────────────────────────────── */
export default function WidgetConfigPanel(props) {
  var isOpen = props.isOpen
  var onClose = props.onClose
  var gridKey = props.gridKey
  var onSaved = props.onSaved

  // Local state (panel acildiginda servisten yuklenir)
  var [visibleIds, setVisibleIds] = useState([])
  var [order, setOrder] = useState([])
  var [colors, setColors] = useState({})
  var [search, setSearch] = useState('')
  var [saving, setSaving] = useState(false)

  // Panel acildiginda config yukle
  useEffect(function() {
    if (!isOpen) return
    var saved = loadWidgetConfig(gridKey)
    if (saved) {
      setVisibleIds(saved.visibleIds || [])
      setOrder(saved.order || saved.visibleIds || [])
      setColors(saved.colors || {})
    } else {
      setVisibleIds(DEFAULT_CONFIG.visibleIds.slice())
      setOrder(DEFAULT_CONFIG.order.slice())
      setColors(Object.assign({}, DEFAULT_CONFIG.colors))
    }
    setSearch('')
  }, [isOpen, gridKey])

  var sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 3 } }),
    useSensor(TouchSensor, { activationConstraint: { delay: 100, tolerance: 5 } })
  )

  // Aktif widget'lar — order'a gore registry objeleri
  var activeWidgets = useMemo(function() {
    return order
      .map(function(id) { return getWidgetById(id) })
      .filter(function(w) { return w && visibleIds.indexOf(w.id) !== -1 })
  }, [order, visibleIds])

  // Havuzdaki widget'lar — registry'de olup aktif olmayanlar
  var availableWidgets = useMemo(function() {
    var filtered = widgetRegistry.filter(function(w) {
      return visibleIds.indexOf(w.id) === -1
    })
    if (search.trim()) {
      var q = search.toLowerCase()
      filtered = filtered.filter(function(w) {
        return w.label.toLowerCase().indexOf(q) !== -1 || w.category.indexOf(q) !== -1
      })
    }
    return filtered
  }, [visibleIds, search])

  // Kategoriye gore gruplandir
  var groupedAvailable = useMemo(function() {
    var groups = {}
    availableWidgets.forEach(function(w) {
      if (!groups[w.category]) groups[w.category] = []
      groups[w.category].push(w)
    })
    return groups
  }, [availableWidgets])

  /* ── Handlers ────────────────── */
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
    var newOrder = order.indexOf(widgetId) === -1 ? order.concat([widgetId]) : order
    setVisibleIds(newVisible)
    setOrder(newOrder)
  }

  function handleRemove(widgetId) {
    setVisibleIds(visibleIds.filter(function(id) { return id !== widgetId }))
    setOrder(order.filter(function(id) { return id !== widgetId }))
  }

  function handleColorChange(widgetId, newColor) {
    var next = Object.assign({}, colors)
    next[widgetId] = newColor
    setColors(next)
  }

  async function handleSave() {
    setSaving(true)
    try {
      await saveWidgetConfig(gridKey, {
        visibleIds: visibleIds,
        order: order,
        colors: colors,
      })
      if (onSaved) onSaved()
      if (onClose) onClose()
    } catch (e) {
      alert('Ayarlar kaydedilirken hata olustu: ' + e.message)
    } finally {
      setSaving(false)
    }
  }

  async function handleReset() {
    if (!window.confirm('Widget ayarlari varsayilana sifirlanacak. Devam edilsin mi?')) return
    try {
      await resetWidgetConfig(gridKey)
      setVisibleIds(DEFAULT_CONFIG.visibleIds.slice())
      setOrder(DEFAULT_CONFIG.order.slice())
      setColors(Object.assign({}, DEFAULT_CONFIG.colors))
      if (onSaved) onSaved()
    } catch (e) {
      alert('Sifirlanirken hata olustu: ' + e.message)
    }
  }

  return (
    <AnimatePresence>
      {isOpen && (
        <>
          {/* Backdrop */}
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            className="fixed inset-0 z-[9998] bg-black/60 backdrop-blur-sm"
            onClick={onClose}
          />

          {/* Panel */}
          <motion.div
            initial={{ opacity: 0, x: 80 }}
            animate={{ opacity: 1, x: 0 }}
            exit={{ opacity: 0, x: 60 }}
            transition={{ type: 'spring', stiffness: 320, damping: 30 }}
            className="fixed right-0 top-0 bottom-0 z-[9999] w-full max-w-md"
          >
            <div className="h-full flex flex-col border-l border-slate-200 dark:border-white/10 shadow-[-8px_0_40px_rgba(15,23,42,0.1)] dark:shadow-[-8px_0_40px_rgba(0,0,0,0.3)] bg-white/92 dark:bg-[rgba(8,11,20,0.92)] backdrop-blur-[32px]">
              {/* Header */}
              <div className="flex items-center justify-between px-5 py-4 border-b border-slate-200 dark:border-white/[0.06] flex-shrink-0">
                <div>
                  <h3 className="text-base font-bold text-slate-800 dark:text-white/90">Widget Ayarlari</h3>
                  <p className="text-[11px] text-slate-500 dark:text-white/30 mt-0.5">Goruntulenecek alanlari sec, sirala ve renklendir</p>
                </div>
                <button onClick={onClose} className="p-2 rounded-xl hover:bg-slate-100 dark:hover:bg-white/5 transition-colors">
                  <X size={18} className="text-slate-500 dark:text-white/40" />
                </button>
              </div>

              {/* Content */}
              <div className="flex-1 overflow-y-auto min-h-0">

                {/* Aktif widget'lar */}
                <div className="px-5 pt-4 pb-2">
                  <div className="flex items-center gap-2 mb-3">
                    <div className="w-1.5 h-1.5 rounded-full bg-emerald-500 dark:bg-emerald-400" />
                    <span className="text-[11px] font-bold text-slate-500 dark:text-white/40 uppercase tracking-wider">
                      Aktif Widget'lar ({activeWidgets.length})
                    </span>
                  </div>

                  {activeWidgets.length === 0 ? (
                    <div className="text-center py-8 text-slate-400 dark:text-white/40 text-sm">
                      Henuz widget eklenmedi
                    </div>
                  ) : (
                    <DndContext sensors={sensors} collisionDetection={closestCenter} onDragEnd={handleDragEnd}>
                      <SortableContext
                        items={activeWidgets.map(function(w) { return w.id })}
                        strategy={verticalListSortingStrategy}
                      >
                        <div className="space-y-1.5">
                          {activeWidgets.map(function(w) {
                            return (
                              <ActiveWidgetRow
                                key={w.id}
                                widget={w}
                                color={colors[w.id] || w.defaultColor}
                                onColorChange={handleColorChange}
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
                <div className="mx-5 my-4 h-px bg-slate-200 dark:bg-white/[0.06]" />

                {/* Havuz */}
                <div className="px-5 pb-4">
                  <div className="flex items-center gap-2 mb-3">
                    <div className="w-1.5 h-1.5 rounded-full bg-blue-500 dark:bg-blue-400" />
                    <span className="text-[11px] font-bold text-slate-500 dark:text-white/40 uppercase tracking-wider">
                      Widget Havuzu
                    </span>
                  </div>

                  {/* Search */}
                  <div className="relative mb-3">
                    <Search size={14} className="absolute left-3 top-1/2 -translate-y-1/2 text-slate-400 dark:text-white/40" />
                    <input
                      type="text"
                      value={search}
                      onChange={function(e) { setSearch(e.target.value) }}
                      placeholder="Widget ara..."
                      className="w-full pl-9 pr-3 py-2 rounded-xl bg-slate-50 dark:bg-white/[0.04] border border-slate-200 dark:border-white/[0.06] text-sm text-slate-700 dark:text-white/70 placeholder:text-slate-400 dark:placeholder:text-white/20 focus:outline-none focus:border-indigo-400 dark:focus:border-white/15 transition-colors"
                    />
                  </div>

                  {Object.keys(groupedAvailable).length === 0 ? (
                    <div className="text-center py-6 text-slate-400 dark:text-white/40 text-xs">
                      {search ? 'Sonuc bulunamadi' : 'Tum widget\'lar eklenmis'}
                    </div>
                  ) : (
                    categories.map(function(cat) {
                      var items = groupedAvailable[cat.id]
                      if (!items || items.length === 0) return null
                      return (
                        <div key={cat.id} className="mb-4">
                          <p className="text-[10px] font-semibold text-slate-400 dark:text-white/45 uppercase tracking-wider mb-2 pl-1">
                            {cat.label}
                          </p>
                          <div className="space-y-1">
                            {items.map(function(w) {
                              return <PoolWidgetRow key={w.id} widget={w} onAdd={handleAdd} />
                            })}
                          </div>
                        </div>
                      )
                    })
                  )}
                </div>
              </div>

              {/* Footer */}
              <div className="px-5 py-4 border-t border-slate-200 dark:border-white/[0.06] flex items-center gap-2 flex-shrink-0">
                <button
                  onClick={handleReset}
                  className="px-3 py-2.5 rounded-xl bg-slate-50 dark:bg-white/[0.02] hover:bg-slate-100 dark:hover:bg-white/[0.06] border border-slate-200 dark:border-white/[0.06] text-xs font-medium text-slate-500 dark:text-white/40 hover:text-slate-700 dark:hover:text-white/60 transition-all flex items-center gap-1.5"
                  title="Varsayilana sifirla"
                >
                  <RotateCcw size={13} />
                  Sifirla
                </button>
                <div className="flex-1" />
                <button
                  onClick={onClose}
                  className="px-4 py-2.5 rounded-xl bg-slate-100 dark:bg-white/[0.04] hover:bg-slate-200 dark:hover:bg-white/[0.08] border border-slate-200 dark:border-white/[0.06] text-sm font-medium text-slate-600 dark:text-white/50 hover:text-slate-800 dark:hover:text-white/70 transition-all"
                >
                  Iptal
                </button>
                <button
                  onClick={handleSave}
                  disabled={saving}
                  className="px-4 py-2.5 rounded-xl bg-indigo-500 hover:bg-indigo-600 dark:bg-indigo-500/20 dark:hover:bg-indigo-500/30 border border-indigo-500 dark:border-indigo-400/20 dark:hover:border-indigo-400/30 text-sm font-semibold text-white dark:text-indigo-300 dark:hover:text-indigo-200 transition-all flex items-center gap-2 disabled:opacity-50 shadow-sm"
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
