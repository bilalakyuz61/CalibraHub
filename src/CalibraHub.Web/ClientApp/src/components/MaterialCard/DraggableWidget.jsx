import { useSortable } from '@dnd-kit/sortable'
import { CSS } from '@dnd-kit/utilities'
import { motion } from 'framer-motion'
import WidgetTooltip from './WidgetTooltip'

const colorMap = {
  emerald: { bg: 'rgba(16,185,129,0.12)', border: 'rgba(16,185,129,0.25)', text: '#6ee7b7', icon: '#34d399' },
  amber:   { bg: 'rgba(245,158,11,0.12)', border: 'rgba(245,158,11,0.25)', text: '#fcd34d', icon: '#fbbf24' },
  blue:    { bg: 'rgba(59,130,246,0.12)', border: 'rgba(59,130,246,0.25)', text: '#93c5fd', icon: '#60a5fa' },
  violet:  { bg: 'rgba(139,92,246,0.12)', border: 'rgba(139,92,246,0.25)', text: '#c4b5fd', icon: '#a78bfa' },
  cyan:    { bg: 'rgba(6,182,212,0.12)',  border: 'rgba(6,182,212,0.25)',  text: '#67e8f9', icon: '#22d3ee' },
  rose:    { bg: 'rgba(244,63,94,0.12)',  border: 'rgba(244,63,94,0.25)',  text: '#fda4af', icon: '#fb7185' },
  slate:   { bg: 'rgba(100,116,139,0.12)',border: 'rgba(100,116,139,0.25)',text: '#cbd5e1', icon: '#94a3b8' },
  indigo:  { bg: 'rgba(99,102,241,0.12)', border: 'rgba(99,102,241,0.25)', text: '#a5b4fc', icon: '#818cf8' },
  teal:    { bg: 'rgba(20,184,166,0.12)', border: 'rgba(20,184,166,0.25)', text: '#5eead4', icon: '#2dd4bf' },
  orange:  { bg: 'rgba(249,115,22,0.12)', border: 'rgba(249,115,22,0.25)', text: '#fdba74', icon: '#fb923c' },
}

export default function DraggableWidget({ widget }) {
  var id = widget.id
  var Icon = widget.icon
  var label = widget.label
  var value = widget.value
  var detail = widget.detail || ''
  var color = widget.color || 'blue'
  var palette = colorMap[color] || colorMap.blue

  var sortable = useSortable({ id: id })
  var style = {
    transform: CSS.Transform.toString(sortable.transform),
    transition: sortable.transition,
    zIndex: sortable.isDragging ? 50 : 1,
  }

  if (!Icon) return null

  return (
    <div ref={sortable.setNodeRef} style={style} {...sortable.attributes} {...sortable.listeners}>
      <WidgetTooltip label={label} value={value} detail={detail}>
        <motion.div
          layout
          animate={sortable.isDragging
            ? { scale: 1.06, rotate: 1, boxShadow: '0 12px 40px rgba(0,0,0,0.35)' }
            : { scale: 1, rotate: 0, boxShadow: '0 2px 8px rgba(0,0,0,0.08)' }
          }
          transition={{ type: 'spring', stiffness: 350, damping: 26, mass: 0.7 }}
          className="flex items-center gap-2 px-2.5 py-1.5 rounded-xl cursor-grab active:cursor-grabbing select-none whitespace-nowrap"
          style={{ background: palette.bg, border: '1px solid ' + palette.border }}
        >
          <div
            className="w-6 h-6 rounded-lg flex items-center justify-center flex-shrink-0"
            style={{ background: palette.bg, border: '1px solid ' + palette.border }}
          >
            <Icon size={13} style={{ color: palette.icon }} strokeWidth={1.8} />
          </div>
          <div className="flex flex-col min-w-0 leading-none">
            <span className="text-[9px] font-semibold uppercase tracking-wider text-white/30">{label}</span>
            <span className="text-xs font-bold leading-tight tracking-tight" style={{ color: palette.text }}>{value}</span>
          </div>
        </motion.div>
      </WidgetTooltip>
    </div>
  )
}
