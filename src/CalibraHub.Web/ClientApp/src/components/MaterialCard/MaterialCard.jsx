import { useState, useCallback } from 'react'
import {
  DndContext, closestCenter, PointerSensor, TouchSensor, KeyboardSensor, useSensor, useSensors,
} from '@dnd-kit/core'
import { arrayMove, SortableContext, horizontalListSortingStrategy } from '@dnd-kit/sortable'
import { motion } from 'framer-motion'
import { CircleDot, Pencil, Trash2, Settings2 } from 'lucide-react'
import DraggableWidget from './DraggableWidget'

var statusConfig = {
  active:   { bg: 'rgba(16,185,129,0.15)', border: 'rgba(16,185,129,0.3)', text: '#6ee7b7', label: 'Aktif' },
  passive:  { bg: 'rgba(245,158,11,0.15)', border: 'rgba(245,158,11,0.3)', text: '#fcd34d', label: 'Pasif' },
  critical: { bg: 'rgba(239,68,68,0.15)',  border: 'rgba(239,68,68,0.3)',  text: '#fca5a5', label: 'Kritik' },
}

export default function MaterialCard(props) {
  var materialId   = props.materialId || ''
  var materialCode = props.materialCode || ''
  var materialName = props.materialName || ''
  var description  = props.description || ''
  var status       = props.status || 'active'
  var imageUrl     = props.imageUrl || null
  var initialWidgets = props.widgets || []
  var onEdit       = props.onEdit
  var onDelete     = props.onDelete
  var onToggleStatus = props.onToggleStatus
  var onOpenConfig = props.onOpenConfig

  var [widgets, setWidgets] = useState(initialWidgets)
  var [hovered, setHovered] = useState(false)
  var statusStyle = statusConfig[status] || statusConfig.active

  var sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 5 } }),
    useSensor(TouchSensor, { activationConstraint: { delay: 150, tolerance: 5 } }),
    useSensor(KeyboardSensor)
  )

  var handleDragEnd = useCallback(function(event) {
    var active = event.active
    var over = event.over
    if (!over || active.id === over.id) return
    setWidgets(function(prev) {
      var oldIndex = prev.findIndex(function(w) { return w.id === active.id })
      var newIndex = prev.findIndex(function(w) { return w.id === over.id })
      return arrayMove(prev, oldIndex, newIndex)
    })
  }, [])

  return (
    <motion.div
      initial={{ opacity: 0, y: 8 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.3, ease: [0.23, 1, 0.32, 1] }}
      onMouseEnter={function() { setHovered(true) }}
      onMouseLeave={function() { setHovered(false) }}
      className="w-full"
    >
      <div className={'glass rounded-2xl overflow-hidden transition-all duration-300 ' +
        (hovered ? 'shadow-[0_8px_40px_rgba(0,0,0,0.22)]' : 'shadow-[0_2px_12px_rgba(0,0,0,0.1)]')
      }>
        <div className="flex items-center gap-0">

          {/* Sol: Kimlik */}
          <div
            className="flex items-center gap-3.5 px-5 py-3.5 flex-shrink-0 min-w-[260px] max-w-[320px] cursor-pointer group"
            onClick={function() { if (onEdit) onEdit(materialId) }}
          >
            {imageUrl ? (
              <img src={imageUrl} alt={materialName}
                className="w-11 h-11 rounded-xl object-cover border border-slate-200 dark:border-white/10 flex-shrink-0" />
            ) : (
              <div className="w-11 h-11 rounded-xl bg-slate-100 dark:bg-white/5 border border-slate-200 dark:border-white/8 flex items-center justify-center flex-shrink-0">
                <CircleDot size={18} className="text-slate-400 dark:text-white/40" strokeWidth={1.5} />
              </div>
            )}

            <div className="flex-1 min-w-0">
              <div className="flex items-center gap-2 mb-0.5">
                <span className="text-[10px] font-mono font-semibold tracking-wider text-slate-500 dark:text-white/35 uppercase">
                  {materialCode}
                </span>
                {/* Durum badge — tiklaninca toggle */}
                <span
                  className="text-[9px] font-bold px-1.5 py-px rounded-full uppercase tracking-wider cursor-pointer hover:brightness-110 dark:hover:brightness-125 transition-all"
                  style={{
                    background: statusStyle.bg,
                    border: '1px solid ' + statusStyle.border,
                    color: statusStyle.text,
                  }}
                  title="Durumu degistirmek icin tikla"
                  onClick={function(e) {
                    e.stopPropagation()
                    if (onToggleStatus) onToggleStatus(materialId)
                  }}
                >
                  {statusStyle.label}
                </span>
              </div>
              <h3 className="text-sm font-bold text-slate-800 dark:text-white/85 tracking-tight leading-tight truncate group-hover:text-slate-900 dark:group-hover:text-white transition-colors">
                {materialName}
              </h3>
              {description && (
                <p className="text-[11px] text-slate-500 dark:text-white/45 truncate mt-0.5 leading-tight">{description}</p>
              )}
            </div>
          </div>

          {/* Ayirici */}
          <div className="w-px h-10 bg-slate-200 dark:bg-white/[0.06] flex-shrink-0" />

          {/* Orta: Widget'lar */}
          <div className="flex-1 min-w-0 px-3 py-2.5 overflow-x-auto">
            {widgets.length > 0 ? (
              <DndContext sensors={sensors} collisionDetection={closestCenter} onDragEnd={handleDragEnd}>
                <SortableContext items={widgets.map(function(w) { return w.id })} strategy={horizontalListSortingStrategy}>
                  <div className="flex items-center gap-1.5">
                    {widgets.map(function(widget) {
                      return <DraggableWidget key={widget.id} widget={widget} />
                    })}
                  </div>
                </SortableContext>
              </DndContext>
            ) : (
              <span className="text-[11px] text-slate-400 dark:text-white/40">Widget yok</span>
            )}
          </div>

          {/* Ayirici */}
          <div className="w-px h-10 bg-slate-200 dark:bg-white/[0.06] flex-shrink-0" />

          {/* Sag: Aksiyonlar */}
          <div className="flex items-center gap-1 px-3 flex-shrink-0">
            {onOpenConfig && (
              <button
                onClick={function(e) { e.stopPropagation(); onOpenConfig() }}
                className="p-2 rounded-xl hover:bg-slate-100 dark:hover:bg-white/5 transition-colors group"
                title="Widget Ayarlari"
              >
                <Settings2 size={14} className="text-slate-400 dark:text-white/40 group-hover:text-indigo-600 dark:group-hover:text-indigo-400/70 transition-colors" />
              </button>
            )}
            {onEdit && (
              <button
                onClick={function(e) { e.stopPropagation(); onEdit(materialId) }}
                className="p-2 rounded-xl hover:bg-slate-100 dark:hover:bg-white/5 transition-colors group"
                title="Duzenle"
              >
                <Pencil size={14} className="text-slate-400 dark:text-white/40 group-hover:text-amber-600 dark:group-hover:text-amber-400/70 transition-colors" />
              </button>
            )}
            {onDelete && (
              <button
                onClick={function(e) {
                  e.stopPropagation()
                  if (window.confirm('"' + materialName + '" silmek istediginizden emin misiniz?')) {
                    onDelete(materialId)
                  }
                }}
                className="p-2 rounded-xl hover:bg-red-100 dark:hover:bg-red-500/10 transition-colors group"
                title="Sil"
              >
                <Trash2 size={14} className="text-slate-400 dark:text-white/40 group-hover:text-red-600 dark:group-hover:text-red-400/70 transition-colors" />
              </button>
            )}
          </div>

        </div>
      </div>
    </motion.div>
  )
}
